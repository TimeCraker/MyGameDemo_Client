// --- FILE: Assets/Scripts/Network/DataManager.cs ---

using UnityEngine;

// [全局数据管理类，用于跨脚本共享 Token 和玩家信息]
public static class DataManager
{
    // 缓存的 JWT Token，用于 WebSocket 鉴权
    public static string Token { get; private set; } = string.Empty;

    // 当前登录的玩家 ID
    public static uint MyUserId { get; private set; } = 0;

    /// <summary>当前战斗房间 ID（由 React 注入 / BattleWsClient 写入，供其他系统读取）</summary>
    public static string RoomId { get; private set; } = string.Empty;

    // 登录成功后调用此方法保存数据
    public static void SaveLoginData(string token, uint userId)
    {
        Token = token;
        MyUserId = userId;
        Debug.Log($"💾 数据已缓存 -> UserID: {MyUserId}, Token: {Token.Substring(0, 15)}...");
    }

    public static void SetBattleRoom(string roomId)
    {
        RoomId = string.IsNullOrWhiteSpace(roomId) ? string.Empty : roomId;
    }

    // 退出登录时清除数据
    public static void ClearData()
    {
        Token = string.Empty;
        MyUserId = 0;
        RoomId = string.Empty;
    }

    // 检查是否已登录
    public static bool IsLoggedIn()
    {
        return !string.IsNullOrEmpty(Token) && MyUserId != 0;
    }
}
// ===== 新增代码 END =====