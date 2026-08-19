using System;
using System.Collections.Generic;

/// <summary>
/// 런(웨이브 전투~정비~상점) 동안 누적되는 진행 상태.
/// PlayerSession(선택된 로봇=머리)과는 별개로, 웨이브를 진행하며 쌓이는 값들을 담는다.
/// 다른 static 상태(PlayerSession, GameOverManager)와 동일한 패턴을 따른다.
/// 아직 웨이브/상점/모딩 시스템이 없어 실제로 값을 채우는 코드는 이후 Phase에서 연결한다.
/// </summary>
public static class RunState
{
    public static int WaveNumber { get; set; } = 0;
    public static int Gold { get; set; } = 0;

    // 골드 획득의 <b>소수점 나머지</b>를 다음 획득으로 이월하는 누적기(2026-08-19 버그 수정).
    //
    // 골드는 int인데 획득량에는 배율이 곱해진다(금화의 잔향 디스크 +10%, 미니 픽시 머리 -50%).
    // 예전에는 곱한 결과를 곧바로 Mathf.RoundToInt로 잘라서 <b>작은 획득량에서 배율이 통째로
    // 사라졌다</b> - 실측 검증 결과:
    //   - 미니 픽시(-50%) + 골드 1짜리 픽업 → RoundToInt(0.5) = 0 (은행가 반올림이라 0으로 내려감).
    //     1~5웨이브는 기본 좀비(골드 1)만 나오므로 <b>골드가 전혀 오르지 않았다.</b>
    //   - 금화의 잔향(+10%) + 골드 1~3짜리 픽업 → 1.1/2.2/3.3이 전부 원래 값으로 반올림되어
    //     <b>증가분이 완전히 소멸했다</b>(리더의 골드 10에서만 +1이 붙었다).
    // 나머지를 이월하면 기대값이 정확히 보존된다(예: -50%는 픽업 2번마다 정확히 1골드,
    // +10%는 픽업 10번마다 정확히 +1골드). 경험치 쪽은 이미 Max(1,...) 가드가 있었지만
    // 골드에만 그 가드가 없던 비대칭이 이 버그의 직접 원인이었다.
    private static float goldFraction;

    /// <summary>
    /// 배율이 곱해진 <b>실수</b> 골드 획득량을 더한다. 정수부만 즉시 반영하고 소수점 나머지는
    /// 다음 획득으로 이월하므로, 작은 획득량에도 배율이 정확히(평균적으로) 적용된다.
    /// 상점 구매처럼 이미 정수인 지출/수입은 그냥 <see cref="Gold"/>를 직접 쓰면 된다.
    /// </summary>
    public static void AddGoldWithFraction(float exactAmount)
    {
        goldFraction += exactAmount;

        // 내림으로 정수부만 떼어낸다(반올림이 아니라 내림이어야 나머지 이월이 정확해진다).
        int whole = UnityEngine.Mathf.FloorToInt(goldFraction);
        goldFraction -= whole;
        Gold += whole;
    }

    public static int CoreExp { get; set; } = 0;
    public static int CoreLevel { get; set; } = 1;

    // AI 코어 업그레이드로 누적된 스탯 보너스(RobotStats.Compute가 반영). 레벨업했지만
    // 아직 정비 시간에 카드를 선택하지 않은 건수(GameFlowManager가 이 값을 보고 업그레이드 카드를 띄운다)
    public static Dictionary<StatType, float> CoreStatBonuses { get; private set; } = new Dictionary<StatType, float>();
    public static int PendingCoreUpgradeChoices { get; set; } = 0;

    // 소켓 하나에 장착된 무기. 등급마다 별도의 무기 데이터 행이 존재하므로(13종 x 5등급 = 65행)
    // WeaponId만 알면 등급별 성능이 전부 따라온다. Grade는 UI 표시(등급 색상)용 사본이다.
    public struct EquippedWeapon
    {
        public int WeaponId;
        public ItemGrade Grade;
    }

    // 소켓 인덱스 순서대로 장착된 무기 (머리 파츠가 정하는 소켓 개수만큼 채워짐).
    // 실제 소켓 개수는 아직 PlayerShootManager의 인스펙터 설정을 따른다(Phase 4에서 머리 파츠가 결정).
    public static List<EquippedWeapon> EquippedWeapons { get; private set; } = new List<EquippedWeapon>();

    // 장착된 디스크 ID 목록 (슬롯 최대 개수는 머리 파츠 능력치가 결정 - 고정 상한 없음)
    public static List<int> EquippedDiscIds { get; private set; } = new List<int>();

    // 장착된 디스크들이 만들어낸 스탯 증감 합계. CoreStatBonuses와 동일한 방식으로
    // RobotStats.Compute가 그대로 더한다(하락 스탯은 음수로 들어온다).
    // 디스크 정의(ShopCatalog)를 RobotStats가 직접 조회하지 않아도 되도록, 장착 시점에
    // 계산된 결과만 여기에 누적해둔다.
    public static Dictionary<StatType, float> DiscStatBonuses { get; private set; } = new Dictionary<StatType, float>();

    // 2026-08-12 디스크 기획서(김재원) 반영 - "처치마다 누적(최대치 있음)" 효과(교향곡:화염/바람
    // 소리/금속음/교향곡:암석)의 진행도. discId -> 지금까지 누적된 값(cap 이하로 클램프됨).
    // DiscStatBonuses와 별도로 두는 이유: 같은 종류 디스크를 두 장 장착해도 각자 독립적으로
    // cap까지 쌓여야 하는데, DiscStatBonuses는 스탯별 "합계"만 들고 있어 개별 진행도를 모른다.
    public static Dictionary<int, float> DiscStackProgress { get; private set; } = new Dictionary<int, float>();

