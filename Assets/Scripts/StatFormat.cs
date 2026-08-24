using UnityEngine;

/// <summary>
/// <b>플레이어에게 직접 보이는 숫자</b>의 표기 규칙을 한 곳에 모은 것(2026-08-24 사용자 지정).
///
/// 사용자 지시 원문: "인게임에서 체력, 데미지 등 직접적으로 유저가 보는 대부분의 숫자들은
/// 소수점 없이 정수로만 표현해야해. 공격속도 등 소수점이 기본 단위인건 빼고. 단, 실제 처리되는
/// 숫자나 아이템 설명 등 자체를 정수로 바꿔버리면 안됨."
///
/// 그래서 이 클래스는 <b>표시 전용</b>이다 - 실제 계산은 전부 float 그대로 두고(체력 12.4가
/// 12로 깎이지 않는다) 화면에 찍을 때만 반올림한다. 아이템/디스크/파츠 <b>설명 문구</b>는
/// 기획 수치를 그대로 보여줘야 하므로 여기 규칙을 적용하지 않는다(PartData/DiscData/HeadEffect).
///
/// <b>어떤 스탯이 정수이고 어떤 스탯이 소수인가</b>
/// - 정수(<see cref="Int"/>): 체력·공격력·방어력·행운·데미지·골드·경험치·점수처럼 "개수" 감각의 값
/// - 소수(<see cref="Decimal"/>): 공격속도·이동속도·무게·질량·사거리·시간처럼 소수점이 기본 단위인 값
/// - 퍼센트(<see cref="Percent"/>/<see cref="RatioPercent"/>): 데이터가 %로 들어있는 값(치명타
///   확률 5 = 5%)과 비율로 들어있는 값(치명타 피해 0.5 = 50%)을 구분해서 둘 다 "N%"로 찍는다.
/// </summary>
public static class StatFormat
{
    /// <summary>정수 표기(반올림). 체력·공격력·방어력·행운·데미지 등.</summary>
    public static string Int(float value) => Mathf.RoundToInt(value).ToString();

    /// <summary>소수점이 기본 단위인 값(공격속도·이동속도·무게·질량·사거리·시간). 최대 2자리.</summary>
    public static string Decimal(float value) => value.ToString("0.##");

    /// <summary>데이터가 이미 % 단위인 스탯(치명타 확률 5 = 5%). 정수 + "%".</summary>
    public static string Percent(float value) => Mathf.RoundToInt(value) + "%";

    /// <summary>데이터가 비율인 스탯(치명타 피해 0.5 = 50%). 정수 + "%".</summary>
    public static string RatioPercent(float ratio) => Mathf.RoundToInt(ratio * 100f) + "%";

    /// <summary>"현재/최대" 꼴(체력 등). 둘 다 정수로 찍는다.</summary>
    public static string IntPair(float current, float max) => Int(current) + " / " + Int(max);

    /// <summary>
    /// 이 스탯이 화면에서 "N%"로 보여야 하는지. 데이터가 이미 % 단위인 것만 true다
    /// (<see cref="StatType.CritDamage"/>는 비율이라 <see cref="RatioPercent"/>를 따로 쓴다).
    /// <see cref="AiCoreUpgradePool.BuildEffectLine"/>이 쓰던 분류를 그대로 옮겨와, 파츠·디스크
    /// 설명과 AI 코어 카드가 같은 기준을 공유하게 한다.
    /// </summary>
    public static bool IsPercentStat(StatType stat)
    {
        switch (stat)
        {
            case StatType.Avoid:
            case StatType.CritChance:
            case StatType.GoldGain:
            case StatType.ExpGain:
            case StatType.WeaponRangeBonus:
                return true;
            default:
                return false;
        }
    }
}
