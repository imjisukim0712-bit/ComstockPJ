using UnityEngine;

/// <summary>
/// <see cref="HeadEffect"/> 12종의 실제 계산을 전부 모아둔 곳.
///
/// <b>왜 한 곳에 모았나</b> — 머리 효과의 훅 지점이 6개 파일에 흩어져 있다(피해량·공격속도는
/// PlayerShootManager, 스탯은 RobotStats, 구르기는 PlayerRobotController, 파츠 제한은
/// ModdingManager, 보상은 RewardPickup). 각 호출부가 `switch (head)`를 따로 들고 있으면
/// 머리를 하나 추가할 때마다 6곳을 고쳐야 하고, 그중 하나를 잊으면 조용히 반만 적용된다.
/// 그래서 호출부는 전부 "배율 하나만 물어보는" 형태로 두고 분기는 여기서만 한다.
///
/// <b>상수를 public const로 노출한 이유</b> — <see cref="HeadEffectExtensions.ToDescription"/>이
/// 이 값을 읽어 UI 문구를 생성한다. 밸런스를 조정하면 화면 설명이 저절로 따라오게 하려는 것으로,
/// AI 코어 3택 카드에서 고정 문구가 수치와 어긋났던 함정(2026-08-13)을 되풀이하지 않기 위함이다.
///
/// <b>바인딩</b> — 이 클래스는 스스로 FindObjectOfType을 돌리지 않는다. 매 발사마다 씬을 뒤지는
/// 비용을 피하려고 <see cref="Bind"/>/<see cref="RegisterPlayer"/>/<see cref="RegisterShootManager"/>로
/// 필요한 참조를 받아둔다. 아무것도 바인딩되지 않은 상태(타이틀 화면 등)에서도 모든 질의는
/// "효과 없음"에 해당하는 안전한 기본값을 돌려준다.
/// </summary>
public static class HeadEffects
{
    // ── 밸런스 상수 (기획서 Ver04 수치. 환산 근거는 작업.md 2026-08-19 항목 참고) ──────────

    /// <summary>컴스톡 MK-01 — [연사] 공격속도 증가분(0.15 = +15%).</summary>
    public const float ComstockRapidFireAttackSpeed = 0.15f;

    /// <summary>가드맨 — [산탄] 피해량 증가분(0.15 = +15%).</summary>
    public const float GuardmanShotgunDamage = 0.15f;

    /// <summary>가드맨 — "연속 2회 발사"의 <b>추가</b> 발사 횟수. 1 = 원래 1회 + 추가 1회 = 총 2회.</summary>
    public const int GuardmanExtraBursts = 1;

    /// <summary>가드맨 — 1회차와 2회차 사이 간격(초). 0이면 같은 프레임에 겹쳐 한 번처럼 보인다.</summary>
    public const float GuardmanBurstInterval = 0.12f;

    /// <summary>메테우스 — [폭발] 폭발 반경 증가분(0.20 = +20%).</summary>
    public const float MeteusSplashRadius = 0.20f;

    /// <summary>메테우스 — [폭발] 공격속도 증가분(0.10 = +10%).</summary>
    public const float MeteusAttackSpeed = 0.10f;

    /// <summary>버서커 — 효과가 켜지는 체력 비율(0.5 = 최대 체력의 50% 이하).</summary>
    public const float BerserkerHpThreshold = 0.5f;

    /// <summary>버서커 — 조건 충족 시 피해량 배율.</summary>
    public const float BerserkerDamage = 1.5f;

    /// <summary>버서커 — 조건 충족 시 공격속도 배율(기획서 원문의 "1,8배"는 1.8 오타).</summary>
    public const float BerserkerAttackSpeed = 1.8f;

    /// <summary>해피 픽셀 — 1중첩에 필요한 행운 수치.</summary>
    public const float HappyPixelLuckPerStack = 10f;

    /// <summary>해피 픽셀 — 1중첩당 이동속도 증가량(가산).</summary>
    public const float HappyPixelMoveSpeedPerStack = 1f;

    /// <summary>해피 픽셀 — 1중첩당 공격속도 배율(중첩마다 곱해진다).</summary>
    public const float HappyPixelAttackSpeedPerStack = 1.2f;

