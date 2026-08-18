using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 코어 경험치/레벨업/업그레이드 선택지를 담당한다.
/// 적 처치 보상(RewardPickup)이 RunState.CoreExp를 올릴 때마다 RunState.OnChanged가
/// 발행되므로, 여기서 그 이벤트를 구독해 레벨업 여부를 판정한다.
/// 실제 카드 UI 노출/선택 버튼 처리는 GameFlowManager가 이 매니저를 통해 진행한다.
///
/// <b>2026-08-13 등급 도입 + 레벨업 빈도 대폭 상향(사용자 요청)</b>
/// - 선택지마다 등급(일반~전설)을 뽑고, 증가량 = 일반 등급 기준값 x 등급 배율이다
///   (<see cref="AiCoreUpgradePool"/> 참고).
/// - "레벨업을 훨씬 자주 하되 한 번에 올려주는 수치는 1/3~1/4로" 라는 요청에 맞춰
///   필요 경험치 공식을 <b>50 + 20 x 레벨 → 4 + 2 x 레벨</b>로 낮췄다. 필요 경험치는
///   레벨에 비례해 늘어나므로(누적 = 대략 계수 x 레벨^2) 계수를 12배 낮추면 <b>같은 경험치로
///   약 3.5배 레벨업</b>한다. 상한도 같은 배수로 올려(20 → 70) 총 성장량이 예전과 비슷하게 남는다.
/// </summary>
public class AiCoreManager : MonoBehaviour
{
    [SerializeField] private AiCoreUpgradePool upgradePool;

    [Tooltip("메모리(머리 파츠)가 정하는 AI 코어 최대 레벨의 임시값. " +
             "실제 값은 로봇 모딩(Phase 4)에서 머리 파츠 데이터로 대체된다. " +
             "2026-08-13 레벨업 빈도를 약 3.5배로 올리면서 20 → 70으로 함께 올렸다")]
    [SerializeField] private int coreMaxLevelPlaceholder = 70;

    [Tooltip("레벨 N -> N+1에 필요한 경험치 = expBase + expPerLevel * N. " +
             "2026-08-13 '레벨업을 훨씬 자주' 요청으로 50+20*N → 4+2*N (계수 1/12)")]
    [SerializeField] private int expBase = 4;
    [SerializeField] private int expPerLevel = 2;

    [Header("골드 리롤 (2026-08-18) - 공식은 상점 새로고침과 동일")]
    [Tooltip("카드 화면을 열었을 때 첫 리롤에 드는 골드")]
    [SerializeField] private int rerollBaseCost = 10;

    [Tooltip("같은 카드 화면에서 리롤을 반복할 때마다 추가로 붙는 골드")]
    [SerializeField] private int rerollCostIncrement = 5;

    private void OnEnable() => RunState.OnChanged += HandleRunStateChanged;
    private void OnDisable() => RunState.OnChanged -= HandleRunStateChanged;

    private void HandleRunStateChanged()
    {
        if (RunState.CoreLevel >= coreMaxLevelPlaceholder) return;

        int required = RequiredExpForNextLevel();
        while (RunState.CoreExp >= required && RunState.CoreLevel < coreMaxLevelPlaceholder)
        {
            RunState.CoreExp -= required;
            RunState.CoreLevel++;
            RunState.PendingCoreUpgradeChoices++;
            required = RequiredExpForNextLevel();
        }
    }

    private int RequiredExpForNextLevel() => Mathf.Max(1, expBase + expPerLevel * RunState.CoreLevel);

    /// <summary>HUD의 경험치 바 표시용. 최대 레벨이면 다음 레벨이 없다는 뜻으로 -1을 돌려준다.</summary>
    public int GetRequiredExpForNextLevel()
    {
        return RunState.CoreLevel >= coreMaxLevelPlaceholder ? -1 : RequiredExpForNextLevel();
    }

    public int MaxLevel => coreMaxLevelPlaceholder;

