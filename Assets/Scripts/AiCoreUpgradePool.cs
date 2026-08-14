using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 코어가 레벨업할 때 정비 시간에 제시되는 업그레이드 선택지 풀.
/// 레벨업마다 이 목록에서 서로 다른 3개를 무작위로 뽑아 카드로 보여준다(AiCoreManager 참고).
/// 시트에서 가져오는 데이터가 아니라 기획자가 직접 채워 넣는 로컬 전용 데이터라
/// GameDataAsset과 별도의 에셋으로 관리한다.
///
/// <b>2026-08-13 등급 도입(사용자 요청)</b>: 무기·디스크·파츠처럼 업그레이드 선택지에도
/// 등급(일반/희귀/서사/유일/전설)이 붙는다. <see cref="Option.amount"/>는 이제
/// <b>일반 등급 기준값</b>이고, 실제 증가량은 여기에 등급 배율(<see cref="GradeSetting.amountMultiplier"/>)을
/// 곱한 값이다. 사용자가 지정한 예시(A* 알고리즘 이동속도 0.3 / 0.5 / 0.7 / 1.0 / 1.5)가
/// 그대로 나오도록 배율을 1 / 5/3 / 7/3 / 10/3 / 5로 잡았다 - 기준값 b에 대해 항상
/// b / 1.67b / 2.33b / 3.33b / 5b 가 되므로, 기준값을 0.3의 배수로 잡으면 위 예시와 같은
/// 깔끔한 수열이 나온다.
/// </summary>
[CreateAssetMenu(fileName = "AiCoreUpgradePool", menuName = "Comstock/AI 코어 업그레이드 풀")]
public class AiCoreUpgradePool : ScriptableObject
{
    [Serializable]
    public struct Option
    {
        public string displayName;

        [TextArea]
        [Tooltip("비워두면 statType/등급별 실제 증가량으로 자동 생성한다(등급마다 수치가 달라지므로 " +
                 "고정 문구를 넣으면 카드에 적힌 값과 실제 적용값이 어긋난다)")]
        public string description;

        public StatType statType;

        [Tooltip("일반(Normal) 등급 기준 증가량. 상위 등급은 여기에 등급 배율이 곱해진다")]
        public float amount;
    }

    /// <summary>등급 하나의 설정. ShopCatalog.GradeSetting과 같은 역할이며 가중치/최소 웨이브도 같은 값을 쓴다.</summary>
    [Serializable]
    public struct GradeSetting
    {
        public ItemGrade grade;

        [Tooltip("일반 등급 기준값(Option.amount)에 곱해지는 배율. 일반=1")]
        public float amountMultiplier;

        [Tooltip("등급 추첨에서 이 등급이 뽑힐 상대 가중치(클수록 자주 등장)")]
        public float weight;

        [Tooltip("이 등급이 등장하기 시작하는 최소 웨이브. 초반에 전설이 나오지 않게 막는 용도")]
        public int minWave;
    }

    public List<Option> options = new List<Option>();

    [Header("등급 설정 (가중치/최소 웨이브는 ShopCatalog와 같은 값)")]
    [SerializeField]
    private List<GradeSetting> gradeSettings = new List<GradeSetting>
    {
        new GradeSetting { grade = ItemGrade.Normal,    amountMultiplier = 1f,       weight = 50f, minWave = 1 },
        new GradeSetting { grade = ItemGrade.Rare,      amountMultiplier = 5f / 3f,  weight = 26f, minWave = 1 },
        new GradeSetting { grade = ItemGrade.Epic,      amountMultiplier = 7f / 3f,  weight = 14f, minWave = 3 },
        new GradeSetting { grade = ItemGrade.Unique,    amountMultiplier = 10f / 3f, weight = 7f,  minWave = 5 },
        new GradeSetting { grade = ItemGrade.Legendary, amountMultiplier = 5f,       weight = 3f,  minWave = 7 }
    };

    public IReadOnlyList<GradeSetting> GradeSettings => gradeSettings;

    /// <summary>등급 설정을 찾는다. 없으면 배율 1의 기본값을 돌려준다.</summary>
    public GradeSetting GetGradeSetting(ItemGrade grade)
    {
        foreach (GradeSetting setting in gradeSettings)
        {
            if (setting.grade == grade) return setting;
        }

        return new GradeSetting { grade = grade, amountMultiplier = 1f, weight = 1f, minWave = 1 };
    }

    /// <summary>
    /// 현재 웨이브에서 등장 가능한 등급들 중 가중치로 하나를 뽑는다(minWave 조건 때문에 초반에는
    /// 낮은 등급만 뽑힌다). ShopCatalog.RollGrade와 같은 규칙이다.
    /// </summary>
    public ItemGrade RollGrade(int waveNumber)
    {
        float totalWeight = 0f;
        foreach (GradeSetting setting in gradeSettings)
        {
            if (waveNumber < setting.minWave) continue;
            totalWeight += Mathf.Max(0f, setting.weight);
        }

        if (totalWeight <= 0f) return ItemGrade.Normal;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        foreach (GradeSetting setting in gradeSettings)
        {
            if (waveNumber < setting.minWave) continue;

            roll -= Mathf.Max(0f, setting.weight);
            if (roll <= 0f) return setting.grade;
        }

        return ItemGrade.Normal;
    }

    /// <summary>
    /// 이 등급으로 뽑혔을 때 실제로 적용되는 증가량.
    ///
    /// 체력/공격력/방어력은 <see cref="RobotStats"/>가 정수로 반영하지만 여기서 정수로 깎지는
    /// 않는다 - 기준값이 1보다 작은 스탯(방어력 0.3)을 등급마다 반올림하면 일반=0, 희귀=1,
    /// 서사=1처럼 등급 구분이 사라진다. AI 코어 보너스는 <see cref="RunState.CoreStatBonuses"/>에
    /// <b>소수 그대로 누적</b>되고 RobotStats가 그 합계를 한 번만 반올림하므로, 여러 번 뽑으면
    /// 소수 증가분도 정확히 누적된다(0.3을 두 번 = 0.6 → 반영 +1).
    /// </summary>
    public float GetGradedAmount(Option option, ItemGrade grade)
    {
        float amount = option.amount * Mathf.Max(0f, GetGradeSetting(grade).amountMultiplier);
        return Mathf.Round(amount * 100f) / 100f; // 3.3333… 같은 값이 카드에 그대로 나오지 않게 다듬는다
    }

    /// <summary>
    /// "이동속도 +0.5"처럼 실제 적용값을 그대로 보여주는 한 줄 설명을 만든다.
    /// 등급마다 수치가 달라지므로 에셋에 고정 문구를 두지 않고 매번 여기서 만든다.
    /// </summary>
    public static string BuildEffectLine(StatType statType, float amount)
    {
        string name = StatTypeNames.ToKorean(statType);

        switch (statType)
        {
            // 데이터에 이미 퍼센트 단위(5 = 5%)로 들어있는 스탯
            case StatType.Avoid:
            case StatType.CritChance:
            case StatType.GoldGain:
            case StatType.WeaponRangeBonus:
                return $"{name} +{amount:0.##}%";

            // 비율(0.1 = 10%)로 들어있는 스탯
            case StatType.CritDamage:
                return $"{name} +{amount * 100f:0.##}%";

            default:
                return $"{name} +{amount:0.##}";
        }
    }
}