    /// <summary>해피 픽셀 — 최대 중첩 수.</summary>
    public const int HappyPixelMaxStacks = 3;

    /// <summary>네온아이 — 무기가 하나도 없을 때의 기본 공격력/공격속도 보너스 값.</summary>
    public const float NeonEyeBaseBonus = 7.5f;

    /// <summary>네온아이 — 장착 무기 1정당 위 보너스에서 깎이는 양.</summary>
    public const float NeonEyePenaltyPerWeapon = 1.25f;

    /// <summary>
    /// 네온아이 — 위 보너스를 공격속도 배율로 바꿀 때 나누는 값.
    ///
    /// 이 프로젝트에는 "공격속도" 로봇 스탯이 없어(무기별 weapon_atsp뿐) 기획서의 7.5를
    /// 그대로 더할 대상이 없다. 그래서 보너스를 <c>x(1 + 보너스/10)</c>로 환산했다 —
    /// 무기 1정이면 +62.5%, 6정이면 +0%. 퍼센트로 읽으면(+7.5%) 효과가 체감되지 않아
    /// 다른 머리들의 15~80% 범위와 맞추기 위한 선택이다(2026-08-19 사용자 확정: 배율로 넣고
    /// 플레이 후 조정).
    /// </summary>
    public const float NeonEyeAttackSpeedDivisor = 10f;

    /// <summary>네온아이 — 장착 무기 1정당 최대 체력 증가량.</summary>
    public const int NeonEyeHpPerWeapon = 10;

    /// <summary>네온아이 — 장착 무기 1정당 방어력 증가량.</summary>
    public const int NeonEyeDefPerWeapon = 2;

    /// <summary>핫팟 — 디스크 1개당 공격력·방어력·이동속도 가산량이자 공격속도 배율 증가분.</summary>
    public const float HotPotBonusPerDisc = 0.2f;

    /// <summary>픽시 — 모든 소켓이 근접일 때의 사거리 배율.</summary>
    public const float PixieAllMeleeRange = 2f;

    /// <summary>
    /// 픽시 — 원거리 무기를 끼웠을 때 그 무기의 사거리가 떨어지는 상한(유닛).
    /// 전술 마체테의 사거리(1.76유닛)를 "근접급"의 기준으로 삼았다.
    /// </summary>
    public const float PixieRangedPenaltyRange = 1.76f;

    /// <summary>소다캔 — (미구현) 조건 충족 시 피해량 배율. 문구 생성에만 쓰인다.</summary>
    public const float SodaCanDamage = 2f;

    /// <summary>프라이빗 컴스톡 — [정밀] 피해량 배율.</summary>
    public const float PrivateComstockDamage = 2f;

    // 2026-08-19 사용자 조정: 0.5(발사 간격 2배) → 0.75(발사 간격 1.33배)로 완화.
    /// <summary>프라이빗 컴스톡 — [정밀] 공격속도 배율(1보다 작으면 그만큼 발사 간격이 늘어난다).</summary>
    public const float PrivateComstockAttackSpeed = 0.75f;

    // 2026-08-19 사용자 조정: +2 → +1로 완화.
    /// <summary>프라이빗 컴스톡 — [정밀] 추가 관통 횟수.</summary>
    public const int PrivateComstockPierce = 1;

    /// <summary>미니 픽시 — 경험치 획득량 증가분(+0.5 = +50%).</summary>
    public const float MiniPixieExpBonus = 0.5f;

    /// <summary>미니 픽시 — 골드 수급량 변화분(-0.5 = -50%).</summary>
    public const float MiniPixieGoldBonus = -0.5f;

    // ── 바인딩 ────────────────────────────────────────────────────────────────────

    private static PartsCatalog catalog;
    private static PlayerRobotController player;
    private static PlayerShootManager shoot;

    // Current를 매번 카탈로그에서 조회하지 않도록 캐시한다. 머리는 런 중에 바뀌지 않으므로
    // 캐시가 무효해지는 시점은 Bind()와 로봇 선택 변경뿐이다.
    private static bool resolved;
    private static HeadEffect cached_effect;
    private static int cached_robot_id = int.MinValue;

