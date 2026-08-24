using System.Collections.Generic;

/// <summary>
/// 해금(도감)의 대상 분류. 맵은 아트가 없어 이번 범위에서 제외했다(사용자 확정, 2026-08-19).
///
/// <b>무기(2026-08-20 추가)는 해금 조건이 없다</b>(사용자 확정: "무기는 해금 조건 없으니
/// 도감에 명세만 추가하면 됨") - 13종 전부 <see cref="UnlockEntry.UnlockedFromStart"/>로
/// 등록되어 처음부터 열려 있고, 도감은 그 스탯·설명을 보여주는 용도로만 쓰인다.
/// </summary>
public enum UnlockCategory
{
    Head,
    Disc,
    Accessory,
    Weapon
}

/// <summary>
/// 해금 진행도 카운터의 키. <see cref="UnlockState"/>가 이 키로 값을 쌓고,
/// <see cref="UnlockEntry.counterKey"/>가 같은 키를 가리키는 항목이 목표치에 도달하면 해금된다.
///
/// 세 가지 종류가 있고 값을 넣는 함수가 다르다.
/// - <b>누적</b>(처치 수 등): <see cref="UnlockState.AddProgress"/>
/// - <b>최고 기록</b>(방어력 10 달성 등): <see cref="UnlockState.ReportMax"/>
/// - <b>서로 다른 것의 개수</b>(다리 종류/무기 종류): <see cref="UnlockState.AddDistinct"/>
///   (아래 <see cref="DistinctKeys"/>에 등록된 키만 이 방식이다)
/// 조건이 "한 번이라도 있었나"인 것은 목표치 1짜리 누적으로 다룬다.
/// </summary>
public static class UnlockProgressKey
{
    public const string Kills = "kills";                        // 누적 처치 수(전체)
    public const string MeleeKills = "kills_melee";             // 근접 무기로 처치
    public const string PrecisionKills = "kills_precision";     // 정밀화기로 처치
    public const string ChargerKills = "kills_charger";
    public const string SprinterKills = "kills_sprinter";
    public const string BossKills = "kills_boss";
    public const string LowHpKills = "kills_lowhp";             // 체력 50 이하인 상태에서 처치

    public const string MaxDefense = "max_def";                 // 도달한 최고 방어력
    public const string MaxAttack = "max_atk";
    public const string MaxLuck = "max_luck";

    public const string SkillUses = "skill_uses";               // 스킬(스페이스바 - 다리 파츠 기술) 사용 횟수
    public const string SkillWithNonDefaultLegs = "skill_nondefault_legs";
    public const string HitsTaken = "hits_taken";               // 적에게 피격당한 횟수
    public const string DiscPurchases = "disc_purchases";       // 누적 디스크 구매 수
    public const string LevelsGained = "levels_gained";         // 누적 레벨업 횟수
    public const string Discs4Equipped = "discs_4_equipped";

    public const string HpBelow20 = "hp_below_20";              // HP 20 이하까지 떨어져 본 적 있음
    public const string HealOutsideWaveEnd = "heal_outside_wave_end";

    public const string Wave7Cleared = "wave7_cleared";
    public const string NoDamageWaveCleared = "wave_cleared_nodamage";
    public const string Hp200WaveCleared = "wave_cleared_hp200";
    public const string LowHpWaveCleared = "wave_cleared_hp30";
    public const string LegKindsCleared = "leg_kinds_cleared";  // 웨이브를 클리어해 본 다리 파츠 '종류'

    public const string EndlessEntered = "endless_entered";
    public const string EndlessKills = "endless_kills";
    public const string EndlessSprinterKills = "endless_kills_sprinter";
    public const string EndlessShopPurchases = "endless_shop_purchases";
    public const string EndlessLegendaryPurchases = "endless_legendary_purchases";
    public const string EndlessWeaponKinds = "endless_weapon_kinds";
    public const string Konami = "konami";

