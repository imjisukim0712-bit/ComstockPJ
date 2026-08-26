/// <summary>
/// 파츠 하나가 가질 수 있는 <b>특수효과</b>의 종류. 단순 가산 스탯(방어력 +3 같은 것)은
/// <see cref="PartData.bonusStat"/>/<see cref="PartData.bonusStat2"/>로 표현하고,
/// "다른 스탯에서 파생되는" 값이나 조건부 발동처럼 <b>합산만으로 표현할 수 없는 것</b>만 여기 넣는다.
///
/// 실제 계산은 전부 <see cref="PartEffects"/>에 있다(머리 고유 효과의 <c>HeadEffects</c>와 같은 구조).
/// 값은 int로 직렬화되므로 <b>중간에 끼워 넣지 말고 항상 맨 끝에 추가</b>할 것.
/// </summary>
public enum PartEffect
{
    None,

    /// <summary>행운의 effectAmount%만큼 방어력이 증가(연극 마스크).</summary>
    DefFromLuckPercent,

    /// <summary>공격력의 effectAmount%만큼 방어력이 증가(철인 헬멧).</summary>
    DefFromAtkPercent,

    /// <summary>방어력이 effectAmount% 증가(방탄모). 가산·파생이 모두 끝난 뒤 곱해진다.</summary>
    DefPercentBonus,

    /// <summary>체력이 절반 이하일 때만 방어력 +effectAmount(탈). 피해 계산 시점에 판정한다.</summary>
    DefWhenLowHp,

    /// <summary>근접 공격으로 받은 피해의 effectAmount%를 공격자에게 되돌린다(가시 플레이트).</summary>
    MeleeReflectPercent,

    /// <summary>
    /// (미사용) AI 코어의 시작 레벨이 effectAmount만큼 올라간다.
    ///
    /// <b>2026-08-26 사용자 지시로 폐지</b> - 유일한 사용처였던 뉴럴 캐시가 "최대 레벨 +20~45"로
    /// 바뀌었다(coreMaxLevelBonus). 지금 게임에서는 파츠를 런 도중에 부품 상자로 얻으므로
    /// "시작 레벨"이라는 개념 자체가 성립하지 않았다 - 장착하는 순간 레벨이 한꺼번에 지급됐다.
    /// enum은 int로 직렬화되므로 <see cref="PartSlot.Foot"/> 전례대로 <b>자리만 남겨둔다</b>
    /// (지우면 뒤에 있는 PerDiscStat 이하 값이 전부 밀려 카탈로그의 효과가 뒤바뀐다).
    /// 계산 로직(AiCoreManager/ModdingManager)과 번역 키는 함께 지웠다.
    /// </summary>
    CoreStartLevel,

    /// <summary>장착한 디스크 1개당 effectStat이 effectAmount만큼 오른다(확장 프레임/코어 연결망/허브 접속기).</summary>
    PerDiscStat,

    /// <summary>장착한 "교향곡" 계열 디스크 1개당 공격력이 effectAmount만큼 오른다(교향곡 모음집).</summary>
    PerSymphonyDiscAtk,

    /// <summary>이동속도가 effectAmount% 증가한다(다리 기획서 Ver02). 방어력 %증가(DefPercentBonus)와
    /// 같은 자리에서, 가산·파생이 모두 끝난 값에 곱해진다.</summary>
    MoveSpeedPercentBonus,

    /// <summary>질량이 effectAmount% 만큼 변한다(거미 다리: -50). 위와 같은 방식으로 곱해진다.</summary>
    MassPercentBonus
}
