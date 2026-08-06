/// <summary>
/// 상점에 나오는 품목(무기/디스크)의 등급. 기획서의 "등급 = 수직 강화"에 해당한다.
/// 같은 종류의 무기라도 등급이 높으면 더 강한 성능으로 나온다(실제 배율은 ShopCatalog에서 지정).
///
/// 등급 이름은 밸런스 수치가 아니라 표시용 고정값이라 데이터 에셋이 아니라 여기에 둔다.
/// 수치(스탯 배율/가격 배율/등장 가중치)는 전부 ShopCatalog 에셋에서 관리한다.
/// </summary>
public enum ItemGrade
{
    Normal,     // 일반
    Rare,       // 희귀
    Epic,       // 서사
    Unique,     // 유일
    Legendary   // 전설
}

/// <summary>등급의 한글 표시명과 표시 색상을 제공한다.</summary>
public static class ItemGradeExtensions
{
    public static string ToKorean(this ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Normal: return "일반";
            case ItemGrade.Rare: return "희귀";
            case ItemGrade.Epic: return "서사";
            case ItemGrade.Unique: return "유일";
            case ItemGrade.Legendary: return "전설";
            default: return grade.ToString();
        }
    }

    /// <summary>등급 표시에 쓸 색상 코드(TMP 리치텍스트 &lt;color=#RRGGBB&gt;에 그대로 넣는 용도).</summary>
    public static string ToColorHex(this ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Normal: return "#C8C8C8";     // 회색
            case ItemGrade.Rare: return "#4FA3FF";       // 파랑
            case ItemGrade.Epic: return "#B45CFF";       // 보라
            case ItemGrade.Unique: return "#FFB000";     // 주황
            case ItemGrade.Legendary: return "#FF4B4B";  // 빨강
            default: return "#FFFFFF";
        }
    }
}