    /// <summary>파츠 카탈로그를 연결한다(ModdingManager와 머리 선택 화면이 호출).</summary>
    public static void Bind(PartsCatalog value)
    {
        catalog = value;
        resolved = false;
    }

    public static void RegisterPlayer(PlayerRobotController value) => player = value;
    public static void RegisterShootManager(PlayerShootManager value) => shoot = value;

    /// <summary>씬 재시작 시 이전 판의 참조가 남지 않도록 비운다(카탈로그는 유지 - 에셋이라 판과 무관).</summary>
    public static void ResetRuntimeRefs()
    {
        player = null;
        shoot = null;
        resolved = false;
    }

    /// <summary>연결된 파츠 카탈로그. 아직 <see cref="Bind"/>되지 않았으면 null.</summary>
    public static PartsCatalog Catalog => catalog;

    /// <summary>
    /// 지금 선택된 머리의 데이터(스프라이트·기본 무기·소켓 수 등). 카탈로그가 없으면 기본값이
    /// 돌아오며 그 경우 <see cref="HeadModdingInfoIsValid"/>가 false다.
    /// 리그의 몸통 스프라이트와 UI 아이콘이 이 값을 읽는다.
    /// </summary>
    public static PartsCatalog.HeadModdingInfo CurrentHeadInfo =>
        catalog != null ? catalog.GetHeadModdingInfo(PlayerSession.SelectedRobotId) : default;

    /// <summary>카탈로그가 연결돼 있어 머리 데이터를 신뢰할 수 있는지(리그 데모 씬 등에서는 false).</summary>
    public static bool HeadModdingInfoIsValid => catalog != null;

    /// <summary>지금 선택된 머리의 고유 효과. 데이터가 없으면 <see cref="HeadEffect.None"/>.</summary>
    public static HeadEffect Current
    {
        get
        {
            int robotId = PlayerSession.SelectedRobotId;

            // 로봇이 바뀌었거나 아직 안 풀었으면 다시 조회한다.
            if (!resolved || robotId != cached_robot_id)
            {
                cached_robot_id = robotId;
                cached_effect = catalog != null ? catalog.GetHeadModdingInfo(robotId).effect : HeadEffect.None;
                resolved = true;
            }

            return cached_effect;
        }
    }

    // ── 무기 분류 조회 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 이 무기의 투사체 타입(연사/산탄/정밀/폭발/에너지/근접).
    ///
    /// <see cref="WeaponType"/>은 PartsCatalog.weaponMeta에 무기 65행 전부 이미 채워져 있는데
    /// 2026-08-19까지 런타임에서 아무도 읽지 않는 죽은 데이터였다(상점 분류 표시 1곳 제외).
    /// 머리 효과가 이 데이터의 첫 소비자다.
    /// </summary>
    private static bool TryGetType(WeaponData weapon, out WeaponType type)
    {
        if (catalog != null && catalog.TryGetWeaponMeta(weapon.weapon_id, out PartsCatalog.WeaponMetaEntry meta))
        {
            type = meta.type;
            return true;
        }

        type = default;
        return false;
    }

    private static bool IsType(WeaponData weapon, WeaponType want)
    {
        return TryGetType(weapon, out WeaponType type) && type == want;
    }

    // ── 피해량 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="PlayerShootManager.ComputeDamage"/>의 마지막에 곱해지는, "이번 발사의
    /// 최종 데미지 전체"에 적용되는 배율(로봇 공격력 분배분·치명타·디스크 효과까지 전부 포함된
    /// 값에 곱해진다). 근접·빔·투사체가 전부 그 함수를 거치므로 여기 한 곳이면 세 발사 방식에
    /// 모두 적용된다.
    ///
    /// <b>프라이빗 컴스톡의 [정밀] 공격력 x2는 여기 포함되지 않는다</b>(2026-08-19) -
    /// <see cref="WeaponAttackMultiplier"/> 참고.
    /// </summary>
    public static float DamageMultiplier(WeaponData weapon)
    {
        switch (Current)
        {
            case HeadEffect.Guardman:
                return IsType(weapon, WeaponType.Shotgun) ? 1f + GuardmanShotgunDamage : 1f;

            case HeadEffect.Berserker:
                return IsBerserkerActive() ? BerserkerDamage : 1f;

            // 소다캔: 로켓 엔진 다리 파츠와 이동속도 램프업 패시브가 프로젝트에 없어 조건을
            // 판정할 수가 없다. 다리 기획서가 나오면 여기서 SodaCanDamage를 돌려주면 된다.
            case HeadEffect.SodaCan:
                return 1f;

            default:
                return 1f;
        }
    }

