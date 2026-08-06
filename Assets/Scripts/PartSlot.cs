/// <summary>
/// 팔/다리 계열 모딩 슬롯 6종(기획서 p.8). 머리는 로봇 종류 자체라 모딩 대상이 아니다.
/// `RunState.EquippedPartIds`의 키로 이 enum의 `ToString()`을 그대로 사용한다.
/// </summary>
public enum PartSlot
{
    ArmWeaponSocket, // 팔 - 무기 소켓 (장착 가능 무기 타입을 결정)
    ArmArmor,        // 팔 - 팔 장갑 (방어력)
    MagneticCore,    // 팔 - 자기장 코어 (지탱 가능 무기 무게)
    Leg,             // 다리 (무게 지탱 + 회피 판정에 쓰이는 이동 기술)
    LegArmor,        // 다리 - 다리 장갑 (방어력)
    Foot             // 다리 - 발 (기동력/이동속도)
}

public static class PartSlotExtensions
{
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
            default: return slot.ToString();
        }
    }
}
