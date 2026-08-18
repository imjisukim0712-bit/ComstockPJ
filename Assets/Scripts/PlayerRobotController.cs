using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerRobotController : MonoBehaviour
{
    public int RobotId { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int Atk { get; private set; }
    public int Def { get; private set; }
    public float MoveSpeed { get; private set; }
    public float Avoid { get; private set; }
    public float Luck { get; private set; }
    public float Cc { get; private set; }       // robot_cc: 치명타 확률 (0~100)
    public float Cd { get; private set; }       // robot_cd: 치명타 데미지 배율 (예: 0.5 = +50%)
    public float Capacity { get; private set; } // robot_capacity: 무기 장탄수 증감 비율
    public float Reload { get; private set; }   // robot_reload: 무기 장전속도 증감 비율 (시트에 있지만 아직 사용처 없음)
    public float Mess { get; private set; }     // robot_mess: 로봇 질량 → Rigidbody.mass에 적용
    public int SpecialId { get; private set; }  // robot_special: 필살기 ID (실제 필살기 동작은 별도 시스템 필요)

    public bool IsDead { get; private set; }

    /// <summary>구르기(대시) 중인지. 이 동안에는 무적이라 TakeDamage가 통째로 무시된다.</summary>
    public bool IsDashing { get; private set; }

    /// <summary>
    /// 구르기 재사용 대기 비율. 0 = 지금 바로 쓸 수 있음, 1 = 방금 썼음.
    /// HUD의 구르기 아이콘이 이 값으로 덮개를 채운다(2026-08-13).
    /// </summary>
    public float DashCooldownRatio =>
        dashCooldown > 0f ? Mathf.Clamp01(dashCooldownLeft / dashCooldown) : 0f;

    /// <summary>구르기 중 캐릭터가 회전한 각도(도, 0~360). 무기도 이 값을 그대로 따라 돌아
    /// 몸과 무기가 같이 구르는 것처럼 보이게 한다(PlayerShootManager가 읽음).</summary>
    public float DashSpinDegrees { get; private set; }

    /// <summary>디스크 기획서(2026-08-12)의 처치 시/조건부/1회성 효과를 실행하는 컴포넌트.
    /// HitFlash와 같은 방식으로 Awake에서 자동으로 붙는다.</summary>
    public DiscEffectRuntime DiscEffects { get; private set; }

    [Header("구르기 (Space) - 전부 밸런스 미확정 임시값")]
    [Tooltip("한 번 구를 때 이동하는 거리(유닛)")]
    // 2026-08-12 사용자 지적 "PC 구르기 거리가 너무 김" - 이 값(5)은 2026-08-10 이미지 1/2 축소
    // 이전(몸이 지금의 2배였을 때) 튜닝된 값이라 몸 크기 대비 과도해졌다. 5 → 2.5.
    [SerializeField] private float dashDistance = 2.5f;
    [Tooltip("구르기가 지속되는 시간(초). 이 시간 동안 무적이다")]
    [SerializeField] private float dashDuration = 0.28f;
    [Tooltip("구르기 재사용 대기시간(초)")]
    [SerializeField] private float dashCooldown = 1.2f;

    [Header("적 밀어내기")]
    [Tooltip("이동 중 몸이 겹친 적을 밀어내는 속도(내 이동 속도에 대한 배율).\n" +
             "1이면 내가 가는 속도만큼 밀어낸다. 좀비가 다시 다가오는 속도보다 커야 실제로 밀린다")]
    [SerializeField] private float enemyPushSpeedRatio = 1.2f;

    [Header("테스트용 설정 (로봇 선택 씬 없이 이 씬만 실행할 때 사용)")]
    [Tooltip("PlayerSession.SelectedRobotId가 -1(미선택)일 때 대신 사용할 로봇 ID")]
    [SerializeField] private int fallbackRobotIdForTesting = 0;

    [Header("애니메이션")]
    [Tooltip("이동 애니메이션을 재생할 Animator (비어 있으면 자신/자식에서 자동 탐색)")]
    [SerializeField] private Animator animator;
    [Tooltip("Animator Controller의 Bool 파라미터 이름")]
    [SerializeField] private string isMovingParam = "IsMoving";
    [Header("좌우 반전")]
    [Tooltip("좌우 이동 입력에 따라 SpriteRenderer.flipX로 이미지를 뒤집는다.\n" +
             "* Transform Scale은 절대 건드리지 않는다 - Player 자신의 BoxCollider나 자식(무기 앵커)들의 " +
             "Collider가 음수 스케일이 되어 'BoxCollider does not support negative scale' 경고가 나는 문제를 " +
             "근본적으로 없앤다. 자식 위치도 전혀 흔들리지 않는다.")]
    [SerializeField] private bool flipSprite = true;
    [Tooltip("오른쪽 이동일 때 flipX를 켠다(뒤집는다). 끄면 왼쪽 이동일 때 켜진다")]
    [SerializeField] private bool flipXOnRight = true;
    [Tooltip("뒤집을 SpriteRenderer. 비어 있으면 자신/자식에서 자동 탐색")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("맵 경계")]
    [Tooltip("맵(배경 스프라이트) 가장자리에서 이만큼 안쪽까지만 이동할 수 있다. 몸이 배경 밖으로 반쯤 걸치지 않도록 하는 여유값")]
    [SerializeField] private float mapEdgeMargin = 0.8f;

    [Header("구르기 연출")]
    [Tooltip("구르는 동안 몸을 회전시킬 절차적 리그. 비어 있으면 자식에서 자동 탐색")]
    [SerializeField] private ProceduralCharacterRig proceduralRig;

    [Header("피격 넉백")]
    [Tooltip("피격당했을 때 공격자 반대 방향으로 밀려나는 속도(유닛/초)")]
    [SerializeField] private float knockbackStrength = 3.5f;
    [Tooltip("넉백 속도가 초당 이만큼씩 감쇠한다(EnemyUnit의 감쇠와 비슷한 오더)")]
    [SerializeField] private float knockbackDecay = 30f;
    private Vector3 knockbackVelocity;

    private Rigidbody rb;
    private HitFlash hitFlash;
    private PlayerHitFeedback hitFeedback;
    private Vector3 moveInput;
    private bool hasIsMovingParam;
    private bool wasMoving;
    private RobotData baseRobotData;
    private bool statsInitialized;

    // 마지막으로 향한 이동 방향. 제자리에서 Space를 눌렀을 때 구를 방향으로 쓴다.
    private Vector3 lastMoveDirection = Vector3.right;
    private Vector3 dashDirection;
    private float dashTimeLeft;
    private float dashCooldownLeft;

    // ── 디스크 기획서(2026-08-12) 관련 상태 ──────────────────────────
    // 시간제 임시 스탯 보너스(광분 바이러스/교향곡:파도/공명의 소리/마지막 발악). RunState의
    // 영구 보너스 체계와 분리해 여기서 직접 만료시각까지만 유지한다 - 몇 초짜리 버프 때문에
    // RunState.NotifyChanged를 계속 유발하며 전체 스탯을 재계산하지 않아도 되게 하려는 목적.
    private readonly List<(StatType stat, float amount, float expireTime)> tempStatBonuses = new List<(StatType, float, float)>();

    // "위장 디스크" - 공격 중이 아닐 때만 붙는 이동속도 보너스. DiscEffectRuntime이 매 프레임
    // 최신값으로 덮어쓴다(조건이 사라지면 즉시 0으로 돌아와야 하므로 만료시각 방식을 쓰지 않는다).
    private float conditionalMoveSpeedBonus;

    // "에너지 베리어 디스크" - 회복되지 않는 여분 체력. 웨이브가 시작될 때마다 가득 채워진다.
    private int shieldMaxHp;
    private int shieldHp;

    // "마지막 발악 디스크" - 발동 중 무적 종료 시각(0이면 비활성).
    private float lastStandInvulnUntil;

    // 정비 화면에서 필드를 초기화할 때 되돌아갈 시작 위치(Awake 시점의 위치).
    private Vector3 startPosition;

    private void Awake()
    {
        // 씬을 재시작해서 새 Player가 만들어질 때 이전 판의 게임오버/런 상태가 남아있지 않도록 초기화
        GameOverManager.Reset();
        GameWinManager.Reset();
        RunState.Reset();
        EnemyUnit.Alive.Clear();
        EnemyUnit.ResetStaticCaches();
        RewardPickup.ResetStaticCaches();
        GameFlowManager.ResetStaticState();
        MapBounds.ResetCache();
        HitFlash.ResetStaticCaches();

        startPosition = transform.position;

        rb = GetComponent<Rigidbody>();

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogWarning("Animator를 찾을 수 없어 이동 애니메이션이 재생되지 않습니다. Player에 Animator를 추가하고 PlayerAnimator 컨트롤러를 지정하세요.");
        }
        else
        {
            hasIsMovingParam = HasParameter(animator, isMovingParam);
            if (!hasIsMovingParam)
                Debug.LogWarning($"Animator Controller에 Bool 파라미터 '{isMovingParam}'이(가) 없습니다.");
        }

        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogWarning("SpriteRenderer를 찾을 수 없어 좌우 반전이 동작하지 않습니다.");
        }

        if (proceduralRig == null) proceduralRig = GetComponentInChildren<ProceduralCharacterRig>();

        // 피격 시 0.25초 흰색. 절차적 리그가 몸통/다리 스프라이트를 런타임에 만들어 붙이므로
        // HitFlash는 첫 피격 때 대상 렌더러를 다시 수집한다(HitFlash.Play() 참고).
        hitFlash = GetComponent<HitFlash>();
        if (hitFlash == null) hitFlash = gameObject.AddComponent<HitFlash>();

        DiscEffects = GetComponent<DiscEffectRuntime>();
        if (DiscEffects == null) DiscEffects = gameObject.AddComponent<DiscEffectRuntime>();

        // 피격 시 카메라 흔들림·화면 비네트·효과음(2026-08-14). HitFlash/DiscEffects와 동일하게
        // 자동 부착한다.
        hitFeedback = GetComponent<PlayerHitFeedback>();
        if (hitFeedback == null) hitFeedback = gameObject.AddComponent<PlayerHitFeedback>();

        rb.isKinematic = false; // 물리 충돌이 필요하므로 false로
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.constraints |= RigidbodyConstraints.FreezePositionZ;
        rb.linearDamping = 0f; // 관성(미끄러짐) 최소화 - 버전에 따라 rb.drag일 수 있음
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    private void Start()
    {
        if (GameDataManager.Instance.IsLoaded) InitFromSession();
        else GameDataManager.Instance.OnLoaded += InitFromSession;
    }

    private void OnEnable() => RunState.OnChanged += HandleRunStateChanged;
    private void OnDisable() => RunState.OnChanged -= HandleRunStateChanged;

    private void InitFromSession()
    {
        int robotId = PlayerSession.SelectedRobotId;

        // 로봇 선택 씬을 거치지 않고 이 씬을 바로 실행한 경우 (에디터 테스트 등)
        // SelectedRobotId가 -1(미선택 기본값)로 남아있을 수 있으므로 폴백 처리
        if (robotId == -1)
        {
            Debug.LogWarning($"선택된 로봇이 없어 테스트용 기본 로봇ID {fallbackRobotIdForTesting}(으)로 대체합니다.");
            robotId = fallbackRobotIdForTesting;
            PlayerSession.SelectedRobotId = robotId; // 이후 다른 스크립트도 동일한 값을 참조하도록 동기화
        }

        if (!GameDataManager.Instance.Robots.TryGetValue(robotId, out RobotData data))
        {
            Debug.LogWarning($"선택된 로봇ID {robotId}의 데이터를 찾을 수 없습니다. GameDataManager.Robots에 해당 ID의 CSV 데이터가 있는지 확인하세요.");
            return;
        }

        RobotId = data.robot_id;
        baseRobotData = data;
        Capacity = data.robot_capacity; // 탄약 제거로 더 이상 게임플레이에 쓰이지 않음(데이터만 보존)
        Reload = data.robot_reload;
        SpecialId = data.robot_special;

        ApplyAggregatedStats(isInitialApply: true);
        statsInitialized = true;
    }

    // AI 코어 업그레이드 선택 등으로 RunState.CoreStatBonuses가 바뀔 때마다 최종 스탯을 다시 계산한다.
    private void HandleRunStateChanged()
    {
        if (statsInitialized) ApplyAggregatedStats(isInitialApply: false);
    }

    // RobotStats.Compute()로 머리(로봇) 기본값 + AI 코어 누적 보너스를 합산해 반영한다.
    // 최대 체력이 늘어나면 그만큼 현재 체력도 함께 채워준다(로그라이크 통상 관례).
    private void ApplyAggregatedStats(bool isInitialApply)
    {
        AggregatedRobotStats stats = RobotStats.Compute(baseRobotData);

        int previousMaxHp = MaxHp;
        MaxHp = stats.MaxHp;
        Atk = stats.Atk;
        Def = stats.Def;
        MoveSpeed = stats.MoveSpeed;
        Avoid = stats.Avoid;
        Luck = stats.Luck;
        Cc = stats.Cc;
        Cd = stats.Cd;
        Mess = stats.Mess;

        CurrentHp = isInitialApply ? MaxHp : Mathf.Min(MaxHp, CurrentHp + Mathf.Max(0, MaxHp - previousMaxHp));

        // 로봇 질량(robot_mess)을 Rigidbody에 반영. 0/미설정이면 물리 계산이 깨지므로 기존 값 유지
        if (rb != null && Mess > 0f) rb.mass = Mess;
    }

    /// <summary>
    /// 적의 공격력(enemyAtk)을 받아 피격 처리한다.
    /// 1) robot_avoid(회피 확률) 판정: 0~100 랜덤값이 회피 확률 이하면 데미지 계산 자체를 하지 않는다.
    /// 2) 회피 실패 시 받는 데미지 = 적의 공격력 - robot_def(방어력) (0 미만으로는 내려가지 않음)
    /// 3) "에너지 베리어" 등 실드가 있으면 먼저 실드부터 깎인다.
    /// 4) 이 데미지로 체력이 0 이하가 되는 순간 "마지막 발악" 디스크가 남아있으면 대신 발동한다.
    /// 체력이 0 이하가 되면 1회차 게임오버 처리를 한다.
    /// </summary>
    public void TakeDamage(int enemyAtk, Vector3? attackerPosition = null)
    {
        if (IsDead) return;
        if (IsDashing) return; // 구르기 중에는 무적 - 회피 판정조차 하지 않는다
        if (Time.time < lastStandInvulnUntil) return; // 마지막 발악 무적 구간

        float effective_avoid = Avoid + GetTempStatBonus(StatType.Avoid);
        float avoid_roll = Random.Range(0f, 100f);
        if (avoid_roll <= effective_avoid) return; // 회피 성공 - 데미지 계산식 자체가 발동하지 않음

        // 방어력이 적 공격력보다 높아도 <b>반드시 1은 들어간다</b>(2026-08-13 사용자 지정).
        // 예전에는 하한이 0이라 방어력만 올려두면 특정 적에게 완전 무적이 됐다 - 방어력
        // 업그레이드 몇 번으로 난이도가 통째로 무너지던 원인이다. 적 쪽 피해 계산
        // (EnemyUnit.TakeDamage)은 원래부터 최소 1이었으므로 이제 양쪽 규칙이 같다.
        float effective_def = Def + GetTempStatBonus(StatType.Def);
        int dmg = Mathf.Max(1, enemyAtk - Mathf.RoundToInt(effective_def));

        if (shieldHp > 0)
        {
            int absorbed = Mathf.Min(shieldHp, dmg);
            shieldHp -= absorbed;
            dmg -= absorbed;
        }

        int new_hp = CurrentHp - dmg;

        if (new_hp <= 0 && DiscEffects != null && DiscEffects.TryTriggerLastStand(out float speedBonusRatio, out float invulnDuration))
        {
            CurrentHp = 1;
            lastStandInvulnUntil = Time.time + invulnDuration;
            ApplyTempStatBonus(StatType.MoveSpeed, MoveSpeed * speedBonusRatio, invulnDuration);
            if (hitFlash != null) hitFlash.Play();
            ApplyHitKnockback(attackerPosition);
            hitFeedback?.OnHit(dmg, attackerPosition);
            return;
        }

        CurrentHp = Mathf.Max(0, new_hp);

        if (hitFlash != null) hitFlash.Play(); // 피격 시 0.25초 흰색
        ApplyHitKnockback(attackerPosition);
        hitFeedback?.OnHit(dmg, attackerPosition);

        if (CurrentHp <= 0) Die();
    }

    /// <summary>
    /// 피격 시 공격자 반대 방향으로 짧게 밀려난다(2026-08-14). 세기를 누적하지 않고 덮어써서
    /// 연타를 맞아도 무한 가속하지 않는다(EnemyUnit.ApplyKnockback과 동일 정책). 방향을 알 수
    /// 없거나(attackerPosition 미전달) 완전히 겹친 경우는 조용히 생략한다.
    /// </summary>
    private void ApplyHitKnockback(Vector3? attackerPosition)
    {
        if (knockbackStrength <= 0f || !attackerPosition.HasValue) return;

        Vector3 diff = transform.position - attackerPosition.Value;
        diff.z = 0f;
        if (diff.sqrMagnitude <= 0.0001f) return;

        knockbackVelocity = diff.normalized * knockbackStrength;
    }

    /// <summary>디스크(포근한 치유/이끼 낀 등)의 처치·주기 회복 효과가 호출한다. 최대 체력을 넘지 않는다.</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
    }

    /// <summary>duration초 동안 stat에 amount를 더한다(같은 스탯에 여러 개가 겹치면 합산된다).
    /// 광분 바이러스/교향곡:파도/공명의 소리/마지막 발악 등 시간제 디스크 효과가 사용한다.</summary>
    public void ApplyTempStatBonus(StatType stat, float amount, float duration)
    {
        tempStatBonuses.Add((stat, amount, Time.time + duration));
    }

    /// <summary>stat에 현재 유효한 임시 보너스 합계. 만료된 항목은 조회하면서 함께 정리한다.</summary>
    public float GetTempStatBonus(StatType stat)
    {
        float total = 0f;
        for (int i = tempStatBonuses.Count - 1; i >= 0; i--)
        {
            if (Time.time >= tempStatBonuses[i].expireTime) { tempStatBonuses.RemoveAt(i); continue; }
            if (tempStatBonuses[i].stat == stat) total += tempStatBonuses[i].amount;
        }
        return total;
    }

    /// <summary>"위장 디스크" 전용 - DiscEffectRuntime이 매 프레임 최신값으로 덮어쓴다.</summary>
    public void SetConditionalMoveSpeedBonus(float amount) => conditionalMoveSpeedBonus = amount;

    /// <summary>"에너지 베리어 디스크" 전용 - 웨이브 시작마다 DiscEffectRuntime이 상한을 다시 설정한다.</summary>
    public void SetShieldMaxHp(int amount) => shieldMaxHp = Mathf.Max(0, amount);

    /// <summary>실드를 상한까지 가득 채운다(웨이브 시작 시 호출).</summary>
    public void RefillShield() => shieldHp = shieldMaxHp;

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (rb != null) rb.linearVelocity = Vector3.zero;
        GameOverManager.TriggerGameOver();

        enabled = false; // 이동/애니메이션 처리(Update, FixedUpdate) 중단
    }

    private void Update()
    {
        // 정비(AI 코어/로봇 정비/상점) 화면이 열려 있는 동안에는 조작을 완전히 막는다.
        // Time.timeScale=0으로도 이동은 멈추지만, 입력 폴링과 애니메이션까지 확실히 정지시켜
        // "인게임이 뒤에서 계속 진행되는 것처럼 보이는" 상태를 없앤다.
        if (GameFlowManager.IsIntermission || GameFlowManager.IsPaused || GameOverManager.IsGameOver || GameWinManager.IsGameWon)
        {
            moveInput = Vector3.zero;
            UpdateAnimation(0f);
            return;
        }

        if (Keyboard.current == null) return; // 키보드 미인식 등 예외 방지

        UpdateDashTimers();

        float h = 0f;
        float v = 0f;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v += 1f;

        // X-Y 평면 위에서만 이동 (Z축은 사용하지 않음)
        moveInput = new Vector3(h, v, 0f);
        moveInput.Normalize();

        if (moveInput.sqrMagnitude > 0.0001f) lastMoveDirection = moveInput;

        if (Keyboard.current.spaceKey.wasPressedThisFrame) TryStartDash();

        UpdateAnimation(h);
    }

    // 구르기 지속시간/쿨다운을 흘려보낸다. 지속시간이 끝나면 무적도 함께 풀린다.
    // 구르는 동안에는 캐릭터가 실제로 한 바퀴(0~360도) 도는 것처럼 회전각을 계산해
    // 절차적 리그(다리를 숨기고 몸통만 회전)와 무기(PlayerShootManager가 DashSpinDegrees를
    // 읽어 같은 각도로 맞춘다)에 전달한다.
    private void UpdateDashTimers()
    {
        if (dashCooldownLeft > 0f) dashCooldownLeft -= Time.deltaTime;

        if (!IsDashing)
        {
            DashSpinDegrees = 0f;
            if (proceduralRig != null) proceduralRig.SetRoll(false, 0f);
            return;
        }

        dashTimeLeft -= Time.deltaTime;

        // 굴러가는 방향(dashDirection.x)에 맞춰 회전 방향을 정한다 - 실제 바퀴처럼 오른쪽으로
        // 구르면 시계방향(-), 왼쪽으로 구르면 반시계방향(+)으로 돌아야 진행 방향과 맞아 보인다.
        // 순수 수직 이동(x=0에 가까움)은 기존처럼 시계방향을 기본값으로 쓴다.
        float spinSign = dashDirection.x < -0.0001f ? 1f : -1f;

        float progress01 = dashDuration > 0f ? 1f - Mathf.Clamp01(dashTimeLeft / dashDuration) : 1f;
        DashSpinDegrees = spinSign * progress01 * 360f;
        if (proceduralRig != null) proceduralRig.SetRoll(true, DashSpinDegrees);

        if (dashTimeLeft <= 0f) IsDashing = false;
    }

    /// <summary>
    /// 기획서의 "Space → 구르기". 이동 입력 방향으로(제자리면 마지막으로 향하던 방향으로)
    /// 짧고 빠르게 이동하며, 구르는 동안은 무적이다.
    /// </summary>
    private void TryStartDash()
    {
        if (IsDashing || dashCooldownLeft > 0f || dashDuration <= 0f) return;

        dashDirection = moveInput.sqrMagnitude > 0.0001f ? moveInput : lastMoveDirection;
        IsDashing = true;
        dashTimeLeft = dashDuration;
        dashCooldownLeft = dashCooldown;
    }

    /// <summary>
    /// 정비 화면에 들어갈 때 플레이어를 시작 위치로 되돌린다(필드 초기화).
    /// GameFlowManager가 호출한다.
    /// </summary>
    public void ReturnToStartPosition()
    {
        IsDashing = false;
        dashTimeLeft = 0f;
        DashSpinDegrees = 0f;
        if (proceduralRig != null) proceduralRig.SetRoll(false, 0f);
        moveInput = Vector3.zero;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.position = startPosition;
        }
        transform.position = startPosition;
    }

    private void UpdateAnimation(float horizontal)
    {
        // 입력이 있고 실제로 움직일 수 있는 상태일 때만 '이동 중'으로 판단
        bool isMoving = moveInput.sqrMagnitude > 0.0001f && MoveSpeed > 0f;

        if (isMoving != wasMoving)
        {
            if (animator != null && hasIsMovingParam) animator.SetBool(isMovingParam, isMoving);
        }
        wasMoving = isMoving;

        UpdateFacing(horizontal);
    }

    /// <summary>좌우 입력에 따라 SpriteRenderer.flipX로 캐릭터 이미지를 뒤집는다. Transform은 건드리지 않는다.</summary>
    private void UpdateFacing(float horizontal)
    {
        // 입력이 없으면 마지막으로 보던 방향을 유지
        if (!flipSprite || spriteRenderer == null || Mathf.Abs(horizontal) < 0.0001f) return;

        bool movingRight = horizontal > 0f;
        spriteRenderer.flipX = (movingRight == flipXOnRight);
    }

    private static bool HasParameter(Animator anim, string paramName)
    {
        if (anim.runtimeAnimatorController == null || string.IsNullOrEmpty(paramName)) return false;

        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == paramName) return true;
        }
        return false;
    }

    private void FixedUpdate()
    {
        // 넉백은 매 프레임 감쇠한다(EnemyUnit의 넉백 감쇠와 동일한 방식). 구르기 중에는 아래에서
        // 더하지 않지만, 감쇠 자체는 계속 진행시켜야 구르기가 끝난 뒤 잔여 넉백이 자연스럽게
        // 이어진다(팝핑 방지).
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDecay * Time.fixedDeltaTime);

        // 구르기 중에는 입력과 무관하게 정해진 방향으로 고정 속도(거리/지속시간)로 밀려난다.
        if (IsDashing && dashDuration > 0f)
        {
            rb.linearVelocity = dashDirection * (dashDistance / dashDuration);
        }
        else
        {
            // 디스크 기획서(2026-08-12) - 광분 바이러스/마지막 발악(시간제) + 위장(조건부, 매 프레임 갱신)의
            // 이동속도 보너스를 함께 반영한다.
            float effective_move_speed = MoveSpeed + GetTempStatBonus(StatType.MoveSpeed) + conditionalMoveSpeedBonus;
            rb.linearVelocity = moveInput * effective_move_speed + knockbackVelocity; // MovePosition 대신 이걸로 교체
        }

        PushOverlappingEnemies();
        ClampToMap();
    }

    /// <summary>
    /// 이동 중 몸이 겹친 적을 밀어낸다(2026-08-10 사용자 요청: "로봇은 이동할 때 좀비를 밀어낼
    /// 수 있어야 함").
    ///
    /// <b>물리 충돌만으로는 절대 밀리지 않는다.</b> <see cref="EnemyUnit.FixedUpdate"/>가 매
    /// 프레임 Rigidbody 속도를 통째로 덮어쓰기 때문에, 충돌로 밀려나도 바로 다음 프레임에
    /// 원래대로 돌아간다. 그래서 적의 이동 속도에 더해질 성분(<see cref="EnemyUnit.ApplyPlayerPush"/>)
    /// 으로 직접 넣어준다.
    ///
    /// 겹친 깊이에 비례해 밀어내므로 살짝 스치면 약하게, 깊이 파고들면 강하게 밀린다.
    /// 구르기 중에도 그대로 적용되어 좀비 무리를 뚫고 지나갈 수 있다.
    /// </summary>
    private void PushOverlappingEnemies()
    {
        Vector3 velocity = rb.linearVelocity;
        if (velocity.sqrMagnitude < 0.0001f) return; // 멈춰 있으면 밀지 않는다

        float myRadius = PushBodyRadius();
        float pushSpeed = velocity.magnitude * enemyPushSpeedRatio;

        foreach (EnemyUnit enemy in EnemyUnit.Alive)
        {
            if (enemy == null || enemy.IsDead) continue;

            Vector3 diff = enemy.transform.position - transform.position;
            diff.z = 0f;

            float dist = diff.magnitude;
            float minDist = myRadius + enemy.BodyRadius;
            if (dist >= minDist) continue; // 몸이 안 닿았으면 밀지 않는다

            // 완전히 겹쳐 방향을 못 구하면 진행 방향으로 밀어낸다.
            Vector3 dir = dist > 0.0001f ? diff / dist : velocity.normalized;
            float depth = (minDist - dist) / minDist; // 파고든 비율 0~1

            enemy.ApplyPlayerPush(dir * (pushSpeed * depth));
        }
    }

    /// <summary>밀어내기 판정에 쓰는 내 몸 반지름. EnemyUnit.BodyRadius와 같은 기준(짧은 축)을 쓴다.</summary>
    private float PushBodyRadius()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return 0.5f;
        return Mathf.Max(0.05f, Mathf.Min(col.bounds.extents.x, col.bounds.extents.y));
    }

    /// <summary>
    /// 플레이어를 맵(배경 스프라이트) 안으로 가둔다. 배경 밖은 아무것도 그려지지 않은 빈
    /// 공간이라 나가면 곧바로 화면이 비어 보인다(2026-08-10 배경을 원본 스케일로 되돌리며 신설).
    ///
    /// 속도를 미리 자르지 않고 <b>위치를 잘라낸 뒤 그 축의 속도를 죽이는</b> 방식을 쓴다 -
    /// 구르기는 속도를 통째로 덮어쓰기 때문에 속도 쪽에서 막으면 벽에 붙은 채로 계속
    /// 밀리는 상태가 되고, 위치를 자르면 어떤 이동 수단이든 똑같이 막힌다.
    /// </summary>
    private void ClampToMap()
    {
        if (!MapBounds.HasBounds) return;

        Vector3 current = rb.position;
        Vector3 clamped = MapBounds.ClampPosition(current, mapEdgeMargin);
        if (clamped == current) return;

        // 경계에 닿은 축의 속도만 0으로 만들어 미끄러짐(벽을 따라 흐르는 이동)은 그대로 살린다.
        Vector3 v = rb.linearVelocity;
        if (!Mathf.Approximately(clamped.x, current.x)) v.x = 0f;
        if (!Mathf.Approximately(clamped.y, current.y)) v.y = 0f;
        rb.linearVelocity = v;

        rb.position = clamped;
    }
}
