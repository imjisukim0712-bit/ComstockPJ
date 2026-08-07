using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로봇 정비(파츠 모딩)를 담당한다.
/// - 런 시작 시 모든 모딩 슬롯에 기본 파츠를 채운다.
/// - 부품 상자는 정비 화면에 들어갈 때 <b>전부 자동으로 개봉</b>되어 임시 인벤토리에 적재된다
///   (예전에는 플레이어가 버튼으로 하나씩 열었고, 열자마자 즉시 장착됐다).
/// - 교체는 인벤토리 ↔ 슬롯 <b>맞교환</b>이다. 슬롯에서 빠진 파츠는 인벤토리로 들어간다.
/// - 임시 인벤토리는 정비 화면을 닫을 때 통째로 비워진다(사용자 확정 사항).
/// - 무기 소켓의 타입 제한, 자기장 코어+다리의 무게 제한 검증(ShopManager가 무기 구매 시 이 검증을 통과해야 한다).
/// </summary>
public class ModdingManager : MonoBehaviour
{
    [SerializeField] private PartsCatalog catalog;

    public PartsCatalog Catalog => catalog;

    /// <summary>
    /// 씬에 하나만 존재하는 정비 매니저. RewardPickup처럼 상자 적재량 상한을 물어봐야 하는 쪽이
    /// 매번 FindFirstObjectByType을 돌리지 않도록 노출한다.
    /// </summary>
    public static ModdingManager Instance { get; private set; }

    // 적재량/디스크 슬롯 수는 머리(로봇) 능력치라 로봇 ID가 필요한데, 이 조회가 몬스터 처치마다
    // 일어나므로 플레이어 참조를 캐시해둔다.
    private PlayerRobotController player_cache;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // RunState.Reset()은 PlayerRobotController.Awake()에서 호출된다. Unity는 같은 프레임의
    // 모든 Awake가 끝난 뒤에 Start를 호출하는 것을 보장하므로, 여기서는 Awake가 아니라
    // Start에서 기본값을 채워야 Reset()이 먼저 실행됨을 안전하게 보장할 수 있다
    // (AiCoreManager의 OnChanged 구독 순서 버그와 같은 종류의 함정 - 작업.md Phase 2 참고).
    private void Start()
    {
        EnsureDefaultPartsEquipped();
    }

    /// <summary>모딩 슬롯 중 아직 아무것도 장착되지 않은 슬롯에 기본 파츠를 채운다.</summary>
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

    // ---------------------------------------------------------------------
    // 머리(로봇) 능력치 조회
    // ---------------------------------------------------------------------

    private PartsCatalog.HeadModdingInfo GetHeadInfo()
    {
        if (catalog == null)
        {
            return new PartsCatalog.HeadModdingInfo
            {
                weaponSocketCount = 2,
                discSlotCount = 6,
                partBoxCapacity = PartsCatalog.DefaultPartBoxCapacity
            };
        }

        if (player_cache == null) player_cache = FindFirstObjectByType<PlayerRobotController>();
        return catalog.GetHeadModdingInfo(player_cache != null ? player_cache.RobotId : -1);
    }

    /// <summary>
    /// 적재량 - 한 번에 보유 가능한 최대 부품 상자 개수이자 정비 임시 인벤토리의 크기.
    /// 머리(로봇) 능력치이므로 로봇마다 다르다.
    /// </summary>
    public int PartBoxCapacity => Mathf.Max(0, GetHeadInfo().partBoxCapacity);

    /// <summary>
    /// 장착 가능한 최대 디스크 개수. DiscSlot 파츠를 끼웠으면 그 파츠 값이 우선하고,
    /// 없으면 머리(로봇) 기본값을 쓴다.
    /// </summary>
    public int DiscSlotCount
    {
        get
        {
            if (TryGetEquippedPart(PartSlot.DiscSlot, out PartData discSlotPart) && discSlotPart.discSlotCount > 0)
                return discSlotPart.discSlotCount;

            return Mathf.Max(0, GetHeadInfo().discSlotCount);
        }
    }

