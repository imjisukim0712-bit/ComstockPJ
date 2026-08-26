using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 파츠 테이블(헬멧 / 다리장갑 / 무기소켓 / 메모리 / 디스크슬롯)을 명세대로 재생성한다.
/// `WeaponTableGenerator`(무기 65행) · `DiscTableGenerator`(디스크 21종)와 같은 패턴이다.
///
/// <b>출처</b>: 2026-08-20 사용자 제공 명세 2종
///  - `20260820_컴스톡장갑명세_Ver01_김지수.md` (헬멧 6종 / 다리장갑 4종)
///  - `컴스톡_소켓_메모리_자기장코어_디스크슬롯_명세.md` (소켓 7종 / 메모리 3종 / 디스크슬롯 5종)
///
/// <b>등급 수치는 명세에 5개 값이 그대로 적혀 있으므로 배율 계산을 하지 않는다</b>
/// (예: 행운 +2/2.5/3/4/5). 값이 하나만 적힌 항목은 등급과 무관한 고정 수치라 5등급 모두 같다.
///
/// <b>이 생성기는 위 5개 슬롯의 "루트 파츠"만 갈아끼운다.</b>
///  - 슬롯당 1개씩 있는 기본 장착 파츠(`isDefaultStarter`)는 손대지 않는다(사용자 확정 2026-08-20).
///  - 다리 슬롯은 명세에 없어 그대로 둔다(팔 장갑·자기장 코어도 여기 있었지만 2026-08-26에
///    슬롯 자체가 삭제됐다 - PartSlot.cs 참고).
/// 무게는 명세에 없으므로 각 슬롯의 기존 임시값을 승계한다(헬멧 1.5 / 다리장갑 2 / 소켓 2 /
/// 메모리 0 / 디스크슬롯 0.8).
/// </summary>
public static class PartsTableGenerator
{
    private const string CatalogPath = "Assets/Data/PartsCatalog.asset";

    /// <summary>등급 5개 값. 값을 1개만 주면 5등급 전부 같은 값으로 취급한다.</summary>
    private static float[] V(params float[] values)
    {
        if (values == null || values.Length == 0) return null;
        if (values.Length == 1) return new[] { values[0], values[0], values[0], values[0], values[0] };
        return values;
    }

    private static int[] I(params int[] values)
    {
        if (values == null || values.Length == 0) return null;
        if (values.Length == 1) return new[] { values[0], values[0], values[0], values[0], values[0] };
        return values;
    }

    /// <summary>파츠 "종류" 하나의 정의. 등급 5행은 이 정의에서 파생 생성한다.</summary>
    private struct Def
    {
        public int idBase;          // partId = idBase + 등급인덱스
        public string name;
        public PartSlot slot;
        public string icon;         // Resources/PartIcons/ 아래 파일명
        public float weight;

        public StatType stat1;
        public float[] amount1;     // null이면 없음
        public StatType stat2;
        public float[] amount2;

        public PartEffect effect;
        public StatType effectStat;
        public float[] effectAmount;

        public int[] discSlots;
        public int[] coreMaxLevel;

        // 무기 소켓 전용
        public bool restrictsType;
        public WeaponType allowedType;
        public float[] socketAtkSpeedPct;
        public float[] socketDamageFlat;
        public float[] socketDamagePct;
        public float[] socketCritPct;
        public float[] socketSplashPct;
        public float[] socketDefIgnorePct;
    }

    private const float HelmetWeight = 1.5f;
    private const float LegArmorWeight = 2f;
    private const float SocketWeight = 2f;
    private const float MemoryWeight = 0f;
    private const float DiscSlotWeight = 0.8f;

