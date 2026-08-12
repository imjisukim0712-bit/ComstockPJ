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
    [Tooltip("돌진이 지속되는 시간(초). 아래 '지나치는 거리'에 먼저 도달하면 그 전에 끝난다")]
    [SerializeField] private float dashDuration = 0.35f;
    [Tooltip("돌진 중 이동속도 배율(monster_speed 대비)")]
    [SerializeField] private float dashSpeedMultiplier = 4f;
    [Tooltip("돌진 시작 시점의 플레이어 위치를 이만큼 지나치면 돌진을 멈춘다(유닛). " +
             "방향을 고정했기 때문에 플레이어가 피하면 계속 직진하는데, 이 상한이 없으면 화면 밖까지 밀고 나간다")]
    [SerializeField] private float dashOvershoot = 2f;

    private bool is_dashing;
    private Vector3 dash_direction;
    private bool hit_this_dash;
    private Vector3 dash_start_position;
    private float dash_max_distance;

    protected override void ExecuteAttackEffect()
    {
        if (player_transform == null) return;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;
        dash_direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.right;

        // 돌진 방향은 여기서 한 번만 정하고 돌진이 끝날 때까지 고정한다(직선 돌진).
        dash_start_position = transform.position;
        dash_max_distance = dir.magnitude + Mathf.Max(0f, dashOvershoot);

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

            // 돌진은 시작 시점에 정한 방향 그대로 직선으로만 나간다. 예전에는 매 물리 프레임
            // 플레이어 방향으로 방향을 다시 잡아서 유도탄처럼 휘어 보였다(2026-08-12 수정).
            if (Vector3.Distance(dash_start_position, transform.position) >= dash_max_distance)
            {
                is_dashing = false;
                rb.linearVelocity = Vector3.zero;
                return;
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
