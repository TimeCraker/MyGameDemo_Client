using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("玩家全局数据")]
    public string Username = "TimeCraker"; // 预设玩家名
    public HeroClass SelectedClass = HeroClass.Role1_Speedster; // 默认选中职业

    // ===== 新增代码 START =====
    // 修改内容：新增用于跨场景保存房间号的字段
    // 修改原因：阶段二匹配成功后需要将 RoomId 写入全局，并在 ArenaScene 中使用
    public string CurrentRoomId = string.Empty;
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
    }

    // ===== 新增代码 START =====
    // 修改内容：新增供 WebGL/网页端调用的桥接入口 EnterBattle(payload)
    // 修改原因：React Web 端已完成登录/大厅/匹配；Unity 仅作为战斗容器，需要从网页侧注入 token/userId/roomId/heroClass
    // 约定格式：token|userId|roomId|heroClass
    public void EnterBattle(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            Debug.LogError("❌ EnterBattle: payload 为空，无法进入战场");
            return;
        }

        string[] parts = payload.Split('|');
        if (parts.Length < 4)
        {
            Debug.LogError($"❌ EnterBattle: payload 格式错误，期望 4 段，实际 {parts.Length} 段，payload={payload}");
            return;
        }

        string token = parts[0];
        string userIdStr = parts[1];
        string roomId = parts[2];
        string heroClassStr = parts[3];

        if (!uint.TryParse(userIdStr, out uint userId) || userId == 0)
        {
            Debug.LogError($"❌ EnterBattle: userId 解析失败，userId={userIdStr}");
            return;
        }

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

        // 注意：React 已完成排队匹配并拿到 roomId，这里绝对不能再发 match_req
        // 仅进入“准备连接对战服”的流程（下一阶段补齐房间同步逻辑）
        if (NetManager.Instance != null)
        {
            NetManager.Instance.ConnectToArena();
        }
        else
        {
            Debug.LogError("❌ EnterBattle: 未检测到 NetManager.Instance，无法进入对战连接流程");
        }
    }
    // ===== 新增代码 END =====
}