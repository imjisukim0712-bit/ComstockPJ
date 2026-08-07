using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정비 시간 상점의 로직을 담당한다(화면 표시는 ShopPanelUI가 맡는다).
///
/// 기획서 p.13 기준 동작:
/// - 품목 4칸을 등급 가중치로 무작위 생성한다(무기 또는 디스크).
/// - 새로고침으로 품목을 다시 뽑을 수 있고, 같은 웨이브에서 반복할수록 비용이 오른다.
/// - 칸마다 개별 잠금이 가능하며, 잠긴 칸은 새로고침해도 그대로 남는다.
/// - 구매하면 골드가 차감되고 그 칸은 "구매 완료" 상태가 된다.
///
/// 무기 구매는 소켓 즉시 교체다. 다만 소켓이 여러 개라 어느 소켓을 바꿀지는 플레이어가
/// 골라야 하므로, 이 매니저는 교체 자체를 수행하는 EquipWeaponToSocket만 제공하고
/// "어느 소켓에 넣을지" 고르는 흐름은 ShopPanelUI가 처리한다.
/// </summary>
public class ShopManager : MonoBehaviour
{
    /// <summary>상점 칸 하나에 놓인 품목. 무기이거나 디스크다.</summary>
    public class Offer
    {
        public bool IsDisc;
        public ItemGrade Grade;
        public int Price;
        public bool Purchased;
        public bool Locked;

        // 무기일 때만 사용
        public int WeaponId;
        public WeaponData Weapon;

        // 디스크일 때만 사용
        public DiscData Disc;

        public string DisplayName => IsDisc ? Disc.discName : Weapon.weapon_name;
        public string CategoryName => IsDisc ? "디스크" : "무기";

        /// <summary>카드 본문에 보여줄 성능 요약.</summary>
        public string BuildDescription()
        {
            if (IsDisc) return Disc.BuildDescription();

            // 등급별 수치가 데이터 행에 이미 반영되어 있으므로 배율을 곱하지 않고 그대로 보여준다.
            string text = $"공격력 {Weapon.weapon_atk:0.##} / 공격속도 {Weapon.weapon_atsp:0.##} / 사거리 {Weapon.weapon_range:0.##}";

            if (Weapon.weapon_projectiles > 1) text += $" / {Weapon.weapon_projectiles}발";
            if (Weapon.weapon_splash > 0f) text += $" / 폭발 {Weapon.weapon_splash:0.##}";
            if (Weapon.weapon_pierce != 0)
            {
                string pierce = Weapon.weapon_pierce < 0 ? "관통 전체" : $"관통 {Weapon.weapon_pierce}회";
                if (Weapon.weapon_pierce_chance > 0f && Weapon.weapon_pierce_chance < 1f)
                {
                    pierce += $"({Weapon.weapon_pierce_chance * 100f:0}%)";
                }
                text += $" / {pierce}";
            }
            if (Weapon.weapon_defignore > 0f) text += $" / 방어무시 {Weapon.weapon_defignore * 100f:0}%";
            if (Weapon.weapon_knockback > 0f) text += " / 넉백";

            return text;
        }
    }

    [SerializeField] private ShopCatalog catalog;

    private readonly List<Offer> offers = new List<Offer>();

    public IReadOnlyList<Offer> Offers => offers;
    public ShopCatalog Catalog => catalog;

    /// <summary>다음 새로고침에 필요한 골드.</summary>
    public int CurrentRefreshCost => catalog != null ? catalog.GetRefreshCost(RunState.ShopRefreshCount) : 0;

    /// <summary>장착된 디스크가 슬롯 상한에 도달했는지.</summary>
    public bool IsDiscSlotFull => RunState.EquippedDiscIds.Count >= GetDiscSlotCount();

    /// <summary>디스크 슬롯 최대 개수(UI 표시용으로 공개).</summary>
    public int DiscSlotCount => GetDiscSlotCount();

    /// <summary>
    /// 디스크 슬롯 최대 개수. 원래 머리(로봇) 파츠 능력치가 정하는 값이라
    /// PartsCatalog.HeadModdingInfo에서 조회한다(Phase 4). 정비 시스템을 못 찾으면
    /// ShopCatalog의 임시값(Phase 3에서 쓰던 고정 상수)으로 폴백한다.
    /// </summary>
    private int GetDiscSlotCount()
    {
        // ModdingManager가 디스크 슬롯 파츠(DiscSlot)까지 반영해 최종 개수를 계산한다 -
        // 파츠를 끼웠으면 그 값이, 없으면 머리(로봇) 기본값이 쓰인다.
        ModdingManager modding = ModdingManager.Instance;
        if (modding != null && modding.Catalog != null) return modding.DiscSlotCount;

        return catalog != null ? catalog.DiscSlotCount : 0;
    }

    /// <summary>
    /// 새 웨이브의 상점을 연다. 새로고침 횟수(비용)를 초기화하고 품목을 새로 뽑는다.
    /// 잠금도 웨이브가 바뀌면 의미가 없으므로 전부 해제된다.
    /// </summary>
    public void OpenForNewWave()
    {
        RunState.ShopRefreshCount = 0;
        offers.Clear();
        RerollOffers(respectLocks: false);
    }

