using System;
using UnityEngine;

/// <summary>
/// 파츠 하나의 정의. 슬롯마다 의미 있는 필드가 달라서(예: 무기소켓은 허용 무기 카테고리,
/// 장갑은 방어력) 여러 구조체로 쪼개지 않고 하나로 통일했다 - DiscData와 같은 결정.
/// 무기·디스크와 달리 상점에서 사는 게 아니라 부품 상자에서 무작위로 나오므로 가격 필드는 없다.
///
/// 파츠는 <b>종류마다 5개 등급이 전부 존재하는 형식</b>이다(예: "연극 마스크" = 일반/희귀/서사/유일/전설).
/// 새 종류를 추가할 때는 5개 등급을 모두 채울 것.
///
/// 2026-08-20 장갑 명세(김지수) + 소켓/메모리/디스크슬롯 명세 반영으로 <b>효과가 2개 이상인 파츠</b>가
/// 생겨서 가산 스탯을 2쌍으로 늘리고(<see cref="bonusStat2"/>), 합산으로 표현할 수 없는 것은
/// <see cref="PartEffect"/>로 뺐다(DiscData가 statA~C + effectType으로 푸는 방식과 같다).
/// </summary>
[Serializable]
public struct PartData
{
    [Tooltip("파츠 고유 ID. RunState.EquippedPartIds에 이 값이 저장된다")]
    public int partId;

    public string partName;
    public PartSlot slot;
    public ItemGrade grade;

    [Tooltip("체크하면 새 런 시작 시 이 슬롯에 자동으로 장착된다. 슬롯당 정확히 1개만 체크할 것")]
    public bool isDefaultStarter;

    [Tooltip("Assets/Resources/PartIcons/ 아래의 스프라이트 파일명(확장자 제외). 비어 있으면 " +
             "슬롯 공용 아이콘 → 코드 생성 실루엣 순으로 폴백한다(PartIconLibrary 참고)")]
    public string iconName;

    [Header("무기 소켓(ArmWeaponSocket) 전용 - 종류는 '장착 가능한 무기 카테고리'를 결정한다")]
    [Tooltip("체크 해제하면 카테고리 제한이 없다(범용 소켓). 체크하면 allowedWeaponType과 다른 " +
             "카테고리의 무기를 끼울 때 무게 배율 패널티가 붙는다(장착 자체는 막지 않는다)")]
    public bool restrictsWeaponType;

    [Tooltip("허용하는 무기 카테고리(연사/산탄/정밀/폭발/에너지/근접). restrictsWeaponType이 켜져 있을 때만 의미가 있다")]
    public WeaponType allowedWeaponType;

    [Header("무기 소켓 등급 효과 (2026-08-20 명세) - 이 소켓에 낀 무기에만 적용된다")]
    [Tooltip("공격 속도 증가율(%). 대기시간이 이 비율만큼 짧아진다")]
    public float socketAttackSpeedPercent;

    [Tooltip("공격력 증가량(절대값). 무기 1발 데미지 계산에 그대로 더해진다")]
    public float socketDamageFlat;

    [Tooltip("공격력 증가율(%). 근접 소켓처럼 절대값 대신 비율로 주는 소켓용")]
    public float socketDamagePercent;

    [Tooltip("치명타 확률 증가량(%p)")]
    public float socketCritChancePercent;

    [Tooltip("스플래시(폭발) 반경 증가율(%)")]
    public float socketSplashPercent;

    [Tooltip("방어력 무시 증가량(%p)")]
    public float socketDefIgnorePercent;

    [Header("스탯 보너스 (가산. 효과가 2개인 파츠는 두 쌍을 모두 쓴다)")]
    public StatType bonusStat;
    public float bonusAmount;
    public StatType bonusStat2;
    public float bonusAmount2;

    [Header("특수효과 (합산으로 표현할 수 없는 것 - 계산은 PartEffects에 있다)")]
    public PartEffect effect;

    [Tooltip("effect가 PerDiscStat일 때 어떤 스탯을 올릴지. 그 외 effect에서는 쓰이지 않는다")]
    public StatType effectStat;

    [Tooltip("effect의 수치. %인지 절대값인지는 PartEffect의 각 항목 설명 참고")]
    public float effectAmount;

