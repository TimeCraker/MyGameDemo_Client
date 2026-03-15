using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class LobbyController : MonoBehaviour
{
    [Header("3D 展示模型")]
    [Tooltip("拖入大厅中央用来展示的方块")]
    public GameObject displayModel;

    private VisualElement lobbyLayer;
    private VisualElement rosterLayer;
    private HeroClass previewClass = HeroClass.Role1_Speedster;

    private void OnEnable()
    {
        // 1. 初始化与兜底逻辑
        if (GameManager.Instance == null)
        {
            GameObject gm = new GameObject("GameManager");
            gm.AddComponent<GameManager>();
        }

        var root = GetComponent<UIDocument>().rootVisualElement;
        if (root == null) return;

        // 2. 获取图层与 UI 节点
        lobbyLayer = root.Q<VisualElement>("lobby-layer");
        rosterLayer = root.Q<VisualElement>("roster-layer");

        Label usernameLabel = root.Q<Label>("txt-username");
        if (usernameLabel != null && GameManager.Instance != null)
        {
            usernameLabel.text = GameManager.Instance.Username;
        }

        // 3. 绑定主界面按钮事件
        Button btnTraining = root.Q<Button>("btn-training");
        if (btnTraining != null) btnTraining.clicked += EnterTrainingCamp;

        Button btnOpenRoster = root.Q<Button>("btn-open-roster");
        if (btnOpenRoster != null) btnOpenRoster.clicked += () => ToggleRoster(true);

        // 4. 绑定选角面板(Roster)按钮事件
        Button btnR1 = root.Q<Button>("btn-r1");
        if (btnR1 != null) btnR1.clicked += () => PreviewRole(HeroClass.Role1_Speedster, Color.cyan);

        Button btnR2 = root.Q<Button>("btn-r2");
        if (btnR2 != null) btnR2.clicked += () => PreviewRole(HeroClass.Role2_Curser, new Color(0.6f, 0f, 1f));

        Button btnR3 = root.Q<Button>("btn-r3");
        if (btnR3 != null) btnR3.clicked += () => PreviewRole(HeroClass.Role3_Reviver, Color.red);

        Button btnR4 = root.Q<Button>("btn-r4");
        if (btnR4 != null) btnR4.clicked += () => PreviewRole(HeroClass.Role4_Tank, Color.yellow);

        Button btnDeploy = root.Q<Button>("btn-deploy");
        if (btnDeploy != null) btnDeploy.clicked += ConfirmDeployment;

        ToggleRoster(false);
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            previewClass = GameManager.Instance.SelectedClass;
            Color defaultColor = Color.cyan;

            switch (previewClass)
            {
                case HeroClass.Role1_Speedster: defaultColor = Color.cyan; break;
                case HeroClass.Role2_Curser: defaultColor = new Color(0.6f, 0f, 1f); break;
                case HeroClass.Role3_Reviver: defaultColor = Color.red; break;
                case HeroClass.Role4_Tank: defaultColor = Color.yellow; break;
            }

            PreviewRole(previewClass, defaultColor);
        }
    }

    private void PreviewRole(HeroClass role, Color modelColor)
    {
        previewClass = role;
        if (displayModel != null)
        {
            Renderer r = displayModel.GetComponent<Renderer>();
            if (r != null) r.material.color = modelColor;
        }
        Debug.Log($"👁️ 正在预览角色: {role}");
    }

    private void ConfirmDeployment()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SelectedClass = previewClass;
            Debug.Log($"✅ 已确认出战英雄: {GameManager.Instance.SelectedClass}");
        }
        ToggleRoster(false);
    }

    private void ToggleRoster(bool show)
    {
        if (rosterLayer != null)
        {
            rosterLayer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void EnterTrainingCamp()
    {
        if (GameManager.Instance != null)
        {
            Debug.Log($"🚀 带着职业 {GameManager.Instance.SelectedClass} 进入训练营！");
        }
        else
        {
            Debug.Log("🚀 进入训练营！(未检测到 GameManager)");
        }

        SceneManager.LoadScene("test");
    }
}