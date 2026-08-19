using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 디스크 21종을 `Assets/Data/ShopCatalog.asset`(discs 목록)에 채우는 <b>에디터 전용</b> 도구.
/// `20260810_디스크기획서_Ver01_김재원.pdf`(2026-08-12 반영) 21종 전부를 다룬다.
/// WeaponTableGenerator와 동일한 패턴 - 사람이 실제로 편집하는 곳은 아래 <see cref="Defs"/> 배열뿐이고,
/// 나머지는 그대로 ShopCatalog.discs에 옮겨 담는다.
///
/// <b>아이콘 매칭 근거</b>: `Assets/Resources/Discs/디스크01~21.png`에는 파일명에 효과 정보가
/// 없어(단순 번호), 각 PNG를 실제로 열어 그림(색상/문양)과 기획서 아이콘 설명을 대조해
/// 매칭했다. 대부분(번개=노랑+번개무늬, 위장=카모, 은하수=성운, 숲의 소리(구 나무뭐시기)=나무결, 금속음=
/// 브러시드 메탈 등)은 명확했으나, 파랑 계열 3종(물 빠지는 소리/교향곡:파도/결정의 마찰음
/// 후보였던 서킷보드 무늬)은 확실한 단서가 부족해 색 계열로 근사 배정했다 - 나중에 기획자
/// 확인이 필요하면 아래 Defs의 iconName만 바꾸면 된다.
///
/// <b>등급/가격</b>: 기획서에 디스크별 등급·가격이 명시돼 있지 않아(예시 스크린샷의 "에픽"
/// 태그는 UI 목업일 뿐 전체 배정표가 아님), 효과의 강력함을 보고 5등급에 고르게 배분했다
/// (밸런스 미확정 임시값 - 가격은 등급으로만 정해지며 <see cref="GradePrices"/>에 모아 뒀다.
///  2026-08-13 사용자 요청으로 20/35/55/80/120 → 20/25/31/39/49로 완만해졌다).
///
/// <b>% 수치 환산</b>: 기획서의 "%"는 스탯이 이미 0~100 스케일인 회피율/치명타확률/치명타피해/
/// 행운/골드획득량에는 그대로(%p) 적용했지만, 절대값 스탯인 이동속도(기준 2.5)·공격력(기준 3)은
/// 로봇 100001(컴스톡 MK-1) 기준값의 %로 환산한 절대치를 대신 넣었다 - 이 프로젝트의 스탯
/// 보너스 체계가 전부 가산식이라 진짜 곱연산 %를 지원하지 않기 때문(다른 무기/디스크 수치와
/// 동일한 전제). 나중에 기준 스탯이 크게 바뀌면 같이 재조정해야 한다.
/// </summary>
public static class DiscTableGenerator
{
    private const string ShopCatalogPath = "Assets/Data/ShopCatalog.asset";
    private const int IdBase = 400000;

    /// <summary>
    /// 등급별 디스크 가격(일반→전설). 디스크 가격은 원래부터 등급으로만 정해져 있었으므로
    /// (기존 20 / 35 / 55 / 80 / 120) 개별 정의에 두지 않고 이 표에서 가져온다.
    ///
    /// 2026-08-13 사용자 요청("등급마다 가격이 25%씩 증가")으로 일반 20에 1.25^등급을 곱한
    /// 값으로 교체했다 - 전설이 일반의 6배에서 <b>2.44배</b>로 완만해진다.
    /// 무기 쪽 등급 배율(WeaponTableGenerator.PriceMultipliers)과 같은 규칙이다.
    /// </summary>
    private static readonly int[] GradePrices = { 20, 25, 31, 39, 49 };

    private struct DiscDef
    {
        public int num;              // 파일명 번호(디스크01~21) = ID 하위 2자리
        public string name;
        public ItemGrade grade;
        public DiscEffectType effectType;
        public string description;

        public StatType statA; public float amountA;
        public StatType statB; public float amountB;
        public StatType statC; public float amountC;

        public float flatValue;
        public float chance01;
        public float multiplier;
        public float cap;
        public float interval;
        public float duration;
        public float radius;
        public int maxUses;
    }

