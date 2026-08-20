using UnityEngine;

/// <summary>
/// 게임에서 실제로 일어난 일을 <see cref="UnlockState"/>의 진행도로 옮기는 곳(2026-08-19 Phase E).
///
/// <b>왜 한 곳에 모았나</b> — 해금 조건 39개의 훅 지점이 10개 파일에 흩어져 있다(처치·피격·회복·
/// 스킬·상점·웨이브 종료·레벨업·스탯). 각 호출부가 `UnlockState.AddProgress("...")`를 직접
/// 부르면 키 문자열이 온 프로젝트에 흩어지고 조건이 바뀔 때 추적이 어려워진다. 호출부는
/// "무슨 일이 일어났는지"만 알리고, 그것이 어떤 진행도인지는 여기서만 판단한다
/// (<see cref="HeadEffects"/>가 머리 효과를 한곳에 모은 것과 같은 이유).
///
/// 웨이브 하나 동안만 유효한 상태(무피격 여부)도 여기서 들고 있다.
/// </summary>
public static class UnlockTracker
{
    /// <summary>버서커 조건("체력 50 이하일 때 적 50마리 처치")의 체력 기준.</summary>
    private const int LowHpThreshold = 50;

    /// <summary>물 빠지는 소리 조건("HP 20 이하 달성")의 기준.</summary>
    private const int HpBelowThreshold = 20;

    /// <summary>금속음 조건("체력 200 달성 후 웨이브 클리어")의 기준.</summary>
    private const int HighMaxHpThreshold = 200;

    /// <summary>마지막 발악(생존 장치) 조건("체력 30 이하로 웨이브 클리어")의 기준.</summary>
    private const int LowHpClearThreshold = 30;

    /// <summary>기본 다리 파츠 ID. 이것 외의 다리를 끼고 스킬을 쓰면 은하수 디스크가 열린다.</summary>
    public const int DefaultLegPartId = 500004;

    private static PlayerRobotController player;
    private static bool subscribed;
    private static bool damagedThisWave;

    /// <summary>플레이어 등록 + 처치 이벤트 구독. <see cref="PlayerRobotController.Awake"/>에서
    /// <see cref="RunScore.EnsureKillTrackingSubscribed"/>와 같은 자리에 호출한다.
    /// 처치 이벤트가 static이라 씬을 재시작해도 이전 판 구독이 남을 수 있어 -= 후 +=로 붙인다.</summary>
    public static void RegisterPlayer(PlayerRobotController value)
    {
        player = value;
        damagedThisWave = false;

        EnemyUnit.OnKilledByPlayer -= HandleEnemyKilled;
        EnemyUnit.OnKilledByPlayer += HandleEnemyKilled;
        subscribed = true;
    }

    public static bool IsSubscribed => subscribed;

    // ── 전투 ──────────────────────────────────────────────────────────────────────

    private static void HandleEnemyKilled(EnemyUnit unit)
    {
        UnlockState.AddProgress(UnlockProgressKey.Kills);

        bool endless = RunState.IsEndless;
        if (endless) UnlockState.AddProgress(UnlockProgressKey.EndlessKills);

        if (unit is BossUnit) UnlockState.AddProgress(UnlockProgressKey.BossKills);
        else if (unit is ChargerUnit) UnlockState.AddProgress(UnlockProgressKey.ChargerKills);
        else if (unit is SprinterUnit)
        {
            UnlockState.AddProgress(UnlockProgressKey.SprinterKills);
            if (endless) UnlockState.AddProgress(UnlockProgressKey.EndlessSprinterKills);
        }

        if (player != null && player.CurrentHp <= LowHpThreshold)
            UnlockState.AddProgress(UnlockProgressKey.LowHpKills);

        // 마지막에 이 적을 때린 무기의 분류(근접/정밀)를 그대로 쓴다. 무기 없이 죽은 경우
        // (디스크의 연쇄 번개 등)는 weaponId가 0이라 아무 카운터도 오르지 않는다.
        if (unit != null && TryGetWeaponType(unit.LastDamageWeaponId, out WeaponType type))
        {
            if (type == WeaponType.Melee) UnlockState.AddProgress(UnlockProgressKey.MeleeKills);
            else if (type == WeaponType.Precision) UnlockState.AddProgress(UnlockProgressKey.PrecisionKills);
        }
    }

    /// <summary>플레이어가 실제로 피해를 입었을 때(회피·무적으로 무효화된 경우는 제외).</summary>
    public static void ReportPlayerDamaged(float currentHp)
    {
        damagedThisWave = true;
        UnlockState.AddProgress(UnlockProgressKey.HitsTaken);

        if (currentHp <= HpBelowThreshold) UnlockState.ReportMax(UnlockProgressKey.HpBelow20, 1);
    }

    /// <summary>체력 회복. 웨이브 종료 시의 전체 회복만 <paramref name="fromWaveEnd"/>가 true다.</summary>
    public static void ReportHealed(bool fromWaveEnd)
    {
        if (fromWaveEnd) return;
        UnlockState.ReportMax(UnlockProgressKey.HealOutsideWaveEnd, 1);
    }

    /// <summary>스킬(스페이스바 - 다리 파츠의 기술) 사용. 실제로 발동한 경우에만 부른다.</summary>
    public static void ReportSkillUsed()
    {
        UnlockState.AddProgress(UnlockProgressKey.SkillUses);

        if (IsNonDefaultLegEquipped()) UnlockState.ReportMax(UnlockProgressKey.SkillWithNonDefaultLegs, 1);
    }