    /// <summary>
    /// <see cref="PlayerShootManager.ComputeDamage"/>가 <b>무기 자체 위력(weapon_atk)에만</b>
    /// 곱하는 배율 - 로봇 전체 공격력(robot_atk, 슬롯 수만큼 균등 분배됨)에는 적용되면 안 되는
    /// 종류의 보너스를 위한 것이다. 현재는 프라이빗 컴스톡의 [정밀] 공격력 x2가 유일하다.
    ///
    /// 2026-08-19 버그 수정: 예전에는 이 배율이 <see cref="DamageMultiplier"/>에 섞여 있어
    /// robot_atk 분배분까지 함께 배로 늘어났다 - "정밀화기 장착 시 정밀화기의 공격력만 2배가
    /// 되어야 한다"는 사용자 확정에 따라 분리했다.
    /// </summary>
    public static float WeaponAttackMultiplier(WeaponData weapon)
    {
        if (Current != HeadEffect.PrivateComstock) return 1f;
        return IsType(weapon, WeaponType.Precision) ? PrivateComstockDamage : 1f;
    }

    /// <summary>버서커의 발동 조건(현재 체력이 최대치의 50% 이하). 플레이어 미바인딩 시 false.</summary>
    private static bool IsBerserkerActive()
    {
        if (player == null || player.MaxHp <= 0) return false;
        return (float)player.CurrentHp / player.MaxHp <= BerserkerHpThreshold;
    }

    // ── 공격속도 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 발사 대기시간 계산에 곱해지는 배율(1보다 크면 그만큼 빨리 쏜다).
    /// <see cref="PlayerShootManager"/>의 쿨다운 식에서 기존 임시 버프 배율과 함께 곱해진다.
    /// </summary>
    public static float AttackSpeedMultiplier(WeaponData weapon)
    {
        switch (Current)
        {
            case HeadEffect.ComstockMk01:
                return IsType(weapon, WeaponType.RapidFire) ? 1f + ComstockRapidFireAttackSpeed : 1f;

            case HeadEffect.Meteus:
                return IsType(weapon, WeaponType.Explosive) ? 1f + MeteusAttackSpeed : 1f;

            case HeadEffect.Berserker:
                return IsBerserkerActive() ? BerserkerAttackSpeed : 1f;

            case HeadEffect.HappyPixel:
                return Mathf.Pow(HappyPixelAttackSpeedPerStack, HappyPixelStacks());

            case HeadEffect.NeonEye:
                return 1f + Mathf.Max(0f, NeonEyeBonus()) / NeonEyeAttackSpeedDivisor;

            case HeadEffect.HotPot:
                return 1f + HotPotBonus();

            case HeadEffect.PrivateComstock:
                return IsType(weapon, WeaponType.Precision) ? PrivateComstockAttackSpeed : 1f;

            default:
                return 1f;
        }
    }

    // ── 폭발 반경 / 관통 / 발사 횟수 ────────────────────────────────────────────────

    /// <summary>메테우스의 폭발 범위 증가. 폭발 무기가 아니거나 다른 머리면 1.</summary>
    public static float SplashRadiusMultiplier(WeaponData weapon)
    {
        if (Current != HeadEffect.Meteus) return 1f;
        return IsType(weapon, WeaponType.Explosive) ? 1f + MeteusSplashRadius : 1f;
    }

