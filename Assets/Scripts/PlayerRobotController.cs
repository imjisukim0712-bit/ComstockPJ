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

    [Header("테스트용 설정 (로봇 선택 씬 없이 이 씬만 실행할 때 사용)")]
    [Tooltip("PlayerSession.SelectedRobotId가 -1(미선택)일 때 대신 사용할 로봇 ID")]
    [SerializeField] private int fallbackRobotIdForTesting = 0;

    [Header("애니메이션")]
    [Tooltip("이동 애니메이션을 재생할 Animator (비어 있으면 자신/자식에서 자동 탐색)")]
    [SerializeField] private Animator animator;
    [Tooltip("Animator Controller의 Bool 파라미터 이름")]
    [SerializeField] private string isMovingParam = "IsMoving";
    [Header("좌우 반전")]
    [Tooltip("좌우 이동 입력에 따라 Transform Scale.x 부호를 바꿔 이미지를 뒤집는다")]
    [SerializeField] private bool flipByScale = true;
    [Tooltip("오른쪽 이동일 때 Scale.x를 음수로 만든다. 끄면 왼쪽 이동일 때 음수가 된다")]
    [SerializeField] private bool negativeScaleOnRight = true;

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool hasIsMovingParam;
    private bool wasMoving;
    private float baseScaleX = 1f;

    private void Awake()
    {
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

        // 반전은 부호만 바꾸므로 원래 크기를 절댓값으로 기억
        baseScaleX = Mathf.Abs(transform.localScale.x);

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
        MaxHp = data.robot_hp;
        CurrentHp = data.robot_hp;
        Atk = data.robot_atk;
        Def = data.robot_def;
        MoveSpeed = data.robot_speed;
        Avoid = data.robot_avoid;
        Luck = data.robot_luck;
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

    /// <summary>좌우 입력에 따라 Scale.x 부호를 바꿔 캐릭터 이미지를 뒤집는다.</summary>
    private void UpdateFacing(float horizontal)
    {
        // 입력이 없으면 마지막으로 보던 방향을 유지
        if (!flipByScale || Mathf.Abs(horizontal) < 0.0001f) return;

        bool movingRight = horizontal > 0f;
        float sign = (movingRight == negativeScaleOnRight) ? -1f : 1f;
        float targetX = baseScaleX * sign;

        Vector3 s = transform.localScale;
        if (Mathf.Approximately(s.x, targetX)) return; // Y축만 눌렀을 때 등 불필요한 대입 방지

        s.x = targetX;
        transform.localScale = s;
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