    /// <summary>"서로 다른 것의 개수"로 세는 키. 값 대신 ID 집합을 저장하고 그 크기를 진행도로 쓴다.</summary>
    public static readonly HashSet<string> DistinctKeys = new HashSet<string>
    {
        LegKindsCleared,
        EndlessWeaponKinds
    };
}

/// <summary>해금 대상 한 줄. 이름·아이콘은 각 카탈로그에 이미 있으므로 여기엔 조건만 담는다.</summary>
public struct UnlockEntry
{
    public UnlockCategory category;
    public int itemId;

    /// <summary>카탈로그를 못 찾았을 때 쓰는 이름(로그·비상용). 화면 표시는 각 카탈로그의 이름을 쓴다.</summary>
    public string fallbackName;

    /// <summary>비어 있으면 <b>초기 해금</b>(처음부터 열려 있음).</summary>
    public string counterKey;

    public int requiredAmount;

    /// <summary>도감에 그대로 보여줄 조건 문구.</summary>
    public string conditionText;

    public bool UnlockedFromStart => string.IsNullOrEmpty(counterKey);
}

/// <summary>
/// 도감 항목 52개(머리 12 + 디스크 21 + 악세사리 6 + 무기 13). 앞의 39개는
/// `20260818_해금기획서_Ver01_김재원.pdf` 전사본(작업.md 2026-08-19 계획 항목)을 그대로
/// 옮긴 것이다. **무기 13종(2026-08-20 추가)은 해금 기획서에 없던 항목이라 조건 없이
/// 전부 초기 해금으로 등록했다**(사용자 확정) - 도감에서는 명세(스탯)만 보여준다.
///
/// <b>디스크 이름 대응</b> — 해금 기획서와 디스크 기획서(Ver01)의 표기가 3건 다르다. 효과를
/// 대조해 아래처럼 대응시켰다(2026-08-19).
///   아침의 새 소리 = 포근한 치유(경험치 획득 시 회복 - 2026-08-24 사용자 지정으로 발동 시점이
///   "처치 시"에서 바뀌었다) / 생존 장치 = 마지막 발악(사망 직전 1회 생존) /
///   변압기 = 공명의 소리(공격력·방어력이 번갈아 바뀜 = 변압)
/// 나머지 18종은 이름이 그대로 일치한다.
/// </summary>
public static class UnlockCatalog
{
    public const int ComstockHeadId = 100001;

