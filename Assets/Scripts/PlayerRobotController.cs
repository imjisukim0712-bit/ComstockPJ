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

    private Rigidbody rb;
    private Vector3 moveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
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
    }

    private void FixedUpdate()
    {
    rb.linearVelocity = moveInput * MoveSpeed; // MovePosition 대신 이걸로 교체
    }
}
