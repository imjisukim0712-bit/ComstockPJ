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
             "(자기장 코어 + 다리)의 weightCapacity와 비교된다. 디스크에는 무게가 없다")]
    public float weight;

    [Header("무게 지탱 (자기장 코어, 다리 전용)")]
    [Tooltip("장착된 모든 무기 + 파츠의 무게 합이 이 값을 넘으면 초과분만큼 이동속도가 깎인다")]
    public float weightCapacity;

    [Header("디스크 슬롯 (DiscSlot 슬롯 전용)")]
    [Tooltip("장착 가능한 최대 디스크 개수. 이 파츠를 끼우면 로봇(머리) 기본값 대신 이 값이 쓰인다")]
    public int discSlotCount;

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
            lines.Add(restrictsWeaponType ? $"{allowedWeaponType.ToKorean()} 전용" : "모든 카테고리 장착 가능");

            var boosts = new System.Collections.Generic.List<string>();
            if (socketAttackSpeedPercent != 0f) boosts.Add($"공격 속도 +{socketAttackSpeedPercent:0.##}%");
            if (socketDamageFlat != 0f) boosts.Add($"공격력 +{socketDamageFlat:0.##}");
            if (socketDamagePercent != 0f) boosts.Add($"공격력 +{socketDamagePercent:0.##}%");
            if (socketCritChancePercent != 0f) boosts.Add($"치명타 확률 +{socketCritChancePercent:0.##}%");
            if (socketSplashPercent != 0f) boosts.Add($"스플래시 범위 +{socketSplashPercent:0.##}%");
            if (socketDefIgnorePercent != 0f) boosts.Add($"방어력 무시 +{socketDefIgnorePercent:0.##}%p");
            if (boosts.Count > 0) lines.Add(string.Join(" · ", boosts));

            AppendCommonLines(lines);
            return lines.Count > 0 ? string.Join("\n", lines) : "(보너스 없음)";
        }

        if (discSlotCount > 0) lines.Add($"디스크 슬롯 {discSlotCount}칸");
        if (coreMaxLevelBonus != 0) lines.Add($"AI 코어 최대 레벨 +{coreMaxLevelBonus}");

        if (bonusAmount != 0f) lines.Add(FormatStatLine(bonusStat, bonusAmount));
        if (bonusAmount2 != 0f) lines.Add(FormatStatLine(bonusStat2, bonusAmount2));

        string effectLine = BuildEffectLine();
        if (effectLine.Length > 0) lines.Add(effectLine);

        AppendCommonLines(lines);
        return lines.Count > 0 ? string.Join("\n", lines) : "(보너스 없음)";
    }

    private void AppendCommonLines(System.Collections.Generic.List<string> lines)
    {
        if (weightCapacity != 0f) lines.Add($"무게 지탱 +{weightCapacity:0.##}");
        if (weight != 0f) lines.Add($"무게 {weight:0.##}");
    }

    private static string FormatStatLine(StatType stat, float amount)
    {
        string sign = amount > 0f ? "+" : "-";
        float shown = Mathf.Abs(amount);
        bool percent = stat == StatType.CritChance || stat == StatType.Avoid ||
                       stat == StatType.GoldGain || stat == StatType.ExpGain;

        return percent
            ? $"{StatTypeNames.ToKorean(stat)} {sign}{shown:0.##}%"
            : $"{StatTypeNames.ToKorean(stat)} {sign}{shown:0.##}";
    }

    private string BuildEffectLine()
    {
        switch (effect)
        {
            case PartEffect.DefFromLuckPercent:
                return $"행운의 {effectAmount:0.##}%만큼 방어력 증가";
            case PartEffect.DefFromAtkPercent:
                return $"공격력의 {effectAmount:0.##}%만큼 방어력 증가";
            case PartEffect.DefPercentBonus:
                return $"방어력 {effectAmount:0.##}% 증가";
            case PartEffect.DefWhenLowHp:
                return $"체력이 50% 이하일 때 방어력 +{effectAmount:0.##}";
            case PartEffect.MeleeReflectPercent:
                return $"근접 공격 반사 {effectAmount:0.##}%";
            case PartEffect.CoreStartLevel:
                return $"AI 코어 시작 레벨 +{effectAmount:0.##}";
            case PartEffect.PerDiscStat:
                return $"장착한 디스크 1개당 {StatTypeNames.ToKorean(effectStat)} +{effectAmount:0.##}";
            case PartEffect.PerSymphonyDiscAtk:
                return $"장착한 \"교향곡\" 계열 디스크 1개당 공격력 +{effectAmount:0.###}";
            default:
                return string.Empty;
        }
    }
}
