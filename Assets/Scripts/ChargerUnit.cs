using System.Collections;
using UnityEngine;

/// <summary>
/// 차저(돌진 좀비, 좀비 기획서 Ver04 p.19) - "사거리 안의 적을 향해 돌진하며 공격",
/// "3초간 돌진하여 몸통 박치기 공격"(3초는 기획서에 명시된 값, 나머지는 임시값).
///
/// 스프린터와의 차이: 한 번 맞혀도 돌진이 끝날 때까지 멈추지 않고 계속 밀고 나가며,
/// 돌진 중 공격속도(monster_atsp) 쿨다운 간격으로 여러 번 재히트할 수 있다("계속 부딪히며
/// 미는" 돌진형 몸통박치기).
///
/// <b>2026-08-23 사용자 제공 전용 애니메이션 3종 적용</b>(그 전까지는 이동/돌진 내내 프리팹의
/// 정지 스프라이트 Charger.png가 고정돼 있었다 - 사용자 제공 애니메이션이 없어 일부러 비워
/// 뒀던 것). 걷기(Resources/ChargerMove)는 <see cref="MonsterAnimationLibrary"/>에 몬스터ID
/// 200002로 등록해 기존 파이프라인을 그대로 탄다. 돌진(Resources/ChargerCharge)은
/// <see cref="ResolveMoveClip"/>을 오버라이드해 <see cref="is_charging"/>인 동안만 다른 폴더로
/// 바꿔치기한다 - <see cref="EnemyUnit.UpdateWalkAnimation"/>은 IsAttacking이 꺼진 뒤(즉
/// 예비 동작이 끝나고 실제 돌진이 시작된 뒤)에만 프레임을 그리므로 별도 코루틴 없이 저절로
/// 맞아떨어진다. 돌진 예비 동작(Resources/ChargerChargePrep)은 그 반대 구간(IsAttacking이
/// 켜져 있는 동안)이라 MoveClip 파이프라인을 못 타서, <see cref="TryAttack"/>을 오버라이드해
/// 예비 동작과 병렬로 도는 별도 코루틴(<see cref="PlayChargePrepFrames"/>)으로 직접 재생한다
/// (베이스는 이 구간에서 몸 색만 주황으로 물들이므로 두 연출이 함께 나온다). 같은 시점에
/// 사용자 제공 "몬스터 돌진" 경고 이펙트(<see cref="ChargeWarningEffect"/>)도 돌진 방향으로
/// 한 번 재생한다.
///
/// <b>2026-08-24 크기 정합</b> - 세 세트가 캔버스 안에 서로 다른 크기로 그려져 있어서 돌진
/// 준비에 들어가는 순간 차저가 눈에 띄게 쪼그라들었다. 걷기(ChargerMove, 640px@PPU100,
/// 몸높이 약 5.8유닛)를 기준으로 실루엣 겹침(IoU 0.98)을 실측해 <b>돌진준비 세트의 PPU를
/// 100→73</b>으로 낮추고 pivot을 몸 중심에 맞췄다(돌진 세트는 오차 4%라 그대로 뒀다).
/// 프리팹의 정지 스프라이트도 걷기 세트보다 62% 작은 옛 <c>Charger.png</c>였는데
/// (<see cref="EnemyUnit"/>.RestoreAttackVisual이 공격 직후 이 스프라이트로 되돌리므로
/// 한 프레임 반짝 작아졌고, 체력바 폭도 이 캔버스 기준으로 잡혀 좁았다) 다른 몬스터들과
/// 같은 관례로 <c>ChargerMove/f01</c>로 교체했다.
/// </summary>
public class ChargerUnit : EnemyUnit
{
    private const string ChargePrepFolder = "ChargerChargePrep";
    private static Sprite[] cached_prep_frames;

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetStaticCaches() => cached_prep_frames = null;

    [Header("차저 돌진 (지속시간 3초는 기획서 지정값, 나머지는 밸런스 미확정 임시값)")]
    [SerializeField] private float chargeDuration = 3f;
    [SerializeField] private float chargeSpeedMultiplier = 2.5f;
    [Tooltip("돌진 시작 시점의 플레이어 위치를 이만큼 지나치면 돌진을 멈춘다(유닛). " +
             "방향을 고정했기 때문에 플레이어가 피하면 계속 직진하는데, 이 상한이 없으면 " +
             "3초 x 속도로 화면 밖(20유닛 이상)까지 밀고 나간다")]
    [SerializeField] private float chargeOvershoot = 2f;

    [Header("차저 전용 연출 (2026-08-23, 전부 크기 미확정 임시값)")]
    [Tooltip("돌진 경고 이펙트의 폭(월드 유닛)")]
    [SerializeField] private float chargeWarningWidth = 2.5f;

    private bool is_charging;
    private Vector3 charge_direction;
    private float next_ram_time;
    private Vector3 charge_start_position;
    private float charge_max_distance;

