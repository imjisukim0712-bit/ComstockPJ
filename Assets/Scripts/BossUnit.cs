using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 "좀비 군집체"(보스몬스터기획서 Ver01 + 보스연출가이드라인 Ver01).
///
/// <b>2026-08-23 전면 재구현.</b> 그 전까지는 "쿨다운마다 원형 광역 공격 1종 → 공격이 끝나면
/// 무조건 그로기"인 단일 패턴 보스였다. 기획서 구조로 갈아끼우면서 아래가 전부 새로 들어왔다:
/// <list type="bullet">
/// <item>페이즈 2단계 - 체력 50% 최초 도달 시 1회, 무적 + 행동정지 5초 연출 뒤 폭주 상태로 전환</item>
/// <item>공격 패턴 3종 - 돌진 박치기 / 잔해 낙하 / 좀비 소환(페이즈 2 전용)</item>
/// <item>그로기 트리거를 <b>시간 기반 → 피해 누적 게이지</b>로 교체(만충 시 진행 중이던 패턴 즉시 캔슬)</item>
/// <item>등장(소환) 연출과 사망 연출 - 연출이 끝나야 <see cref="OnDefeated"/>가 발행된다</item>
/// </list>
///
/// <b>행동 하나 = 코루틴 하나</b>라는 규칙으로 묶여 있다(<see cref="BossAction"/>).
/// 진행 중인 행동은 <see cref="action_routine"/> 한 자리에만 들어가므로,
/// 그로기/페이즈 전환/사망이 <see cref="CancelAction"/> 한 번으로 무엇이든 확실히 끊을 수 있다
/// (예전 구조에는 캔슬 대상이 되는 패턴이 하나뿐이라 캔슬 개념 자체가 없었다).
///
/// <b>몸 스프라이트 소유권</b>은 기존 규칙을 그대로 따른다 - 행동 중에는
/// <see cref="EnemyUnit.IsAttacking"/>을 켜 두고, 그동안 걷기 애니메이션이 몸 스프라이트를
/// 건드리지 않는 사이에 <see cref="PlayBodyFrames"/>가 직접 프레임을 그린다. 같은 플래그가
/// 이동(ComputeSeekDirection)과 근접 공격 시작(TryAttack)도 함께 잠근다.
///
/// <b>아트 규격</b> - 몸통 세트는 전부 800px 캔버스(BossMove만 512px)지만, <b>캔버스 안에
/// 그려진 보스 자체의 크기가 세트마다 달라서</b> 2026-08-24에 PPU로 맞췄다. 기준은 가장 오래
/// 보이는 <see cref="MonsterAnimationLibrary.BossFolder"/>(512px@PPU64, 몸높이 약 5.7유닛)이고,
/// 나머지는 실루엣 겹침(IoU)으로 실측한 배율만큼 PPU를 옮겼다 - <b>돌진 42 / 포효 112 /
/// 그로기 105</b>(사망은 오차 1%라 100 유지). 특히 돌진 세트는 돌진 궤적까지 담은 광각 구도라
/// 보스가 캔버스 대비 42%로 작게 그려져 있었고(= 돌진할 때마다 보스가 절반 크기로 쪼그라들어
/// 보였다), PPU 42로 낮추면 캔버스가 19유닛까지 커진다. 캔버스가 커진 만큼 그림이 캔버스
/// 안에서 좌우로 4유닛 넘게 움직이므로 <b>프레임마다 pivot을 몸 중심에 맞춰</b> 제자리에
/// 고정했다(실제 이동은 Rigidbody가 담당한다). 몸집을 기준으로 계산하는 값
/// (<see cref="EnemyUnit.BodyVisualWidth"/> 등)은 재생 중인 프레임이 아니라 프리팹
/// 스프라이트에서 재야 한다 - 캔버스 크기가 세트마다 다르기 때문이다.
/// 원본이 오른쪽을 보고 있어 임포트할 때 좌우 반전했다("아트는 왼쪽을 본다" 프로젝트 관례 -
/// <see cref="EnemyUnit.LateUpdate"/>의 flipX. 같은 이유로 이번에 BossGroggy도 함께 반전해
/// 그로기에 들어갈 때 보스가 갑자기 반대쪽을 보던 기존 불일치를 고쳤다).
/// 바닥 이펙트 3종(돌진 경고 14 / 잔해 낙하 18 / 잔해 낙하 주의 16)만 256px이다.
/// </summary>
public class BossUnit : EnemyUnit
{
    // ── Resources 폴더 ─────────────────────────────────────────
    private const string GroggyFolder = "BossGroggy";                 // 12프레임 800px
    private const string ChargeFolder = "BossCharge";                 // 120프레임 800px
    private const string DeathFolder = "BossDeath";                   // 16프레임 800px
    private const string RoarFolder = "BossRoar";                     // 36프레임 800px
    private const string SummonFxFolder = "BossSummon";               // 99프레임 800px (등장/페이즈 전환 공용)
    private const string DeathExplosionFolder = "BossDeathExplosion"; // 60프레임 800px
    private const string DebrisFallFolder = "BossDebrisFall";         // 18프레임 256px
    private const string DebrisWarningFolder = "BossDebrisWarning";   // 16프레임 256px
    private const string ChargeWarningFolder = "BossChargeWarning";   // 14프레임 256px

    // ── 아트 실측 비율 (캔버스 대비 실제로 그림이 그려진 영역) ──
    // BossFrameEffect는 "캔버스 전체"를 기준으로 크기를 맞추므로, 인스펙터 값(= 눈에 보이는
    // 크기)을 캔버스 크기로 환산할 때 쓴다. 알파 바운딩박스를 픽셀로 직접 재서 얻은 값이다.
    private const float ChargeWarningArtWidthRatio = 103f / 256f; // 레인 폭 (세로는 캔버스를 꽉 채운다)
    private const float DebrisWarningArtRatio = 238f / 256f;      // 경고 서클 지름

