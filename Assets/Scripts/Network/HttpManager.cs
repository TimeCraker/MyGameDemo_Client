// --- FILE: Assets/Scripts/Network/HttpManager.cs ---

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ===== 新增代码 START =====
// [HTTP 网络请求管理类，负责与 Go 后端的 /api/v1 接口交互]
public class HttpManager : MonoBehaviour
{
    public static HttpManager Instance;

    [Header("服务器配置")]
    public string httpBaseUrl = "http://localhost:8081/api/v1";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject); // 切换场景时不销毁
    }

    // --- JSON 数据结构定义 (与 Go 后端严格对应) ---
    [Serializable] public class EmailReq { public string email; }
    [Serializable] public class RegisterReq { public string username; public string password; public string email; public string code; }
    [Serializable] public class LoginReq { public string username; public string password; }
    [Serializable] public class LoginRes { public string message; public string token; }
    [Serializable] public class ErrorRes { public string error; }

    // 1. 发送邮箱验证码
    public void SendEmailCode(string email, Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(new EmailReq { email = email });
        StartCoroutine(PostRequest($"{httpBaseUrl}/send-code", json, callback));
    }

    // 2. 注册账号
    public void Register(string username, string password, string email, string code, Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(new RegisterReq { username = username, password = password, email = email, code = code });
        StartCoroutine(PostRequest($"{httpBaseUrl}/register", json, callback));
    }

    // 3. 登录账号
    public void Login(string username, string password, Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(new LoginReq { username = username, password = password });
        StartCoroutine(PostRequest($"{httpBaseUrl}/login", json, (success, responseData) =>
        {
            if (success)
            {
                // 解析后端返回的 Token
                LoginRes res = JsonUtility.FromJson<LoginRes>(responseData);
                // 注意：目前后端 Login 接口未返回 UserID，前端暂用 0 替代，或后续让后端加上
                // 为了不修改后端，我们先缓存 Token，UserID 稍后通过 /api/me 获取或在 NetManager 中处理
                DataManager.SaveLoginData(res.token, 0); 
                callback(true, "登录成功");
            }
            else
            {
                callback(false, responseData);
            }
        }));
    }

    // 底层 POST 请求协程
    private IEnumerator PostRequest(string url, string jsonBody, Action<bool, string> callback)
    {
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                callback(true, request.downloadHandler.text);
            }
            else
            {
                // 尝试解析后端的自定义错误信息 {"error": "..."}
                string errorMsg = request.error;
                try
                {
                    ErrorRes errRes = JsonUtility.FromJson<ErrorRes>(request.downloadHandler.text);
                    if (errRes != null && !string.IsNullOrEmpty(errRes.error))
                    {
                        errorMsg = errRes.error;
                    }
                }
                catch { /* 解析失败则使用默认网络错误 */ }
                
                callback(false, errorMsg);
            }
        }
    }
}
// ===== 新增代码 END =====