    [Header("무게")]
    [Tooltip("이 파츠 자체의 무게. 장착된 무기 무게와 함께 합산되어 " +
             "다리의 weightCapacity와 비교된다. 디스크에는 무게가 없다")]
    public float weight;

    [Header("무게 지탱 (다리 전용)")]
    [Tooltip("장착된 모든 무기 + 파츠의 무게 합이 이 값을 넘으면 초과분만큼 이동속도가 깎인다")]
    public float weightCapacity;

    [Header("디스크 슬롯 (DiscSlot 슬롯 전용)")]
    [Tooltip("장착 가능한 최대 디스크 개수. 이 파츠를 끼우면 로봇(머리) 기본값 대신 이 값이 쓰인다")]
    public int discSlotCount;

    [Header("다리(Leg) 전용 액티브/패시브 스킬 (2026-08-18 다리 기획서 Ver02)")]
    [Tooltip("Space를 눌렀을 때의 동작. None이면 액티브 스킬이 없다(로켓 추진기)")]
    public LegSkillType legSkillType;

    [Tooltip("legSkillType이 None이 아닐 때의 재사용 대기시간(초)")]
    public float legSkillCooldown;

    [Tooltip("체크하면 최대 체력의 25%를 잃을 때마다 이동속도가 5%씩 하락한다(거미 다리 전용, 누적)")]
    public bool legHpLossSpeedPenalty;

    [Tooltip("체크하면 같은 방향으로 2초 이상 이동할 때 이동속도가 점진적으로 최대 +2까지 가속한다" +
             "(방향을 바꾸면 즉시 리셋). 로켓 추진기 전용")]
    public bool legSpeedRampPassive;

    [Tooltip("질량이 이 %만큼 변한다(거미 다리: -50). effect 필드는 이동속도 %효과가 이미 쓰고 " +
             "있어서(파츠 하나는 PartEffect를 하나만 가진다) 별도 필드로 뺐다")]
    public float legMassPercent;

    [Tooltip("ProceduralCharacterRig가 다리 대신 그릴 시각 종류. legSkillType(Space 동작)과는 " +
             "별개 필드다 - 지금은 다리 4종에 1:1로 대응한다")]
    public LegVisualMode legVisualType;

    [Header("메모리 (Memory 슬롯 전용)")]
    [Tooltip("AI 코어 최대 레벨 증가량. 2026-08-20 명세부터 '머리 기본값을 대체'가 아니라 " +
             "'머리 기본값에 더한다'(명세 표기가 +15/+25/… 가산형이다)")]
    public int coreMaxLevelBonus;

    /// <summary>이 소켓이 해당 카테고리의 무기를 제 짝으로 받아들이는가(범용 소켓은 항상 true).</summary>
    public bool AcceptsWeaponType(WeaponType type) => !restrictsWeaponType || allowedWeaponType == type;

