using UnityEngine;

/// <summary>
/// 스프라이트 조각(몸통 / 허벅지 / 정강이 / 발)을 코드로 조립하고, 관절 각도를 매 프레임
/// 계산해서 움직이는 절차적(procedural) 리그.
///
/// 애니메이션 클립을 재생하지 않는다. 실제 이동 속도에서 보행 주파수를 뽑고, 발이 지면을
/// 밟는 궤적을 만든 뒤 2관절 IK로 허벅지/정강이 각도를 역산한다. 그래서
/// - 속도가 변하면 보폭·보행 속도가 그대로 따라오고(발이 미끄러지지 않는다)
/// - 보폭·발 높이·몸통 바운스 같은 값을 인스펙터에서 즉시 바꿔볼 수 있으며
/// - 프레임 수만큼 스프라이트를 그릴 필요가 없다.
///
/// 사용법
///   1) 빈 GameObject에 이 컴포넌트를 붙인다(스프라이트를 비워두면 Resources/Parts에서 자동 로드).
///   2) 부모/자신에 Rigidbody가 있으면 그 속도를 자동으로 읽는다.
///      없으면 <see cref="SetLocomotion"/>으로 속도를 직접 넣어준다.
///
/// 주의: 좌우 반전은 몸통(머리)과 다리를 서로 다른 방식으로 처리한다.
/// - 몸통은 SpriteRenderer.flipX로 즉시 뒤집는다(스케일을 뒤집으면 회전 중간에 납작하게
///   찌그러져 보이는 문제가 있어서 피했다).
/// - 다리는 LegsGroup이라는 별도 자식의 localScale.x 부호로 뒤집는다(크기는 항상 1이라
///   찌그러지지 않고 즉시 전환된다).
/// 이 리그 밑(특히 LegsGroup 아래)에는 Collider를 두지 말 것(프로젝트 안내.md의
/// "음수 스케일 + Collider" 경고와 같은 이유). 무기 앵커 같은 Collider가 필요한 오브젝트는
/// 이 컴포넌트가 붙은 오브젝트의 형제로 둔다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public class ProceduralCharacterRig : MonoBehaviour
{
    private const string ResourceFolder = "Parts/";

    // Resources/Parts의 실제 원본. 인스펙터 직렬화값이나 런타임 참조가 서로 바뀌어도 이 기준으로
    // 되돌릴 수 있도록 클래스 전체에서 하나씩만 캐시한다.
    private static Sprite expectedBodySprite;
    private static Sprite expectedThighSprite;
    private static Sprite expectedShinSprite;
    private static Sprite expectedFootSprite;

    [Header("스프라이트 (비워두면 Resources/Parts에서 자동 로드)")]
    [SerializeField] private Sprite bodySprite;
    [SerializeField] private Sprite thighSprite;
    [SerializeField] private Sprite shinSprite;
    [SerializeField] private Sprite footSprite;

    [Header("리그 전체")]
    [Tooltip("스프라이트 원본이 커서(몸통 250px) 그대로 쓰면 화면을 덮는다. 리그 루트에 곱하는 배율")]
    [SerializeField] private float rigScale = 0.55f;
    [Tooltip("다리 파츠 전체에 한 번 더 곱하는 배율. 기본 1 = 파츠별 배율을 그대로 쓴다.\n" +
             "다리를 통째로 키우거나 줄여보고 싶을 때만 건드린다(데모 슬라이더용)")]
    [SerializeField] private float legScale = 1.0f;

    [Header("파츠별 배율 — 원본 이미지의 '외곽선 두께'를 몸통과 맞추는 값")]
    [Tooltip("파츠마다 원본 PNG의 해상도가 달라서(몸통 250px / 허벅지 98px / 정강이 74px / 신발 117px)\n" +
             "전부 같은 배율로 그리면 외곽선(검은 테두리) 두께가 제각각이 된다. 원본 이미지는 외곽선\n" +
             "두께가 같아 보이도록 그려졌으므로, 각 파츠를 '외곽선 두께가 몸통과 같아지는 배율'로\n" +
             "그려야 원래 의도한 크기가 된다.\n\n" +
             "기본값은 원본 PNG의 외곽선 두께를 픽셀 단위로 실측해 역산한 값이다(2026-08-10):\n" +
             "  몸통 4.27px / 허벅지 9.06px / 정강이 7.42px / 신발 9.84px\n" +
             "  → 허벅지 4.27/9.06=0.471, 정강이 4.27/7.42=0.576, 신발 4.27/9.84=0.434\n" +
             "예전에는 이 셋을 legScale 하나(0.30)로 뭉뚱그려서 다리가 의도보다 훨씬 얇고 작았다.")]
    [SerializeField] private float thighScale = 0.471f;
    [SerializeField] private float shinScale = 0.576f;
    [SerializeField] private float footScale = 0.434f;
    [Tooltip("두 고관절 사이의 거리(유닛, 몸통과 같은 로컬 스케일 = 몸통 폭 2.5유닛 기준).\n" +
             "2026-08-10 파츠 배율을 원본 기준으로 바로잡으면서 다리가 커져, 예전 값(0.7)으로는\n" +
             "발이 몸통 폭 밖으로 벌어졌다. 사용자 레퍼런스처럼 두 다리가 몸통 아래에 나란히\n" +
             "오도록 0.55로 줄였다")]
    [SerializeField] private float hipSeparation = 0.55f;
    [Tooltip("다리뼈(허벅지+정강이)를 완전히 폈을 때 대비 골반 높이 비율. 1보다 작으면 무릎이 굽는다.\n" +
             "발목~밑창 높이는 항상 그대로 더해지므로 이 값은 순수하게 '무릎을 얼마나 굽힐지'만 정한다.\n" +
             "1에 가까울수록 다리가 곧게 서지만, 그만큼 발을 앞뒤로 뻗을 여유가 줄어 보폭이 잘린다")]
    [SerializeField] private float hipHeightRatio = 0.96f;
    [Tooltip("대기 자세에서 두 발을 앞뒤로 벌리는 폭. 다리 길이에 대한 배수다.\n" +
             "보행 중에는 쓰이지 않으므로(gaitBlend=0일 때만 적용) 보폭에 영향을 주지 않는다")]
    [SerializeField] private float idleStanceRatio = 0.05f;
    [Tooltip("앞쪽 다리(Leg_Front)의 고관절(다리 뿌리) 자체를 뒤쪽 다리 쪽(중심)으로 살짝\n" +
             "당긴다. hipSeparation과 같은 로컬 단위. 뒤쪽 다리는 건드리지 않는다 - 대기/보행\n" +
             "구분 없이 다리 전체(뼈·발 모두)가 그만큼 이동한다")]
    [SerializeField] private float frontLegPullBack = 0.06f;
    [Tooltip("발 스프라이트의 발끝이 왼쪽을 향하도록 그려져 있어, 기본 방향(+X)에 맞추려면 뒤집어야 한다.\n" +
             "두 발 모두 항상 이 방향(=진행 방향)을 향한다 — 사람처럼 두 발이 서로 다른 쪽을 보지 않는다")]
    [SerializeField] private bool footSpriteFlipX = true;
    [Tooltip("뒤쪽 다리에 곱하는 색. 살짝 어둡게 해서 앞뒤 깊이감을 준다")]
    [SerializeField] private Color backLegTint = new Color(0.80f, 0.80f, 0.83f, 1f);
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int bodySortingOrder = 0;

    [Header("관절 앵커 (스프라이트 안 정규화 좌표, 좌하단 = 0,0)")]
    [Tooltip("몸통에서 골반(다리가 붙는 지점)")]
    [SerializeField] private Vector2 bodyHipAnchor = new Vector2(0.50f, 0.21f);
    [Tooltip("허벅지에서 고관절(윗끝)")]
    [SerializeField] private Vector2 thighHipAnchor = new Vector2(0.52f, 0.84f);
    [Tooltip("허벅지에서 무릎(공 모양 관절의 중심)")]
    [SerializeField] private Vector2 thighKneeAnchor = new Vector2(0.32f, 0.32f);
    [Tooltip("정강이에서 무릎(윗끝)")]
    [SerializeField] private Vector2 shinKneeAnchor = new Vector2(0.48f, 0.82f);
    [Tooltip("정강이에서 발목(아랫끝)")]
    [SerializeField] private Vector2 shinAnkleAnchor = new Vector2(0.48f, 0.15f);
    [Tooltip("발에서 발목(정강이가 꽂히는 지점). 신발 스프라이트 안에서 이 지점에 정강이 끝이 온다.\n" +
             "값이 높으면(≈0.88 = 신발 입구) 정강이와 신발이 '따로 떨어져' 보이고, 값을 낮춰 신발 안쪽에\n" +
             "두면 정강이 끝이 신발 통에 파묻혀 '신발을 신은' 모습이 된다(신발이 정강이보다 위에 그려짐).\n" +
             "2026-08-10 사용자 지정으로 0.88 → 0.62")]
    [SerializeField] private Vector2 footAnkleAnchor = new Vector2(0.55f, 0.62f);
    [Tooltip("발 스프라이트에서 바닥에 닿는 밑창 높이")]
    [SerializeField] private float footSoleY = 0.16f;

    [Header("보행 — 길이 계열은 전부 '다리 길이에 대한 배수'다")]
    [Tooltip("한 다리가 한 사이클을 도는 동안 몸이 전진하는 거리. 보행 주파수 = 속도 / 실제 보폭.\n" +
             "배수로 두면 legScale이나 그림이 바뀌어도 보행이 저절로 따라간다")]
    [SerializeField] private float strideRatio = 1.0f;
    [Tooltip("한 사이클 중 발이 땅에 붙어 있는 비율(0~1). 나머지는 공중(스윙)")]
    [Range(0.3f, 0.85f)][SerializeField] private float stanceRatio = 0.6f;
    [Tooltip("스윙 중 발이 들리는 최대 높이(다리 길이 배수). 수평 이동(스윙 폭)보다 너무\n" +
             "크면 '앞으로 내딛는다'보다 '제자리에서 발을 들었다 놓는다'는 느낌이 난다 -\n" +
             "지면에 안 걸릴 정도로만 작게 잡는 게 자연스럽다")]
    [SerializeField] private float stepHeightRatio = 0.14f;
    [Tooltip("보행 주파수 상한(초당 사이클). 속도가 아무리 빨라도 이보다 빨리 젓지 않는다")]
    [SerializeField] private float maxStepFrequency = 3.2f;
    [Tooltip("이 속도에서 보행 강도가 100%가 된다. 그보다 느리면 대기 자세와 섞인다")]
    [SerializeField] private float fullGaitSpeed = 3.0f;
    [Tooltip("무릎이 굽는 기본 방향(부호). 두 다리에 항상 같은 부호를 쓴다 — 사람처럼 두 다리가\n" +
             "서로 반대쪽으로 꺾이지 않는다. 실제로 어느 다리가 더 굽어 보이는지는 보행 위상\n" +
             "(발이 앞/뒤 어디 있는지)에 따라 자연스럽게 달라진다")]
    [SerializeField] private bool kneeBendsForward = true;
    [Tooltip("스윙 중(발이 공중에 있을 때) 발끝을 추가로 드는 각도 - 지면에 걸리지 않게 한다")]
    [SerializeField] private float toeLiftDegrees = 20f;
    [Tooltip("뒤꿈치가 지면에 닿는 순간(스탠스 시작) 발끝이 살짝 들리는 각도")]
    [SerializeField] private float heelStrikeDegrees = 10f;
    [Tooltip("발끝으로 지면을 밀어내는 순간(스탠스 끝) 발끝이 눌리는 각도. 보통 음수(발끝이 아래로)")]
    [SerializeField] private float pushOffDegrees = -18f;

    [Header("몸통 반응")]
    [Tooltip("걸을 때 골반이 위아래로 흔들리는 폭(다리 길이 배수). 발이 땅을 칠 때 가라앉는다")]
    [SerializeField] private float bobRatio = 0.09f;
    [Tooltip("걸을 때 몸통이 좌우로 흔들리는 폭(다리 길이 배수)")]
    [SerializeField] private float swayRatio = 0.045f;
    [Tooltip("걸을 때 몸통이 좌우로 기우는 각도")]
    [SerializeField] private float rollDegrees = 3.5f;
    [Tooltip("속도 1유닛/초당 전진 방향으로 기우는 각도")]
    [SerializeField] private float leanDegreesPerSpeed = 2.2f;
    [SerializeField] private float maxLeanDegrees = 11f;
    [Tooltip("착지 순간 몸통이 눌리는 정도(0.05 = 5%)")]
    [SerializeField] private float squashAmount = 0.05f;

    [Header("대기(정지) 자세")]
    [Tooltip("숨쉬기 상하 진폭(다리 길이 배수)")]
    [SerializeField] private float breathRatio = 0.04f;
    [Tooltip("숨쉬기 주기(초당 횟수)")]
    [SerializeField] private float breathFrequency = 0.55f;

    [Header("전환 속도")]
    [Tooltip("보행 <-> 대기 자세가 섞이는 속도(초당)")]
    [SerializeField] private float gaitBlendSpeed = 5f;

    [Header("속도 입력")]
    [Tooltip("이 Rigidbody의 속도로 보행을 구동한다. 비어 있으면 자신/부모에서 찾고, 그래도 없으면 SetLocomotion() 입력을 쓴다")]
    [SerializeField] private Rigidbody velocitySource;

    // ── 런타임 상태 ───────────────────────────────────────────────
    private Transform rigRoot;
    private Transform legsGroup;      // 다리 전용 부모. 좌우 반전을 이 노드의 scale.x 부호로만 처리한다
    private Transform bodyPivot;      // 골반 위치. 몸통 스프라이트가 이 아래 달린다
    private Transform bodyVisual;
    private SpriteRenderer bodyRenderer;
    private readonly Leg[] legs = new Leg[2];

    private float thighLength;
    private float shinLength;
    private float ankleToSole;        // 발목에서 밑창까지의 거리
    private float boneReach;          // 허벅지 + 정강이 (완전히 폈을 때)
    private float legLength;          // 고관절 ~ 밑창. 모든 '배수' 파라미터의 기준 길이
    private float standHipY;          // 기본 골반 높이 (= ankleToSole + boneReach * hipHeightRatio)

    private float phase;              // 보행 사이클 위상 0~1
    private float gaitBlend;          // 0 = 대기, 1 = 보행
    // 좌우 방향. 스케일/보간 없이 즉시 전환되는 이산값(+1/-1)이다 - 몸통은 flipX로,
    // 다리는 legsGroup.localScale.x 부호로 이 값을 그대로 반영한다(둘 다 찌그러짐 없음).
    private float facingSign = 1f;
    private Vector2 externalVelocity;
    private bool built;

    // 구르기(대시) 연출 - 외부(PlayerRobotController)가 SetRoll()로 켜고 끈다. 다리 IK는
    // 손대지 않고(구르는 속도가 보행 로직이 감당할 수 있는 범위를 훨씬 넘어서므로) 대신
    // 다리를 숨기고 몸통만 통째로 회전시켜 "떼굴떼굴 구른다"는 느낌을 낸다.
    private bool rollActive;
    private float rollSpinDegrees;

    /// <summary>보행에 쓰이는 현재 속도(유닛/초).</summary>
    public float CurrentSpeed { get; private set; }

    /// <summary>고관절에서 밑창까지의 거리. 배수 파라미터의 기준값이다.</summary>
    public float LegLength => legLength;

    // 실제로 각 다리 파츠에 적용되는 최종 배율. 파츠별 배율(외곽선 두께 맞춤) x 전체 배율.
    // 스프라이트 크기와 뼈 길이가 반드시 같은 값을 써야 관절이 어긋나지 않으므로 여기 하나로 모은다.
    private float ThighScale => thighScale * legScale;
    private float ShinScale => shinScale * legScale;
    private float FootScale => footScale * legScale;

    /// <summary>데모/툴에서 슬라이더로 만지기 위한 접근자.</summary>
    public float StrideRatio { get => strideRatio; set => strideRatio = Mathf.Max(0.05f, value); }
    public float StepHeightRatio { get => stepHeightRatio; set => stepHeightRatio = Mathf.Max(0f, value); }
    public float StanceRatio { get => stanceRatio; set => stanceRatio = Mathf.Clamp(value, 0.3f, 0.85f); }
    public float BobRatio { get => bobRatio; set => bobRatio = value; }
    public float HipHeightRatio { get => hipHeightRatio; set => hipHeightRatio = Mathf.Clamp(value, 0.55f, 0.99f); }
    public bool KneeBendsForward { get => kneeBendsForward; set => kneeBendsForward = value; }
    public float ToeLiftDegrees { get => toeLiftDegrees; set => toeLiftDegrees = value; }
    public float HeelStrikeDegrees { get => heelStrikeDegrees; set => heelStrikeDegrees = value; }
    public float PushOffDegrees { get => pushOffDegrees; set => pushOffDegrees = value; }
    // 뼈 길이/부착 위치가 바뀌는 값은 즉시 Build()를 부르지 않고 플래그만 세운다.
    // 슬라이더를 드래그하면 한 프레임에 여러 번 들어올 수 있는데, Destroy()가 지연 파괴라
    // 그때마다 조립하면 RigRoot가 잠깐 두 개가 된다.
    private bool rebuildRequested;

    /// <summary>다리 파츠 배율. 바꾸면 다음 프레임에 리그를 다시 조립한다(뼈 길이가 바뀌므로).</summary>
    public float LegScale
    {
        get => legScale;
        set { float v = Mathf.Clamp(value, 0.05f, 2f); if (!Mathf.Approximately(v, legScale)) { legScale = v; rebuildRequested = true; } }
    }
    /// <summary>고관절 좌우 간격. 바꾸면 다음 프레임에 리그를 다시 조립한다.</summary>
    public float HipSeparation
    {
        get => hipSeparation;
        set { float v = Mathf.Max(0f, value); if (!Mathf.Approximately(v, hipSeparation)) { hipSeparation = v; rebuildRequested = true; } }
    }
    /// <summary>앞다리 고관절을 뒤다리 쪽으로 당기는 양. 바꾸면 다음 프레임에 리그를 다시 조립한다.</summary>
    public float FrontLegPullBack
    {
        get => frontLegPullBack;
        set { if (!Mathf.Approximately(value, frontLegPullBack)) { frontLegPullBack = value; rebuildRequested = true; } }
    }

    /// <summary>한쪽 다리의 뼈대.</summary>
    private class Leg
    {
        public Transform hip;      // 고관절(회전축) = 허벅지 뼈
        public Transform knee;     // 무릎(회전축) = 정강이 뼈
        public Transform ankle;    // 발목(회전축) = 발
        public SpriteRenderer thighRenderer;
        public SpriteRenderer shinRenderer;
        public SpriteRenderer footRenderer;
        public float phaseOffset;  // 0 또는 0.5
        public float sideOffsetX;  // 좌우로 벌린 양
    }

    private void Awake()
    {
        if (velocitySource == null) velocitySource = GetComponentInParent<Rigidbody>();
        Build();
    }

    private void OnDestroy()
    {
        // 에디터에서 [리그 다시 만들기]를 반복해도 찌꺼기가 남지 않게 한다
    }

    /// <summary>Rigidbody가 없을 때 외부에서 이동 속도를 넣어준다(XY 평면).</summary>
    public void SetLocomotion(Vector2 planarVelocity) => externalVelocity = planarVelocity;

    /// <summary>
    /// 구르기(대시) 연출을 켜고 끈다. active=true인 동안은 매 프레임 spinDegrees(보통 0→360으로
    /// 진행)를 몸통 회전에 그대로 적용하고 다리는 숨긴다. active=false가 되는 순간 다리를 되살리고
    /// 평소 보행 로직으로 되돌아간다.
    /// </summary>
    public void SetRoll(bool active, float spinDegrees)
    {
        rollActive = active;
        rollSpinDegrees = spinDegrees;
    }

    /// <summary>PlayerRobotController처럼 3D 벡터를 쓰는 쪽을 위한 편의 오버로드.</summary>
    public void SetLocomotion(Vector3 planarVelocity) => externalVelocity = new Vector2(planarVelocity.x, planarVelocity.y);

    // ── 조립 ─────────────────────────────────────────────────────

    [ContextMenu("리그 다시 만들기")]
    public void Build()
    {
        LoadPartSprites();
        if (bodySprite == null || thighSprite == null || shinSprite == null || footSprite == null)
        {
            Debug.LogError("ProceduralCharacterRig: 스프라이트가 비어 있습니다. Assets/Resources/Parts/에 " +
                           "Body / LegUpper / LegLower / Foot 이 있는지 확인하세요.");
            return;
        }

        // 이전 리그 제거 (다시 만들기 대응). 지연 파괴(Destroy)를 쓰면 같은 프레임에
        // RigRoot가 두 개 존재할 수 있으므로 즉시 파괴한다.
        Transform old = rigRoot != null ? rigRoot : transform.Find("RigRoot");
        if (old != null) DestroyImmediate(old.gameObject);
        rebuildRequested = false;

        rigRoot = new GameObject("RigRoot").transform;
        rigRoot.SetParent(transform, false);
        rigRoot.localScale = new Vector3(rigScale, rigScale, 1f);

        // 뼈 길이는 앵커에서 자동으로 나온다. 손으로 넣는 값은 앵커뿐이다.
        Vector2 thighHip = AnchorToLocal(thighSprite, thighHipAnchor);
        Vector2 thighKnee = AnchorToLocal(thighSprite, thighKneeAnchor);
        Vector2 shinKnee = AnchorToLocal(shinSprite, shinKneeAnchor);
        Vector2 shinAnkle = AnchorToLocal(shinSprite, shinAnkleAnchor);

        // 파츠별 배율을 각자에게 곱한다(몸통은 원본 크기 유지). 뼈 길이도 같은 배율로 재므로
        // 스프라이트와 관절 위치가 항상 일치한다.
        thighLength = Vector2.Distance(thighHip, thighKnee) * ThighScale;
        shinLength = Vector2.Distance(shinKnee, shinAnkle) * ShinScale;

        // 스프라이트가 비스듬히 그려져 있어도(허벅지가 약 19도 기울어 있다) 뼈 축이 정확히
        // 아래(-Y)를 향하도록 보정각을 역산한다. 두 다리 모두 이 값을 그대로 쓴다 — 사람처럼
        // 두 다리가 같은 방향 규칙으로 굽어야 하며, 실제로 보이는 차이는 보행 위상에서 나온다.
        float thighTilt = TiltToDown(thighKnee - thighHip);
        float shinTilt = TiltToDown(shinAnkle - shinKnee);

        Vector2 footAnkle = AnchorToLocal(footSprite, MaybeMirrorX(footAnkleAnchor));
        Vector2 footSole = AnchorToLocal(footSprite, MaybeMirrorX(new Vector2(footAnkleAnchor.x, footSoleY)));
        ankleToSole = Mathf.Abs(footAnkle.y - footSole.y) * FootScale;

        boneReach = thighLength + shinLength;
        legLength = boneReach + ankleToSole;
        standHipY = ankleToSole + boneReach * hipHeightRatio;

        // 몸통 - 좌우 반전은 flipX로 즉시 처리한다(스케일 반전이 아니므로 회전 중간에
        // 찌그러지는 프레임이 없다). Apply()에서 매 프레임 flipX와 앵커를 갱신한다.
        bodyPivot = new GameObject("BodyPivot").transform;
        bodyPivot.SetParent(rigRoot, false);
        bodyPivot.localPosition = new Vector3(0f, standHipY, 0f);

        GameObject bodyGo = new GameObject("Body");
        bodyVisual = bodyGo.transform;
        bodyVisual.SetParent(bodyPivot, false);
        bodyRenderer = bodyGo.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = bodySprite;
        bodyRenderer.sortingLayerName = sortingLayerName;
        bodyRenderer.sortingOrder = bodySortingOrder;
        ApplyBodyFacing();

        // 다리 전용 부모. 좌우 반전은 이 노드의 localScale.x 부호만으로 처리한다(크기는
        // 항상 1이라 찌그러지는 중간 프레임이 없다 - 몸통의 flipX와 같은 이유로 분리했다).
        legsGroup = new GameObject("LegsGroup").transform;
        legsGroup.SetParent(rigRoot, false);
        legsGroup.localScale = new Vector3(facingSign, 1f, 1f);

        // 다리 2개. 0 = 뒤쪽(먼저 그려짐), 1 = 앞쪽. phaseOffset(0 / 0.5)만 다르고 그 외
        // 규칙(굽힘 부호·그림 방향)은 두 다리가 완전히 동일하다 — 사람처럼.
        // 앞다리는 고관절 자체를 frontLegPullBack만큼 뒤다리 쪽(중심)으로 당긴다 - 대기/보행
        // 구분 없이 다리 전체(뼈·발 모두)가 통째로 이동한다(idle 타겟만 조정하는 것과 다르다).
        legs[0] = BuildLeg("Leg_Back", bodySortingOrder - 10, -hipSeparation * 0.5f, 0.5f, backLegTint,
                           thighHip, thighTilt, shinKnee, shinTilt, footAnkle);
        legs[1] = BuildLeg("Leg_Front", bodySortingOrder - 5, hipSeparation * 0.5f - frontLegPullBack, 0f, Color.white,
                           thighHip, thighTilt, shinKnee, shinTilt, footAnkle);

        built = true;
        Apply(0f, Vector2.zero);   // 첫 프레임부터 올바른 자세로 서 있게
    }

    /// <summary>몸통의 flipX와 앵커 위치를 facingSign에 맞춰 갱신한다. flipX는 픽셀만
    /// 좌우로 뒤집으므로, 배치에 쓰는 앵커도 같이 뒤집어야 위치가 어긋나지 않는다
    /// (발의 MaybeMirrorX와 같은 원리).</summary>
    private void ApplyBodyFacing()
    {
        bool flip = facingSign > 0f;
        bodyRenderer.flipX = flip;
        Vector2 anchor = flip ? new Vector2(1f - bodyHipAnchor.x, bodyHipAnchor.y) : bodyHipAnchor;
        Vector3 anchorPos = -(Vector3)AnchorToLocal(bodySprite, anchor);
        bodyVisual.localPosition = new Vector3(anchorPos.x, anchorPos.y, 0f);
    }

    private Leg BuildLeg(string name, int sortingBase, float sideX, float phaseOffset, Color tint,
                         Vector2 thighHip, float thighTilt, Vector2 shinKnee, float shinTilt, Vector2 footAnkle)
    {
        Leg leg = new Leg { phaseOffset = phaseOffset, sideOffsetX = sideX };

        leg.hip = new GameObject(name).transform;
        leg.hip.SetParent(legsGroup, false);
        leg.hip.localPosition = new Vector3(sideX, standHipY, 0f);
        // 정강이가 허벅지보다 먼저 그려지고, 무릎 공(허벅지)과 신발이 그 위를 덮는다
        leg.thighRenderer = AttachVisual(leg.hip, "Thigh", thighSprite, thighHip, thighTilt,
                                         sortingBase + 1, tint, false, ThighScale);

        leg.knee = new GameObject("Knee").transform;
        leg.knee.SetParent(leg.hip, false);
        leg.knee.localPosition = new Vector3(0f, -thighLength, 0f);
        leg.shinRenderer = AttachVisual(leg.knee, "Shin", shinSprite, shinKnee, shinTilt,
                                        sortingBase, tint, false, ShinScale);

        leg.ankle = new GameObject("Ankle").transform;
        leg.ankle.SetParent(leg.knee, false);
        leg.ankle.localPosition = new Vector3(0f, -shinLength, 0f);
        leg.footRenderer = AttachVisual(leg.ankle, "Foot", footSprite, footAnkle, 0f,
                                        sortingBase + 2, tint, footSpriteFlipX, FootScale);

        return leg;
    }

    /// <summary>뼈(회전축)에 스프라이트를 달되, 지정한 앵커가 정확히 회전축에 오도록 위치를 계산한다.</summary>
    private SpriteRenderer AttachVisual(Transform bone, string name, Sprite sprite, Vector2 anchorLocal, float tiltDeg,
                                        int sortingOrder, Color tint, bool flipX, float partScale = 1f)
    {
        GameObject go = new GameObject(name);
        Transform t = go.transform;
        t.SetParent(bone, false);
        t.localRotation = Quaternion.Euler(0f, 0f, tiltDeg);
        t.localScale = new Vector3(partScale, partScale, 1f);
        // 앵커가 원점에 오도록: pos + R * (anchor * scale) = 0
        t.localPosition = Quaternion.Euler(0f, 0f, tiltDeg) * (new Vector3(-anchorLocal.x, -anchorLocal.y, 0f) * partScale);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.flipX = flipX;
        sr.color = tint;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    /// <summary>
    /// 인게임 로봇의 외형은 인스펙터에 남은 임의 참조가 아니라 Resources/Parts의 네 파츠가
    /// 유일한 기준이다. 씬 참조가 잘못 저장됐더라도 Build 시 반드시 원본 파츠로 복구한다.
    /// </summary>
    private void LoadPartSprites()
    {
        if (expectedBodySprite == null) expectedBodySprite = Resources.Load<Sprite>(ResourceFolder + "Body");
        if (expectedThighSprite == null) expectedThighSprite = Resources.Load<Sprite>(ResourceFolder + "LegUpper");
        if (expectedShinSprite == null) expectedShinSprite = Resources.Load<Sprite>(ResourceFolder + "LegLower");
        if (expectedFootSprite == null) expectedFootSprite = Resources.Load<Sprite>(ResourceFolder + "Foot");

        bodySprite = expectedBodySprite;
        thighSprite = expectedThighSprite;
        shinSprite = expectedShinSprite;
        footSprite = expectedFootSprite;
    }

    /// <summary>
    /// 피격·공격 연출 등 다른 런타임 코드가 실수로 로봇 렌더러의 sprite를 바꿔도 다음 프레임에
    /// 지정 파츠로 복구한다. 색상·회전·스케일은 건드리지 않아 기존 애니메이션은 그대로 유지된다.
    /// </summary>
    private void RestorePartSprites()
    {
        // 필드 자체가 다른 파츠를 가리키는 경우까지 복구한다(예: bodySprite에 Foot이 들어간 상태).
        if (bodySprite != expectedBodySprite || thighSprite != expectedThighSprite ||
            shinSprite != expectedShinSprite || footSprite != expectedFootSprite)
        {
            LoadPartSprites();
        }

        // 렌더러 참조가 다른 뼈의 파츠로 바뀐 경우 정확한 자식 경로에서 다시 가져온다.
        if (bodyVisual != null && (bodyRenderer == null || bodyRenderer.transform != bodyVisual))
            bodyRenderer = bodyVisual.GetComponent<SpriteRenderer>();

        for (int i = 0; i < legs.Length; i++)
        {
            Leg leg = legs[i];
            if (leg == null) continue;
            leg.thighRenderer = ResolvePartRenderer(leg.hip, "Thigh", leg.thighRenderer);
            leg.shinRenderer = ResolvePartRenderer(leg.knee, "Shin", leg.shinRenderer);
            leg.footRenderer = ResolvePartRenderer(leg.ankle, "Foot", leg.footRenderer);
        }

        if (bodyRenderer != null && bodyRenderer.sprite != bodySprite) bodyRenderer.sprite = bodySprite;

        for (int i = 0; i < legs.Length; i++)
        {
            Leg leg = legs[i];
            if (leg == null) continue;
            if (leg.thighRenderer != null && leg.thighRenderer.sprite != thighSprite) leg.thighRenderer.sprite = thighSprite;
            if (leg.shinRenderer != null && leg.shinRenderer.sprite != shinSprite) leg.shinRenderer.sprite = shinSprite;
            if (leg.footRenderer != null && leg.footRenderer.sprite != footSprite) leg.footRenderer.sprite = footSprite;
        }
    }

    private static SpriteRenderer ResolvePartRenderer(Transform parent, string childName, SpriteRenderer current)
    {
        if (parent == null) return null;
        if (current != null && current.transform.parent == parent && current.gameObject.name == childName) return current;

        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<SpriteRenderer>() : null;
    }

    /// <summary>정규화 앵커 → 스프라이트 로컬 좌표(pivot = Center 기준, 유닛).</summary>
    private static Vector2 AnchorToLocal(Sprite sprite, Vector2 normalized)
    {
        Rect r = sprite.rect;
        float ppu = sprite.pixelsPerUnit;
        return new Vector2((normalized.x - 0.5f) * r.width / ppu,
                           (normalized.y - 0.5f) * r.height / ppu);
    }

    private Vector2 MaybeMirrorX(Vector2 normalized)
        => footSpriteFlipX ? new Vector2(1f - normalized.x, normalized.y) : normalized;

    /// <summary>주어진 방향 벡터가 정확히 아래(-Y)를 향하게 만드는 회전각(도).</summary>
    private static float TiltToDown(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-8f) return 0f;
        float current = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(current, -90f);
    }

    // ── 매 프레임 갱신 ────────────────────────────────────────────

    private void Update()
    {
        if (rebuildRequested) Build();
        if (!built) return;

        RestorePartSprites();

        if (rollActive)
        {
            ApplyRollPose();
            return;
        }
        if (!legsGroup.gameObject.activeSelf) legsGroup.gameObject.SetActive(true); // 구르기 종료 시 다리 복구

        float dt = Time.deltaTime;
        Vector2 velocity = velocitySource != null
            ? new Vector2(velocitySource.linearVelocity.x, velocitySource.linearVelocity.y)
            : externalVelocity;

        CurrentSpeed = velocity.magnitude;

        // 좌우 방향: 확실히 움직일 때만 바꾼다(제자리에서 떨리는 것 방지). 스케일 보간 없이
        // 즉시 전환한다 - 몸통(flipX)·다리(legsGroup 부호) 둘 다 찌그러지는 중간 프레임이 없다.
        float newFacing = facingSign;
        if (Mathf.Abs(velocity.x) > 0.15f) newFacing = Mathf.Sign(velocity.x);
        if (!Mathf.Approximately(newFacing, facingSign))
        {
            facingSign = newFacing;
            ApplyBodyFacing();
            legsGroup.localScale = new Vector3(facingSign, 1f, 1f);
        }

        // 보행 강도와 위상
        float targetBlend = Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.01f, fullGaitSpeed));
        gaitBlend = Mathf.MoveTowards(gaitBlend, targetBlend, gaitBlendSpeed * dt);

        float frequency = Mathf.Clamp(CurrentSpeed / Mathf.Max(0.02f, strideRatio * legLength), 0f, maxStepFrequency);
        phase = Mathf.Repeat(phase + frequency * dt, 1f);

        Apply(dt, velocity);
    }

    // 다른 컴포넌트의 Update/코루틴이 sprite를 건드린 뒤에도 실제 렌더 직전에 한 번 더 확정한다.
    private void LateUpdate()
    {
        if (built) RestorePartSprites();
    }

    /// <summary>
    /// 구르는 동안의 자세. 다리 IK는 구르기 속도(보행 로직이 감당하는 범위를 훨씬 넘어선다)를
    /// 감당하지 못하므로 아예 숨기고, 몸통만 rollSpinDegrees만큼 회전시킨다(0→360도로 진행하면
    /// 정확히 한 바퀴 돈다). 회전 중간에 살짝 떠올랐다 내려오는 아치를 더해 튕겨 구르는 느낌을 낸다.
    /// </summary>
    private void ApplyRollPose()
    {
        if (legsGroup.gameObject.activeSelf) legsGroup.gameObject.SetActive(false);

        float progress01 = Mathf.Repeat(Mathf.Abs(rollSpinDegrees), 360f) / 360f;
        float hop = Mathf.Sin(progress01 * Mathf.PI) * legLength * 0.25f;

        bodyPivot.localPosition = new Vector3(0f, standHipY + hop, 0f);
        bodyPivot.localRotation = Quaternion.Euler(0f, 0f, rollSpinDegrees);
        bodyVisual.localScale = Vector3.one;
    }

    private void Apply(float dt, Vector2 velocity)
    {
        // 골반 높이는 인스펙터/슬라이더로 실시간 조절할 수 있어야 하므로 매 프레임 다시 구한다
        standHipY = ankleToSole + boneReach * hipHeightRatio;

        // 길이 계열 파라미터는 전부 다리 길이에 대한 배수다(그림이나 legScale이 바뀌어도 따라간다)
        float bobAmount = bobRatio * legLength;
        float swayAmount = swayRatio * legLength;
        float breathAmplitude = breathRatio * legLength;

        // ── 몸통 ──
        // 발이 땅을 칠 때(한 사이클에 두 번) 골반이 가라앉는다
        float bob = -Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f)) * bobAmount * gaitBlend;
        // 숨쉬기는 **아래로만** 움직인다. 위로 올리면 거의 편 다리가 지면에 닿지 못해
        // 발이 공중에 뜬다(IK 목표가 사거리를 벗어나 잘림).
        float breath = -(0.5f + 0.5f * Mathf.Sin(Time.time * breathFrequency * Mathf.PI * 2f))
                       * breathAmplitude * (1f - gaitBlend);
        float sway = Mathf.Sin(phase * Mathf.PI * 2f) * swayAmount * gaitBlend;

        bodyPivot.localPosition = new Vector3(sway, standHipY + bob + breath, 0f);

        // velocity.x의 부호를 그대로 써야 한다. Abs를 쓰면 왼쪽으로 갈 때도 오른쪽과
        // 똑같이(항상 -lean 방향으로) 기울어 걷는 방향과 무관하게 늘 같은 쪽으로 기운
        // 것처럼 보인다 - 오른쪽으로 갈 땐 오른쪽으로, 왼쪽으로 갈 땐 왼쪽으로 기울어야 한다.
        float lean = Mathf.Clamp(velocity.x * leanDegreesPerSpeed, -maxLeanDegrees, maxLeanDegrees) * gaitBlend;
        float roll = Mathf.Sin(phase * Mathf.PI * 2f) * rollDegrees * gaitBlend;
        // 전진 방향으로 기울려면 z를 음수로 돌린다(오른쪽 이동 시 lean이 양수이므로 -lean은 음수)
        bodyPivot.localRotation = Quaternion.Euler(0f, 0f, -lean + roll);

        float squash = Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2f)) * squashAmount * gaitBlend;
        bodyVisual.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f);

        // ── 다리 ──
        // 이만큼만 뒤로 밀어야 발이 지면에서 미끄러지지 않는다
        float stanceTravel = strideRatio * legLength * stanceRatio;
        for (int i = 0; i < legs.Length; i++)
        {
            Leg leg = legs[i];
            leg.hip.localPosition = new Vector3(leg.sideOffsetX + sway * 0.35f, standHipY + bob + breath * 0.5f, 0f);

            float p = Mathf.Repeat(phase + leg.phaseOffset, 1f);
            Vector2 gaitTarget = FootTargetForPhase(p, stanceTravel, out float toeAngle);
            // 대기 자세: 두 발을 앞뒤로 조금 벌려 둔다(완전히 겹치면 다리가 한 짝만 보인다)
            float idleX = (i == 0 ? -0.5f : 0.5f) * idleStanceRatio * legLength;
            Vector2 idleTarget = new Vector2(idleX, ankleToSole);

            Vector2 target = Vector2.Lerp(idleTarget, gaitTarget, gaitBlend);
            target.x += leg.sideOffsetX;
            SolveLeg(leg, target, toeAngle * gaitBlend);
        }
    }

    /// <summary>
    /// 한 사이클(0~1) 중 발이 있어야 할 위치와 각도. 앞쪽 절반은 땅을 밀고(스탠스), 뒤쪽은
    /// 공중으로 돌아온다(스윙). 스탠스 이동량을 stride * stanceRatio로 맞춰서 발이 지면에서
    /// 미끄러지지 않는다.
    ///
    /// 발 각도는 실제 걸음의 뒤꿈치착지→평발→발끝밀기→스윙 흐름을 따른다:
    /// 스탠스 시작(뒤꿈치착지)에 발끝이 살짝 들리고, 스탠스 끝(발끝밀기)에는 눌리며,
    /// 스윙 중에는 지면에 걸리지 않도록 추가로 들린다. 스탠스↔스윙 경계에서 각도가
    /// 끊기지 않도록(순간적으로 툭 꺾이지 않도록) 양끝 각도를 일치시켜 이어붙였다.
    /// </summary>
    private Vector2 FootTargetForPhase(float p, float stanceTravel, out float toeAngle)
    {
        if (p < stanceRatio)
        {
            float u = p / stanceRatio;                       // 0(뒤꿈치착지) → 1(발끝밀기)
            toeAngle = Mathf.Lerp(heelStrikeDegrees, pushOffDegrees, u);
            return new Vector2(Mathf.Lerp(stanceTravel * 0.5f, -stanceTravel * 0.5f, u), ankleToSole);
        }

        float s = (p - stanceRatio) / (1f - stanceRatio);    // 0(발끝밀기 직후) → 1(다음 뒤꿈치착지 직전)
        float eased = s * s * (3f - 2f * s);                 // smoothstep: 이륙/착지를 부드럽게
        // pushOffDegrees(s=0)에서 heelStrikeDegrees(s=1)로 선형 이동 + 중간에 지면 회피용으로 더 든다.
        // 양끝에서 sin(0)=sin(pi)=0이라 스탠스와 각도가 정확히 이어진다.
        toeAngle = Mathf.Lerp(pushOffDegrees, heelStrikeDegrees, s) + Mathf.Sin(s * Mathf.PI) * toeLiftDegrees;
        return new Vector2(Mathf.Lerp(-stanceTravel * 0.5f, stanceTravel * 0.5f, eased),
                           ankleToSole + Mathf.Sin(s * Mathf.PI) * stepHeightRatio * legLength);
    }

    /// <summary>
    /// 2관절 IK. 고관절은 고정, 발 위치를 목표로 두고 허벅지/정강이 각도를 코사인 법칙으로 역산한다.
    /// 뼈의 진행 방향은 로컬 -Y이므로 방향각 θ에 대한 z회전은 θ + 90도다.
    /// </summary>
    private void SolveLeg(Leg leg, Vector2 targetLocal, float toeAngleDeg)
    {
        Vector2 hip = new Vector2(leg.hip.localPosition.x, leg.hip.localPosition.y);
        Vector2 delta = targetLocal - hip;

        float reach = thighLength + shinLength;
        float minReach = Mathf.Abs(thighLength - shinLength) + 0.001f;
        float maxReach = reach - 0.002f;

        // 목표가 사거리를 벗어나면 방향을 유지한 채 거리만 줄이는 게 보통이지만, 그렇게 하면
        // 발이 지면에서 떠버린다. 지면 접촉이 더 중요하므로 높이(y)를 살리고 **가로(x)를 줄인다**.
        // 결과적으로 보폭이 자동으로 다리 길이에 맞게 잘린다.
        if (delta.magnitude > maxReach)
        {
            float dy = Mathf.Clamp(delta.y, -maxReach, maxReach);
            float maxDx = Mathf.Sqrt(Mathf.Max(0f, maxReach * maxReach - dy * dy));
            delta = new Vector2(Mathf.Clamp(delta.x, -maxDx, maxDx), dy);
            targetLocal = hip + delta;
        }

        float dist = Mathf.Clamp(delta.magnitude, minReach, maxReach);

        float baseAngle = Mathf.Atan2(delta.y, delta.x);
        float cosA = (dist * dist + thighLength * thighLength - shinLength * shinLength) / (2f * dist * thighLength);
        float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

        // 두 다리 모두 같은 부호를 쓴다 - 실제로 어느 쪽 무릎이 더 굽어 보이는지는
        // baseAngle(보행 위상에 따른 목표 방향)에서 자연스럽게 갈린다
        float sign = kneeBendsForward ? 1f : -1f;
        float thighAngle = baseAngle + a * sign;

        Vector2 kneePos = hip + new Vector2(Mathf.Cos(thighAngle), Mathf.Sin(thighAngle)) * thighLength;
        Vector2 toTarget = targetLocal - kneePos;
        float shinAngle = Mathf.Atan2(toTarget.y, toTarget.x);

        float thighZ = thighAngle * Mathf.Rad2Deg + 90f;
        float shinZ = shinAngle * Mathf.Rad2Deg + 90f;

        leg.hip.localRotation = Quaternion.Euler(0f, 0f, thighZ);
        leg.knee.localRotation = Quaternion.Euler(0f, 0f, shinZ - thighZ);
        // 발은 정강이 각도를 상쇄해서 '월드 기준' 각도를 유지한다(스탠스에서 항상 평평)
        leg.ankle.localRotation = Quaternion.Euler(0f, 0f, toeAngleDeg - shinZ);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!built || rigRoot == null) return;
        Gizmos.color = Color.yellow;
        foreach (Leg leg in legs)
        {
            if (leg == null) continue;
            Gizmos.DrawWireSphere(leg.hip.position, 0.03f);
            Gizmos.DrawWireSphere(leg.knee.position, 0.03f);
            Gizmos.DrawWireSphere(leg.ankle.position, 0.03f);
            Gizmos.DrawLine(leg.hip.position, leg.knee.position);
            Gizmos.DrawLine(leg.knee.position, leg.ankle.position);
        }
        // 지면
        Gizmos.color = Color.green;
        Vector3 g = transform.position;
        Gizmos.DrawLine(g + Vector3.left * 2f, g + Vector3.right * 2f);
    }
#endif
}