    // ---------------------------------------------------------------------
    // 부품 상자 (적재량 상한이 있다)
    // ---------------------------------------------------------------------

    /// <summary>부품 상자를 더 받을 수 있는지. EnemyUnit이 드랍하기 전에 확인한다.</summary>
    public bool CanReceiveMorePartBoxes => RunState.UnopenedPartBoxCount < PartBoxCapacity;

    /// <summary>
    /// 부품 상자를 amount개 지급하되 적재량 상한을 넘지 않도록 자르고, 실제 지급된 개수를 돌려준다.
    /// 드랍 시점에도 상한을 검사하지만, 여러 몬스터가 거의 동시에 죽으면 둘 다 검사를 통과한 뒤
    /// 습득 시점에 상한을 넘을 수 있어서 여기서 한 번 더 막는다.
    /// </summary>
    public int AddPartBoxes(int amount)
    {
        int granted = Mathf.Clamp(PartBoxCapacity - RunState.UnopenedPartBoxCount, 0, Mathf.Max(0, amount));
        RunState.UnopenedPartBoxCount += granted;
        return granted;
    }

    // ---------------------------------------------------------------------
    // 임시 인벤토리 (정비 화면 동안만 존재)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 보유한 부품 상자를 <b>전부</b> 개봉해 임시 인벤토리에 적재한다.
    /// 정비 화면에 들어갈 때 ModdingPanelUI가 한 번 호출한다(플레이어가 하나씩 열지 않는다).
    /// 개봉된 파츠는 자동 장착되지 않는다 - 무엇을 낄지는 플레이어가 인벤토리에서 고른다.
    /// </summary>
    /// <returns>이번에 개봉된 개수</returns>
    public int OpenAllBoxesIntoInventory()
    {
        if (catalog == null) return 0;

        int opened = 0;
        int wave = Mathf.Max(1, RunState.WaveNumber);

        while (RunState.UnopenedPartBoxCount > 0)
        {
            ItemGrade grade = catalog.RollBoxGrade(wave);
            if (!catalog.TryRollLootPart(grade, out PartData part))
            {
                // 뽑을 파츠가 하나도 없는 데이터 상태다. 계속 돌면 무한 루프이므로 중단한다
                // (상자는 소모하지 않고 남겨둬 다음 정비 때 다시 시도할 수 있게 한다).
                Debug.LogWarning("PartsCatalog에 부품 상자로 뽑을 파츠가 없어 개봉을 중단했습니다.");
                break;
            }

            RunState.UnopenedPartBoxCount--;
            RunState.ModdingInventory.Add(part.partId);
            opened++;
        }

        if (opened > 0) RunState.NotifyChanged();
        return opened;
    }

    /// <summary>임시 인벤토리에 담긴 파츠들(표시용). 카탈로그에서 찾지 못한 ID는 건너뛴다.</summary>
    public List<PartData> GetInventoryParts()
    {
        var result = new List<PartData>();
        if (catalog == null) return result;

        foreach (int partId in RunState.ModdingInventory)
        {
            if (catalog.TryGetPart(partId, out PartData part)) result.Add(part);
        }

        return result;
    }

    /// <summary>
    /// 인벤토리의 index번째 파츠를 장착할 수 있는 슬롯. 파츠마다 슬롯이 정해져 있으므로
    /// 해당 슬롯 하나만 활성화된다(정비 화면이 이 값으로 노란색 강조를 켠다).
    /// </summary>
    public bool TryGetEquipableSlot(int inventoryIndex, out PartSlot slot)
    {
        slot = default;

        if (catalog == null) return false;
        if (inventoryIndex < 0 || inventoryIndex >= RunState.ModdingInventory.Count) return false;
        if (!catalog.TryGetPart(RunState.ModdingInventory[inventoryIndex], out PartData part)) return false;

        slot = part.slot;
        return true;
    }

