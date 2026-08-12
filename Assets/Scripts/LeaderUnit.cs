using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 리더(우두머리 좀비, 좀비 기획서 Ver04 p.20). 공격 방식은 기본 근접(머리 휘두르기 박치기)이라
/// EnemyUnit의 공통 공격 모션·추적을 그대로 쓰고, 무리(팔로워) 등록만 추가로 보관한다.
///
/// - "리더 좀비를 따르는 무리는 리더가 사망하면 무리 상태가 해제된다" - 무리는 리더→팔로워
///   단방향 참조뿐이고 팔로워 쪽에 별도 상태/보너스가 없어(기획서에 그런 효과가 명시돼 있지
///   않음), 리더가 파괴되면 이 참조 자체가 사라지므로 별도 처리가 필요 없다.
/// - <b>도주 기믹은 2026-08-12 사용자 요청으로 삭제했다.</b> 예전에는 "리더의 모든 무리가
///   사망하면 리더 좀비는 도주한다"(기획서 p.22)를 구현해 무리가 전멸하면 플레이어 반대
///   방향으로 달아나고 공격도 멈췄지만, 이제는 무리 생존 여부와 무관하게 끝까지 전투한다.
///   그래서 <see cref="pack"/>은 더 이상 행동을 바꾸지 않고 스포너가 넘겨준 무리 구성을
///   보관만 한다(스폰 자체는 <c>EnemySpawner.SpawnLeaderPack</c>이 그대로 담당한다).
/// </summary>
public class LeaderUnit : EnemyUnit
{
    private readonly List<EnemyUnit> pack = new List<EnemyUnit>();

    /// <summary>스포너가 리더를 스폰한 직후 함께 스폰한 팔로워들을 등록한다.</summary>
    public void SetPack(List<EnemyUnit> members)
    {
        pack.Clear();
        pack.AddRange(members);
    }
}