    // ── 돌진 프레임 구간 (120프레임 시퀀스 안의 단계 경계, 0-based) ──
    // 001~033 예비 동작(웅크림) / 034~070 돌진(먼지 꼬리) / 071~120 회복(기립).
    private const int ChargeWindupEndFrame = 32;
    private const int ChargeDashStartFrame = 33;
    private const int ChargeDashEndFrame = 69;
    private const int ChargeRecoverStartFrame = 70;

    // 잔해 낙하 18프레임 중 실제로 착탄(바닥에 닿는)하는 프레임. 이 프레임이 판정 순간과
    // 겹치도록 낙하 이펙트를 판정보다 그만큼 먼저 띄운다.
    private const float DebrisFallImpactRatio = 5f / 18f;

    // 소환 이펙트 99프레임 중 한가운데서 크게 번쩍하는 프레임(= 보스 본체가 드러나는 순간).
    private const float SummonFxRevealRatio = 49f / 99f;

    [Header("페이즈 (기획서: 체력 50%에서 1회 전환. 폭주 수치는 명세가 없어 임시값)")]
    [Tooltip("이 체력 비율 이하로 최초로 떨어지면 페이즈 2로 전환한다")]
    [SerializeField] private float phase2HpRatio = 0.5f;

    [Tooltip("전환 연출 동안 완전 무적 + 행동정지되는 시간(초). 기획서 지정값 5초")]
    [SerializeField] private float phaseTransitionDuration = 5f;

    [Tooltip("전환 연출 앞부분에서 포효 모션을 재생하는 시간(초)")]
    [SerializeField] private float phaseTransitionRoarDuration = 1.5f;

    [Tooltip("페이즈 2('재구성 이후 폭주 상태')의 이동속도 배율 - 기획서에 수치 명세가 없다")]
    [SerializeField] private float phase2SpeedMultiplier = 1.25f;

    [Tooltip("페이즈 2의 패턴 쿨타임 배율(1보다 작으면 더 자주 쓴다) - 기획서에 수치 명세가 없다")]
    [SerializeField] private float phase2CooldownMultiplier = 0.8f;

    [Header("① 돌진 박치기 (기획서: 사거리 350 · 쿨타임 10.5초)")]
    [Tooltip("돌진 사이의 쿨다운(초). 기획서 지정값 10.5")]
    [SerializeField] private float chargeCooldown = 10.5f;

    [Tooltip("기획 사거리 350을 프로젝트 관례(기획 수치 ÷ 100 = 유닛)로 환산한 값")]
    [SerializeField] private float chargeRange = 3.5f;

    [Tooltip("돌진 예비 동작(바닥에 경고 레인 표시) 시간(초)")]
    [SerializeField] private float chargeTelegraphDuration = 1.1f;

    [Tooltip("돌진 중 이동속도 배율")]
    [SerializeField] private float chargeSpeedMultiplier = 4f;

    [Tooltip("돌진 시작 시점 플레이어 거리에 더하는 여유 거리(유닛). 피하면 이만큼 지나쳐 멈춘다")]
    [SerializeField] private float chargeOvershoot = 2f;

    [Tooltip("한 번의 돌진이 나아갈 수 있는 최대 거리(유닛) - 화면 밖까지 밀고 나가지 않도록")]
    [SerializeField] private float chargeMaxDistance = 12f;

    [Tooltip("돌진이 끝난 뒤 기립(회복) 모션 시간(초)")]
    [SerializeField] private float chargeRecoverDuration = 0.8f;

    [Tooltip("돌진 중 몸 프레임 재생 속도(초당 프레임 수)")]
    [SerializeField] private float chargeDashFps = 30f;

    [Tooltip("바닥에 그려지는 돌진 경고 레인의 폭(월드 유닛)")]
    [SerializeField] private float chargeLaneWidth = 2.6f;

    [Header("② 잔해 낙하 (기획서: 사거리 500 이내 · 쿨타임 7초 · 예시 7곳)")]
    [SerializeField] private float debrisCooldown = 7f;

    [Tooltip("기획 사거리 500을 ÷100 환산한 값. 플레이어가 이 안에 있으면 돌진보다 우선한다")]
    [SerializeField] private float debrisRange = 5f;

    [Tooltip("한 번에 떨어뜨리는 잔해 수(기획서 평면도 예시 7곳). 1개는 항상 플레이어 발밑이다")]
    [SerializeField] private int debrisCount = 7;

    [Tooltip("나머지 잔해가 플레이어를 중심으로 흩어지는 반경(유닛)")]
    [SerializeField] private float debrisSpreadRadius = 3.5f;

    [Tooltip("잔해 하나의 피해 반경(유닛) - 바닥 경고 서클의 반지름과 같다")]
    [SerializeField] private float debrisImpactRadius = 1.2f;

    [Tooltip("경고 서클이 떠 있는 시간(초). 이 동안 범위 밖으로 피할 수 있다")]
    [SerializeField] private float debrisTelegraphDuration = 1.3f;

    [Tooltip("낙하 이펙트(18프레임) 전체 재생 시간(초)")]
    [SerializeField] private float debrisFallDuration = 0.75f;

    [Tooltip("낙하 잔해 스프라이트의 캔버스 폭(월드 유닛)")]
    [SerializeField] private float debrisFallWidth = 3.2f;

    [Tooltip("낙하 잔해를 경고 서클 중심에서 아래로 내리는 거리(월드 유닛). 잔해 스프라이트는 " +
             "피벗이 캔버스 아래-중앙이라 그대로 놓으면 잔해 더미가 서클 위쪽에만 얹혀 " +
             "'서클 뒤에 있는' 것처럼 보인다 - 더미 높이의 절반쯤 내려야 서클 안에 앉은 그림이 된다")]
    [SerializeField] private float debrisFallDropOffset = -0.6f;

    [Tooltip("잔해에 맞았을 때의 피해(플레이어 방어력을 적용하지 않는 고정 데미지) - 밸런스 미확정 임시값")]
    [SerializeField] private int debrisDamage = 20;

    [Tooltip("낙하 잔해의 정렬 순서. 플레이어(13)보다 높아야 앞에 떨어지는 것처럼 보인다")]
    [SerializeField] private int debrisSortingOrder = 15;