    public static readonly UnlockEntry[] All =
    {
        // ── 머리 12종 ────────────────────────────────────────────────────────────
        new UnlockEntry { category = UnlockCategory.Head, itemId = ComstockHeadId, fallbackName = "컴스톡 MK-01",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100003, fallbackName = "가드맨",
                          counterKey = UnlockProgressKey.MaxDefense, requiredAmount = 10,
                          conditionText = "방어력 10 달성" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100004, fallbackName = "메테우스",
                          counterKey = UnlockProgressKey.MaxAttack, requiredAmount = 20,
                          conditionText = "공격력 20 달성" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100005, fallbackName = "버서커",
                          counterKey = UnlockProgressKey.LowHpKills, requiredAmount = 50,
                          conditionText = "체력 50 이하일 때 적 50마리 처치" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100006, fallbackName = "해피 픽셀",
                          counterKey = UnlockProgressKey.MaxLuck, requiredAmount = 20,
                          conditionText = "행운 20 달성" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100007, fallbackName = "네온아이",
                          counterKey = UnlockProgressKey.BossKills, requiredAmount = 1,
                          conditionText = "보스 1회 처치" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100008, fallbackName = "핫팟",
                          counterKey = UnlockProgressKey.DiscPurchases, requiredAmount = 50,
                          conditionText = "디스크 누적 구매 50회" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100009, fallbackName = "픽시",
                          counterKey = UnlockProgressKey.MeleeKills, requiredAmount = 300,
                          conditionText = "근접 무기로 적 300마리 처치" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100010, fallbackName = "팬봇",
                          counterKey = UnlockProgressKey.SkillUses, requiredAmount = 100,
                          conditionText = "스킬 100번 사용" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100011, fallbackName = "소다캔",
                          counterKey = UnlockProgressKey.LegKindsCleared, requiredAmount = 5,
                          conditionText = "모든 종류의 다리 파츠를 착용하고 각각 웨이브 1회 이상 클리어" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100012, fallbackName = "프라이빗 컴스톡",
                          counterKey = UnlockProgressKey.PrecisionKills, requiredAmount = 200,
                          conditionText = "정밀화기로 적 200마리 처치" },
        new UnlockEntry { category = UnlockCategory.Head, itemId = 100013, fallbackName = "미니 픽시",
                          counterKey = UnlockProgressKey.LevelsGained, requiredAmount = 150,
                          conditionText = "누적 레벨 150 달성" },

        // ── 디스크 21종 (초기 해금 7종) ──────────────────────────────────────────
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400019, fallbackName = "교향곡: 번개 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400006, fallbackName = "포근한 치유 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400008, fallbackName = "숲의 소리 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400010, fallbackName = "교향곡: 화염 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400009, fallbackName = "이끼 낀 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400001, fallbackName = "네잎클로버 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400011, fallbackName = "금화의 잔향 디스크",
                          conditionText = "처음부터 사용할 수 있습니다" },

        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400018, fallbackName = "광분 바이러스 디스크",
                          counterKey = UnlockProgressKey.Kills, requiredAmount = 10,
                          conditionText = "적 10마리 처치" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400020, fallbackName = "물 빠지는 소리 디스크",
                          counterKey = UnlockProgressKey.HpBelow20, requiredAmount = 1,
                          conditionText = "HP 20 이하 달성" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400003, fallbackName = "777 디스크",
                          counterKey = UnlockProgressKey.Wave7Cleared, requiredAmount = 1,
                          conditionText = "웨이브 7 클리어" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400012, fallbackName = "바람 소리 디스크",
                          counterKey = UnlockProgressKey.NoDamageWaveCleared, requiredAmount = 1,
                          conditionText = "한 번도 피격당하지 않고 웨이브 클리어" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400015, fallbackName = "금속음 디스크",
                          counterKey = UnlockProgressKey.Hp200WaveCleared, requiredAmount = 1,
                          conditionText = "최대 체력 200 이상으로 웨이브 클리어" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400002, fallbackName = "염동력 디스크",
                          counterKey = UnlockProgressKey.Discs4Equipped, requiredAmount = 1,
                          conditionText = "디스크 4개 이상 착용" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400004, fallbackName = "마지막 발악 디스크",
                          counterKey = UnlockProgressKey.LowHpWaveCleared, requiredAmount = 1,
                          conditionText = "체력 30 이하로 웨이브 클리어" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400005, fallbackName = "에너지 베리어 디스크",
                          counterKey = UnlockProgressKey.HealOutsideWaveEnd, requiredAmount = 1,
                          conditionText = "웨이브 종료 회복 외의 방법으로 체력 회복" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400014, fallbackName = "위장 디스크",
                          counterKey = UnlockProgressKey.HitsTaken, requiredAmount = 30,
                          conditionText = "적에게 피격 30회" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400016, fallbackName = "은하수 디스크",
                          counterKey = UnlockProgressKey.SkillWithNonDefaultLegs, requiredAmount = 1,
                          conditionText = "기본 다리 외의 다리를 착용하고 스킬 사용" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400017, fallbackName = "공명의 소리 디스크",
                          counterKey = UnlockProgressKey.ChargerKills, requiredAmount = 10,
                          conditionText = "차저 10마리 처치" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400021, fallbackName = "교향곡: 파도 디스크",
                          counterKey = UnlockProgressKey.SprinterKills, requiredAmount = 10,
                          conditionText = "스프린터 10마리 처치" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400013, fallbackName = "결정의 마찰음 디스크",
                          counterKey = UnlockProgressKey.Kills, requiredAmount = 500,
                          conditionText = "적 500마리 처치" },
        new UnlockEntry { category = UnlockCategory.Disc, itemId = 400007, fallbackName = "교향곡: 암석 디스크",
                          counterKey = UnlockProgressKey.EndlessEntered, requiredAmount = 1,
                          conditionText = "엔드리스 모드 진입" },

        // ── 악세사리 6종 (전부 엔드리스 모드 조건) ───────────────────────────────
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600001, fallbackName = "8비트 선글라스",
                          counterKey = UnlockProgressKey.EndlessShopPurchases, requiredAmount = 1,
                          conditionText = "엔드리스 상점에서 아이템 1회 구매" },
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600002, fallbackName = "왕관",
                          counterKey = UnlockProgressKey.EndlessKills, requiredAmount = 300,
                          conditionText = "엔드리스에서 적 300마리 처치" },
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600003, fallbackName = "합격 목걸이",
                          counterKey = UnlockProgressKey.EndlessLegendaryPurchases, requiredAmount = 1,
                          conditionText = "엔드리스에서 전설 등급 장비 구매" },
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600004, fallbackName = "유니콘 뿔",
                          counterKey = UnlockProgressKey.EndlessWeaponKinds, requiredAmount = 6,
                          conditionText = "엔드리스에서 서로 다른 6종류의 무기 착용" },
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600005, fallbackName = "조이스틱",
                          counterKey = UnlockProgressKey.Konami, requiredAmount = 1,
                          conditionText = "엔드리스 진입 이후 위위아래아래좌우좌우 + 스페이스 2번 입력" },
        new UnlockEntry { category = UnlockCategory.Accessory, itemId = 600006, fallbackName = "의문의 검은 고양이 귀",
                          counterKey = UnlockProgressKey.EndlessSprinterKills, requiredAmount = 200,
                          conditionText = "엔드리스에서 스프린터 200마리 처치" },

        // ── 무기 13종 (전부 초기 해금 - 2026-08-20 사용자 확정, 명세만 보여주는 용도) ─────────
        // itemId는 각 종류의 일반 등급 행(Assets/Data/GameDataAsset.asset의 weapon_id)이다.
        // ID 체계는 WeaponTableGenerator.MakeWeaponId(kind, 0)과 동일: 300000 + 종류x100 + 1.
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300101, fallbackName = "중기관총",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300201, fallbackName = "전투산탄총",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300301, fallbackName = "대물저격총",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300401, fallbackName = "플라즈마캐논",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300501, fallbackName = "로켓런처",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300601, fallbackName = "소드오프샷건",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300701, fallbackName = "레이저피스톨",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300801, fallbackName = "지정사수소총",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 300901, fallbackName = "기관단총",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 301001, fallbackName = "유탄발사기",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 301101, fallbackName = "생존단검",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 301201, fallbackName = "전술 마체테",
                          conditionText = "처음부터 사용할 수 있습니다" },
        new UnlockEntry { category = UnlockCategory.Weapon, itemId = 301301, fallbackName = "전기톱검",
                          conditionText = "처음부터 사용할 수 있습니다" }
    };

    public static bool TryGet(int itemId, out UnlockEntry entry)
    {
        foreach (UnlockEntry e in All)
        {
            if (e.itemId != itemId) continue;
            entry = e;
            return true;
        }

        entry = default;
        return false;
    }

    /// <summary>도감 격자 한 칸씩을 만들 때 쓴다(카테고리 안 순서는 배열 순서 그대로).</summary>
    public static List<UnlockEntry> GetByCategory(UnlockCategory category)
    {
        var result = new List<UnlockEntry>();
        foreach (UnlockEntry e in All)
        {
            if (e.category == category) result.Add(e);
        }

        return result;
    }
}
