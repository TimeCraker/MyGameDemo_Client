using Google.Protobuf; // 引入 Protobuf 命名空间
// 假设生成的类在默认命名空间或你定义的命名空间
using Pb;
using Proto;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class NetManager : MonoBehaviour
{
    private ClientWebSocket _socket;
    // ❗ 记得换成你 Postman 拿到的新 Token
    private string _url = "ws://localhost:8081/ws?token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX2lkIjoxLCJleHAiOjE3NzI2MDg4ODEsImlhdCI6MTc3MjUyMjQ4MX0.fSHCspqvRCNXSHX3CKeeZsa7YVUJHtfyR3tCi73UyR0";

    async void Start()
    {
        _socket = new ClientWebSocket();
        try
        {
            await _socket.ConnectAsync(new Uri(_url), CancellationToken.None);
            Debug.Log("✅ 成功连接到 Go 服务器！");

            // 启动接收循环
            _ = ReceiveLoop();

            // 测试：发一个移动包
            SendMove(12.5f, 0, -5.2f);
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 连接失败: {e.Message}");
        }
    }

    public async void SendMove(float x, float y, float z)
    {
        if (_socket.State != WebSocketState.Open) return;

        // 构造 Protobuf 消息 (这部分和 Go 非常像)
        GameMessage moveMsg = new GameMessage
        {
            Type = "move",
            X = x,
            Y = y,
            Z = z
        };

        // 序列化
        byte[] data = moveMsg.ToByteArray();

        await _socket.SendAsync(new ArraySegment<byte>(data),
            WebSocketMessageType.Binary, true, CancellationToken.None);
        Debug.Log($"🚀 已向服务器同步坐标: ({x}, {y}, {z})");
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 4];
        while (_socket.State == WebSocketState.Open)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                // 反序列化
                GameMessage incoming = GameMessage.Parser.ParseFrom(buffer, 0, result.Count);
                Debug.Log($"📩 收到服务器广播 | 类型: {incoming.Type}");

                // 这里以后写逻辑：如果是别人的坐标，就移动场景里的方块
            }
        }
    }
}