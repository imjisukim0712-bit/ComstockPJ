using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 리더(우두머리 좀비, 좀비 기획서 Ver04 p.20). 공격 방식은 기본 근접(머리 휘두르기 박치기)이라
/// EnemyUnit의 공통 공격 모션·추적을 그대로 쓰고, 주기적으로 주변 좀비를 회복시키는 지원
/// 능력만 추가로 가진다.
///
/// <b>무리(팔로워) 동반 스폰 기믹은 2026-08-21 사용자 요청으로 완전히 삭제했다.</b> 예전에는
/// 리더가 스폰될 때 <c>EnemySpawner</c>가 팔로워 좀비 3마리를 함께 스폰해 이 클래스가 그 목록만
/// 보관했지만("앞으로 좀비는 다른 좀비가 따라다니지 않음" - 사용자 지정), 이제 어떤 몬스터도
/// 다른 몬스터를 대동하고 등장하지 않는다. 대신 <b>주변 좀비를 주기적으로 회복시키는</b>
/// 지원형 능력으로 교체했다. 리더 자체의 등장 빈도는 <c>EnemySpawner</c>의 웨이브당 상한으로
/// 줄이는 대신 체력을 올려 마주쳤을 때의 위협감을 유지한다(작업.md 참고).
///
/// <b>도주 기믹은 2026-08-12 사용자 요청으로 이미 삭제했다</b>(무리 생존 여부와 무관하게
/// 끝까지 전투한다 - 이 클래스는 그 사실을 바꾸지 않는다).
/// </summary>
public class LeaderUnit : EnemyUnit
{
    [Header("주변 회복 (2026-08-21 무리 동반 기믹 대체)")]
    [Tooltip("이 간격(초)마다 주변 좀비를 회복시킨다")]
    [SerializeField] private float healInterval = 3f;

    [Tooltip("한 번에 회복시킬 최대 대상 수(가장 가까운 순으로 고른다)")]
    [SerializeField] private int healTargetCount = 3;

    [Tooltip("대상의 최대 체력 대비 회복 비율(0.2 = 20%)")]
    [SerializeField] private float healPercent = 0.2f;

    [Tooltip("이 반경(월드 유닛) 안의 좀비만 회복 대상이 된다")]
    [SerializeField] private float healRadius = 6f;

    private float heal_timer;
    private readonly List<EnemyUnit> heal_candidates_buffer = new List<EnemyUnit>();

    protected override void Update()
    {
        base.Update(); // 근접 접촉 판정(추적/공격)은 그대로 유지

        if (IsDead || GameOverManager.IsGameOver || GameWinManager.IsGameWon) return;

        heal_timer += Time.deltaTime;
        if (heal_timer < healInterval) return;

        heal_timer = 0f;
        HealNearbyZombies();
    }

    /// <summary>
    /// 반경 안에서 자신을 뺀 살아있는 좀비 중, 체력이 덜 찬 유닛만 후보로 모아 가장 가까운
    /// 순으로 최대 <see cref="healTargetCount"/>마리를 <see cref="healPercent"/>만큼 회복시킨다.
    /// </summary>
    private void HealNearbyZombies()
    {
        heal_candidates_buffer.Clear();
        float radius_sqr = healRadius * healRadius;

        foreach (EnemyUnit unit in Alive)
        {
            if (unit == null || unit == this || unit.IsDead) continue;
            if (unit.CurrentHp >= unit.MaxHp) continue; // 이미 풀피면 대상에서 제외
            if ((unit.transform.position - transform.position).sqrMagnitude > radius_sqr) continue;

            heal_candidates_buffer.Add(unit);
        }

        if (heal_candidates_buffer.Count == 0) return;

        heal_candidates_buffer.Sort((a, b) =>
        {
            float da = (a.transform.position - transform.position).sqrMagnitude;
            float db = (b.transform.position - transform.position).sqrMagnitude;
            return da.CompareTo(db);
        });

        int count = Mathf.Min(healTargetCount, heal_candidates_buffer.Count);
        for (int i = 0; i < count; i++)
        {
            EnemyUnit target = heal_candidates_buffer[i];
            target.Heal(target.MaxHp * healPercent);
        }
    }
}
