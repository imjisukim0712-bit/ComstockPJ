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

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool hasIsMovingParam;
    private bool wasMoving;
    private RobotData baseRobotData;
    private bool statsInitialized;

    private void Awake()
    {
        // 씬을 재시작해서 새 Player가 만들어질 때 이전 판의 게임오버/인벤토리/런 상태가 남아있지 않도록 초기화
        GameOverManager.Reset();
        GameWinManager.Reset();
        PlayerInventory.Reset();
        RunState.Reset();
        EnemyUnit.Alive.Clear();
        EnemyUnit.ResetStaticCaches();

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
    /// 체력이 0 이하가 되면 1회차 게임오버 처리를 한다.
    /// </summary>
    public void TakeDamage(int enemyAtk)
    {
        if (IsDead) return;

        float avoid_roll = Random.Range(0f, 100f);
        if (avoid_roll <= Avoid) return; // 회피 성공 - 데미지 계산식 자체가 발동하지 않음

        int dmg = Mathf.Max(0, enemyAtk - Def);
        CurrentHp = Mathf.Max(0, CurrentHp - dmg);

        if (CurrentHp <= 0) Die();
    }

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
        if (Keyboard.current == null) return; // 키보드 미인식 등 예외 방지

        float h = 0f;
        float v = 0f;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v += 1f;

        // X-Y 평면 위에서만 이동 (Z축은 사용하지 않음)
        moveInput = new Vector3(h, v, 0f);
        moveInput.Normalize();

        UpdateAnimation(h);
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
        rb.linearVelocity = moveInput * MoveSpeed; // MovePosition 대신 이걸로 교체
    }
}
