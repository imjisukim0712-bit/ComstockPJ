/// <summary>
/// 무기의 화기 계열. 팔 파츠의 "무기 소켓" 서브파츠가 이 타입 중 하나를 허용 타입으로
/// 지정하고, 그 소켓에는 같은 타입의 무기만 장착할 수 있다(기획서 p.8 "팔: 무기 소켓 =
/// 장착 가능 무기 타입").
///
/// weapon_id → WeaponType 매핑은 시트 기반 WeaponData에 컬럼을 추가하지 않고
/// PartsCatalog.WeaponMeta 로컬 테이블에서 관리한다(외부 데이터 스키마를 건드리지 않기 위함).
/// </summary>
public enum WeaponType
{
    RapidFire,  // 연사화기 (예: 기관단총)
    Shotgun,    // 산탄화기 (예: 샷건)
    Precision,  // 정밀화기 (예: 라이플, 스나이퍼)
    Explosive,  // 폭발화기 (예: 수류탄)
    Energy,     // 에너지무기
    Melee       // 근접무기 (현재 데이터에 해당 무기 없음 - 향후 확장용)
}

public static class WeaponTypeExtensions
{
    public static string ToKorean(this WeaponType type)
    {
        switch (type)
        {
            case WeaponType.RapidFire: return "연사화기";
            case WeaponType.Shotgun: return "산탄화기";
            case WeaponType.Precision: return "정밀화기";
            case WeaponType.Explosive: return "폭발화기";
            case WeaponType.Energy: return "에너지무기";
            case WeaponType.Melee: return "근접무기";
            default: return type.ToString();
        }
    }
}