    /// <summary>프라이빗 컴스톡의 추가 관통 횟수. 무제한 관통(-1) 무기는 호출부에서 건드리지 않는다.</summary>
    public static int BonusPierce(WeaponData weapon)
    {
        if (Current != HeadEffect.PrivateComstock) return 0;
        return IsType(weapon, WeaponType.Precision) ? PrivateComstockPierce : 0;
    }

    /// <summary>
    /// 가드맨의 "연속 2회 발사" — <b>추가</b> 발사 횟수를 돌려준다(0이면 평소대로 1회).
    /// 탄 수를 2배로 늘리는 방식이 아니라 짧은 간격을 두고 한 번 더 쏘는 방식이다
    /// (2026-08-19 사용자 확정).
    /// </summary>
    public static int ExtraBursts(WeaponData weapon)
    {
        if (Current != HeadEffect.Guardman) return 0;
        return IsType(weapon, WeaponType.Shotgun) ? GuardmanExtraBursts : 0;
    }

    // ── 사거리 ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 픽시의 사거리 배율 — 활성 소켓 전부에 근접무기를 끼웠을 때만 x2.
    /// 하나라도 원거리가 섞이면 배율이 없고, 대신 그 원거리 무기는
    /// <see cref="TryGetRangeCap"/>에서 근접급으로 잘린다.
    /// </summary>
    public static float RangeMultiplier(WeaponData weapon)
    {
        if (Current != HeadEffect.Pixie) return 1f;
        return IsAllSocketsMelee() ? PixieAllMeleeRange : 1f;
    }

    /// <summary>
    /// 픽시가 원거리 무기에 씌우는 사거리 상한. 근접무기이거나 다른 머리면 false.
    /// 사거리·감지거리 양쪽에 같은 상한을 걸어야 "감지했는데 탄이 안 닿는" 상태가 되지 않는다.
    /// </summary>
    public static bool TryGetRangeCap(WeaponData weapon, out float cap)
    {
        cap = 0f;
        if (Current != HeadEffect.Pixie) return false;
        if (IsType(weapon, WeaponType.Melee)) return false;

        cap = PixieRangedPenaltyRange;
        return true;
    }

    /// <summary>활성 소켓이 1개 이상이고 그 전부에 근접무기가 끼워져 있는지.</summary>
    private static bool IsAllSocketsMelee()
    {
        if (shoot == null) return false;

        int sockets = shoot.SocketCount;
        if (sockets <= 0) return false;

        for (int i = 0; i < sockets; i++)
        {
            if (!shoot.TryGetSocketInfo(i, out WeaponData weapon, out _)) return false; // 빈 소켓이 있으면 조건 미달
            if (!IsType(weapon, WeaponType.Melee)) return false;
        }

        return true;
    }

    // ── 스탯 (RobotStats.Compute에서 호출) ─────────────────────────────────────────

    /// <summary>
    /// 머리 효과가 만들어내는 스탯 증감을 집계 결과에 더한다.
    /// AI 코어/디스크/파츠 보너스를 다 더한 <b>뒤</b>, 무게 패널티와 하한 클램프 <b>전</b>에
    /// 호출된다 — 행운(해피 픽셀)과 디스크 수(핫팟)가 다른 보너스로 늘어난 값까지 반영되어야
    /// 하기 때문이다.
    /// </summary>
    public static void ApplyStatBonuses(ref AggregatedRobotStats stats)
    {
        switch (Current)
        {
            case HeadEffect.HappyPixel:
                // 이동속도만 스탯이고 공격속도는 AttackSpeedMultiplier가 담당한다.
                stats.MoveSpeed += HappyPixelMoveSpeedPerStack * HappyPixelStacks(stats.Luck);
                break;

            case HeadEffect.NeonEye:
            {
                int weapons = EquippedWeaponCount();
                float bonus = NeonEyeBonus(weapons);

                // 보너스가 음수가 될 수 있다(무기 7정 이상). 공격력은 그대로 반영하고
                // RobotStats의 하한 클램프(Atk >= 0)가 받아준다.
                // 2026-08-20 스탯 소수화: 반올림 없이 그대로 더한다.
                stats.Atk += bonus;
                stats.MaxHp += NeonEyeHpPerWeapon * weapons;
                stats.Def += NeonEyeDefPerWeapon * weapons;
                break;
            }

            case HeadEffect.HotPot:
            {
                float bonus = HotPotBonus();
                stats.Atk += bonus;
                stats.Def += bonus;
                stats.MoveSpeed += bonus;
                break;
            }
        }
    }

