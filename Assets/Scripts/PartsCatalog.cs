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

        [Tooltip("무기 카테고리(연사/산탄/정밀/폭발/에너지/근접). 2026-08-20 소켓 명세 반영으로 " +
                 "<b>무기 소켓 파츠가 이 값으로 짝을 가린다</b>(불일치면 소켓 보정 없음 + 무게 x2)")]
        public WeaponType type;

        [Tooltip("무기 타입(경무장/중무장/근접무기). 2026-08-20 소켓 명세 교체 이후 <b>소켓 장착 제한에는 " +
                 "쓰이지 않는다</b>(그 역할은 위 type = 무기 카테고리가 맡는다) - 지금은 상점 표시용이다")]
        public WeaponClass weaponClass;

        [Tooltip("자기장 코어 + 다리의 weightCapacity 합과 비교되는 무게")]
        public float weight;
    }

    /// <summary>
    /// robot_id(머리 파츠) → 모딩 관련 고정값 + 머리 고유 정체성. RobotData(시트)에 해당 컬럼이
    /// 없어 여기서 보강한다.
    ///
    /// 2026-08-19 `머리 기획서 Ver04` 반영으로 스프라이트/기본 무기/고유 효과가 추가됐다.
    /// <b>체력·질량 같은 순수 스탯은 여기 넣지 않는다</b> — 그쪽은 계속 GameDataAsset의
    /// robots(RobotData)가 유일한 출처이며, 한 값을 두 에셋에 적어두면 반드시 어긋난다.
    /// 여기 있는 것은 "RobotData에 컬럼이 없어서 둘 곳이 없는" 값들뿐이다.
    /// </summary>
    [Serializable]
    public struct HeadModdingInfo
    {
        public int robotId;

        [Tooltip("무기 소켓 개수 (씬에 리깅된 소켓 개수와 이 값 중 작은 쪽이 실제로 쓰인다 - " +
                 "ModdingManager.ActiveSocketCount)")]
        public int weaponSocketCount;

        [Tooltip("장착 가능한 최대 디스크 개수. DiscSlot 파츠를 장착하면 그 파츠 값이 이 기본값을 대체한다")]
        public int discSlotCount;

        [Tooltip("적재량 - 한 번에 보유할 수 있는 최대 부품 상자 개수. 이 개수에 도달하면 " +
                 "몬스터가 더 이상 부품 상자를 드랍하지 않으며, 정비 화면의 임시 인벤토리 크기도 이 값이다")]
        public int partBoxCapacity;

        [Tooltip("Assets/Resources/Heads/ 아래의 스프라이트 파일명(확장자 제외). " +
                 "이 스프라이트가 인게임에서 로봇의 몸통(=머리)으로 그려지고 UI 아이콘으로도 쓰인다. " +
                 "비어 있으면 기존 리그 기본값(Parts/Body)이 쓰인다")]
        public string spriteName;

        [Tooltip("이 머리로 시작할 때 소켓 0번부터 순서대로 장착되는 기본 무기 ID들. " +
                 "비어 있으면 씬에 저장된 소켓 무기가 그대로 쓰인다")]
        public int[] defaultWeaponIds;

        [Tooltip("이 머리의 고유 효과. 실제 계산은 전부 HeadEffects에 있다")]
        public HeadEffect effect;

        [Tooltip("머리 선택 화면에 노출할지. 디버그용 로봇(미니 컴스톡 등)은 꺼둔다")]
        public bool selectableInHeadSelect;

        [Tooltip("네온아이처럼 스프라이트가 여러 장인 머리의 프레임 수. 0/1이면 단일 이미지. " +
                 "2 이상이면 spriteName 뒤에 _0.._N-1이 붙은 파일을 순환 재생한다")]
        public int spriteFrameCount;

        [Tooltip("스프라이트 프레임 1장이 화면에 머무는 시간(초). spriteFrameCount가 2 이상일 때만 쓰인다")]
        public float spriteFrameSeconds;
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

    [Header("무기 소켓 개별화 - 불일치/과적 패널티 (밸런스 미확정 임시값)")]
    [Tooltip("무기 소켓 파츠가 제한하는 타입과 다른 무기를 끼웠을 때, 그 무기 무게에 곱해지는 배율. " +
             "장착 자체는 항상 허용되고 이 배율만큼 무게만 늘어난다(2026-08-12 사용자 확정)")]
    [SerializeField] private float mismatchWeightMultiplier = 2.0f;

    [Tooltip("총 무게가 지탱력(자기장 코어+다리)을 초과했을 때, 초과분 1당 이동속도가 깎이는 양. " +
             "지탱력 초과는 더 이상 장착을 막지 않고(하드 캡 제거) 이 감속으로만 반영된다")]
    [SerializeField] private float overweightSpeedPenaltyPerUnit = 0.05f;

    public IReadOnlyList<PartData> Parts => parts;
    public float PartBoxDropChance => partBoxDropChance;
    public float MismatchWeightMultiplier => mismatchWeightMultiplier > 0f ? mismatchWeightMultiplier : 1f;
    public float OverweightSpeedPenaltyPerUnit => Mathf.Max(0f, overweightSpeedPenaltyPerUnit);

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

        // 데이터가 아직 없는 로봇ID면 안전한 기본값으로 폴백.
        // 2026-08-18 사용자 지정: "모든 로봇의 무기 기본 최대치 4개(더 적을 수도 있으나 특별한
        // 경우)" - 기본값을 4로 올렸다. 예외적으로 소켓이 더 적은 로봇은 headModdingInfos에
        // 그 로봇 전용 항목을 등록해서 이 폴백을 우회하면 된다.
        return new HeadModdingInfo
        {
            robotId = robotId,
            weaponSocketCount = 4,
            discSlotCount = 6,
            partBoxCapacity = DefaultPartBoxCapacity,
            spriteName = null,                  // null이면 리그가 기존 Parts/Body를 그대로 쓴다
            defaultWeaponIds = null,            // null이면 씬에 저장된 소켓 무기를 그대로 쓴다
            effect = HeadEffect.None,
            selectableInHeadSelect = false,      // 데이터가 없는 로봇을 선택 화면에 띄우지 않는다
            spriteFrameCount = 0,
            spriteFrameSeconds = 0f
        };
    }

    /// <summary>
    /// 머리 선택 화면에 띄울 머리 목록(<see cref="HeadModdingInfo.selectableInHeadSelect"/>가 켜진 것).
    /// 등록 순서를 그대로 유지하므로 에셋에서의 순서가 곧 화면 배치 순서다.
    /// </summary>
    public List<HeadModdingInfo> GetSelectableHeads()
    {
        var result = new List<HeadModdingInfo>();

        foreach (HeadModdingInfo entry in headModdingInfos)
        {
            if (!entry.selectableInHeadSelect) continue;

            HeadModdingInfo info = entry;
            if (info.partBoxCapacity <= 0) info.partBoxCapacity = DefaultPartBoxCapacity;
            result.Add(info);
        }

        return result;
    }

    /// <summary>Phase 3 ShopCatalog와 동일한 가중치 추첨 방식으로 부품 상자 등급을 뽑는다.</summary>
    public ItemGrade RollBoxGrade(int waveNumber)
    {
        // 상점과 같은 행운 보정을 쓴다(LuckBonus 참고, 2026-08-19 신설).
        float totalWeight = 0f;
        foreach (BoxGradeSetting setting in boxGradeSettings)
        {
            if (waveNumber < setting.minWave) continue;
            totalWeight += Mathf.Max(0f, setting.weight) * LuckBonus.WeightMultiplier(setting.grade);
        }

        if (totalWeight <= 0f) return ItemGrade.Normal;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        foreach (BoxGradeSetting setting in boxGradeSettings)
        {
            if (waveNumber < setting.minWave) continue;

            roll -= Mathf.Max(0f, setting.weight) * LuckBonus.WeightMultiplier(setting.grade);
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

#if UNITY_EDITOR
    // WeaponTableGenerator(에디터 전용)가 목록을 통째로 갈아끼우기 위한 진입점.
    // 리플렉션 대신 명시적 세터를 두는 이유: 필드 이름이 바뀌면 컴파일 에러로 바로 드러나고,
    // #if UNITY_EDITOR 덕분에 빌드에는 아예 포함되지 않는다.
    public void EditorSetWeaponMeta(List<WeaponMetaEntry> value) => weaponMeta = value;
    public List<PartData> EditorGetParts() => parts;
    public void EditorSetParts(List<PartData> value) => parts = value;
#endif
}