    /// <summary>정비 화면·상점 카드에 보여줄 설명. <b>데이터에서 매번 생성</b>한다(고정 문구는 반드시 어긋난다).</summary>
    public string BuildDescription()
    {
        var lines = new System.Collections.Generic.List<string>();

        if (slot == PartSlot.ArmWeaponSocket)
        {
            lines.Add(restrictsWeaponType ? Loc.T("part.weapon_only", allowedWeaponType.ToDisplayName()) : Loc.T("part.all_categories"));

            var boosts = new System.Collections.Generic.List<string>();
            if (socketAttackSpeedPercent != 0f) boosts.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{socketAttackSpeedPercent:0.##}%");
            if (socketDamageFlat != 0f) boosts.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{socketDamageFlat:0.##}");
            if (socketDamagePercent != 0f) boosts.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{socketDamagePercent:0.##}%");
            if (socketCritChancePercent != 0f) boosts.Add($"{StatTypeNames.ToDisplayName(StatType.CritChance)} +{socketCritChancePercent:0.##}%");
            if (socketSplashPercent != 0f) boosts.Add($"{Loc.T("detail.splash_radius")} +{socketSplashPercent:0.##}%");
            if (socketDefIgnorePercent != 0f) boosts.Add($"{Loc.T("detail.defignore")} +{socketDefIgnorePercent:0.##}%p");
            if (boosts.Count > 0) lines.Add(string.Join(" · ", boosts));

            AppendCommonLines(lines);
            return lines.Count > 0 ? string.Join("\n", lines) : Loc.T("part.no_bonus");
        }

        if (discSlotCount > 0) lines.Add(Loc.T("part.disc_slots", discSlotCount));
        if (coreMaxLevelBonus != 0) lines.Add(Loc.T("part.core_maxlevel", coreMaxLevelBonus));

        if (bonusAmount != 0f) lines.Add(FormatStatLine(bonusStat, bonusAmount));
        if (bonusAmount2 != 0f) lines.Add(FormatStatLine(bonusStat2, bonusAmount2));

        string effectLine = BuildEffectLine();
        if (effectLine.Length > 0) lines.Add(effectLine);

        if (legMassPercent != 0f) lines.Add(Loc.T("part.mass_change", legMassPercent.ToString("0.##")));
        if (legSkillType != LegSkillType.None)
            lines.Add(Loc.T("part.active_skill", legSkillType.ToDisplayName(), legSkillCooldown.ToString("0.#")));
        if (legHpLossSpeedPenalty)
            lines.Add(Loc.T("part.leg.hp_speed_penalty"));
        if (legSpeedRampPassive)
            lines.Add(Loc.T("part.leg.rampup"));

        AppendCommonLines(lines);
        return lines.Count > 0 ? string.Join("\n", lines) : Loc.T("part.no_bonus");
    }

    private void AppendCommonLines(System.Collections.Generic.List<string> lines)
    {
        if (weightCapacity != 0f) lines.Add(Loc.T("part.weight_capacity", weightCapacity.ToString("0.##")));
        if (weight != 0f) lines.Add(Loc.T("detail.weight", weight.ToString("0.##")));
    }

    private static string FormatStatLine(StatType stat, float amount)
    {
        string sign = amount > 0f ? "+" : "-";
        float shown = Mathf.Abs(amount);

        // 2026-08-24 사용자 지정("%로 적용되는 스탯들은 숫자뒤에 % 붙여줘") - 판정 기준을
        // StatFormat으로 모아 AI 코어 카드(AiCoreUpgradePool.BuildEffectLine)와 같은 분류를
        // 쓰게 했다. 예전에는 여기 목록에 사거리 증폭(WeaponRangeBonus)과 치명타 피해
        // (CritDamage, 비율 단위)가 빠져 있어서 "사거리 증폭 +10"처럼 단위 없이 나왔다.
        //
        // 설명 문구의 <b>수치 자체는 반올림하지 않는다</b>(사용자 지정: "아이템 설명 등 자체를
        // 정수로 바꿔버리면 안됨") - 기획 수치를 그대로 보여줘야 하므로 소수점을 유지한다.
        if (stat == StatType.CritDamage)
            return $"{StatTypeNames.ToDisplayName(stat)} {sign}{shown * 100f:0.##}%";

        return StatFormat.IsPercentStat(stat)
            ? $"{StatTypeNames.ToDisplayName(stat)} {sign}{shown:0.##}%"
            : $"{StatTypeNames.ToDisplayName(stat)} {sign}{shown:0.##}";
    }

    private string BuildEffectLine()
    {
        switch (effect)
        {
            case PartEffect.DefFromLuckPercent:
                return Loc.T("parteffect.luck_to_def", effectAmount.ToString("0.##"));
            case PartEffect.DefFromAtkPercent:
                return Loc.T("parteffect.atk_to_def", effectAmount.ToString("0.##"));
            case PartEffect.DefPercentBonus:
                return Loc.T("parteffect.def_percent", effectAmount.ToString("0.##"));
            case PartEffect.DefWhenLowHp:
                return Loc.T("parteffect.lowhp_def", effectAmount.ToString("0.##"));
            case PartEffect.MeleeReflectPercent:
                return Loc.T("parteffect.melee_reflect", effectAmount.ToString("0.##"));
            case PartEffect.PerDiscStat:
                return Loc.T("parteffect.per_disc", StatTypeNames.ToDisplayName(effectStat), effectAmount.ToString("0.##"));
            case PartEffect.PerSymphonyDiscAtk:
                return Loc.T("parteffect.per_symphony_disc", effectAmount.ToString("0.###"));
            case PartEffect.MoveSpeedPercentBonus:
                return Loc.T("parteffect.movespeed_percent", effectAmount.ToString("0.##"));
            case PartEffect.MassPercentBonus:
                return Loc.T("part.mass_change", effectAmount.ToString("0.##"));
            default:
                return string.Empty;
        }
    }
}