    private static readonly Def[] Defs =
    {
        // ── 헬멧 6종 (머리부 장갑의 새 명칭) ──────────────────────────────
        new Def {
            idBase = 530000, name = "연극 마스크", slot = PartSlot.Helmet,
            icon = "Helmet_TheaterMask", weight = HelmetWeight,
            stat1 = StatType.Luck, amount1 = V(2f, 2.5f, 3f, 4f, 5f),
            effect = PartEffect.DefFromLuckPercent, effectAmount = V(15f)
        },
        new Def {
            idBase = 530010, name = "행운의 양동이", slot = PartSlot.Helmet,
            icon = "Helmet_LuckyBucket", weight = HelmetWeight,
            stat1 = StatType.Def, amount1 = V(3f),
            stat2 = StatType.Avoid, amount2 = V(3f, 3.3f, 4.4f, 5.5f, 7f)
        },
        new Def {
            idBase = 530020, name = "방탄모", slot = PartSlot.Helmet,
            icon = "Helmet_BallisticHelmet", weight = HelmetWeight,
            stat1 = StatType.Def, amount1 = V(4f),
            effect = PartEffect.DefPercentBonus, effectAmount = V(10f, 12f, 15f, 18f, 20f)
        },
        new Def {
            idBase = 530030, name = "탈", slot = PartSlot.Helmet,
            icon = "Helmet_Imaetal", weight = HelmetWeight,
            stat1 = StatType.MaxHp, amount1 = V(10f, 15f, 20f, 25f, 40f),
            effect = PartEffect.DefWhenLowHp, effectAmount = V(5f)
        },
        new Def {
            idBase = 530040, name = "철인 헬멧", slot = PartSlot.Helmet,
            icon = "Helmet_IronHelmet", weight = HelmetWeight,
            stat1 = StatType.Atk, amount1 = V(3f, 4.5f, 6f, 7f, 9f),
            effect = PartEffect.DefFromAtkPercent, effectAmount = V(5f)
        },
        new Def {
            idBase = 530050, name = "빵봉투", slot = PartSlot.Helmet,
            icon = "Helmet_BreadBag", weight = HelmetWeight,
            stat1 = StatType.CritChance, amount1 = V(10f),
            stat2 = StatType.Def, amount2 = V(-10f)
        },

        // ── 다리장갑 4종 ────────────────────────────────────────────────
        new Def {
            idBase = 531000, name = "양철 플레이트", slot = PartSlot.LegArmor,
            icon = "LegArmor_TinPlate", weight = LegArmorWeight,
            stat1 = StatType.Def, amount1 = V(3f, 3.5f, 4f, 4.5f, 5f)
        },
        new Def {
            idBase = 531010, name = "가시 플레이트", slot = PartSlot.LegArmor,
            icon = "LegArmor_SpikePlate", weight = LegArmorWeight,
            effect = PartEffect.MeleeReflectPercent, effectAmount = V(10f, 12f, 15f, 17f, 20f)
        },
        new Def {
            idBase = 531020, name = "경사 장갑", slot = PartSlot.LegArmor,
            icon = "LegArmor_SlopedArmor", weight = LegArmorWeight,
            stat1 = StatType.Avoid, amount1 = V(4f, 4.6f, 5.2f, 6f, 8f)
        },
        new Def {
            idBase = 531030, name = "반응형 장갑", slot = PartSlot.LegArmor,
            icon = "LegArmor_ReactiveArmor", weight = LegArmorWeight,
            stat1 = StatType.Def, amount1 = V(2f, 2.4f, 3f, 3.5f, 4.5f),
            stat2 = StatType.Avoid, amount2 = V(2f, 2.4f, 3f, 3.5f, 4.5f)
        },

        // ── 무기 소켓 7종 (장착 가능한 무기 카테고리를 결정) ─────────────
        new Def {
            idBase = 532000, name = "연사 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_RapidFire", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.RapidFire,
            socketAtkSpeedPct = V(5f, 7f, 10f, 13f, 17f),
            socketDamageFlat = V(0.3f, 0.6f, 0.9f, 1.2f, 1.5f)
        },
        new Def {
            idBase = 532010, name = "산탄 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Shotgun", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.Shotgun,
            socketAtkSpeedPct = V(5f, 7f, 10f, 13f, 17f),
            socketDamageFlat = V(0.3f, 0.6f, 0.9f, 1.2f, 1.5f)
        },
        new Def {
            idBase = 532020, name = "정밀 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Precision", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.Precision,
            socketCritPct = V(4f, 6f, 9f, 12f, 16f),
            socketDamageFlat = V(0.3f, 0.6f, 0.9f, 1.2f, 1.5f)
        },
        new Def {
            idBase = 532030, name = "폭발 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Explosive", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.Explosive,
            socketCritPct = V(4f, 6f, 9f, 12f, 16f),
            socketSplashPct = V(6f, 9f, 13f, 17f, 22f)
        },
        new Def {
            idBase = 532040, name = "에너지 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Energy", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.Energy,
            socketDefIgnorePct = V(4f, 6f, 9f, 12f, 16f),
            socketDamageFlat = V(0.3f, 0.6f, 0.9f, 1.2f, 1.5f)
        },
        new Def {
            idBase = 532050, name = "근접 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Melee", weight = SocketWeight,
            restrictsType = true, allowedType = WeaponType.Melee,
            socketDamagePct = V(4f, 7f, 11f, 15f, 20f),
            socketAtkSpeedPct = V(5f, 7f, 10f, 13f, 17f)
        },
        new Def {
            idBase = 532060, name = "범용 소켓", slot = PartSlot.ArmWeaponSocket,
            icon = "Socket_Universal", weight = SocketWeight,
            restrictsType = false,  // 어떤 카테고리든 받는다(대신 카테고리 전용 보정 없음)
            socketAtkSpeedPct = V(3f, 5f, 7f, 9f, 12f),
            socketCritPct = V(6f, 9f, 12f, 16f, 20f)
        },

        // ── 메모리 3종 (AI 코어 최대 레벨을 결정) ───────────────────────
        new Def {
            idBase = 533000, name = "메모리 칩", slot = PartSlot.Memory,
            icon = "Memory_Chip", weight = MemoryWeight,
            coreMaxLevel = I(15, 25, 35, 43, 50)
        },
        new Def {
            // 2026-08-26 사용자 지시로 "AI 코어 시작 레벨 +15~35"(PartEffect.CoreStartLevel)를
            // <b>최대 레벨 +20~45</b>로 교체했다. 시작 레벨 개념은 지금 게임 구조와 맞지 않는다 -
            // 파츠를 런 도중에 부품 상자로 얻으므로 "시작"이랄 것이 없고, 장착하는 순간 레벨
            // 15~35개가 한꺼번에 지급돼 업그레이드 카드가 무더기로 쌓였다.
            // 세 메모리의 역할 구분: 칩 = 상한만(최대 +50) / 뉴럴 캐시 = 상한 + 성장 속도 /
            // 아카식 = 상한 + 성장 + 골드.
            idBase = 533010, name = "뉴럴 캐시", slot = PartSlot.Memory,
            icon = "Memory_NeuralCache", weight = MemoryWeight,
            coreMaxLevel = I(20, 26, 32, 38, 45),
            stat1 = StatType.ExpGain, amount1 = V(10f, 15f, 20f, 25f, 30f)
        },
        new Def {
            idBase = 533020, name = "아카식 레지스터", slot = PartSlot.Memory,
            icon = "Memory_AkashicRegister", weight = MemoryWeight,
            coreMaxLevel = I(15, 20, 25, 30, 35),
            stat1 = StatType.ExpGain, amount1 = V(5f, 10f, 15f, 20f, 25f),
            stat2 = StatType.GoldGain, amount2 = V(5f, 10f, 15f, 20f, 25f)
        },

        // ── 디스크 슬롯 5종 ────────────────────────────────────────────
        new Def {
            idBase = 534000, name = "기본 슬롯", slot = PartSlot.DiscSlot,
            icon = "DiscSlot_Basic", weight = DiscSlotWeight,
            discSlots = I(4, 5, 6, 7, 8),
            stat1 = StatType.Def, amount1 = V(2.5f)
        },
        new Def {
            idBase = 534010, name = "확장 프레임", slot = PartSlot.DiscSlot,
            icon = "DiscSlot_ExpansionFrame", weight = DiscSlotWeight,
            discSlots = I(7, 8, 9, 10, 12),
            effect = PartEffect.PerDiscStat, effectStat = StatType.MaxHp,
            effectAmount = V(5f, 6f, 8f, 10f, 13f)
        },
        new Def {
            idBase = 534020, name = "코어 연결망", slot = PartSlot.DiscSlot,
            icon = "DiscSlot_CoreNetwork", weight = DiscSlotWeight,
            discSlots = I(5, 6, 7, 8, 9),
            effect = PartEffect.PerDiscStat, effectStat = StatType.Atk,
            effectAmount = V(2f, 2.5f, 3.5f, 5f, 7f)
        },
        new Def {
            idBase = 534030, name = "허브 접속기", slot = PartSlot.DiscSlot,
            icon = "DiscSlot_HubAdapter", weight = DiscSlotWeight,
            discSlots = I(5, 6, 7, 8, 9),
            effect = PartEffect.PerDiscStat, effectStat = StatType.Def,
            effectAmount = V(3f, 4f, 5.5f, 7f, 10f)
        },
        new Def {
            idBase = 534040, name = "교향곡 모음집", slot = PartSlot.DiscSlot,
            icon = "DiscSlot_SymphonyCollection", weight = DiscSlotWeight,
            discSlots = I(6, 7, 8, 9, 11),
            effect = PartEffect.PerSymphonyDiscAtk, effectAmount = V(0.1f)
        }
    };

