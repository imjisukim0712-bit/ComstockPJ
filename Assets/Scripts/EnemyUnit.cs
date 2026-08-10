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
/// - 드랍테이블   : 사망 시 DropTableManager.RollDrop()으로 결정, DropItemManager가 땅에 상자로 생성
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyUnit : MonoBehaviour
{
    // 자동공격(PlayerShootManager)이 "사거리 내 최근접 적"을 찾을 때 순회하는 전역 생존 목록.
    // Physics.OverlapSphere를 무기 슬롯마다 매 프레임 돌리는 대신, 스폰/사망 시점에만 갱신되는
    // 이 목록을 순회하는 쪽이 더 저렴하다.
    public static readonly List<EnemyUnit> Alive = new List<EnemyUnit>();

    /// <summary>넉백 배율 기준점(기획서 p.15 좀비 기본 질량). 이 질량보다 무거우면 덜, 가벼우면 더 밀려난다.</summary>
    public const float ReferenceMass = 50f;

    public int MonsterId { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int Atk { get; private set; }
    public int Def { get; private set; }
    public float MoveSpeed { get; private set; }
    public float AttackRange { get; private set; } // monster_range: 공격 사거리
    public float AtSp { get; private set; }         // monster_atsp: 공격속도 - 공격 쿨다운에 사용
    public float Mass { get; private set; } = ReferenceMass;       // monster_mass - 넉백 저항(기획서 p.22)
    public float DetectRange { get; private set; } = float.MaxValue; // monster_detect - AI 각성 판정에 쓰는 감지범위
    public MonsterAiType AiType { get; private set; } = MonsterAiType.Aggressive;
    public bool IsDead { get; private set; }

    /// <summary>현재 플레이어를 추격 중인지(AI 성격값 상태 머신 - 좀비 기획서 Ver04 p.10).</summary>
    public bool IsAlerted { get; private set; }

    // 다음 공격이 가능한 시각 (monster_atsp 기반 쿨다운). 서브클래스(스프린터/차저 등)가
    // 자신만의 공격 타이밍을 구현할 때도 같은 필드를 재사용할 수 있도록 protected로 열어둔다.
    protected float next_attack_time = 0f;

    // protected - 스프린터/차저(돌진)처럼 자신만의 이동 상태에서 직접 속도를 제어하는
    // 서브클래스를 위해 열어둔다.
    protected Rigidbody rb;
    protected Transform player_transform;
    protected PlayerRobotController player;
    private Rigidbody player_rb; // 압박형(Pressure) AI의 이동 예측에 쓰는 플레이어 속도 소스

    [Header("체력바 (체력이 100% 미만일 때만 표시)")]
    [Tooltip("체력바 폭 = 본인 스프라이트 폭 × 이 비율")]
    [SerializeField] private float healthBarWidthRatio = 0.55f;
    [Tooltip("체력바 두께(월드 유닛). 몬스터마다 스케일이 달라도 두께는 폭 대비 일정 비율로 맞춘다")]
    [SerializeField] private float healthBarThicknessRatio = 0.16f;
    [Tooltip("스프라이트 맨 위에서 체력바까지 추가로 띄우는 여백(월드 유닛)")]
    [SerializeField] private float healthBarMargin = 0.15f;

    private SpriteRenderer body_sprite_renderer;
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
    }

    // 처치 시 보상이 나올 확률(사용자 지정). 부품 상자 확률만 PartsCatalog 에셋에 있고
    // 경험치/골드는 데이터로 뺄 자리가 아직 없어 여기 상수로 둔다 - 밸런스 미확정 임시값.
    private const float ExpDropChance = 0.5f;   // 1/2
    private const float GoldDropChance = 1f / 3f; // 1/3

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
        DetectRange = data.EffectiveDetectRange;
        AiType = data.monster_ai;
        IsAlerted = false;
        wander_timer = 0f; // 배회형이면 다음 FixedUpdate에서 곧바로 새 방향을 뽑는다

        UpdateHealthBar(); // 스폰 직후는 항상 100%이므로 바를 숨긴 상태로 초기화한다
    }

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

    private void OnDestroy()
    {
        Alive.Remove(this);
    }

    private void FindPlayer()
    {
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj == null) return;

        player_transform = player_obj.transform;
        player = player_obj.GetComponent<PlayerRobotController>();
        player_rb = player_obj.GetComponent<Rigidbody>();
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

        UpdateAlertState(distance);

        if (distance <= AttackRange + BodyContactRadius())
        {
            TryAttack();
        }
    }

    // ── AI 성격값 (좀비 기획서 Ver04 p.10) ──────────────────────
    // 추격형/압박형은 감지범위 전체로 정찰하고, 휴식형/배회형은 그보다 훨씬 좁은
    // (감지범위의 1/10) 범위 안에 들어와야, 또는 피격당해야 깨어난다.
    // 한 번 깨어나면(추격 시작) 거리가 감지범위 x2 이상 벌어져야 다시 대기 상태로 돌아간다.
    private const float PassiveDetectDivisor = 10f;
    private const float DeaggroMultiplier = 2f;

    private float PassiveDetectRadius =>
        (AiType == MonsterAiType.Resting || AiType == MonsterAiType.Wandering)
            ? DetectRange / PassiveDetectDivisor
            : DetectRange;

    private void UpdateAlertState(float distanceToPlayer)
    {
        if (IsAlerted)
        {
            if (distanceToPlayer > DetectRange * DeaggroMultiplier) IsAlerted = false; // 추격 종료 → 대기 상태 복귀
            return;
        }

        if (distanceToPlayer <= PassiveDetectRadius) IsAlerted = true;
    }

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
        float self_radius = 0.6f;
        Collider self_collider = GetComponent<Collider>();
        if (self_collider != null) self_radius = Mathf.Max(self_collider.bounds.extents.x, self_collider.bounds.extents.y);

        float player_radius = 0.8f;
        if (player != null)
        {
            Collider player_collider = player.GetComponent<Collider>();
            if (player_collider != null) player_radius = Mathf.Max(player_collider.bounds.extents.x, player_collider.bounds.extents.y);
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

        bool use_frame_animation = GetType() == typeof(EnemyUnit) && ZombieAttackFrames.Length > 0;

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
            if (body_sprite_renderer != null) body_sprite_renderer.sprite = original_body_sprite;
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

            float t = Mathf.Clamp01(elapsed / duration);
            int frame_index = Mathf.Clamp(startIndex + Mathf.FloorToInt(t * count), 0, frames.Length - 1);
            body_sprite_renderer.sprite = frames[frame_index];

            elapsed += Time.deltaTime;
            yield return null;
        }

        body_sprite_renderer.sprite = frames[Mathf.Clamp(endIndexInclusive, 0, frames.Length - 1)];
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
        player.TakeDamage(Atk);
    }

    /// <summary>공격 판정 시점에 플레이어가 여전히 사거리(+몸통 여유) 안에 있는지 재확인한다.</summary>
    protected bool IsPlayerWithinAttackRange()
    {
        if (player_transform == null) return false;

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f;
        return to_player.magnitude <= AttackRange + BodyContactRadius();
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

        Vector3 move_dir = seek + separation;
        Vector3 move_velocity = move_dir.sqrMagnitude > 0.0001f ? move_dir.normalized * MoveSpeed : Vector3.zero;

        // 넉백 속도를 시간에 따라 줄이면서 이동 속도에 더한다(덮어쓰지 않는다 - ApplyKnockback 주석 참고)
        knockback_velocity = Vector3.MoveTowards(knockback_velocity, Vector3.zero, KnockbackDecay * Time.fixedDeltaTime);

        rb.linearVelocity = move_velocity + knockback_velocity;
    }

    // 압박형(Pressure)의 이동 예측 시간(초). 기획서에 구체적 수치가 없어 임시값이다.
    private const float PressurePredictionTime = 0.6f;

    /// <summary>
    /// AI 성격값에 따른 이동 방향(정규화 전). 대기 상태(미각성)에서는 배회형만 움직이고
    /// 나머지는 가만히 서 있는다. 각성 상태에서는 압박형만 플레이어의 예상 이동 방향을
    /// 선회 추적하고, 나머지는 현재 위치로 직진한다(좀비 기획서 Ver04 p.10).
    /// protected virtual - 스프린터/차저처럼 돌진 상태에서 이 로직을 완전히 대체하는
    /// 서브클래스를 위해 열어둔다.
    /// </summary>
    protected virtual Vector3 ComputeSeekDirection()
    {
        if (IsAttacking) return Vector3.zero; // 공격 모션 중에는 자리를 지킨다(들이받으면서 공격하지 않도록)

        if (!IsAlerted)
        {
            return AiType == MonsterAiType.Wandering ? ComputeWanderDirection() : Vector3.zero;
        }

        if (AiType == MonsterAiType.Pressure)
        {
            Vector3 predicted_offset = PredictPlayerPosition() - transform.position;
            predicted_offset.z = 0f;
            return predicted_offset;
        }

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f;
        return to_player;
    }

    private Vector3 PredictPlayerPosition()
    {
        Vector3 velocity = player_rb != null ? player_rb.linearVelocity : Vector3.zero;
        velocity.z = 0f;
        return player_transform.position + velocity * PressurePredictionTime;
    }

    // ── 배회형(Wandering) 자유 배회 ──────────────────────────────
    private const float WanderMinInterval = 1.5f;
    private const float WanderMaxInterval = 3.5f;
    private Vector3 wander_direction = Vector3.right;
    private float wander_timer;

    private Vector3 ComputeWanderDirection()
    {
        wander_timer -= Time.fixedDeltaTime;
        if (wander_timer <= 0f)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            wander_direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            wander_timer = Random.Range(WanderMinInterval, WanderMaxInterval);
        }
        return wander_direction;
    }

    // 몸이 실제로 겹치는 이웃들로부터 밀려나는 방향을 합산한다.
    // 예전에는 고정 반경(1.3) 안에 들어오기만 하면 거리 역수(1/dist)로 계속 밀어냈지만,
    // 이제는 두 몸의 반지름 합보다 가까울 때만, 파고든 깊이에 비례해서 밀어낸다.
    // 그래서 몸이 닿기 전에는 서로 전혀 밀지 않는다.
    private Vector3 ComputeSeparation()
    {
        Vector3 push = Vector3.zero;

        // 나와 "가장 큰 이웃"이 닿을 수 있는 최대 거리까지만 검색하면 충분하다.
        float searchRadius = body_radius + max_body_radius;

        Collider[] nearby = Physics.OverlapSphere(transform.position, searchRadius);
        foreach (Collider col in nearby)
        {
            EnemyUnit other = col.GetComponent<EnemyUnit>();
            if (other == null) other = col.GetComponentInParent<EnemyUnit>();
            if (other == null || other == this) continue;

            Vector3 diff = transform.position - other.transform.position;
            diff.z = 0f;

            float dist = diff.magnitude;
            float minDist = body_radius + other.body_radius; // 두 몸이 딱 맞닿는 중심간 거리
            if (dist >= minDist) continue;                   // 아직 안 겹쳤으면 밀어내지 않는다

            // 완전히 겹쳐 방향을 못 구하는 경우(거의 같은 좌표)에는 임의 방향으로 살짝 흩는다.
            Vector3 dir = dist > 0.0001f ? diff / dist : new Vector3(Random.value - 0.5f, Random.value - 0.5f, 0f).normalized;

            push += dir * ((minDist - dist) / minDist); // 파고든 비율만큼만 (0~1)
        }

        return push * SeparationWeight;
    }

    /// <summary>
    /// 피해를 입힌다. def_ignore_percent(0~1)만큼 방어력이 무시된다
    /// (플라즈마 캐논 0.5 = 방어력 절반 무시, 레이저 피스톨 0.25 등).
    /// 기본값이 0이라 방어무시가 없는 기존 호출부는 그대로 동작한다.
    /// </summary>
    public virtual void TakeDamage(int amount, float def_ignore_percent = 0f)
    {
        if (IsDead) return;

        int effective_def = Mathf.RoundToInt(Def * (1f - Mathf.Clamp01(def_ignore_percent)));
        int dmg = Mathf.Max(1, amount - effective_def);
        CurrentHp -= dmg;
        UpdateHealthBar();
        PlayHitFlash();
        if (CurrentHp <= 0) Die();
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

        float world_sprite_width = body_sprite_renderer.bounds.size.x;
        float world_sprite_top = body_sprite_renderer.bounds.extents.y; // pivot이 중앙이므로 top = 절반 높이(월드 기준)

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

    /// <summary>MaxHp 대비 현재 체력 비율에 맞춰 채움 바 폭을 갱신하고, 100% 미만일 때만 보이게 한다.</summary>
    private void UpdateHealthBar()
    {
        if (health_bar_root == null) return;

        float ratio = MaxHp > 0 ? Mathf.Clamp01((float)CurrentHp / MaxHp) : 0f;
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

        BroadcastDeathAlert();

        int? droppedItemId = DropTableManager.RollDrop(MonsterId);
        if (droppedItemId.HasValue)
        {
            // 아이템을 땅에 물리적인 상자로 생성 - 플레이어가 일정 범위로 다가가면 자동 습득(ItemPickup 참고)
            DropItemManager.SpawnDrop(droppedItemId.Value, transform.position);
        }

        GrantKillRewards();

        Destroy(gameObject);
    }

    /// <summary>
    /// 사망 시 자기 감지범위 안의 배회형/휴식형 좀비를 즉시 각성시킨다(좀비 기획서 Ver04 p.22
    /// "주변의 다른 좀비가 사망하면 배회, 휴식 상태더라도 추격 상태로 전환"). 감지범위가
    /// 무제한(monster_detect 미설정 - 보스 등 임시 데이터)이면 거리와 무관하게 전부 각성시킨다.
    /// </summary>
    private void BroadcastDeathAlert()
    {
        bool unlimited = DetectRange >= float.MaxValue;

        foreach (EnemyUnit other in Alive)
        {
            if (other == null || other == this || other.IsDead) continue;
            if (other.AiType != MonsterAiType.Resting && other.AiType != MonsterAiType.Wandering) continue;
            if (other.IsAlerted) continue;

            if (!unlimited)
            {
                float dist = Vector3.Distance(other.transform.position, transform.position);
                if (dist > DetectRange) continue;
            }

            other.IsAlerted = true;
        }
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
    private const int ExpPerMaxHp = 30;
    private const int GoldPerMaxHp = 60;

    private void GrantKillRewards()
    {
        // 예전에는 처치할 때마다 경험치와 골드가 항상 하나씩 나와서, 60초 웨이브 한 번에
        // 픽업이 수백 개씩 쌓였다. 이제는 확률 드랍이다(사용자 지정: 경험치 1/2, 골드 1/3).
        if (Random.value < ExpDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.Exp, Mathf.Max(1, MaxHp / ExpPerMaxHp), transform.position);
        }

        if (Random.value < GoldDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.Gold, Mathf.Max(1, MaxHp / GoldPerMaxHp), transform.position);
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

        if (Random.value <= modding_manager_cache.Catalog.PartBoxDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.PartBox, 1, transform.position);
        }
    }
}
