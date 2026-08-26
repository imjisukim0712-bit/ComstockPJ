using UnityEngine;

/// <summary>최종 집계된 로봇 스탯. PlayerRobotController/PlayerShootManager는 이 결과만 읽는다.</summary>
public struct AggregatedRobotStats
{
    // 2026-08-20 파츠 명세에 공격력 +0.3 / 방어력 +2.5 같은 소수 수치가 대량으로 들어오면서
    // int였던 세 스탯을 float로 바꿨다. 예전에는 ApplyBonus가 보너스를 개별 반올림해서
    // +0.3이 그대로 0으로 사라졌다(사용자 지적: "소수점 아래 숫자 있으면 그것도 표시").
    public float MaxHp;
    public float Atk;
    public float Def;
    public float MoveSpeed;
    public float Avoid;
    public float Luck;
    public float Cc; // 치명타 확률(0~100)
    public float Cd; // 치명타 데미지 배율
    public float Mess; // 질량
}

/// <summary>
/// 로봇의 최종 스탯 = 머리(로봇) 기본값(RobotData)
///                  + AI 코어 업그레이드 누적(RunState.CoreStatBonuses)
///                  + 장착 디스크 증감(RunState.DiscStatBonuses, 하락분은 음수로 들어있다)
///                  + 장착 파츠 보너스(RunState.PartStatBonuses, 헬멧/다리장갑=방어력,
///                    다리=회피+이동속도). 2026-08-26 팔장갑, 2026-08-18 발이 삭제되어
///                    그 두 슬롯은 더 이상 보너스를 만들지 않는다(PartSlot.cs 참고).
/// </summary>
public static class RobotStats
{
    public static AggregatedRobotStats Compute(RobotData baseData)
    {
        var result = new AggregatedRobotStats
        {
            MaxHp = baseData.robot_hp,
            Atk = baseData.robot_atk,
            Def = baseData.robot_def,
            MoveSpeed = baseData.robot_speed,
            Avoid = baseData.robot_avoid,
            Luck = baseData.robot_luck,
            Cc = baseData.robot_cc,
            Cd = baseData.robot_cd,
            Mess = baseData.robot_mess
        };

        foreach (var bonus in RunState.CoreStatBonuses)
        {
            ApplyBonus(ref result, bonus.Key, bonus.Value);
        }

        foreach (var bonus in RunState.DiscStatBonuses)
        {
            ApplyBonus(ref result, bonus.Key, bonus.Value);
        }

        foreach (var bonus in RunState.PartStatBonuses)
        {
            ApplyBonus(ref result, bonus.Key, bonus.Value);
        }

        // 2026-08-19 머리 기획서 Ver04 - 머리(로봇) 고유 효과의 스탯 증감.
        // 위 세 보너스를 <b>다 더한 뒤</b>에 오는 것이 중요하다 - 해피 픽셀은 최종 행운을,
        // 핫팟은 보유 디스크 수를 읽어야 하므로 다른 보너스로 늘어난 값까지 반영되어야 한다.
        // 무게 패널티·하한 클램프보다는 앞이라 결과가 음수로 튀어도 아래에서 정리된다.
        HeadEffects.ApplyStatBonuses(ref result);

        // 2026-08-20 파츠 특수효과(연극 마스크의 행운 파생 방어력, 방탄모의 방어력 %증가 등).
        // HeadEffects <b>다음</b>이어야 한다 - 파생 효과는 최종 행운·공격력을 읽어야 하고,
        // 방어력 %증가는 가산·파생이 모두 끝난 값에 곱해져야 한다.
        PartEffects.ApplyStatBonuses(ref result);

        // 2026-08-12 "무기 소켓 개별화" 플랜 - 무게 지탱력(다리) 초과는 더 이상
        // 장착 자체를 막지 않는 대신(ModdingManager의 하드 캡 제거), 초과분에 비례해 이동속도를
        // 깎는다. ModdingManager.Instance가 없으면(씬 배치 누락 등) 패널티 없이 통과시킨다.
        ModdingManager modding = ModdingManager.Instance;
        if (modding != null)
        {
            float overweight = Mathf.Max(0f, modding.GetTotalWeight() - modding.GetTotalWeightCapacity());
            if (overweight > 0f) result.MoveSpeed -= overweight * modding.OverweightSpeedPenaltyPerUnit;
        }

        // 디스크의 하락 스탯 때문에 값이 0 밑으로 내려가 이동 불가/즉사 같은 상태가 되지 않도록 최소값을 둔다.
        result.MaxHp = Mathf.Max(1f, result.MaxHp);
        result.Atk = Mathf.Max(0f, result.Atk);
        result.Def = Mathf.Max(0f, result.Def);
        result.MoveSpeed = Mathf.Max(0.1f, result.MoveSpeed);
        result.Avoid = Mathf.Max(0f, result.Avoid);
        result.Luck = Mathf.Max(0f, result.Luck);
        result.Cc = Mathf.Max(0f, result.Cc);
        result.Cd = Mathf.Max(0f, result.Cd);
        result.Mess = Mathf.Max(0.1f, result.Mess);

        return result;
    }

    private static void ApplyBonus(ref AggregatedRobotStats stats, StatType type, float amount)
    {
        switch (type)
        {
            // 2026-08-20: RoundToInt를 없앴다 - 보너스를 개별 반올림하면 +0.3 같은 소수가
            // 통째로 사라지고, 여러 개를 더해도 절대 소수가 되지 않는다.
            case StatType.MaxHp: stats.MaxHp += amount; break;
            case StatType.Atk: stats.Atk += amount; break;
            case StatType.Def: stats.Def += amount; break;
            case StatType.MoveSpeed: stats.MoveSpeed += amount; break;
            case StatType.Avoid: stats.Avoid += amount; break;
            case StatType.Luck: stats.Luck += amount; break;
            case StatType.CritChance: stats.Cc += amount; break;
            case StatType.CritDamage: stats.Cd += amount; break;
            case StatType.Mass: stats.Mess += amount; break;
        }
    }
}
