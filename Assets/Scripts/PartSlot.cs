/// <summary>
/// 모딩 슬롯(컨셉기획서의 파츠 9부위 중 실제로 교체 가능한 8종).
/// `RunState.EquippedPartIds`의 키로 이 enum의 `ToString()`을 그대로 사용한다.
///
/// **머리(Head)는 여기에 넣지 않는다.** 머리는 곧 로봇 종류 자체(PlayerSession.SelectedRobotId)이며
/// 런 중 교체되지 않는 조회 전용 부위다. 여기에 넣으면 부품 상자 추첨(TryRollLootPart)이
/// 머리 파츠를 뽑아버려 "런 중 변경 불가" 규칙이 깨진다.
///
/// **순서를 절대 바꾸지 말 것.** enum은 int로 직렬화되므로 중간에 값을 끼워 넣으면
/// PartsCatalog.asset에 이미 저장된 파츠들의 슬롯이 통째로 어긋난다. 새 슬롯은 항상 맨 끝에 추가한다.
/// </summary>
public enum PartSlot
{
    ArmWeaponSocket, // 팔 - 무기 소켓 (장착 가능 무기 타입을 결정)
    ArmArmor,        // 팔 - 팔 장갑 (방어력)
    MagneticCore,    // 팔 - 자기장 코어 (지탱 가능 무기 무게)
    Leg,             // 다리 (무게 지탱 + 회피 판정에 쓰이는 이동 기술)
    LegArmor,        // 다리 - 다리 장갑 (방어력)
    Foot,            // 다리 - 발 (기동력/이동속도)
    Helmet,          // 머리 - 헬멧 (방어력)
    DiscSlot         // 머리 - 디스크 슬롯 (장착 가능한 최대 디스크 개수)
}

public static class PartSlotExtensions
{
    /// <summary>
    /// 정비 화면에 슬롯을 배치하는 순서(시안 이미지 기준). 부위 그룹(머리 → 팔 → 다리)
    /// 순서대로 나열한다. enum 선언 순서는 직렬화 호환 때문에 못 바꾸므로 표시 순서를 따로 둔다.
    /// </summary>
    public static readonly PartSlot[] DisplayOrder =
    {
        PartSlot.Helmet,          // 머리
        PartSlot.DiscSlot,        // 머리
        PartSlot.ArmWeaponSocket, // 팔
        PartSlot.ArmArmor,        // 팔
        PartSlot.MagneticCore,    // 팔
        PartSlot.Leg,             // 다리
        PartSlot.LegArmor,        // 다리
        PartSlot.Foot             // 다리
    };

    public static string ToKorean(this PartSlot slot)
    {
        switch (slot)
        {
            case PartSlot.ArmWeaponSocket: return "무기 소켓";
            case PartSlot.ArmArmor: return "팔 장갑";
            case PartSlot.MagneticCore: return "자기장 코어";
            case PartSlot.Leg: return "다리";
            case PartSlot.LegArmor: return "다리 장갑";
            case PartSlot.Foot: return "발";
            case PartSlot.Helmet: return "헬멧";
            case PartSlot.DiscSlot: return "디스크 슬롯";
            default: return slot.ToString();
        }
    }
}
