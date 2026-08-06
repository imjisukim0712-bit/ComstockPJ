using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 코어 경험치/레벨업/업그레이드 선택지를 담당한다.
/// 적 처치 보상(RewardPickup)이 RunState.CoreExp를 올릴 때마다 RunState.OnChanged가
/// 발행되므로, 여기서 그 이벤트를 구독해 레벨업 여부를 판정한다.
/// 실제 카드 UI 노출/선택 버튼 처리는 GameFlowManager가 이 매니저를 통해 진행한다.
/// </summary>
public class AiCoreManager : MonoBehaviour
{
    [SerializeField] private AiCoreUpgradePool upgradePool;

    [Tooltip("메모리(머리 파츠)가 정하는 AI 코어 최대 레벨의 임시값. " +
             "실제 값은 로봇 모딩(Phase 4)에서 머리 파츠 데이터로 대체된다")]
    [SerializeField] private int coreMaxLevelPlaceholder = 20;

    [Tooltip("레벨 N -> N+1에 필요한 경험치 = expBase + expPerLevel * N (사용자 지정: 50 + 20*레벨)")]
    [SerializeField] private int expBase = 50;
    [SerializeField] private int expPerLevel = 20;

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

    private int RequiredExpForNextLevel() => expBase + expPerLevel * RunState.CoreLevel;

    /// <summary>HUD의 경험치 바 표시용. 최대 레벨이면 다음 레벨이 없다는 뜻으로 -1을 돌려준다.</summary>
    public int GetRequiredExpForNextLevel()
    {
        return RunState.CoreLevel >= coreMaxLevelPlaceholder ? -1 : RequiredExpForNextLevel();
    }

    public int MaxLevel => coreMaxLevelPlaceholder;

    /// <summary>업그레이드 풀에서 서로 다른 최대 count개를 무작위로 뽑는다.</summary>
    public List<AiCoreUpgradePool.Option> DrawChoices(int count = 3)
    {
        var remaining = new List<AiCoreUpgradePool.Option>(upgradePool.options);
        var picked = new List<AiCoreUpgradePool.Option>();

        for (int i = 0; i < count && remaining.Count > 0; i++)
        {
            int index = Random.Range(0, remaining.Count);
            picked.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return picked;
    }

    /// <summary>선택된 업그레이드를 RunState에 반영하고 대기 중인 선택 개수를 하나 줄인다.</summary>
    public void ApplyChoice(AiCoreUpgradePool.Option option)
    {
        if (!RunState.CoreStatBonuses.ContainsKey(option.statType)) RunState.CoreStatBonuses[option.statType] = 0f;
        RunState.CoreStatBonuses[option.statType] += option.amount;

        RunState.PendingCoreUpgradeChoices = Mathf.Max(0, RunState.PendingCoreUpgradeChoices - 1);
        RunState.NotifyChanged();
    }
}
