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
    /// <summary>상점 칸 하나에 놓인 품목. 무기·디스크·악세사리 중 하나다.</summary>
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

        // 악세사리일 때만 사용(2026-08-19 Phase D - 엔드리스 전용, IsDisc와 별개 플래그).
        public bool IsAccessory;
        public AccessoryData Accessory;

        public string DisplayName => IsAccessory ? Accessory.accessoryName : IsDisc ? Disc.discName : Weapon.weapon_name;
        public string CategoryName => IsAccessory ? "악세사리" : IsDisc ? "디스크" : "무기";

        /// <summary>카드 본문에 보여줄 성능 요약.</summary>
        public string BuildDescription()
        {
            // 효과는 없고 점수만 준다(기획 확정) - 능력치 문구 대신 그 사실과 지금까지 구매한
            // 개수를 보여준다. 개수를 함께 보여줘야 "여러 개 겹쳐 살수록 위로 쌓인다"는 동작이
            // 카드에서도 드러난다.
            if (IsAccessory)
            {
                int owned = 0;
                foreach (int id in RunState.AccessoryPurchaseOrder) if (id == Accessory.accessoryId) owned++;
                return $"점수 +{Accessory.score} (효과 없음)\n보유 {owned}개";
            }

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
    /// 새 웨이브의 상점을 연다. 새로고침 횟수(비용)를 초기화하고, 잠기지 않은 칸만 새로 뽑는다.
    ///
    /// 예전에는 "웨이브가 바뀌면 잠금이 의미 없다"고 보고 매 웨이브 전부 해제했지만, 사용자가
    /// "잠긴 아이템은 다음 웨이브에도 품목이 바뀌면 안 된다"고 요청해(2026-08-12) 웨이브를
    /// 넘어 잠금이 유지되도록 바꿨다 - respectLocks=true로 RerollOffers를 부르면 첫 진입 때는
    /// (모든 칸이 null이라) 전부 새로 뽑히고, 이후 웨이브부터는 잠긴 칸만 그대로 남는다.
    /// </summary>
    public void OpenForNewWave()
    {
        RunState.ShopRefreshCount = 0;
        RerollOffers(respectLocks: true);
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
        // 악세사리는 이미 구매했어도 다시 사는 것이 정상 동작(스택형)이라 Purchased 검사에서
        // 제외한다 - 대신 카드 자체를 "구매 완료" 스탬프로 막지 않고 계속 살 수 있게
        // CreateAccessoryOffer()가 Purchased를 절대 true로 두지 않는다.
        if (!offer.IsAccessory && offer.Purchased) { reason = "이미 구매했습니다"; return false; }
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

        // 핫팟(누적 디스크 구매 50) / 염동력(디스크 4개 이상 착용) / 엔드리스 구매 조건들
        UnlockTracker.ReportDiscPurchased(RunState.EquippedDiscIds.Count);
        UnlockTracker.ReportShopPurchase(offer.Grade);

        RunState.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 악세사리를 구매한다(2026-08-19 Phase D). 무기·디스크와 달리 <b>장착 슬롯이 없고 중복
    /// 구매도 막지 않는다</b> - 살 때마다 <see cref="RunState.AccessoryPurchaseOrder"/>에
    /// 쌓이고(시각적으로 위로 쌓이는 순서와 같다) <see cref="RunScore.AddAccessoryScore"/>로
    /// 점수만 늘어난다. 카드는 구매 후에도 "구매 완료"로 잠기지 않고 계속 다시 살 수 있다.
    /// </summary>
    public bool TryPurchaseAccessory(int index)
    {
        if (!CanPurchase(index, out _)) return false;

        Offer offer = offers[index];
        if (!offer.IsAccessory) return false;

        RunState.Gold -= offer.Price;
        RunState.AccessoryPurchaseOrder.Add(offer.Accessory.accessoryId);
        RunScore.AddAccessoryScore(offer.Accessory.score);
        UnlockTracker.ReportShopPurchase(offer.Grade);

        RunState.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 이 칸의 무기를 이 소켓에 장착할 수 있는지 확인한다(골드 부족/이미 구매 여부만 검사).
    ///
    /// 2026-08-12 "무기 소켓 개별화" 플랜부터 타입 불일치/무게 초과는 더 이상 구매를 막지
    /// 않는다 - 타입이 달라도, 지탱력을 넘어도 항상 장착할 수 있고 대신 무게 패널티(타입
    /// 불일치 시 배율, 초과 시 이동속도 감소)로만 반영된다. 비차단 경고 문구는
    /// BuildSocketWarning을 따로 호출해서 얻는다.
    /// </summary>
    public bool CanPurchaseWeaponIntoSocket(int index, int socketIndex, out string reason)
    {
        if (!CanPurchase(index, out reason)) return false;

        Offer offer = offers[index];
        if (offer.IsDisc || offer.IsAccessory) { reason = "무기 칸이 아닙니다"; return false; }

        return true;
    }

    /// <summary>
    /// 소켓 선택 버튼에 곁들일 <b>비차단</b> 경고 문구(타입 불일치/무게 초과). 구매를 막지는
    /// 않지만 어떤 패널티가 붙는지 미리 알려준다. 경고가 없으면 빈 문자열을 돌려준다.
    /// </summary>
    public string BuildSocketWarning(int index, int socketIndex)
    {
        if (index < 0 || index >= offers.Count || offers[index] == null) return string.Empty;

        Offer offer = offers[index];
        if (offer.IsDisc || offer.IsAccessory) return string.Empty;

        ModdingManager modding = Object.FindFirstObjectByType<ModdingManager>();
        if (modding == null) return string.Empty;

        var warnings = new List<string>();

        if (modding.IsWeaponMismatched(socketIndex, offer.WeaponId))
        {
            warnings.Add($"타입 불일치 - 무게 x{modding.MismatchWeightMultiplier:0.#} 적용");
        }

        if (!modding.CheckWeightLimit(socketIndex, offer.WeaponId, out float totalAfter, out float capacity))
        {
            warnings.Add($"무게 초과 ({totalAfter:0.#} / {capacity:0.#}) - 이동속도 감소");
        }

        return string.Join(" / ", warnings);
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

        // 유니콘 뿔(엔드리스에서 서로 다른 6종류 무기 착용) / 목걸이(전설 등급 구매) 등
        UnlockTracker.ReportWeaponEquipped(offer.WeaponId);
        UnlockTracker.ReportShopPurchase(offer.Grade);

        RunState.NotifyChanged();
        return true;
    }

    /// <summary>
    /// 디스크를 장착하고, 상시 스탯 성분을 RunState의 합산 보너스에 반영한다.
    ///
    /// 2026-08-12 디스크 기획서 반영으로 효과가 21종으로 늘면서, 상시 스탯 가감(statA/B/C)을
    /// 갖는 타입은 <see cref="DiscEffectType.StatModifier"/>와
    /// <see cref="DiscEffectType.PassiveAuraSlow"/>(자기 자신에게 걸리는 회피율 성분만) 둘뿐이다.
    /// 나머지 타입(처치 시 발동/주기/조건부 등)은 상시 보너스가 없고 <see cref="DiscEffectRuntime"/>이
    /// 매 프레임 또는 이벤트 시점에 직접 처리한다.
    /// </summary>
    private void EquipDisc(DiscData disc)
    {
        RunState.EquippedDiscIds.Add(disc.discId);

        if (disc.effectType == DiscEffectType.StatModifier)
        {
            AddDiscBonus(disc.statA, disc.amountA);
            AddDiscBonus(disc.statB, disc.amountB);
            AddDiscBonus(disc.statC, disc.amountC);
        }
        else if (disc.effectType == DiscEffectType.PassiveAuraSlow)
        {
            AddDiscBonus(disc.statA, disc.amountA); // amountB(주변 적 감속)는 DiscEffectRuntime이 처리
        }
        else if (disc.effectType == DiscEffectType.LastStand)
        {
            RunState.DiscUsesRemaining.TryGetValue(disc.discId, out int remaining);
            RunState.DiscUsesRemaining[disc.discId] = remaining + Mathf.Max(1, disc.maxUses);
        }
        else if (disc.effectType == DiscEffectType.CritChancePerDisc)
        {
            // 착용 디스크 총 개수(이번에 추가된 것 포함)에 비례하므로, 매 장착마다 다시 계산한다.
            RecomputeCritChancePerDisc(disc);
        }
    }

    /// <summary>
    /// "염동력" 등 크리티컬 확률이 착용 디스크 개수에 비례하는 효과의 기여분을 다시 계산해
    /// DiscStatBonuses에 덮어쓴다. 다른 스탯처럼 누적만 하면 장착할 때마다 이전 개수 기준
    /// 값이 남아 이중으로 더해지므로, 전용 키로 따로 추적해 항상 최신 값으로 교체한다.
    /// </summary>
    private void RecomputeCritChancePerDisc(DiscData disc)
    {
        if (!RunState.DiscStackProgress.TryGetValue(disc.discId, out float previous)) previous = 0f;

        // 이 디스크 자체를 여러 장 장착했으면(중복 장착 허용) 장 수만큼 배로 적용한다.
        int copies = 0;
        foreach (int id in RunState.EquippedDiscIds) if (id == disc.discId) copies++;

        float total = disc.amountA * copies * RunState.EquippedDiscIds.Count;
        RunState.DiscStackProgress[disc.discId] = total;

        if (!RunState.DiscStatBonuses.ContainsKey(StatType.CritChance)) RunState.DiscStatBonuses[StatType.CritChance] = 0f;
        RunState.DiscStatBonuses[StatType.CritChance] += total - previous;
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

    /// <summary>엔드리스 모드 상점 칸이 악세사리로 대체될 확률(2026-08-19 Phase D, 사용자 지정
    /// "칸마다 5% 확률"). 엔드리스가 아니면 절대 등장하지 않는다(기획 확정).</summary>
    private const float AccessoryAppearChanceInEndless = 0.05f;

    private Offer CreateRandomOffer()
    {
        // 악세사리는 무기/디스크 추첨보다 먼저 굴린다(계획서 표현 그대로) - 당첨되면 그 칸은
        // 통째로 악세사리로 대체되고 아래 무기/디스크 로직은 타지 않는다.
        // 잠긴 악세사리는 등장하지 않는다(2026-08-19 Phase E) - 하나도 해금되지 않았으면
        // 이 칸은 그냥 무기/디스크가 된다. 확률을 다시 굴리지는 않는다(당첨됐지만 후보가
        // 없어 넘어가는 것으로 처리 - 해금이 늘어날수록 5%에 가까워진다).
        if (RunState.IsEndless && Random.value < AccessoryAppearChanceInEndless &&
            AccessoryCatalog.TryGetRandomUnlocked(out AccessoryData rolledAccessory))
        {
            return CreateAccessoryOffer(rolledAccessory);
        }

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

    /// <summary>
    /// 악세사리 6종 중 하나를 무작위로 골라 칸에 놓는다. 등급 개념이 없어(효과가 아예 없으니
    /// 등급을 매길 대상도 없다) 카드 헤더 색상용으로 <see cref="ItemGrade.Epic"/>을 고정으로
    /// 쓴다 - 5% 확률로만 뜨는 만큼 "발견하면 반가운" 정도의 색상이 되도록 고른 임의값이다.
    /// </summary>
    private Offer CreateAccessoryOffer(AccessoryData accessory)
    {
        return new Offer
        {
            IsAccessory = true,
            Accessory = accessory,
            Grade = ItemGrade.Epic,
            Price = Mathf.Max(0, accessory.price)
        };
    }

    // 이번 웨이브에 등장 가능한(등급 minWave를 만족하는) 디스크만 추린다.
    private List<DiscData> GetAvailableDiscs()
    {
        int wave = Mathf.Max(1, RunState.WaveNumber);
        var result = new List<DiscData>();

        foreach (DiscData disc in catalog.Discs)
        {
            // 해금되지 않은 디스크는 상점에 아예 뜨지 않는다(2026-08-19 Phase E).
            // 초기 해금 7종의 등급 분포가 일반 3 / 에픽 3 / 유니크 1이라 레어·전설 등급을
            // 뽑아도 후보가 비는데, CreateDiscOffer()가 등급을 아래로만 내려가며 찾으므로
            // 빈 칸이 되지는 않는다(레어 -> 일반, 전설 -> 유니크).
            if (!UnlockState.IsUnlocked(disc.discId)) continue;
            if (catalog.IsGradeAvailable(disc.grade, wave)) result.Add(disc);
        }

        return result;
    }

    /// <summary>
    /// 등급을 먼저 뽑고, <b>그 등급의 디스크 중에서</b> 하나를 고른다(무기와 동일한 규칙).
    /// 예전에는 등장 가능한 디스크 전체에서 균등 추첨해서, 등급이 하나씩 열릴 때마다
    /// 고등급 비중이 계단식으로 뛰었다(전설 의도 3% → 실제 9.5%). 등급별 보유 수가
    /// 제각각(일반 4 / 레어 5 / 에픽 6 / 유니크 4 / 전설 2)이라 균등 추첨은 등급 확률을
    /// 그대로 데이터 개수 비율로 만들어버린다.
    /// </summary>
    private Offer CreateDiscOffer(List<DiscData> availableDiscs)
    {
        ItemGrade rolled = catalog.RollGrade(Mathf.Max(1, RunState.WaveNumber));

        // 뽑은 등급에 디스크가 하나도 없으면 한 등급씩 낮춰가며 찾는다
        // (위로 올라가면 minWave 게이팅이 깨지므로 반드시 아래로만 내려간다).
        // DiscData는 struct라 null로 "못 찾음"을 표현할 수 없어 플래그를 따로 쓴다.
        DiscData disc = default;
        bool found = false;

        for (int g = (int)rolled; g >= 0 && !found; g--)
        {
            discGradeBuffer.Clear();
            foreach (DiscData candidate in availableDiscs)
            {
                if ((int)candidate.grade == g) discGradeBuffer.Add(candidate);
            }

            if (discGradeBuffer.Count > 0)
            {
                disc = discGradeBuffer[Random.Range(0, discGradeBuffer.Count)];
                found = true;
            }
        }

        // 모든 하위 등급이 비어 있는 비정상 상황에서만 전체에서 뽑는다.
        if (!found) disc = availableDiscs[Random.Range(0, availableDiscs.Count)];

        return new Offer
        {
            IsDisc = true,
            Disc = disc,
            Grade = disc.grade,
            Price = Mathf.Max(0, disc.price)
        };
    }

    // 등급별 디스크 후보를 담는 재사용 버퍼(매 추첨마다 리스트를 새로 만들지 않으려고).
    private readonly List<DiscData> discGradeBuffer = new List<DiscData>();

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
