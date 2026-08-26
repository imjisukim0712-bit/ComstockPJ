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

    [Tooltip("ModdingManager(메모리 파츠)를 찾지 못하는 극단적인 경우에만 쓰는 폴백값. " +
             "2026-08-18부터 실제 최대 레벨은 메모리(Memory) 파츠가 정한다 - MaxLevel 참고")]
    [SerializeField] private int coreMaxLevelFallback = 50;

    [Tooltip("레벨 N -> N+1에 필요한 경험치 = expBase + expPerLevel * N. " +
             "2026-08-13 '레벨업을 훨씬 자주' 요청으로 50+20*N → 4+2*N (계수 1/12)")]
    [SerializeField] private int expBase = 4;
    [SerializeField] private int expPerLevel = 2;

    [Tooltip("2026-08-19 - 이 레벨 이상부터 필요 경험치에 아래 배율이 곱해진다(오름차순). " +
             "선형 공식만으로는 후반 레벨업이 너무 쉬워서 구간마다 요구량을 계단식으로 올린다")]
    [SerializeField] private int[] expTierStartLevels = { 15, 30, 45 };

    [Tooltip("expTierStartLevels와 같은 순서의 배율. 첫 구간(1~14레벨)은 항상 x1")]
    [SerializeField] private float[] expTierMultipliers = { 2f, 4f, 8f };

    [Header("골드 리롤 (2026-08-18) - 공식은 상점 새로고침과 동일")]
    [Tooltip("카드 화면을 열었을 때 첫 리롤에 드는 골드")]
    [SerializeField] private int rerollBaseCost = 10;

    [Tooltip("같은 카드 화면에서 리롤을 반복할 때마다 추가로 붙는 골드")]
    [SerializeField] private int rerollCostIncrement = 5;

    [Tooltip("2026-08-23 사용자 제공 레벨업 이펙트(LevelUpEffect)의 폭(월드 유닛)")]
    [SerializeField] private float levelUpEffectWidth = 2.5f;

    private void OnEnable() => RunState.OnChanged += HandleRunStateChanged;
    private void OnDisable() => RunState.OnChanged -= HandleRunStateChanged;

    private void HandleRunStateChanged()
    {
        int maxLevel = MaxLevel;

        if (RunState.CoreLevel >= maxLevel) return;

        int level_before = RunState.CoreLevel;
        int required = RequiredExpForNextLevel();
        while (RunState.CoreExp >= required && RunState.CoreLevel < maxLevel)
        {
            RunState.CoreExp -= required;
            RunState.CoreLevel++;
            RunState.PendingCoreUpgradeChoices++;
            UnlockTracker.ReportLevelUp(); // 미니 픽시(누적 레벨 150)
            required = RequiredExpForNextLevel();
        }

        // 한 프레임에 여러 레벨이 오르더라도(대량 경험치 보상 등) 이펙트는 한 번만 재생한다.
        if (RunState.CoreLevel > level_before) PlayLevelUpEffect();
    }

    /// <summary>플레이어 <b>머리</b>에 레벨업 이펙트를 한 번 재생하고 효과음을 함께 낸다.
    /// 태그로 찾는 이유는 이 매니저가 플레이어가 아닌 별도 GameObject에 배치돼 있어 직접 참조가
    /// 없기 때문이다.
    ///
    /// 2026-08-25 사용자 요청으로 <b>발밑(플레이어 원점)이 아니라 머리 위치</b>에서 재생하고,
    /// 재생 내내 머리를 따라가게 했다. 머리는 <see cref="ProceduralCharacterRig.BodyVisual"/>이며
    /// (이 프로젝트에서 "머리" = 리그의 몸통 스프라이트), 리그가 없는 예외 상황에서는 예전처럼
    /// 플레이어 원점에 고정 재생한다.</summary>
    private void PlayLevelUpEffect()
    {
        GameObject player_go = GameObject.FindGameObjectWithTag("Player");
        if (player_go == null) return;

        SpriteRenderer body = player_go.GetComponentInChildren<SpriteRenderer>();
        int sorting = body != null ? body.sortingOrder + 5 : 15;

        var rig = player_go.GetComponentInChildren<ProceduralCharacterRig>();
        Transform head = rig != null ? rig.BodyVisual : null;

        LevelUpEffect.Play(head != null ? head.position : player_go.transform.position,
                           levelUpEffectWidth, sorting, head);

        SFXManager.Play(SFXManager.LevelUpClipName);
    }

    private int RequiredExpForNextLevel()
    {
        int level = RunState.CoreLevel;
        float required = (expBase + expPerLevel * level) * ExpTierMultiplier(level);

        return Mathf.Max(1, Mathf.RoundToInt(required));
    }

    /// <summary>
    /// 레벨 구간 배율. 선형 공식(4 + 2L)만으로는 45레벨까지 누적 2,156밖에 되지 않아
    /// 20웨이브 안에 최대 레벨에 여유롭게 도달했다(2026-08-19 사용자 지적).
    /// 15/30/45레벨을 경계로 x2 / x4 / x8을 곱해 후반 성장 속도를 늦춘다
    /// (누적: 15레벨 266 / 30레벨 1,706 / 45레벨 6,386 / 50레벨 10,306).
    /// </summary>
    private float ExpTierMultiplier(int level)
    {
        if (expTierStartLevels == null || expTierMultipliers == null) return 1f;

        float multiplier = 1f;
        int count = Mathf.Min(expTierStartLevels.Length, expTierMultipliers.Length);

        // 오름차순 전제. 조건을 만족하는 마지막 구간의 배율이 최종값이다.
        for (int i = 0; i < count; i++)
        {
            if (level >= expTierStartLevels[i]) multiplier = expTierMultipliers[i];
        }

        return multiplier;
    }

    /// <summary>HUD의 경험치 바 표시용. 최대 레벨이면 다음 레벨이 없다는 뜻으로 -1을 돌려준다.</summary>
    public int GetRequiredExpForNextLevel()
    {
        return RunState.CoreLevel >= MaxLevel ? -1 : RequiredExpForNextLevel();
    }

    /// <summary>2026-08-18부터 메모리(Memory) 파츠가 정한다(ModdingManager.CoreMaxLevel).
    /// ModdingManager를 못 찾는 극단적인 경우에만 인스펙터 폴백값을 쓴다.</summary>
    public int MaxLevel => ModdingManager.Instance != null ? ModdingManager.Instance.CoreMaxLevel : coreMaxLevelFallback;

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

        /// <summary>
        /// 카드는 <b>등급 / 이름 / 설명</b> 세 덩어리를 각각 다른 글자 칸에 그린다
        /// (2026-08-25 사용자 지시: "등급 \ 이름 \ 설명으로 나눠서 엔터치는게 나을듯" + 레퍼런스 이미지).
        /// <para>예전에는 세 정보를 한 줄 문자열에 <c>\n</c> 하나로 이어 붙였는데, 이름이 길면
        /// 등급과 이름이 한 줄에서 제멋대로 접혀 "글자 엔터가 어색"했다. 칸을 나누면 각 줄이
        /// 자기 칸 안에서만 접히므로 그런 일이 없다. 실제 배치는
        /// <see cref="GameFlowManager"/>가 맡는다.</para>
        /// </summary>
        public string GradeLine() => Grade.ToDisplayName();

        /// <summary>등급색(카드의 등급 줄에 쓰는 색).</summary>
        public string GradeColorHex() => Grade.ToColorHex();

        public string NameLine() => Option.DisplayName();

        /// <summary>실제 적용값으로 만든 효과 설명(등급마다 수치가 달라지므로 매번 생성한다).</summary>
        public string EffectLine()
        {
            return string.IsNullOrWhiteSpace(Option.description)
                ? AiCoreUpgradePool.BuildEffectLine(Option.statType, Amount)
                : Option.description;
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