    private static readonly DiscDef[] Defs =
    {
        new DiscDef {
            num = 1, name = "네잎클로버 디스크", grade = ItemGrade.Normal,
            effectType = DiscEffectType.StatModifier,
            description = "행운이 7% 증가합니다.",
            statA = StatType.Luck, amountA = 7f
        },
        new DiscDef {
            num = 2, name = "염동력 디스크", grade = ItemGrade.Unique,
            effectType = DiscEffectType.CritChancePerDisc,
            description = "착용한 디스크의 수만큼 치명타 확률이 3% 증가합니다.",
            amountA = 3f
        },
        new DiscDef {
            num = 3, name = "777 디스크", grade = ItemGrade.Unique,
            effectType = DiscEffectType.OnAttackChanceBonusDamage,
            description = "공격 시 7%의 확률로 데미지가 77% 증가합니다.",
            chance01 = 0.07f, multiplier = 0.77f
        },
        new DiscDef {
            num = 4, name = "마지막 발악 디스크", grade = ItemGrade.Legendary,
            effectType = DiscEffectType.LastStand,
            description = "체력이 1 이하로 떨어지면 3초간 체력을 1로 고정하고 무적 상태가 되며 이동속도가 100% 증가합니다 (제한 1회).",
            amountA = 1.0f, duration = 3f, maxUses = 1
        },
        new DiscDef {
            num = 5, name = "에너지 베리어 디스크", grade = ItemGrade.Legendary,
            effectType = DiscEffectType.WaveShieldMaxHp,
            description = "회복되지 않는 최대 체력 15가 주어집니다 (웨이브마다 초기화).",
            flatValue = 15f
        },
        new DiscDef {
            num = 6, name = "포근한 치유 디스크", grade = ItemGrade.Normal,
            effectType = DiscEffectType.OnKillHeal,
            description = "적을 처치하면 HP를 1 회복합니다.",
            flatValue = 1f
        },
        new DiscDef {
            num = 7, name = "교향곡: 암석 디스크", grade = ItemGrade.Rare,
            effectType = DiscEffectType.OnKillStackStat,
            description = "적을 처치하면 방어력이 0.05 증가합니다 (최대 10).",
            statA = StatType.Def, amountA = 0.05f, cap = 10f // 200마리에 상한 도달
        },
        new DiscDef {
            num = 8, name = "숲의 소리 디스크", grade = ItemGrade.Normal,
            effectType = DiscEffectType.StatModifier,
            description = "이동속도 5% 감소, 체력 30 증가.",
            statA = StatType.MaxHp, amountA = 30f,
            statB = StatType.MoveSpeed, amountB = -0.125f // 로봇 기준 이동속도 2.5의 5%
        },
        new DiscDef {
            num = 9, name = "이끼 낀 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.PeriodicHeal,
            description = "5초마다 HP가 2만큼 회복됩니다.",
            interval = 5f, flatValue = 2f
        },
        new DiscDef {
            num = 10, name = "교향곡: 화염 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.OnKillStackStat,
            description = "적을 처치하면 공격력이 0.05 증가합니다 (최대 10).",
            statA = StatType.Atk, amountA = 0.05f, cap = 10f // 200마리에 상한 도달
        },
        new DiscDef {
            num = 11, name = "금화의 잔향 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.StatModifier,
            description = "획득하는 골드가 10% 증가합니다.",
            statA = StatType.GoldGain, amountA = 10f
        },
        new DiscDef {
            num = 12, name = "바람 소리 디스크", grade = ItemGrade.Normal,
            effectType = DiscEffectType.OnKillStackStat,
            description = "적을 처치하면 이동속도가 0.1% 증가합니다 (최대 20%).",
            statA = StatType.MoveSpeed, amountA = 0.003f, cap = 0.6f // 로봇 기준 이동속도 3.0의 0.1%/20%, 200마리에 상한 도달
        },
        new DiscDef {
            num = 13, name = "결정의 마찰음 디스크", grade = ItemGrade.Unique,
            effectType = DiscEffectType.StatModifier,
            description = "공격력이 15% 감소합니다. 치명타 확률이 5% 증가하고, 치명타 데미지가 15% 증가합니다.",
            statA = StatType.Atk, amountA = -0.45f, // 로봇 기준 공격력 3의 15%
            statB = StatType.CritChance, amountB = 5f,
            statC = StatType.CritDamage, amountC = 0.15f
        },
        new DiscDef {
            num = 14, name = "위장 디스크", grade = ItemGrade.Rare,
            effectType = DiscEffectType.MoveSpeedWhenNotAttacking,
            description = "적을 공격하고 있지 않을 경우 이동속도 20% 증가.",
            amountA = 0.5f // 로봇 기준 이동속도 2.5의 20%
        },
        new DiscDef {
            num = 15, name = "금속음 디스크", grade = ItemGrade.Rare,
            effectType = DiscEffectType.OnKillStackStat,
            description = "적을 처치하면 최대 체력이 0.2 증가합니다 (최대 40).",
            statA = StatType.MaxHp, amountA = 0.2f, cap = 40f // 200마리에 상한 도달
        },
        new DiscDef {
            num = 16, name = "은하수 디스크", grade = ItemGrade.Rare,
            effectType = DiscEffectType.SkillCooldownReduction,
            description = "스킬 쿨타임이 15% 감소합니다.",
            amountA = 15f
        },
        new DiscDef {
            num = 17, name = "공명의 소리 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.OscillatingAtkDef,
            description = "10초마다 공격력 +10, 방어력 -5 효과와 공격력 -10, 방어력 +5 효과가 번갈아 적용됩니다.",
            amountA = 10f, amountB = 5f, interval = 10f
        },
        new DiscDef {
            num = 18, name = "광분 바이러스 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.OnKillTempMoveAtkSpeed,
            description = "적을 처치하면 3초간 이동속도와 공격속도가 2% 증가합니다.",
            amountA = 0.05f, amountB = 0.02f, duration = 3f // 이동속도는 로봇 기준 2.5의 2%, 공격속도는 배율 증가분
        },
        new DiscDef {
            num = 19, name = "교향곡: 번개 디스크", grade = ItemGrade.Unique,
            effectType = DiscEffectType.OnKillChainLightning,
            description = "적을 처치하면 다른 적 하나에게 번개가 튀어 20의 피해를 입힙니다.",
            flatValue = 20f
        },
        new DiscDef {
            num = 20, name = "물 빠지는 소리 디스크", grade = ItemGrade.Rare,
            effectType = DiscEffectType.PassiveAuraSlow,
            description = "회피율 +2%. 가까이 있는 적의 이동속도 2% 감소.",
            statA = StatType.Avoid, amountA = 2f, amountB = 2f, radius = 3f
        },
        new DiscDef {
            num = 21, name = "교향곡: 파도 디스크", grade = ItemGrade.Epic,
            effectType = DiscEffectType.OnKillTempDefDodge,
            description = "적을 처치하면 3초간 방어력과 회피율이 3% 증가합니다.",
            amountA = 0.15f, amountB = 3f, duration = 3f // 방어력은 로봇 기준 5의 3%, 회피율은 %p 그대로
        },
    };

