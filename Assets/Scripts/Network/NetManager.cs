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
    public string serverUrl = "ws://localhost:8081/ws?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoyLCJleHAiOjE3NzMzOTA1ODUsImlhdCI6MTc3MzEzMTM4NX0.ZaHYoUoCuql_3qkbgMOBopaT0TWXW2pCX5meG_bwV-8";

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
            uint incomingId = msg.UserId;

            // 忽略自己发出的广播，避免自己的方块鬼畜
            if (incomingId == myUserId) return;

            Vector3 newPos = new Vector3(msg.X, msg.Y, msg.Z);

            // 如果字典里没有这个玩家，就当场捏一个新方块出来
            if (!otherPlayers.ContainsKey(incomingId))
            {
                GameObject newPlayer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                newPlayer.name = $"Player_{incomingId}";

                // 【修复】指定 UnityEngine.Random 以消除 CS0104 歧义，并基于 ID 生成唯一颜色
                Renderer renderer = newPlayer.GetComponent<Renderer>();
                UnityEngine.Random.InitState((int)incomingId);
                renderer.material.color = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);

                newPlayer.transform.position = newPos;

                otherPlayers.Add(incomingId, newPlayer);
                // 【新增】初始化目标位置字典
                otherPlayerTargets.Add(incomingId, newPos);

                Debug.Log($"👤 发现新玩家入场: ID {incomingId}");
            }
            else
            {
                // 【修改】不再直接瞬移，而是更新目标位置字典，让 Update 里的 MoveTowards 处理平滑移动
                otherPlayerTargets[incomingId] = newPos;
            }
        }
        // 【关键修复】将 logout 逻辑移出 move 分支，现在它可以被正常触发处理
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