using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class HttpManager : MonoBehaviour
{
    public static HttpManager Instance;

    [Header("服务器配置")]
    // [直接写死 127.0.0.1，防止 Unity 面板覆盖导致 localhost 解析失败]
    public string httpBaseUrl = "http://127.0.0.1:8081/api/v1";

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

    // 1. 发送邮箱验证码 (带雷达日志)
    public void SendEmailCode(string email, Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(new EmailReq { email = email });
        Debug.Log($"📡 ==== 3. HttpManager 开始组装发信请求，目标 URL: {httpBaseUrl}/send-code ====");
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
                LoginRes res = JsonUtility.FromJson<LoginRes>(responseData);
                DataManager.SaveLoginData(res.token, 0);
                callback(true, "登录成功");
            }
            else
            {
                callback(false, responseData);
            }
        }));
    }

    // 底层 POST 请求协程 (带雷达日志)
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
                Debug.Log($"✅ ==== 4. HTTP 请求成功送达！后端返回: {request.downloadHandler.text} ====");
                callback(true, request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"❌ ==== 4. HTTP 请求发生致命错误: {request.error} ====");
                string errorMsg = request.error;
                try
                {
                    ErrorRes errRes = JsonUtility.FromJson<ErrorRes>(request.downloadHandler.text);
                    if (errRes != null && !string.IsNullOrEmpty(errRes.error))
                    {
                        errorMsg = errRes.error;
                        Debug.LogError($"❌ ==== 后端附加的详细错误: {errorMsg} ====");
                    }
                }
                catch { /* 解析失败则使用默认网络错误 */ }

                callback(false, errorMsg);
            }
        }
    }
}