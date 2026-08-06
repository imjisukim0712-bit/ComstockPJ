using System;
using System.Collections.Generic;

/// <summary>
/// 런(웨이브 전투~정비~상점) 동안 누적되는 진행 상태.
/// PlayerSession(선택된 로봇=머리)과는 별개로, 웨이브를 진행하며 쌓이는 값들을 담는다.
/// 다른 static 상태(PlayerSession, GameOverManager, PlayerInventory)와 동일한 패턴을 따른다.
/// 아직 웨이브/상점/모딩 시스템이 없어 실제로 값을 채우는 코드는 이후 Phase에서 연결한다.
/// </summary>
public static class RunState
{
    public static int WaveNumber { get; set; } = 0;
    public static int Gold { get; set; } = 0;

    public static int CoreExp { get; set; } = 0;
    public static int CoreLevel { get; set; } = 1;

    // AI 코어 업그레이드로 누적된 스탯 보너스(RobotStats.Compute가 반영). 레벨업했지만
    // 아직 정비 시간에 카드를 선택하지 않은 건수(GameFlowManager가 이 값을 보고 업그레이드 카드를 띄운다)
    public static Dictionary<StatType, float> CoreStatBonuses { get; private set; } = new Dictionary<StatType, float>();
    public static int PendingCoreUpgradeChoices { get; set; } = 0;

    // 소켓 하나에 장착된 무기. 같은 무기라도 등급에 따라 성능이 달라지므로(등급 = 수직 강화)
    // 무기 ID만으로는 부족하고 등급과 그 등급의 스탯 배율을 함께 들고 있어야 한다.
    // 배율을 여기 저장해두면 PlayerShootManager가 상점 카탈로그 에셋을 몰라도 된다.
    public struct EquippedWeapon
    {
        public int WeaponId;
        public ItemGrade Grade;
        public float StatMultiplier;
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

    // 이번 웨이브의 상점에서 새로고침을 몇 번 했는지 (비용이 누를수록 비싸진다).
    // 다음 웨이브 상점을 열 때 0으로 되돌린다.
    public static int ShopRefreshCount { get; set; } = 0;

    // 팔/다리 등 모딩 대상 파츠 ID (PartSlot.ToString() -> 장착된 파츠 ID). 머리는 PlayerSession.SelectedRobotId로 고정 관리
    public static Dictionary<string, int> EquippedPartIds { get; private set; } = new Dictionary<string, int>();

    // 장착된 파츠들이 만들어낸 스탯 증감 합계. CoreStatBonuses/DiscStatBonuses와 동일한 방식으로
    // RobotStats.Compute가 그대로 더한다. 파츠를 교체(ModdingManager.EquipPart)할 때마다
    // 이 값을 처음부터 다시 계산해서 채운다(디스크처럼 누적만 하지 않는 이유: 같은 슬롯에
    // 새 파츠를 장착하면 이전 파츠의 보너스는 사라져야 하기 때문).
    public static Dictionary<StatType, float> PartStatBonuses { get; private set; } = new Dictionary<StatType, float>();

    // 인게임에서 파밍했지만 아직 정비 시간에 개봉하지 않은 부품 상자 개수
    public static int UnopenedPartBoxCount { get; set; } = 0;

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
        CoreExp = 0;
        CoreLevel = 1;
        CoreStatBonuses.Clear();
        PendingCoreUpgradeChoices = 0;
        EquippedWeapons.Clear();
        EquippedDiscIds.Clear();
        DiscStatBonuses.Clear();
        ShopRefreshCount = 0;
        EquippedPartIds.Clear();
        PartStatBonuses.Clear();
        UnopenedPartBoxCount = 0;
    }
}
