using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

using UnityEngine.Video; // 引入视频控制命名空间

public class LoginController : MonoBehaviour
{
    private VisualElement root;
    private VisualElement panelLogin, panelRegister, panelLoading;
    private TextField loginUser, loginPass, regUser, regPass, regEmail, regCode;
    private Button btnLogin, btnGotoReg, btnSendCode, btnRegister, btnGotoLogin;
    private Label tipLogin, tipReg, textLoadingStatus, textPressKey;
    private VisualElement progressFill;

    // 控制是否处于等待按键的状态
    private bool isWaitingForKey = false;

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        panelLogin = root.Q<VisualElement>("panel-login");
        panelRegister = root.Q<VisualElement>("panel-register");


        panelLoading = root.Q<VisualElement>("panel-loading");
        textLoadingStatus = root.Q<Label>("text-loading-status");
        textPressKey = root.Q<Label>("text-press-key");
        progressFill = root.Q<VisualElement>("progress-fill");


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
        tipReg = root.Q<Label>("tip-reg");

        btnLogin.clicked += HandleLogin;
        btnGotoReg.clicked += () => SwitchPanel(false);

        btnSendCode.clicked += HandleSendCode;
        btnRegister.clicked += HandleRegister;
        btnGotoLogin.clicked += () => SwitchPanel(true);
    }


    // [在 Update 中实时监听玩家的“任意键”敲击]
    private void Update()
    {
        if (isWaitingForKey && Input.anyKeyDown)
        {
            isWaitingForKey = false;
            EnterGameWorld();
        }
    }

    private void SwitchPanel(bool showLogin)
    {
        panelLogin.style.display = showLogin ? DisplayStyle.Flex : DisplayStyle.None;
        panelRegister.style.display = showLogin ? DisplayStyle.None : DisplayStyle.Flex;
        panelLoading.style.display = DisplayStyle.None; // 确保切换时加载界面是隐藏的
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
        ShowTip(tipLogin, "登录验证中...", Color.black);

        HttpManager.Instance.Login(loginUser.value, loginPass.value, (success, msg) =>
        {
            btnLogin.SetEnabled(true);
            if (success)
            {
                uint userId = ExtractUserIdFromToken(DataManager.Token);
                DataManager.SaveLoginData(DataManager.Token, userId);

                // [登录成功后，不再直接隐藏全屏 UI，而是切换到大厂级加载动画模式]
                panelLogin.style.display = DisplayStyle.None;
                panelLoading.style.display = DisplayStyle.Flex;
                StartCoroutine(LoadingRoutine());
            }
            else { ShowTip(tipLogin, "登录失败: " + msg, Color.red); }
        });
    }

    // [模拟加载协程与动画，增强沉浸感]
    private IEnumerator LoadingRoutine()
    {
        float progress = 0f;
        while (progress < 100f)
        {
            // 随机增加进度，模拟真实的资源加载停顿感
            progress += UnityEngine.Random.Range(2f, 8f);
            if (progress > 100f) progress = 100f;

            // 动态控制 CSS 里的 width 属性来拉长进度条
            progressFill.style.width = new Length(progress, LengthUnit.Percent);

            // 根据进度改变提示文本
            if (progress > 80f) textLoadingStatus.text = "正在初始化网络同步...";
            else if (progress > 40f) textLoadingStatus.text = "正在加载环境资源...";

            yield return new WaitForSeconds(0.08f);
        }

        // 加载彻底完成，显示高燃提示
        textLoadingStatus.text = "加载完成";
        textPressKey.style.display = DisplayStyle.Flex;
        isWaitingForKey = true; // 激活 Update 里的按键监听
    }

    // [最终的仪式感：闭幕视频，正式进入真实 3D 世界]
    private void EnterGameWorld()
    {
        // 1. 彻底隐藏 UI
        root.style.display = DisplayStyle.None;

        // 2. 找到主摄像机上的视频播放器并将其禁用 (露出背后的真实 3D 场景)
        if (Camera.main != null)
        {
            VideoPlayer videoPlayer = Camera.main.GetComponent<VideoPlayer>();
            if (videoPlayer != null)
            {
                videoPlayer.enabled = false;
            }
        }

        // 3. 正式呼叫大厅进行长连接，加载方块人
        NetManager.Instance.ConnectToServer();
    }

    private void HandleSendCode()
    {
        if (string.IsNullOrEmpty(regEmail.value) || !regEmail.value.Contains("@"))
        {
            ShowTip(tipReg, "请输入正确的邮箱地址！", Color.red);
            return;
        }

        StartCoroutine(CodeCountdownRoutine());
        ShowTip(tipReg, "发送中...", Color.black);

        HttpManager.Instance.SendEmailCode(regEmail.value, (success, msg) =>
        {
            ShowTip(tipReg, success ? "验证码已发送至邮箱！" : "发送失败: " + msg, success ? new Color(0, 0.6f, 0) : Color.red);
        });
    }

    private IEnumerator CodeCountdownRoutine()
    {
        btnSendCode.SetEnabled(false);
        int countdown = 60;

        while (countdown > 0)
        {
            btnSendCode.text = $"{countdown}s";
            yield return new WaitForSeconds(1f);
            countdown--;
        }

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
                loginUser.value = regUser.value;
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