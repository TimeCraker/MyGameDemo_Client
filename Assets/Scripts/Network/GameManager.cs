using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("玩家全局数据")]
    public string Username = "TimeCraker"; // 预设玩家名
    public HeroClass SelectedClass = HeroClass.Role1_Speedster; // 默认选中职业

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
}