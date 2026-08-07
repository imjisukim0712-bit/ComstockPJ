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

    public int MonsterId { get; private set; }
    public int MaxHp { get; private set; }
    public int CurrentHp { get; private set; }
    public int Atk { get; private set; }
    public int Def { get; private set; }
    public float MoveSpeed { get; private set; }
    public float AttackRange { get; private set; } // monster_range: 공격 사거리
    public float AtSp { get; private set; }         // monster_atsp: 공격속도 - 공격 쿨다운에 사용
    public bool IsDead { get; private set; }

    // 다음 공격이 가능한 시각 (monster_atsp 기반 쿨다운)
    private float next_attack_time = 0f;

    private Rigidbody rb;
    private Transform player_transform;
    private PlayerRobotController player;

    // 부품 상자 드랍 확률 조회용. 몬스터마다 새로 찾지 않도록 클래스 전체가 공유하는 캐시.
    // PlayerRobotController.Awake()가 Alive.Clear()와 함께 매 런 시작마다 비워준다(재시도 시 이전 판의 참조가 남지 않도록).
    private static ModdingManager modding_manager_cache;

    public static void ResetStaticCaches() => modding_manager_cache = null;

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

        Alive.Add(this);
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

        if (to_player.magnitude <= AttackRange + BodyContactRadius())
        {
            TryAttack();
        }
    }

    // 콜라이더끼리 물리적으로 맞닿았을 때도(= "들이박았을 때") 확실히 공격이 들어가도록
    // 물리 충돌 이벤트를 보조 트리거로 함께 사용한다.
    private void OnCollisionEnter(Collision collision) => TryAttackFromCollision(collision.collider);
    private void OnCollisionStay(Collision collision) => TryAttackFromCollision(collision.collider);

    private void TryAttackFromCollision(Collider other)
    {
        if (other.GetComponent<PlayerRobotController>() == null && other.GetComponentInParent<PlayerRobotController>() == null) return;
        TryAttack();
    }

    // 자신과 플레이어 콜라이더의 대략적인 반지름 합. 두 몸이 물리적으로 맞닿는 최소 중심간 거리를
    // 근사해서, 몸집 때문에 실제로는 닿았는데 monster_range보다 멀어서 공격 판정이 안 나는 문제를 막는다.
    private float BodyContactRadius()
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

    // monster_atsp(공격속도) 쿨다운에 맞춰 실제 데미지를 적용한다.
    private void TryAttack()
    {
        if (player == null || Time.time < next_attack_time) return;

        player.TakeDamage(Atk);

        float cooldown = AtSp > 0f ? 1f / AtSp : 1f; // 공격속도가 높을수록 다음 공격까지 대기시간이 짧아짐
        next_attack_time = Time.time + cooldown;
    }

    // 다른 몬스터를 밀어내는 반경/세기. 매 프레임 velocity를 "플레이어 방향"으로만 강제로
    // 덮어쓰면 물리 충돌로 겹침이 풀려도 바로 다음 프레임에 다시 겹치게 되어(=서로 엉겨붙어 부들거림)
    // 이동 방향 자체에 "주변 몬스터로부터 밀려나는 힘"을 함께 섞어서 자연스럽게 퍼지게 한다.
    private const float SeparationRadius = 1.3f;
    private const float SeparationWeight = 1.4f;

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

        Vector3 to_player = player_transform.position - transform.position;
        to_player.z = 0f; // X-Y 평면만 사용

        Vector3 seek = to_player.sqrMagnitude > 0.0001f ? to_player.normalized : Vector3.zero;
        Vector3 separation = ComputeSeparation();

        Vector3 move_dir = seek + separation;
        rb.linearVelocity = move_dir.sqrMagnitude > 0.0001f ? move_dir.normalized * MoveSpeed : Vector3.zero;
    }

    // 반경 안의 다른 EnemyUnit들로부터 밀려나는 방향(가까울수록 세게)을 합산한다.
    private Vector3 ComputeSeparation()
    {
        Vector3 push = Vector3.zero;

        Collider[] nearby = Physics.OverlapSphere(transform.position, SeparationRadius);
        foreach (Collider col in nearby)
        {
            EnemyUnit other = col.GetComponent<EnemyUnit>();
            if (other == null) other = col.GetComponentInParent<EnemyUnit>();
            if (other == null || other == this) continue;

            Vector3 diff = transform.position - other.transform.position;
            diff.z = 0f;

            float dist = diff.magnitude;
            if (dist > 0.0001f) push += diff.normalized / dist; // 가까울수록 더 세게 밀어냄
        }

        return push * SeparationWeight;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;

        int dmg = Mathf.Max(1, amount - Def);
        CurrentHp -= dmg;
        if (CurrentHp <= 0) Die();
    }

    protected virtual void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (rb != null) rb.linearVelocity = Vector3.zero;

        int? droppedItemId = DropTableManager.RollDrop(MonsterId);
        if (droppedItemId.HasValue)
        {
            // 아이템을 땅에 물리적인 상자로 생성 - 플레이어가 일정 범위로 다가가면 자동 습득(ItemPickup 참고)
            DropItemManager.SpawnDrop(droppedItemId.Value, transform.position);
        }

        GrantKillRewards();

        Destroy(gameObject);
    }

    // AI 코어 경험치 + 골드를 필드 위의 픽업 오브젝트로 생성한다(플레이어가 다가가면 자동 흡수 -
    // RewardPickup 참고). 데이터테이블에 아직 몬스터별 경험치/골드 컬럼이 없어서(기획 확정 전)
    // MaxHp 비례 임시 공식을 사용한다. 실제 값 컬럼이 시트에 추가되면 이 공식을 그 값으로 교체한다.
    private void GrantKillRewards()
    {
        // 예전에는 처치할 때마다 경험치와 골드가 항상 하나씩 나와서, 60초 웨이브 한 번에
        // 픽업이 수백 개씩 쌓였다. 이제는 확률 드랍이다(사용자 지정: 경험치 1/2, 골드 1/3).
        if (Random.value < ExpDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.Exp, Mathf.Max(1, MaxHp / 10), transform.position);
        }

        if (Random.value < GoldDropChance)
        {
            RewardPickupManager.SpawnReward(RewardType.Gold, Mathf.Max(1, MaxHp / 20), transform.position);
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
