using System;
using UnityEngine;

/// <summary>
/// 디스크(CD 형태) 한 종류의 정의. 다른 로그라이크의 유물/아이템 역할이며,
/// 기획서대로 <b>특정 스탯이 오르는 대신 다른 스탯이 내려가는 리스크</b>를 함께 가진다.
///
/// 무기와 달리 구글시트에 원본 테이블이 없는 신규 데이터라, 기획자가 직접 채우는
/// 로컬 전용 데이터로 ShopCatalog 에셋 안에 목록으로 보관한다
/// (AiCoreUpgradePool과 동일한 방식).
/// </summary>
[Serializable]
public struct DiscData
{
    [Tooltip("디스크 고유 ID. RunState.EquippedDiscIds에 이 값이 저장된다")]
    public int discId;

    [Tooltip("상점 카드와 장착 목록에 표시될 이름")]
    public string discName;

    [Tooltip("이 디스크의 기본 등급. 상점 등장 시 이 등급으로 고정된다(무기와 달리 등급별 배율을 곱하지 않는다)")]
    public ItemGrade grade;

    [Tooltip("기본 판매 가격(골드). 등급별 가격 배율은 적용하지 않고 이 값을 그대로 쓴다")]
    public int price;

    [Header("상승 스탯 (이득)")]
    public StatType upStat;
    public float upAmount;

    [Header("하락 스탯 (리스크)")]
    [Tooltip("내려갈 스탯. upStat과 같은 값으로 두지 말 것")]
    public StatType downStat;

    [Tooltip("내려갈 양(양수로 적는다. 실제 적용 시 음수로 바뀐다)")]
    public float downAmount;

    /// <summary>상점 카드/장착 목록에 보여줄 한 줄 설명. 예: "공격력 +3 / 이동속도 -0.4"</summary>
    public string BuildDescription()
    {
        return $"{StatTypeNames.ToKorean(upStat)} +{upAmount:0.##} / {StatTypeNames.ToKorean(downStat)} -{downAmount:0.##}";
    }
}

/// <summary>StatType을 UI에 한글로 보여주기 위한 표시명 모음.</summary>
public static class StatTypeNames
{
    public static string ToKorean(StatType type)
    {
        switch (type)
        {
            case StatType.MaxHp: return "체력";
            case StatType.Atk: return "공격력";
            case StatType.Def: return "방어력";
            case StatType.MoveSpeed: return "이동속도";
            case StatType.Avoid: return "회피율";
            case StatType.Luck: return "행운";
            case StatType.CritChance: return "치명타 확률";
            case StatType.CritDamage: return "치명타 피해";
            case StatType.Mass: return "질량";
            default: return type.ToString();
        }
    }
}
