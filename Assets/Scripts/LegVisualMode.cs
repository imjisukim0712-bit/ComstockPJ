/// <summary>
/// 장착된 다리 파츠에 따라 <see cref="ProceduralCharacterRig"/>가 다리 대신 그리는 시각 종류
/// (2026-08-21 다리 기획서 Ver02 비주얼 적용). <see cref="LegSkillType"/>(Space 동작)과는 별개
/// 개념이라 필드를 분리했다 - 지금은 4종 다리가 1:1로 대응하지만, 스킬과 비주얼이 다른 조합으로
/// 섞일 가능성을 열어둔다.
/// </summary>
public enum LegVisualMode
{
    /// <summary>기본 2족 IK 다리(기존 걸음걸이 리그, 변경 없음)</summary>
    Biped,

    /// <summary>캐터필러(트레일다리) - 무한궤도 애니메이션(리깅 없이 프레임만 재생)</summary>
    Tread,

    /// <summary>로켓 추진기 - 호버링 부스터 + 화염 애니메이션(리깅 없이 프레임만 재생)</summary>
    Rocket,

    /// <summary>거미 다리 - 4개의 실제 2관절 IK 다리</summary>
    Spider
}
