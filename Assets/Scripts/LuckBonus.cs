using UnityEngine;

/// <summary>
/// 행운(Luck) 스탯이 아이템 <b>등급 추첨 가중치</b>에 주는 아주 약한 보정.
///
/// 2026-08-19 이전까지 행운은 이름만 있고 등급 확률에 전혀 관여하지 않았다(전수 조사 확인:
/// <see cref="ShopCatalog.RollGrade"/> / <see cref="PartsCatalog.RollBoxGrade"/> /
/// <see cref="AiCoreUpgradePool.RollGrade"/> 세 곳 모두 고정 가중치 + minWave만 썼고,
/// 행운을 실제로 소비하는 곳은 해피 픽셀 머리 효과 하나뿐이었다).
///
/// 2026-08-19에 처음 넣을 때도 "체감이 거의 없는 수준"을 노렸지만, 사용자가 2026-08-21
/// "행운 조금만 올려도 고등급 무기가 미친듯이 많이 나온다"고 재차 지적해 그때의 배율을
/// <b>1/10로 더 낮췄다</b>: <b>행운 10당 등급 한 단계마다 +0.1%</b> 가중치(일반은 무보정,
/// 전설은 4단계라 +0.4%). 행운 기여분 자체도 <see cref="LuckCap"/>에서 잘라 상한을 둔다.
///
/// 예) 행운 20(=LuckCap의 40%)에서 전설 가중치 3 → 3 x (1 + 0.002 x 4) = 3.024 →
/// 등장률 3.0% → 약 3.02%. 행운을 상한(50)까지 채워도 전설 가중치는 3 → 3.06(+2%)에 그친다.
///
/// AI 코어 업그레이드 등급에는 적용하지 않는다 - 그쪽은 "주운 아이템"이 아니라 레벨업 보상이라
/// 행운의 대상이 아니라고 봤다(필요하면 같은 방식으로 한 줄만 추가하면 된다).
/// </summary>
public static class LuckBonus
{
    /// <summary>행운 10당 등급 한 단계마다 붙는 가중치 비율(0.001 = 0.1%, 2026-08-21 10배 완화).</summary>
    public const float PerTenLuckPerGrade = 0.001f;

    /// <summary>보정에 반영되는 행운의 상한. 이 이상은 등급 확률을 더 올리지 않는다.</summary>
    public const float LuckCap = 50f;

    private static PlayerRobotController player;

    /// <summary>플레이어를 등록한다(PlayerRobotController.Awake). 씬 재시작 시 이전 판 참조가
    /// 남지 않도록 <see cref="Clear"/>를 먼저 부른다.</summary>
    public static void RegisterPlayer(PlayerRobotController value) => player = value;

    public static void Clear() => player = null;

    /// <summary>현재 행운. 플레이어가 없으면(타이틀 화면 등) 0.</summary>
    public static float CurrentLuck => player != null ? Mathf.Max(0f, player.Luck) : 0f;

    /// <summary>
    /// 등급 가중치에 곱할 배율. 일반(0단계)은 항상 1이고, 등급이 한 단계 오를 때마다
    /// 행운 비례분이 한 번씩 더 붙는다.
    /// </summary>
    public static float WeightMultiplier(ItemGrade grade)
    {
        float luck = Mathf.Min(CurrentLuck, LuckCap);
        if (luck <= 0f) return 1f;

        return 1f + (luck / 10f) * PerTenLuckPerGrade * (int)grade;
    }
}