    /// <summary>해피 픽셀의 중첩 수. 인자를 안 주면 현재 플레이어의 행운을 읽는다.</summary>
    private static int HappyPixelStacks()
    {
        return player != null ? HappyPixelStacks(player.Luck) : 0;
    }

    private static int HappyPixelStacks(float luck)
    {
        // HappyPixelLuckPerStack은 const 양수라 0 나눗셈 가드가 필요 없다
        // (가드를 두면 컴파일러가 도달 불가 코드로 경고한다).
        return Mathf.Clamp(Mathf.FloorToInt(luck / HappyPixelLuckPerStack), 0, HappyPixelMaxStacks);
    }

    /// <summary>네온아이의 현재 보너스 값(공격력에 그대로, 공격속도엔 /10해서 쓰인다).</summary>
    private static float NeonEyeBonus() => NeonEyeBonus(EquippedWeaponCount());

    private static float NeonEyeBonus(int weaponCount) => NeonEyeBaseBonus - NeonEyePenaltyPerWeapon * weaponCount;

    /// <summary>핫팟의 디스크 수 기반 보너스.</summary>
    private static float HotPotBonus() => HotPotBonusPerDisc * RunState.EquippedDiscIds.Count;

    /// <summary>
    /// 실제로 무기가 들어있는 소켓 개수. <see cref="RunState.EquippedWeapons"/>는 빈 소켓도
    /// WeaponId 0으로 자리를 채워두므로 0을 걸러내야 한다.
    /// </summary>
    private static int EquippedWeaponCount()
    {
        int count = 0;
        foreach (RunState.EquippedWeapon w in RunState.EquippedWeapons)
        {
            if (w.WeaponId > 0) count++;
        }
        return count;
    }

    // ── 구르기 (팬봇) ──────────────────────────────────────────────────────────────

    /// <summary>구르기 쿨다운에 곱해지는 배율. 팬봇은 0이라 쿨다운 없이 계속 구를 수 있다.</summary>
    public static float RollCooldownMultiplier => Current == HeadEffect.FanBot ? 0f : 1f;

    /// <summary>구르는 동안 무적인지. 팬봇만 false(무제한 구르기의 대가로 무적을 뺐다).</summary>
    public static bool RollGrantsInvincibility => Current != HeadEffect.FanBot;

    // ── 파츠 장착 제한 (팬봇) ──────────────────────────────────────────────────────

    /// <summary>
    /// 이 파츠를 장착할 수 있는지. 팬봇은 "기본 다리만 착용 가능"이라 다리 계열
    /// (다리/다리 장갑/발)에서 기본 파츠가 아닌 것을 거른다.
    /// 다리 계열이 아닌 파츠와 다른 머리는 항상 true.
    /// </summary>
    public static bool IsPartAllowed(PartData part)
    {
        if (Current != HeadEffect.FanBot) return true;

        bool isLegFamily = part.slot == PartSlot.Leg || part.slot == PartSlot.LegArmor || part.slot == PartSlot.Foot;
        if (!isLegFamily) return true;

        return part.isDefaultStarter;
    }

    /// <summary>장착이 막혔을 때 UI에 띄울 이유. 막히지 않으면 null.</summary>
    public static string GetPartBlockReason(PartData part)
    {
        return IsPartAllowed(part) ? null : "팬봇은 기본 다리만 장착할 수 있습니다";
    }

    // ── 보상 (미니 픽시) ───────────────────────────────────────────────────────────

    /// <summary>경험치 획득량에 곱해지는 배율.</summary>
    public static float ExpGainMultiplier => Current == HeadEffect.MiniPixie ? 1f + MiniPixieExpBonus : 1f;

    /// <summary>골드 수급량에 곱해지는 배율.</summary>
    public static float GoldGainMultiplier => Current == HeadEffect.MiniPixie ? 1f + MiniPixieGoldBonus : 1f;
}