    /// <summary>
    /// 카드 한 장 = 업그레이드 종류 + 등급 + 그 등급에서의 실제 증가량.
    /// 같은 종류라도 등급이 다르면 다른 카드이므로, 등급까지 확정한 이 구조체를 UI에 넘긴다.
    /// </summary>
    public struct UpgradeChoice
    {
        public AiCoreUpgradePool.Option Option;
        public ItemGrade Grade;

        /// <summary>등급 배율까지 적용된 실제 증가량(카드에 적히는 값 = 실제 반영되는 값).</summary>
        public float Amount;

        /// <summary>카드에 표시할 두 줄 문구. 첫 줄은 등급(색상)+이름, 둘째 줄은 실제 효과.</summary>
        public string BuildLabel()
        {
            string effect = string.IsNullOrWhiteSpace(Option.description)
                ? AiCoreUpgradePool.BuildEffectLine(Option.statType, Amount)
                : Option.description;

            return $"<color={Grade.ToColorHex()}>{Grade.ToKorean()}</color> {Option.displayName}\n{effect}";
        }
    }

    /// <summary>
    /// 업그레이드 풀에서 서로 다른 최대 count개를 무작위로 뽑고, <b>카드마다 등급을 따로</b> 굴린다.
    /// 등급 추첨은 현재 웨이브를 기준으로 하므로(minWave) 초반에는 상위 등급이 나오지 않는다.
    /// </summary>
    public List<UpgradeChoice> DrawChoices(int count = 3)
    {
        var remaining = new List<AiCoreUpgradePool.Option>(upgradePool.options);
        var picked = new List<UpgradeChoice>();

        for (int i = 0; i < count && remaining.Count > 0; i++)
        {
            int index = Random.Range(0, remaining.Count);
            AiCoreUpgradePool.Option option = remaining[index];
            remaining.RemoveAt(index);

            ItemGrade grade = upgradePool.RollGrade(RunState.WaveNumber);
            picked.Add(new UpgradeChoice
            {
                Option = option,
                Grade = grade,
                Amount = upgradePool.GetGradedAmount(option, grade)
            });
        }

        return picked;
    }

    /// <summary>선택된 업그레이드를 RunState에 반영하고 대기 중인 선택 개수를 하나 줄인다.</summary>
    public void ApplyChoice(UpgradeChoice choice)
    {
        StatType stat = choice.Option.statType;
        if (!RunState.CoreStatBonuses.ContainsKey(stat)) RunState.CoreStatBonuses[stat] = 0f;
        RunState.CoreStatBonuses[stat] += choice.Amount;

        RunState.PendingCoreUpgradeChoices = Mathf.Max(0, RunState.PendingCoreUpgradeChoices - 1);
        RunState.NotifyChanged();
    }

    // ── 골드 리롤 (2026-08-18) ──────────────────────────────────────────
    // 상점 새로고침(ShopCatalog.GetRefreshCost + ShopManager.TryRefresh)과 같은 공식/구조다.
    // 누적치는 카드 화면을 열 때마다 ResetRerollCount()로 되돌리므로, 레벨업이 연달아 밀려
    // 카드가 여러 번 떠도 매번 기본 비용부터 시작한다(사용자 확정).

    /// <summary>지금 떠 있는 카드 화면에서 다음 리롤에 필요한 골드.</summary>
    public int CurrentRerollCost =>
        Mathf.Max(0, rerollBaseCost + rerollCostIncrement * Mathf.Max(0, RunState.CoreRerollCount));

    /// <summary>카드 화면을 새로 열 때 호출. 리롤 누적 비용을 기본값으로 되돌린다.</summary>
    public void ResetRerollCount() => RunState.CoreRerollCount = 0;

    /// <summary>
    /// 골드를 내고 3택을 다시 뽑을 수 있으면 차감하고 true. 골드가 모자라면 아무것도 하지 않고 false.
    /// 실제로 카드를 다시 그리는 것은 호출부(GameFlowManager)가 <see cref="DrawChoices"/>로 한다.
    /// </summary>
    public bool TryReroll()
    {
        int cost = CurrentRerollCost;
        if (RunState.Gold < cost) return false;

        RunState.Gold -= cost;
        RunState.CoreRerollCount++;
        RunState.NotifyChanged();
        return true;
    }
}
