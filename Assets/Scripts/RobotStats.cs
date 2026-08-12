using UnityEngine;

/// <summary>최종 집계된 로봇 스탯. PlayerRobotController/PlayerShootManager는 이 결과만 읽는다.</summary>
public struct AggregatedRobotStats
{
    public int MaxHp;
    public int Atk;
    public int Def;
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
///                  + 장착 파츠 보너스(RunState.PartStatBonuses, 팔장갑/다리장갑=방어력,
///                    다리=회피, 발=이동속도).
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

        // 2026-08-12 "무기 소켓 개별화" 플랜 - 무게 지탱력(자기장 코어+다리) 초과는 더 이상
        // 장착 자체를 막지 않는 대신(ModdingManager의 하드 캡 제거), 초과분에 비례해 이동속도를
        // 깎는다. ModdingManager.Instance가 없으면(씬 배치 누락 등) 패널티 없이 통과시킨다.
        ModdingManager modding = ModdingManager.Instance;
        if (modding != null)
        {
            float overweight = Mathf.Max(0f, modding.GetTotalWeight() - modding.GetTotalWeightCapacity());
            if (overweight > 0f) result.MoveSpeed -= overweight * modding.OverweightSpeedPenaltyPerUnit;
        }

        // 디스크의 하락 스탯 때문에 값이 0 밑으로 내려가 이동 불가/즉사 같은 상태가 되지 않도록 최소값을 둔다.
        result.MaxHp = Mathf.Max(1, result.MaxHp);
        result.Atk = Mathf.Max(0, result.Atk);
        result.Def = Mathf.Max(0, result.Def);
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
            case StatType.MaxHp: stats.MaxHp += Mathf.RoundToInt(amount); break;
            case StatType.Atk: stats.Atk += Mathf.RoundToInt(amount); break;
            case StatType.Def: stats.Def += Mathf.RoundToInt(amount); break;
            case StatType.MoveSpeed: stats.MoveSpeed += amount; break;
            case StatType.Avoid: stats.Avoid += amount; break;
            case StatType.Luck: stats.Luck += amount; break;
            case StatType.CritChance: stats.Cc += amount; break;
            case StatType.CritDamage: stats.Cd += amount; break;
            case StatType.Mass: stats.Mess += amount; break;
        }
    }
}
