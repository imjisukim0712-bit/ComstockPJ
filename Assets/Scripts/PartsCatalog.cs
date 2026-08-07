using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로봇 모딩(팔/다리 파츠)이 사용하는 모든 로컬 데이터를 담는 에셋.
///
/// 무기의 "타입"과 "무게"는 WeaponData(무기 테이블)에 컬럼이 없어서 여기 별도 매핑으로
/// 관리한다(ShopCatalog가 무기 등급·가격을 따로 관리하는 것과 같은 이유).
///
/// 무기 소켓 개수·디스크 슬롯 개수도 원래 머리(로봇) 파츠 데이터가 정해야 하는 값인데
/// RobotData에 그 필드가 없으므로 headModdingInfos에서 로봇ID로 조회한다.
/// 이 에셋 전체가 로컬 전용이며, 어떤 필드도 프로젝트 밖 리소스를 참조하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "PartsCatalog", menuName = "Comstock/파츠 카탈로그")]
public class PartsCatalog : ScriptableObject
{
    /// <summary>weapon_id → 무기 타입/투사체 타입/무게. 시트에 없는 값이라 여기서 보강한다.</summary>
    [Serializable]
    public struct WeaponMetaEntry
    {
        public int weaponId;

        [Tooltip("투사체 타입(연사/산탄/정밀/폭발/에너지/근접) - 공격 방식의 분류. " +
                 "소켓 장착 제한과는 무관하다")]
        public WeaponType type;

        [Tooltip("무기 타입(경무장/중무장/근접무기) - 무기 소켓 파츠가 이 값으로 장착 가능 여부를 가른다")]
        public WeaponClass weaponClass;

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

        [Tooltip("장착 가능한 최대 디스크 개수. DiscSlot 파츠를 장착하면 그 파츠 값이 이 기본값을 대체한다")]
        public int discSlotCount;

        [Tooltip("적재량 - 한 번에 보유할 수 있는 최대 부품 상자 개수. 이 개수에 도달하면 " +
                 "몬스터가 더 이상 부품 상자를 드랍하지 않으며, 정비 화면의 임시 인벤토리 크기도 이 값이다")]
        public int partBoxCapacity;
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

    /// <summary>적재량 데이터가 아직 없는 로봇에 쓰는 안전한 기본 상자 보유 상한.</summary>
    public const int DefaultPartBoxCapacity = 20;

    public HeadModdingInfo GetHeadModdingInfo(int robotId)
    {
        foreach (HeadModdingInfo entry in headModdingInfos)
        {
            if (entry.robotId != robotId) continue;

            // struct라 여기서 복사본이 만들어진다(foreach 변수 자체는 수정할 수 없다).
            // 적재량이 0인 항목은 이 필드가 없던 시절에 저장된 데이터라, 그대로 두면 상자를
            // 하나도 못 얻는 상태가 되므로 안전한 기본값으로 보정해서 돌려준다.
            HeadModdingInfo info = entry;
            if (info.partBoxCapacity <= 0) info.partBoxCapacity = DefaultPartBoxCapacity;
            return info;
        }

        // 데이터가 아직 없는 로봇ID면 안전한 기본값으로 폴백
        return new HeadModdingInfo
        {
            robotId = robotId,
            weaponSocketCount = 2,
            discSlotCount = 6,
            partBoxCapacity = DefaultPartBoxCapacity
        };
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
    /// 추첨된 등급에 해당하는 루트 파츠(기본 파츠 제외) 전체에서 하나를 뽑는다.
    ///
    /// 파츠는 종류마다 5개 등급이 전부 존재하는 형식이므로(예: 무쇠다리 = 일반/희귀/서사/유일/전설)
    /// 등급을 먼저 확정해도 후보가 비지 않는다. 예전에는 슬롯을 먼저 무작위로 고른 뒤 그 슬롯에
    /// 해당 등급이 없으면 등급 무관하게 뽑는 폴백을 탔는데, 슬롯당 파츠가 몇 개 없어서 이 폴백이
    /// 거의 항상 걸렸고 그 결과 boxGradeSettings의 minWave가 무시돼 웨이브 1에도 전설이 나왔다.
    ///
    /// 그래도 데이터가 아직 채워지지 않은 등급을 만날 수 있으므로, 후보가 없으면 <b>하위</b>
    /// 등급으로 내려가며 찾는다(상위로 올라가면 minWave 게이팅이 다시 깨진다).
    /// </summary>
    public bool TryRollLootPart(ItemGrade grade, out PartData result)
    {
        var candidates = new List<PartData>();

        for (int g = (int)grade; g >= 0; g--)
        {
            candidates.Clear();

            foreach (PartData part in parts)
            {
                if (part.isDefaultStarter) continue;
                if ((int)part.grade != g) continue;
                candidates.Add(part);
            }

            if (candidates.Count > 0)
            {
                result = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                return true;
            }
        }

        result = default;
        return false;
    }
}