    // "마지막 발악" 디스크처럼 런당 사용 횟수 제한이 있는 효과의 남은 횟수. discId -> 남은 횟수.
    // 장착 시 maxUses만큼 채워지고(같은 디스크를 여러 장 장착하면 합산), 발동될 때마다 1씩 줄어든다.
    public static Dictionary<int, int> DiscUsesRemaining { get; private set; } = new Dictionary<int, int>();

    // 이번 웨이브의 상점에서 새로고침을 몇 번 했는지 (비용이 누를수록 비싸진다).
    // 다음 웨이브 상점을 열 때 0으로 되돌린다.
    public static int ShopRefreshCount { get; set; } = 0;

    // 지금 떠 있는 AI 코어 3택 카드에서 골드 리롤을 몇 번 했는지 (상점 새로고침과 같은 누적 방식).
    // 카드 화면을 새로 열 때마다 0으로 되돌린다(레벨업이 연달아도 매번 기본 비용부터 시작).
    public static int CoreRerollCount { get; set; } = 0;

    // 팔/다리 등 모딩 대상 파츠 ID (PartSlot.ToString() -> 장착된 파츠 ID). 머리는 PlayerSession.SelectedRobotId로 고정 관리
    // PartSlot.ArmWeaponSocket은 더 이상 이 딕셔너리에 저장되지 않는다 - 아래
    // EquippedWeaponSocketPartIds가 소켓 인덱스별로 독립 관리한다(2026-08-12 "무기 소켓 개별화" 플랜).
    public static Dictionary<string, int> EquippedPartIds { get; private set; } = new Dictionary<string, int>();

    // 무기 소켓(팔 - 무기 소켓) 파츠를 소켓 인덱스별로 독립 저장한다. 예전에는 EquippedPartIds의
    // "ArmWeaponSocket" 키 하나가 모든 물리 소켓에 공통 적용됐는데, 소켓마다 다른 종류(허용 무기
    // 타입)·등급(사거리/감지거리/회전속도 배율)을 가질 수 있어야 해서 인덱스로 분리했다.
    // 키가 없는 소켓은 "표준 소켓"(기본 파츠, 타입 제한 없음)이 낀 것으로 간주한다.
    public static Dictionary<int, int> EquippedWeaponSocketPartIds { get; private set; } = new Dictionary<int, int>();

    // 장착된 파츠들이 만들어낸 스탯 증감 합계. CoreStatBonuses/DiscStatBonuses와 동일한 방식으로
    // RobotStats.Compute가 그대로 더한다. 파츠를 교체(ModdingManager.EquipPart)할 때마다
    // 이 값을 처음부터 다시 계산해서 채운다(디스크처럼 누적만 하지 않는 이유: 같은 슬롯에
    // 새 파츠를 장착하면 이전 파츠의 보너스는 사라져야 하기 때문).
    public static Dictionary<StatType, float> PartStatBonuses { get; private set; } = new Dictionary<StatType, float>();

    // 인게임에서 파밍했지만 아직 정비 시간에 개봉하지 않은 부품 상자 개수.
    // 상한은 머리(로봇)의 적재량(PartsCatalog.HeadModdingInfo.partBoxCapacity)이며,
    // 상한에 도달하면 몬스터가 더 이상 상자를 드랍하지 않는다.
    public static int UnopenedPartBoxCount { get; set; } = 0;

    // 정비 화면에서만 존재하는 임시 인벤토리(부품 상자를 자동 개봉해 담아둔 파츠 ID 목록).
    // 정비 화면을 닫으면 남은 내용물은 전부 사라진다(사용자 확정 사항). 파츠를 교체하면
    // 슬롯에서 빠진 기존 파츠도 이 목록으로 들어왔다가 정비 종료와 함께 함께 사라진다.
    public static List<int> ModdingInventory { get; private set; } = new List<int>();

    public static event Action OnChanged;

    public static void NotifyChanged() => OnChanged?.Invoke();

    // 씬 재시작(재시도) 시 이전 판의 런 진행 상태가 남아있지 않도록 초기화할 때 사용.
    // OnChanged는 여기서 null로 비우지 않는다 - 이 Reset()은 PlayerRobotController.Awake()에서
    // 호출되는데, 같은 씬의 다른 오브젝트(AiCoreManager 등)의 OnEnable 구독이 Player의 Awake보다
    // 먼저 실행되는 경우 그 구독이 여기서 지워져 버리는 초기화 순서 버그가 있었다. 각 구독자는
    // 자신의 OnDisable에서 스스로 구독 해제하므로 여기서 강제로 비울 필요가 없다.
    public static void Reset()
    {
        WaveNumber = 0;
        Gold = 0;
        goldFraction = 0f;
        CoreExp = 0;
        CoreLevel = 1;
        CoreStatBonuses.Clear();
        PendingCoreUpgradeChoices = 0;
        EquippedWeapons.Clear();
        EquippedDiscIds.Clear();
        DiscStatBonuses.Clear();
        DiscStackProgress.Clear();
        DiscUsesRemaining.Clear();
        ShopRefreshCount = 0;
        CoreRerollCount = 0;
        EquippedPartIds.Clear();
        EquippedWeaponSocketPartIds.Clear();
        PartStatBonuses.Clear();
        UnopenedPartBoxCount = 0;
        ModdingInventory.Clear();
    }
}
