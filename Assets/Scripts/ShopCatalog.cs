using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점이 사용하는 모든 데이터(무기 등급·가격, 디스크 목록, 새로고침 비용)를 담는 로컬 에셋.
///
/// 무기의 등급·가격은 GameDataAsset의 무기 테이블에 컬럼이 없어서 여기에 둔다.
///
/// 무기는 "종류 = 수평 강화 / 등급 = 수직 강화" 규칙에 따라, 무기 6종을 그대로 두고
/// 등급별 배율(GradeSetting.statMultiplier)을 곱해서 상위 등급 무기를 만들어낸다.
/// 나중에 기획자가 등급별 무기 행을 실제로 채우면, WeaponEntry에 등급별 weapon_id를 직접
/// 지정하는 방식으로 바꾸면 된다.
/// </summary>
[CreateAssetMenu(fileName = "ShopCatalog", menuName = "Comstock/상점 카탈로그")]
public class ShopCatalog : ScriptableObject
{
    /// <summary>등급 하나의 밸런스 설정.</summary>
    [Serializable]
    public struct GradeSetting
    {
        public ItemGrade grade;

        [Tooltip("이 등급으로 나올 때 무기 기본 스탯(공격력/공격속도)에 곱해지는 배율. 일반=1")]
        public float statMultiplier;

        [Tooltip("이 등급으로 나올 때 무기 기본 가격에 곱해지는 배율")]
        public float priceMultiplier;

        [Tooltip("상점 품목 추첨에서 이 등급이 뽑힐 상대 가중치(클수록 자주 등장)")]
        public float weight;

        [Tooltip("이 등급이 등장하기 시작하는 최소 웨이브. 초반 웨이브에 전설이 나오지 않게 막는 용도")]
        public int minWave;
    }

    /// <summary>상점에 내놓을 무기 한 종류.</summary>
    [Serializable]
    public struct WeaponEntry
    {
        [Tooltip("GameDataManager.Weapons 조회에 쓰는 weapon_id (무기 시트 기준)")]
        public int weaponId;

        [Tooltip("일반 등급 기준 가격(골드). 실제 가격 = 이 값 x 등급 가격배율")]
        public int basePrice;
    }

    [Header("등급 설정 (전부 밸런스 미확정 임시값)")]
    [SerializeField]
    private List<GradeSetting> gradeSettings = new List<GradeSetting>
    {
        new GradeSetting { grade = ItemGrade.Normal,    statMultiplier = 1.0f, priceMultiplier = 1.0f, weight = 50f, minWave = 1 },
        new GradeSetting { grade = ItemGrade.Rare,      statMultiplier = 1.3f, priceMultiplier = 1.8f, weight = 26f, minWave = 1 },
        new GradeSetting { grade = ItemGrade.Epic,      statMultiplier = 1.7f, priceMultiplier = 3.0f, weight = 14f, minWave = 3 },
        new GradeSetting { grade = ItemGrade.Unique,    statMultiplier = 2.2f, priceMultiplier = 5.0f, weight = 7f,  minWave = 5 },
        new GradeSetting { grade = ItemGrade.Legendary, statMultiplier = 3.0f, priceMultiplier = 8.0f, weight = 3f,  minWave = 7 }
    };

    [Header("상점에 나올 무기 (가격은 전부 밸런스 미확정 임시값)")]
    [SerializeField]
    private List<WeaponEntry> weaponEntries = new List<WeaponEntry>();

    [Header("상점에 나올 디스크 (전부 밸런스 미확정 임시값)")]
    [SerializeField]
    private List<DiscData> discs = new List<DiscData>();

    [Header("품목 구성")]
    [Tooltip("상점에 한 번에 표시되는 품목 칸 수 (기획서 p.13 기준 4칸)")]
    [SerializeField] private int slotCount = 4;

    [Tooltip("품목 하나가 무기가 아니라 디스크로 나올 확률(0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float discAppearChance = 0.4f;

    [Header("새로고침")]
    [Tooltip("웨이브마다 첫 새로고침에 드는 비용")]
    [SerializeField] private int refreshBaseCost = 10;

    [Tooltip("같은 웨이브에서 새로고침을 반복할 때마다 추가로 붙는 비용")]
    [SerializeField] private int refreshCostIncrement = 5;

    [Header("디스크 슬롯 (임시)")]
    [Tooltip("장착 가능한 최대 디스크 개수의 임시값. 원래는 머리 파츠(로봇) 데이터가 정하는 값이라 " +
             "로봇 모딩(Phase 4)에서 머리 파츠 능력치로 대체된다")]
    [SerializeField] private int discSlotCountPlaceholder = 6;

    public IReadOnlyList<GradeSetting> GradeSettings => gradeSettings;
    public IReadOnlyList<WeaponEntry> WeaponEntries => weaponEntries;
    public IReadOnlyList<DiscData> Discs => discs;
    public int SlotCount => Mathf.Max(1, slotCount);
    public float DiscAppearChance => discAppearChance;
    public int DiscSlotCount => Mathf.Max(0, discSlotCountPlaceholder);

    /// <summary>같은 웨이브 안에서 refreshCount번 새로고침한 뒤의 다음 새로고침 비용.</summary>
    public int GetRefreshCost(int refreshCount)
    {
        return Mathf.Max(0, refreshBaseCost + refreshCostIncrement * Mathf.Max(0, refreshCount));
    }

    /// <summary>해당 등급이 이번 웨이브에 등장할 수 있는지(minWave 조건).</summary>
    public bool IsGradeAvailable(ItemGrade grade, int waveNumber)
    {
        return waveNumber >= GetGradeSetting(grade).minWave;
    }

    /// <summary>등급 설정을 찾는다. 없으면 배율 1의 기본값을 돌려준다.</summary>
    public GradeSetting GetGradeSetting(ItemGrade grade)
    {
        foreach (GradeSetting setting in gradeSettings)
        {
            if (setting.grade == grade) return setting;
        }

        return new GradeSetting { grade = grade, statMultiplier = 1f, priceMultiplier = 1f, weight = 1f, minWave = 1 };
    }

    /// <summary>
    /// 현재 웨이브에서 등장 가능한 등급들 중 가중치로 하나를 뽑는다.
    /// (minWave 조건 때문에 초반에는 낮은 등급만 뽑힌다)
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

#if UNITY_EDITOR
    // WeaponTableGenerator(에디터 전용)가 상점 판매 목록을 통째로 갈아끼우기 위한 진입점.
    public void EditorSetWeaponEntries(List<WeaponEntry> value) => weaponEntries = value;
#endif
}
