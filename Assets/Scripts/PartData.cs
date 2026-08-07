using System;
using UnityEngine;

/// <summary>
/// 파츠 하나의 정의. 슬롯마다 의미 있는 필드가 달라서(예: 무기소켓은 허용 무기 타입,
/// 장갑은 방어력) 여러 구조체로 쪼개지 않고 하나로 통일했다 - DiscData와 같은 결정.
/// 무기·디스크와 달리 상점에서 사는 게 아니라 부품 상자에서 무작위로 나오므로 가격 필드는 없다.
///
/// 파츠는 <b>종류마다 5개 등급이 전부 존재하는 형식</b>이다(예: "경량 헬멧" = 일반/희귀/서사/유일/전설).
/// 새 종류를 추가할 때는 5개 등급을 모두 채울 것.
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

    [Header("무기 소켓(ArmWeaponSocket) 전용 - 종류는 '장착 가능한 무기 타입'을 결정한다")]
    [Tooltip("체크 해제하면 무기 타입 제한이 없다(기본 파츠 상태). 체크하면 allowedWeaponClass와 " +
             "다른 타입의 무기를 이 소켓에 장착할 수 없다. 제한은 무기 타입(경/중/근접)에만 걸리며 " +
             "투사체 타입(연사/산탄/정밀 등)과는 무관하다")]
    public bool restrictsWeaponClass;
    public WeaponClass allowedWeaponClass;

    [Header("무기 소켓 등급 효과 - 사거리/감지거리/회전속도 배율")]
    [Tooltip("탄이 날아가는 최대 거리에 곱해지는 배율. 1 = 무기 기본값 그대로")]
    public float socketRangeMultiplier;

    [Tooltip("적을 감지해 발사를 시작하는 거리에 곱해지는 배율. 1 = 무기 기본값 그대로.\n" +
             "감지한 적에게 탄이 닿아야 하므로 사거리 배율보다 크지 않게 두는 것이 안전하다")]
    public float socketDetectRangeMultiplier;

    [Tooltip("무기가 조준 방향으로 돌아가는 속도에 곱해지는 배율. 1 = 소켓 기본 속도 그대로")]
    public float socketRotationSpeedMultiplier;

    [Header("스탯 보너스 (팔장갑/다리장갑/헬멧=방어력, 다리=회피, 발=이동속도)")]
    public StatType bonusStat;
    public float bonusAmount;

    [Header("무게 지탱 (자기장 코어, 다리 전용)")]
    [Tooltip("장착된 모든 무기의 weight 합이 (자기장 코어 + 다리)의 이 값을 넘으면 장착이 거부된다")]
    public float weightCapacity;

    [Header("디스크 슬롯 (DiscSlot 슬롯 전용)")]
    [Tooltip("장착 가능한 최대 디스크 개수. 이 파츠를 끼우면 로봇(머리) 기본값 대신 이 값이 쓰인다")]
    public int discSlotCount;

    /// <summary>배율 필드가 0(미설정)인 옛 데이터를 만나도 1배로 안전하게 동작하도록 보정한다.</summary>
    public float RangeMultiplier => socketRangeMultiplier > 0f ? socketRangeMultiplier : 1f;
    public float DetectRangeMultiplier => socketDetectRangeMultiplier > 0f ? socketDetectRangeMultiplier : 1f;
    public float RotationSpeedMultiplier => socketRotationSpeedMultiplier > 0f ? socketRotationSpeedMultiplier : 1f;

    /// <summary>정비 화면 카드에 보여줄 한 줄 요약.</summary>
    public string BuildDescription()
    {
        if (slot == PartSlot.ArmWeaponSocket)
        {
            string allowed = restrictsWeaponClass ? allowedWeaponClass.ToKorean() : "전체";
            return $"{allowed} 장착 / 사거리 x{RangeMultiplier:0.##} · 감지 x{DetectRangeMultiplier:0.##} · 회전 x{RotationSpeedMultiplier:0.##}";
        }

        if (slot == PartSlot.DiscSlot)
        {
            return $"디스크 슬롯 {discSlotCount}칸";
        }

        string statPart = bonusAmount != 0f ? $"{StatTypeNames.ToKorean(bonusStat)} +{bonusAmount:0.##}" : string.Empty;
        string weightPart = weightCapacity != 0f ? $"무게 지탱 +{weightCapacity:0.##}" : string.Empty;

        if (statPart.Length > 0 && weightPart.Length > 0) return $"{statPart} / {weightPart}";
        return statPart.Length > 0 ? statPart : (weightPart.Length > 0 ? weightPart : "(보너스 없음)");
    }
}
