using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 리더(우두머리 좀비, 좀비 기획서 Ver04 p.20). 공격 방식은 기본 근접(머리 휘두르기 박치기)이라
/// EnemyUnit의 공통 공격 모션을 그대로 쓰고, 무리(팔로워) 관리만 추가한다(p.22 예외처리).
///
/// - "리더 좀비를 따르는 무리는 리더가 사망하면 무리 상태가 해제된다" - 무리는 리더→팔로워
///   단방향 참조뿐이고 팔로워 쪽에 별도 상태/보너스가 없어(기획서에 그런 효과가 명시돼 있지
///   않음), 리더가 파괴되면 이 참조 자체가 사라지므로 별도 처리가 필요 없다.
/// - "리더의 모든 무리가 사망하면 리더 좀비는 도주한다" - 이건 능동적 상태 전환이 필요해
///   <see cref="Update"/>에서 매 프레임 무리 생존 여부를 확인해 도주 상태로 전환한다.
/// </summary>
public class LeaderUnit : EnemyUnit
{
    private readonly List<EnemyUnit> pack = new List<EnemyUnit>();
    private bool is_fleeing;

    /// <summary>스포너가 리더를 스폰한 직후 함께 스폰한 팔로워들을 등록한다.</summary>
    public void SetPack(List<EnemyUnit> members)
    {
        pack.Clear();
        pack.AddRange(members);
    }

    protected override void Update()
    {
        if (!is_fleeing && pack.Count > 0 && IsPackWiped()) is_fleeing = true;

        if (is_fleeing) return; // 도주 중에는 감지/공격 판정을 하지 않는다(더 이상 싸우지 않음)

        base.Update();
    }

    // 도주 중에는 플레이어 반대 방향으로 이동한다. 도주가 아닐 때는 기존 AI 성격값 로직을 그대로 쓴다.
    protected override Vector3 ComputeSeekDirection()
    {
        if (!is_fleeing) return base.ComputeSeekDirection();
        if (player_transform == null) return Vector3.zero;

        Vector3 away = transform.position - player_transform.position;
        away.z = 0f;
        return away.sqrMagnitude > 0.0001f ? away : Vector3.up;
    }

    private bool IsPackWiped()
    {
        foreach (EnemyUnit member in pack)
        {
            if (member != null && !member.IsDead) return false;
        }
        return true;
    }
}
