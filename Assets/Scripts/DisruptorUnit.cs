using UnityEngine;

/// <summary>
/// 디스럭터(자폭 좀비, 좀비 기획서 Ver04 p.18) - "큰 피해를 입거나 로봇 주변에 다다르면
/// 폭발하며 주변에 피해". 두 경로 모두 같은 <see cref="Detonate"/>로 귀결된다.
///
/// "큰 피해"의 구체적 기준이 기획서에 없어 <see cref="bigHitThresholdRatio"/>(현재 최대체력
/// 대비 비율)로 임시 정의했다.
/// </summary>
public class DisruptorUnit : EnemyUnit
{
    [Header("디스럭터 자폭 (전부 밸런스 미확정 임시값)")]
    [Tooltip("한 번에 이 비율(최대체력 대비) 이상의 피해를 받으면 사거리와 무관하게 즉시 자폭한다")]
    [SerializeField] private float bigHitThresholdRatio = 0.3f;
    [Tooltip("자폭 폭발 반경(월드 유닛)")]
    [SerializeField] private float explosionRadius = 3f;

    private bool detonated;

    // "큰 피해"를 입으면 정상적인 체력 차감 대신 즉시 자폭한다 - 자폭 자체가 이 몬스터의
    // 죽음/처리 방식이라 base.TakeDamage()의 일반 사망 경로를 타지 않는다.
    public override void TakeDamage(int amount, float def_ignore_percent = 0f)
    {
        if (IsDead || detonated) return;

        bool big_hit = MaxHp > 0 && amount >= MaxHp * bigHitThresholdRatio;
        if (big_hit)
        {
            Detonate();
            return;
        }

        base.TakeDamage(amount, def_ignore_percent);
    }

    // 사거리 안(= "로봇 주변에 다다르면")에 들어와 공격 모션이 끝나면 자폭한다.
    protected override void ExecuteAttackEffect()
    {
        Detonate();
    }

    private void Detonate()
    {
        if (detonated) return;
        detonated = true;

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (Collider hit in hits)
        {
            PlayerRobotController hit_player = hit.GetComponent<PlayerRobotController>();
            if (hit_player == null) hit_player = hit.GetComponentInParent<PlayerRobotController>();
            if (hit_player != null)
            {
                hit_player.TakeDamage(Atk, transform.position);
                break; // 플레이어는 한 명뿐이라 찾으면 바로 종료
            }
        }

        Die(); // 자폭 = 사망 처리(처치 보상/드랍은 그대로 유지)
    }
}
