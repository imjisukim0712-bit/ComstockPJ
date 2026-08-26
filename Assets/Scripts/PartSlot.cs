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

    // 2026-08-26 사용자 지시로 팔장갑/자기장 코어 시스템 완전 삭제. enum은 int로 직렬화되므로
    // Foot과 같은 이유(위 클래스 주석 참고)로 자리만 남겨둔다 - PartsCatalog에는 이 두 슬롯의
    // 파츠 데이터가 이제 없고(GetDefaultPart가 null 반환), DisplayOrder에도 없어 UI 어디에도
    // 나타나지 않는다. 자기장 코어가 주던 무기 무게 지탱력은 다리(Leg)만으로 계산된다
    // (ModdingManager.GetTotalWeightCapacity 참고).
    ArmArmor,        // (미사용) 팔 - 팔 장갑
    MagneticCore,    // (미사용) 팔 - 자기장 코어
    Leg,             // 다리 (무게 지탱 + 회피, 2026-08-18부터 이동속도까지 - 아래 Foot 참고)
    LegArmor,        // 다리 - 다리 장갑 (방어력)

    // 2026-08-18 `UI 기획서.pdf` 반영: "메모리 추가 / 발 삭제"(사용자 확정). enum은 int로
    // 직렬화되므로 값을 지울 수 없어 자리만 남겨둔다 - PartsCatalog에는 이 슬롯의 파츠 데이터가
    // 이제 없고(GetDefaultPart가 null 반환), DisplayOrder에도 없어 UI에 나타나지 않는다.
    // 발이 주던 이동속도 보너스는 다리(Leg) 파츠로 옮겨졌다(경량 부츠/제트 부스터 계열).
    Foot,
    Helmet,          // 머리 - 헬멧 (방어력)
    DiscSlot,        // 머리 - 디스크 슬롯 (장착 가능한 최대 디스크 개수)

    Memory           // 머리 - 메모리 카드 (AI 코어 최대 레벨을 결정, 2026-08-18 신설)
}

public static class PartSlotExtensions
{
    /// <summary>
    /// 정비 화면에 슬롯을 배치하는 순서(시안 이미지 기준). 부위 그룹(머리 → 팔 → 다리)
    /// 순서대로 나열한다. enum 선언 순서는 직렬화 호환 때문에 못 바꾸므로 표시 순서를 따로 둔다.
    ///
    /// <b>ArmWeaponSocket은 여기 없다</b>(2026-08-12 "무기 소켓 개별화" 플랜) - 무기 소켓은 이제
    /// 소켓 인덱스별로 카드가 N개(ModdingManager.ActiveSocketCount) 그려지므로, 이 슬롯 하나로
    /// 표현할 수 없어 UI가 별도로 처리한다(ModdingPanelUI.RebuildSlots 참고).
    /// </summary>
    /// <summary>2026-08-26 팔장갑/자기장 코어 삭제로 5칸(메모리/헬멧/다리/다리장갑/디스크
    /// 슬롯)만 남았다(3열 그리드에 순서대로 채우면 2행, 마지막 행은 2칸만 찬다). Foot·ArmArmor·
    /// MagneticCore는 더 이상 나열하지 않는다.</summary>
    public static readonly PartSlot[] DisplayOrder =
    {
        PartSlot.Memory,          // 머리
        PartSlot.Helmet,          // 머리
        PartSlot.Leg,             // 다리
        PartSlot.LegArmor,        // 다리
        PartSlot.DiscSlot         // 머리
    };

    /// <summary>파츠 슬롯의 표시명(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
    public static string ToDisplayName(this PartSlot slot)
    {
        switch (slot)
        {
            case PartSlot.ArmWeaponSocket: return Loc.T("partslot.weaponsocket");
            // ArmArmor/MagneticCore는 2026-08-26 삭제된 슬롯이라 case가 없다 - 번역 키
            // (partslot.armarmor/magneticcore)도 함께 지웠으므로 Loc.T를 부르면 키 문자열이
            // 그대로 화면에 나올 뿐이다. default로 떨어져 enum 이름을 돌려준다(어차피
            // DisplayOrder에 없어 UI에는 나타나지 않는다).
            case PartSlot.Leg: return Loc.T("partslot.leg");
            case PartSlot.LegArmor: return Loc.T("partslot.legarmor");
            case PartSlot.Foot: return Loc.T("partslot.foot");
            case PartSlot.Helmet: return Loc.T("partslot.helmet");
            case PartSlot.DiscSlot: return Loc.T("partslot.discslot");
            case PartSlot.Memory: return Loc.T("partslot.memory");
            default: return slot.ToString();
        }
    }
}
