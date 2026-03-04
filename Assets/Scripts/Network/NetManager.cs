using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Google.Protobuf;
using Proto;

public class NetManager : MonoBehaviour
{
    // 单例模式：防止你挂多了
    private static NetManager _instance;
    private ClientWebSocket _socket;

    [Header("网络配置")]
    [Tooltip("请确保 Token 是最新的！")]
    public string serverUrl = "ws://localhost:8081/ws?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoxLCJleHAiOjE3NzI4NzgyNTksImlhdCI6MTc3MjYxOTA1OX0.aZkO4aBBjSywWHjYk3lp4S-2V-ck2A4YhCIIygzxANc";

    [Header("移动设置")]
    public float moveSpeed = 5f;
    private Vector3 targetPosition;

    private void Awake()
    {
        // 强制检查是否有多余的脚本
        if (_instance != null && _instance != this)
        {
            Debug.LogError("⚠️ 发现重复的 NetManager！正在自动销毁多余脚本。请检查你的场景物体！");
            Destroy(this);
            return;
        }
        _instance = this;
    }

    async void Start()
    {
        targetPosition = transform.position;
        _socket = new ClientWebSocket();

        try
        {
            Debug.Log("🌐 正在建立连接...");
            // 设置 5 秒连接超时
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await _socket.ConnectAsync(new Uri(serverUrl), cts.Token);
            }

            if (_socket.State == WebSocketState.Open)
            {
                Debug.Log("✅ 状态：已成功连接至 Go 后端！");
                _ = ReceiveLoop();

                // 刚连上时同步一次位置
                SendMove(transform.position.x, transform.position.y, transform.position.z);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 连接阶段崩溃: {e.Message}");
        }
    }

    void Update()
    {
        // 点击鼠标左键，移动并同步
        if (Input.GetMouseButtonDown(0))
        {
            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                float randomX = UnityEngine.Random.Range(-8f, 8f);
                float randomZ = UnityEngine.Random.Range(-8f, 8f);
                targetPosition = new Vector3(randomX, transform.position.y, randomZ);

                Debug.Log($"[Local] 目标设定: ({randomX:F1}, {randomZ:F1})");
                SendMove(randomX, transform.position.y, randomZ);
            }
            else
            {
                Debug.LogWarning("⚠️ 发送中止：网络连接已断开，请检查后端或 Token");
            }
        }

        // 丝滑移动逻辑
        if (Vector3.Distance(transform.position, targetPosition) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            if (targetPosition != transform.position)
                transform.LookAt(targetPosition);
        }
    }

    public async void SendMove(float x, float y, float z)
    {
        if (_socket == null || _socket.State != WebSocketState.Open) return;

        try
        {
            GameMessage moveMsg = new GameMessage
            {
                Type = "move",
                X = x,
                Y = y,
                Z = z,
                UserId = 1
            };

            byte[] data = moveMsg.ToByteArray();
            await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            Debug.Log($"🚀 [网络已送达] X:{x:F1} Z:{z:F1}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 网络传输异常: {e.Message}");
        }
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
                    // 这里后端如果广播了，你会看到这条日志
                    Debug.Log($"📩 [收到广播] 类型: {incoming.Type} 内容: {incoming.Content}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"接收链路关闭: {e.Message}");
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (_socket != null)
        {
            _socket.Dispose();
            Debug.Log("🔌 WebSocket 已安全释放");
        }
    }
}