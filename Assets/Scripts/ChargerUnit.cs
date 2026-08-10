using System.Collections;
using UnityEngine;

/// <summary>
/// 차저(돌진 좀비, 좀비 기획서 Ver04 p.19) - "사거리 안의 적을 향해 돌진하며 공격",
/// "3초간 돌진하여 몸통 박치기 공격"(3초는 기획서에 명시된 값, 나머지는 임시값).
///
/// 스프린터와의 차이: 한 번 맞혀도 돌진이 끝날 때까지 멈추지 않고 계속 밀고 나가며,
/// 돌진 중 공격속도(monster_atsp) 쿨다운 간격으로 여러 번 재히트할 수 있다("계속 부딪히며
/// 미는" 돌진형 몸통박치기).
/// </summary>
public class ChargerUnit : EnemyUnit
{
    [Header("차저 돌진 (지속시간 3초는 기획서 지정값, 나머지는 밸런스 미확정 임시값)")]
    [SerializeField] private float chargeDuration = 3f;
    [SerializeField] private float chargeSpeedMultiplier = 2.5f;

    private bool is_charging;
    private Vector3 charge_direction;
    private float next_ram_time;

    protected override void ExecuteAttackEffect()
    {
        if (player_transform == null) return;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;
        charge_direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.right;

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

            // 돌진 시작 방향을 고정하지 않고 매 물리 프레임 현재 플레이어 방향으로 갱신한다.
            // 모든 좀비가 항상 플레이어를 향해야 한다는 공통 규칙을 돌진 중에도 유지한다.
            if (player_transform != null)
            {
                Vector3 to_player = player_transform.position - transform.position;
                to_player.z = 0f;
                if (to_player.sqrMagnitude > 0.0001f) charge_direction = to_player.normalized;
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

        hit_player.TakeDamage(Atk);

        float cooldown = AtSp > 0f ? 1f / AtSp : 1f;
        next_ram_time = Time.time + cooldown;
    }
}
