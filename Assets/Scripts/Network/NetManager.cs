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
    public static NetManager Instance { get; private set; }

    private ClientWebSocket _socket;

    [Header("网络配置")]
    [Tooltip("WebSocket 基础地址")]
    public string baseWsUrl = "ws://localhost:8081/ws";

    [Tooltip("当前登录的玩家ID (将由 DataManager 动态赋值)")]
    public uint myUserId = 0;

    // ===== 新增代码 START =====
    // [用于控制 3D 游戏场景的显隐，避免在登录界面穿模显示]
    [Header("场景控制")]
    [Tooltip("把场景里的 Ground 和 PlayerObject 打包放在一个空物体下，拖到这里")]
    public GameObject gameEnvironment;
    // ===== 新增代码 END =====

    [Header("移动设置")]
    public float moveSpeed = 5f;
    private Vector3 targetPosition;

    private Dictionary<uint, GameObject> otherPlayers = new Dictionary<uint, GameObject>();
    private Dictionary<uint, Vector3> otherPlayerTargets = new Dictionary<uint, Vector3>();
    private ConcurrentQueue<GameMessage> messageQueue = new ConcurrentQueue<GameMessage>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        targetPosition = transform.position;
        Debug.Log("💤 NetManager 已就绪，等待玩家登录...");

        // ===== 新增代码 START =====
        // [启动时隐藏 3D 游戏环境，只显示带有视频背景的 UI]
        if (gameEnvironment != null)
        {
            gameEnvironment.SetActive(false);
        }
        // ===== 新增代码 END =====
    }

    public async void ConnectToServer()
    {
        if (!DataManager.IsLoggedIn())
        {
            Debug.LogError("❌ 尚未登录，无法连接 WebSocket！");
            return;
        }

        myUserId = DataManager.MyUserId;
        string finalUrl = $"{baseWsUrl}?token={DataManager.Token}";

        _socket = new ClientWebSocket();

        try
        {
            Debug.Log($"🌐 正在连接游戏大厅 (玩家ID: {myUserId})...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await _socket.ConnectAsync(new Uri(finalUrl), cts.Token);
            }

            if (_socket.State == WebSocketState.Open)
            {
                Debug.Log("✅ 成功连接至 Go 后端大厅！");

                // ===== 新增代码 START =====
                // [连接成功后，显示 3D 游戏环境，正式开始游戏]
                if (gameEnvironment != null)
                {
                    gameEnvironment.SetActive(true);
                }
                // ===== 新增代码 END =====

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
        // ===== 新增代码 START =====
        // [终极拦截：如果 WebSocket 没连上，绝对不允许处理任何游戏内的鼠标点击和移动逻辑！]
        if (_socket == null || _socket.State != WebSocketState.Open) return;
        // ===== 新增代码 END =====

        while (messageQueue.TryDequeue(out GameMessage msg))
        {
            ProcessNetworkMessage(msg);
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                targetPosition = new Vector3(hit.point.x, transform.position.y, hit.point.z);

                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    SendMove(targetPosition.x, targetPosition.y, targetPosition.z);
                }
            }
        }

        if (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
        }

        foreach (var kvp in otherPlayers)
        {
            uint id = kvp.Key;
            GameObject otherCube = kvp.Value;

            if (otherPlayerTargets.TryGetValue(id, out Vector3 targetPos))
            {
                if (Vector3.Distance(otherCube.transform.position, targetPos) > 0.01f)
                {
                    otherCube.transform.position = Vector3.MoveTowards(otherCube.transform.position, targetPos, moveSpeed * Time.deltaTime);
                }
            }
        }
    }

    private void ProcessNetworkMessage(GameMessage msg)
    {
        if (msg.Type == "move")
        {
            HandleSingleMove(msg);
        }
        else if (msg.Type == "init_players")
        {
            HandleInitPlayers(msg);
        }
        else if (msg.Type == "chat")
        {
            Debug.Log($"💬 [玩家 {msg.UserId}]: {msg.Content}");
        }
        else if (msg.Type == "logout")
        {
            uint logoutId = msg.UserId;
            if (otherPlayers.ContainsKey(logoutId))
            {
                Destroy(otherPlayers[logoutId]);
                otherPlayers.Remove(logoutId);
                otherPlayerTargets.Remove(logoutId);
                Debug.Log($"👋 玩家 {logoutId} 已离开游戏，已清理对应模型。");
            }
        }
    }

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

    private void HandleInitPlayers(GameMessage msg)
    {
        Debug.Log($"同步中：收到服务器发来的 {msg.Players.Count} 个在线玩家位置");
        foreach (var p in msg.Players)
        {
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

    private void CreateNewPlayer(uint id, Vector3 pos)
    {
        GameObject newPlayer = GameObject.CreatePrimitive(PrimitiveType.Cube);
        newPlayer.name = $"Player_{id}";

        Renderer renderer = newPlayer.GetComponent<Renderer>();
        UnityEngine.Random.InitState((int)id);
        renderer.material.color = UnityEngine.Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);

        newPlayer.transform.position = pos;
        CreateNameTag(newPlayer, $"Player {id}");

        otherPlayers.Add(id, newPlayer);
        otherPlayerTargets.Add(id, pos);
        Debug.Log($"👤 成功加载玩家模型: ID {id}");
    }

    private void CreateNameTag(GameObject parent, string name)
    {
        GameObject textObj = new GameObject("NameTag");
        textObj.transform.SetParent(parent.transform);
        textObj.transform.localPosition = new Vector3(0, 1.2f, 0);

        TextMesh tm = textObj.AddComponent<TextMesh>();
        tm.text = name;
        tm.fontSize = 20;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.characterSize = 0.1f;
        tm.color = Color.white;
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
                    GameMessage incoming = GameMessage.Parser.ParseFrom(buffer, 0, result.Count);
                    messageQueue.Enqueue(incoming);
                }
            }
            catch (Exception e) { Debug.LogWarning($"接收链路关闭: {e.Message}"); break; }
        }
    }

    private void OnDestroy() { if (_socket != null) _socket.Dispose(); }
}