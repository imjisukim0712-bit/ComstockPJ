using System;
using UnityEngine;

/// <summary>
/// 디스크 하나의 효과가 어떤 "모양"인지. 21종 각각이 서로 다른 동작을 하지만, 실제로는
/// 아래 카테고리 중 하나를 골라 파라미터(statA/amountA 등)만 다르게 채우는 방식으로 전부
/// 표현된다(무기의 WeaponFireMode와 같은 접근 - 종류별 클래스를 만들지 않고 데이터+해석자로 처리).
/// 실제 해석은 DiscEffectRuntime(주기/조건부 효과)과 EquipDisc/EnemyUnit/PlayerShootManager/
/// RewardPickup(즉발/훅 기반 효과) 여러 곳에 나뉘어 있다 - 각 필드의 의미는 아래 주석 참고.
/// </summary>
public enum DiscEffectType
{
    /// <summary>영구 스탯 가감. statA/B/C(사용하는 만큼)를 장착 즉시 RunState.DiscStatBonuses에 더한다.</summary>
    StatModifier,

    /// <summary>적 처치 시 다른 적 하나에게 flatValue만큼 고정 피해(번개가 튐).</summary>
    OnKillChainLightning,

    /// <summary>적 처치 시 duration초 동안 이동속도(amountA, 절대값)와 공격속도(amountB, 배율 증가분)가 함께 오른다.</summary>
    OnKillTempMoveAtkSpeed,

    /// <summary>적 처치 시 duration초 동안 방어력(amountA, 절대값)과 회피율(amountB, %p)이 함께 오른다.</summary>
    OnKillTempDefDodge,

    /// <summary>
    /// <b>경험치를 획득할 때</b> flatValue만큼 즉시 회복(2026-08-24 사용자 지정으로 발동 시점이
    /// "적 처치"에서 "경험치 획득"으로 바뀌었다 - 처치해도 경험치가 안 나오는 경우가 절반이라
    /// (EnemyUnit.ExpDropChance) 회복 빈도가 절반으로 줄어든 셈이다).
    ///
    /// enum 이름만 바꿨고 <b>순서(=직렬화되는 정수 4)는 그대로</b>다 - ShopCatalog.asset에
    /// effectType이 숫자로 들어있어 중간에 끼워 넣거나 순서를 바꾸면 다른 효과로 어긋난다.
    /// </summary>
    OnExpGainHeal,

    /// <summary>적을 처치할 때마다 statA가 amountA씩 누적된다(cap까지만).</summary>
    OnKillStackStat,

    /// <summary>공격이 명중할 때마다 chance01 확률로 이번 데미지가 (1+multiplier)배가 된다.</summary>
    OnAttackChanceBonusDamage,

    /// <summary>상시: statA(보통 회피율)가 amountA만큼 영구 상승 + radius 반경 안의 적 이동속도를 amountB%만큼 감소.</summary>
    PassiveAuraSlow,

    /// <summary>현재 장착한 디스크 개수 x amountA(%p)만큼 치명타 확률이 상승(장착/해제될 때마다 재계산).</summary>
    CritChancePerDisc,

    /// <summary>체력이 0 이하가 되는 순간 1회(maxUses)에 한해 체력 1로 고정 + duration초 무적 + 이동속도 amountA배 증가.</summary>
    LastStand,

    /// <summary>interval초마다 flatValue만큼 회복.</summary>
    PeriodicHeal,

    /// <summary>플레이어가 공격 중이 아닐 때 이동속도가 amountA(절대값)만큼 상승.</summary>
    MoveSpeedWhenNotAttacking,

    /// <summary>interval초마다 공격력 ±amountA / 방어력 ∓amountB 조합이 번갈아 적용된다.</summary>
    OscillatingAtkDef,

    /// <summary>회복되지 않는 최대 체력 flatValue가 추가된다. 웨이브가 시작될 때마다 가득 채워진다.</summary>
    WaveShieldMaxHp,

    /// <summary>스킬(필살기) 쿨타임 amountA% 감소. 필살기가 데모 범위 밖이라 현재는 값만 보관하고 소비처가 없다.</summary>
    SkillCooldownReduction
}

/// <summary>
/// 디스크(CD 형태) 한 종류의 정의. 다른 로그라이크의 유물/아이템 역할이다.
///
/// 2026-08-12 `20260810_디스크기획서_Ver01_김재원.pdf` 반영 - 기존에는 "상승 스탯 하나 +
/// 하락 스탯 하나"만 표현 가능한 단순 구조였지만, 실제 기획서의 21종은 대부분 처치 시 발동/
/// 시간제/조건부/확률형 효과라 <see cref="DiscEffectType"/>로 종류를 나누고 범용 파라미터
/// (statA~C, flatValue, chance01 등)로 표현하도록 확장했다. 실제 21종 데이터는
/// `Assets/Scripts/Editor/DiscTableGenerator.cs`(에디터 전용, 메뉴
/// `Comstock/디스크 테이블 21종 재생성`)가 채운다 - 무기 테이블과 동일한 패턴.
///
/// 무기와 달리 구글시트에 원본 테이블이 없는 신규 데이터라, 기획자가 직접 채우는
/// 로컬 전용 데이터로 ShopCatalog 에셋 안에 목록으로 보관한다.
/// </summary>
[Serializable]
public struct DiscData
{
    [Tooltip("디스크 고유 ID. RunState.EquippedDiscIds에 이 값이 저장된다")]
    public int discId;

