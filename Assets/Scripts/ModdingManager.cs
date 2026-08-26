using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로봇 정비(파츠 모딩)를 담당한다.
/// - 런 시작 시 모든 모딩 슬롯에 기본 파츠를 채운다.
/// - 부품 상자는 정비 화면에 들어갈 때 <b>전부 자동으로 개봉</b>되어 임시 인벤토리에 적재된다
///   (예전에는 플레이어가 버튼으로 하나씩 열었고, 열자마자 즉시 장착됐다).
/// - 교체는 인벤토리 ↔ 슬롯 <b>맞교환</b>이다. 슬롯에서 빠진 파츠는 인벤토리로 들어간다.
/// - 임시 인벤토리는 정비 화면을 닫을 때 통째로 비워진다(사용자 확정 사항).
/// - 무기 소켓의 타입 제한, 다리의 무게 제한 검증(ShopManager가 무기 구매 시 이 검증을 통과해야 한다).
///   (2026-08-26 자기장 코어 삭제 전에는 "코어+다리"의 합이었다 - PartSlot.cs 참고)
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

    // ActiveSocketCount가 매 프레임(PlayerShootManager.Update)에서 조회되므로 캐시해둔다.
    private PlayerShootManager shoot_cache;

    private void Awake()
    {
        Instance = this;

        // 머리(로봇) 고유 효과가 무기 분류(WeaponType)와 머리 데이터를 조회할 수 있도록
        // 카탈로그를 넘겨준다. HeadEffects는 static이라 씬을 다시 시작하면 이전 판의 참조가
        // 남으므로 여기서 매번 다시 연결한다(2026-08-19).
        HeadEffects.Bind(catalog);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (subscribed_wave_manager != null)
        {
            subscribed_wave_manager.OnWaveStarted -= HandleWaveStartedForPartBox;
            subscribed_wave_manager.OnWaveEnded -= HandleWaveEndedForPartBox;
            subscribed_wave_manager = null;
        }
    }

    // RunState.Reset()은 PlayerRobotController.Awake()에서 호출된다. Unity는 같은 프레임의
    // 모든 Awake가 끝난 뒤에 Start를 호출하는 것을 보장하므로, 여기서는 Awake가 아니라
    // Start에서 기본값을 채워야 Reset()이 먼저 실행됨을 안전하게 보장할 수 있다
    // (AiCoreManager의 OnChanged 구독 순서 버그와 같은 종류의 함정 - 작업.md Phase 2 참고).
    private void Start()
    {
        EnsureDefaultPartsEquipped();

        // 부품 상자 최소 보장(2026-08-24)에 쓰는 웨이브 시작/종료 구독. Awake가 아니라 Start에
        // 두는 이유는 위와 같다(다른 오브젝트의 Awake 완료를 보장받아야 WaveManager를 찾을 수 있다).
        EnsureWaveSubscription();
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
            // 무기 소켓은 슬롯 하나가 아니라 소켓 인덱스별로 채워야 하므로 아래에서 따로 처리한다.
            if (slot == PartSlot.ArmWeaponSocket) continue;

            // 2026-08-18 "메모리 추가 / 발 삭제" - Foot은 더 이상 장착 대상이 아니다(PartsCatalog에
            // 이 슬롯의 파츠 데이터 자체가 없다). 건너뛰지 않으면 매 런마다
            // "PartsCatalog에 발 슬롯의 기본 파츠가 없습니다" 경고만 찍힌다.
            if (slot == PartSlot.Foot) continue;

            // 2026-08-26 팔장갑/자기장 코어 시스템 삭제 - 위와 같은 이유로 건너뛴다.
            if (slot == PartSlot.ArmArmor || slot == PartSlot.MagneticCore) continue;

            string key = slot.ToString();
            if (RunState.EquippedPartIds.ContainsKey(key)) continue;

            PartData? defaultPart = catalog.GetDefaultPart(slot);
            if (defaultPart == null)
            {
                Debug.LogWarning($"PartsCatalog에 {slot.ToDisplayName()} 슬롯의 기본 파츠가 없습니다.");
                continue;
            }

            RunState.EquippedPartIds[key] = defaultPart.Value.partId;
            changed = true;
        }

        PartData? defaultSocketPart = catalog.GetDefaultPart(PartSlot.ArmWeaponSocket);
        if (defaultSocketPart == null)
        {
            Debug.LogWarning("PartsCatalog에 무기 소켓 슬롯의 기본 파츠가 없습니다.");
        }
        else
        {
            for (int i = 0; i < ActiveSocketCount; i++)
            {
                if (RunState.EquippedWeaponSocketPartIds.ContainsKey(i)) continue;
                RunState.EquippedWeaponSocketPartIds[i] = defaultSocketPart.Value.partId;
                changed = true;
            }
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

    /// <summary>
    /// socketIndex번 무기 소켓에 장착된 파츠(종류 + 등급 배율)를 가져온다. 이 소켓에 아직
    /// 아무것도 장착하지 않았으면(EnsureDefaultPartsEquipped가 아직 못 채운 새 소켓 등) 카탈로그의
    /// 기본 파츠(표준 소켓, 타입 제한 없음)로 안전하게 폴백한다.
    /// </summary>
    public bool TryGetEquippedWeaponSocketPart(int socketIndex, out PartData part)
    {
        part = default;
        if (catalog == null) return false;

        if (RunState.EquippedWeaponSocketPartIds.TryGetValue(socketIndex, out int id) && catalog.TryGetPart(id, out part))
            return true;

        PartData? defaultPart = catalog.GetDefaultPart(PartSlot.ArmWeaponSocket);
        if (defaultPart == null) return false;

        part = defaultPart.Value;
        return true;
    }

    // ---------------------------------------------------------------------
    // 머리(로봇) 능력치 조회
    // ---------------------------------------------------------------------

    private PartsCatalog.HeadModdingInfo GetHeadInfo()
    {
        if (catalog == null)
        {
            // 2026-08-18: 모든 로봇의 무기 기본 최대치를 4로 올렸다(PartsCatalog.GetHeadModdingInfo와
            // 동일한 폴백값 - 카탈로그 자체가 없는 극단적인 경우에도 일관되게 4를 쓴다).
            return new PartsCatalog.HeadModdingInfo
            {
                weaponSocketCount = 4,
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
    /// 실제로 쓸 수 있는 무기 소켓 개수 = Min(씬에 물리적으로 리깅된 소켓 수, 머리(로봇)
    /// 파츠가 정한 소켓 개수). 2026-08-12 "무기 소켓 개별화" 플랜에서 신설 - 지금은 두 로봇
    /// 전부 weaponSocketCount=2이고 씬 리깅도 2개라 실질적으로 기존과 동일하게 동작한다.
    /// 3번째 이상 소켓은 씬에 RigingPoint 등을 배치해야 실제로 나타난다(별도 후보 작업).
    /// </summary>
    public int ActiveSocketCount
    {
        get
        {
            if (shoot_cache == null) shoot_cache = FindFirstObjectByType<PlayerShootManager>();
            int rigged = shoot_cache != null ? shoot_cache.RiggedSocketCount : 0;
            return Mathf.Max(0, Mathf.Min(rigged, GetHeadInfo().weaponSocketCount));
        }
    }

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

    /// <summary>기본 상한(카탈로그에 메모리 파츠가 아직 없는 극단적인 경우의 폴백).
    /// 기획서 표기(MAX:50)와 맞춘 값 - 기본(일반 등급) 메모리 파츠의 값과 같다.</summary>
    private const int DefaultCoreMaxLevel = 50;

    /// <summary>
    /// AI 코어가 도달할 수 있는 최대 레벨. 2026-08-18부터 메모리(Memory) 파츠가 정한다
    /// (이전에는 AiCoreManager 인스펙터의 고정값이었다). <see cref="AiCoreManager.MaxLevel"/>이
    /// 이 값을 그대로 읽는다.
    /// </summary>
    public int CoreMaxLevel
    {
        get
        {
            // 2026-08-20 메모리 명세는 "AI 코어 최대 레벨 +15/25/35/43/50"(가산형)이다.
            // 그 전 데이터는 절대값(50/55/...)이었고 이 프로퍼티도 값을 대체했는데, 명세대로
            // 바꾸면서 머리 기본값에 더하는 방식으로 통일했다.
            int bonus = TryGetEquippedPart(PartSlot.Memory, out PartData memory)
                ? Mathf.Max(0, memory.coreMaxLevelBonus)
                : 0;

            return DefaultCoreMaxLevel + bonus;
        }
    }

    // ---------------------------------------------------------------------
    // 부품 상자 (적재량 상한이 있다)
    // ---------------------------------------------------------------------

    /// <summary>부품 상자를 더 받을 수 있는지. EnemyUnit이 드랍하기 전에 확인한다.</summary>
    public bool CanReceiveMorePartBoxes => RunState.UnopenedPartBoxCount < PartBoxCapacity;

    // ── 부품 상자 최소 보장 (2026-08-24 사용자 지정) ────────────────────────
    //
    // "부품상자 나올 확률을 2~3웨이브 마다 1개씩은 나오게 만들어줘".
    // 확률만 올리면 <b>운이 나쁠 때 5~6웨이브 내내 하나도 못 받는</b> 구간이 계속 생기므로,
    // 확률 상향과 확정 지급을 함께 쓴다:
    //   1) 마지막 드랍 이후 PartBoxGuaranteeWaves 웨이브가 지나면 그 웨이브의 드랍 확률에
    //      배율을 곱해 웨이브 도중 자연스럽게 나오도록 유도한다(= 몬스터가 떨어뜨린다).
    //   2) 그래도 안 나온 채 웨이브가 끝나면 웨이브 종료 시점에 1개를 직접 지급한다(안전망).
    // 카운터는 웨이브 단위라 WaveManager의 웨이브 시작/종료 이벤트에 맞춰 움직인다.

    private int waves_since_part_box;          // 마지막 부품 상자 드랍 이후 지난 웨이브 수
    private bool part_box_dropped_this_wave;
    private WaveManager subscribed_wave_manager;

    /// <summary>이번 웨이브가 "최소 보장" 구간인지(드랍 확률에 배율이 곱해진다).</summary>
    public bool IsPartBoxGuaranteeWave =>
        catalog != null && waves_since_part_box >= catalog.PartBoxGuaranteeWaves;

    /// <summary>
    /// 지금 적용해야 하는 부품 상자 드랍 확률. <see cref="EnemyUnit"/>이 카탈로그 값을 직접
    /// 읽는 대신 이 값을 쓰면 보장 구간의 상향이 자동으로 반영된다.
    /// </summary>
    public float EffectivePartBoxDropChance
    {
        get
        {
            if (catalog == null) return 0f;

            float chance = catalog.PartBoxDropChance;
            if (IsPartBoxGuaranteeWave) chance *= catalog.PartBoxGuaranteeChanceMultiplier;
            return Mathf.Clamp01(chance);
        }
    }

    /// <summary>부품 상자가 실제로 드랍됐을 때 <see cref="EnemyUnit"/>이 알려준다(보장 카운터 초기화).</summary>
    public void NotifyPartBoxDropped()
    {
        part_box_dropped_this_wave = true;
        waves_since_part_box = 0;
    }

    private void EnsureWaveSubscription()
    {
        if (subscribed_wave_manager != null) return;

        subscribed_wave_manager = FindFirstObjectByType<WaveManager>();
        if (subscribed_wave_manager == null) return;

        subscribed_wave_manager.OnWaveStarted += HandleWaveStartedForPartBox;
        subscribed_wave_manager.OnWaveEnded += HandleWaveEndedForPartBox;
    }

    private void HandleWaveStartedForPartBox(int wave) => part_box_dropped_this_wave = false;

    private void HandleWaveEndedForPartBox(int wave)
    {
        if (part_box_dropped_this_wave)
        {
            waves_since_part_box = 0;
            return;
        }

        waves_since_part_box++;

        // 보장 구간인데도 이 웨이브에 하나도 안 나왔으면 여기서 확정 지급한다. 필드에 픽업으로
        // 뿌리지 않고 바로 주는 이유: 이 시점에는 이미 웨이브가 끝나 필드 정리(자석 흡수)가
        // 진행되므로, 지금 스폰한 픽업은 주울 기회가 없거나 타이밍에 따라 사라질 수 있다.
        if (waves_since_part_box >= (catalog != null ? catalog.PartBoxGuaranteeWaves : 2) &&
            CanReceiveMorePartBoxes)
        {
            int granted = AddPartBoxes(1);
            if (granted > 0)
            {
                waves_since_part_box = 0;
                RunState.NotifyChanged();
                Debug.Log($"부품 상자 최소 보장 지급(웨이브 {wave} 종료) - 이 웨이브에 드랍이 없었습니다.");
            }
        }
    }

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

    /// <summary>
    /// 교체에 실패하면 reason에 이유가 담긴다(정비 화면이 그대로 보여준다).
    ///
    /// 무게 지탱력 초과는 더 이상 이 교체를 막지 않는다(2026-08-12 "무기 소켓 개별화" 플랜) -
    /// 장착은 항상 허용되고, 초과분은 RobotStats.Compute의 이동속도 감소로만 반영된다.
    /// 초과 여부는 정비 화면의 능력치 패널(GetTotalWeight/GetTotalWeightCapacity)이 계속 보여준다.
    /// </summary>
    public bool TrySwapInventoryWithSlot(int inventoryIndex, PartSlot slot, out string reason)
    {
        reason = string.Empty;

        if (catalog == null) return false;
        if (inventoryIndex < 0 || inventoryIndex >= RunState.ModdingInventory.Count) return false;

        int incomingId = RunState.ModdingInventory[inventoryIndex];
        if (!catalog.TryGetPart(incomingId, out PartData incoming)) return false;

        // 파츠는 자기 슬롯에만 들어간다.
        if (incoming.slot != slot) return false;

        // 머리 효과의 장착 제한(현재는 팬봇의 "기본 다리만 착용 가능"만 있다). 무게 초과와 달리
        // 이건 진짜로 장착을 막는 제한이라 여기서 걸러야 한다 - 팬봇은 무제한 구르기의 대가로
        // 다리 강화를 포기하는 머리이므로 패널티로 완화하면 정체성이 사라진다.
        string headBlockReason = HeadEffects.GetPartBlockReason(incoming);
        if (headBlockReason != null)
        {
            reason = headBlockReason;
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
    /// 인벤토리의 무기 소켓 파츠와 socketIndex번 소켓에 장착된 파츠를 <b>맞교환</b>한다.
    /// TrySwapInventoryWithSlot과 같은 맞교환 로직이되, 대상이 PartSlot 하나가 아니라 소켓
    /// 인덱스다(소켓마다 독립적으로 종류/등급을 가질 수 있어야 하기 때문 - 2026-08-12 플랜).
    /// 이 소켓에 아직 아무것도 없었으면(기본 파츠 상태) 빠지는 자리에는 기본 파츠가 채워진다.
    /// </summary>
    public bool TrySwapInventoryWithWeaponSocket(int inventoryIndex, int socketIndex, out string reason)
    {
        reason = string.Empty;

        if (catalog == null) return false;
        if (inventoryIndex < 0 || inventoryIndex >= RunState.ModdingInventory.Count) return false;
        if (socketIndex < 0 || socketIndex >= ActiveSocketCount) return false;

        int incomingId = RunState.ModdingInventory[inventoryIndex];
        if (!catalog.TryGetPart(incomingId, out PartData incoming)) return false;
        if (incoming.slot != PartSlot.ArmWeaponSocket) return false;

        if (!RunState.EquippedWeaponSocketPartIds.TryGetValue(socketIndex, out int previousId))
        {
            // 이 소켓은 지금까지 기본 파츠(표준 소켓)가 낀 것으로 간주됐다 - 빠지는 자리에
            // 그 기본 파츠를 실제로 채워 넣어야 인벤토리로 "되돌릴" 수 있다.
            PartData? fallback = catalog.GetDefaultPart(PartSlot.ArmWeaponSocket);
            if (fallback == null)
            {
                // 기본 파츠조차 데이터에 없는 예외 상태 - 빠지는 자리가 없으니 인벤토리 칸을 지운다.
                RunState.EquippedWeaponSocketPartIds[socketIndex] = incomingId;
                RunState.ModdingInventory.RemoveAt(inventoryIndex);
                RecomputePartStatBonuses();
                RunState.NotifyChanged();
                return true;
            }
            previousId = fallback.Value.partId;
        }

        RunState.EquippedWeaponSocketPartIds[socketIndex] = incomingId;
        RunState.ModdingInventory[inventoryIndex] = previousId; // 맞교환: 빠진 파츠가 인벤토리의 그 자리를 차지한다.

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

    /// <summary>다리(Leg)의 weightCapacity = 무게를 지탱할 수 있는 총량(2026-08-26 자기장 코어
    /// 삭제 전에는 코어+다리 합이었다 - PartSlot.cs 참고).</summary>
    public float GetTotalWeightCapacity()
    {
        float total = 0f;
        if (TryGetEquippedPart(PartSlot.Leg, out PartData leg)) total += leg.weightCapacity;
        return total;
    }

    public float GetWeaponWeight(int weaponId)
    {
        return catalog != null && catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta) ? meta.weight : 0f;
    }

    /// <summary>
    /// 이 소켓에 낀 무기 소켓 파츠가 <b>무기 카테고리</b>(연사/산탄/정밀/폭발/에너지/근접)를
    /// 제한하는데 이 무기가 그 카테고리가 아니면 true.
    ///
    /// 2026-08-20 소켓 명세 반영으로 제한축이 무기 타입(경무장/중무장/근접)에서
    /// <b>무기 카테고리로 바뀌었다</b> - 명세의 소켓 7종이 "장착 가능한 무기 카테고리"를 정한다.
    /// 범용 소켓은 restrictsWeaponType이 꺼져 있어 어떤 무기든 제 짝으로 받는다.
    ///
    /// 2026-08-12 "무기 소켓 개별화" 플랜부터 이 불일치는 장착을 막지 않는다 - 대신
    /// GetEffectiveWeaponWeight가 무게에 배율을 곱해 이동속도 패널티로 이어지고,
    /// 소켓의 등급 보정(GetWeaponSocketModifiers)도 받지 못한다.
    /// </summary>
    public bool IsWeaponMismatched(int socketIndex, int weaponId)
    {
        if (!TryGetEquippedWeaponSocketPart(socketIndex, out PartData socketPart) || !socketPart.restrictsWeaponType)
            return false;

        // 이 무기의 카테고리 정보가 카탈로그에 없으면(데이터 누락) 불일치로 취급하지 않는다.
        if (catalog == null || !catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta))
            return false;

        return !socketPart.AcceptsWeaponType(meta.type);
    }

    /// <summary>타입 불일치 무기에 곱해지는 무게 배율(PartsCatalog의 밸런스 임시값).</summary>
    public float MismatchWeightMultiplier => catalog != null ? catalog.MismatchWeightMultiplier : 2.0f;

    /// <summary>무게 지탱력 초과 1당 이동속도가 깎이는 양(PartsCatalog의 밸런스 임시값). RobotStats.Compute가 읽는다.</summary>
    public float OverweightSpeedPenaltyPerUnit => catalog != null ? catalog.OverweightSpeedPenaltyPerUnit : 0.05f;

    /// <summary>이 소켓에 이 무기를 넣었을 때 실제로 적용되는 무게 = 기본 무게 x (타입 불일치면 배율, 아니면 1).</summary>
    public float GetEffectiveWeaponWeight(int socketIndex, int weaponId)
    {
        float baseWeight = GetWeaponWeight(weaponId);
        return IsWeaponMismatched(socketIndex, weaponId) ? baseWeight * MismatchWeightMultiplier : baseWeight;
    }

    /// <summary>
    /// 현재 장착된 모든 무기의 무게 합(타입 불일치 배율 반영). excludeSocketIndex를 주면 그
    /// 소켓은 제외한다 - 같은 소켓에 새 무기를 넣을 때 "그 소켓의 기존 무게를 빼고 새 무게를
    /// 더해서" 비교하기 위함.
    /// </summary>
    public float GetEquippedWeaponWeightSum(int excludeSocketIndex = -1)
    {
        float sum = 0f;
        for (int i = 0; i < RunState.EquippedWeapons.Count; i++)
        {
            if (i == excludeSocketIndex) continue;
            sum += GetEffectiveWeaponWeight(i, RunState.EquippedWeapons[i].WeaponId);
        }
        return sum;
    }

    /// <summary>
    /// 현재 장착된 무기 소켓 파츠들의 무게 합. excludeSocketIndex를 주면 그 소켓은 제외한다
    /// (GetEquippedWeaponWeightSum과 같은 이유 - 그 소켓을 다른 파츠로 교체했을 때의 무게 계산용).
    /// </summary>
    private float GetEquippedWeaponSocketPartsWeightSum(int excludeSocketIndex = -1)
    {
        float sum = 0f;
        for (int i = 0; i < ActiveSocketCount; i++)
        {
            if (i == excludeSocketIndex) continue;
            if (TryGetEquippedWeaponSocketPart(i, out PartData part)) sum += part.weight;
        }
        return sum;
    }

    /// <summary>
    /// 장착된 <b>파츠</b>들의 무게 합(디스크는 무게가 없다). excludeSlot을 주면 그 슬롯은 빼고 센다 -
    /// 그 슬롯을 다른 파츠로 교체했을 때의 무게를 계산하기 위함이다. 무기 소켓 파츠는 슬롯 하나가
    /// 아니라 소켓 인덱스별로 존재하므로 excludeSocketIndex로 따로 뺄 소켓을 지정한다.
    /// </summary>
    public float GetEquippedPartsWeightSum(PartSlot? excludeSlot = null, int excludeSocketIndex = -1)
    {
        float sum = 0f;

        foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
        {
            if (slot == PartSlot.ArmWeaponSocket) continue; // 소켓별 무게는 아래에서 따로 합산한다
            if (excludeSlot.HasValue && slot == excludeSlot.Value) continue;
            if (TryGetEquippedPart(slot, out PartData part)) sum += part.weight;
        }

        sum += GetEquippedWeaponSocketPartsWeightSum(excludeSocketIndex);

        return sum;
    }

    /// <summary>무기 + 파츠를 전부 합친 현재 총 무게.</summary>
    public float GetTotalWeight() => GetEquippedWeaponWeightSum() + GetEquippedPartsWeightSum();

    /// <summary>
    /// 이 소켓에 이 무기를 넣었을 때 무게(타입 불일치 배율 반영)가 지탱력(다리)을
    /// 넘지 않는지 확인한다. <b>더 이상 장착을 막지 않는다</b> - 반환값은 UI 경고 표시용일
    /// 뿐이며, 초과분은 RobotStats.Compute의 이동속도 감소로만 반영된다(2026-08-12 플랜).
    /// </summary>
    public bool CheckWeightLimit(int socketIndex, int newWeaponId, out float totalAfter, out float capacity)
    {
        capacity = GetTotalWeightCapacity();
        totalAfter = GetEquippedWeaponWeightSum(socketIndex) + GetEffectiveWeaponWeight(socketIndex, newWeaponId) + GetEquippedPartsWeightSum();
        return totalAfter <= capacity;
    }

    /// <summary>
    /// 이 파츠로 교체했을 때 무게가 지탱력을 넘지 않는지 확인한다(UI 경고 표시용 - 더 이상
    /// 교체를 막지 않는다). 파츠에도 무게가 있기 때문에(사용자 확정 사항) 무기와 같은 계산을 쓴다.
    ///
    /// 주의: 교체하려는 파츠가 다리면 지탱력 자체가 바뀌므로,
    /// 새 파츠의 weightCapacity를 반영한 지탱력과 비교해야 한다.
    /// </summary>
    public bool CheckPartWeightLimit(PartData newPart, out float totalAfter, out float capacity)
    {
        totalAfter = GetEquippedWeaponWeightSum() + GetEquippedPartsWeightSum(newPart.slot) + newPart.weight;

        capacity = GetCapacityAfterSwap(PartSlot.Leg, newPart);

        return totalAfter <= capacity;
    }

    // 해당 슬롯의 지탱력을 구하되, 교체 대상이 바로 그 슬롯이면 새 파츠의 값을 쓴다.
    private float GetCapacityAfterSwap(PartSlot slot, PartData newPart)
    {
        if (newPart.slot == slot) return newPart.weightCapacity;
        return TryGetEquippedPart(slot, out PartData current) ? current.weightCapacity : 0f;
    }

    /// <summary>
    /// 현재 장착된 무기 소켓 파츠의 등급 효과 배율. 소켓 파츠가 없으면 전부 1배를 돌려준다.
    /// PlayerShootManager가 매 프레임 조회하므로 값만 넘기는 가벼운 구조체로 반환한다.
    /// </summary>
    public struct SocketModifiers
    {
        public float AttackSpeedPercent;  // 공격 속도 +% (대기시간이 이만큼 짧아진다)
        public float DamageFlat;          // 공격력 +절대값
        public float DamagePercent;       // 공격력 +%
        public float CritChancePercent;   // 치명타 확률 +%p
        public float SplashPercent;       // 스플래시 반경 +%
        public float DefIgnorePercent;    // 방어력 무시 +%p

        /// <summary>보정 없음(소켓 파츠가 없거나 카테고리가 안 맞을 때).</summary>
        public static SocketModifiers None => default;

        public float AttackSpeedMultiplier => 1f + AttackSpeedPercent * 0.01f;
        public float DamageMultiplier => 1f + DamagePercent * 0.01f;
        public float SplashMultiplier => 1f + SplashPercent * 0.01f;
    }

    /// <summary>
    /// socketIndex번 소켓에 장착된 무기 소켓 파츠가 <b>그 소켓에 낀 무기</b>에 주는 보정.
    ///
    /// 2026-08-20 소켓 명세 반영으로 반환값이 사거리/감지/회전 배율에서 공격속도·공격력·치명타·
    /// 스플래시·방어력무시로 통째로 바뀌었다. <b>카테고리가 맞지 않으면 보정이 없다</b>
    /// (범용 소켓은 카테고리 제한이 없으므로 항상 자기 보정을 준다).
    /// PlayerShootManager가 매 프레임 조회하므로 값만 넘기는 가벼운 구조체로 반환한다.
    /// </summary>
    public SocketModifiers GetWeaponSocketModifiers(int socketIndex, int weaponId)
    {
        if (!TryGetEquippedWeaponSocketPart(socketIndex, out PartData socketPart)) return SocketModifiers.None;
        if (IsWeaponMismatched(socketIndex, weaponId)) return SocketModifiers.None;

        return new SocketModifiers
        {
            AttackSpeedPercent = socketPart.socketAttackSpeedPercent,
            DamageFlat = socketPart.socketDamageFlat,
            DamagePercent = socketPart.socketDamagePercent,
            CritChancePercent = socketPart.socketCritChancePercent,
            SplashPercent = socketPart.socketSplashPercent,
            DefIgnorePercent = socketPart.socketDefIgnorePercent
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
    //
    // 2026-08-20 명세 반영으로 (1) 가산 스탯이 파츠당 2쌍이 되고 (2) "장착 디스크 1개당" 계열
    // 효과가 생겼다. 후자는 디스크를 사고 팔 때도 값이 바뀌므로 ShopManager가 디스크 장착 후에
    // RecomputePartBonuses()를 호출한다.
    private void RecomputePartStatBonuses()
    {
        RunState.PartStatBonuses.Clear();

        foreach (var kv in RunState.EquippedPartIds)
        {
            if (!catalog.TryGetPart(kv.Value, out PartData part)) continue;

            AddPartBonus(part.bonusStat, part.bonusAmount);
            AddPartBonus(part.bonusStat2, part.bonusAmount2);

            switch (part.effect)
            {
                case PartEffect.PerDiscStat:
                    AddPartBonus(part.effectStat, part.effectAmount * RunState.EquippedDiscIds.Count);
                    break;

                case PartEffect.PerSymphonyDiscAtk:
                    AddPartBonus(StatType.Atk, part.effectAmount * CountSymphonyDiscs());
                    break;
            }
        }
    }

    private static void AddPartBonus(StatType stat, float amount)
    {
        if (amount == 0f) return;

        if (!RunState.PartStatBonuses.ContainsKey(stat)) RunState.PartStatBonuses[stat] = 0f;
        RunState.PartStatBonuses[stat] += amount;
    }

    /// <summary>
    /// 디스크를 사고 판 뒤 파츠 보너스를 다시 계산한다("장착 디스크 1개당" 계열 효과 때문).
    /// 디스크 자체의 상시 스탯은 ShopManager가 RunState.DiscStatBonuses에 따로 넣는다.
    /// </summary>
    public void RecomputePartBonuses() => RecomputePartStatBonuses();

    /// <summary>
    /// 장착한 디스크 중 이름이 "교향곡"으로 시작하는 것의 개수(교향곡 모음집 슬롯이 읽는다).
    /// 디스크 이름은 ShopCatalog가 갖고 있어 씬에서 ShopManager를 한 번만 찾아 캐시한다.
    /// </summary>
    private int CountSymphonyDiscs()
    {
        if (RunState.EquippedDiscIds.Count == 0) return 0;

        if (symphony_disc_ids == null)
        {
            symphony_disc_ids = new HashSet<int>();

            ShopManager shopManager = FindFirstObjectByType<ShopManager>();
            ShopCatalog shopCatalog = shopManager != null ? shopManager.Catalog : null;

            if (shopCatalog != null)
            {
                foreach (DiscData disc in shopCatalog.Discs)
                {
                    if (!string.IsNullOrEmpty(disc.discName) && disc.discName.StartsWith("교향곡"))
                        symphony_disc_ids.Add(disc.discId);
                }
            }
        }

        int count = 0;
        foreach (int id in RunState.EquippedDiscIds)
        {
            if (symphony_disc_ids.Contains(id)) count++;
        }

        return count;
    }

    private HashSet<int> symphony_disc_ids;
}