    [Header("③ 좀비 소환 (기획서: 페이즈 2 전용 · 쿨타임 15초 · 8마리)")]
    [SerializeField] private float summonCooldown = 15f;
    [SerializeField] private int summonCount = 8;

    [Tooltip("소환된 좀비가 보스 주위에 배치되는 반경(유닛)")]
    [SerializeField] private float summonSpawnRadius = 3f;

    [Tooltip("소환 전 포효 모션(36프레임) 재생 시간(초)")]
    [SerializeField] private float summonRoarDuration = 1.2f;

    [Tooltip("소환 후보 몬스터ID(종류 랜덤). 리더(200006)는 웨이브당 등장 수 제한 기믹이 " +
             "따로 있어 기본 후보에서 뺐다")]
    [SerializeField]
    private List<int> summonMonsterIds = new List<int> { 200001, 200002, 200003, 200004, 200005 };

    [Header("그로기 (기획서: 피해 누적 게이지 100 · 지속 5초 · 피해 +200%)")]
    [Tooltip("그로기 게이지 최대치. 기획서 지정값 100")]
    [SerializeField] private float groggyGaugeMax = 100f;

    [Tooltip("게이지를 0에서 만충까지 채우는 데 필요한 누적 피해량(최대 체력 대비 비율). " +
             "기획서는 '게이지 100'이라고만 적혀 있고 피해 → 게이지 환산이 없어 여기서 정한다 - " +
             "0.12면 최대 체력의 12%를 넣을 때마다 그로기이므로 한 판에 약 8번 들어간다. 밸런스 미확정 임시값")]
    [SerializeField] private float groggyGaugeDamageRatio = 0.12f;

    [Tooltip("그로기 지속 시간(초). 기획서 지정값 5")]
    [SerializeField] private float groggyDuration = 5f;

    [Tooltip("그로기 상태 프레임(BossGroggy, 12프레임) 재생 속도(초당 프레임 수)")]
    [SerializeField] private float groggyFps = 10f;

    [Tooltip("그로기 상태에서 받는 피해 배율. 기획서 지정값 +200% = x3")]
    [SerializeField] private float groggyDamageMultiplier = 3f;

    [Tooltip("머리 위 기절 이펙트(별)의 가로 크기(월드 유닛)")]
    [SerializeField] private float groggyStarsWidth = 2.5f;

    [Tooltip("머리 위 기절 이펙트가 몸 위로 얼마나 더 떠 있을지(월드 유닛)")]
    [SerializeField] private float groggyStarsMargin = 0.3f;

    [Header("등장(소환) 연출")]
    [Tooltip("소환진(BossSummon 99프레임)이 도는 전체 시간(초). 절반쯤에서 본체가 드러난다")]
    [SerializeField] private float spawnFormationDuration = 3.3f;

    [Tooltip("소환진 스프라이트의 캔버스 폭(월드 유닛)")]
    [SerializeField] private float spawnFormationWidth = 12f;

    [Tooltip("본체가 드러난 뒤 포효(경고) 모션을 재생하는 시간(초)")]
    [SerializeField] private float spawnRoarDuration = 1.4f;

    [Header("사망 연출")]
    [Tooltip("사망 모션(BossDeath 16프레임) 재생 시간(초)")]
    [SerializeField] private float deathMotionDuration = 1.3f;

    [Tooltip("사망 폭발(BossDeathExplosion 60프레임) 재생 시간(초). 이 연출이 끝나야 승리 판정이 난다")]
    [SerializeField] private float deathExplosionDuration = 2f;

    [Tooltip("사망 폭발 스프라이트의 캔버스 폭(월드 유닛)")]
    [SerializeField] private float deathExplosionWidth = 14f;

    [Header("이동/대기 모션 (좀비 군집체 8프레임)")]
    [Tooltip("제자리에서도 재생하는 몸통 꿈틀거림 모션의 재생 속도(초당 프레임 수). " +
             "이동속도에 비례해 자동으로 빨라진다(EnemyUnit.UpdateWalkAnimation)")]
    [SerializeField] private float idleMotionFps = 5f;

    [Header("바닥 이펙트 렌더링")]
    [Tooltip("바닥에 깔리는 경고(돌진 레인 / 잔해 서클)의 정렬 순서. 배경(map)과 같은 0을 쓰되 " +
             "z를 카메라 쪽으로 당겨 배경 위·모든 캐릭터(1 이상) 아래에 그려지게 한다")]
    [SerializeField] private int groundSortingOrder = 0;

    [Tooltip("바닥 이펙트의 z 좌표. 배경(map)이 z=-1이라 그보다 카메라에 가까워야 덮인다")]
    [SerializeField] private float groundZ = -1.5f;

    /// <summary>보스가 지금 하고 있는 단일 행동. 동시에 둘 이상 진행되지 않는다.</summary>
    private enum BossAction { None, Spawn, Charge, Debris, Summon, Groggy, PhaseChange, Death }

    private BossAction current_action = BossAction.None;
    private Coroutine action_routine;
    private readonly List<BossFrameEffect> active_effects = new List<BossFrameEffect>();

    private int current_phase = 1;
    private bool is_groggy;
    private bool is_invulnerable;
    private bool is_dying;
    private float groggy_gauge;

    private float next_charge_time;
    private float next_debris_time;
    private float next_summon_time;

    private bool is_dashing;
    private Vector3 dash_direction;
    private Vector3 dash_start;
    private float dash_max_distance;
    private float next_ram_time;

    private static EnemySpawner spawner_cache;
    private static CameraFollow camera_cache;

    /// <summary>보스가 죽는 <b>연출까지 끝난</b> 순간 딱 한 번 발행된다. WaveManager가 승리 판정에 사용한다.</summary>
    public event System.Action OnDefeated;

    /// <summary>현재 페이즈(1 또는 2). 디버그·검증용.</summary>
    public int CurrentPhase => current_phase;