    /// <summary>
    /// 새로고침. 비용을 낼 골드가 있으면 잠기지 않은 칸만 다시 뽑는다.
    /// </summary>
    /// <returns>실제로 새로고침했으면 true, 골드가 모자라면 false</returns>
    public bool TryRefresh()
    {
        int cost = CurrentRefreshCost;
        if (RunState.Gold < cost) return false;

        RunState.Gold -= cost;
        RunState.ShopRefreshCount++;
        RerollOffers(respectLocks: true);
        RunState.NotifyChanged();
        return true;
    }

    /// <summary>칸의 잠금 상태를 뒤집는다. 이미 구매한 칸은 잠글 수 없다.</summary>
    public void ToggleLock(int index)
    {
        if (index < 0 || index >= offers.Count) return;
        if (offers[index].Purchased) return;

        offers[index].Locked = !offers[index].Locked;
    }

    /// <summary>
    /// 칸을 구매할 수 있는지 확인한다. 살 수 없으면 이유를 함께 돌려준다(UI에 그대로 표시).
    /// </summary>
    public bool CanPurchase(int index, out string reason)
    {
        reason = string.Empty;

        if (index < 0 || index >= offers.Count) { reason = "잘못된 칸입니다"; return false; }

        Offer offer = offers[index];
        if (offer.Purchased) { reason = "이미 구매했습니다"; return false; }
        if (RunState.Gold < offer.Price) { reason = "골드가 부족합니다"; return false; }
        if (offer.IsDisc && IsDiscSlotFull) { reason = "디스크 슬롯이 가득 찼습니다"; return false; }

        return true;
    }

