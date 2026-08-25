/// <summary>
/// 무기 타입 - 팔의 "무기 소켓" 파츠가 <b>어떤 무기를 장착할 수 있는지</b>를 결정하는 분류.
/// 경무장 / 중무장 / 근접무기 3가지뿐이다(사용자 확정 사항).
///
/// <b>투사체 타입(<see cref="WeaponType"/>)과는 다른 개념이며 둘 다 유지된다.</b>
/// - 무기 타입(이 enum)   : 소켓 장착 가능 여부를 가른다. 경/중/근접 3종
/// - 투사체 타입(WeaponType): 연사/산탄/정밀/폭발/에너지/근접 등 공격 방식의 분류
/// 소켓 제한은 <b>무기 타입에만</b> 영향을 준다.
///
/// weapon_id → WeaponClass 매핑은 시트 기반 WeaponData에 컬럼을 추가하지 않고
/// PartsCatalog.weaponMeta 로컬 테이블에서 관리한다(외부 데이터 스키마를 건드리지 않기 위함).
/// </summary>
public enum WeaponClass
{
    Light, // 경무장
    Heavy, // 중무장
    Melee  // 근접무기
}

public static class WeaponClassExtensions
{
    /// <summary>무기 분류의 표시명(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
    public static string ToDisplayName(this WeaponClass weaponClass)
    {
        switch (weaponClass)
        {
            case WeaponClass.Light: return Loc.T("weaponclass.light");
            case WeaponClass.Heavy: return Loc.T("weaponclass.heavy");
            case WeaponClass.Melee: return Loc.T("weaponclass.melee");
            default: return weaponClass.ToString();
        }
    }
}