    /// <summary>그로기 게이지 진행도(0~1). 디버그·검증용(전용 UI는 아직 없다).</summary>
    public float GroggyGaugeRatio => groggyGaugeMax > 0f ? Mathf.Clamp01(groggy_gauge / groggyGaugeMax) : 0f;

    /// <summary>씬 재로드로 정적 캐시가 남지 않도록(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static new void ResetStaticCaches()
    {
        spawner_cache = null;
        camera_cache = null;
    }

    /// <summary>
    /// 보스 스탯은 데이터테이블 밖에서 WaveManager가 <c>monster_id = -1</c>로 만들어 넘기므로
    /// 몬스터ID로는 프레임 세트를 찾을 수 없다. 그래서 폴더명을 직접 지정한다.
    /// 이 세트(8프레임)는 보행 사이클이 아니라 <b>제자리 꿈틀거림</b>이라
    /// <c>playWhileIdle = true</c>로 둔다 - 멈춘 동안 얼어붙어 있으면 죽은 것처럼 보인다.
    /// </summary>
    protected override MonsterAnimationLibrary.Clip ResolveMoveClip() =>
        MonsterAnimationLibrary.GetByFolder(MonsterAnimationLibrary.BossFolder,
                                            stillFrameIndex: 0, fps: idleMotionFps, playWhileIdle: true);

    protected override void Awake()
    {
        base.Awake();

        // 스폰 직후 곧바로 패턴이 터지지 않도록 첫 쿨다운을 부여한다.
        next_charge_time = Time.time + chargeCooldown;
        next_debris_time = Time.time + debrisCooldown * 0.5f;
        next_summon_time = Time.time + summonCooldown;
    }

    // ── 등장(소환) 연출 ─────────────────────────────────────────

    /// <summary>
    /// WaveManager가 <see cref="EnemyUnit.Init"/> 직후에 호출한다. 연출이 끝날 때까지 보스는
    /// 완전 무적이고 움직이지 않으며, 본체 스프라이트도 숨어 있다(소환진이 절반쯤 진행돼
    /// 크게 번쩍하는 순간에 드러난다 - <see cref="SummonFxRevealRatio"/>).
    /// 체력바는 체력 100%면 원래 숨어 있으므로 따로 끌 필요가 없다.
    /// </summary>
    public void PlaySpawnIntro()
    {
        is_invulnerable = true;
        StartAction(BossAction.Spawn, SpawnIntroRoutine());
    }

    private IEnumerator SpawnIntroRoutine()
    {
        SetTargetable(false); // 연출 동안에는 자동 조준 대상에서 빠진다(피해가 0이라 헛발이 된다)

        SpriteRenderer sr = BodySpriteRenderer;
        if (sr != null) sr.enabled = false;

        RegisterEffect(BossFrameEffect.Play(SummonFxFolder, transform.position, spawnFormationWidth,
            sr != null ? sr.sortingOrder + 2 : 3, spawnFormationDuration));

        yield return new WaitForSeconds(spawnFormationDuration * SummonFxRevealRatio);

        if (sr != null) sr.enabled = true;
        ShakeCamera(0.6f, 0.28f);

        yield return PlayBodyFrames(RoarFolder, 0, int.MaxValue, spawnRoarDuration);

        is_invulnerable = false;
        SetTargetable(true);
    }

    // ── 패턴 선택 ──────────────────────────────────────────────

    protected override void Update()
    {
        if (is_dying) return;

        // 등장 연출/페이즈 전환 중에는 근접 접촉 공격(base.Update의 TryAttack)도 열지 않는다.
        if (!is_invulnerable) base.Update();

        if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) return;
        if (current_action != BossAction.None || IsAttacking) return;

