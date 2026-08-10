using System.Collections;
using UnityEngine;

/// <summary>
/// 스프린터(질주 좀비, 좀비 기획서 Ver04 p.17). 사거리 안에 들어오면 EnemyUnit의 공통
/// 공격 모션(예비 동작 - 몸 색 텔레그래프)을 거친 뒤, 그 판정 시점에 데미지를 바로 주는
/// 대신 플레이어 방향으로 짧게 돌진해 몸으로 부딪혀야 맞는다("몸을 낮춰 준비 → 돌진 박치기").
/// </summary>
public class SprinterUnit : EnemyUnit
{
    [Header("스프린터 돌진 (전부 밸런스 미확정 임시값)")]
    [Tooltip("돌진이 지속되는 시간(초)")]
    [SerializeField] private float dashDuration = 0.35f;
    [Tooltip("돌진 중 이동속도 배율(monster_speed 대비)")]
    [SerializeField] private float dashSpeedMultiplier = 4f;

    private bool is_dashing;
    private Vector3 dash_direction;
    private bool hit_this_dash;

    protected override void ExecuteAttackEffect()
    {
        if (player_transform == null) return;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;
        dash_direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.right;

        StartCoroutine(DashRoutine());
    }

    private IEnumerator DashRoutine()
    {
        is_dashing = true;
        hit_this_dash = false;

        yield return new WaitForSeconds(dashDuration);

        is_dashing = false;
    }

    protected override void FixedUpdate()
    {
        if (is_dashing)
        {
            if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon)
            {
                rb.linearVelocity = Vector3.zero;
                return;
            }

            // 짧은 돌진 중에도 현재 플레이어 방향을 계속 갱신한다.
            if (player_transform != null)
            {
                Vector3 to_player = player_transform.position - transform.position;
                to_player.z = 0f;
                if (to_player.sqrMagnitude > 0.0001f) dash_direction = to_player.normalized;
            }

            rb.linearVelocity = dash_direction * (MoveSpeed * dashSpeedMultiplier);
            return;
        }

        base.FixedUpdate();
    }

    // base(OnCollisionEnter/Stay)는 그대로 두고(공격 모션 트리거는 여전히 유효), 돌진 중
    // 접촉했을 때의 박치기 데미지만 추가로 처리한다.
    protected override void OnCollisionEnter(Collision collision)
    {
        base.OnCollisionEnter(collision);
        HandleDashContact(collision.collider);
    }

    protected override void OnCollisionStay(Collision collision)
    {
        base.OnCollisionStay(collision);
        HandleDashContact(collision.collider);
    }

    private void HandleDashContact(Collider other)
    {
        if (!is_dashing || hit_this_dash) return;

        PlayerRobotController hit_player = other.GetComponent<PlayerRobotController>();
        if (hit_player == null) hit_player = other.GetComponentInParent<PlayerRobotController>();
        if (hit_player == null) return;

        hit_player.TakeDamage(Atk);
        hit_this_dash = true;
        is_dashing = false; // 맞히면 그 자리에서 멈춘다 - 계속 밀고 나가지 않는다
    }
}
