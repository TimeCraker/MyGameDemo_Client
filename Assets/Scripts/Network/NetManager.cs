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
    public string serverUrl = "ws://localhost:8081/ws?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoyLCJleHAiOjE3NzMzOTg3MDgsImlhdCI6MTc3MzEzOTUwOH0.cymhm_pUa76scNDr7o7D00sOJKGSWvUA35demPMITvQ";

    [Tooltip("你当前登录的玩家ID，用于过滤自己的广播")]
    public uint myUserId = 1;

    [Header("移动设置")]
    public float moveSpeed = 5f;
    private Vector3 targetPosition;

    // 存放其他玩家的字典 (Key: 玩家ID, Value: 对应的方块物体)
    private Dictionary<uint, GameObject> otherPlayers = new Dictionary<uint, GameObject>();

    // 【新增】存放其他玩家的目标坐标 (用于平滑移动)
    private Dictionary<uint, Vector3> otherPlayerTargets = new Dictionary<uint, Vector3>();

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
            // 【修改】根据反馈，删除了看向移动方向的 LookAt 代码
        }

        // 4. 其他玩家方块的平滑移动
        foreach (var kvp in otherPlayers)
        {
            uint id = kvp.Key;
            GameObject otherCube = kvp.Value;

            // 【修改】从目标位置字典获取坐标，实现平滑滑行
            if (otherPlayerTargets.TryGetValue(id, out Vector3 targetPos))
            {
                if (Vector3.Distance(otherCube.transform.position, targetPos) > 0.01f)
                {
                    otherCube.transform.position = Vector3.MoveTowards(otherCube.transform.position, targetPos, moveSpeed * Time.deltaTime);
                    // 【修改】根据反馈，删除了看向移动方向的 LookAt 代码
                }
            }
        }
    }

    // 处理解析好的 Protobuf 消息
    private void ProcessNetworkMessage(GameMessage msg)
    {
        // 处理玩家移动消息
        if (msg.Type == "move")
        {
            HandleSingleMove(msg);
        }
        // 【新增功能】处理初次进入游戏时的全员位置初始化
        else if (msg.Type == "init_players")
        {
            HandleInitPlayers(msg);
        }
        // 处理玩家下线逻辑
        else if (msg.Type == "logout")
        {
            uint logoutId = msg.UserId;
            if (otherPlayers.ContainsKey(logoutId))
            {
                // 1. 销毁场景里的物体
                Destroy(otherPlayers[logoutId]);
                // 2. 从字典里移除记录
                otherPlayers.Remove(logoutId);
                otherPlayerTargets.Remove(logoutId);

                Debug.Log($"👋 玩家 {logoutId} 已离开游戏，已清理对应模型。");
            }
        }
    }

    // 【抽离】处理单个玩家移动的消息逻辑
    private void HandleSingleMove(GameMessage msg)
    {
        uint incomingId = msg.UserId;
        if (incomingId == myUserId) return;

        Vector3 newPos = new Vector3(msg.X, msg.Y, msg.Z);

        if (!otherPlayers.ContainsKey(incomingId))
        {
            CreateNewPlayer(incomingId, newPos);
        }
        else
        {
            otherPlayerTargets[incomingId] = newPos;
        }
    }

    // 【新增】处理批量玩家初始化的逻辑
    private void HandleInitPlayers(GameMessage msg)
    {
        Debug.Log($"同步中：收到服务器发来的 {msg.Players.Count} 个在线玩家位置");
        foreach (var p in msg.Players)
        {
            // 排除掉自己，因为自己已经存在于场景中了
            if (p.UserId == myUserId) continue;

            Vector3 pos = new Vector3(p.X, p.Y, p.Z);
            if (!otherPlayers.ContainsKey(p.UserId))
            {
                CreateNewPlayer(p.UserId, pos);
            }
            else
            {
                otherPlayerTargets[p.UserId] = pos;
            }
        }
    }

    // 【抽离】统一的创建新玩家物体逻辑
    private void CreateNewPlayer(uint id, Vector3 pos)
    {
        GameObject newPlayer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        newPlayer.name = $"Player_{id}";

        // 【修复】指定 UnityEngine.Random 以消除 CS0104 歧义，并基于 ID 生成唯一颜色
        Renderer renderer = newPlayer.GetComponent<Renderer>();
        UnityEngine.Random.InitState((int)id);
        renderer.material.color = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);

        newPlayer.transform.position = pos;
        otherPlayers.Add(id, newPlayer);
        otherPlayerTargets.Add(id, pos);
        Debug.Log($"👤 成功加载玩家模型: ID {id}");
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