using System.Collections.Generic;
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

    // 2026-08-21 다리 기획서 Ver02 비주얼 - 캐터필러/로켓 추진기는 관절 리깅이 필요 없는
    // "이미 완성된 애니메이션 프레임"이라(사용자 확인) IK 없이 그대로 재생만 한다. 프레임은
    // 한 번만 로드해 공유한다(Resources/Parts/Tread, Resources/Parts/Rocket).
    private static Sprite[] treadBackFrames;
    private static Sprite[] treadFrontFrames;
    private static Sprite[] rocketBaseFrames;
    private static Sprite[] rocketNozzleFrames;
    private static Sprite[] rocketFlameFrames;

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
             "2026-08-10 사용자 지정으로 0.88 → 0.62했었으나, 2026-08-12 재확인 결과 0.62는 신발이\n" +
             "정강이 전체를 완전히 가려버리는 버그 수준이었다(스크린샷으로 실측 확인). 0.85로 올려\n" +
             "정강이가 보이면서 발목만 살짝 파묻히는 원래 의도로 되돌렸다.")]
    [SerializeField] private Vector2 footAnkleAnchor = new Vector2(0.55f, 0.85f);
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

    /// <summary>몸통(=머리) 스프라이트가 붙은 Transform. 악세사리(2026-08-19 Phase D)처럼
    /// 몸을 따라 움직이고 flipX와 함께 뒤집혀야 하는 장식을 붙일 자리가 필요한 외부 스크립트가
    /// 쓴다 - 다리 파츠와 달리 flipX 하나로 좌우가 뒤집히므로 별도 미러 보정 없이 자식으로
    /// 붙이기만 하면 된다.</summary>
    public Transform BodyVisual => bodyVisual;

    /// <summary>몸통 스프라이트 렌더러. 정렬 순서(sortingOrder)를 참고하거나 로컬 bounds로
    /// "머리 꼭대기" 위치를 계산할 때 쓴다.</summary>
    public SpriteRenderer BodyRenderer => bodyRenderer;

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

    // 2026-08-21 다리 기획서 Ver02 - 거미 다리의 "폴짝 뛰기"는 구르기와 같은 숨김+아치 연출을
    // 쓰지만 몸통이 360도로 돌면 안 된다(회전 없이 위아래로만 튄다). 예전엔 위로 튀는 아치의
    // 진행도(progress01)를 rollSpinDegrees 각도에서 그대로 역산했어서 회전 없이는 아치도 만들
    // 수 없었다 - 이 필드로 아치 진행도를 회전과 분리한다. -1(기본값)이면 예전처럼 회전에서
    // 역산한다(구르기 그대로 하위호환).
    private float rollHopProgressOverride = -1f;

    // 다리 비주얼 종류(기획서 Ver02) - PlayerRobotController가 장착 다리 파츠를 읽어
    // SetLegVisual()로 알려준다. Biped(기본값)면 이 파일의 원래 걸음걸이 리그가 그대로 쓰인다.
    private LegVisualMode legVisualMode = LegVisualMode.Biped;
    private Transform treadRoot;
    private Transform rocketRoot;
    private Transform spiderRoot;
    private FrameCycler treadBack;
    private FrameCycler treadFront;
    private FrameCycler rocketBase;
    private FrameCycler rocketNozzle;
    private FrameCycler rocketFlame;
    private float treadDistancePhase; // 이동 거리 기반(트랙이 실제로 굴러가는 느낌)
    private float rocketIdlePhase;    // 시간 기반(정지해 있어도 호버링+화염이 돈다)

    // 거미 다리(관절 IK 4개) - Assets/Prototype/SpiderLegRig.html에서 사용자와 함께 검증한
    // 알고리즘(고관절 고정, 2관절 IK, "무릎은 항상 몸통 중심에서 더 먼 쪽으로", 대각선 짝
    // 반응형 걸음)을 그대로 옮겨왔다. 앵커 좌표는 그 프로토타입에서 실측한 값과 같다.
    private static Sprite spiderUpperSprite;
    private static Sprite spiderLowerSprite;
    private static Sprite spiderTorsoSprite;
    private readonly SpiderLeg[] spiderLegs = new SpiderLeg[4];

    // ── 거미 다리 기하: Prototype/SpiderLegRig.html의 LEG_DEFS 실측값 ──────────────
    // 고관절 위치는 밑판(body.png 827x509) 원본 픽셀 기준 비율이라, 밑판 크기가 바뀌어도
    // 저절로 따라온다. 뒷다리(sy 음수 = 화면 위)는 멀리 있어 얕게, 앞다리는 가까워 깊게 짚는다.
    private const float SpiderRearHipXRatio = 327.75f / 827f;
    private const float SpiderRearHipYRatio = 163.10f / 509f;
    private const float SpiderFrontHipXRatio = 313.85f / 827f;
    private const float SpiderFrontHipYRatio = 170.55f / 509f;
    private const float SpiderRearLegScale = 0.94f, SpiderRearSpread = 0.56f, SpiderRearDrop = 0.30f;
    private const float SpiderFrontLegScale = 1.06f, SpiderFrontSpread = 0.50f, SpiderFrontDrop = 0.52f;

    private class SpiderLeg
    {
        public Transform hip;
        public Transform knee;
        public Vector2 hipLocalPos;   // 고정(보행 중에도 움직이지 않는다 - 발만 움직인다)
        public Vector2 idealLocalPos; // 대기 자세 발 목표(몸통 기준 로컬)

        // 앞/뒤 다리가 원근 배율(0.94 / 1.06)로 서로 다른 크기라 뼈 길이도 다리마다 다르다.
        public float thighLength;
        public float shinLength;
        public bool isRear;

        // 발은 <b>월드 좌표</b>로 들고 있어야 한다 - 몸이 걸어가는 동안 발은 "그 자리에 심겨"
        // 있다가 목표에서 너무 멀어지면 그제서야 다음 자리로 옮긴다. 로컬(몸통 기준) 좌표로
        // 들고 있으면 몸이 움직여도 발이 항상 몸을 그대로 따라가 버려서(오차가 절대 생기지
        // 않는다) 반응형 걸음이 전혀 트리거되지 않는다 - 실제로 처음 구현에서 겪은 버그다.
        public Vector3 footWorldPos;
        public bool stepping;
        public float stepT;
        public Vector3 stepFromWorld;
        public Vector3 stepToWorld;
        public int pairIndex; // 대각선 짝(0/1) - 반대 짝이 스텝 중이면 기다린다
    }

    [Header("다리 비주얼 - 캐터필러/로켓 (2026-08-21 다리 기획서 Ver02)")]
    [Tooltip("트랙 1칸(프레임)이 넘어가는 데 필요한 이동 거리(유닛). 작을수록 트랙이 빨리 돈다")]
    [SerializeField] private float treadUnitsPerFrame = 0.35f;
    [Tooltip("로켓 호버링/화염 애니메이션 재생 속도(초당 프레임)")]
    [SerializeField] private float rocketFps = 8f;
    [Tooltip("캐터필러 장착 시 머리(몸통) 위치 미세 조정(음수 = 아래로). 다리(트레드) 위치는 " +
             "건드리지 않고 머리만 움직인다. 리그 로컬 단위이며 월드로는 x0.5" +
             "(rigScale 0.55 x 리그 lossyScale 0.909)다")]
    [SerializeField] private float treadBodyYOffset = 0f;
    [Tooltip("로켓 추진기 장착 시 머리(몸통) 위치 미세 조정(음수 = 아래로). 다리(로켓) 위치는 " +
             "건드리지 않고 머리만 움직인다")]
    [SerializeField] private float rocketBodyYOffset = 0f;

    [Header("다리 비주얼 - 거미 (2026-08-21 다리 기획서 Ver02, 실제 IK 리깅)")]
    [Tooltip("거미 다리 파츠(upper_leg/lower_leg) 기본 크기 배율. 프로토타입(Prototype/SpiderLegRig.html)은 " +
             "밑판(body.png)과 다리에 같은 assetScale을 쓰고 앞/뒤 다리에만 0.94/1.06 원근 배율을 곱한다 - " +
             "그래서 <b>이 값은 spiderTorsoScale과 항상 같게 유지</b>해야 프로토타입 비율이 지켜진다. " +
             "둘을 같은 비율로 올리면 조립체 전체가 커진다(로봇 머리가 밑판보다 크기 때문에, 너무 작으면 " +
             "뒤쪽 다리 한 쌍이 머리에 완전히 가려진다 - 2026-08-24 사용자 리포트)")]
    [SerializeField] private float spiderLegScale = 0.32f;
    [Tooltip("대기 자세에서 발이 바깥으로 뻗는 양의 <b>배율</b>(1 = 프로토타입 기본값). " +
             "실제 기준값은 앞/뒤 다리별로 다르다(뒤 0.56 / 앞 0.50)")]
    [SerializeField] private float spiderStanceSpreadRatio = 1f;
    [Tooltip("대기 자세에서 발이 아래로 내려가는 양의 <b>배율</b>(1 = 프로토타입 기본값). " +
             "실제 기준값은 앞/뒤 다리별로 다르다(뒤 0.30 / 앞 0.52 - 뒷다리는 멀리 있어 얕게 짚는다)")]
    [SerializeField] private float spiderStanceDropRatio = 1f;
    [Tooltip("발이 목표 지점에서 이 거리(유닛) 이상 벌어지면 그 다리가 스텝을 시작한다")]
    [SerializeField] private float spiderStepThreshold = 0.16f;
    [Tooltip("스텝 진행 속도(1/스텝 시간) - 클수록 빠르게 사삭거린다. 정지 상태의 기준값이며 " +
             "이동 속도에 따라 spiderStepSpeedPerSpeed만큼 더 빨라진다")]
    [SerializeField] private float spiderStepSpeed = 11f;
    [Tooltip("이동 속도 1유닛/초마다 스텝 속도에 곱해지는 증가율. 0이면 예전처럼 속도와 무관하게 " +
             "항상 같은 빠르기로 딛는다(빠르게 달리면 발이 몸을 못 따라가 끌려 보인다)")]
    [SerializeField] private float spiderStepSpeedPerSpeed = 0.16f;
    [Tooltip("스텝 목표에 이동 방향으로 살짝 더 뻗는 오버슈트(유닛). 정지 상태의 기준값")]
    [SerializeField] private float spiderStepOvershoot = 0.1f;
    [Tooltip("스텝하는 동안 몸이 이동할 거리(속도 x 스텝 시간)를 목표에 얼마나 미리 얹을지. " +
             "1 = 착지 순간 발이 정확히 대기 위치에 오도록 완전히 앞서 짚는다. 0이면 예전 동작")]
    [SerializeField] private float spiderStepLeadRatio = 1f;
    [Tooltip("이동 속도에 따라 앞서 짚는 거리의 상한(유닛). 구르기처럼 극단적으로 빠른 순간에 " +
             "다리가 몸에서 지나치게 멀리 뻗는 것을 막는다")]
    [SerializeField] private float spiderStepLeadMax = 0.9f;
    [Tooltip("스텝 중 발이 들리는 높이(유닛)")]
    [SerializeField] private float spiderFootLiftHeight = 0.05f;
    [Tooltip("거미 다리 장착 시 머리(몸통) 위치를 다리에 맞춰 위/아래로 미세 조정한다")]
    [SerializeField] private float spiderBodyYOffset = 0f;
    [Tooltip("거미 다리 밑판(body.png, 호버 패드 하우징) 크기 배율. 원본이 몸통(250px)보다 " +
             "훨씬 큰 캔버스(827px)라 별도 배율이 필요하다. <b>spiderLegScale과 항상 같은 값으로 유지</b>할 것 " +
             "- 고관절 위치가 이 밑판 크기 비율에서 계산되므로 둘이 어긋나면 다리가 밑판에서 떨어진다")]
    [SerializeField] private float spiderTorsoScale = 0.32f;
    [Tooltip("거미 다리 밑판의 세로 위치 미세 조정")]
    [SerializeField] private float spiderTorsoYOffset = 0f;

    /// <summary>이미지 6장을 순서대로 돌리기만 하는 프레임 재생기(관절 없음).</summary>
    private class FrameCycler
    {
        public SpriteRenderer renderer;
        public Sprite[] frames;

        public void ShowFrame(float phase01)
        {
            if (renderer == null || frames == null || frames.Length == 0) return;
            int idx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(phase01, 1f) * frames.Length), 0, frames.Length - 1);
            renderer.sprite = frames[idx];
        }
    }

    /// <summary>
    /// 장착된 다리 파츠에 맞는 비주얼로 바꾼다. PlayerRobotController가 장비가 바뀔 때(정비 화면
    /// 등)마다 호출한다 - SetRoll()처럼 즉시 반영되고, 다음 Update()에서 실제로 그려진다.
    /// </summary>
    public void SetLegVisual(LegVisualMode mode)
    {
        if (legVisualMode == mode) return;
        legVisualMode = mode;
        if (built) EnsureAltVisualBuilt();
    }

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
    public void SetRoll(bool active, float spinDegrees, float hopProgressOverride = -1f)
    {
        rollActive = active;
        rollSpinDegrees = spinDegrees;
        rollHopProgressOverride = hopProgressOverride;
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

        // 다리 2개. phaseOffset(0 / 0.5)만 다르고 그 외 규칙(굽힘 부호·그림 방향)은 두 다리가
        // 완전히 동일하다 — 사람처럼. 앞다리는 고관절 자체를 frontLegPullBack만큼 뒤다리 쪽
        // (중심)으로 당긴다 - 대기/보행 구분 없이 다리 전체(뼈·발 모두)가 통째로 이동한다
        // (idle 타겟만 조정하는 것과 다르다). 이 위치·위상 배정 자체는 바꾸지 않는다.
        //
        // 밝기/그리기 순서(Leg_Front=밝고 앞에 그려짐, Leg_Back=어둡고 뒤에 그려짐)는 반대로
        // 뒤집었다(2026-08-12, "왼쪽을 볼 때 오른쪽 다리가 더 밝아야 한다"는 사용자 지정) -
        // legsGroup이 facingSign만큼 좌우로 뒤집히므로, 로컬 -X 쪽 다리는 오른쪽을 바라볼 때
        // 화면 왼쪽에, 왼쪽을 바라볼 때 화면 오른쪽에 온다. 그 다리를 밝게(Leg_Front) 하면
        // "오른쪽을 볼 때 왼쪽 다리가 밝고, 왼쪽을 볼 때 오른쪽 다리가 밝다"는 요청과 일치한다.
        legs[0] = BuildLeg("Leg_Front", bodySortingOrder - 5, -hipSeparation * 0.5f, 0.5f, Color.white,
                           thighHip, thighTilt, shinKnee, shinTilt, footAnkle);
        legs[1] = BuildLeg("Leg_Back", bodySortingOrder - 10, hipSeparation * 0.5f - frontLegPullBack, 0f, backLegTint,
                           thighHip, thighTilt, shinKnee, shinTilt, footAnkle);

        // 리그를 다시 조립하면 rigRoot가 통째로 새로 생기므로 트레드/로켓/거미 자식도 다시 붙여야 한다.
        treadRoot = null;
        rocketRoot = null;
        spiderRoot = null;
        for (int i = 0; i < spiderLegs.Length; i++) spiderLegs[i] = null;
        EnsureAltVisualBuilt();

        built = true;
        Apply(0f, Vector2.zero);   // 첫 프레임부터 올바른 자세로 서 있게
    }

    /// <summary>
    /// 캐터필러/로켓 비주얼(관절 없이 프레임만 재생)을 필요할 때 만든다. legVisualMode가 Biped가
    /// 아니면 그 종류의 자식을 만들어 두고, 나머지는 비활성화한다. 매번 새로 만들지 않고
    /// 한 번 만든 뒤 SetActive만 토글한다(자주 안 바뀌는 값이지만 정비 화면에서 바뀔 수 있다).
    /// </summary>
    private void EnsureAltVisualBuilt()
    {
        if (rigRoot == null) return; // Build() 도중(리그 자체가 아직 없음)

        if (legVisualMode == LegVisualMode.Tread && treadRoot == null) treadRoot = BuildTreadVisual();
        if (legVisualMode == LegVisualMode.Rocket && rocketRoot == null) rocketRoot = BuildRocketVisual();
        if (legVisualMode == LegVisualMode.Spider && spiderRoot == null) spiderRoot = BuildSpiderVisual();

        if (treadRoot != null) treadRoot.gameObject.SetActive(legVisualMode == LegVisualMode.Tread);
        if (rocketRoot != null) rocketRoot.gameObject.SetActive(legVisualMode == LegVisualMode.Rocket);
        if (spiderRoot != null) spiderRoot.gameObject.SetActive(legVisualMode == LegVisualMode.Spider);

        bool hideLegs = legVisualMode != LegVisualMode.Biped;
        if (legsGroup != null && legsGroup.gameObject.activeSelf == hideLegs) legsGroup.gameObject.SetActive(!hideLegs);

        ApplyAltVisualFacing();
    }

    // 머리 밑단 타원을 다리 유닛의 "윗면 개구부 타원 앞림"에 앉히기 위한 시트 정렬 상수
    // (원본 250px 캔버스 픽셀 실측, 2026-08-23):
    //  - 머리(Body.png) 밑단 외곽선 곡선의 중심 y = 195
    //  - 캐터필러 하우징(b_f0000) 윗면 개구부 앞림 y = 150  →  완전 정합에서 45px 올림
    //  - 로켓 허브(0.png, 0번 프레임) 윗면 개구부 앞림 y = 162.5  →  32.5px 올림
    // 로켓 허브는 프레임마다 최대 10px 오르내리는 호버링 애니메이션이지만, 개구부 타원 높이
    // (37px)보다 작아 밑단이 개구부 안에서 노는 정도로 흡수된다(0번 프레임 기준으로 고정).
    private const float TreadHeadSeatRaise = 0.45f;   // = 45px / PPU 100 (리그 로컬 단위)
    private const float RocketHeadSeatRaise = 0.325f; // = 32.5px / PPU 100

    // 좌우(x) 시트 정렬(사용자 확정: "몸통과 다리의 몸통 보조부분이 일자로 딱 맞아야해").
    // 원본 250px 캔버스 가로 실측(행 스캔): 머리 몸통 벽 [70,189] → 중심 129.5,
    // 캐터필러 컵 [78,184] → 중심 131, 로켓 허브 [70,180] → 중심 125.
    // 컵/허브 중심이 머리 몸통 중심에 오도록 다리 루트를 (머리중심 - 다리중심)/100 만큼 민다.
    private const float TreadSeatXOffset = -0.015f;  // (129.5 - 131) / 100
    private const float RocketSeatXOffset = 0.045f;  // (129.5 - 125) / 100

    /// <summary>
    /// 대체 다리(캐터필러/로켓/거미) 비주얼의 좌우 반전과 x 시트 오프셋을 <b>머리와 동기화</b>한다.
    ///
    /// 머리 스프라이트는 facingSign &gt; 0일 때 뒤집히는데(ApplyBodyFacing의 flipX 조건),
    /// 예전 코드는 다리 루트에 facingSign을 그대로 스케일로 넣어 <b>부호 관례가 정반대</b>였다 -
    /// 머리가 뒤집히는 순간 다리는 안 뒤집혀서(또는 그 반대) 캔버스에 조립된 좌우 관계가
    /// 깨지고, 컵/허브가 머리 옆으로 미끄러져 보였다(사용자 리포트: "좌우 어긋남").
    /// 게다가 캐터필러/로켓 루트는 생성 시점에 반전을 아예 적용하지 않아 첫 방향 전환 전까지
    /// 항상 원본 방향이었다. 이제 머리와 같은 조건으로 함께 뒤집고, x 시트 오프셋도 반전에
    /// 맞춰 부호를 뒤집는다(반전은 rigRoot 원점 기준이라 localPosition은 스스로 안 뒤집힌다).
    /// </summary>
    private void ApplyAltVisualFacing()
    {
        float mirror = facingSign > 0f ? -1f : 1f; // bodyRenderer.flipX와 같은 조건
        if (treadRoot != null)
        {
            treadRoot.localScale = new Vector3(mirror, 1f, 1f);
            treadRoot.localPosition = new Vector3(TreadSeatXOffset * mirror, treadRoot.localPosition.y, 0f);
        }
        if (rocketRoot != null)
        {
            rocketRoot.localScale = new Vector3(mirror, 1f, 1f);
            rocketRoot.localPosition = new Vector3(RocketSeatXOffset * mirror, rocketRoot.localPosition.y, 0f);
        }
        if (spiderRoot != null) spiderRoot.localScale = new Vector3(mirror, 1f, 1f);
    }

    /// <summary>
    /// 캐터필러/로켓 장착 시 머리(몸통 스프라이트)를 다리 유닛에 <b>이미지 기준으로</b> 붙이는
    /// 보정량(리그 로컬 단위, 음수 = 아래로). 두 단계로 계산한다:
    ///
    /// (1) <b>캔버스 정합</b> - 작가가 준 캐터필러/로켓 아트는 머리(Parts/Body.png)와 같은
    /// 250px 캔버스에 그려져 있는데, 머리 스프라이트는 2족 보행용으로 "고관절 앵커
    /// (bodyHipAnchor)가 bodyPivot에 오도록" 캔버스 중심보다 (0.5 - anchor.y) x 높이만큼
    /// 위로 밀려 있다(ApplyBodyFacing 참고). 다리 프레임은 캔버스 중심이 그대로 standHipY에
    /// 놓이므로 그 밀린 양을 먼저 되돌린다.
    ///
    /// (2) <b>시트 정렬</b> - 완전 정합은 머리 밑단이 다리 안으로 약 80px 파묻혀 "다리의
    /// 아래쪽 타원"에 합쳐진 모습이 된다(사용자 재지적: "아래쪽 동그라미가 아니라 위쪽면
    /// 동그라미에 합쳐야지"). 그래서 머리 밑단 곡선이 <b>윗면 개구부 타원의 앞림</b>과
    /// 포개지는 높이까지 위 상수만큼 다시 올린다. 머리는 다리보다 앞 레이어이므로
    /// (BuildTreadVisual/BuildRocketVisual 참고) 밑단 타원이 개구부 위에 그대로 보이고,
    /// 개구부의 뒷림은 머리 양옆으로 살짝 드러난다 - 사용자 레퍼런스 그림과 같은 구성.
    ///
    /// 거미는 원본 캔버스 규격(827x509)이 달라 이 정합이 성립하지 않는다(spiderTorso*로 별도
    /// 조정). 머리가 내려간 만큼 무기 소켓도 같이 내려와야 하는데, 소켓은 리그 밖(Player 직속)
    /// 이라 리그가 못 옮긴다 - PlayerShootManager가 HeadCanvasWorldOffsetY를 읽어 처리한다.
    /// </summary>
    private float AltCanvasAlignY()
    {
        float seatRaise;
        if (legVisualMode == LegVisualMode.Tread) seatRaise = TreadHeadSeatRaise;
        else if (legVisualMode == LegVisualMode.Rocket) seatRaise = RocketHeadSeatRaise;
        else return 0f;
        if (bodySprite == null) return 0f;

        // ApplyBodyFacing이 실제로 쓰는 앵커(머리별 보정 반영)를 그대로 되돌려야 캔버스가 정확히 포개진다.
        return -(0.5f - EffectiveBodyHipAnchorY()) * bodySprite.rect.height / bodySprite.pixelsPerUnit + seatRaise;
    }

    /// <summary>
    /// 머리가 authored 기준 위치보다 내려간 양(월드 단위, 내려갔으면 음수). 무기 소켓
    /// (RigingPoint, 리그 밖 Player 직속)이 머리 귀 옆 높이를 유지하도록 PlayerShootManager가
    /// 매 프레임 읽어 소켓을 같은 만큼 내린다.
    ///
    /// 두 가지가 합산된다: 캐터필러/로켓의 캔버스 정합(<see cref="AltCanvasAlignY"/>, Biped/Spider
    /// 에서는 0)과, 머리 그림이 캔버스 안에서 위쪽에 그려져 생긴 보정
    /// (<see cref="HeadArtSeatAlignY"/>, 기본 머리에서는 0). 둘 다 "머리 그림이 실제로 이동한 양"
    /// 이므로 소켓도 같은 만큼 따라가야 귀 옆에 남는다.
    /// </summary>
    public float HeadCanvasWorldOffsetY
    {
        get
        {
            if (rigRoot == null) return 0f;
            float local = AltCanvasAlignY() + HeadArtSeatAlignY();
            return local == 0f ? 0f : rigRoot.TransformVector(new Vector3(0f, local, 0f)).y;
        }
    }

    /// <summary>
    /// 캐터필러(트레일다리) - 뒤/앞 트랙 2장을 몸통 밑 중앙에 겹쳐 그린다. 원본 PNG가 몸통(250px)과
    /// 같은 캔버스 규격이라(사용자가 이 리그 기준으로 그렸다) 위치·스케일 보정 없이 rigRoot
    /// 밑에 몸통과 같은 자리(standHipY)에 놓기만 하면 비례가 맞는다.
    /// </summary>
    private Transform BuildTreadVisual()
    {
        LoadTreadFrames();

        Transform root = new GameObject("TreadVisual").transform;
        root.SetParent(rigRoot, false);
        root.localPosition = new Vector3(0f, standHipY, 0f);

        // 다리(하우징+트랙)는 전부 머리(bodySortingOrder)보다 **뒤**에 그린다(사용자 확정
        // 레퍼런스: 머리가 다리 레이어보다 앞이고, 하우징의 윗면 개구부 림은 머리 뒤로 살짝
        // 보인다). 머리 밑단이 개구부에 앉는 모습은 레이어가 아니라 AltCanvasAlignY()의 시트
        // 정렬(머리 밑단 타원 = 개구부 앞림)이 만든다.
        treadBack = new FrameCycler { renderer = CreateFrameRenderer(root, "Back", bodySortingOrder - 2), frames = treadBackFrames };
        treadFront = new FrameCycler { renderer = CreateFrameRenderer(root, "Front", bodySortingOrder - 1), frames = treadFrontFrames };
        // Update()가 아직 한 번도 안 돌았어도(예: 정비 화면처럼 timeScale=0인 상태에서 장비를
        // 바꾸는 경우) sprite가 null인 첫 프레임이 보이지 않도록 즉시 0번 프레임을 채운다.
        treadBack.ShowFrame(0f);
        treadFront.ShowFrame(0f);
        return root;
    }

    /// <summary>
    /// 로켓 추진기 - 화염(맨 뒤) → 베이스(호버링 바디) → 노즐(맨 앞) 순으로 겹쳐 그린다.
    /// 세 레이어가 같은 프레임 인덱스를 공유해야 원본이 의도한 모양이 나온다(사용자가 이미
    /// 완성해서 넘겨준 애니메이션을 그대로 재생하는 것뿐이라 리깅이 필요 없다).
    /// </summary>
    private Transform BuildRocketVisual()
    {
        LoadRocketFrames();

        Transform root = new GameObject("RocketVisual").transform;
        root.SetParent(rigRoot, false);
        root.localPosition = new Vector3(0f, standHipY, 0f);

        // 캐터필러와 같은 규칙 - 다리(허브/노즐/화염)는 전부 머리보다 뒤. 머리가 허브 윗면
        // 개구부에 앉는 모습은 AltCanvasAlignY()의 시트 정렬이 만든다.
        rocketFlame = new FrameCycler { renderer = CreateFrameRenderer(root, "Flame", bodySortingOrder - 3), frames = rocketFlameFrames };
        rocketBase = new FrameCycler { renderer = CreateFrameRenderer(root, "Base", bodySortingOrder - 2), frames = rocketBaseFrames };
        rocketNozzle = new FrameCycler { renderer = CreateFrameRenderer(root, "Nozzle", bodySortingOrder - 1), frames = rocketNozzleFrames };
        rocketFlame.ShowFrame(0f);
        rocketBase.ShowFrame(0f);
        rocketNozzle.ShowFrame(0f);
        return root;
    }

    private SpriteRenderer CreateFrameRenderer(Transform parent, string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    private static Sprite LoadFrame(string path)
    {
        Sprite s = Resources.Load<Sprite>(path);
        if (s == null) Debug.LogWarning($"ProceduralCharacterRig: 프레임 스프라이트를 찾을 수 없습니다 - Resources/{path}");
        return s;
    }

    private static void LoadTreadFrames()
    {
        if (treadBackFrames != null && treadFrontFrames != null) return;
        treadBackFrames = new Sprite[6];
        treadFrontFrames = new Sprite[6];
        for (int i = 0; i < 6; i++)
        {
            treadBackFrames[i] = LoadFrame($"{ResourceFolder}Tread/b_f000{i}");
            treadFrontFrames[i] = LoadFrame($"{ResourceFolder}Tread/f_f000{i}");
        }
    }

    private static void LoadRocketFrames()
    {
        if (rocketBaseFrames != null && rocketNozzleFrames != null && rocketFlameFrames != null) return;
        rocketBaseFrames = new Sprite[6];
        rocketNozzleFrames = new Sprite[6];
        rocketFlameFrames = new Sprite[6];
        for (int i = 0; i < 6; i++)
        {
            rocketBaseFrames[i] = LoadFrame($"{ResourceFolder}Rocket/{i}");
            rocketNozzleFrames[i] = LoadFrame($"{ResourceFolder}Rocket/{i}b");
            rocketFlameFrames[i] = LoadFrame($"{ResourceFolder}Rocket/{i}fx");
        }
    }

    /// <summary>
    /// 거미 다리(4개 실제 IK 다리) 조립. 앵커 좌표는 Prototype/SpiderLegRig.html에서 픽셀 단위로
    /// 실측하고 사용자와 함께 검증한 값을 그대로 쓴다 - upper_leg.png(405x213): 고관절 볼
    /// 중심(52.5,83.5)→무릎 오렌지 링 중심(281.3,125.1). lower_leg.png(458x210): 무릎 볼
    /// 중심(52.5,76.0)→발톱 끝(444,195). 원본 측정은 PIL(위가 0)이라 Unity 정규화 앵커
    /// (아래가 0)로 쓰려면 y를 뒤집어야 한다(1 - pixelY/height) - AnchorToLocal/TiltToDown은
    /// 2족 다리와 동일한 헬퍼를 그대로 재사용한다.
    /// </summary>
    private Transform BuildSpiderVisual()
    {
        if (spiderUpperSprite == null) spiderUpperSprite = Resources.Load<Sprite>(ResourceFolder + "Spider/upper_leg");
        if (spiderLowerSprite == null) spiderLowerSprite = Resources.Load<Sprite>(ResourceFolder + "Spider/lower_leg");
        if (spiderTorsoSprite == null) spiderTorsoSprite = Resources.Load<Sprite>(ResourceFolder + "Spider/body");
        if (spiderUpperSprite == null || spiderLowerSprite == null)
        {
            Debug.LogError("ProceduralCharacterRig: 거미 다리 스프라이트를 찾을 수 없습니다 - " +
                           "Resources/Parts/Spider/upper_leg, lower_leg");
            return null;
        }

        Vector2 hipAnchorN = new Vector2(52.5f / 405f, 1f - 83.5f / 213f);
        Vector2 kneeAnchorUpperN = new Vector2(281.3f / 405f, 1f - 125.1f / 213f);
        Vector2 kneeAnchorLowerN = new Vector2(52.5f / 458f, 1f - 76f / 210f);
        Vector2 footAnchorN = new Vector2(444f / 458f, 1f - 195f / 210f);

        Vector2 hipLocalOnSprite = AnchorToLocal(spiderUpperSprite, hipAnchorN);
        Vector2 kneeLocalOnUpper = AnchorToLocal(spiderUpperSprite, kneeAnchorUpperN);
        Vector2 kneeLocalOnLower = AnchorToLocal(spiderLowerSprite, kneeAnchorLowerN);
        Vector2 footLocalOnLower = AnchorToLocal(spiderLowerSprite, footAnchorN);

        // 스프라이트 원본 기준 뼈 길이(배율 미적용). 실제 길이는 다리별 원근 배율을 곱해서 쓴다.
        float thighLengthBase = Vector2.Distance(hipLocalOnSprite, kneeLocalOnUpper);
        float shinLengthBase = Vector2.Distance(kneeLocalOnLower, footLocalOnLower);

        float thighTilt = TiltToDown(kneeLocalOnUpper - hipLocalOnSprite);
        float shinTilt = TiltToDown(footLocalOnLower - kneeLocalOnLower);

        Transform root = new GameObject("SpiderVisual").transform;
        root.SetParent(rigRoot, false); // 좌우 반전은 EnsureAltVisualBuilt → ApplyAltVisualFacing이 담당

        // 몸통(다리 밑판) - 사용자 지적(2026-08-21): "거미다리 밑에 몸통은 왜 없앴니 그것도
        // 포함시켜야지". 거미 다리 파츠 세트에 원래 포함된 body.png(호버 패드 하우징)를
        // 다리와 머리 사이에 끼운다. 이 그림 자체에 작은 원통 손잡이(장식용 스텀프)가 top-center에
        // 있지만 실제 로봇 머리보다 훨씬 작고 얼굴이 없어서, 머리를 그 위(=더 앞, 높은
        // sortingOrder)에 그리면 자연스럽게 가려진다 - 별도로 잘라낼 필요가 없었다.
        if (spiderTorsoSprite != null)
        {
            GameObject torsoGo = new GameObject("Torso");
            torsoGo.transform.SetParent(root, false);
            torsoGo.transform.localPosition = new Vector3(0f, standHipY + spiderTorsoYOffset, 0f);
            torsoGo.transform.localScale = new Vector3(spiderTorsoScale, spiderTorsoScale, 1f);
            SpriteRenderer torsoRenderer = torsoGo.AddComponent<SpriteRenderer>();
            torsoRenderer.sprite = spiderTorsoSprite;
            torsoRenderer.sortingLayerName = sortingLayerName;
            // 프로토타입 draw() 순서: 뒷다리 → 밑판 → 앞다리. 밑판이 그 사이에 끼어야
            // 뒷다리는 몸통 뒤로, 앞다리는 몸통 앞으로 보이는 컨셉 아트의 깊이감이 나온다.
            torsoRenderer.sortingOrder = bodySortingOrder - 4;
        }

        // 4개 고관절. 위치는 밑판(body.png) 원본 픽셀 비율에서 뽑으므로 밑판 크기를 바꾸면
        // 저절로 따라온다. 고관절 자체는 보행 중에도 고정이고(숨쉬기/보행 bob이 없다), 발만 움직인다.
        float torsoW = spiderTorsoSprite != null ? spiderTorsoSprite.bounds.size.x * spiderTorsoScale : 1.65f;
        float torsoH = spiderTorsoSprite != null ? spiderTorsoSprite.bounds.size.y * spiderTorsoScale : 1.02f;
        float torsoCenterY = standHipY + spiderTorsoYOffset;

        // 프로토타입 LEG_DEFS 순서: RL(뒤-좌), RR(뒤-우), FL(앞-좌), FR(앞-우).
        // 대각선 걸음 짝(GAIT_GROUPS): 뒤좌+앞우 = 짝0, 뒤우+앞좌 = 짝1
        bool[] rearOf = { true, true, false, false };
        float[] sideOf = { -1f, +1f, -1f, +1f };
        int[] pairOf = { 0, 1, 1, 0 };

        for (int i = 0; i < 4; i++)
        {
            bool isRear = rearOf[i];
            float side = sideOf[i];
            bool mirror = side < 0f;   // 프로토타입 drawPivoted(mirror = hip.x < body.x)와 동일 조건

            float legScale = spiderLegScale * (isRear ? SpiderRearLegScale : SpiderFrontLegScale);
            float hipX = side * (isRear ? SpiderRearHipXRatio : SpiderFrontHipXRatio) * torsoW;
            float hipY = torsoCenterY + (isRear ? +SpiderRearHipYRatio : -SpiderFrontHipYRatio) * torsoH;

            var leg = new SpiderLeg
            {
                pairIndex = pairOf[i],
                isRear = isRear,
                hipLocalPos = new Vector2(hipX, hipY),
                thighLength = thighLengthBase * legScale,
                shinLength = shinLengthBase * legScale,
            };

            // 대기 자세 발 목표: 바깥으로(spread)와 아래로(drop)를 따로 정하는 것이 핵심이다 -
            // 길이 하나로 정하면 발이 수평으로 멀리 나가 발톱이 눕고 기어가는 모양이 된다.
            float reach = leg.thighLength + leg.shinLength;
            float dx = reach * (isRear ? SpiderRearSpread : SpiderFrontSpread) * spiderStanceSpreadRatio;
            float dy = -reach * (isRear ? SpiderRearDrop : SpiderFrontDrop) * spiderStanceDropRatio;

            // 목표가 다리 도달 범위를 벗어나면 방향을 유지한 채 길이만 줄인다(프로토타입과 동일).
            float chord = Mathf.Sqrt(dx * dx + dy * dy);
            float clamped = Mathf.Clamp(chord, Mathf.Abs(leg.thighLength - leg.shinLength) * 1.06f, reach * 0.985f);
            if (chord > 1e-5f && !Mathf.Approximately(clamped, chord))
            {
                float k = clamped / chord;
                dx *= k; dy *= k;
            }
            leg.idealLocalPos = leg.hipLocalPos + new Vector2(side * dx, dy);

            // 왼쪽 다리는 스프라이트를 좌우로 뒤집는다. flipX만 켜면 앵커(고관절 볼)가 반대편으로
            // 가버리므로 <b>앵커도 함께 미러링</b>하고 기울기 보정각도 부호를 뒤집어야 한다
            // (2족 발의 MaybeMirrorX + footSpriteFlipX와 같은 원리).
            Vector2 thighAnchor = mirror
                ? AnchorToLocal(spiderUpperSprite, new Vector2(1f - hipAnchorN.x, hipAnchorN.y))
                : hipLocalOnSprite;
            Vector2 shinAnchor = mirror
                ? AnchorToLocal(spiderLowerSprite, new Vector2(1f - kneeAnchorLowerN.x, kneeAnchorLowerN.y))
                : kneeLocalOnLower;

            int shinOrder = isRear ? bodySortingOrder - 6 : bodySortingOrder - 3;
            int thighOrder = isRear ? bodySortingOrder - 5 : bodySortingOrder - 2;

            leg.hip = new GameObject("Hip_" + i).transform;
            leg.hip.SetParent(root, false);
            leg.hip.localPosition = new Vector3(leg.hipLocalPos.x, leg.hipLocalPos.y, 0f);
            AttachVisual(leg.hip, "Thigh", spiderUpperSprite, thighAnchor, mirror ? -thighTilt : thighTilt,
                        thighOrder, Color.white, mirror, legScale);

            leg.knee = new GameObject("Knee_" + i).transform;
            leg.knee.SetParent(leg.hip, false);
            leg.knee.localPosition = new Vector3(0f, -leg.thighLength, 0f);
            AttachVisual(leg.knee, "Shin", spiderLowerSprite, shinAnchor, mirror ? -shinTilt : shinTilt,
                        shinOrder, Color.white, mirror, legScale);

            // 발을 대기 자세 목표 위치에 "심는다" - 월드 좌표로 저장(위 필드 설명 참고).
            leg.footWorldPos = root.TransformPoint(new Vector3(leg.idealLocalPos.x, leg.idealLocalPos.y, 0f));

            spiderLegs[i] = leg;
            SolveSpiderLeg(leg, leg.idealLocalPos); // 첫 프레임부터 올바른 자세로
        }

        return root;
    }

    /// <summary>
    /// 거미 다리 2관절 IK. hip은 고정, foot을 목표로 코사인 법칙으로 무릎을 구한다. 무릎이 부풀 수
    /// 있는 두 해 중 <b>더 위쪽(y가 큰) 무릎</b>을 고른다 - 프로토타입의 <c>solveIKArchUp</c>과 같은
    /// 규칙이며, 이래야 거미처럼 관절이 위로 솟고 발톱이 아래로 내려오는 자세가 된다.
    /// (2026-08-24 수정: 예전에는 "몸통 중심에서 더 먼 쪽"을 골라 무릎이 <b>반대로 꺾여</b> 있었다 -
    /// 프로토타입과 다른 규칙이 들어가 있던 것이 원인이었다.)
    /// footLocal은 호출자가 이미 로컬 공간으로 변환해 건네준 발 목표다(들려 있는 동안은 살짝
    /// 위로 보정된 값이 들어온다).
    /// </summary>
    private void SolveSpiderLeg(SpiderLeg leg, Vector2 footLocal)
    {
        Vector2 hip = leg.hipLocalPos;
        Vector2 delta = footLocal - hip;

        float thigh = leg.thighLength;
        float shin = leg.shinLength;

        float maxReach = thigh + shin - 0.001f;
        float minReach = Mathf.Abs(thigh - shin) + 0.001f;
        float dist = Mathf.Clamp(delta.magnitude, minReach, maxReach);
        Vector2 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.down;
        Vector2 clampedTarget = hip + dir * dist;

        float baseAngle = Mathf.Atan2(dir.y, dir.x);
        float cosA = (thigh * thigh + dist * dist - shin * shin) / (2f * thigh * dist);
        float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

        float angleA = baseAngle + a;
        float angleB = baseAngle - a;
        Vector2 kneeA = hip + new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA)) * thigh;
        Vector2 kneeB = hip + new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB)) * thigh;

        bool useA = kneeA.y >= kneeB.y;   // 아치업: 더 위쪽 무릎
        Vector2 knee = useA ? kneeA : kneeB;
        float thighAngle = useA ? angleA : angleB;

        Vector2 toTarget = clampedTarget - knee;
        float shinAngle = Mathf.Atan2(toTarget.y, toTarget.x);

        float thighZ = thighAngle * Mathf.Rad2Deg + 90f;
        float shinZ = shinAngle * Mathf.Rad2Deg + 90f;

        leg.hip.localRotation = Quaternion.Euler(0f, 0f, thighZ);
        leg.knee.localRotation = Quaternion.Euler(0f, 0f, shinZ - thighZ);
    }

    /// <summary>
    /// 대각선 짝 반응형 걸음(Prototype/SpiderLegRig.html에서 검증한 방식) - 발이 목표 지점에서
    /// spiderStepThreshold 이상 벌어지면 그 다리가 스텝을 시작하되, <b>반대 짝이 이미 스텝
    /// 중이면 기다린다</b>. 항상 두 다리는 지면에 붙어 있어 미끄러지지 않고, 오차가 큰 다리부터
    /// 우선권을 준다.
    ///
    /// 발은 월드 좌표(footWorldPos)로 저장한다 - 몸통 기준 로컬 좌표로 들고 있으면 몸이 움직여도
    /// 발이 몸을 그대로 따라가 버려 오차가 절대 안 생긴다(반응형 걸음이 트리거되지 않는 버그).
    /// 매 프레임 현재 로컬 좌표(footLocalNow)를 새로 구해서 그걸로 오차를 재고 IK를 푼다.
    /// </summary>
    private void UpdateSpiderVisual(float dt, Vector2 velocity)
    {
        // 이동 방향은 월드 벡터라, 로컬(=몸통 기준, facingSign 반전이 걸려 있는) 공간으로
        // 옮겨야 스텝 오버슈트 방향이 실제 화면 이동 방향과 맞는다.
        Vector2 moveDirLocal = Vector2.zero;
        if (velocity.sqrMagnitude > 1f)
        {
            Vector3 localDir = spiderRoot.InverseTransformDirection(new Vector3(velocity.x, velocity.y, 0f));
            moveDirLocal = ((Vector2)localDir).normalized;
        }

        // ── 이동 속도에 맞춰 스텝을 빠르게 + 앞서 짚게 한다 (2026-08-25) ───────────────
        //
        // 예전에는 스텝 속도와 오버슈트가 <b>고정 상수</b>였다. 스텝 한 번에 걸리는 시간(1/11 =
        // 0.09초) 동안 몸은 계속 이동하는데 발의 착지 지점은 <b>스텝을 시작한 순간의 대기 위치</b>
        // 로 굳어 있어서, 빠르게 달릴수록 착지하자마자 그 발이 이미 몸 뒤로 밀려 있었다. 그러면
        // 곧바로 다시 스텝 조건에 걸리고, 네 다리가 전부 뒤처진 채 대각선 교대 순서를 기다리게
        // 되어 <b>발이 땅에 끌려다니는 것처럼</b> 보인다(2026-08-25 사용자 리포트).
        //
        // 두 가지로 고친다. (1) 빠를수록 스텝을 빨리 끝내고, (2) 스텝하는 동안 몸이 이동할 거리를
        // 착지 지점에 미리 얹어 <b>앞서 짚게</b> 한다 - 실제 보행이 하는 일과 같다. 정지 상태
        // (speed = 0)에서는 두 값 모두 예전 상수 그대로가 되므로 서 있을 때의 모습은 바뀌지 않는다.
        float speed = velocity.magnitude;
        float stepSpeed = spiderStepSpeed * (1f + Mathf.Max(0f, spiderStepSpeedPerSpeed) * speed);

        // 앞서 짚을 거리는 <b>스텝 한 번이 아니라 교대 한 바퀴</b> 동안 몸이 가는 거리로 잡는다.
        // 네 다리가 대각선 두 쌍으로 번갈아 딛으므로(pairBusy), 한 발이 다시 자기 차례를 받기까지
        // 스텝 시간의 약 2배가 걸린다. 스텝 시간만 보고 계산했더니 실측에서 발 오차가 임계값
        // (0.16)의 6~11배인 1.0~1.8유닛으로 남아 여전히 끌려 보였다.
        // 오차가 임계값의 이 배수를 넘으면 대각선 교대 순서를 무시하고 즉시 딛는다.
        const float SpiderUrgentStepFactor = 2.5f;

        const float StepCycleFactor = 2f;
        float cycleSeconds = StepCycleFactor / Mathf.Max(0.01f, stepSpeed);
        float stepLead = Mathf.Min(spiderStepLeadMax, speed * cycleSeconds * spiderStepLeadRatio);
        float stepReach = spiderStepOvershoot + stepLead;

        var footLocalNow = new Vector2[spiderLegs.Length];
        for (int i = 0; i < spiderLegs.Length; i++)
            footLocalNow[i] = spiderRoot.InverseTransformPoint(spiderLegs[i].footWorldPos);

        bool[] pairBusy = { false, false };
        foreach (SpiderLeg leg in spiderLegs) if (leg.stepping) pairBusy[leg.pairIndex] = true;

        int[] order = { 0, 1, 2, 3 };
        System.Array.Sort(order, (x, y) =>
            Vector2.Distance(footLocalNow[y], spiderLegs[y].idealLocalPos)
                .CompareTo(Vector2.Distance(footLocalNow[x], spiderLegs[x].idealLocalPos)));

        foreach (int i in order)
        {
            SpiderLeg leg = spiderLegs[i];
            if (leg.stepping) continue;
            float err = Vector2.Distance(footLocalNow[i], leg.idealLocalPos);
            if (err <= spiderStepThreshold) continue;

            // 평소에는 대각선 두 쌍이 번갈아 딛는다(반대 짝이 스텝 중이면 기다린다). 다만
            // <b>이미 크게 뒤처진 발은 순서를 기다리지 않고 즉시 딛는다</b> - 빠르게 달릴 때는
            // 교대를 기다리는 시간 자체가 병목이라, 실측에서 발 오차가 임계값의 9배(1.5유닛,
            // 다리 길이보다 길다)까지 벌어진 채 유지됐다. 그 상태가 곧 "발이 땅에 끌려다니는"
            // 모습이다. 순서를 지키는 것보다 발이 몸을 따라가는 쪽이 낫다.
            if (err <= spiderStepThreshold * SpiderUrgentStepFactor && pairBusy[1 - leg.pairIndex]) continue;

            leg.stepping = true;
            leg.stepT = 0f;
            leg.stepFromWorld = leg.footWorldPos;
            Vector2 stepToLocal = leg.idealLocalPos + moveDirLocal * stepReach;
            leg.stepToWorld = spiderRoot.TransformPoint(new Vector3(stepToLocal.x, stepToLocal.y, 0f));
            pairBusy[leg.pairIndex] = true;
        }

        foreach (SpiderLeg leg in spiderLegs)
        {
            if (!leg.stepping) continue;

            leg.stepT += dt * stepSpeed;
            if (leg.stepT >= 1f)
            {
                leg.stepT = 1f;
                leg.footWorldPos = leg.stepToWorld;
                leg.stepping = false;
            }
            else
            {
                float t = leg.stepT;
                float ease = t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                leg.footWorldPos = Vector3.Lerp(leg.stepFromWorld, leg.stepToWorld, ease);
            }
        }

        for (int i = 0; i < spiderLegs.Length; i++)
        {
            SpiderLeg leg = spiderLegs[i];
            Vector2 local = spiderRoot.InverseTransformPoint(leg.footWorldPos);
            float lift = leg.stepping ? Mathf.Sin(Mathf.PI * Mathf.Min(1f, leg.stepT)) * spiderFootLiftHeight : 0f;
            SolveSpiderLeg(leg, new Vector2(local.x, local.y + lift)); // IK만 살짝 들어올려서 푼다(실제 심긴 위치는 안 바뀜)
        }
    }

    /// <summary>
    /// 이동 속도의 x 부호로 좌우 방향을 정한다. 확실히 움직일 때만 바꿔서 제자리에서 떨리지
    /// 않게 하고, 스케일 보간 없이 즉시 전환한다 - 몸통(flipX)·다리(legsGroup 부호) 둘 다
    /// 찌그러지는 중간 프레임이 없다.
    /// </summary>
    private void UpdateFacing(Vector2 velocity)
    {
        float newFacing = facingSign;
        if (Mathf.Abs(velocity.x) > 0.15f) newFacing = Mathf.Sign(velocity.x);
        if (Mathf.Approximately(newFacing, facingSign)) return;

        facingSign = newFacing;

        // 다리 비주얼을 바꾸는 도중(SetLegVisual)이나 씬 전환 프레임에는 조각이 아직/이미 없을 수
        // 있다. 이 메서드는 구르는 동안에도 매 프레임 불리므로 방어적으로 확인한다.
        if (bodyRenderer != null && bodyVisual != null && bodySprite != null) ApplyBodyFacing();
        if (legsGroup != null) legsGroup.localScale = new Vector3(facingSign, 1f, 1f);
        ApplyAltVisualFacing();
    }

    /// <summary>몸통의 flipX와 앵커 위치를 facingSign에 맞춰 갱신한다. flipX는 픽셀만
    /// 좌우로 뒤집으므로, 배치에 쓰는 앵커도 같이 뒤집어야 위치가 어긋나지 않는다
    /// (발의 MaybeMirrorX와 같은 원리).</summary>
    private void ApplyBodyFacing()
    {
        bool flip = facingSign > 0f;
        bodyRenderer.flipX = flip;
        float anchorY = EffectiveBodyHipAnchorY();
        Vector2 anchor = flip ? new Vector2(1f - bodyHipAnchor.x, anchorY) : new Vector2(bodyHipAnchor.x, anchorY);
        Vector3 anchorPos = -(Vector3)AnchorToLocal(bodySprite, anchor);
        bodyVisual.localPosition = new Vector3(anchorPos.x, anchorPos.y, 0f);
    }

    // ── 머리 그림이 캔버스 안에서 차지하는 세로 위치 보정 (2026-08-25) ──────────────────
    //
    // <b>문제</b>: bodyHipAnchor.y(0.21)는 "캔버스 아래에서 21% 지점이 고관절"이라는 고정
    // 상수인데, 이 값은 기본 머리(Parts/Body.png)의 그림 밑단(캔버스 아래에서 51px = 20.4%)에
    // 맞춰 튜닝된 값이다. 머리 12종은 전부 250x250 규격이지만 그 <b>캔버스 안에서 그림이
    // 그려진 높이는 제각각</b>이라(메테우스는 밑단이 아래에서 85px, 가드맨은 77px), 같은 앵커를
    // 쓰면 그림이 위쪽에 그려진 머리일수록 다리에서 떠 보인다
    // (2026-08-25 사용자 리포트: "메테우스 헬멧일때 기본 다리와 머리가 너무 멀리 떨어져 있다").
    // 스프린터가 좀비보다 작아 보였던 것과 같은 종류의 함정이다 - 규격 픽셀 수가 같아도
    // 캔버스를 채운 정도는 다르다.
    //
    // <b>해결</b>: 앵커를 그림 밑단에서 역산한다. 기준 머리에서 "앵커가 밑단보다 얼마나 위인가"
    // (여유 간격)를 실측해 두고, 어떤 머리든 그 간격을 유지하도록 앵커 y를 다시 구한다.
    // 기준 머리 자신은 계산 결과가 authored 값과 픽셀 단위로 같아지므로 <b>기존 동작에 회귀가 없다</b>.
    //
    // 그림 밑단은 <b>PNG 알파를 한 번 실측해 굳힌 표</b>(HeadArtBottomRatio)로 갖고 있다.
    // Tight 스프라이트 메시(<c>sprite.vertices</c>)로 읽어보기도 했지만 Unity가 메시를 알파보다
    // 넉넉하게 부풀려서 - 메테우스는 알파 밑단이 캔버스 아래 85px인데 메시는 53px까지 내려온다 -
    // 보정량이 실제 필요량의 1/8밖에 나오지 않았다. 알파를 런타임에 직접 읽으려면 isReadable을
    // 켜야 하는데 머리 19장의 텍스처 사본을 메모리에 상주시킬 이유가 없어 표로 굳혔다.
    //
    // <b>새 머리를 추가하면 이 표에 한 줄을 더한다.</b> 값은 "캔버스 아래에서 그림 밑단까지의
    // 픽셀 ÷ 캔버스 높이"이고, 표에 없는 머리는 보정 없이(= 예전 동작 그대로) 지나간다.

    /// <summary>머리 스프라이트별 그림 밑단 비율(0 = 캔버스 맨 아래, 1 = 맨 위). 2026-08-25 알파 실측값.</summary>
    private static readonly Dictionary<string, float> HeadArtBottomRatio = new Dictionary<string, float>
    {
        { "Body",            0.2040f }, // 기준 머리(= 컴스톡 MK-01). 밑단 51px, authored 앵커 0.21이 이 값에 맞춰져 있다
        { "ComstockMk01",    0.2040f },
        { "Berserker",       0.1480f },
        { "FanBot",          0.0680f },
        { "Guardman",        0.3080f },
        { "HappyPixel",      0.2000f },
        { "HotPot",          0.1520f },
        { "Meteus",          0.3400f }, // 밑단 85px - 기본보다 34px 높아 다리에서 떠 보였다(이번 리포트)
        { "MiniPixie",       0.2320f },
        { "NeonEye_0",       0.2000f },
        { "NeonEye_1",       0.2000f },
        { "NeonEye_2",       0.2000f },
        { "NeonEye_3",       0.2000f },
        { "NeonEye_4",       0.2000f },
        { "NeonEye_5",       0.2000f },
        { "NeonEye_6",       0.2000f },
        { "NeonEye_7",       0.2000f },
        { "Pixie",           0.1080f },
        { "PrivateComstock", 0.1480f },
        { "SodaCan",         0.1200f },
    };

    /// <summary>기준 머리(Body)의 밑단 비율. 표가 바뀌어도 상수를 손댈 필요가 없게 표에서 읽는다.</summary>
    private const string ReferenceHeadSpriteName = "Body";

    /// <summary>지금 머리 그림에 맞는 고관절 앵커 y(0~1). 표에 없는 머리는 authored 값 그대로.</summary>
    private float EffectiveBodyHipAnchorY()
    {
        if (bodySprite == null) return bodyHipAnchor.y;
        if (!HeadArtBottomRatio.TryGetValue(bodySprite.name, out float bottomRatio)) return bodyHipAnchor.y;
        if (!HeadArtBottomRatio.TryGetValue(ReferenceHeadSpriteName, out float referenceRatio)) return bodyHipAnchor.y;

        // 기준 머리에서 "앵커가 그림 밑단보다 얼마나 위인가"를 그대로 유지한다.
        // 기준 머리 자신은 결과가 authored 값과 정확히 같아지므로 기존 동작에 회귀가 없다.
        float gap = bodyHipAnchor.y - referenceRatio;
        return Mathf.Clamp01(bottomRatio + gap);
    }

    /// <summary>이번 보정으로 머리 그림이 authored 앵커 기준보다 내려간 양(리그 로컬, 음수 = 아래로).
    /// 무기 소켓이 귀 옆 높이를 유지하도록 <see cref="HeadCanvasWorldOffsetY"/>가 함께 반영한다.</summary>
    private float HeadArtSeatAlignY()
    {
        if (bodySprite == null) return 0f;
        float heightUnits = bodySprite.rect.height / bodySprite.pixelsPerUnit;
        return -(EffectiveBodyHipAnchorY() - bodyHipAnchor.y) * heightUnits;
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
        // 신발은 정강이보다 낮은 sortingOrder를 써서 뒤에 그려진다 - 정강이 아랫부분이 신발 통
        // 위로 겹쳐 보여야 "신발 안에 다리를 넣은" 모습이 된다(2026-08-12, "신발이 다리보다
        // 앞에 있어 신은 것처럼 안 보인다" 리포트로 발견. 예전 footAnkleAnchor 조정은 조인트
        // 위치 문제였고 이건 별개로 그리기 순서 문제였다).
        leg.footRenderer = AttachVisual(leg.ankle, "Foot", footSprite, footAnkle, 0f,
                                        sortingBase - 1, tint, footSpriteFlipX, FootScale);

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
        // 2026-08-19 머리 기획서 Ver04 - <b>몸통 스프라이트가 곧 "머리"</b>다(이 리그에 별도 head
        // 오브젝트는 없다). 그래서 다리 세 파츠와 달리 몸통만은 Resources/Parts 고정이 아니라
        // 선택된 머리(PlayerSession.SelectedRobotId)가 정한다. 머리 아트는 전부 기존
        // Parts/Body.png와 같은 250x250 규격이라 다리 배율·콜라이더·무기 소켓을 건드릴 필요가 없다.
        //
        // 머리 데이터가 없는 경우(JointRigDemo 씬처럼 ModdingManager가 없는 씬)에는
        // HeadSpriteLibrary가 기존 Parts/Body를 돌려주므로 예전 동작 그대로다.
        expectedBodySprite = HeadSpriteLibrary.GetCurrentBodySprite(Time.unscaledTime);

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
        // 네온아이처럼 눈 색이 순환하는 머리는 보여줄 프레임이 시간에 따라 바뀌므로 매 프레임
        // 다시 물어봐야 한다. 단일 이미지 머리에서 매 프레임 조회하는 낭비를 피하려고
        // 애니메이션이 있는 경우에만 갱신한다(HeadSpriteLibrary가 프레임 배열을 캐싱한다).
        // Time.unscaledTime을 쓰는 이유: 정비·상점 화면은 timeScale이 0이라 deltaTime 기반이면
        // 색 순환이 멈춘다.
        if (HeadSpriteLibrary.CurrentHeadIsAnimated())
        {
            Sprite frame = HeadSpriteLibrary.GetCurrentBodySprite(Time.unscaledTime);
            if (frame != null)
            {
                expectedBodySprite = frame;
                bodySprite = frame;
            }
        }

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

        float dt = Time.deltaTime;
        Vector2 velocity = velocitySource != null
            ? new Vector2(velocitySource.linearVelocity.x, velocitySource.linearVelocity.y)
            : externalVelocity;

        // 좌우 방향은 <b>구르는 동안에도</b> 갱신한다(2026-08-25 사용자 요청: "왼쪽으로 구를때는
        // 얼굴이 왼쪽 보고있어야함"). 예전에는 rollActive 분기가 이 지점 앞에서 곧장 return해서
        // 구르기 직전에 보던 방향이 구르는 내내 얼어붙어 있었다 - 오른쪽을 보다가 왼쪽으로 구르면
        // 얼굴이 뒤통수 방향(오른쪽)을 향한 채 굴러갔다. 구르기도 결국 Rigidbody 속도로 이동하므로
        // 평소와 같은 규칙(속도의 x 부호)을 그대로 쓰면 된다.
        UpdateFacing(velocity);

        if (rollActive)
        {
            ApplyRollPose();
            return;
        }

        CurrentSpeed = velocity.magnitude;

        if (legVisualMode != LegVisualMode.Biped)
        {
            // 다리 기획서 Ver02 - 캐터필러/로켓/거미는 걸음걸이가 아니라 프레임 애니메이션(또는
            // 독립 IK)이라 보행 위상(phase)은 아예 진행시키지 않는다. 예전엔 Apply()를 그대로
            // 불러 걷기용 숨쉬기(breath) bob까지 몸통에 남아 있었는데, 다리 쪽은 그 bob을 전혀
            // 안 따라가므로 숨 쉴 때마다 머리가 다리에서 살짝 떴다 붙었다 하는 것처럼 보였다
            // (사용자 리포트: "몸통에서 머리가 공중부양"). 이제 몸통을 완전히 고정된 자세로 두고
            // (아래 오프셋만 반영) bob/breath/lean/sway를 전부 끈다 - 다리와 항상 딱 붙어 있다.
            standHipY = ankleToSole + boneReach * hipHeightRatio; // 인스펙터 실시간 조정 대응(Apply()와 동일한 이유)
            float bodyYOffset = legVisualMode == LegVisualMode.Tread ? treadBodyYOffset
                               : legVisualMode == LegVisualMode.Rocket ? rocketBodyYOffset
                               : legVisualMode == LegVisualMode.Spider ? spiderBodyYOffset : 0f;
            bodyPivot.localPosition = new Vector3(0f, standHipY + AltCanvasAlignY() + bodyYOffset, 0f);
            bodyPivot.localRotation = Quaternion.identity;
            bodyVisual.localScale = Vector3.one;

            UpdateAltVisual(dt, velocity);
            return;
        }

        if (!legsGroup.gameObject.activeSelf) legsGroup.gameObject.SetActive(true); // 구르기 종료 시 다리 복구

        // 보행 강도와 위상
        float targetBlend = Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.01f, fullGaitSpeed));
        gaitBlend = Mathf.MoveTowards(gaitBlend, targetBlend, gaitBlendSpeed * dt);

        float frequency = Mathf.Clamp(CurrentSpeed / Mathf.Max(0.02f, strideRatio * legLength), 0f, maxStepFrequency);
        phase = Mathf.Repeat(phase + frequency * dt, 1f);

        Apply(dt, velocity);
    }

    /// <summary>
    /// 캐터필러 트랙은 실제로 이동한 거리에 비례해 굴러가고(멈추면 트랙도 멈춘다), 로켓의
    /// 호버링+화염은 이동과 무관하게 항상 재생된다(정지해 있어도 떠 있는 로켓이니까).
    /// </summary>
    private void UpdateAltVisual(float dt, Vector2 velocity)
    {
        if (legVisualMode == LegVisualMode.Tread && treadBack != null)
        {
            treadDistancePhase += CurrentSpeed * dt / Mathf.Max(0.01f, treadUnitsPerFrame) / 6f;
            treadBack.ShowFrame(treadDistancePhase);
            treadFront.ShowFrame(treadDistancePhase);
        }
        else if (legVisualMode == LegVisualMode.Rocket && rocketBase != null)
        {
            rocketIdlePhase += rocketFps * dt / 6f;
            rocketBase.ShowFrame(rocketIdlePhase);
            rocketNozzle.ShowFrame(rocketIdlePhase);
            rocketFlame.ShowFrame(rocketIdlePhase);
        }
        else if (legVisualMode == LegVisualMode.Spider && spiderLegs[0] != null)
        {
            UpdateSpiderVisual(dt, velocity);
        }
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
        // 지금 4종 다리는 Roll/Hop(구르기 계열)을 쓰는 다리(기본/거미)와 Tread/Rocket 비주얼을
        // 쓰는 다리(캐터필러/로켓)가 서로 겹치지 않는다(legVisualType이 항상 Biped 또는
        // Spider다) - 그래서 여기서는 legsGroup만 신경 쓰면 된다.
        if (legsGroup.gameObject.activeSelf) legsGroup.gameObject.SetActive(false);

        float progress01 = rollHopProgressOverride >= 0f
            ? rollHopProgressOverride
            : Mathf.Repeat(Mathf.Abs(rollSpinDegrees), 360f) / 360f;
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
