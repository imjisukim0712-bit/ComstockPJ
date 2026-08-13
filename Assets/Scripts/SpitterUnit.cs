using System.Collections;
using UnityEngine;

/// <summary>
/// 스피터(원거리 좀비, 좀비 기획서 Ver04 p.16). 근접 접촉 대신 사거리 안에서 플레이어를 향해
/// 직선 투사체를 발사한다. 공격 모션(예비 동작→판정) 구조는 EnemyUnit과 동일하게 재사용하고,
/// 판정 시점에 직접 데미지를 주는 대신 <see cref="EnemyProjectile"/>을 발사하도록
/// <see cref="ExecuteAttackEffect"/>만 교체한다.
/// </summary>
public class SpitterUnit : EnemyUnit
{
    [Header("스피터 투사체 (전부 밸런스 미확정 임시값)")]
    // 2026-08-12 "투사체 속도가 너무 빠르다" 리포트로 25% 감속: 12 → 9 → 6.75(재차 25% 감속),
    // 2026-08-13 사용자 요청으로 또 25% 감속: 6.75 → 5.0625
    [SerializeField] private float projectileSpeed = 5.0625f;
    // 2026-08-10 "인게임 모든 이미지 1/2" 지시로 0.35 → 0.175 됐던 것이, 2026-08-12
    // "육안으로 확인이 어렵다" 리포트로 원래 크기(0.35)로 되돌아갔고, 이후 사용자 지시로
    // 3배(1.05)로 재상향.
    [SerializeField] private float projectileVisualSize = 1.05f;

    // 원거리 몬스터는 앞으로 내지를 이유가 없다. 예비 동작(뒤로 젖히며 뱉을 준비)만 남기고
    // 타격 돌진은 0으로 죽인다(EnemyUnit의 공격 모션 뼈대는 그대로 재사용).
    private void Reset() => ConfigureRangedMotion();
    private void OnValidate() => ConfigureRangedMotion();

    private void ConfigureRangedMotion()
    {
        strikeLungeRatio = 0f;
    }

    protected override void Awake()
    {
        base.Awake();
        ConfigureRangedMotion(); // 런타임에 AddComponent로 붙는 경로에서도 확실히 적용
    }

    protected override void ExecuteAttackEffect()
    {
        if (player_transform == null) return;

        Vector3 dir = player_transform.position - transform.position;
        dir.z = 0f;

        EnemyProjectile.Spawn(transform.position, dir, projectileSpeed, Atk, AttackRange + 2f, projectileVisualSize);
    }
}
