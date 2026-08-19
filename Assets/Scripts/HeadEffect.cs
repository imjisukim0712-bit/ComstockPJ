/// <summary>
/// 머리(로봇)마다 하나씩 붙는 고유 효과. `머리 기획서 Ver04`(2026-08-18, 백승오) 12종.
///
/// 머리는 <b>게임 시작 시 1회 고르고 런 중에는 절대 바뀌지 않는</b> 캐릭터 정체성이라
/// (상점·부품 상자에 등장하지 않는다) 여기 있는 효과는 전부 "런 내내 켜져 있는 패시브"다.
/// 조건부 효과(버서커의 체력 50%, 픽시의 근접 6개 등)도 조건 판정만 매 프레임 다시 하는
/// 것이고 효과 자체가 붙었다 떨어지지는 않는다.
///
/// 실제 계산은 전부 <see cref="HeadEffects"/>에 모여 있다. enum과 계산을 갈라놓은 이유는
/// 훅 지점이 6개 파일에 흩어져 있어서(피해량/공격속도/사거리/관통/스탯/보상) 각 호출부가
/// switch를 따로 갖게 두면 머리를 추가할 때마다 6곳을 고쳐야 하기 때문이다.
/// </summary>
public enum HeadEffect
{
    /// <summary>효과 없음. 데이터가 아직 없는 로봇(기존 디버그용 100002 등)의 안전한 기본값.</summary>
    None = 0,

    /// <summary>컴스톡 MK-01 — [연사] 무기의 공격속도 +15%.</summary>
    ComstockMk01 = 1,

    /// <summary>가드맨 — [산탄] 무기의 피해량 +15%, 그리고 연속 2회 발사.</summary>
    Guardman = 2,

    /// <summary>메테우스 — [폭발] 무기의 폭발 범위 +20%, 공격속도 +10%.</summary>
    Meteus = 3,

    /// <summary>버서커 — 체력이 최대치의 50% 이하일 때 공격력 x1.5, 공격속도 x1.8.</summary>
    Berserker = 4,

    /// <summary>해피 픽셀 — 행운 10당 이동속도 +1 / 공격속도 x1.2 (최대 3중첩).</summary>
    HappyPixel = 5,

    /// <summary>네온아이 — 기본 공격력·공격속도 보너스가 장착한 무기 수만큼 깎이고, 대신 무기 수만큼 체력·방어력이 오른다.</summary>
    NeonEye = 6,

    /// <summary>핫팟 — 보유 디스크 수에 비례해 공격력·방어력·이동속도·공격속도가 오른다.</summary>
    HotPot = 7,

    /// <summary>픽시 — 모든 소켓이 근접이면 사거리 x2. 원거리를 하나라도 끼면 그 무기 사거리가 근접급으로 떨어진다.</summary>
    Pixie = 8,

    /// <summary>팬봇 — 기본 다리만 장착 가능. 대신 구르기 쿨다운이 없고, 구르기 무적도 사라진다.</summary>
    FanBot = 9,

    /// <summary>
    /// 소다캔 — (효과 보류) 로켓 엔진 장착 + 최대 추가 이동속도 유지 시 공격력 x2.
    ///
    /// <b>지금은 아무 일도 하지 않는다.</b> 프로젝트에 "로켓 엔진" 다리 파츠가 없고
    /// (다리 파츠는 표준/유압/안정화/경량 부츠/제트 부스터 5종이며 전부 고정 보너스라)
    /// "패시브의 최대 추가 이동 속도" 램프업 개념 자체가 존재하지 않는다.
    /// 다리 기획서가 나오면 <see cref="HeadEffects"/>의 해당 분기만 채우면 된다.
    /// </summary>
    SodaCan = 10,

    /// <summary>프라이빗 컴스톡 — [정밀] 무기의 공격력 x2, 공격속도 x0.5, 관통 +2.</summary>
    PrivateComstock = 11,

    /// <summary>미니 픽시 — 경험치 획득량 +50%, 골드 수급량 -50%.</summary>
    MiniPixie = 12
}

public static class HeadEffectExtensions
{
    public static string ToKorean(this HeadEffect effect)
    {
        switch (effect)
        {
            case HeadEffect.ComstockMk01: return "연사 특화";
            case HeadEffect.Guardman: return "산탄 특화";
            case HeadEffect.Meteus: return "폭발 특화";
            case HeadEffect.Berserker: return "광폭화";
            case HeadEffect.HappyPixel: return "행운 중첩";
            case HeadEffect.NeonEye: return "화력 vs 생존";
            case HeadEffect.HotPot: return "디스크 공명";
            case HeadEffect.Pixie: return "근접 전념";
            case HeadEffect.FanBot: return "무한 구르기";
            case HeadEffect.SodaCan: return "가속 폭주";
            case HeadEffect.PrivateComstock: return "정밀 관통";
            case HeadEffect.MiniPixie: return "속성 성장";
            default: return "없음";
        }
    }