    // ── 성장 ──────────────────────────────────────────────────────────────────────

    /// <summary>스탯이 다시 계산될 때마다 도달한 최고치를 기록한다(가드맨/메테우스/해피 픽셀).</summary>
    public static void ReportStats(float defense, float attack, float luck)
    {
        // 해금 조건은 정수 기준이라 소수 스탯은 내림해서 기록한다(2026-08-20 스탯 소수화).
        UnlockState.ReportMax(UnlockProgressKey.MaxDefense, Mathf.FloorToInt(defense));
        UnlockState.ReportMax(UnlockProgressKey.MaxAttack, Mathf.FloorToInt(attack));
        UnlockState.ReportMax(UnlockProgressKey.MaxLuck, Mathf.FloorToInt(luck));
    }

    /// <summary>AI 코어 레벨업 1회(미니 픽시의 "누적 레벨 150").</summary>
    public static void ReportLevelUp() => UnlockState.AddProgress(UnlockProgressKey.LevelsGained);

    // ── 상점 ──────────────────────────────────────────────────────────────────────

    /// <summary>디스크 구매(핫팟) + 구매 후 장착 개수(염동력의 "디스크 4개 이상 착용").</summary>
    public static void ReportDiscPurchased(int equippedDiscCount)
    {
        UnlockState.AddProgress(UnlockProgressKey.DiscPurchases);
        if (equippedDiscCount >= 4) UnlockState.ReportMax(UnlockProgressKey.Discs4Equipped, 1);
    }

    /// <summary>상점에서 무엇이든 구매했을 때(악세사리 해금 조건은 전부 엔드리스 기준이다).</summary>
    public static void ReportShopPurchase(ItemGrade grade)
    {
        if (!RunState.IsEndless) return;

        UnlockState.AddProgress(UnlockProgressKey.EndlessShopPurchases);
        if (grade == ItemGrade.Legendary) UnlockState.AddProgress(UnlockProgressKey.EndlessLegendaryPurchases);
    }

    /// <summary>무기를 소켓에 장착했을 때. 유니콘 뿔의 "서로 다른 6종류"는 무기 <b>분류</b>
    /// (연사/산탄/정밀/폭발/에너지/근접) 6가지를 뜻한다 - 같은 분류의 다른 등급을 여러 개 사도
    /// 1종류로 센다.</summary>
    public static void ReportWeaponEquipped(int weaponId)
    {
        if (!RunState.IsEndless) return;
        if (!TryGetWeaponType(weaponId, out WeaponType type)) return;

        UnlockState.AddDistinct(UnlockProgressKey.EndlessWeaponKinds, (int)type);
    }

    // ── 웨이브 / 모드 ─────────────────────────────────────────────────────────────

    public static void ReportWaveStarted() => damagedThisWave = false;

    /// <summary>웨이브를 클리어한 순간(체력 회복 전에 부른다 - 체력 조건 2건이 여기에 걸린다).</summary>
    public static void ReportWaveCleared(int waveNumber)
    {
        if (waveNumber >= 7) UnlockState.ReportMax(UnlockProgressKey.Wave7Cleared, 1);
        if (!damagedThisWave) UnlockState.ReportMax(UnlockProgressKey.NoDamageWaveCleared, 1);

        if (player != null)
        {
            if (player.MaxHp >= HighMaxHpThreshold) UnlockState.ReportMax(UnlockProgressKey.Hp200WaveCleared, 1);
            if (player.CurrentHp <= LowHpClearThreshold) UnlockState.ReportMax(UnlockProgressKey.LowHpWaveCleared, 1);
        }

        // 소다캔 - 이번 웨이브를 클리어할 때 신고 있던 다리 '종류'를 기록한다. 같은 다리의
        // 다른 등급은 ID 끝자리만 다르므로(예: 유압 다리 510090~510094) 10으로 나눠 묶는다.
        UnlockState.AddDistinct(UnlockProgressKey.LegKindsCleared, CurrentLegPartId() / 10);

        damagedThisWave = false;
        UnlockState.Flush();
    }

    public static void ReportEndlessEntered() => UnlockState.ReportMax(UnlockProgressKey.EndlessEntered, 1);

    /// <summary>코나미 커맨드 입력(조이스틱). 엔드리스에서만 인정한다.</summary>
    public static void ReportKonamiCode()
    {
        if (!RunState.IsEndless) return;
        UnlockState.ReportMax(UnlockProgressKey.Konami, 1);
    }

    // ── 보조 ──────────────────────────────────────────────────────────────────────

    private static bool TryGetWeaponType(int weaponId, out WeaponType type)
    {
        type = default;
        if (weaponId <= 0) return false;

        PartsCatalog catalog = HeadEffects.Catalog;
        if (catalog == null) return false;

        if (!catalog.TryGetWeaponMeta(weaponId, out PartsCatalog.WeaponMetaEntry meta)) return false;

        type = meta.type;
        return true;
    }

    private static int CurrentLegPartId()
    {
        return RunState.EquippedPartIds.TryGetValue(PartSlot.Leg.ToString(), out int partId) && partId > 0
            ? partId
            : DefaultLegPartId;
    }

    private static bool IsNonDefaultLegEquipped() => CurrentLegPartId() != DefaultLegPartId;
}
