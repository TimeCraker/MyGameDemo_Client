using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Google.Protobuf;
using NativeWebSocket;
using Proto;
using UnityEngine;

public class BattleWsClient : MonoBehaviour
{
    [Serializable]
    private class BattleInitPayload
    {
        public string token;
        public string roomId;
        public uint userId;
        public string wsBase;
    }

    private const string DefaultWsBase = "ws://127.0.0.1:8081/ws";
    private readonly Dictionary<uint, GameObject> _playerCubes = new Dictionary<uint, GameObject>();
    private WebSocket _socket;
    // ===== 新增代码 START =====
    // 修改内容：新增本地玩家与定时发送状态缓存
    // 修改原因：战场必须持续上报本地坐标，避免远端看不到移动
    [SerializeField] private Transform localPlayerTransform;
    private uint _localUserId;
    private string _localRoomId = string.Empty;
    private float _moveSendTimer;
    private const float MoveSendInterval = 1f / 12f; // 12Hz
    private bool _isSendingMove;
    // ===== 新增代码 END =====

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void NotifyReactBattleResult(string resultType, string payloadJson);
#endif

    // ===== 新增代码 START =====
    // 修改内容：入口处强制解锁帧率（关闭垂直同步 + 交给浏览器调度）
    // 修改原因：WebGL 调试阶段降低拖拽感，提升对战视觉响应
    // 影响范围：仅当前脚本所在运行实例
    private void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
    }
    // ===== 新增代码 END =====

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        _socket?.DispatchMessageQueue();
#endif
        // ===== 新增代码 START =====
        // 修改内容：按固定频率上传本地玩家 move 包（x/y/z + rot_y）
        // 修改原因：补齐战场实时同步链路，解决“哑巴状态”
        // 影响范围：仅 Battle WebSocket 打开时的周期上报
        if (_socket == null || _socket.State != WebSocketState.Open) return;
        _moveSendTimer += Time.deltaTime;
        if (_moveSendTimer < MoveSendInterval) return;
        _moveSendTimer = 0f;

        TryResolveLocalPlayerTransform();
        if (localPlayerTransform == null) return;
        if (_isSendingMove) return;
        SendMove(localPlayerTransform.position, localPlayerTransform.eulerAngles.y);
        // ===== 新增代码 END =====
    }

    private async void OnDestroy()
    {
        DataManager.SetBattleRoom("");
        if (_socket != null)
        {
            await _socket.Close();
            _socket = null;
        }
    }

    // ===== 新增代码 START =====
    // 修改内容：供 React 通过 SendMessage 调用，使用 JSON 注入 token/room_id/user_id/ws 基址
    // 修改原因：WebGL 场景下通过 JS -> Unity 完成战场初始化参数传递
    // 影响范围：战场初始化握手阶段
    public async void EnterBattle(string payloadJson)
    {
        // ===== 新增代码 START =====
        // 修改内容：连接防抖锁，避免重复创建 WebSocket
        // 修改原因：前端严格模式/重复注入可能触发 EnterBattle 两次
        // 影响范围：Battle WebSocket 建连入口
        if (_socket != null && (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.Connecting))
        {
            Debug.LogWarning("[BattleWsClient] EnterBattle ignored: socket already open/connecting.");
            return;
        }
        // ===== 新增代码 END =====

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            Debug.LogError("[BattleWsClient] Empty payload.");
            return;
        }

        BattleInitPayload payload;
        try
        {
            payload = JsonUtility.FromJson<BattleInitPayload>(payloadJson);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleWsClient] Parse payload failed: {ex}");
            return;
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.token) || string.IsNullOrWhiteSpace(payload.roomId))
        {
            Debug.LogError("[BattleWsClient] Missing token/roomId.");
            return;
        }
        _localUserId = payload.userId;
        _localRoomId = payload.roomId;

        // token + roomId query，与网关 websocket.go 对齐（roomId 为空时后端按大厅处理，战斗必须带 roomId）
        string wsBase = string.IsNullOrWhiteSpace(payload.wsBase) ? DefaultWsBase : payload.wsBase;
        wsBase = wsBase.TrimEnd('/');
        string wsUrl =
            $"{wsBase}?token={Uri.EscapeDataString(payload.token)}&scope=battle&roomId={Uri.EscapeDataString(payload.roomId)}";
        Debug.Log($"[BattleWsClient] Connecting URL => {wsUrl}");
        DataManager.SetBattleRoom(payload.roomId);

        _socket = new WebSocket(wsUrl);

        _socket.OnOpen += () =>
        {
            Debug.Log("[BattleWsClient] Connected.");
            SendInitOrJoin(payload.roomId, payload.token, payload.userId);
        };

        _socket.OnError += (err) =>
        {
            Debug.LogError($"[BattleWsClient] Error: {err}");
        };

        _socket.OnClose += (code) =>
        {
            Debug.Log($"[BattleWsClient] Closed: {code}");
        };

        _socket.OnMessage += (bytes) =>
        {
            HandleBinaryMessage(bytes);
        };

        await _socket.Connect();
    }
    // ===== 新增代码 END =====

    private async void SendInitOrJoin(string roomId, string token, uint userId)
    {
        if (_socket == null || _socket.State != WebSocketState.Open) return;

        var initMessage = new GameMessage
        {
            Type = "init",
            RoomId = roomId,
            UserId = userId,
            Content = token
        };

        byte[] data = initMessage.ToByteArray();
        await _socket.Send(data);
    }

    // ===== 新增代码 START =====
    // 修改内容：二进制 Protobuf 接收与解析
    // 修改原因：战场通信协议由 JSON 改为 game.proto 强类型二进制
    // 影响范围：所有服务端推送消息处理
    private void HandleBinaryMessage(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return;

        GameMessage message;
        try
        {
            message = GameMessage.Parser.ParseFrom(bytes);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleWsClient] ParseFrom failed: {ex}");
            return;
        }

        switch (message.Type)
        {
            case "snapshot":
                foreach (var p in message.Players)
                {

                    // 修改内容：先过滤本地玩家回音包，避免本地重影
                    // 修改原因：服务端广播全量房间状态，本地玩家数据不应生成远端替身
                    // 影响范围：snapshot 玩家渲染
                    if (p.UserId == _localUserId) continue;
                    UpsertPlayerCube(p.UserId, p.X, p.Y, p.Z, p.RotY);
                }
                break;
            case "move":
            case "state":

                // 修改内容：先过滤本地玩家回音包，避免本地重影
                // 修改原因：服务端回播本地 move/state 时不应驱动远端实体创建
                // 影响范围：move/state 玩家渲染
                if (message.UserId == _localUserId) return;
                UpsertPlayerCube(message.UserId, message.X, message.Y, message.Z, message.RotY);

                break;
            case "opponent_left":
            case "victory":
            case "win":
                NotifyReact(message.Type, message.RoomId, message.UserId);
                break;
            default:
                break;
        }
    }

    // 修改内容：Greybox 玩家方块与浅粉 -> 紫色调试配色
    // 修改原因：快速可视化双端同步，提升联调可读性
    // 影响范围：仅测试方块渲染层
    private void UpsertPlayerCube(uint userId, float x, float y, float z, float rotY)
    {
        if (!_playerCubes.TryGetValue(userId, out var cube))
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Player_{userId}";
            _playerCubes[userId] = cube;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Unlit/Color"));
                Color lightPink = new Color(1.0f, 0.72f, 0.86f, 1.0f);
                Color purple = new Color(0.56f, 0.20f, 0.86f, 1.0f);
                float t = Mathf.PingPong(_playerCubes.Count - 1, 1f);
                mat.color = Color.Lerp(lightPink, purple, t);
                renderer.material = mat;
            }

            // ===== 新增代码 START =====
            // 修改内容：为远端玩家注入网络驱动所需组件（刚体 + 控制器）
            // 修改原因：远端对象必须走 PlayerController 的插值系统，而非死物方块
            // 影响范围：远端玩家实体初始化
            var rb = cube.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cube.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            var pc = cube.GetComponent<PlayerController>();
            if (pc == null)
            {
                pc = cube.AddComponent<PlayerController>();
            }
            pc.isLocalPlayer = false;
            // ===== 新增代码 END =====
        }

        // ===== 新增代码 START =====
        // 修改内容：统一通过 PlayerController 网络插值接口喂状态
        // 修改原因：避免直接改 transform 造成远端“植物人/瞬移感”
        // 影响范围：远端玩家位姿更新
        var playerController = cube.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.isLocalPlayer = false;
            playerController.ApplyNetworkState(x, y, z, rotY);
            return;
        }
        // 兜底：若组件异常缺失，至少保证位置与朝向更新
        cube.transform.position = new Vector3(x, y, z);
        cube.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
        // ===== 新增代码 END =====
    }
    // ===== 新增代码 END =====

    private void NotifyReact(string resultType, string roomId, uint userId)
    {
        string payloadJson = $"{{\"roomId\":\"{roomId}\",\"userId\":{userId}}}";
#if UNITY_WEBGL && !UNITY_EDITOR
        NotifyReactBattleResult(resultType, payloadJson);
#else
        Debug.Log($"[BattleWsClient] Result => {resultType}, {payloadJson}");
#endif
    }

    // ===== 新增代码 START =====
    // 修改内容：自动定位本地玩家 Transform（PlayerController.isLocalPlayer = true）
    // 修改原因：不修改 PlayerController 的前提下补齐本地坐标上传
    // 影响范围：BattleWsClient 内部引用解析
    private void TryResolveLocalPlayerTransform()
    {
        if (localPlayerTransform != null) return;
        var playerControllers = FindObjectsOfType<PlayerController>();
        for (int i = 0; i < playerControllers.Length; i++)
        {
            if (playerControllers[i] != null && playerControllers[i].isLocalPlayer)
            {
                localPlayerTransform = playerControllers[i].transform;
                Debug.Log($"[BattleWsClient] Local player resolved => {localPlayerTransform.name}");
                return;
            }
        }
    }

    private async void SendMove(Vector3 position, float rotY)
    {
        if (_socket == null || _socket.State != WebSocketState.Open) return;
        _isSendingMove = true;
        try
        {
            var moveMessage = new GameMessage
            {
                Type = "move",
                UserId = _localUserId,
                RoomId = _localRoomId,
                X = position.x,
                Y = position.y,
                Z = position.z,
                RotY = rotY
            };
            await _socket.Send(moveMessage.ToByteArray());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BattleWsClient] Send move failed: {ex.Message}");
        }
        finally
        {
            _isSendingMove = false;
        }
    }
    // ===== 新增代码 END =====
}

