using UnityEngine;

/// <summary>
/// 로봇 정비(팔/다리 파츠 모딩)를 담당한다.
/// - 런 시작 시 6개 슬롯에 기본 파츠를 채운다.
/// - 부품 상자 개봉(등급 추첨 → 파츠 추첨 → 즉시 장착).
/// - 무기 소켓의 타입 제한, 자기장 코어+다리의 무게 제한 검증(ShopManager가 무기 구매 시 이 검증을 통과해야 한다).
/// </summary>
public class ModdingManager : MonoBehaviour
{
    [SerializeField] private PartsCatalog catalog;

    public PartsCatalog Catalog => catalog;

    // RunState.Reset()은 PlayerRobotController.Awake()에서 호출된다. Unity는 같은 프레임의
    // 모든 Awake가 끝난 뒤에 Start를 호출하는 것을 보장하므로, 여기서는 Awake가 아니라
    // Start에서 기본값을 채워야 Reset()이 먼저 실행됨을 안전하게 보장할 수 있다
    // (AiCoreManager의 OnChanged 구독 순서 버그와 같은 종류의 함정 - 작업.md Phase 2 참고).
    private void Start()
    {
        EnsureDefaultPartsEquipped();
    }

    /// <summary>6개 슬롯 중 아직 아무것도 장착되지 않은 슬롯에 기본 파츠를 채운다.</summary>
    public void EnsureDefaultPartsEquipped()
    {
        if (catalog == null)
        {
            Debug.LogWarning("ModdingManager에 PartsCatalog가 연결되지 않았습니다.");
            return;
        }

        bool changed = false;

        foreach (PartSlot slot in (PartSlot[])System.Enum.GetValues(typeof(PartSlot)))
        {
            string key = slot.ToString();
            if (RunState.EquippedPartIds.ContainsKey(key)) continue;

            PartData? defaultPart = catalog.GetDefaultPart(slot);
            if (defaultPart == null)
            {
                Debug.LogWarning($"PartsCatalog에 {slot.ToKorean()} 슬롯의 기본 파츠가 없습니다.");
                continue;
            }

            RunState.EquippedPartIds[key] = defaultPart.Value.partId;
            changed = true;
        }

        if (changed) RecomputePartStatBonuses();
    }

    public bool TryGetEquippedPart(PartSlot slot, out PartData part)
    {
        part = default;

        if (catalog == null) return false;
        if (!RunState.EquippedPartIds.TryGetValue(slot.ToString(), out int id)) return false;

        return catalog.TryGetPart(id, out part);
    }

    /// <summary>자기장 코어 + 다리의 weightCapacity 합 = 무기 무게를 지탱할 수 있는 총량.</summary>
    public float GetTotalWeightCapacity()
    {
        float total = 0f;
        if (TryGetEquippedPart(PartSlot.MagneticCore, out PartData core)) total += core.weightCapacity;
        if (TryGetEquippedPart(PartSlot.Leg, out PartData leg)) total += leg.weightCapacity;
        return total;
    }

    public float GetWeaponWeight(int weaponId)
    {
        return catalog != null && catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta) ? meta.weight : 0f;
    }

    /// <summary>
    /// 현재 장착된 모든 무기의 무게 합. excludeSocketIndex를 주면 그 소켓은 제외한다 -
    /// 같은 소켓에 새 무기를 넣을 때 "그 소켓의 기존 무게를 빼고 새 무게를 더해서" 비교하기 위함.
    /// </summary>
    public float GetEquippedWeaponWeightSum(int excludeSocketIndex = -1)
    {
        float sum = 0f;
        for (int i = 0; i < RunState.EquippedWeapons.Count; i++)
        {
            if (i == excludeSocketIndex) continue;
            sum += GetWeaponWeight(RunState.EquippedWeapons[i].WeaponId);
        }
        return sum;
    }

    /// <summary>이 소켓에 이 무기를 넣었을 때 무게 제한(자기장 코어+다리)을 넘지 않는지 확인한다.</summary>
    public bool CheckWeightLimit(int socketIndex, int newWeaponId, out float totalAfter, out float capacity)
    {
        capacity = GetTotalWeightCapacity();
        totalAfter = GetEquippedWeaponWeightSum(socketIndex) + GetWeaponWeight(newWeaponId);
        return totalAfter <= capacity;
    }

    /// <summary>무기 소켓 파츠의 타입 제한과 이 무기의 타입이 맞는지 확인한다.</summary>
    public bool CheckWeaponTypeAllowed(int weaponId, out string reason)
    {
        reason = string.Empty;

        // 기본 파츠(무기 타입 제한 없음)이거나 파츠 정보를 못 찾으면 통과시킨다.
        if (!TryGetEquippedPart(PartSlot.ArmWeaponSocket, out PartData socketPart) || !socketPart.restrictsWeaponType)
            return true;

        // 이 무기의 타입 정보가 카탈로그에 없으면(데이터 누락) 상점이 막히지 않도록 통과시킨다.
        if (catalog == null || !catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta))
            return true;

        if (meta.type == socketPart.allowedWeaponType) return true;

        reason = $"이 소켓은 {socketPart.allowedWeaponType.ToKorean()}만 장착할 수 있습니다 (현재: {meta.type.ToKorean()})";
        return false;
    }

    /// <summary>부품 상자를 하나 연다. 성공하면 등급 추첨 → 파츠 추첨 → 즉시 장착까지 마친다.</summary>
    public bool TryOpenBox(out PartData resultPart)
    {
        resultPart = default;

        if (catalog == null || RunState.UnopenedPartBoxCount <= 0) return false;

        ItemGrade grade = catalog.RollBoxGrade(Mathf.Max(1, RunState.WaveNumber));
        if (!catalog.TryRollLootPart(grade, out resultPart))
        {
            Debug.LogWarning("PartsCatalog에 부품 상자로 뽑을 파츠가 없습니다.");
            return false;
        }

        RunState.UnopenedPartBoxCount--;
        EquipPart(resultPart);
        return true;
    }

    /// <summary>파츠를 해당 슬롯에 장착(교체)한다. 같은 슬롯의 이전 파츠 보너스는 사라진다.</summary>
    public void EquipPart(PartData part)
    {
        RunState.EquippedPartIds[part.slot.ToString()] = part.partId;
        RecomputePartStatBonuses();
        RunState.NotifyChanged();
    }

    // 디스크(RunState.DiscStatBonuses)는 구매할 때마다 누적만 하면 되지만, 파츠는 같은
    // 슬롯을 교체하면 이전 파츠의 보너스가 사라져야 하므로 매번 전체를 다시 계산한다.
    private void RecomputePartStatBonuses()
    {
        RunState.PartStatBonuses.Clear();

        foreach (var kv in RunState.EquippedPartIds)
        {
            if (!catalog.TryGetPart(kv.Value, out PartData part)) continue;
            if (part.bonusAmount == 0f) continue;

            if (!RunState.PartStatBonuses.ContainsKey(part.bonusStat)) RunState.PartStatBonuses[part.bonusStat] = 0f;
            RunState.PartStatBonuses[part.bonusStat] += part.bonusAmount;
        }
    }
}
