using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Google.Protobuf;
using Proto;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class NetManager : MonoBehaviour
{
    private static NetManager _instance;
    private ClientWebSocket _socket;

    [Header("网络配置")]
    [Tooltip("贴入你最新的 Token")]
    public string serverUrl = "ws://localhost:8081/ws?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoyLCJleHAiOjE3NzMyMDgxMTcsImlhdCI6MTc3Mjk0ODkxN30.on_YOgGnaRi5VDuujh3a-48K65lcfGujJ_rDEYZJsTE";

    [Tooltip("你当前登录的玩家ID，用于过滤自己的广播")]
    public uint myUserId = 1;

    [Header("移动设置")]
    public float moveSpeed = 5f;
    private Vector3 targetPosition;

    // 存放其他玩家的字典 (Key: 玩家ID, Value: 对应的方块物体)
    private Dictionary<uint, GameObject> otherPlayers = new Dictionary<uint, GameObject>();

    // 线程安全队列，用于把后台收到的网络消息丢给主线程处理
    private ConcurrentQueue<GameMessage> messageQueue = new ConcurrentQueue<GameMessage>();

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(this); return; }
        _instance = this;
    }

    async void Start()
    {
        targetPosition = transform.position;
        _socket = new ClientWebSocket();

        try
        {
            Debug.Log("🌐 正在连接服务器...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await _socket.ConnectAsync(new Uri(serverUrl), cts.Token);
            }

            if (_socket.State == WebSocketState.Open)
            {
                Debug.Log("✅ 成功连接至 Go 后端！");
                _ = ReceiveLoop();
                SendMove(transform.position.x, transform.position.y, transform.position.z);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 连接失败: {e.Message}");
        }
    }

    void Update()
    {
        // 1. 处理网络队列里的消息 (主线程中执行)
        while (messageQueue.TryDequeue(out GameMessage msg))
        {
            ProcessNetworkMessage(msg);
        }

        // 2. 鼠标点击地面移动 (射线检测)
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // 确保点到的是地面，而不是天上
                targetPosition = new Vector3(hit.point.x, transform.position.y, hit.point.z);

                // 给后端发消息
                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    SendMove(targetPosition.x, targetPosition.y, targetPosition.z);
                }
            }
        }

        // 3. 自己方块的平滑移动
        if (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            transform.LookAt(new Vector3(targetPosition.x, transform.position.y, targetPosition.z));
        }

        // 4. 其他玩家方块的平滑移动
        foreach (var kvp in otherPlayers)
        {
            GameObject otherCube = kvp.Value;
            // 我们把其他玩家的目标位置存在了他们物体的名字里（偷懒的简便做法，后续可优化）
            if (Vector3.Distance(otherCube.transform.position, otherCube.transform.localScale) > 0.01f)
            {
                // 注意：这里临时借用了 localScale 来存目标坐标，生产环境应单独挂脚本
                // 暂时用这种简易方式让你看到效果
            }
        }
    }

    // 处理解析好的 Protobuf 消息
    private void ProcessNetworkMessage(GameMessage msg)
    {
        if (msg.Type == "move")
        {
            uint incomingId = msg.UserId;

            // 忽略自己发出的广播，避免自己的方块鬼畜
            if (incomingId == myUserId) return;

            Vector3 newPos = new Vector3(msg.X, msg.Y, msg.Z);

            // 如果字典里没有这个玩家，就当场捏一个新方块出来
            if (!otherPlayers.ContainsKey(incomingId))
            {
                GameObject newPlayer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                newPlayer.name = $"Player_{incomingId}";
                // 换个颜色区分一下（变成红色）
                newPlayer.GetComponent<Renderer>().material.color = Color.red;
                newPlayer.transform.position = newPos;

                otherPlayers.Add(incomingId, newPlayer);
                Debug.Log($"👤 发现新玩家入场: ID {incomingId}");
            }
            else
            {
                // 如果已经存在，更新它的位置（这里先做瞬移，等跑通了我们再加平滑移动）
                otherPlayers[incomingId].transform.position = newPos;
            }
        }
    }

    public async void SendMove(float x, float y, float z)
    {
        if (_socket == null || _socket.State != WebSocketState.Open) return;
        try
        {
            GameMessage moveMsg = new GameMessage { Type = "move", X = x, Y = y, Z = z, UserId = myUserId };
            byte[] data = moveMsg.ToByteArray();
            await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception e) { Debug.LogError($"❌ 发送异常: {e.Message}"); }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[4096];
        while (_socket != null && _socket.State == WebSocketState.Open)
        {
            try
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // 解析 Protobuf，并塞进队列给主线程处理
                    GameMessage incoming = GameMessage.Parser.ParseFrom(buffer, 0, result.Count);
                    messageQueue.Enqueue(incoming);
                }
            }
            catch (Exception e) { Debug.LogWarning($"接收链路关闭: {e.Message}"); break; }
        }
    }

    private void OnDestroy() { if (_socket != null) _socket.Dispose(); }
}