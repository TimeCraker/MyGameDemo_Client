using UnityEngine;
using UnityEngine.UIElements;

public class HUDController : MonoBehaviour
{
    public static HUDController Instance;

    [Header("跟随设置")]
    [Tooltip("拖入你需要跟随的主角")]
    public Transform targetPlayer;

    // 👉 核心修正：大幅增加向下偏移，保证绝对不挡脸！
    [Tooltip("UI相对于主角坐标的偏移量")]
    public Vector3 offset = new Vector3(0, 0, -1.2f);

    private VisualElement root;
    private VisualElement hudPanel;
    private VisualElement hpFill;
    private VisualElement energyFill;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        hudPanel = root.Q<VisualElement>("hud-panel");
        hpFill = root.Q<VisualElement>("hp-fill");
        energyFill = root.Q<VisualElement>("energy-fill");
    }

    private void LateUpdate()
    {
        // 兜底逻辑：如果没手动拖入，自动寻找本地玩家
        if (targetPlayer == null)
        {
            GameObject hero = GameObject.Find("hero");
            if (hero != null) targetPlayer = hero.transform;
            else return;
        }

        if (hudPanel == null) return;

        // 获取带偏移量的 3D 坐标
        Vector3 worldPos = targetPlayer.position + offset;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

        if (screenPos.z < 0)
        {
            hudPanel.style.display = DisplayStyle.None;
            return;
        }

        hudPanel.style.display = DisplayStyle.Flex;

        Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
            root.panel,
            new Vector2(screenPos.x, Screen.height - screenPos.y)
        );

        hudPanel.style.left = panelPos.x;
        hudPanel.style.top = panelPos.y;
    }

    public void UpdateHP(int current, int max)
    {
        if (hpFill == null) return;
        float percent = (float)current / max * 100f;
        hpFill.style.width = new Length(percent, LengthUnit.Percent);
    }

    public void UpdateEnergy(float current, float max)
    {
        if (energyFill == null) return;
        float percent = current / max * 100f;
        energyFill.style.width = new Length(percent, LengthUnit.Percent);
    }
}