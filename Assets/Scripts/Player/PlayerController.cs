using System.Collections;
using UnityEngine;

public enum PlayerState
{
    IdleWalk, Charging, PreCast, Dashing, PostCast, HitStun, Dead
}

public enum HeroClass
{
    Role1_Speedster,
    Role2_Curser,
    Role3_Reviver,
    Role4_Tank
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("网络与身份")]
    public bool isLocalPlayer = true;
    public HeroClass currentClass = HeroClass.Role1_Speedster;

    // 👉 新增：游戏就绪锁
    public bool isGameReady = false;

    [Header("资源数值")]
    public int maxHp = 300;
    public int currentHp = 300;
    public float maxEnergy = 15f;
    public float currentEnergy = 0f;

    [Header("视觉与反馈")]
    public Transform chargeIndicator;

    [Header("状态监测 (只读)")]
    public PlayerState currentState = PlayerState.IdleWalk;
    public float currentChargeTime = 0f;

    [Header("移动配置")]
    public float walkSpeed = 4f;
    public float chargingMoveSpeed = 1.5f;
    public float maxChargeTime = 5f;

    private float distancePerChargeSecond = 5f;
    private Plane groundPlane;

    // --- Buff 状态变量 ---
    private float chargeSpeedMultiplier = 1f;
    private float damageTakenMultiplier = 1f;
    private bool hasReviveBuff = false;
    private bool isInvincible = false;
    private int reviveCount = 0;
    private bool hasNoPostCastBuff = false;
    private bool isUltimateActive = false;

    void Start()
    {
        groundPlane = new Plane(Vector3.up, Vector3.zero);
        currentHp = maxHp;
        currentEnergy = 0f;

        if (chargeIndicator != null) chargeIndicator.gameObject.SetActive(false);

        // 👉 新增：开局从大厅管理器读取你选定的职业
        if (isLocalPlayer && GameManager.Instance != null)
        {
            currentClass = GameManager.Instance.SelectedClass;

            // 为了直观，把自身方块也染成对应职业的颜色
            Renderer r = GetComponent<Renderer>();
            switch (currentClass)
            {
                case HeroClass.Role1_Speedster: r.material.color = Color.cyan; break;
                case HeroClass.Role2_Curser: r.material.color = new Color(0.6f, 0f, 1f); break; // 紫色
                case HeroClass.Role3_Reviver: r.material.color = Color.red; break;
                case HeroClass.Role4_Tank: r.material.color = Color.yellow; break;
            }
        }

        // 场景加载完毕，直接解锁允许行动！
        isGameReady = true;
    }

    void Update()
    {
        // 👉 新增拦截：如果不是本地玩家，或者游戏尚未就绪，冻结一切逻辑！
        if (!isLocalPlayer || !isGameReady) return;

        HandleUltimateDrain();

        // 🔒 状态机锁：死亡、受击僵直、前摇、冲刺、后摇 期间，绝对禁止任何输入和走位！
        if (currentState == PlayerState.Dead || currentState == PlayerState.HitStun ||
            currentState == PlayerState.PreCast || currentState == PlayerState.Dashing || currentState == PlayerState.PostCast)
            return;

        if (currentState == PlayerState.IdleWalk || currentState == PlayerState.Charging)
        {
            HandleMouseLook();
        }

        if (currentState == PlayerState.IdleWalk)
        {
            HandleMovement(walkSpeed);
        }
        else if (currentState == PlayerState.Charging)
        {
            HandleMovement(chargingMoveSpeed);
        }

        HandleCombatInput();
        HandleUltimateInput();
        UpdateVisuals();
    }

    private void HandleUltimateInput()
    {
        if (Input.GetKeyDown(KeyCode.Q) && currentEnergy >= maxEnergy && currentState == PlayerState.IdleWalk && !isUltimateActive)
        {
            Debug.Log($"🌟 {gameObject.name} 释放了大招！当前职业：{currentClass}");
            StartUltimate();
        }
    }

    private void StartUltimate()
    {
        isUltimateActive = true;

        switch (currentClass)
        {
            case HeroClass.Role1_Speedster:
                chargeSpeedMultiplier = 1.5f;
                hasNoPostCastBuff = true;
                Debug.Log("⚡ 极速蓄力 Buff 开始！1.5倍蓄力且无后摇，持续 15 秒");
                break;
            case HeroClass.Role2_Curser:
                Debug.Log("🔮 释放疲劳诅咒！(等待网络)");
                break;
            case HeroClass.Role3_Reviver:
                hasReviveBuff = true;
                Debug.Log("👼 复活甲已激活！(如果在 15 秒内被击杀将复活)");
                break;
            case HeroClass.Role4_Tank:
                damageTakenMultiplier = 0.5f;
                Debug.Log("🛡️ 坚毅护甲 Buff 开始！受到伤害减半，持续 15 秒");
                break;
        }
    }

    private void HandleUltimateDrain()
    {
        if (!isUltimateActive) return;

        float drainRate = maxEnergy / 15f;
        currentEnergy -= drainRate * Time.deltaTime;

        if (currentEnergy <= 0)
        {
            currentEnergy = 0;
            EndUltimate();
        }

        if (isLocalPlayer && HUDController.Instance != null)
        {
            HUDController.Instance.UpdateEnergy(currentEnergy, maxEnergy);
        }
    }

    private void EndUltimate()
    {
        isUltimateActive = false;

        if (currentClass == HeroClass.Role1_Speedster)
        {
            chargeSpeedMultiplier = 1f;
            hasNoPostCastBuff = false;
            Debug.Log("⚡ 极速蓄力 Buff 结束！");
        }
        else if (currentClass == HeroClass.Role3_Reviver)
        {
            hasReviveBuff = false;
            Debug.Log("👼 复活甲时间结束，未被击杀，Buff自然消散！");
        }
        else if (currentClass == HeroClass.Role4_Tank)
        {
            damageTakenMultiplier = 1f;
            Debug.Log("🛡️ 坚毅护甲 Buff 结束！");
        }

        Debug.Log("🔋 大招状态已彻底解除，可以重新开始攒气！");

        if (isLocalPlayer && HUDController.Instance != null)
        {
            HUDController.Instance.UpdateEnergy(currentEnergy, maxEnergy);
        }
    }

    private void UpdateVisuals()
    {
        if (chargeIndicator != null)
        {
            if (currentState == PlayerState.Charging)
            {
                chargeIndicator.gameObject.SetActive(true);
                float exactDashDistance = currentChargeTime * distancePerChargeSecond;
                float scaleZ = Mathf.Max(exactDashDistance, 0.01f);
                chargeIndicator.localScale = new Vector3(0.2f, 0.05f, scaleZ);
                chargeIndicator.localPosition = new Vector3(0, -0.4f, exactDashDistance / 2f);
            }
            else
            {
                chargeIndicator.gameObject.SetActive(false);
            }
        }
    }

    private void HandleMovement(float speed)
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 moveDir = new Vector3(h, 0, v).normalized;
        if (moveDir.magnitude > 0.1f)
        {
            float moveStep = speed * Time.deltaTime;
            bool canMove = true;
            RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.4f, moveDir, moveStep + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            foreach (var hit in hits)
            {
                if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("DamageTrap"))
                {
                    canMove = false;
                    break;
                }
            }

            if (canMove) transform.Translate(moveDir * moveStep, Space.World);
        }
    }

    private void HandleMouseLook()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (groundPlane.Raycast(ray, out float enterDistance))
        {
            Vector3 hitPoint = ray.GetPoint(enterDistance);
            Vector3 lookDirection = hitPoint - transform.position;
            lookDirection.y = 0;

            if (lookDirection.sqrMagnitude > 0.01f) transform.forward = lookDirection;
        }
    }

    private void HandleCombatInput()
    {
        if (currentState == PlayerState.IdleWalk && Input.GetMouseButton(0))
        {
            currentState = PlayerState.Charging;
            currentChargeTime = 0f;
        }

        if (currentState == PlayerState.Charging)
        {
            if (Input.GetMouseButton(0))
            {
                float addedTime = Time.deltaTime * chargeSpeedMultiplier;
                currentChargeTime += addedTime;

                AddEnergy(Time.deltaTime);

                if (currentChargeTime >= maxChargeTime)
                {
                    currentChargeTime = maxChargeTime;
                    StartCoroutine(ExecuteDashSequence());
                }
            }
            else
            {
                StartCoroutine(ExecuteDashSequence());
            }
        }
    }

    private IEnumerator ExecuteDashSequence()
    {
        currentState = PlayerState.PreCast;
        yield return new WaitForSeconds(0.05f);

        currentState = PlayerState.Dashing;

        float dashDuration = 0.25f;
        float targetDistance = currentChargeTime * distancePerChargeSecond;
        float initialSpeed = (2f * targetDistance) / dashDuration;
        float timer = 0f;

        while (timer < dashDuration)
        {
            if (currentState != PlayerState.Dashing) yield break;

            float t = timer / dashDuration;
            float currentSpeed = Mathf.Lerp(initialSpeed, 0f, t);
            float moveStep = currentSpeed * Time.deltaTime;

            RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.4f, transform.forward, moveStep + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            bool isWallHit = false;
            foreach (var hit in hits)
            {
                if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("DamageTrap"))
                {
                    isWallHit = true;
                    break;
                }
            }

            if (isWallHit)
            {
                Debug.Log($"🧱 [破绽] 撞墙或陷阱！暴露破绽！");
                StopAllCoroutines();
                StartCoroutine(PostCastRoutine());
                yield break;
            }

            transform.Translate(Vector3.forward * moveStep, Space.Self);
            timer += Time.deltaTime;
            yield return null;
        }

        if (currentState == PlayerState.Dashing)
        {
            StartCoroutine(PostCastRoutine());
        }
    }

    private IEnumerator PostCastRoutine()
    {
        currentChargeTime = 0f;

        if (hasNoPostCastBuff)
        {
            currentState = PlayerState.IdleWalk;
            yield break;
        }

        currentState = PlayerState.PostCast;
        yield return new WaitForSeconds(0.2f);
        if (currentState == PlayerState.PostCast) currentState = PlayerState.IdleWalk;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("DamageTrap"))
        {
            if (currentState == PlayerState.HitStun || currentState == PlayerState.Dead) return;

            Debug.Log($"🔥 踩中伤害陷阱！受到 100 点伤害！");

            Vector3 knockbackDir = (transform.position - other.transform.position).normalized;
            if (knockbackDir.sqrMagnitude < 0.01f) knockbackDir = -transform.forward;
            knockbackDir.y = 0;

            TakeDamageAndStun(knockbackDir.normalized, 100);
            return;
        }

        if (other.CompareTag("Player"))
        {
            PlayerController enemy = other.GetComponent<PlayerController>();
            if (enemy == null) return;

            if (this.currentState == PlayerState.Dashing && enemy.currentState == PlayerState.Dashing)
            {
                Vector3 knockbackDir = (transform.position - other.transform.position).normalized;
                knockbackDir.y = 0;
                TakeDamageAndStun(knockbackDir, 15);
                return;
            }

            if (this.currentState == PlayerState.Dashing && enemy.currentState != PlayerState.Dashing && enemy.currentState != PlayerState.HitStun)
            {
                AddEnergy(1f);
                Vector3 knockbackDir = (enemy.transform.position - transform.position).normalized;
                knockbackDir.y = 0;

                enemy.TakeDamageAndStun(knockbackDir, 30);
                return;
            }
        }
    }

    public void TakeDamageAndStun(Vector3 knockbackDir, int baseDamage = 30)
    {
        if (isInvincible) return;

        if (currentState == PlayerState.Charging)
        {
            float compensation = currentChargeTime * 0.5f;
            AddEnergy(compensation);
        }

        StopAllCoroutines();
        currentState = PlayerState.HitStun;
        currentChargeTime = 0f;

        int finalDamage = Mathf.RoundToInt(baseDamage * damageTakenMultiplier);
        currentHp -= finalDamage;

        Debug.Log($"🩸 受到 {finalDamage} 点真实伤害！剩余: {currentHp} / {maxHp}");

        if (isLocalPlayer && HUDController.Instance != null)
        {
            HUDController.Instance.UpdateHP(currentHp, maxHp);
        }

        if (currentHp <= 0)
        {
            if (hasReviveBuff) TriggerRevival();
            else Die();
        }
        else
        {
            StartCoroutine(HitStunRoutine(knockbackDir));
        }
    }

    private void TriggerRevival()
    {
        hasReviveBuff = false;
        reviveCount++;

        int recoverHp = 150 / (int)Mathf.Pow(2, reviveCount - 1);
        if (recoverHp < 1) recoverHp = 1;

        currentHp = recoverHp;

        if (isLocalPlayer && HUDController.Instance != null)
            HUDController.Instance.UpdateHP(currentHp, maxHp);

        currentEnergy = 0f;
        EndUltimate();

        Debug.Log($"👼 复活甲生效！强行拦截死亡，恢复 {recoverHp} 点血量！获得 1 秒无敌！大招已终止！");

        StartCoroutine(InvincibilityRoutine());
    }

    private IEnumerator InvincibilityRoutine()
    {
        isInvincible = true;
        currentState = PlayerState.IdleWalk;
        yield return new WaitForSeconds(1f);
        isInvincible = false;
        Debug.Log("🛡️ 无敌时间结束！");
    }

    private IEnumerator HitStunRoutine(Vector3 knockbackDir)
    {
        float stunDuration = 0.5f;
        float timer = 0f;
        float knockbackInitialSpeed = 12f;

        while (timer < stunDuration)
        {
            float currentSpeed = Mathf.Lerp(knockbackInitialSpeed, 0, timer / stunDuration);
            float moveStep = currentSpeed * Time.deltaTime;

            if (Physics.SphereCast(transform.position, 0.4f, knockbackDir, out RaycastHit hit, moveStep + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.CompareTag("Wall") || hit.collider.CompareTag("DamageTrap")) moveStep = 0f;
            }

            transform.Translate(knockbackDir * moveStep, Space.World);
            timer += Time.deltaTime;
            yield return null;
        }

        currentState = PlayerState.IdleWalk;
    }

    private void AddEnergy(float amount)
    {
        if (isUltimateActive) return;

        currentEnergy += amount;
        if (currentEnergy > maxEnergy) currentEnergy = maxEnergy;

        if (isLocalPlayer && HUDController.Instance != null)
        {
            HUDController.Instance.UpdateEnergy(currentEnergy, maxEnergy);
        }
    }

    private void Die()
    {
        currentState = PlayerState.Dead;
        currentHp = 0;
        Debug.Log($"☠️ {gameObject.name} 被击杀，进入死亡结算！");

        transform.localScale = new Vector3(1, 0.2f, 1);
        transform.position -= new Vector3(0, 0.4f, 0);
    }
}