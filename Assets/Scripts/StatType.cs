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
    Mass,         // robot_mess

    // 2026-08-12 디스크 기획서(김재원) "금화의 잔향 디스크" 반영 - 로봇 스탯이 아니라
    // 골드 획득량에 곱해지는 비율(%)이라 RobotStats.ApplyBonus에는 연결하지 않는다
    // (해당 switch에 case가 없어도 안전하게 무시된다). RewardPickup.CollectImmediately()가
    // RunState.DiscStatBonuses에서 직접 읽어 골드 수령량에 곱한다.
    GoldGain
}
