using UnityEngine;

public class GameManager : MonoBehaviour
{
    // 修改内容：新增前端注入载荷模型（JSON）
    // 修改原因：React 端改为 JSON 传参，便于扩展 wsBase 等字段
    [System.Serializable]
    private class EnterBattlePayload
    {
        public string token;
        public string roomId;
        public uint userId;
        public string selectedClass;
        public string wsBase;
    }

    public static GameManager Instance;

    [Header("玩家全局数据")]
    public string Username = "TimeCraker"; // 预设玩家名
    public HeroClass SelectedClass = HeroClass.Role1_Speedster; // 默认选中职业

    // ===== 新增代码 START =====
    // 修改内容：新增用于跨场景保存房间号的字段
    // 修改原因：阶段二匹配成功后需要将 RoomId 写入全局，并在 ArenaScene 中使用
    public string CurrentRoomId = string.Empty;
    // ===== 新增代码 END =====

    // ===== 新增代码 START =====
    // 修改内容：缓存 BattleWsClient 引用，避免 EnterBattle 时反复场景查找
    // 修改原因：统一网络链路入口并提升稳定性
    [Header("网络桥接")]
    [SerializeField] private BattleWsClient battleWsClient;
    // ===== 新增代码 END =====

    private void Awake()
    {
        // 经典的单例模式与跨场景保留
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // ===== 新增代码 START =====
        // 修改内容：Awake 阶段完成 BattleWsClient 引用初始化
        // 修改原因：确保 EnterBattle 能优先走 WebGL 兼容链路
        if (battleWsClient == null)
        {
            battleWsClient = GetComponent<BattleWsClient>();
        }
        if (battleWsClient == null)
        {
            battleWsClient = FindObjectOfType<BattleWsClient>();
        }
        // ===== 新增代码 END =====
    }

    // ===== 新增代码 START =====
    // 修改内容：新增供 WebGL/网页端调用的桥接入口 EnterBattle(payload)
    // 修改原因：React Web 端已完成登录/大厅/匹配；Unity 仅作为战斗容器，需要从网页侧注入 token/userId/roomId/heroClass
    // 约定格式：优先 JSON（token/userId/roomId/selectedClass/wsBase），兼容旧格式 token|userId|roomId|heroClass
    public void EnterBattle(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogError("❌ EnterBattle: payload 为空，无法进入战场");
            return;
        }

        // ===== 新增代码 START =====
        // 修改内容：先尝试 JSON 解析；失败再回退旧的分隔符协议
        // 修改原因：前端已切换 JSON 传参，同时保留向后兼容
        string token = string.Empty;
        uint userId = 0;
        string roomId = string.Empty;
        string heroClassStr = string.Empty;
        string wsBase = string.Empty;

        bool parsed = false;
        if (!string.IsNullOrWhiteSpace(payload) && payload.TrimStart().StartsWith("{"))
        {
            try
            {
                EnterBattlePayload p = JsonUtility.FromJson<EnterBattlePayload>(payload);
                if (p != null && !string.IsNullOrWhiteSpace(p.token) && !string.IsNullOrWhiteSpace(p.roomId) && p.userId > 0)
                {
                    token = p.token;
                    userId = p.userId;
                    roomId = p.roomId;
                    heroClassStr = p.selectedClass ?? string.Empty;
                    wsBase = p.wsBase ?? string.Empty;
                    parsed = true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"⚠️ EnterBattle JSON 解析失败，将回退旧协议: {ex.Message}");
            }
        }

        if (!parsed)
        {
            string[] parts = payload.Split('|');
            if (parts.Length < 4)
            {
                Debug.LogError($"❌ EnterBattle: payload 格式错误，期望 4 段或 JSON，实际 {parts.Length} 段，payload={payload}");
                return;
            }

            token = parts[0];
            if (!uint.TryParse(parts[1], out userId) || userId == 0)
            {
                Debug.LogError($"❌ EnterBattle: userId 解析失败，userId={parts[1]}");
                return;
            }
            roomId = parts[2];
            heroClassStr = parts[3];
        }
        // ===== 新增代码 END =====

        // 将 token/userId 写入 DataManager（替代旧 Unity 登录态）
        DataManager.SaveLoginData(token, userId);

        // 保存房间号供后续对战同步使用
        CurrentRoomId = roomId ?? string.Empty;

        // 兼容前端命名差异：前端/协议可能与 Unity 枚举名不完全一致
        // - Role2_Cursemancer -> Role2_Curser
        // - Role4_Bulwark -> Role4_Tank
        if (string.Equals(heroClassStr, "Role2_Cursemancer", System.StringComparison.OrdinalIgnoreCase))
        {
            heroClassStr = "Role2_Curser";
        }
        else if (string.Equals(heroClassStr, "Role4_Bulwark", System.StringComparison.OrdinalIgnoreCase))
        {
            heroClassStr = "Role4_Tank";
        }

        if (!System.Enum.TryParse(heroClassStr, ignoreCase: false, out HeroClass parsedClass))
        {
            Debug.LogWarning($"⚠️ EnterBattle: heroClass 解析失败，将使用默认职业。heroClass={heroClassStr}");
        }
        else
        {
            SelectedClass = parsedClass;
        }

        Debug.Log($"✅ EnterBattle: 注入完成 -> userId={userId}, roomId={CurrentRoomId}, class={SelectedClass}");

        // ===== 新增代码 START =====
        // 修改内容：优先切到 WebGL 兼容的 BattleWsClient（NativeWebSocket）
        // 修改原因：ClientWebSocket 在 WebGL 不可用，且战场已切到 Protobuf 二进制链路
        if (battleWsClient != null)
        {
            string jsonPayload = JsonUtility.ToJson(new EnterBattlePayload
            {
                token = token,
                roomId = roomId,
                userId = userId,
                selectedClass = heroClassStr,
                wsBase = wsBase
            });
            battleWsClient.EnterBattle(jsonPayload);
        }
        else
        {
            Debug.LogWarning("⚠️ EnterBattle: 未检测到 BattleWsClient，回退 NetManager.ConnectToArena()");
            // 保底回退老链路（编辑器或历史场景）
            if (NetManager.Instance != null)
            {
                NetManager.Instance.ConnectToArena();
            }
            else
            {
                Debug.LogError("❌ EnterBattle: 未检测到 BattleWsClient/NetManager，无法进入对战连接流程");
            }
        }
        // ===== 新增代码 END =====
    }
    // ===== 新增代码 END =====
}