    /// <summary>
    /// 돌진 중(<see cref="is_charging"/>)에는 걷기 대신 돌진 프레임 세트를 재생한다.
    /// <see cref="EnemyUnit.UpdateWalkAnimation"/>은 IsAttacking이 꺼진 뒤에만 이 값을 읽으므로,
    /// 예비 동작(IsAttacking=true) 중에는 호출되지 않고 실제 돌진이 시작된 뒤에만 적용된다.
    /// </summary>
    protected override MonsterAnimationLibrary.Clip ResolveMoveClip()
    {
        if (is_charging) return MonsterAnimationLibrary.GetByFolder("ChargerCharge", 0, 16f, true);
        return base.ResolveMoveClip();
    }

    /// <summary>
    /// 베이스(EnemyUnit.TryAttack)와 같은 가드 조건이지만, 예비 동작과 병렬로 도는 돌진 준비
    /// 프레임 애니메이션과 돌진 경고 이펙트를 함께 시작한다(위 클래스 주석 참고).
    /// </summary>
    protected override void TryAttack()
    {
        if (player == null || IsAttacking || Time.time < next_attack_time) return;

        Vector3 dir = player_transform != null ? player_transform.position - transform.position : transform.right;
        dir.z = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.right;
        dir.Normalize();

        StartCoroutine(PlayChargePrepFrames(dir, attackWindupSeconds));
        ChargeWarningEffect.Play(transform.position, dir, chargeWarningWidth,
            BodySpriteRenderer != null ? BodySpriteRenderer.sortingOrder + 1 : 1);

        StartCoroutine(PerformAttackMotion());
    }

    /// <summary>
    /// 예비 동작(attackWindupSeconds) 동안 "차저 돌진준비" 프레임을 한 번 재생한다. 베이스는 이
    /// 구간에서 몸 색만 주황으로 물들이므로(RestoreAttackVisual이 나중에 색과 스프라이트를 함께
    /// 되돌린다) 두 연출이 겹쳐서 나온다.
    /// </summary>
    private IEnumerator PlayChargePrepFrames(Vector3 direction, float duration)
    {
        if (cached_prep_frames == null)
        {
            Sprite[] loaded = Resources.LoadAll<Sprite>(ChargePrepFolder);
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
            cached_prep_frames = loaded;
        }

        if (cached_prep_frames.Length == 0 || BodySpriteRenderer == null) yield break;

        BodySpriteRenderer.flipX = direction.x > 0f;

        float fps = cached_prep_frames.Length / Mathf.Max(0.01f, duration); // 예비 동작 시간 안에 전체 프레임을 한 번 재생
        float phase = 0f;
        while (phase < cached_prep_frames.Length)
        {
            if (BodySpriteRenderer == null) yield break;
            int index = Mathf.Min(cached_prep_frames.Length - 1, Mathf.FloorToInt(phase));
            BodySpriteRenderer.sprite = cached_prep_frames[index];
            yield return null;
            phase += Time.deltaTime * fps;
        }
    }

    protected override void ExecuteAttackEffect()
    {
        if (player_transform == null) return;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;
        charge_direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.right;

        // 돌진 방향은 여기서 한 번만 정하고 돌진이 끝날 때까지 고정한다(직선 돌진).
        charge_start_position = transform.position;
        charge_max_distance = dir.magnitude + Mathf.Max(0f, chargeOvershoot);

        StartCoroutine(ChargeRoutine());
    }

    private IEnumerator ChargeRoutine()
    {
        is_charging = true;
        next_ram_time = 0f;

        yield return new WaitForSeconds(chargeDuration);

        is_charging = false;
    }

    protected override void FixedUpdate()
    {
        if (is_charging)
        {
            if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon)
            {
                rb.linearVelocity = Vector3.zero;
                return;
            }

            // 돌진은 시작 시점에 정한 방향 그대로 직선으로만 나간다. 예전에는 매 물리 프레임
            // 플레이어 방향으로 방향을 다시 잡아서(공통 추적 규칙을 돌진 중에도 유지) 유도탄처럼
            // 휘어 보였다(2026-08-12 수정 - 돌진은 추적이 아니라 한 방향으로 내지르는 공격이다).
            if (Vector3.Distance(charge_start_position, transform.position) >= charge_max_distance)
            {
                is_charging = false;
                rb.linearVelocity = Vector3.zero;
                return;
            }

            rb.linearVelocity = charge_direction * (MoveSpeed * chargeSpeedMultiplier);
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

    private void HandleRamContact(Collider other)
    {
        if (!is_charging || Time.time < next_ram_time) return;

        PlayerRobotController hit_player = other.GetComponent<PlayerRobotController>();
        if (hit_player == null) hit_player = other.GetComponentInParent<PlayerRobotController>();
        if (hit_player == null) return;

        MeleeAttackPlayer(hit_player); // 돌진도 접촉 근접이라 가시 플레이트 반사 대상이다

        float cooldown = AtSp > 0f ? 1f / AtSp : 1f;
        next_ram_time = Time.time + cooldown;
    }
}
