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

    /// <summary>프라이빗 컴스톡 — [정밀] 무기의 공격력 x2(무기 자체 위력에만 적용, 로봇
    /// 공격력 분배분은 제외), 공격속도 x0.75, 관통 +1.</summary>
    PrivateComstock = 11,

    /// <summary>미니 픽시 — 경험치 획득량 +50%, 골드 수급량 -50%.</summary>
    MiniPixie = 12
}

public static class HeadEffectExtensions
{
    /// <summary>머리 고유 효과의 짧은 표시명(2026-08-25 다국어 도입으로 ToKorean에서 개명).</summary>
    public static string ToDisplayName(this HeadEffect effect)
    {
        switch (effect)
        {
            case HeadEffect.ComstockMk01: return Loc.T("headeffect.comstockmk01.name");
            case HeadEffect.Guardman: return Loc.T("headeffect.guardman.name");
            case HeadEffect.Meteus: return Loc.T("headeffect.meteus.name");
            case HeadEffect.Berserker: return Loc.T("headeffect.berserker.name");
            case HeadEffect.HappyPixel: return Loc.T("headeffect.happypixel.name");
            case HeadEffect.NeonEye: return Loc.T("headeffect.neoneye.name");
            case HeadEffect.HotPot: return Loc.T("headeffect.hotpot.name");
            case HeadEffect.Pixie: return Loc.T("headeffect.pixie.name");
            case HeadEffect.FanBot: return Loc.T("headeffect.fanbot.name");
            case HeadEffect.SodaCan: return Loc.T("headeffect.sodacan.name");
            case HeadEffect.PrivateComstock: return Loc.T("headeffect.privatecomstock.name");
            case HeadEffect.MiniPixie: return Loc.T("headeffect.minipixie.name");
            default: return Loc.T("common.none");
        }
    }

    /// <summary>
    /// 머리 선택 화면·정비 화면에 그대로 뿌리는 효과 설명.
    ///
    /// <b>문구를 데이터에 고정 문자열로 두지 않고 여기서 상수를 읽어 생성한다.</b>
    /// AI 코어 3택 카드에서 이미 겪은 함정이다(2026-08-13) - 수치를 바꿀 때 문구를 같이
    /// 고치는 걸 잊으면 화면이 거짓말을 하게 된다. 여기서는 <see cref="HeadEffects"/>의
    /// 실제 상수를 문구에 끼워 넣으므로 밸런스를 조정하면 설명이 저절로 따라온다.
    ///
    /// <b>다국어(2026-08-25)</b>: 번역문의 <c>{0}</c>,<c>{1}</c>… 자리에 그 상수들이 들어간다.
    /// 그래서 번역문을 고쳐도 수치는 여전히 코드 상수에서 나온다(위 원칙이 그대로 유지된다).
    /// 자리 수가 안 맞으면 <see cref="Loc.T(string, object[])"/>가 예외 대신 서식 전 원문을 돌려준다.
    /// </summary>
    public static string ToDescription(this HeadEffect effect)
    {
        switch (effect)
        {
            case HeadEffect.ComstockMk01:
                return Loc.T("headeffect.comstockmk01.desc", Pct(HeadEffects.ComstockRapidFireAttackSpeed));

            case HeadEffect.Guardman:
                return Loc.T("headeffect.guardman.desc", Pct(HeadEffects.GuardmanShotgunDamage));

            case HeadEffect.Meteus:
                return Loc.T("headeffect.meteus.desc",
                    Pct(HeadEffects.MeteusSplashRadius), Pct(HeadEffects.MeteusAttackSpeed));

            case HeadEffect.Berserker:
                return Loc.T("headeffect.berserker.desc",
                    (HeadEffects.BerserkerHpThreshold * 100f).ToString("0"),
                    HeadEffects.BerserkerDamage.ToString("0.##"),
                    HeadEffects.BerserkerAttackSpeed.ToString("0.##"));

            case HeadEffect.HappyPixel:
                return Loc.T("headeffect.happypixel.desc",
                    HeadEffects.HappyPixelLuckPerStack.ToString("0"),
                    HeadEffects.HappyPixelMoveSpeedPerStack.ToString("0.##"),
                    HeadEffects.HappyPixelAttackSpeedPerStack.ToString("0.##"),
                    HeadEffects.HappyPixelMaxStacks);

            case HeadEffect.NeonEye:
                return Loc.T("headeffect.neoneye.desc",
                    HeadEffects.NeonEyeBaseBonus.ToString("0.##"),
                    Pct(HeadEffects.NeonEyeBaseBonus / HeadEffects.NeonEyeAttackSpeedDivisor),
                    HeadEffects.NeonEyePenaltyPerWeapon.ToString("0.##"),
                    HeadEffects.NeonEyeHpPerWeapon.ToString("0"),
                    HeadEffects.NeonEyeDefPerWeapon.ToString("0"));

            case HeadEffect.HotPot:
                return Loc.T("headeffect.hotpot.desc",
                    HeadEffects.HotPotBonusPerDisc.ToString("0.##"), Pct(HeadEffects.HotPotBonusPerDisc));

            case HeadEffect.Pixie:
                return Loc.T("headeffect.pixie.desc",
                    HeadEffects.PixieAllMeleeRange.ToString("0.##"),
                    HeadEffects.PixieRangedPenaltyRange.ToString("0.##"));

            case HeadEffect.FanBot:
                return Loc.T("headeffect.fanbot.desc");

            case HeadEffect.SodaCan:
                return Loc.T("headeffect.sodacan.desc", HeadEffects.SodaCanDamage.ToString("0.##"));

            case HeadEffect.PrivateComstock:
                return Loc.T("headeffect.privatecomstock.desc",
                    HeadEffects.PrivateComstockDamage.ToString("0.##"),
                    HeadEffects.PrivateComstockAttackSpeed.ToString("0.##"),
                    HeadEffects.PrivateComstockPierce);

            case HeadEffect.MiniPixie:
                return Loc.T("headeffect.minipixie.desc",
                    Signed(HeadEffects.MiniPixieExpBonus), Signed(HeadEffects.MiniPixieGoldBonus));

            default:
                return Loc.T("headeffect.none.desc");
        }
    }

    /// <summary>0.15 → "15%". 배율 상수를 그대로 퍼센트 문구로 바꾼다.</summary>
    private static string Pct(float ratio) => $"{ratio * 100f:0.#}%";

    /// <summary>+0.5 → "+50%", -0.5 → "-50%".</summary>
    private static string Signed(float ratio) => $"{(ratio >= 0f ? "+" : "-")}{UnityEngine.Mathf.Abs(ratio) * 100f:0.#}%";
}