    /// <summary>
    /// 디스크를 구매해 즉시 장착한다. 무기는 어느 소켓에 넣을지 정해야 해서
    /// PurchaseWeaponIntoSocket을 따로 쓴다.
    /// </summary>
    public bool TryPurchaseDisc(int index)
    {
        if (!CanPurchase(index, out _)) return false;

        Offer offer = offers[index];
        if (!offer.IsDisc) return false;

        RunState.Gold -= offer.Price;
        EquipDisc(offer.Disc);
        offer.Purchased = true;
        offer.Locked = false;

        RunState.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 이 칸의 무기를 이 소켓에 장착할 수 있는지 확인한다(골드 부족/이미 구매 여부에 더해
    /// 무기 소켓 타입 제한, 자기장 코어+다리 무게 제한까지 검사). 안 되면 이유를 함께 돌려준다.
    /// </summary>
    public bool CanPurchaseWeaponIntoSocket(int index, int socketIndex, out string reason)
    {
        if (!CanPurchase(index, out reason)) return false;

        Offer offer = offers[index];
        if (offer.IsDisc) { reason = "무기 칸이 아닙니다"; return false; }

        // 정비 시스템(ModdingManager)을 못 찾으면 제약 없이 통과시킨다 - 씬 배치 누락 같은
        // 예외 상황에서 상점 자체가 완전히 막히는 것을 막기 위함.
        ModdingManager modding = Object.FindFirstObjectByType<ModdingManager>();
        if (modding == null) return true;

        if (!modding.CheckWeaponTypeAllowed(offer.WeaponId, out reason)) return false;

        if (!modding.CheckWeightLimit(socketIndex, offer.WeaponId, out float totalAfter, out float capacity))
        {
            reason = $"무기 무게 초과 ({totalAfter:0.#} / {capacity:0.#})";
            return false;
        }

        return true;
    }

    /// <summary>무기를 구매해 지정한 소켓에 즉시 장착(교체)한다.</summary>
    public bool TryPurchaseWeaponIntoSocket(int index, int socketIndex)
    {
        if (!CanPurchaseWeaponIntoSocket(index, socketIndex, out _)) return false;

        Offer offer = offers[index];

        PlayerShootManager shootManager = Object.FindFirstObjectByType<PlayerShootManager>();
        if (shootManager == null)
        {
            Debug.LogWarning("PlayerShootManager를 찾을 수 없어 무기를 장착하지 못했습니다.");
            return false;
        }

        if (!shootManager.EquipWeapon(socketIndex, offer.WeaponId)) return false;

        RunState.Gold -= offer.Price;
        offer.Purchased = true;
        offer.Locked = false;

        RunState.NotifyChanged();
        return true;
    }

    /// <summary>디스크를 장착하고, 그 증감치를 RunState의 합산 보너스에 반영한다.</summary>
    private void EquipDisc(DiscData disc)
    {
        RunState.EquippedDiscIds.Add(disc.discId);

        AddDiscBonus(disc.upStat, disc.upAmount);
        AddDiscBonus(disc.downStat, -disc.downAmount); // 하락 스탯은 음수로 누적
    }

    private static void AddDiscBonus(StatType stat, float amount)
    {
        if (!RunState.DiscStatBonuses.ContainsKey(stat)) RunState.DiscStatBonuses[stat] = 0f;
        RunState.DiscStatBonuses[stat] += amount;
    }

    /// <summary>
    /// 품목을 다시 뽑는다. respectLocks가 true면 잠긴 칸과 이미 구매한 칸은 건드리지 않는다.
    /// (구매한 칸은 새로고침하면 새 품목으로 채워진다 - 칸이 영구히 죽어버리면 새로고침 가치가 없어서)
    /// </summary>
    private void RerollOffers(bool respectLocks)
    {
        if (catalog == null)
        {
            Debug.LogWarning("ShopManager에 ShopCatalog가 연결되지 않아 품목을 만들 수 없습니다.");
            return;
        }

        int slotCount = catalog.SlotCount;

        while (offers.Count < slotCount) offers.Add(null);
        while (offers.Count > slotCount) offers.RemoveAt(offers.Count - 1);

        for (int i = 0; i < slotCount; i++)
        {
            if (respectLocks && offers[i] != null && offers[i].Locked) continue;

            offers[i] = CreateRandomOffer();
        }
    }

    private Offer CreateRandomOffer()
    {
        // 디스크는 자기 등급이 고정이라, 이번 웨이브에 등장 가능한 등급의 디스크만 후보가 된다
        // (무기의 RollGrade와 동일한 minWave 규칙을 디스크에도 적용 - 1웨이브에 전설이 뜨지 않도록).
        List<DiscData> availableDiscs = GetAvailableDiscs();

        bool wantDisc = availableDiscs.Count > 0 && Random.value < catalog.DiscAppearChance;

        // 무기 목록이 비어 있으면 디스크로, 디스크가 없으면 무기로 폴백한다.
        if (wantDisc) return CreateDiscOffer(availableDiscs);
        if (catalog.WeaponEntries.Count > 0) return CreateWeaponOffer();
        if (availableDiscs.Count > 0) return CreateDiscOffer(availableDiscs);

        Debug.LogWarning("ShopCatalog에 이번 웨이브에 등장 가능한 무기도 디스크도 없어 상점 품목을 만들 수 없습니다.");
        return null;
    }

    // 이번 웨이브에 등장 가능한(등급 minWave를 만족하는) 디스크만 추린다.
    private List<DiscData> GetAvailableDiscs()
    {
        int wave = Mathf.Max(1, RunState.WaveNumber);
        var result = new List<DiscData>();

        foreach (DiscData disc in catalog.Discs)
        {
            if (catalog.IsGradeAvailable(disc.grade, wave)) result.Add(disc);
        }

        return result;
    }

    private Offer CreateDiscOffer(List<DiscData> availableDiscs)
    {
        DiscData disc = availableDiscs[Random.Range(0, availableDiscs.Count)];

        return new Offer
        {
            IsDisc = true,
            Disc = disc,
            Grade = disc.grade,
            Price = Mathf.Max(0, disc.price)
        };
    }

    /// <summary>
    /// 등급을 먼저 뽑고, <b>그 등급의 무기 행 중에서</b> 하나를 고른다.
    /// 무기는 등급마다 별도의 데이터 행을 갖고 있으므로(13종 x 5등급 = 65행) 예전처럼
    /// 무기를 먼저 고르고 등급 배율을 곱하는 방식이 아니다. 가격도 행에 최종가가 들어있다.
    /// </summary>
    private Offer CreateWeaponOffer()
    {
        ItemGrade rolled = catalog.RollGrade(Mathf.Max(1, RunState.WaveNumber));

        // 뽑은 등급에 무기가 하나도 없으면 한 등급씩 낮춰가며 찾는다
        // (PartsCatalog.TryRollLootPart의 등급 폴백과 같은 방식).
        for (int g = (int)rolled; g >= 0; g--)
        {
            List<ShopCatalog.WeaponEntry> candidates = GetWeaponEntriesOfGrade((ItemGrade)g);
            if (candidates.Count == 0) continue;

            ShopCatalog.WeaponEntry entry = candidates[Random.Range(0, candidates.Count)];
            GameDataManager.Instance.Weapons.TryGetValue(entry.weaponId, out WeaponData weapon);

            return new Offer
            {
                IsDisc = false,
                WeaponId = entry.weaponId,
                Weapon = weapon,
                Grade = weapon.weapon_grade,
                Price = Mathf.Max(0, entry.basePrice)
            };
        }

        Debug.LogWarning("상점에 등장 가능한 무기가 없습니다. ShopCatalog의 무기 목록과 GameDataAsset의 무기 테이블이 같은 ID를 갖고 있는지 확인하세요.");
        return null;
    }

    // 상점 판매 목록 중 특정 등급의 무기만 추린다.
    private List<ShopCatalog.WeaponEntry> GetWeaponEntriesOfGrade(ItemGrade grade)
    {
        var result = new List<ShopCatalog.WeaponEntry>();
        if (GameDataManager.Instance == null) return result;

        foreach (ShopCatalog.WeaponEntry entry in catalog.WeaponEntries)
        {
            if (GameDataManager.Instance.Weapons.TryGetValue(entry.weaponId, out WeaponData weapon) &&
                weapon.weapon_grade == grade)
            {
                result.Add(entry);
            }
        }

        return result;
    }
}
