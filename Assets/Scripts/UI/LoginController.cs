using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

public class LoginController : MonoBehaviour
{
    private VisualElement root;
    private VisualElement panelLogin, panelRegister;
    private TextField loginUser, loginPass, regUser, regPass, regEmail, regCode;
    private Button btnLogin, btnGotoReg, btnSendCode, btnRegister, btnGotoLogin;
    private Label tipLogin, tipReg;

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        panelLogin = root.Q<VisualElement>("panel-login");
        panelRegister = root.Q<VisualElement>("panel-register");

        loginUser = root.Q<TextField>("login-user");
        loginPass = root.Q<TextField>("login-pass");
        btnLogin = root.Q<Button>("btn-login");
        btnGotoReg = root.Q<Button>("btn-goto-reg");
        tipLogin = root.Q<Label>("tip-login");

        regEmail = root.Q<TextField>("reg-email");
        regCode = root.Q<TextField>("reg-code");
        regUser = root.Q<TextField>("reg-user");
        regPass = root.Q<TextField>("reg-pass");

        btnSendCode = root.Q<Button>("btn-send-code");
        btnRegister = root.Q<Button>("btn-register");
        btnGotoLogin = root.Q<Button>("btn-goto-login");

        // ===== 修改代码 START =====
        // [修复 NullReferenceException：在 OnEnable 中补上漏掉的 tipReg 元素获取逻辑]
        tipReg = root.Q<Label>("tip-reg");
        // ===== 修改代码 END =====

        btnLogin.clicked += HandleLogin;
        btnGotoReg.clicked += () => SwitchPanel(false);

        btnSendCode.clicked += HandleSendCode;
        btnRegister.clicked += HandleRegister;
        btnGotoLogin.clicked += () => SwitchPanel(true);
    }

    private void SwitchPanel(bool showLogin)
    {
        panelLogin.style.display = showLogin ? DisplayStyle.Flex : DisplayStyle.None;
        panelRegister.style.display = showLogin ? DisplayStyle.None : DisplayStyle.Flex;
        tipLogin.text = ""; tipReg.text = "";
    }

    private void HandleLogin()
    {
        if (string.IsNullOrEmpty(loginUser.value) || string.IsNullOrEmpty(loginPass.value))
        {
            ShowTip(tipLogin, "账号和密码不能为空！", Color.red);
            return;
        }

        btnLogin.SetEnabled(false);
        ShowTip(tipLogin, "登录中，请稍候...", Color.black);

        HttpManager.Instance.Login(loginUser.value, loginPass.value, (success, msg) =>
        {
            btnLogin.SetEnabled(true);
            if (success)
            {
                ShowTip(tipLogin, "登录成功！正在进入游戏大厅...", new Color(0, 0.6f, 0));
                uint userId = ExtractUserIdFromToken(DataManager.Token);
                DataManager.SaveLoginData(DataManager.Token, userId);
                root.style.display = DisplayStyle.None;
                NetManager.Instance.ConnectToServer();
            }
            else { ShowTip(tipLogin, "登录失败: " + msg, Color.red); }
        });
    }

    private void HandleSendCode()
    {
        Debug.Log("🎯 ==== 1. 获取验证码按钮被点击了！ ====");

        if (string.IsNullOrEmpty(regEmail.value) || !regEmail.value.Contains("@"))
        {
            Debug.LogWarning("⚠️ ==== 邮箱格式不对，请求被前端拦截 ====");
            ShowTip(tipReg, "请输入正确的邮箱地址！", Color.red);
            return;
        }

        Debug.Log("🚀 ==== 2. 邮箱验证通过，启动倒计时协程，准备呼叫 HttpManager ====");
        StartCoroutine(CodeCountdownRoutine());
        ShowTip(tipReg, "发送中...", Color.black);

        HttpManager.Instance.SendEmailCode(regEmail.value, (success, msg) =>
        {
            Debug.Log($"📩 ==== 5. 收到后端回调响应！成功状态: {success}, 消息: {msg} ====");
            ShowTip(tipReg, success ? "验证码已发送至邮箱！" : "发送失败: " + msg, success ? new Color(0, 0.6f, 0) : Color.red);
        });
    }

    private IEnumerator CodeCountdownRoutine()
    {
        btnSendCode.SetEnabled(false); // 变灰不可点击
        int countdown = 60;

        while (countdown > 0)
        {
            btnSendCode.text = $"{countdown}s"; // 动态更新文字
            yield return new WaitForSeconds(1f); // 挂起 1 秒
            countdown--;
        }

        // 倒计时结束，恢复原状
        btnSendCode.text = "获取验证码";
        btnSendCode.SetEnabled(true);
    }

    private void HandleRegister()
    {
        if (string.IsNullOrEmpty(regUser.value) || string.IsNullOrEmpty(regPass.value) || string.IsNullOrEmpty(regCode.value))
        {
            ShowTip(tipReg, "请将注册信息填写完整！", Color.red);
            return;
        }

        btnRegister.SetEnabled(false);
        ShowTip(tipReg, "注册中...", Color.black);

        HttpManager.Instance.Register(regUser.value, regPass.value, regEmail.value, regCode.value, (success, msg) =>
        {
            btnRegister.SetEnabled(true);
            if (success)
            {
                ShowTip(tipReg, "注册成功！请返回登录。", new Color(0, 0.6f, 0));
                loginUser.value = regUser.value; // 自动填入账号
                Invoke(nameof(DelaySwitchToLogin), 1.5f);
            }
            else { ShowTip(tipReg, "注册失败: " + msg, Color.red); }
        });
    }

    private void DelaySwitchToLogin() { SwitchPanel(true); }

    private void ShowTip(Label label, string msg, Color color)
    {
        label.text = msg; label.style.color = new StyleColor(color);
    }

    [Serializable] private class JwtPayload { public uint user_id; }
    private uint ExtractUserIdFromToken(string token)
    {
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length < 2) return 0;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonUtility.FromJson<JwtPayload>(json).user_id;
        }
        catch { return 0; }
    }
}