        TrySelectPattern();
    }

    /// <summary>
    /// 기획서 15p FSM의 "전투 대기"에서 쿨타임이 돌아온 패턴을 고르는 지점.
    /// 우선순위는 기획서 발동조건을 그대로 옮긴 것이다 -
    /// 좀비 소환(페이즈 2 + 주기) → 잔해 낙하(사거리 안) → 돌진 박치기(사거리 밖).
    /// </summary>
    private void TrySelectPattern()
    {
        if (player_transform == null) return;

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f;
        float distance = to_player.magnitude;

        if (current_phase >= 2 && Time.time >= next_summon_time)
        {
            StartAction(BossAction.Summon, SummonPattern());
            return;
        }

        if (Time.time >= next_debris_time && distance <= debrisRange)
        {
            StartAction(BossAction.Debris, DebrisPattern());
            return;
        }

        if (Time.time >= next_charge_time && distance > EffectiveAttackRange())
        {
            StartAction(BossAction.Charge, ChargePattern());
        }
    }

    /// <summary>페이즈 2('폭주')에서는 모든 패턴 쿨타임이 짧아진다.</summary>
    private float CooldownFor(float baseCooldown) =>
        current_phase >= 2 ? baseCooldown * Mathf.Max(0.1f, phase2CooldownMultiplier) : baseCooldown;

    // ── ① 돌진 박치기 ──────────────────────────────────────────

    /// <summary>
    /// 예비 동작(바닥에 붉은 경고 레인 표시) → 지정 방향 직선 돌진 → 접촉 시 피해 → 기립.
    ///
    /// 방향은 예비 동작이 시작될 때 한 번만 정하고 끝까지 고정한다 - 매 프레임 플레이어 쪽으로
    /// 다시 잡으면 유도탄처럼 휘어 "피할 수 있는 패턴"이 되지 않는다(2026-08-12에 스프린터·차저에서
    /// 같은 문제를 고쳤다). 대신 방향을 고정한 부작용(피하면 계속 직진)을 막으려고
    /// 시작 시점 플레이어 거리 + <see cref="chargeOvershoot"/>를 상한으로 둔다.
    /// </summary>
    private IEnumerator ChargePattern()
    {
        next_charge_time = Time.time + CooldownFor(chargeCooldown);

        Vector3 to_player = player_transform != null
            ? player_transform.position - transform.position
            : transform.right;
        to_player.z = 0f;
        Vector3 dir = to_player.sqrMagnitude > 0.0001f ? to_player.normalized : transform.right;

        float travel = Mathf.Min(chargeMaxDistance, to_player.magnitude + Mathf.Max(0f, chargeOvershoot));
        travel = Mathf.Max(chargeRange, travel);

        if (BodySpriteRenderer != null) BodySpriteRenderer.flipX = dir.x > 0f;

        // 바닥 경고 레인. 스프라이트는 피벗이 아래-중앙이고 그림이 위(+y)로 자라도록 그려져 있어,
        // 보스 발밑에 놓고 진행 방향으로 z축 회전시키면 그대로 "돌진할 길"이 된다.
        float lane_angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        RegisterEffect(BossFrameEffect.Play(ChargeWarningFolder, GroundPosition(transform.position),
            chargeLaneWidth / ChargeWarningArtWidthRatio, groundSortingOrder,
            chargeTelegraphDuration, lane_angle, travel));

        yield return PlayBodyFrames(ChargeFolder, 0, ChargeWindupEndFrame, chargeTelegraphDuration);

        dash_direction = dir;
        dash_start = transform.position;
        dash_max_distance = travel;
        next_ram_time = 0f;
        is_dashing = true;

        StartCoroutine(PlayBodyFramesWhile(ChargeFolder, ChargeDashStartFrame, ChargeDashEndFrame,
            chargeDashFps, () => is_dashing));

        // 거리 상한(FixedUpdate)이 먼저 걸리는 것이 정상이지만, 벽/다른 유닛에 막혀 못 나아갈 때를
        // 대비해 시간 상한도 함께 둔다.
        float dash_timeout = travel / Mathf.Max(0.1f, MoveSpeed * chargeSpeedMultiplier) + 1f;
        float elapsed = 0f;
        while (is_dashing && elapsed < dash_timeout)
        {
            if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        is_dashing = false;
        if (rb != null) rb.linearVelocity = Vector3.zero;

        yield return PlayBodyFrames(ChargeFolder, ChargeRecoverStartFrame, int.MaxValue, chargeRecoverDuration);
    }

    protected override void FixedUpdate()
    {
        if (is_dashing)
        {
            if (IsDead || is_dying || GameOverManager.IsGameOver || GameWinManager.IsGameWon)
            {
                rb.linearVelocity = Vector3.zero;
                return;
            }

            if (Vector3.Distance(dash_start, transform.position) >= dash_max_distance)
            {
                is_dashing = false;
                rb.linearVelocity = Vector3.zero;
                return;
            }

            rb.linearVelocity = dash_direction * (MoveSpeed * chargeSpeedMultiplier);
            return;
        }

        base.FixedUpdate();
    }

    protected override void OnCollisionEnter(Collision collision)
    {
        base.OnCollisionEnter(collision);
        HandleRamContact(collision.collider);
    }

    protected override void OnCollisionStay(Collision collision)
    {
        base.OnCollisionStay(collision);
        HandleRamContact(collision.collider);
    }

    /// <summary>돌진 중 접촉 판정. 차저와 같은 규칙으로 공격속도 쿨다운 간격마다 다시 맞는다.</summary>
    private void HandleRamContact(Collider other)
    {
        if (!is_dashing || Time.time < next_ram_time) return;

        PlayerRobotController hit_player = other.GetComponent<PlayerRobotController>();
        if (hit_player == null) hit_player = other.GetComponentInParent<PlayerRobotController>();
        if (hit_player == null) return;

        MeleeAttackPlayer(hit_player); // 돌진도 접촉 근접이라 가시 플레이트 반사 대상이다
        ShakeCamera(0.25f, 0.22f);

        float cooldown = AtSp > 0f ? 1f / AtSp : 1f;
        next_ram_time = Time.time + cooldown;
    }

    // ── ② 잔해 낙하 ────────────────────────────────────────────

    /// <summary>
    /// 플레이어 주변 여러 지점에 낙하 경고 서클을 깔고, 예고가 끝나는 순간 그 자리에 잔해가
    /// 떨어져 범위 안의 플레이어에게 피해를 준다. 1번 지점은 항상 플레이어 발밑이고 나머지는
    /// 주변에 흩어진다("플레이어 근처 또는 다수 지점" - 기획서).
    ///
    /// 보스 전용 시전 모션 아트가 없어, 다른 몬스터의 예비 동작과 같은 관례대로
    /// 몸 색을 주황(<see cref="EnemyUnit.AttackTelegraphColor"/>)으로 물들여 대신한다.
    /// </summary>
    private IEnumerator DebrisPattern()
    {
        next_debris_time = Time.time + CooldownFor(debrisCooldown);

        if (BodySpriteRenderer != null) BodySpriteRenderer.color = AttackTelegraphColor;

        Vector3 center = player_transform != null ? player_transform.position : transform.position;
        center.z = 0f;

        var points = new List<Vector3>(Mathf.Max(1, debrisCount)) { center };
        for (int i = 1; i < debrisCount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * debrisSpreadRadius;
            points.Add(new Vector3(center.x + offset.x, center.y + offset.y, 0f));
        }

        foreach (Vector3 point in points)
        {
            RegisterEffect(BossFrameEffect.Play(DebrisWarningFolder, GroundPosition(point),
                debrisImpactRadius * 2f / DebrisWarningArtRatio, groundSortingOrder,
                debrisTelegraphDuration));
        }

        // 낙하 이펙트는 "착탄 프레임"이 판정 순간과 겹치도록 그만큼 먼저 띄운다.
        float impact_lead = Mathf.Min(debrisTelegraphDuration, debrisFallDuration * DebrisFallImpactRatio);
        yield return new WaitForSeconds(debrisTelegraphDuration - impact_lead);

        foreach (Vector3 point in points)
        {
            Vector3 visual = new Vector3(point.x, point.y + debrisFallDropOffset, point.z);
            RegisterEffect(BossFrameEffect.Play(DebrisFallFolder, visual, debrisFallWidth,
                debrisSortingOrder, debrisFallDuration));
        }

        yield return new WaitForSeconds(impact_lead);

        if (!IsDead && !is_dying && !GameOverManager.IsGameOver && player != null)
        {
            ShakeCamera(0.35f, 0.25f);

            foreach (Vector3 point in points)
            {
                Vector3 to_player = player.transform.position - point;
                to_player.z = 0f;
                if (to_player.sqrMagnitude > debrisImpactRadius * debrisImpactRadius) continue;

                player.TakeDamage(debrisDamage, point);
                break; // 여러 잔해가 겹쳐도 한 번만 맞는다
            }
        }

        if (BodySpriteRenderer != null) BodySpriteRenderer.color = original_body_color;
    }

    // ── ③ 좀비 소환 (페이즈 2 전용) ────────────────────────────

    private IEnumerator SummonPattern()
    {
        next_summon_time = Time.time + CooldownFor(summonCooldown);

        yield return PlayBodyFrames(RoarFolder, 0, int.MaxValue, summonRoarDuration);
        ShakeCamera(0.4f, 0.22f);

        SpawnSummonedZombies();
    }

    /// <summary>
    /// 보스 주위에 좀비를 원형으로 배치해 소환한다.
    ///
    /// <see cref="EnemySpawner.SpawnMonsterTracked"/>를 쓰는 것이 중요하다 - 그냥
    /// <c>SpawnMonster</c>로 만들면 스포너의 생존 목록에 등록되지 않아
    /// 웨이브 종료 시 <c>DespawnAllAliveEnemies()</c>가 이 개체들을 지우지 못하고
    /// 정비 화면 뒤·다음 웨이브까지 필드에 남는다.
    /// </summary>
    private void SpawnSummonedZombies()
    {
        if (summonMonsterIds == null || summonMonsterIds.Count == 0) return;

        if (spawner_cache == null) spawner_cache = FindFirstObjectByType<EnemySpawner>();
        if (spawner_cache == null)
        {
            Debug.LogWarning("BossUnit: EnemySpawner를 찾지 못해 좀비 소환 패턴을 건너뜁니다.");
            return;
        }

        float angle_step = 360f / Mathf.Max(1, summonCount);
        float angle_offset = Random.Range(0f, 360f);

        for (int i = 0; i < summonCount; i++)
        {
            float angle = (angle_offset + angle_step * i) * Mathf.Deg2Rad;
            Vector3 position = transform.position +
                new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * summonSpawnRadius;
            position.z = 0f;

            int monster_id = summonMonsterIds[Random.Range(0, summonMonsterIds.Count)];
            spawner_cache.SpawnMonsterTracked(monster_id, position);
        }
    }

    // ── 그로기 (피해 누적 게이지) ──────────────────────────────

    /// <summary>
    /// 게이지가 만충되면 진행 중이던 패턴을 <b>즉시 캔슬</b>하고 그로기에 들어간다.
    /// 게이지는 그로기 배율이 곱해지기 <b>전</b>의 원본 피해로 쌓는다 - 증폭된 피해로 쌓으면
    /// "그로기라서 더 아프고 → 그래서 그로기가 더 빨리 온다"는 순환이 생긴다.
    /// </summary>
    private void AccumulateGroggyGauge(float rawDamage)
    {
        if (is_groggy || is_dying || is_invulnerable) return;
        if (current_action == BossAction.PhaseChange || current_action == BossAction.Spawn) return;

        float damage_to_fill = Mathf.Max(1f, MaxHp * Mathf.Max(0.001f, groggyGaugeDamageRatio));
        groggy_gauge += rawDamage / damage_to_fill * groggyGaugeMax;
        if (groggy_gauge < groggyGaugeMax) return;

        groggy_gauge = 0f;
        StartAction(BossAction.Groggy, GroggyRoutine());
    }

    private IEnumerator GroggyRoutine()
    {
        is_groggy = true;
        is_dashing = false;
        if (rb != null) rb.linearVelocity = Vector3.zero;

        SpriteRenderer sr = BodySpriteRenderer;

        float head_offset = 1f;
        if (sr != null)
        {
            float scale = transform.lossyScale.x;
            if (Mathf.Approximately(scale, 0f)) scale = 1f;
            // 재생 중인 프레임의 bounds가 아니라 프리팹 스프라이트에서 잰 캐시값을 쓴다 -
            // 돌진 세트(19유닛 캔버스)가 떠 있는 동안 그로기에 들어가면 별이 몸집의 두 배
            // 높이로 튄다(2026-08-24, EnemyUnit.BodyVisualHalfHeight 주석 참고).
            head_offset = (BodyVisualHalfHeight + groggyStarsMargin) / scale;
        }
        GroggyStarsEffect.Play(transform, head_offset, groggyDuration,
            sr != null ? sr.sortingOrder + 5 : 5, groggyStarsWidth);

        Sprite[] frames = BossFrameEffect.GetFrames(GroggyFolder);
        if (sr != null && frames.Length > 0)
        {
            float elapsed = 0f;
            float phase = 0f;
            while (elapsed < groggyDuration)
            {
                if (IsDead || is_dying || GameOverManager.IsGameOver) break;

                phase += Time.deltaTime * groggyFps;
                sr.sprite = frames[Mathf.FloorToInt(phase) % frames.Length];

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(groggyDuration);
        }

        is_groggy = false;
    }

    // ── 페이즈 전환 ────────────────────────────────────────────

    /// <summary>
    /// 체력 50%에 최초로 닿는 순간 1회. 전용 전환 아트가 아직 없어 등장 연출과 같은
    /// 소환진 이펙트(BossSummon)를 몸 위에 겹쳐 "재구성"으로 읽히게 하고, 앞부분에 포효 모션을
    /// 얹었다(기획서: "재구성 이후 폭주 상태").
    /// </summary>
    private void TryEnterPhase2()
    {
        if (current_phase >= 2 || is_dying || IsDead) return;
        if (CurrentHp > MaxHp * phase2HpRatio) return;

        is_invulnerable = true;
        StartAction(BossAction.PhaseChange, PhaseTransitionRoutine());
    }

    private IEnumerator PhaseTransitionRoutine()
    {
        current_phase = 2; // 즉시 올려서 같은 프레임에 다시 트리거되지 않게 한다
        groggy_gauge = 0f;
        is_groggy = false;
        is_dashing = false;
        if (rb != null) rb.linearVelocity = Vector3.zero;

        SetTargetable(false);

        SpriteRenderer sr = BodySpriteRenderer;
        RegisterEffect(BossFrameEffect.Play(SummonFxFolder, transform.position, spawnFormationWidth,
            sr != null ? sr.sortingOrder + 2 : 3, phaseTransitionDuration));
        ShakeCamera(0.8f, 0.3f);

        float roar = Mathf.Min(phaseTransitionDuration, phaseTransitionRoarDuration);
        yield return PlayBodyFrames(RoarFolder, 0, int.MaxValue, roar);
        yield return new WaitForSeconds(Mathf.Max(0f, phaseTransitionDuration - roar));

        MoveSpeed *= Mathf.Max(0.1f, phase2SpeedMultiplier);
        is_invulnerable = false;
        SetTargetable(true);

        // 폭주 진입 직후 곧바로 소환을 쓸 수 있게 한다(페이즈 2의 신호탄).
        next_summon_time = Time.time;

        Debug.Log($"보스 페이즈 2 진입 (이동속도 x{phase2SpeedMultiplier:0.##}, 쿨타임 x{phase2CooldownMultiplier:0.##})");
    }

    // ── 피격 / 사망 ────────────────────────────────────────────

    public override void TakeDamage(float amount, float def_ignore_percent = 0f, int source_weapon_id = 0, bool isCrit = false)
    {
        if (is_dying) return;
        if (is_invulnerable) return; // 등장 연출 · 페이즈 전환 5초는 완전 무적

        float raw_amount = amount;
        if (is_groggy) amount *= groggyDamageMultiplier;

        base.TakeDamage(amount, def_ignore_percent, source_weapon_id, isCrit);

        if (IsDead || is_dying) return;

        // 체력 50% 판정은 데미지 프레임에서 확정적으로 잡는다(별도 폴링보다 정확하다).
        TryEnterPhase2();
        if (current_action == BossAction.PhaseChange) return;

        AccumulateGroggyGauge(raw_amount);
    }

    // ── 효과음(2026-08-26) ────────────────────────────────────
    // 사용자가 "보스 사운드" 폴더로 따로 넣어준 묵직한 파열음 4종을 쓴다
    // (일반 몬스터의 Enemy_Hit_*/Enemy_Death와 구분된다).

    /// <summary>보스 피격음은 전용 3종을 무작위로 쓴다.</summary>
    protected override void PlayHitSfx() => SFXManager.PlayBossHit(0.7f);

    /// <summary><b>여기서는 아무 소리도 내지 않는다.</b> 보스의 사망음은 사망 연출이 끝나는
    /// <see cref="base.Die"/> 시점이 아니라 <see cref="DeathSequence"/>의 폭발 순간에 나야 한다
    /// (그때가 화면에서 "터지는" 순간이다).</summary>
    protected override void PlayDeathSfx() { }

    /// <summary>
    /// 사망 모션(16프레임) → 폭발 이펙트(60프레임) → 그제서야 실제 처치 처리.
    ///
    /// <b><see cref="OnDefeated"/>를 연출이 끝난 뒤에 발행하는 것이 핵심이다</b> - 예전에는
    /// 즉시 발행해서, 그대로 두면 사망 애니메이션이 재생되는 동안 이미 승리 화면이 떴다.
    /// 연출 중에는 자동 조준 대상(<see cref="EnemyUnit.Alive"/>)에서 빼고
    /// <see cref="TakeDamage"/>도 막아 "죽은 몸을 계속 때리는" 상태를 없앤다.
    /// </summary>
    protected override void Die()
    {
        if (IsDead || is_dying) return;

        is_dying = true;
        Alive.Remove(this);

        CancelAction();
        current_action = BossAction.Death;
        IsAttacking = true;
        is_dashing = false;
        if (rb != null) rb.linearVelocity = Vector3.zero;

        action_routine = StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        // 진행 중이던 바닥 이펙트(돌진 레인 / 잔해 경고)와 머리 위 별을 사망 모션 시작 시점에
        // 먼저 정리한다 - Die()가 CancelAction()으로 한 번 지우지만, 그 뒤에도 몇 초간 살아있는
        // 동안 새로 남는 것이 없도록 사망 경로에서 다시 확인하는 쪽이 확실하다.
        ClearEffects();

        yield return PlayBodyFrames(DeathFolder, 0, int.MaxValue, deathMotionDuration);

        SpriteRenderer sr = BodySpriteRenderer;
        if (sr != null) sr.enabled = false;

        // 몸만 끄면 체력바·그로기 별이 허공에 떠서 폭발 내내 그대로 보인다
        // (2026-08-24 사용자 리포트 "보스 사망 이후에도 보스의 UI나 이펙트 등이 남아있는 문제").
        HideAttachedVisuals();
        ClearEffects();

        BossFrameEffect.Play(DeathExplosionFolder, transform.position, deathExplosionWidth,
            sr != null ? sr.sortingOrder + 2 : 3, deathExplosionDuration);
        SFXManager.Play(SFXManager.BossDeathClipName, 0.9f); // 폭발과 같은 프레임에(PlayDeathSfx 주석 참고)
        ShakeCamera(0.9f, 0.35f);

        yield return new WaitForSeconds(deathExplosionDuration);

        base.Die();            // 보상 지급 + OnKilledByPlayer + Destroy
        OnDefeated?.Invoke();  // 승리 판정은 연출이 전부 끝난 뒤에
    }

    // ── 행동(코루틴) 관리 ──────────────────────────────────────

    /// <summary>
    /// 진행 중이던 행동을 끊고 새 행동을 시작한다. <see cref="EnemyUnit.IsAttacking"/>을 켜서
    /// 이동·근접 공격·걷기 애니메이션을 한꺼번에 잠근다(기존 소유권 규칙 그대로).
    /// </summary>
    private void StartAction(BossAction action, IEnumerator body)
    {
        CancelAction();

        current_action = action;
        IsAttacking = true;
        action_routine = StartCoroutine(RunAction(body));
    }

    private IEnumerator RunAction(IEnumerator body)
    {
        yield return StartCoroutine(body);
        FinishAction();
    }

    /// <summary>사망 연출만은 무엇으로도 끊지 않는다(<see cref="Die"/>가 직접 슬롯을 차지한다).</summary>
    private void CancelAction()
    {
        if (current_action == BossAction.Death) return;

        // action_routine 하나만 멈추면 부족하다 - 베이스의 근접 공격 모션(PerformAttackMotion)이
        // 돌던 중에 그로기가 들어오면 그 코루틴이 살아남아, 나중에 RestoreAttackVisual()로
        // IsAttacking을 꺼버리고 스프라이트를 되돌려 그로기 연출과 싸운다. 돌진 프레임 루프
        // (PlayBodyFramesWhile)도 별도 코루틴이라 같이 정리해야 한다.
        StopAllCoroutines();
        FinishAction();
    }

    private void FinishAction()
    {
        current_action = BossAction.None;
        action_routine = null;
        is_dashing = false;
        is_groggy = false;

        ClearEffects();

        if (BodySpriteRenderer != null) BodySpriteRenderer.color = original_body_color;
        IsAttacking = false; // 다음 LateUpdate에서 걷기 애니메이션이 몸 스프라이트를 되찾아간다
    }

    private void RegisterEffect(BossFrameEffect effect)
    {
        if (effect != null) active_effects.Add(effect);
    }

    /// <summary>
    /// 패턴이 캔슬되거나 보스가 파괴될 때 남아 있는 예고 이펙트를 확실히 치운다.
    /// 이펙트는 보스의 자식이 아니라 독립 GameObject라(위치를 세계 좌표로 잡기 위해)
    /// 그냥 두면 임자를 잃고 화면에 영원히 남는다(2026-08-21에 실제로 있었던 버그).
    /// </summary>
    private void ClearEffects()
    {
        foreach (BossFrameEffect effect in active_effects)
        {
            if (effect != null) effect.Stop();
        }
        active_effects.Clear();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        ClearEffects();
    }

    // ── 몸 프레임 재생 ─────────────────────────────────────────

    /// <summary>
    /// folder의 [startIndex, endIndexInclusive] 구간을 duration 동안 균등하게 한 번 재생한다.
    /// endIndexInclusive에 <c>int.MaxValue</c>를 주면 마지막 프레임까지다.
    /// 프레임을 못 찾으면 타이밍만 유지하고 조용히 넘어간다(기존 PlayAttackFrames와 같은 관례).
    /// </summary>
    private IEnumerator PlayBodyFrames(string folder, int startIndex, int endIndexInclusive, float duration)
    {
        Sprite[] frames = BossFrameEffect.GetFrames(folder);
        SpriteRenderer sr = BodySpriteRenderer;

        if (frames.Length == 0 || sr == null)
        {
            if (duration > 0f) yield return new WaitForSeconds(duration);
            yield break;
        }

        int start = Mathf.Clamp(startIndex, 0, frames.Length - 1);
        int end = Mathf.Clamp(endIndexInclusive, start, frames.Length - 1);
        int count = end - start + 1;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (IsDead && !is_dying) yield break;
            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;

            float t = Mathf.Clamp01(elapsed / duration);
            sr.sprite = frames[Mathf.Min(end, start + Mathf.FloorToInt(t * count))];

            elapsed += Time.deltaTime;
            yield return null;
        }

        sr.sprite = frames[end];
    }

    /// <summary>
    /// keepGoing이 참인 동안 [startIndex, endIndexInclusive] 구간을 fps로 <b>순환</b> 재생한다.
    /// 돌진처럼 "언제 끝날지 모르는" 구간용이다.
    /// </summary>
    private IEnumerator PlayBodyFramesWhile(string folder, int startIndex, int endIndexInclusive,
                                            float fps, System.Func<bool> keepGoing)
    {
        Sprite[] frames = BossFrameEffect.GetFrames(folder);
        SpriteRenderer sr = BodySpriteRenderer;
        if (frames.Length == 0 || sr == null) yield break;

        int start = Mathf.Clamp(startIndex, 0, frames.Length - 1);
        int end = Mathf.Clamp(endIndexInclusive, start, frames.Length - 1);
        int count = end - start + 1;

        float phase = 0f;
        while (keepGoing())
        {
            if (IsDead || is_dying || GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;

            phase += Time.deltaTime * Mathf.Max(1f, fps);
            sr.sprite = frames[start + (Mathf.FloorToInt(phase) % count)];
            yield return null;
        }
    }

    // ── 공용 도우미 ────────────────────────────────────────────

    /// <summary>바닥에 깔리는 이펙트의 좌표(배경 위에 확실히 그려지도록 z를 카메라 쪽으로 당긴다).</summary>
    private Vector3 GroundPosition(Vector3 position) => new Vector3(position.x, position.y, groundZ);

    /// <summary>
    /// 자동 조준(<see cref="PlayerShootManager"/>)이 순회하는 <see cref="EnemyUnit.Alive"/>
    /// 목록에 넣고 뺀다. 무적 구간(등장 연출·페이즈 전환)에는 빼둬야 플레이어가 아무 피해도
    /// 들어가지 않는 표적에 계속 총알을 쏟지 않는다.
    /// </summary>
    private void SetTargetable(bool targetable)
    {
        if (targetable)
        {
            if (!IsDead && !is_dying && !Alive.Contains(this)) Alive.Add(this);
        }
        else
        {
            Alive.Remove(this);
        }
    }

    private void ShakeCamera(float duration, float magnitude)
    {
        if (camera_cache == null) camera_cache = FindFirstObjectByType<CameraFollow>();
        if (camera_cache != null) camera_cache.Shake(duration, magnitude);
    }
}
