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
    /// <summary>무기 타입의 표시명(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
    public static string ToDisplayName(this WeaponType type)
    {
        switch (type)
        {
            case WeaponType.RapidFire: return Loc.T("weapontype.rapidfire");
            case WeaponType.Shotgun: return Loc.T("weapontype.shotgun");
            case WeaponType.Precision: return Loc.T("weapontype.precision");
            case WeaponType.Explosive: return Loc.T("weapontype.explosive");
            case WeaponType.Energy: return Loc.T("weapontype.energy");
            case WeaponType.Melee: return Loc.T("weapontype.melee");
            default: return type.ToString();
        }
    }
}
