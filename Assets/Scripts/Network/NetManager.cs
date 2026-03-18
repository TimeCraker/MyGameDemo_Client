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

    // ===== 修改代码 START =====
    // [引入动态预制体加载机制，取代原有的强引用，彻底实现 UI 与场景解耦]
    [Header("场景控制")]
    [Tooltip("请将包含了 Ground 和其他静态环境的 3D 环境打包成 Prefab 拖到这里，并从当前场景中删除它")]
    public GameObject gameWorldPrefab;

    // 运行时的场景实例缓存
    private GameObject _currentEnvironment;
    // ===== 修改代码 END =====

    [Header("移动设置")]
    public float moveSpeed = 5f;
    private Vector3 targetPosition;

    private Dictionary<uint, GameObject> otherPlayers = new Dictionary<uint, GameObject>();
    private Dictionary<uint, Vector3> otherPlayerTargets = new Dictionary<uint, Vector3>();
    private ConcurrentQueue<GameMessage> messageQueue = new ConcurrentQueue<GameMessage>();

    // ===== 新增代码 START =====
    // 修改内容：新增匹配成功事件，采用事件驱动方式解耦网络层与 UI/场景层
    // 修改原因：NetManager 只负责收发数据，不应直接操作 UI 或切场景（避免越权与耦合）
    public event Action<string> OnMatchSuccessEvent;

    // 防止重复处理 match_success（例如断线重连或重复推送）
    private bool _matchSuccessHandled = false;
    // ===== 新增代码 END =====

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void Start()
    {
        targetPosition = transform.position;
        Debug.Log("💤 NetManager 已就绪，等待玩家登录...");

        // ===== 修改代码 START =====
        // [移除了隐藏 gameEnvironment 的逻辑，因为初始登录场景中根本就不该有 3D 场景]
        // ===== 修改代码 END =====
    }

    // ===== 新增代码 START =====
    // 修改内容：新增 ConnectToArena() 入口
    // 修改原因：React Web 端已匹配成功并注入 roomId；Unity 仅负责战斗容器，需要进入“对战服连接准备”流程（下一阶段实现）
    public async void ConnectToArena()
    {
        if (!DataManager.IsLoggedIn())
        {
            Debug.LogError("❌ 尚未登录，无法连接 WebSocket！");
            return;
        }

        myUserId = DataManager.MyUserId;

        string roomId = (GameManager.Instance != null) ? GameManager.Instance.CurrentRoomId : string.Empty;
        string finalUrl = $"{baseWsUrl}?token={DataManager.Token}&room_id={roomId}";

        _socket = new ClientWebSocket();
        try
        {
            Debug.Log($"🌐 正在连接对战服务器 (玩家ID: {myUserId}, roomId: {roomId})...");
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await _socket.ConnectAsync(new Uri(finalUrl), cts.Token);
            }

            if (_socket.State == WebSocketState.Open)
            {
                Debug.Log("✅ 成功连接至 Go 后端对战通道！");

                // ===== 修改代码 START =====
                // [连接成功后，动态实例化 3D 游戏世界预制体，正式开始游戏渲染]
                if (gameWorldPrefab != null && _currentEnvironment == null)
                {
                    _currentEnvironment = Instantiate(gameWorldPrefab);
                    Debug.Log("🌍 3D 游戏世界已动态加载完成！");
                }
                else if (gameWorldPrefab == null)
                {
                    Debug.LogWarning("⚠️ 尚未配置 gameWorldPrefab，场景将处于虚空状态！");
                }
                // ===== 修改代码 END =====

                _ = ReceiveLoop();
                SendMove(transform.position.x, transform.position.y, transform.position.z);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 连接失败: {e.Message}");
        }
    }
    // ===== 新增代码 END =====

    void Update()
    {
        // ===== 修改代码 START =====
        // [终极拦截：不仅要判断网络，还要判断场景是否已加载完毕，否则绝对不允许处理游戏内射线和交互！]
        if (_socket == null || _socket.State != WebSocketState.Open || _currentEnvironment == null) return;
        // ===== 修改代码 END =====

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
        // ===== 新增代码 START =====
        // 修改内容：监听匹配成功消息，并通过事件通知 UI 层处理切场景逻辑
        // 修改原因：保证主线程安全（本方法由 Update 主线程调用），并实现职责分离
        else if (msg.Type == "match_success")
        {
            if (_matchSuccessHandled) return;
            _matchSuccessHandled = true;

            string roomId = msg.RoomId;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CurrentRoomId = roomId;
            }

            OnMatchSuccessEvent?.Invoke(roomId);
        }
        // ===== 新增代码 END =====
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

    // ===== 新增代码 START =====
    // 修改内容：新增发送匹配请求的网络指令
    // 修改原因：阶段二大厅匹配按钮需要向后端发送 Type = "match_req" 的 Protobuf 消息
    public async void SendMatchReq()
    {
        if (_socket == null || _socket.State != WebSocketState.Open) return;
        try
        {
            GameMessage matchMsg = new GameMessage { Type = "match_req", UserId = myUserId };
            byte[] data = matchMsg.ToByteArray();
            await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception e) { Debug.LogError($"❌ 发送异常: {e.Message}"); }
    }
    // ===== 新增代码 END =====

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