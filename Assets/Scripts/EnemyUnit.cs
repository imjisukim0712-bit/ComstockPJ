using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스폰된 몬스터 한 마리의 상태/행동. MonsterData(데이터테이블) 필드 사용 방식:
/// - monster_hp   : 체력(MaxHp/CurrentHp). 플레이어 투사체에 맞아 0 이하가 되면 사망.
/// - monster_atk  : 공격 사거리 안에서 플레이어에게 주는 데미지 (플레이어 방어력만큼 경감됨, PlayerRobotController.TakeDamage 참고)
/// - monster_def  : 플레이어 투사체에 맞았을 때 데미지 경감치 (TakeDamage에서 사용)
/// - monster_speed: 플레이어를 향해 다가가는 이동속도
/// - monster_range: 공격 사거리 - 플레이어와의 거리가 이 값 이하면 공격을 시도
/// - monster_type : 공격 타입 (현재 미구현 - 항상 근접 접촉형으로만 동작)
/// - monster_atsp : 공격속도 - 공격 성공 후 다음 공격까지의 쿨다운 (1 / atsp 초)
/// - 보상 드랍    : 사망 시 GrantKillRewards()가 경험치/골드/부품상자를 RewardPickupManager로 생성
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyUnit : MonoBehaviour
{
    private const int BasicZombieMonsterId = 200001;
    // 2026-08-19 좀비 전용 아트(Assets/Resources/Zombie.png)로 교체되며 스프라이트 이름이
    // 기존 "Enemy_zombie_S"에서 "Zombie"로 바뀌었다. 이 상수가 옛 이름을 그대로 들고 있어
    // CanPlayZombieAttackFrames()/CanPlayZombieMoveFrames()가 항상 false가 되고,
    // 좀비 전용 걷기·공격 프레임이 재생되지 않는 회귀가 있었다.
    private const string BasicZombieSpriteName = "Zombie";

    // 자동공격(PlayerShootManager)이 "사거리 내 최근접 적"을 찾을 때 순회하는 전역 생존 목록.
    // Physics.OverlapSphere를 무기 슬롯마다 매 프레임 돌리는 대신, 스폰/사망 시점에만 갱신되는
    // 이 목록을 순회하는 쪽이 더 저렴하다.
    public static readonly List<EnemyUnit> Alive = new List<EnemyUnit>();

    // 2026-08-12 디스크 기획서 반영 - "적을 처치하면 ~" 형태의 디스크 효과(교향곡:번개/화염/암석,
    // 광분 바이러스, 포근한 치유, 바람 소리, 금속음, 교향곡:파도)가 구독하는 전역 이벤트.
    // 이 게임은 플레이어만 데미지를 줄 수 있으므로(EnemyUnit.TakeDamage는 플레이어 공격에서만
    // 호출된다) Die()가 곧 "플레이어가 처치함"과 동일하다 - 별도의 "누가 죽였는지" 구분이 필요 없다.
    public static event System.Action<EnemyUnit> OnKilledByPlayer;

    /// <summary>이 적을 마지막으로 때린 플레이어 무기의 ID(0 = 무기가 아닌 피해).
    /// 해금 진행도(<see cref="UnlockTracker"/>)가 처치 시점에 무기 분류를 알아내는 데 쓴다.</summary>
    public int LastDamageWeaponId { get; private set; }

    // "물 빠지는 소리 디스크" - DiscEffectRuntime이 플레이어 주변 반경 안의 적에게 매 프레임
    // 설정한다(반경 밖이면 다음 프레임에 1로 되돌아간다 - DiscEffectRuntime이 매 프레임 전체를
    // 1로 리셋한 뒤 반경 안의 적만 다시 낮춘다).
    private float aura_slow_multiplier = 1f;
    public void SetAuraSlowMultiplier(float multiplier) => aura_slow_multiplier = Mathf.Clamp01(multiplier);

    /// <summary>넉백 배율 기준점(기획서 p.15 좀비 기본 질량). 이 질량보다 무거우면 덜, 가벼우면 더 밀려난다.</summary>
    public const float ReferenceMass = 50f;

    public int MonsterId { get; private set; }
    // 2026-08-20 스탯 소수화 - 플레이어의 공격력 +0.3 같은 소수 보너스가 실제 피해에
    // 반영되려면 받는 쪽 체력도 소수여야 한다(정수로 두면 매 타격에서 잘려 사라진다).
    // 원본 데이터(MonsterData)는 그대로 int이며, 여기서 float로 받아 웨이브 배율을 곱한다.
    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }
    public float Atk { get; private set; }
    public float Def { get; private set; }
    // set이 protected인 이유: BossUnit이 페이즈 2('폭주') 전환에서 이동속도를 올린다
    // (2026-08-23). 그 외에는 Init/ApplyWaveStatMultiplier 말고 건드리지 않는다.
    public float MoveSpeed { get; protected set; }
    public float AttackRange { get; private set; } // monster_range: 공격 사거리
    public float AtSp { get; private set; }         // monster_atsp: 공격속도 - 공격 쿨다운에 사용
    public float Mass { get; private set; } = ReferenceMass;       // monster_mass - 넉백 저항(기획서 p.22)
    public bool IsDead { get; private set; }

    // 다음 공격이 가능한 시각 (monster_atsp 기반 쿨다운). 서브클래스(스프린터/차저 등)가
    // 자신만의 공격 타이밍을 구현할 때도 같은 필드를 재사용할 수 있도록 protected로 열어둔다.
    protected float next_attack_time = 0f;

    // protected - 스프린터/차저(돌진)처럼 자신만의 이동 상태에서 직접 속도를 제어하는
    // 서브클래스를 위해 열어둔다.
    protected Rigidbody rb;
    protected Transform player_transform;
    protected PlayerRobotController player;

    [Header("체력바 (체력이 100% 미만일 때만 표시)")]
    [Tooltip("체력바 폭 = 본인 스프라이트 폭 × 이 비율")]
    [SerializeField] private float healthBarWidthRatio = 0.55f;
    [Tooltip("체력바 두께(월드 유닛). 몬스터마다 스케일이 달라도 두께는 폭 대비 일정 비율로 맞춘다")]
    [SerializeField] private float healthBarThicknessRatio = 0.16f;
    [Tooltip("스프라이트 맨 위에서 체력바까지 추가로 띄우는 여백(월드 유닛)")]
    [SerializeField] private float healthBarMargin = 0.15f;

    private SpriteRenderer body_sprite_renderer;

    /// <summary>차저처럼 예비 동작 중 전용 프레임을 직접 재생해야 하는 서브클래스를 위한 접근자
    /// (2026-08-23, <see cref="ChargerUnit"/> 참고). 필드 자체를 protected로 열면 기존
    /// 소유권 규칙(IsAttacking 동안에만 PerformAttackMotion이, 그 외엔 UpdateWalkAnimation이
    /// 소유)이 깨지기 쉬워 읽기 전용 프로퍼티로만 노출한다.</summary>
    protected SpriteRenderer BodySpriteRenderer => body_sprite_renderer;

    // 프리팹 스프라이트(= 그 몬스터의 기본 자세)에서 한 번만 잰 몸통 캔버스 크기(월드 유닛).
    //
    // <b>재생 중인 프레임의 bounds를 그때그때 읽으면 안 된다</b> - 프레임 세트마다 캔버스
    // 월드 크기가 다를 수 있기 때문이다(2026-08-24: 보스 돌진 세트는 돌진 궤적까지 담긴
    // 광각 구도라 PPU를 42로 낮춰 캔버스가 19유닛이 됐다. 이때 bounds를 그대로 쓰면 피격
    // 이펙트 지름이나 그로기 별 높이가 몸집의 두 배 이상으로 튄다).
    private float body_visual_width = 1f;
    private float body_visual_half_height = 0.5f;

    /// <summary>몸통 캔버스 폭(월드 유닛). 재생 중인 프레임과 무관하게 항상 같은 값이다.</summary>
    protected float BodyVisualWidth => body_visual_width;

    /// <summary>몸통 캔버스 절반 높이(월드 유닛). pivot이 대략 중앙이므로 머리 위 높이로 쓴다.</summary>
    protected float BodyVisualHalfHeight => body_visual_half_height;

    /// <summary>
    /// 스프라이트 캔버스 안의 투명 여백을 제외한 <b>실제 그림 폭</b>의 비율(0~1).
    /// 기본값은 캔버스 전체이며, 원본 그림이 캔버스 일부만 쓰는 경우에만 재정의한다.
    /// Transform 배율이나 콜라이더 크기와는 별개다.
    /// </summary>
    protected virtual float BodyVisualWidthScale => 1f;

    /// <summary>
    /// 캔버스 안에서 <b>실제 그림이 차지하는 세로 구간</b>(0 = 캔버스 맨 아래, 1 = 맨 위).
    /// 기본값 (0,1)은 캔버스 전체 = 예전 동작과 픽셀 단위로 같다.
    ///
    /// <para><b>왜 배율(곱셈)이 아니라 구간(범위)인가</b>(2026-08-25 검수에서 잡은 문제):
    /// 그림이 캔버스 <b>가운데에 있지 않으면</b> 대칭 축소로는 위쪽 여백을 없앨 수 없다.
    /// 스프린터가 그 경우다 - 알파 실루엣이 캔버스 250px 중 y 4~110에만 있어서 <b>실제 머리
    /// 끝이 pivot(125)보다 아래</b>인데, <c>extents.y × 0.40</c> 같은 곱셈은 항상 pivot 위쪽
    /// 값만 만들 수 있다(0.50u). 그 결과 체력바가 머리 위로 0.86u나 떠 있었다(좀비는 0.43u).
    /// 구간으로 두면 상단 비율이 0.5보다 작을 때 <b>음수 높이</b>가 자연스럽게 나온다.</para>
    /// </summary>
    protected virtual Vector2 BodyVisualVerticalRange => new Vector2(0f, 1f);

    private Transform health_bar_root;
    private Transform health_bar_fill;
    private float health_bar_width;
    private static Sprite white_pixel_sprite;      // pivot 중앙 - 배경용
    private static Sprite white_pixel_sprite_left; // pivot 왼쪽 - 채움 바용(왼쪽 고정, 오른쪽만 줄어듦)

    // 부품 상자 드랍 확률 조회용. 몬스터마다 새로 찾지 않도록 클래스 전체가 공유하는 캐시.
    // PlayerRobotController.Awake()가 Alive.Clear()와 함께 매 런 시작마다 비워준다(재시도 시 이전 판의 참조가 남지 않도록).
    private static ModdingManager modding_manager_cache;

    public static void ResetStaticCaches()
    {
        modding_manager_cache = null;
        max_body_radius = 0.5f; // 이전 판에 있던 보스 크기가 남아 검색 반경이 과도해지지 않도록
        MonsterAnimationLibrary.ResetCache(); // 씬 재로드로 Resources가 언로드됐을 수 있다
        GroggyStarsEffect.ResetCache();
        MuzzleFlashEffect.ResetCache();
        ProjectileSpriteAnimator.ResetCache();
        ChargeWarningEffect.ResetCache();
        ExplosionEffect.ResetCache();
        ZombieHitEffect.ResetCache();
        RollDustEffect.ResetCache();
        LevelUpEffect.ResetCache();
        EnemyProjectile.ResetCache();
        ChargerUnit.ResetStaticCaches();
        BossFrameEffect.ResetCache();
        BossUnit.ResetStaticCaches();
    }

    // 처치 시 보상이 나올 확률(사용자 지정). 부품 상자 확률만 PartsCatalog 에셋에 있고
    // 경험치/골드는 데이터로 뺄 자리가 아직 없어 여기 상수로 둔다 - 밸런스 미확정 임시값.
    private const float ExpDropChance = 0.5f;   // 1/2

    // 2026-08-13 "한 웨이브에서 얻는 골드를 평균 25% 증가" 요청.
    // <b>드랍 확률</b>에 곱한다 - 골드량(MaxHp / GoldPerMaxHp)은 정수라 1골드짜리 몬스터에
    // 1.25를 곱해도 반올림에서 그대로 1이 되어 아무 변화가 없다. 확률에 곱하면 몬스터 체력과
    // 무관하게 기대값이 정확히 25% 늘어난다.
    private const float GoldRewardMultiplier = 1.25f;
    private const float GoldDropChance = (1f / 3f) * GoldRewardMultiplier; // 1/3 → 0.4167

    public void Init(MonsterData data)
    {
        MonsterId = data.monster_id;
        MaxHp = data.monster_hp;
        CurrentHp = data.monster_hp;
        Atk = data.monster_atk;
        Def = data.monster_def;
        MoveSpeed = data.monster_speed;
        AttackRange = data.monster_range;
        AtSp = data.monster_atsp;

        Mass = data.EffectiveMass;

        stat_multiplier = 1f;

        UpdateHealthBar(); // 스폰 직후는 항상 100%이므로 바를 숨긴 상태로 초기화한다
    }

    /// <summary>
    /// 웨이브 스탯 배율(EnemySpawner.CurrentStatMultiplier)을 체력·공격력에 곱한다.
    /// 예전에는 EnemySpawner가 MonsterData의 int 필드를 반올림해서 곱했는데, 스탯 소수화
    /// (2026-08-20) 이후에는 반올림 없이 여기서 곱한다 - 2.5%/웨이브 같은 작은 증가가
    /// 정수 반올림에 먹히지 않는다. Init() 직후에 한 번만 호출한다.
    /// </summary>
    public void ApplyWaveStatMultiplier(float multiplier)
    {
        if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f)) return;

        stat_multiplier = multiplier;
        MaxHp = Mathf.Max(1f, MaxHp * multiplier);
        CurrentHp = MaxHp;
        Atk = Mathf.Max(1f, Atk * multiplier);
        UpdateHealthBar();
    }

    private float stat_multiplier = 1f;

    /// <summary>이 유닛에 적용된 웨이브 스탯 배율(디버그·검증용).</summary>
    public float WaveStatMultiplier => stat_multiplier;

    // protected virtual로 열어둔 이유: BossUnit이 이 클래스를 상속해서 광역 공격 패턴을 얹는다.
    // Awake를 override해서 base.Awake()를 호출해야 Alive 리스트 등록/물리 설정이 그대로 적용된다
    // (override 없이 새 Awake를 정의하면 Unity가 이 메서드를 아예 호출하지 않아 조용히 깨진다).
    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Player와 동일한 규칙: 중력 없이 X-Y 평면에서만, 회전 없이 이동
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.constraints |= RigidbodyConstraints.FreezePositionZ;

        FindPlayer();
        BuildHealthBar();
        ComputeBodyRadius();

        // 공격 모션(예비 동작) 중 몸 색/스프라이트를 잠깐 바꿨다가(PerformAttackMotion) 되돌릴 때 쓸
        // 원래 값을 기억해둔다. BuildHealthBar()가 body_sprite_renderer를 채운 뒤라야 한다.
        if (body_sprite_renderer != null)
        {
            original_body_color = body_sprite_renderer.color;
            original_body_sprite = body_sprite_renderer.sprite;
        }

        // 피격 시 0.25초 흰색 연출. 프리팹에 컴포넌트를 일일이 붙이지 않아도 되도록
        // (좀비/차저/보스 프리팹 3개 + 앞으로 추가될 몬스터) 여기서 자동으로 붙인다.
        // BuildHealthBar()보다 뒤에 와야 체력바 렌더러가 대상에서 제외된다.
        hit_flash = GetComponent<HitFlash>();
        if (hit_flash == null) hit_flash = gameObject.AddComponent<HitFlash>();

        Alive.Add(this);
    }

    private HitFlash hit_flash;

    private void PlayHitFlash()
    {
        if (hit_flash != null) hit_flash.Play();
    }

    // virtual - BossUnit이 이 위에 오버라이드해서 남아있는 텔레그래프 이펙트를 정리한다
    // (2026-08-21, 코루틴이 끝나기 전에 보스가 파괴되면 이펙트가 영원히 남는 버그 수정).
    protected virtual void OnDestroy()
    {
        Alive.Remove(this);
    }

    private void OnDisable()
    {
        // 공격 코루틴이 오브젝트 비활성화로 중단돼도 공격 프레임/색상이 남지 않게 한다.
        RestoreAttackVisual();
    }

    private void FindPlayer()
    {
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj == null) return;

        player_transform = player_obj.transform;
        player = player_obj.GetComponent<PlayerRobotController>();
    }

    protected virtual void Update()
    {
        if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) return;

        if (player_transform == null)
        {
            FindPlayer(); // 스폰 시점에 플레이어가 아직 없었을 경우를 대비해 계속 재시도
            return;
        }

        // 거리 기반 체크: 콜라이더 크기 때문에 서로 맞닿아도 중심점 사이 거리가
        // monster_range보다 클 수 있으므로(예: 몸통 크기 때문에 실제 접촉 거리가 1.3~1.4인데
        // 사거리가 1인 경우), 몸통 반지름만큼 여유를 더해 판정한다.
        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f; // X-Y 평면만 사용
        float distance = to_player.magnitude;

        if (distance <= EffectiveAttackRange())
        {
            TryAttack();
        }
    }

    /// <summary>
    /// 실제 공격이 닿는 중심간 거리.
    ///
    /// <b>예전에는 `AttackRange + BodyContactRadius()`로 둘을 더했는데, 이게 이중 계산이라
    /// 사거리가 비정상적으로 길었다</b>(2026-08-10 사용자 지적). 좀비 기준 실측:
    /// monster_range 1.6 + 몸통 반지름 합 1.285 = <b>2.885유닛</b>으로, 두 몸이 실제로 맞닿는
    /// 거리(1.285)의 2.2배나 떨어져서도 공격이 들어갔다.
    ///
    /// `monster_range`는 이미 '중심 기준' 사거리이므로 몸 반지름을 더하면 안 된다.
    /// 몸 반지름은 <b>하한</b>으로만 쓴다 - 몸집이 커서 실제로는 맞닿았는데 사거리가 그보다
    /// 짧아 판정이 안 나는 경우만 구제하려는 것이 원래 의도였다.
    /// </summary>
    protected float EffectiveAttackRange() => Mathf.Max(AttackRange, BodyContactRadius());

    // 콜라이더끼리 물리적으로 맞닿았을 때도(= "들이박았을 때") 확실히 공격이 들어가도록
    // 물리 충돌 이벤트를 보조 트리거로 함께 사용한다.
    // protected virtual - 스프린터/차저가 돌진 중 접촉 판정을 덧붙일 때 base를 먼저 호출한다.
    protected virtual void OnCollisionEnter(Collision collision) => TryAttackFromCollision(collision.collider);
    protected virtual void OnCollisionStay(Collision collision) => TryAttackFromCollision(collision.collider);

    private void TryAttackFromCollision(Collider other)
    {
        if (other.GetComponent<PlayerRobotController>() == null && other.GetComponentInParent<PlayerRobotController>() == null) return;
        TryAttack();
    }

    // 자신과 플레이어 콜라이더의 대략적인 반지름 합. 두 몸이 물리적으로 맞닿는 최소 중심간 거리를
    // 근사해서, 몸집 때문에 실제로는 닿았는데 monster_range보다 멀어서 공격 판정이 안 나는 문제를 막는다.
    protected float BodyContactRadius()
    {
        // extents의 x/y 중 <b>짧은 쪽</b>을 쓴다. 긴 쪽을 쓰면 몸을 원으로 근사할 때 짧은 축
        // 방향으로 몸 밖까지 판정이 튀어나간다 - ComputeBodyRadius()가 같은 이유로 Min을 쓰는데,
        // 여기만 Max여서 기준이 어긋나 있었다(2026-08-10 사용자 지적 "공격 사거리가 비정상적으로 김").
        // 좀비 기준 실측: Max이면 1.75유닛, Min이면 1.225유닛.
        float self_radius = 0.6f;
        Collider self_collider = GetComponent<Collider>();
        if (self_collider != null) self_radius = Mathf.Min(self_collider.bounds.extents.x, self_collider.bounds.extents.y);

        float player_radius = 0.8f;
        if (player != null)
        {
            Collider player_collider = player.GetComponent<Collider>();
            if (player_collider != null) player_radius = Mathf.Min(player_collider.bounds.extents.x, player_collider.bounds.extents.y);
        }

        return self_radius + player_radius;
    }

    /// <summary>
    /// 지금 공격 모션(예비 동작)이 진행 중인지. 이 동안에는 재차 공격을 시작하지 않고
    /// (<see cref="TryAttack"/> 가드), 이동도 멈춘다(<see cref="ComputeSeekDirection"/> 가드).
    /// </summary>
    public bool IsAttacking { get; protected set; }

    [Header("공격 모션 (전부 밸런스 미확정 임시값)")]
    [Tooltip("예비 동작(몸을 뒤로 젖히는) 시간(초). 이 시간이 지나야 타격 동작으로 넘어간다 - 플레이어가 피할 수 있는 구간")]
    [SerializeField] protected float attackWindupSeconds = 0.35f;

    [Tooltip("타격 동작(앞으로 내지르는) 시간(초). 이 동작이 끝나는 시점에 판정이 들어간다")]
    [SerializeField] protected float attackStrikeSeconds = 0.1f;

    [Tooltip("예비 동작에서 뒤로 젖히는 거리 = 몸 반지름 x 이 값. 규격이 커지면 자동으로 커진다")]
    [SerializeField] protected float windupBackRatio = 0.35f;

    [Tooltip("타격 동작에서 앞으로 내지르는 거리 = 몸 반지름 x 이 값. 0이면 제자리에서 공격한다(원거리 몬스터용)")]
    [SerializeField] protected float strikeLungeRatio = 0.7f;

    [Tooltip("좀비 전용 공격 프레임 애니메이션의 회복 동작(9~12번째 프레임) 재생 시간(초)")]
    [SerializeField] protected float attackRecoverSeconds = 0.15f;

    // protected - 스피터/스프린터/차저/디스럭터 등 서브클래스가 같은 텔레그래프 색/원본 색을
    // 참조하거나(PerformAttackMotion을 그대로 재사용하는 경우) 직접 코루틴을 짤 때도 재사용한다.
    protected static readonly Color AttackTelegraphColor = new Color(1f, 0.55f, 0.15f, 1f); // 공격 예비 동작 표시색(주황)
    protected Color original_body_color = Color.white;
    private Sprite original_body_sprite;

    // 좀비 전용 공격 모션 12프레임(Assets/Resources/ZombieAttack/ZombieAttack_01~12.png, 사용자가
    // 직접 그려 제공한 시퀀스 - 2026-08-10 좀비 기획서 Ver04 항목의 "채팅 첨부라 파일 경로를 특정할
    // 수 없어 적용 못함"이 세션 export에서 원본 데이터를 복구해 해결됨). 파일명 오름차순 = 재생 순서.
    // 기본 좀비(EnemyUnit, MonsterId 200001)에만 적용한다 - 다른 몬스터는 규격(소형~초대형)마다
    // 스프라이트 픽셀 크기가 달라(PPU=100 고정) 이 프레임을 그대로 쓰면 몸 크기가 좀비 크기로
    // 잠깐 바뀌어 보이는 문제가 생긴다(전용 프레임이 생기기 전까지는 기존 색상 텔레그래프 유지).
    private static Sprite[] zombie_attack_frames_cache;

    private static Sprite[] ZombieAttackFrames
    {
        get
        {
            if (zombie_attack_frames_cache == null)
            {
                Sprite[] loaded = Resources.LoadAll<Sprite>("ZombieAttack");
                System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
                zombie_attack_frames_cache = loaded;
            }
            return zombie_attack_frames_cache;
        }
    }

    // 이동(걷기/질주/꿈틀거림) 프레임 세트. 2026-08-20까지는 기본 좀비(200001)의
    // Resources/ZombieMove만 하드코딩돼 있었으나, 사용자가 스피터/스프린터/디스럭터/리더 +
    // 보스(좀비 군집체)의 8프레임 모션을 제공하면서 <see cref="MonsterAnimationLibrary"/>로
    // 옮겨 몬스터ID 단위로 일반화했다. 세트가 없는 몬스터(차저)는 프리팹의 정지 스프라이트를
    // 그대로 유지한다(사용자 지정: "아직 애니메이션 없는 건 멈춘 이미지로 놔둬").
    private MonsterAnimationLibrary.Clip move_clip;
    private bool move_clip_resolved;

    /// <summary>
    /// 이 유닛이 쓸 이동 프레임 세트. 기본은 몬스터ID로 조회하며, 데이터테이블 밖에 있는
    /// 유닛(<see cref="BossUnit"/> - WaveManager가 monster_id = -1로 만들어 넘긴다)은
    /// 이 프로퍼티를 override해서 폴더를 직접 지정한다.
    /// </summary>
    protected virtual MonsterAnimationLibrary.Clip ResolveMoveClip() =>
        MonsterAnimationLibrary.GetByMonsterId(MonsterId);

    /// <summary>
    /// 프레임 세트는 <see cref="Init"/>로 MonsterId가 들어온 뒤에야 결정된다(스폰 순서:
    /// Instantiate → Awake → Init). 그래서 Awake가 아니라 처음 쓰이는 시점에 한 번 조회한다.
    /// </summary>
    private MonsterAnimationLibrary.Clip MoveClip
    {
        get
        {
            if (!move_clip_resolved)
            {
                move_clip = ResolveMoveClip();
                move_clip_resolved = true;
            }
            return move_clip;
        }
    }

    [Tooltip("이동 모션 재생 속도(초당 프레임 수) 강제 지정. 0 이하면 몬스터별 기본값" +
             "(MonsterAnimationLibrary의 Fps)을 쓴다")]
    [SerializeField] private float walkFrameFpsOverride = 0f;

    private float walk_frame_phase;

    /// <summary>
    /// 이동 중일 때 <see cref="MoveClip"/>을 순환 재생하고, 멈추면 그 세트의 "멈춘 이미지"
    /// (<see cref="MonsterAnimationLibrary.Clip.StillFrame"/>)로 되돌린다. 공격 프레임
    /// (<see cref="PlayAttackFrames"/>)이 이미 body_sprite_renderer.sprite를 직접 다루고 있는
    /// 동안(IsAttacking)에는 건드리지 않는다 - 서로 다른 시점에만 소유권을 넘겨받으므로
    /// 매 프레임 충돌하지 않는다.
    /// </summary>
    private void UpdateWalkAnimation()
    {
        if (!CanPlayMoveFrames())
        {
            walk_frame_phase = 0f;
            return;
        }

        MonsterAnimationLibrary.Clip clip = MoveClip;
        Sprite[] frames = clip.Frames;
        Vector2 planar_velocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        bool is_moving = planar_velocity.sqrMagnitude > (FacingVelocityThreshold * FacingVelocityThreshold);

        if (!is_moving && !clip.PlayWhileIdle)
        {
            walk_frame_phase = 0f;
            Sprite still = clip.StillFrame != null ? clip.StillFrame : original_body_sprite;
            if (still != null && body_sprite_renderer.sprite != still) body_sprite_renderer.sprite = still;
            return;
        }

        // fps는 "기준 이동속도(MoveSpeed)로 걸을 때"의 프레임 속도다. 실제 속도가
        // 기준보다 느려지면(감속 오라 등) 애니메이션도 같이 느려지고, 빨라지면(넉백 등) 같이
        // 빨라진다 - 고정 fps로 두면 이동속도를 조정할 때마다 애니메이션과 따로 놀았다
        // (2026-08-12, "애니메이션 속도가 이동속도에 비해 너무 느리다" 리포트로 발견).
        float speed_ratio = MoveSpeed > 0.01f ? planar_velocity.magnitude / MoveSpeed : 1f;
        // 제자리에서도 재생하는 세트(보스 호흡)는 멈춰 있을 때 0배가 되어 얼어붙지 않도록 1을 하한으로 둔다.
        if (clip.PlayWhileIdle) speed_ratio = Mathf.Max(1f, speed_ratio);

        float fps = walkFrameFpsOverride > 0f ? walkFrameFpsOverride : clip.Fps;
        walk_frame_phase += Time.deltaTime * fps * speed_ratio;
        int frame_index = Mathf.FloorToInt(walk_frame_phase) % frames.Length;
        body_sprite_renderer.sprite = frames[frame_index];
    }

    /// <summary>
    /// 이동 프레임을 재생해도 되는 상태인지. 프레임 세트는 몬스터ID 전용이라 남의 몸에
    /// 적용될 일이 없지만(<see cref="MonsterAnimationLibrary"/>), 렌더러 소유권만은 확인한다 -
    /// body_sprite_renderer가 자식/플레이어 오브젝트의 것이면 건드리지 않는다.
    /// </summary>
    private bool CanPlayMoveFrames()
    {
        return !IsAttacking && !IsDead &&
               !CompareTag("Player") &&
               body_sprite_renderer != null &&
               body_sprite_renderer.gameObject == gameObject &&
               MoveClip.HasFrames;
    }

    // monster_atsp(공격속도) 쿨다운에 맞춰 공격을 시작한다.
    // protected virtual - 스피터(원거리)/스프린터·차저(돌진)/디스럭터(자폭) 등이
    // 이 메서드를 완전히 다른 공격 방식으로 교체한다.
    //
    // <b>충돌만으로 무작정 데미지가 들어가지 않는다</b>(2026-08-10 사용자 지정) - 사거리 안에
    // 들어오면 곧바로 데미지를 주는 대신, 짧은 공격 모션(예비 동작)을 먼저 재생하고 그 모션이
    // 끝난 시점에 플레이어가 여전히 사거리 안에 있어야 실제로 맞는다. 플레이어가 그 사이에
    // 벗어나면 공격이 빗나간다. 시각 연출은 전용 애니메이션이 없어 몸 색을 짧게 물들이는
    // 방식으로 대신한다(HitFlash의 흰색 피격 연출과는 다른 색·다른 셰이더 경로 - SpriteRenderer.color를
    // 직접 바꾸는 방식이라 서로 간섭하지 않는다).
    protected virtual void TryAttack()
    {
        if (player == null || IsAttacking || Time.time < next_attack_time) return;

        StartCoroutine(PerformAttackMotion());
    }

    /// <summary>
    /// 공격 모션 → 판정 순서로 진행하는 근접 공격 코루틴.
    ///
    /// 예전에는 예비 동작이 "몸 색만 잠깐 바뀌는" 것이라 실제로는 제자리에서 데미지가 들어가
    /// 사용자에게 "공격 모션 없이 공격한다"고 지적받았다(2026-08-10). 기본 좀비(서브클래스가
    /// 없는 순수 EnemyUnit)는 사용자가 직접 그린 12프레임 시퀀스(<see cref="ZombieAttackFrames"/>)로
    /// 팔을 들어올렸다 내려치는 실제 애니메이션을 재생한다. 다른 몬스터(차저/스피터/스프린터/
    /// 디스럭터/리더/보스)는 아직 전용 프레임이 없어 기존처럼 <b>몸을 뒤로 젖혔다가(예비) 앞으로
    /// 내지르는(타격) 위치 이동</b>으로 대신한다 - 좀비 프레임을 그대로 쓰면 규격(소형~초대형)마다
    /// 다른 픽셀 크기 때문에 몸 크기가 잠깐 좀비 크기로 바뀌어 보이는 문제가 생긴다.
    ///
    /// 위치 이동 방식은 몸 반지름에 비례하므로 규격이 달라도 자동으로 맞는다.
    /// 공격 중에는 <see cref="ComputeSeekDirection"/>이 0을 돌려 평소 추격 이동이 멈추므로
    /// 이 코루틴이 위치를 직접 잡거나 스프라이트를 바꿔도 서로 싸우지 않는다.
    /// </summary>
    protected virtual IEnumerator PerformAttackMotion()
    {
        IsAttacking = true;

        bool use_frame_animation = CanPlayZombieAttackFrames();

        if (use_frame_animation)
        {
            // 1) 예비 동작 - 1~6번 프레임(팔을 서서히 들어올린다)
            yield return PlayAttackFrames(0, 5, attackWindupSeconds);
            // 2) 타격 동작 - 7~8번 프레임(팔을 내려친다) - 이 끝에서 판정한다
            yield return PlayAttackFrames(6, 7, attackStrikeSeconds);
        }
        else
        {
            if (body_sprite_renderer != null) body_sprite_renderer.color = AttackTelegraphColor;

            Vector3 origin = rb != null ? rb.position : transform.position;
            Vector3 dir = AttackDirection();

            // 1) 예비 동작 - 몸을 뒤로 젖힌다(플레이어가 보고 피할 수 있는 구간)
            yield return MoveOverTime(origin, origin - dir * (body_radius * windupBackRatio), attackWindupSeconds);

            // 2) 타격 동작 - 앞으로 내지른다
            Vector3 strike_from = rb != null ? rb.position : transform.position;
            yield return MoveOverTime(strike_from, origin + dir * (body_radius * strikeLungeRatio), attackStrikeSeconds);

            if (body_sprite_renderer != null) body_sprite_renderer.color = original_body_color;
        }

        // 3) 판정 - 내지른 시점에도 플레이어가 사거리 안에 있어야 맞는다
        if (!IsDead && player != null && IsPlayerWithinAttackRange())
        {
            ExecuteAttackEffect();
        }

        float cooldown = AtSp > 0f ? 1f / AtSp : 1f; // 모션이 끝난 뒤부터 대기시간이 흐른다(무기 발사와 동일 규칙)
        next_attack_time = Time.time + cooldown;

        if (use_frame_animation)
        {
            // 4) 회복 동작 - 9~12번 프레임(원래 자세로 돌아온다)
            yield return PlayAttackFrames(8, 11, attackRecoverSeconds);
        }

        RestoreAttackVisual();
    }

    /// <summary>
    /// 좀비 전용 프레임이 다른 EnemyUnit 파생형이나 Player 렌더러에 적용되지 않도록 데이터 ID,
    /// 태그, 컴포넌트 타입, 렌더러 소유권, 원본 스프라이트를 모두 확인한다.
    /// </summary>
    private bool CanPlayZombieAttackFrames()
    {
        return MonsterId == BasicZombieMonsterId &&
               GetType() == typeof(EnemyUnit) &&
               !CompareTag("Player") &&
               body_sprite_renderer != null &&
               body_sprite_renderer.gameObject == gameObject &&
               original_body_sprite != null &&
               original_body_sprite.name == BasicZombieSpriteName &&
               ZombieAttackFrames.Length > 0;
    }

    private void RestoreAttackVisual()
    {
        if (body_sprite_renderer != null)
        {
            body_sprite_renderer.color = original_body_color;
            if (original_body_sprite != null) body_sprite_renderer.sprite = original_body_sprite;
        }

        IsAttacking = false;
    }

    /// <summary>
    /// <see cref="ZombieAttackFrames"/>의 [startIndex, endIndexInclusive] 구간을 duration 동안
    /// 균등하게 재생한다. 프레임 에셋이 없으면(로드 실패 등) 타이밍만 유지하고 조용히 넘어간다.
    /// </summary>
    private IEnumerator PlayAttackFrames(int startIndex, int endIndexInclusive, float duration)
    {
        Sprite[] frames = ZombieAttackFrames;
        if (body_sprite_renderer == null || frames.Length == 0)
        {
            if (duration > 0f) yield return new WaitForSeconds(duration);
            yield break;
        }

        int count = Mathf.Max(1, endIndexInclusive - startIndex + 1);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;
            if (!CanPlayZombieAttackFrames())
            {
                RestoreAttackVisual();
                yield break;
            }

            float t = Mathf.Clamp01(elapsed / duration);
            int frame_index = Mathf.Clamp(startIndex + Mathf.FloorToInt(t * count), 0, frames.Length - 1);
            body_sprite_renderer.sprite = frames[frame_index];

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (CanPlayZombieAttackFrames())
            body_sprite_renderer.sprite = frames[Mathf.Clamp(endIndexInclusive, 0, frames.Length - 1)];
        else
            RestoreAttackVisual();
    }

    /// <summary>공격 모션이 향할 방향(플레이어 쪽). 플레이어를 못 찾으면 바라보던 방향을 유지한다.</summary>
    private Vector3 AttackDirection()
    {
        if (player_transform == null) return transform.right;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.right;
    }

    /// <summary>
    /// <b>접촉 근접 공격</b>으로 플레이어를 때린다(기본 근접 / 차저 돌진 / 스프린터 대시가 모두 이 경로).
    ///
    /// 가시 플레이트(<see cref="PartEffect.MeleeReflectPercent"/>)의 반사가 여기서 일어난다 -
    /// 스피터의 투사체·디스럭터 자폭·보스 광역은 이 함수를 거치지 않으므로 반사되지 않는다
    /// (명세 "근접 공격 반사"를 글자 그대로 따른 것이다).
    /// </summary>
    protected void MeleeAttackPlayer(PlayerRobotController target)
    {
        if (target == null) return;

        target.TakeDamage(Atk, transform.position);

        float reflect_percent = PartEffects.GetMeleeReflectPercent();
        if (reflect_percent > 0f && !IsDead) TakeDamage(Atk * reflect_percent * 0.01f);
    }

    /// <summary>
    /// duration 동안 from → to로 부드럽게 이동시킨다(물리 스텝에 맞춰 Rigidbody 위치를 직접 잡는다).
    /// 도중에 죽으면 즉시 중단한다.
    /// </summary>
    private IEnumerator MoveOverTime(Vector3 from, Vector3 to, float duration)
    {
        if (duration <= 0f)
        {
            if (rb != null && !IsDead) rb.MovePosition(to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;

            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // 부드럽게 가속·감속해서 "젖혔다 내지르는" 느낌을 준다
            if (rb != null) rb.MovePosition(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));

            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>
    /// 공격 모션이 끝나고 판정에 성공했을 때 실제로 적용할 효과. 기본은 근접 데미지 1회.
    /// 스피터(투사체 발사)/디스럭터(자폭) 등이 이 부분만 교체해서 EnemyUnit의 공격 모션·쿨다운
    /// 뼈대(<see cref="PerformAttackMotion"/>)는 그대로 재사용한다.
    /// </summary>
    protected virtual void ExecuteAttackEffect()
    {
        MeleeAttackPlayer(player);
    }

    /// <summary>공격 판정 시점에 플레이어가 여전히 사거리(+몸통 여유) 안에 있는지 재확인한다.</summary>
    protected bool IsPlayerWithinAttackRange()
    {
        if (player_transform == null) return false;

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f;
        return to_player.magnitude <= EffectiveAttackRange();
    }

    // 다른 몬스터를 밀어내는 세기. 매 프레임 velocity를 "플레이어 방향"으로만 강제로
    // 덮어쓰면 물리 충돌로 겹침이 풀려도 바로 다음 프레임에 다시 겹치게 되어(=서로 엉겨붙어 부들거림)
    // 이동 방향 자체에 "주변 몬스터로부터 밀려나는 힘"을 함께 섞어서 자연스럽게 퍼지게 한다.
    private const float SeparationWeight = 1.4f;

    // ── 겹침 방지 반지름 ────────────────────────────────────────
    // 예전에는 SeparationRadius가 1.3유닛 고정 상수였다. 좀비의 실제 몸(콜라이더) 크기는
    // 월드 기준 1.07 x 0.89유닛뿐이라, 몸이 전혀 닿지 않았는데도 1.3유닛 안에 들어오면
    // 서로 밀어내서 "보이는 것보다 훨씬 큰 충돌 반경"으로 느껴졌다(2026-08-10 사용자 지적).
    // 게다가 상수라서 몸집이 2배 이상 큰 보스에게는 반대로 너무 작았다.
    //
    // 이제 각 유닛이 자기 콜라이더에서 몸 반지름을 직접 구하고, 두 유닛의 반지름 합보다
    // 가까울 때만(= 실제로 몸이 겹칠 때만) 밀어낸다. 프리팹 스케일만 바꿔도 자동으로 따라온다.
    // extents의 x/y 중 <b>작은 쪽</b>을 쓴다 - 원으로 근사하는 이상 큰 쪽을 쓰면 짧은 축에서
    // 몸 밖까지 판정이 나가기 때문이다("몸 크기에서 벗어나지 않아야 한다"는 요구사항).
    private float body_radius = 0.5f;

    // 겹침 방지 반지름에 곱하는 계수. 몸 반지름 합을 그대로 쓰면(=1.0) 콜라이더가 그림보다
    // 넉넉한 탓에 좀비들이 실제 몸 크기보다 훨씬 넓게 벌어져 서 있었다.
    // 2026-08-10 사용자 지정: "그 간격이 지나치게 넓음. 현재의 1/3으로 줄여도 무방."
    private const float SeparationRadiusScale = 1f / 3f;

    // OverlapSphere 검색 반경을 정하려면 "가장 큰 이웃"의 반지름을 알아야 한다(보스처럼 큰
    // 유닛이 작은 좀비를 밀어내는 경우). 스폰될 때마다 갱신되는 전역 최댓값을 쓴다.
    private static float max_body_radius = 0.5f;

    /// <summary>겹침 방지에 쓰는 몸 반지름(월드 단위). 콜라이더 → 스프라이트 순으로 실측한다.</summary>
    public float BodyRadius => body_radius;

    private void ComputeBodyRadius()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            body_radius = Mathf.Max(0.05f, Mathf.Min(col.bounds.extents.x, col.bounds.extents.y));
        }
        else
        {
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null) body_radius = Mathf.Max(0.05f, Mathf.Min(sr.bounds.extents.x, sr.bounds.extents.y));
        }

        if (body_radius > max_body_radius) max_body_radius = body_radius;
    }

    // 넉백으로 밀려나는 속도(유닛/초)와 초당 감쇠량. ApplyKnockback이 채우고 FixedUpdate가 소비한다.
    private Vector3 knockback_velocity;
    private const float KnockbackDecay = 40f;

    // 플레이어 로봇의 몸에 밀려나는 속도. PlayerRobotController가 매 FixedUpdate에 채우고
    // 이쪽 FixedUpdate가 소비한 뒤 비운다(넉백과 달리 감쇠 없이 '닿아 있는 동안만' 작용).
    private Vector3 player_push_velocity;

    /// <summary>
    /// 플레이어 로봇이 이동하면서 몸으로 밀어낼 때 호출한다(<see cref="PlayerRobotController"/>).
    ///
    /// 좀비는 매 FixedUpdate에 <see cref="rb"/>의 속도를 통째로 덮어쓰기 때문에, 물리 충돌만으로는
    /// 절대 밀리지 않는다(밀려나도 다음 프레임에 원래 속도로 되돌아간다). 그래서 이동 속도에
    /// 더해질 별도 성분으로 받아둔다(2026-08-10 사용자 요청: "로봇은 이동할 때 좀비를 밀어낼 수
    /// 있어야 함").
    /// </summary>
    public void ApplyPlayerPush(Vector3 velocity) => player_push_velocity = velocity;

    protected virtual void FixedUpdate()
    {
        if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        if (player_transform == null)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        Vector3 seek = ComputeSeekDirection();
        Vector3 separation = ComputeSeparation();

        // 겹침 방지 힘이 플레이어 반대 방향으로 작용하면 좀비가 잠시 도망가는 것처럼 보인다.
        // 뒤쪽 성분만 제거해 전진은 항상 유지하고, 좌우로 비켜 가는 분리 성분은 보존한다.
        if (seek.sqrMagnitude > 0.0001f)
        {
            Vector3 seek_direction = seek.normalized;
            float separation_forward = Vector3.Dot(separation, seek_direction);
            if (separation_forward < 0f) separation -= seek_direction * separation_forward;
        }

        Vector3 move_dir = seek + separation;
        Vector3 move_velocity = move_dir.sqrMagnitude > 0.0001f ? move_dir.normalized * (MoveSpeed * aura_slow_multiplier) : Vector3.zero;

        // 넉백 속도를 시간에 따라 줄이면서 이동 속도에 더한다(덮어쓰지 않는다 - ApplyKnockback 주석 참고)
        knockback_velocity = Vector3.MoveTowards(knockback_velocity, Vector3.zero, KnockbackDecay * Time.fixedDeltaTime);

        rb.linearVelocity = move_velocity + knockback_velocity + player_push_velocity;

        // 플레이어가 계속 밀고 있으면 매 프레임 다시 채워준다. 여기서 비우므로 떨어지는 순간
        // 곧바로 멈춘다(넉백처럼 여운이 남지 않는다).
        player_push_velocity = Vector3.zero;
    }

    private const float FacingVelocityThreshold = 0.01f;

    /// <summary>
    /// 일반 추적·돌진·도주·넉백을 모두 포함한 최종 Rigidbody 이동 방향으로 스프라이트를 뒤집는다.
    /// 원본 이미지(Enemy_zombie_S 등)는 팔을 <b>왼쪽</b>으로 뻗은 상태로 그려져 있으므로, 왼쪽이
    /// 기본(flipX=false)이고 오른쪽으로 이동할 때만 뒤집어야 한다. 거의 정지했을 때는 마지막
    /// 방향을 유지한다.
    /// (2026-08-12 사용자 지적 "좀비 이동 방향이 바라보는 반대로 적용됨" - 부호가 반대로
    /// 들어가 있어 오른쪽으로 이동할 때 왼쪽을 보고, 왼쪽으로 이동할 때 오른쪽을 보고 있었다.)
    /// </summary>
    protected virtual void LateUpdate()
    {
        if (body_sprite_renderer == null || rb == null) return;

        float horizontal_velocity = rb.linearVelocity.x;
        if (Mathf.Abs(horizontal_velocity) >= FacingVelocityThreshold)
            body_sprite_renderer.flipX = horizontal_velocity > 0f;

        UpdateWalkAnimation();
    }

    /// <summary>
    /// 감지 범위나 AI 성격값 없이 항상 현재 플레이어 위치를 향하는 이동 방향(정규화 전).
    /// protected virtual - 스프린터/차저처럼 돌진 상태에서 이 로직을 완전히 대체하는
    /// 서브클래스와, 팔로워 전멸 후 도주하는 리더를 위해 열어둔다.
    /// </summary>
    protected virtual Vector3 ComputeSeekDirection()
    {
        if (IsAttacking) return Vector3.zero; // 공격 모션 중에는 자리를 지킨다(들이받으면서 공격하지 않도록)

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f;

        // 이미 공격이 닿는 거리면 더 파고들지 않는다.
        // 예전에는 사거리 안에 들어와도 계속 전진해서, 공격 한 번 하고 나면 쿨다운 동안
        // 플레이어 몸속까지 밀고 들어왔다(2026-08-10 사용자 지적). 사거리보다 살짝 안쪽
        // (StopDistanceRatio)에서 멈춰, 미세하게 앞뒤로 떠는 것도 막는다.
        float stop_distance = EffectiveAttackRange() * StopDistanceRatio;
        if (to_player.sqrMagnitude <= stop_distance * stop_distance) return Vector3.zero;

        return to_player;
    }

    // 공격 사거리의 몇 %까지 접근하고 멈출지. 1.0으로 두면 사거리 경계에서 멈췄다 나갔다를
    // 반복하며 떨 수 있어 살짝 안쪽으로 들어와 자리를 잡게 한다.
    private const float StopDistanceRatio = 0.9f;

    // 몸이 실제로 겹치는 이웃들로부터 밀려나는 방향을 합산한다.
    // 예전에는 고정 반경(1.3) 안에 들어오기만 하면 거리 역수(1/dist)로 계속 밀어냈지만,
    // 이제는 두 몸의 반지름 합보다 가까울 때만, 파고든 깊이에 비례해서 밀어낸다.
    // 그래서 몸이 닿기 전에는 서로 전혀 밀지 않는다.
    private Vector3 ComputeSeparation()
    {
        Vector3 push = Vector3.zero;

        // 나와 "가장 큰 이웃"이 닿을 수 있는 최대 거리까지만 검색하면 충분하다.
        float searchRadius = (body_radius + max_body_radius) * SeparationRadiusScale;

        Collider[] nearby = Physics.OverlapSphere(transform.position, searchRadius);
        foreach (Collider col in nearby)
        {
            EnemyUnit other = col.GetComponent<EnemyUnit>();
            if (other == null) other = col.GetComponentInParent<EnemyUnit>();
            if (other == null || other == this) continue;

            Vector3 diff = transform.position - other.transform.position;
            diff.z = 0f;

            float dist = diff.magnitude;
            // 두 몸이 딱 맞닿는 중심간 거리 x SeparationRadiusScale.
            // 반지름 합 그대로 쓰면 콜라이더가 스프라이트보다 넉넉해서 실제 그림보다 훨씬
            // 멀찍이 벌어져 보였다(2026-08-10 사용자 지정으로 1/3).
            float minDist = (body_radius + other.body_radius) * SeparationRadiusScale;
            if (dist >= minDist) continue;                   // 아직 안 겹쳤으면 밀어내지 않는다

            // 완전히 겹쳐 방향을 못 구하는 경우(거의 같은 좌표)에는 임의 방향으로 살짝 흩는다.
            Vector3 dir = dist > 0.0001f ? diff / dist : new Vector3(Random.value - 0.5f, Random.value - 0.5f, 0f).normalized;

            push += dir * ((minDist - dist) / minDist); // 파고든 비율만큼만 (0~1)
        }

        return push * SeparationWeight;
    }

    /// <summary>
    /// 방어력(방어무시 반영)을 적용한 <b>실제로 깎이는 체력</b>. 최소 1은 보장한다.
    ///
    /// <see cref="TakeDamage"/>가 쓰는 계산을 그대로 노출한 것이다 - 디스럭터처럼 "이 피해가
    /// 들어가면 체력이 임계치 밑으로 떨어지는가"를 <b>맞기 전에</b> 판단해야 하는 서브클래스가
    /// 같은 공식을 복사해 쓰지 않도록 protected로 열어 둔다(2026-08-24 자폭 조건 수정).
    /// </summary>
    protected float ComputeEffectiveDamage(float amount, float defIgnorePercent)
    {
        float effective_def = Def * (1f - Mathf.Clamp01(defIgnorePercent));
        return Mathf.Max(1f, amount - effective_def);
    }

    /// <summary>
    /// 피해를 입힌다. def_ignore_percent(0~1)만큼 방어력이 무시된다
    /// (플라즈마 캐논 0.5 = 방어력 절반 무시, 레이저 피스톨 0.25 등).
    /// 기본값이 0이라 방어무시가 없는 기존 호출부는 그대로 동작한다.
    /// </summary>
    public virtual void TakeDamage(float amount, float def_ignore_percent = 0f, int source_weapon_id = 0, bool isCrit = false)
    {
        if (IsDead) return;

        // 해금 조건 2건("근접 무기로 300마리" / "정밀화기로 200마리", 2026-08-19 Phase E)이
        // 마지막 타격 무기를 알아야 해서 남겨 둔다. 무기가 아닌 피해(디스크의 연쇄 번개 등)는
        // 0이 넘어오며, 그 경우 어느 무기 카운터도 오르지 않는다.
        if (source_weapon_id > 0) LastDamageWeaponId = source_weapon_id;

        float dmg = ComputeEffectiveDamage(amount, def_ignore_percent);
        CurrentHp -= dmg;
        UpdateHealthBar();
        PlayHitFlash();

        // 2026-08-23 사용자 제공 좀비 피격 이펙트(8프레임) - 몸 전체가 물드는 HitFlash와
        // 별개로 몸통 중앙에 스파크 스프라이트를 한 번 더 재생한다. 크기는 본인 스프라이트
        // 폭에 비례시켜 좀비/차저/보스 등 규격이 달라도 자동으로 맞는다.
        if (body_sprite_renderer != null)
        {
            ZombieHitEffect.Play(transform.position, body_visual_width * 0.5f,
                body_sprite_renderer.sortingOrder + 3);
        }

        // 2026-08-20 사용자 요청 - 맞은 자리 위로 데미지 숫자를 잠깐 띄운다. 체력바가 이미
        // 몸통 위 정확한 높이에 자리를 잡아뒀으므로 그 위치를 그대로 재사용한다.
        Vector3 popup_pos = health_bar_root != null ? health_bar_root.position : transform.position + Vector3.up;
        DamageNumberUI.ShowDealt(popup_pos, dmg, isCrit);

        if (CurrentHp <= 0f) Die();
    }

    /// <summary>외부(리더의 회복 능력 등)에서 체력을 채운다. MaxHp를 넘지 않고, 죽은 유닛에는 적용하지 않는다.</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        CurrentHp = Mathf.Min(MaxHp, CurrentHp + amount);
        UpdateHealthBar();
    }

    // ── 체력바 ──────────────────────────────────────────────────
    // Canvas 없이 SpriteRenderer 두 장(배경/채움)만으로 만든 머리 위 체력바.
    // 100%일 때는 숨겨져 있다가 피해를 입어 CurrentHp < MaxHp가 되는 순간부터 보인다.
    // 폭/두께를 본인 스프라이트 크기에 비례시켜서, 좀비/차저/보스처럼 스케일이 크게
    // 달라도(EnemyUnit 실측 콜라이더 작업과 같은 원칙) 몸집에 맞는 바가 자동으로 나온다.
    private void BuildHealthBar()
    {
        body_sprite_renderer = GetComponent<SpriteRenderer>();
        if (body_sprite_renderer == null) return;

        // SpriteRenderer.bounds는 월드 스페이스 값이다. health_bar_root는 이 오브젝트(transform)의
        // 자식이라 localPosition/localScale에 넣는 값에는 부모의 스케일이 다시 곱해진다 - 그래서
        // 월드 스페이스로 잰 값을 그대로 쓰면 스케일이 이중으로 적용돼(예: 좀비는 0.13배) 체력바가
        // 훨씬 작고 낮은 위치(거의 얼굴 높이)에 그려지는 문제가 있었다. 부모 스케일로 한 번 나눠서
        // "로컬 공간에서 이 값을 넣으면 월드 기준으로 원하는 크기/위치가 나오도록" 되돌린다.
        float scale = transform.lossyScale.x; // 좀비/차저/보스/플레이어 전부 x=y=z 균등 스케일
        if (Mathf.Approximately(scale, 0f)) scale = 1f;

        float world_sprite_width = body_sprite_renderer.bounds.size.x * Mathf.Max(0.01f, BodyVisualWidthScale);

        // 그림의 실제 상단을 pivot(캔버스 중앙) 기준 높이로 환산한다. 투명 캔버스를 먼저 걷어내야
        // 체력바와 피격 이펙트가 빈 공간을 몸집으로 오인하지 않는다. 기본값 (0,1)이면
        // (1 - 0.5) x 캔버스높이 = 캔버스 절반 높이라 예전 동작과 완전히 같다.
        Vector2 visual_range = BodyVisualVerticalRange;
        float visual_top_fraction = Mathf.Clamp01(Mathf.Max(visual_range.x, visual_range.y));
        float world_sprite_top = (visual_top_fraction - 0.5f) * body_sprite_renderer.bounds.size.y;

        // 프레임 세트마다 캔버스 크기가 다를 수 있으므로, 몸집 기준값은 여기(프리팹 스프라이트)에서
        // 한 번만 재서 캐시한다(위 body_visual_width 주석 참고).
        body_visual_width = world_sprite_width;
        body_visual_half_height = world_sprite_top;

        health_bar_width = Mathf.Max(0.05f, (world_sprite_width * healthBarWidthRatio) / scale);
        float thickness = health_bar_width * healthBarThicknessRatio;
        float local_top_offset = (world_sprite_top + healthBarMargin) / scale;

        health_bar_root = new GameObject("HealthBar").transform;
        health_bar_root.SetParent(transform, false);
        health_bar_root.localPosition = new Vector3(0f, local_top_offset, 0f);

        int sorting_base = body_sprite_renderer.sortingOrder + 5;

        SpriteRenderer bg = CreateBarPart("Background", GetWhitePixelSprite(), health_bar_root, sorting_base, new Color(0.1f, 0.1f, 0.1f, 0.85f));
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(health_bar_width, thickness, 1f);

        // 채움 바는 pivot을 왼쪽(0, 0.5)에 둔 전용 스프라이트를 써서, 폭이 줄어들 때
        // 왼쪽 끝은 고정된 채 오른쪽 끝만 줄어들게 한다(가운데 pivot이면 양쪽이 다 줄어든다).
        SpriteRenderer fill = CreateBarPart("Fill", GetLeftPivotPixelSprite(), health_bar_root, sorting_base + 1, Color.green);
        fill.transform.localPosition = new Vector3(-health_bar_width * 0.5f, 0f, 0f); // 바 왼쪽 끝에 고정

        // 두께(y)를 배경과 똑같이 명시적으로 맞춘다. 예전에는 여기서 localScale을 아예 설정하지
        // 않아 기본값 1이 그대로 남았고(UpdateHealthBar는 x만 바꾸고 y는 기존값을 유지한다),
        // 프리팹 스케일이 0.134였을 때는 월드 두께가 우연히 배경과 비슷해져서 눈에 띄지 않았다.
        // 2026-08-10 규격 적용으로 프리팹 스케일이 1이 되면서 채움 바만 두께 1.0으로 그려져
        // 체력바가 위아래로 뚱뚱해 보이는 문제로 드러났다.
        fill.transform.localScale = new Vector3(health_bar_width, thickness, 1f);
        health_bar_fill = fill.transform;

        health_bar_root.gameObject.SetActive(false); // 초기값은 Init()에서 UpdateHealthBar()가 결정
    }

    private static SpriteRenderer CreateBarPart(string name, Sprite sprite, Transform parent, int sortingOrder, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return sr;
    }

    /// <summary>
    /// 몸에 붙어 따라다니는 <b>모든 부속 시각물</b>(체력바, 그로기 별 등 자식 오브젝트)을 끈다.
    ///
    /// 사망 연출이 몸 스프라이트만 끄고 오브젝트를 잠시 살려두는 경우(보스의
    /// <c>DeathSequence</c>는 폭발 이펙트가 끝날 때까지 몇 초 더 살아있다)에 쓴다 - 몸만 끄면
    /// 체력바와 머리 위 별이 허공에 떠서 그대로 보인다(2026-08-24 사용자 리포트
    /// "보스 사망 이후에도 보스의 UI나 이펙트 등이 남아있는 문제").
    /// </summary>
    protected void HideAttachedVisuals()
    {
        if (health_bar_root != null) health_bar_root.gameObject.SetActive(false);

        // 자식으로 붙는 런타임 이펙트(GroggyStarsEffect 등)까지 한꺼번에 끈다. 몸통 렌더러는
        // 호출부가 직접 다루므로(사망 프레임을 마지막까지 보여주는 경우가 있다) 건드리지 않는다.
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr != null && sr.gameObject != gameObject) sr.enabled = false;
        }
    }

    /// <summary>MaxHp 대비 현재 체력 비율에 맞춰 채움 바 폭을 갱신하고, 100% 미만일 때만 보이게 한다.</summary>
    private void UpdateHealthBar()
    {
        if (health_bar_root == null) return;

        float ratio = MaxHp > 0f ? Mathf.Clamp01(CurrentHp / MaxHp) : 0f;
        bool should_show = ratio < 1f && ratio > 0f;

        health_bar_root.gameObject.SetActive(should_show);
        if (!should_show) return;

        health_bar_fill.localScale = new Vector3(health_bar_width * ratio, health_bar_fill.localScale.y, 1f);
        health_bar_fill.GetComponent<SpriteRenderer>().color = Color.Lerp(Color.red, Color.green, ratio);
    }

    /// <summary>Canvas 없이 SpriteRenderer로 단색 막대를 그리기 위한 1x1 흰색 스프라이트(pivot 중앙, 캐시).</summary>
    private static Sprite GetWhitePixelSprite()
    {
        if (white_pixel_sprite == null) white_pixel_sprite = CreateWhitePixelSprite(new Vector2(0.5f, 0.5f));
        return white_pixel_sprite;
    }

    /// <summary>채움 바 전용 - pivot이 왼쪽(0, 0.5)인 1x1 흰색 스프라이트(캐시).</summary>
    private static Sprite GetLeftPivotPixelSprite()
    {
        if (white_pixel_sprite_left == null) white_pixel_sprite_left = CreateWhitePixelSprite(new Vector2(0f, 0.5f));
        return white_pixel_sprite_left;
    }

    private static Sprite CreateWhitePixelSprite(Vector2 pivot)
    {
        Texture2D tex = new Texture2D(4, 4);
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), pivot, 4f); // ppu=4 => 4px 텍스처가 1x1 유닛
    }

    /// <summary>
    /// 넉백을 건다. <b>Rigidbody.AddForce는 이 클래스에서 쓸 수 없다</b> -
    /// FixedUpdate가 매 프레임 linearVelocity를 통째로 덮어쓰기 때문에 다음 프레임에 사라진다.
    /// 그래서 넉백을 별도 속도로 들고 있다가 이동 속도에 <b>더해서</b> 합성한다.
    ///
    /// 이동을 정지시키는 대신 합성하는 이유: 전기톱검(공속 3/s)처럼 빠른 근접무기 앞에서
    /// 적이 영구히 밀려나 접근조차 못 하는 상태(무한 안전지대)가 되는 것을 막기 위함이다.
    /// </summary>
    // 좀비 기획서 Ver04 p.22 예외처리("질량이 높을수록 덜, 낮을수록 더 밀려남")는
    // 사용자 지정으로 이번 작업 범위에서는 보류한다 - 넉백은 지금 당장 적용하지 않는다.
    // Mass 필드/데이터는 이미 들어와 있으므로, 나중에 적용하려면 ReferenceMass 대비 배율
    // (ReferenceMass / Mathf.Max(1f, Mass))을 strength에 곱하면 된다.
    public void ApplyKnockback(Vector3 direction, float strength)
    {
        if (IsDead || strength <= 0f) return;

        direction.z = 0f; // X-Y 평면만 사용
        if (direction.sqrMagnitude <= 0.0001f) return;

        knockback_velocity = direction.normalized * strength; // 누적하지 않고 덮어쓴다(연사에 밀려 날아가지 않도록)
    }

    protected virtual void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (rb != null) rb.linearVelocity = Vector3.zero;

        GrantKillRewards();
        OnKilledByPlayer?.Invoke(this);

        Destroy(gameObject);
    }

    // AI 코어 경험치 + 골드를 필드 위의 픽업 오브젝트로 생성한다(플레이어가 다가가면 자동 흡수 -
    // RewardPickup 참고). 데이터테이블에 아직 몬스터별 경험치/골드 컬럼이 없어서(기획 확정 전)
    // MaxHp 비례 임시 공식을 사용한다. 실제 값 컬럼이 시트에 추가되면 이 공식을 그 값으로 교체한다.
    //
    // 이 나눗셈 상수는 체력 배율에 맞춰 같이 조정해야 한다(원래 값 10/20 기준).
    // 2026-08-09 DPS 기준 체력 리밸런싱(좀비 30→180, 6배)에 맞춰 6배(60/120)로 올렸고,
    // 2026-08-10 난이도 완화(좀비 180→90, 1/2배)에 맞춰 다시 절반(30/60)으로 내렸다 -
    // 최종적으로 원래 값 대비 3배다. 이 상수를 같이 맞추지 않으면 체력만 바꾼 것으로
    // 경험치·골드가 같은 배율로 따라 변해 레벨업/골드 획득 속도가 의도치 않게 바뀐다.
    // 체력을 다시 조정하면 이 상수도 반드시 같은 배율로 맞출 것.
    //
    // 2026-08-18 기본 장착 무기가 4정 -> 1정이 되면서 화력이 약 1/4로 줄어, 좀비 체력을
    // 70 -> 40(약 1.75배 감소)으로 재앵커했다. 그에 맞춰 이 상수도 1.75배 내린다(30/60 -> 17/34).
    private const int ExpPerMaxHp = 17;
    private const int GoldPerMaxHp = 34;

    private void GrantKillRewards()
    {
        // 예전에는 처치할 때마다 경험치와 골드가 항상 하나씩 나와서, 60초 웨이브 한 번에
        // 픽업이 수백 개씩 쌓였다. 이제는 확률 드랍이다(사용자 지정: 경험치 1/2, 골드 1/3).
        if (Random.value < ExpDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.Exp, Mathf.Max(1, Mathf.FloorToInt(MaxHp / ExpPerMaxHp)), transform.position);
        }

        if (Random.value < GoldDropChance)
        {
            // 나눗셈을 <b>반올림</b>한다(2026-08-13). 정수 나눗셈(내림)이었을 때는 체력이 낮은
            // 몬스터일수록 손실률이 커서(체력 110 → 1.83이 1로 깎여 -45%) 이번 체력 하향이
            // 골드 수입까지 조용히 깎아내렸다.
            int goldAmount = Mathf.Max(1, Mathf.RoundToInt(MaxHp / GoldPerMaxHp));
            RewardPickupManager.SpawnReward(RewardType.Gold, goldAmount, transform.position);
        }

        TryDropPartBox();
    }

    // 골드/경험치와 별개로, PartsCatalog.PartBoxDropChance 확률로 부품 상자를 추가로 드랍한다.
    // 단, 머리(로봇)의 적재량 상한에 도달했으면 아예 드랍하지 않는다 - 주울 수 없는 상자가
    // 필드에 쌓이면 플레이어가 헛걸음을 하게 되므로 나오지 않는 편이 낫다.
    private void TryDropPartBox()
    {
        if (modding_manager_cache == null) modding_manager_cache = FindFirstObjectByType<ModdingManager>();
        if (modding_manager_cache == null || modding_manager_cache.Catalog == null) return;

        if (!modding_manager_cache.CanReceiveMorePartBoxes) return;

        // 2026-08-24 사용자 지정("2~3웨이브 마다 1개씩은 나오게") - 카탈로그 값을 그대로 쓰지 않고
        // ModdingManager가 계산한 실효 확률을 쓴다. 마지막 드랍 이후 지정 웨이브가 지나면 확률이
        // 크게 올라가고, 그 웨이브에도 안 나오면 웨이브 종료 시점에 1개가 확정 지급된다
        // (ModdingManager.EffectivePartBoxDropChance 주석 참고).
        if (Random.value <= modding_manager_cache.EffectivePartBoxDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.PartBox, 1, transform.position);
            modding_manager_cache.NotifyPartBoxDropped();
        }
    }
}
