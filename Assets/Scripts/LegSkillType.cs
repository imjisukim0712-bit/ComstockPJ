/// <summary>
/// 다리(Leg) 파츠가 Space(액티브 스킬)에 부여하는 동작 종류(2026-08-18 다리 기획서 Ver02).
/// 파츠·디스크의 특수효과가 <see cref="PartEffect"/>로 빠지는 것과 같은 이유로, "합산 스탯으로
/// 표현할 수 없는 것"을 여기서 분리한다 - 이건 스탯이 아니라 <b>Space 입력에 대한 동작 자체</b>가
/// 다리마다 다르다.
///
/// 값은 int로 직렬화되므로 <b>중간에 끼워 넣지 말고 항상 맨 끝에 추가</b>할 것.
/// </summary>
public enum LegSkillType
{
    /// <summary>액티브 스킬 없음(로켓 추진기 - 패시브만 있다). Space를 눌러도 아무 일도 없다.</summary>
    None,

    /// <summary>구르기(기본 다리) - 이동 방향으로 짧게 굴러가며 무적. 몸통이 360도 회전한다.</summary>
    Roll,

    /// <summary>폴짝 뛰기(거미 다리) - 구르기와 같은 이동/무적이지만 회전 없이 위아래로만 튄다.</summary>
    Hop,

    /// <summary>순간 부스트(캐터필러) - 위치를 옮기지 않고 짧게 이동속도만 배로 올린다. 무적 없음.</summary>
    Boost
}

public static class LegSkillTypeExtensions
{
    /// <summary>다리 스킬의 표시명(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
    public static string ToDisplayName(this LegSkillType type)
    {
        switch (type)
        {
            case LegSkillType.Roll: return Loc.T("legskill.roll");
            case LegSkillType.Hop: return Loc.T("legskill.hop");
            case LegSkillType.Boost: return Loc.T("legskill.boost");
            default: return Loc.T("common.none");
        }
    }
}