    /// <summary>
    /// 머리 선택 화면·정비 화면에 그대로 뿌리는 효과 설명.
    ///
    /// <b>문구를 데이터에 고정 문자열로 두지 않고 여기서 상수를 읽어 생성한다.</b>
    /// AI 코어 3택 카드에서 이미 겪은 함정이다(2026-08-13) - 수치를 바꿀 때 문구를 같이
    /// 고치는 걸 잊으면 화면이 거짓말을 하게 된다. 여기서는 <see cref="HeadEffects"/>의
    /// 실제 상수를 문구에 끼워 넣으므로 밸런스를 조정하면 설명이 저절로 따라온다.
    /// </summary>
    public static string ToDescription(this HeadEffect effect)
    {
        switch (effect)
        {
            case HeadEffect.ComstockMk01:
                return $"[연사] 무기 사용 시 공격속도 +{Pct(HeadEffects.ComstockRapidFireAttackSpeed)}";

            case HeadEffect.Guardman:
                return $"[산탄] 무기 사용 시 피해량 +{Pct(HeadEffects.GuardmanShotgunDamage)}, 연속 2회 발사";

            case HeadEffect.Meteus:
                return $"[폭발] 무기 사용 시 폭발 범위 +{Pct(HeadEffects.MeteusSplashRadius)}, " +
                       $"공격속도 +{Pct(HeadEffects.MeteusAttackSpeed)}";

            case HeadEffect.Berserker:
                return $"체력 {HeadEffects.BerserkerHpThreshold * 100f:0}% 이하일 때 " +
                       $"공격력 x{HeadEffects.BerserkerDamage:0.##}, 공격속도 x{HeadEffects.BerserkerAttackSpeed:0.##}";

            case HeadEffect.HappyPixel:
                return $"행운 {HeadEffects.HappyPixelLuckPerStack:0}당 이동속도 +{HeadEffects.HappyPixelMoveSpeedPerStack:0.##}, " +
                       $"공격속도 x{HeadEffects.HappyPixelAttackSpeedPerStack:0.##} (최대 {HeadEffects.HappyPixelMaxStacks}중첩)";

            case HeadEffect.NeonEye:
                return $"공격력 +{HeadEffects.NeonEyeBaseBonus:0.##}, 공격속도 +{Pct(HeadEffects.NeonEyeBaseBonus / HeadEffects.NeonEyeAttackSpeedDivisor)}\n" +
                       $"장착 무기 1정당 그 보너스가 {HeadEffects.NeonEyePenaltyPerWeapon:0.##}씩 깎이고, " +
                       $"대신 체력 +{HeadEffects.NeonEyeHpPerWeapon:0}·방어력 +{HeadEffects.NeonEyeDefPerWeapon:0}";

            case HeadEffect.HotPot:
                return $"보유 디스크 1개당 공격력·방어력·이동속도 +{HeadEffects.HotPotBonusPerDisc:0.##}, " +
                       $"공격속도 +{Pct(HeadEffects.HotPotBonusPerDisc)}";

            case HeadEffect.Pixie:
                return $"모든 소켓에 [근접] 무기를 끼우면 사거리 x{HeadEffects.PixieAllMeleeRange:0.##}\n" +
                       $"[원거리] 무기를 끼우면 그 무기 사거리가 {HeadEffects.PixieRangedPenaltyRange:0.##}유닛(근접급)으로 떨어진다";

            case HeadEffect.FanBot:
                return "기본 다리만 장착 가능\n구르기 쿨다운 없음, 구르기 무적 효과 제거";

            case HeadEffect.SodaCan:
                return $"로켓 엔진 장착 + 최대 추가 이동속도 유지 시 공격력 x{HeadEffects.SodaCanDamage:0.##}\n" +
                       "(로켓 엔진 다리 파츠 미구현 - 현재 효과 없음)";

            case HeadEffect.PrivateComstock:
                return $"[정밀] 무기 사용 시 공격력 x{HeadEffects.PrivateComstockDamage:0.##}, " +
                       $"공격속도 x{HeadEffects.PrivateComstockAttackSpeed:0.##}, 관통 +{HeadEffects.PrivateComstockPierce}";

            case HeadEffect.MiniPixie:
                return $"경험치 획득량 {Signed(HeadEffects.MiniPixieExpBonus)}, 골드 수급량 {Signed(HeadEffects.MiniPixieGoldBonus)}";

            default:
                return "고유 효과 없음";
        }
    }

    /// <summary>0.15 → "15%". 배율 상수를 그대로 퍼센트 문구로 바꾼다.</summary>
    private static string Pct(float ratio) => $"{ratio * 100f:0.#}%";

    /// <summary>+0.5 → "+50%", -0.5 → "-50%".</summary>
    private static string Signed(float ratio) => $"{(ratio >= 0f ? "+" : "-")}{UnityEngine.Mathf.Abs(ratio) * 100f:0.#}%";
}