    /// <summary>이 생성기가 갈아끼우는 슬롯. 나머지 슬롯의 파츠는 건드리지 않는다.</summary>
    private static readonly PartSlot[] ManagedSlots =
    {
        PartSlot.Helmet, PartSlot.LegArmor, PartSlot.ArmWeaponSocket, PartSlot.Memory, PartSlot.DiscSlot
    };

    [MenuItem("Comstock/파츠 테이블 25종 재생성")]
    public static void Generate()
    {
        PartsCatalog catalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>(CatalogPath);
        if (catalog == null)
        {
            Debug.LogError($"[파츠 생성기] 카탈로그를 찾지 못했습니다: {CatalogPath}");
            return;
        }

        List<PartData> parts = catalog.EditorGetParts();
        int before = parts.Count;

        // 관리 대상 슬롯의 <b>루트 파츠</b>(기본 장착이 아닌 것)만 제거한다.
        int removed = parts.RemoveAll(p => !p.isDefaultStarter && System.Array.IndexOf(ManagedSlots, p.slot) >= 0);

        // 표준 메모리 카드는 예전 방식(절대값 50으로 최대 레벨을 대체)의 값을 들고 있어서,
        // 가산 방식(ModdingManager.CoreMaxLevel = 머리 기본 50 + 파츠 가산)으로 바뀐 뒤에는
        // 그대로 두면 최대 레벨이 100이 된다. 기본 파츠는 보너스가 없어야 하므로 0으로 맞춘다.
        int fixedStarter = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].slot != PartSlot.Memory || !parts[i].isDefaultStarter) continue;
            if (parts[i].coreMaxLevelBonus == 0) continue;

            PartData starter = parts[i];
            starter.coreMaxLevelBonus = 0;
            parts[i] = starter;
            fixedStarter++;
        }

        foreach (Def def in Defs)
        {
            for (int grade = 0; grade < 5; grade++)
            {
                parts.Add(Build(def, grade));
            }
        }

        catalog.EditorSetParts(parts);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[파츠 생성기] 완료 - 이전 {before}개 → 현재 {parts.Count}개 " +
                  $"(루트 파츠 {removed}개 제거, 신규 {Defs.Length}종 x 5등급 = {Defs.Length * 5}개 추가, " +
                  $"표준 메모리 보정 {fixedStarter}건)");
    }

    private static PartData Build(Def def, int grade)
    {
        var part = new PartData
        {
            partId = def.idBase + grade,
            partName = def.name,
            slot = def.slot,
            grade = (ItemGrade)grade,
            isDefaultStarter = false,
            iconName = def.icon,
            weight = def.weight,

            bonusStat = def.stat1,
            bonusAmount = At(def.amount1, grade),
            bonusStat2 = def.stat2,
            bonusAmount2 = At(def.amount2, grade),

            effect = def.effect,
            effectStat = def.effectStat,
            effectAmount = At(def.effectAmount, grade),

            discSlotCount = AtInt(def.discSlots, grade),
            coreMaxLevelBonus = AtInt(def.coreMaxLevel, grade),

            restrictsWeaponType = def.restrictsType,
            allowedWeaponType = def.allowedType,
            socketAttackSpeedPercent = At(def.socketAtkSpeedPct, grade),
            socketDamageFlat = At(def.socketDamageFlat, grade),
            socketDamagePercent = At(def.socketDamagePct, grade),
            socketCritChancePercent = At(def.socketCritPct, grade),
            socketSplashPercent = At(def.socketSplashPct, grade),
            socketDefIgnorePercent = At(def.socketDefIgnorePct, grade)
        };

        return part;
    }

    private static float At(float[] values, int grade) => values == null ? 0f : values[grade];
    private static int AtInt(int[] values, int grade) => values == null ? 0 : values[grade];

    /// <summary>생성 결과를 검증한다(슬롯별 종류 수 · 등급 누락 · ID 중복 · 아이콘 누락).</summary>
    [MenuItem("Comstock/파츠 테이블 검증")]
    public static void Validate()
    {
        PartsCatalog catalog = AssetDatabase.LoadAssetAtPath<PartsCatalog>(CatalogPath);
        if (catalog == null) return;

        var idSeen = new HashSet<int>();
        var duplicateIds = new List<int>();
        var perSlotGrade = new Dictionary<string, int>();
        int missingIcon = 0;

        foreach (PartData p in catalog.Parts)
        {
            if (!idSeen.Add(p.partId)) duplicateIds.Add(p.partId);

            string key = p.slot + "|" + p.grade;
            perSlotGrade.TryGetValue(key, out int count);
            perSlotGrade[key] = count + 1;

            if (!p.isDefaultStarter && System.Array.IndexOf(ManagedSlots, p.slot) >= 0 &&
                string.IsNullOrEmpty(p.iconName))
            {
                missingIcon++;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[파츠 검증] 총 {catalog.Parts.Count}개, ID 중복 {duplicateIds.Count}건, 아이콘 누락 {missingIcon}건");

        for (int g = 0; g < 5; g++)
        {
            int total = 0;
            foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
            {
                perSlotGrade.TryGetValue(slot + "|" + (ItemGrade)g, out int c);
                total += c;
            }
            sb.AppendLine($"  {(ItemGrade)g}: {total}개");
        }

        if (duplicateIds.Count > 0) sb.AppendLine("  중복 ID: " + string.Join(", ", duplicateIds));

        Debug.Log(sb.ToString());
    }
}
