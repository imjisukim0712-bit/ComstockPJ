using System;
using UnityEngine;

/// <summary>
/// 팔/다리 파츠 하나의 정의. 슬롯마다 의미 있는 필드가 달라서(예: 무기소켓은 허용 타입,
/// 장갑은 방어력) 여러 구조체로 쪼개지 않고 하나로 통일했다 - DiscData와 같은 결정.
/// 무기·디스크와 달리 상점에서 사는 게 아니라 부품 상자에서 무작위로 나오므로 가격 필드는 없다.
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

    [Header("무기 소켓(ArmWeaponSocket) 전용")]
    [Tooltip("체크 해제하면 무기 타입 제한이 없다(기본 파츠 상태). 체크하면 allowedWeaponType과 " +
             "다른 타입의 무기를 이 소켓에 장착할 수 없다")]
    public bool restrictsWeaponType;
    public WeaponType allowedWeaponType;

    [Header("스탯 보너스 (팔장갑/다리장갑=방어력, 다리=회피, 발=이동속도)")]
    public StatType bonusStat;
    public float bonusAmount;

    [Header("무게 지탱 (자기장 코어, 다리 전용)")]
    [Tooltip("장착된 모든 무기의 weight 합이 (자기장 코어 + 다리)의 이 값을 넘으면 장착이 거부된다")]
    public float weightCapacity;

    /// <summary>정비 화면 카드에 보여줄 한 줄 요약.</summary>
    public string BuildDescription()
    {
        if (slot == PartSlot.ArmWeaponSocket)
        {
            return restrictsWeaponType ? $"허용 타입: {allowedWeaponType.ToKorean()}" : "무기 타입 제한 없음";
        }

        string statPart = bonusAmount != 0f ? $"{StatTypeNames.ToKorean(bonusStat)} +{bonusAmount:0.##}" : string.Empty;
        string weightPart = weightCapacity != 0f ? $"무게 지탱 +{weightCapacity:0.##}" : string.Empty;

        if (statPart.Length > 0 && weightPart.Length > 0) return $"{statPart} / {weightPart}";
        return statPart.Length > 0 ? statPart : (weightPart.Length > 0 ? weightPart : "(보너스 없음)");
    }
}
