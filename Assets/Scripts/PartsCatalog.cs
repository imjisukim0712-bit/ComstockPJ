using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로봇 모딩(팔/다리 파츠)이 사용하는 모든 로컬 데이터를 담는 에셋.
///
/// 무기의 "타입"과 "무게"는 시트 기반 WeaponData(무기 테이블)에 컬럼이 없어서 여기 별도
/// 매핑으로 관리한다(ShopCatalog가 무기 등급·가격을 시트 밖에서 관리하는 것과 같은 이유 -
/// GameDataCsvImporter가 컬럼을 순서로 읽어서 중간에 컬럼을 끼워 넣으면 기존 파싱이 깨진다).
///
/// 무기 소켓 개수·디스크 슬롯 개수도 원래 머리(로봇) 파츠 데이터가 정해야 하는 값인데
/// RobotData(시트)에 그 필드가 없으므로 headModdingInfos에서 로봇ID로 조회한다.
/// 이 에셋 전체가 로컬 전용이며, 어떤 필드도 프로젝트 밖 리소스를 참조하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "PartsCatalog", menuName = "Comstock/파츠 카탈로그")]
public class PartsCatalog : ScriptableObject
{
    /// <summary>weapon_id → 화기 타입/무게. 시트에 없는 값이라 여기서 보강한다.</summary>
    [Serializable]
    public struct WeaponMetaEntry
    {
        public int weaponId;
        public WeaponType type;

        [Tooltip("자기장 코어 + 다리의 weightCapacity 합과 비교되는 무게")]
        public float weight;
    }

    /// <summary>robot_id(머리 파츠) → 모딩 관련 고정값. RobotData(시트)에 해당 컬럼이 없어 여기서 보강한다.</summary>
    [Serializable]
    public struct HeadModdingInfo
    {
        public int robotId;

        [Tooltip("무기 소켓 개수 (PlayerShootManager 인스펙터 설정과 일치해야 한다)")]
        public int weaponSocketCount;

        [Tooltip("장착 가능한 최대 디스크 개수")]
        public int discSlotCount;
    }

    /// <summary>부품 상자 개봉 시 등급 추첨에 쓰는 가중치. ShopCatalog.GradeSetting과 같은 패턴.</summary>
    [Serializable]
    public struct BoxGradeSetting
    {
        public ItemGrade grade;
        public float weight;
        public int minWave;
    }

    [Header("파츠 목록 (부위당 기본 파츠 1개 + 부품 상자로 얻는 파츠들)")]
    [SerializeField] private List<PartData> parts = new List<PartData>();

    [Header("무기 타입/무게 매핑 (전부 밸런스 미확정 임시값)")]
    [SerializeField] private List<WeaponMetaEntry> weaponMeta = new List<WeaponMetaEntry>();

    [Header("로봇(머리)별 모딩 고정값")]
    [SerializeField] private List<HeadModdingInfo> headModdingInfos = new List<HeadModdingInfo>();

    [Header("부품 상자 등급 (전부 밸런스 미확정 임시값)")]
    [SerializeField]
    private List<BoxGradeSetting> boxGradeSettings = new List<BoxGradeSetting>
    {
        new BoxGradeSetting { grade = ItemGrade.Normal,    weight = 50f, minWave = 1 },
        new BoxGradeSetting { grade = ItemGrade.Rare,      weight = 26f, minWave = 1 },
        new BoxGradeSetting { grade = ItemGrade.Epic,      weight = 14f, minWave = 3 },
        new BoxGradeSetting { grade = ItemGrade.Unique,    weight = 7f,  minWave = 5 },
        new BoxGradeSetting { grade = ItemGrade.Legendary, weight = 3f,  minWave = 7 }
    };

    [Header("부품 상자 드랍")]
    [Tooltip("몬스터 처치 시 부품 상자가 나올 확률(0~1). 골드/경험치와 별개로 판정한다")]
    [Range(0f, 1f)]
    [SerializeField] private float partBoxDropChance = 0.05f;

    public IReadOnlyList<PartData> Parts => parts;
    public float PartBoxDropChance => partBoxDropChance;

    /// <summary>해당 슬롯의 기본(런 시작용) 파츠. 없으면 그 슬롯의 첫 파츠, 그마저 없으면 null.</summary>
    public PartData? GetDefaultPart(PartSlot slot)
    {
        PartData? fallback = null;

        foreach (PartData part in parts)
        {
            if (part.slot != slot) continue;
            if (part.isDefaultStarter) return part;
            if (fallback == null) fallback = part;
        }

        return fallback;
    }

    public bool TryGetPart(int partId, out PartData part)
    {
        foreach (PartData p in parts)
        {
            if (p.partId != partId) continue;
            part = p;
            return true;
        }

        part = default;
        return false;
    }

    public bool TryGetWeaponMeta(int weaponId, out WeaponMetaEntry meta)
    {
        foreach (WeaponMetaEntry entry in weaponMeta)
        {
            if (entry.weaponId != weaponId) continue;
            meta = entry;
            return true;
        }

        meta = default;
        return false;
    }

    public HeadModdingInfo GetHeadModdingInfo(int robotId)
    {
        foreach (HeadModdingInfo info in headModdingInfos)
        {
            if (info.robotId == robotId) return info;
        }

        // 데이터가 아직 없는 로봇ID면 안전한 기본값(소켓 2, 디스크 6)으로 폴백
        return new HeadModdingInfo { robotId = robotId, weaponSocketCount = 2, discSlotCount = 6 };
    }

    /// <summary>Phase 3 ShopCatalog와 동일한 가중치 추첨 방식으로 부품 상자 등급을 뽑는다.</summary>
    public ItemGrade RollBoxGrade(int waveNumber)
    {
        float totalWeight = 0f;
        foreach (BoxGradeSetting setting in boxGradeSettings)
        {
            if (waveNumber < setting.minWave) continue;
            totalWeight += Mathf.Max(0f, setting.weight);
        }

        if (totalWeight <= 0f) return ItemGrade.Normal;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        foreach (BoxGradeSetting setting in boxGradeSettings)
        {
            if (waveNumber < setting.minWave) continue;

            roll -= Mathf.Max(0f, setting.weight);
            if (roll <= 0f) return setting.grade;
        }

        return ItemGrade.Normal;
    }

    /// <summary>
    /// 지정한 등급의 슬롯 중 하나를 무작위로 골라, 그 슬롯의 후보 파츠(기본 파츠 제외) 중
    /// 하나를 뽑는다. 그 등급에 맞는 후보가 없으면 슬롯 전체(등급 무관)에서 다시 뽑는다.
    /// </summary>
    public bool TryRollLootPart(ItemGrade grade, out PartData result)
    {
        var slots = new List<PartSlot>((PartSlot[])Enum.GetValues(typeof(PartSlot)));
        PartSlot chosenSlot = slots[UnityEngine.Random.Range(0, slots.Count)];

        var exactGradeCandidates = new List<PartData>();
        var anyGradeCandidates = new List<PartData>();

        foreach (PartData part in parts)
        {
            if (part.slot != chosenSlot || part.isDefaultStarter) continue;

            anyGradeCandidates.Add(part);
            if (part.grade == grade) exactGradeCandidates.Add(part);
        }

        List<PartData> pool = exactGradeCandidates.Count > 0 ? exactGradeCandidates : anyGradeCandidates;

        if (pool.Count == 0)
        {
            result = default;
            return false;
        }

        result = pool[UnityEngine.Random.Range(0, pool.Count)];
        return true;
    }
}