    [MenuItem("Comstock/디스크 테이블 21종 재생성")]
    public static void Generate()
    {
        var shopCatalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
        if (shopCatalog == null)
        {
            Debug.LogError($"ShopCatalog를 찾을 수 없습니다: {ShopCatalogPath}");
            return;
        }

        var discs = new List<DiscData>();
        foreach (DiscDef def in Defs)
        {
            discs.Add(new DiscData
            {
                discId = IdBase + def.num,
                discName = def.name,
                grade = def.grade,
                price = GradePrices[(int)def.grade],
                iconName = $"Discs/디스크{def.num:00}",
                effectDescription = def.description,
                effectType = def.effectType,
                statA = def.statA, amountA = def.amountA,
                statB = def.statB, amountB = def.amountB,
                statC = def.statC, amountC = def.amountC,
                flatValue = def.flatValue,
                chance01 = def.chance01,
                multiplier = def.multiplier,
                cap = def.cap,
                interval = def.interval,
                duration = def.duration,
                radius = def.radius,
                maxUses = def.maxUses
            });
        }

        shopCatalog.EditorSetDiscs(discs);
        EditorUtility.SetDirty(shopCatalog);
        AssetDatabase.SaveAssets();

        Debug.Log($"디스크 테이블 재생성 완료 - {discs.Count}종 (등급별: " +
                   $"{CountGrade(discs, ItemGrade.Normal)} / {CountGrade(discs, ItemGrade.Rare)} / " +
                   $"{CountGrade(discs, ItemGrade.Epic)} / {CountGrade(discs, ItemGrade.Unique)} / " +
                   $"{CountGrade(discs, ItemGrade.Legendary)})");
    }

    private static int CountGrade(List<DiscData> discs, ItemGrade grade)
    {
        int count = 0;
        foreach (DiscData d in discs) if (d.grade == grade) count++;
        return count;
    }
}