    /// <summary>
    /// 인벤토리의 파츠와 슬롯에 장착된 파츠를 <b>맞교환</b>한다.
    /// 슬롯에서 빠진 기존 파츠는 인벤토리의 같은 자리로 들어가므로 되돌리는 것도 가능하다
    /// (단, 정비 화면을 닫으면 인벤토리에 남은 것은 전부 사라진다).
    ///
    /// 파츠에도 무게가 있으므로(사용자 확정 사항) <b>무게 제한은 여기서도 검사한다</b>.
    /// 무기 타입 제한만 소급 검증하지 않는다 - 그쪽은 "무기를 소켓에 넣는" 상점 구매 시점에만 적용한다.
    /// </summary>
    public bool TrySwapInventoryWithSlot(int inventoryIndex, PartSlot slot) =>
        TrySwapInventoryWithSlot(inventoryIndex, slot, out _);

    /// <summary>교체에 실패하면 reason에 이유가 담긴다(정비 화면이 그대로 보여준다).</summary>
    public bool TrySwapInventoryWithSlot(int inventoryIndex, PartSlot slot, out string reason)
    {
        reason = string.Empty;

        if (catalog == null) return false;
        if (inventoryIndex < 0 || inventoryIndex >= RunState.ModdingInventory.Count) return false;

        int incomingId = RunState.ModdingInventory[inventoryIndex];
        if (!catalog.TryGetPart(incomingId, out PartData incoming)) return false;

        // 파츠는 자기 슬롯에만 들어간다.
        if (incoming.slot != slot) return false;

        // 무거운 파츠로 갈아끼우면 지탱력을 넘길 수 있다.
        // (자기장 코어/다리를 교체하는 경우엔 지탱력 자체도 함께 바뀐다 - CheckPartWeightLimit 참고)
        if (!CheckPartWeightLimit(incoming, out float totalAfter, out float capacity))
        {
            reason = $"무게 초과 ({totalAfter:0.#} / {capacity:0.#})";
            return false;
        }

        string key = slot.ToString();
        bool hadPrevious = RunState.EquippedPartIds.TryGetValue(key, out int previousId);

        RunState.EquippedPartIds[key] = incomingId;

        if (hadPrevious)
        {
            // 맞교환: 빠진 파츠가 인벤토리의 그 자리를 차지한다.
            RunState.ModdingInventory[inventoryIndex] = previousId;
        }
        else
        {
            RunState.ModdingInventory.RemoveAt(inventoryIndex);
        }

        RecomputePartStatBonuses();
        RunState.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 임시 인벤토리를 비운다. 정비 화면을 닫을 때(정비 완료) 호출한다 -
    /// 획득했지만 장착하지 않은 파츠는 전부 사라진다(사용자 확정 사항).
    /// </summary>
    public void ClearInventory()
    {
        if (RunState.ModdingInventory.Count == 0) return;

        RunState.ModdingInventory.Clear();
        RunState.NotifyChanged();
    }

    // ---------------------------------------------------------------------
    // 무게 / 무기 타입 제약 (상점 구매 시 ShopManager가 사용)
    // ---------------------------------------------------------------------

    /// <summary>자기장 코어 + 다리의 weightCapacity 합 = 무게를 지탱할 수 있는 총량.</summary>
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

    /// <summary>
    /// 장착된 <b>파츠</b>들의 무게 합(디스크는 무게가 없다). excludeSlot을 주면 그 슬롯은 빼고 센다 -
    /// 그 슬롯을 다른 파츠로 교체했을 때의 무게를 계산하기 위함이다.
    /// </summary>
    public float GetEquippedPartsWeightSum(PartSlot? excludeSlot = null)
    {
        float sum = 0f;

        foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
        {
            if (excludeSlot.HasValue && slot == excludeSlot.Value) continue;
            if (TryGetEquippedPart(slot, out PartData part)) sum += part.weight;
        }

        return sum;
    }

    /// <summary>무기 + 파츠를 전부 합친 현재 총 무게.</summary>
    public float GetTotalWeight() => GetEquippedWeaponWeightSum() + GetEquippedPartsWeightSum();

    /// <summary>이 소켓에 이 무기를 넣었을 때 무게 제한(자기장 코어+다리)을 넘지 않는지 확인한다.</summary>
    public bool CheckWeightLimit(int socketIndex, int newWeaponId, out float totalAfter, out float capacity)
    {
        capacity = GetTotalWeightCapacity();
        totalAfter = GetEquippedWeaponWeightSum(socketIndex) + GetWeaponWeight(newWeaponId) + GetEquippedPartsWeightSum();
        return totalAfter <= capacity;
    }

    /// <summary>
    /// 이 파츠로 교체했을 때 무게 제한을 넘지 않는지 확인한다.
    /// 파츠에도 무게가 생겼기 때문에(사용자 확정 사항) 파츠 교체도 무기 구매와 같은 검사를 받는다.
    ///
    /// 주의: 교체하려는 파츠가 자기장 코어/다리면 지탱력 자체가 바뀌므로,
    /// 새 파츠의 weightCapacity를 반영한 지탱력과 비교해야 한다.
    /// </summary>
    public bool CheckPartWeightLimit(PartData newPart, out float totalAfter, out float capacity)
    {
        totalAfter = GetEquippedWeaponWeightSum() + GetEquippedPartsWeightSum(newPart.slot) + newPart.weight;

        capacity = 0f;
        capacity += GetCapacityAfterSwap(PartSlot.MagneticCore, newPart);
        capacity += GetCapacityAfterSwap(PartSlot.Leg, newPart);

        return totalAfter <= capacity;
    }

    // 해당 슬롯의 지탱력을 구하되, 교체 대상이 바로 그 슬롯이면 새 파츠의 값을 쓴다.
    private float GetCapacityAfterSwap(PartSlot slot, PartData newPart)
    {
        if (newPart.slot == slot) return newPart.weightCapacity;
        return TryGetEquippedPart(slot, out PartData current) ? current.weightCapacity : 0f;
    }

    /// <summary>
    /// 무기 소켓 파츠의 <b>무기 타입</b>(경무장/중무장/근접무기) 제한과 이 무기가 맞는지 확인한다.
    /// 투사체 타입(연사/산탄/정밀 등)은 이 검사와 무관하다 - 소켓은 무기 타입만 제한한다(사용자 확정 사항).
    /// </summary>
    public bool CheckWeaponTypeAllowed(int weaponId, out string reason)
    {
        reason = string.Empty;

        // 기본 파츠(무기 타입 제한 없음)이거나 파츠 정보를 못 찾으면 통과시킨다.
        if (!TryGetEquippedPart(PartSlot.ArmWeaponSocket, out PartData socketPart) || !socketPart.restrictsWeaponClass)
            return true;

        // 이 무기의 타입 정보가 카탈로그에 없으면(데이터 누락) 상점이 막히지 않도록 통과시킨다.
        if (catalog == null || !catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta))
            return true;

        if (meta.weaponClass == socketPart.allowedWeaponClass) return true;

        reason = $"이 소켓은 {socketPart.allowedWeaponClass.ToKorean()}만 장착할 수 있습니다 (현재: {meta.weaponClass.ToKorean()})";
        return false;
    }

    /// <summary>
    /// 현재 장착된 무기 소켓 파츠의 등급 효과 배율. 소켓 파츠가 없으면 전부 1배를 돌려준다.
    /// PlayerShootManager가 매 프레임 조회하므로 값만 넘기는 가벼운 구조체로 반환한다.
    /// </summary>
    public struct SocketModifiers
    {
        public float Range;        // 탄이 날아가는 최대 거리 배율
        public float DetectRange;  // 적을 감지해 발사를 시작하는 거리 배율
        public float RotationSpeed; // 공격 방향 회전 속도 배율

        public static SocketModifiers Identity => new SocketModifiers { Range = 1f, DetectRange = 1f, RotationSpeed = 1f };
    }

    public SocketModifiers GetWeaponSocketModifiers()
    {
        if (!TryGetEquippedPart(PartSlot.ArmWeaponSocket, out PartData socketPart)) return SocketModifiers.Identity;

        return new SocketModifiers
        {
            Range = socketPart.RangeMultiplier,
            DetectRange = socketPart.DetectRangeMultiplier,
            RotationSpeed = socketPart.RotationSpeedMultiplier
        };
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