    [Tooltip("상점 카드와 장착 목록에 표시될 이름")]
    public string discName;

    [Tooltip("이 디스크의 기본 등급. 상점 등장 시 이 등급으로 고정된다(무기와 달리 등급별 배율을 곱하지 않는다)")]
    public ItemGrade grade;

    [Tooltip("기본 판매 가격(골드). 등급별 가격 배율은 적용하지 않고 이 값을 그대로 쓴다")]
    public int price;

    [Tooltip("Assets/Resources 기준 아이콘 스프라이트 경로(확장자 제외). 예: Discs/디스크01")]
    public string iconName;

    [Tooltip("상점 카드/장착 목록에 보여줄 효과 설명 한 줄(기획서 '효과:' 문구를 그대로 옮김)")]
    public string effectDescription;

    public DiscEffectType effectType;

    [Header("스탯 파라미터 (effectType에 따라 의미가 다름 - 위 enum 주석 참고)")]
    public StatType statA;
    public float amountA;
    public StatType statB;
    public float amountB;
    public StatType statC;
    public float amountC;

    [Header("범용 수치 파라미터 (effectType에 따라 의미가 다름 - 위 enum 주석 참고)")]
    public float flatValue;
    public float chance01;
    public float multiplier;
    public float cap;
    public float interval;
    public float duration;
    public float radius;
    public int maxUses;

    /// <summary>
    /// 상점 카드/장착 목록에 보여줄 한 줄 설명.
    ///
    /// <b>누적형(OnKillStackStat)만 매번 만들어 쓴다(2026-08-19).</b> 고정 문구는 "처치당 증가량"만
    /// 알려줘서, 상한에 도달한 뒤에는 "효과가 적용되지 않는 것"과 화면상 구분이 되지 않았다
    /// (사용자가 "암석 디스크 누적이 초기화된다"고 리포트한 것의 정체가 이것이다 - 실제로는
    /// 상한에 이미 도달해 있었다). 현재 누적치를 함께 보여줘 진행 상황이 드러나게 한다.
    /// 데이터에 고정 문구를 두면 실제 값과 어긋난다는 것은 AI 코어 3택 카드(2026-08-13)와
    /// 머리 효과 설명(2026-08-19)에서 이미 겪은 함정이다.
    /// </summary>
    public string BuildDescription()
    {
        if (effectType != DiscEffectType.OnKillStackStat) return this.DiscEffect();

        int copies = Mathf.Max(1, CountEquippedCopies());
        float total_cap = cap * copies;               // 장 수만큼 상한도 함께 늘어난다(ApplyKillStack과 같은 규칙)
        RunState.DiscStackProgress.TryGetValue(discId, out float progress);

        string stat = StatTypeNames.ToDisplayName(statA);

        // "{stat}이(가)" 같은 조사 분기를 피하려고 기호 표기를 쓴다(체력/방어력/이동속도가 섞인다).
        return Loc.T("disc.perkill_growth", stat, amountA.ToString("0.###"), progress.ToString("0.##"), total_cap.ToString("0.##"));
    }

    /// <summary>지금 장착 중인 이 디스크의 장 수(상점 카드처럼 아직 장착 전이면 0).</summary>
    private int CountEquippedCopies()
    {
        int count = 0;
        foreach (int id in RunState.EquippedDiscIds)
        {
            if (id == discId) count++;
        }

        return count;
    }

    /// <summary>iconName 경로로 Resources에서 스프라이트를 불러온다. 비어있거나 못 찾으면 null.</summary>
    public Sprite LoadIcon() => string.IsNullOrEmpty(iconName) ? null : Resources.Load<Sprite>(iconName);
}

/// <summary>StatType을 UI에 보여주기 위한 표시명 모음(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
public static class StatTypeNames
{
    public static string ToDisplayName(StatType type)
    {
        switch (type)
        {
            case StatType.MaxHp: return Loc.T("stat.maxhp");
            case StatType.Atk: return Loc.T("stat.atk");
            case StatType.Def: return Loc.T("stat.def");
            case StatType.MoveSpeed: return Loc.T("stat.movespeed");
            case StatType.Avoid: return Loc.T("stat.avoid");
            case StatType.Luck: return Loc.T("stat.luck");
            case StatType.CritChance: return Loc.T("stat.critchance");
            case StatType.CritDamage: return Loc.T("stat.critdamage");
            case StatType.Mass: return Loc.T("stat.mass");
            case StatType.GoldGain: return Loc.T("stat.goldgain");
            case StatType.WeaponRangeBonus: return Loc.T("stat.weaponrange");
            case StatType.ExpGain: return Loc.T("stat.expgain");
            default: return type.ToString();
        }
    }
}
