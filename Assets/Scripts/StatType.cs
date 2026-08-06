/// <summary>
/// AI 코어 업그레이드, 디스크(Phase 3), 파츠(Phase 4)가 공통으로 가감할 수 있는 로봇 스탯 종류.
/// RobotStats.Compute()가 이 값들을 실제 최종 스탯에 반영한다.
/// </summary>
public enum StatType
{
    MaxHp,
    Atk,
    Def,
    MoveSpeed,
    Avoid,
    Luck,
    CritChance,   // robot_cc
    CritDamage,   // robot_cd
    Mass          // robot_mess
}
