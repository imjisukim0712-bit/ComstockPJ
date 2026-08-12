using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 ↔ 정비 사이의 흐름을 담당하는 상태 머신.
/// 기획서 흐름: 웨이브 전투 → (AI 코어 업그레이드) → (로봇 정비) → 상점 → 다음 웨이브.
///
/// AI 코어 업그레이드 카드는 레벨업이 있었을 때만 노출되고(RunState.PendingCoreUpgradeChoices),
/// 로봇 정비 화면은 획득한 부품 상자가 있을 때만 노출된다(RunState.UnopenedPartBoxCount).
/// 상점은 기획서대로 웨이브 종료 후 항상 노출되며, "다음 웨이브 시작" 버튼도 상점 화면
/// 안에 있다(기획서 p.13의 3번 요소).
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public enum State
    {
        Combat,
        Intermission
    }

    [Header("연결")]
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private AiCoreManager aiCoreManager;

    [Header("AI 코어 업그레이드 카드 (레벨업했을 때만 노출) - 항상 3개 고정")]
    [SerializeField] private GameObject aiCoreUpgradePanel;
    [SerializeField] private Button option1Button;
    [SerializeField] private TextMeshProUGUI option1Text;
    [SerializeField] private Button option2Button;
    [SerializeField] private TextMeshProUGUI option2Text;
    [SerializeField] private Button option3Button;
    [SerializeField] private TextMeshProUGUI option3Text;

    [Header("로봇 정비 화면 (부품 상자가 있을 때만 노출)")]
    [SerializeField] private ModdingPanelUI moddingPanel;

    [Header("상점 화면 (웨이브 종료 후 항상 노출)")]
    [SerializeField] private ShopPanelUI shopPanel;

    [Header("정비 화면 진입 시 처리")]
    [Tooltip("정비 화면이 열려 있는 동안 Time.timeScale을 0으로 만들어 인게임을 완전히 정지시킨다")]
    [SerializeField] private bool freezeTimeDuringIntermission = true;
    [Tooltip("정비 화면에 들어갈 때 필드에 남은 보상 픽업을 자동 수령 처리하고, 투사체를 정리하며, 플레이어를 시작 위치로 되돌린다")]
    [SerializeField] private bool resetFieldOnIntermission = true;
    [Tooltip("자동 수령 전 골드/경험치 픽업이 플레이어 쪽으로 날아가는 자석 연출의 총 시간(초). " +
             "0이면 연출 없이 즉시 수령한다(기존 동작)")]
    [SerializeField] private float magnetCollectDuration = 0.35f;

    [Tooltip("정비 화면이 열려 있는 동안 숨길 전투 전용 HUD(HP, 상단 웨이브/골드/경험치 바 등).\n" +
             "예전에는 ShopPanelUI/ModdingPanelUI가 각자 숨겼는데, AI 코어 업그레이드 화면에는 그 처리가\n" +
             "없어서 HUD가 비쳐 보였다. 정비 단계 전체를 아는 여기서 한 번만 처리한다")]
    [SerializeField] private GameObject[] combatHudObjects = new GameObject[0];

    public State CurrentState { get; private set; } = State.Combat;

    /// <summary>
    /// 정비 화면(AI 코어 업그레이드/로봇 정비/상점)이 열려 있는지. 플레이어 조작·자동공격 등
    /// 인게임 로직이 이 값을 보고 스스로 멈춘다. GameOverManager.IsGameOver와 같은 용도로 쓰는
    /// 전역 플래그라 static으로 둔다.
    /// </summary>
    public static bool IsIntermission { get; private set; }

    /// <summary>
    /// 씬을 다시 시작할 때 이전 판의 값이 남지 않도록 PlayerRobotController.Awake()가 호출한다
    /// (EnemyUnit.ResetStaticCaches()와 같은 이유).
    /// </summary>
    public static void ResetStaticState()
    {
        IsIntermission = false;
        Time.timeScale = 1f; // 정비 중에 플레이모드를 껐다 켠 경우 0으로 굳어있지 않도록
    }

    private int lastEndedWaveNumber;

    private void Awake()
    {
        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
    }

    private void Start()
    {
        if (waveManager != null) waveManager.OnWaveEnded += HandleWaveEnded;
        if (moddingPanel != null) moddingPanel.OnProceedRequested += HandleModdingProceedRequested;
        if (shopPanel != null) shopPanel.OnNextWaveRequested += HandleNextWaveRequested;
    }

    private void OnDestroy()
    {
        if (waveManager != null) waveManager.OnWaveEnded -= HandleWaveEnded;
        if (moddingPanel != null) moddingPanel.OnProceedRequested -= HandleModdingProceedRequested;
        if (shopPanel != null) shopPanel.OnNextWaveRequested -= HandleNextWaveRequested;
    }

    private void HandleWaveEnded(int waveNumber)
    {
        // 플레이어가 이 웨이브 도중 사망했다면(웨이브 타이머는 게임오버와 무관하게 그대로 끝까지
        // 흐른다) 정비/상점 화면으로 넘어가지 않는다 - GameOverManager의 게임오버 화면이 이미 떠 있다.
        if (GameOverManager.IsGameOver) return;

        CurrentState = State.Intermission;
        IsIntermission = true;
        lastEndedWaveNumber = waveNumber;

        // 정비는 "전체 화면 UI + 인게임 완전 정지" 상태여야 한다(사용자 확정 사항).
        // 필드 정리를 먼저 하고 나서 시간을 멈춘다 - timeScale=0 상태에서 물리 이동을 시키면
        // Rigidbody가 그대로 반영되지 않을 수 있기 때문. 자석 연출이 여러 프레임에 걸쳐 재생돼야
        // 하므로(코루틴) 정지 화면 진입 자체를 코루틴 완료 이후로 미룬다.
        if (resetFieldOnIntermission) StartCoroutine(ResetFieldForIntermissionRoutine());
        else EnterIntermissionScreens();
    }

    private void EnterIntermissionScreens()
    {
        if (freezeTimeDuringIntermission) Time.timeScale = 0f;

        CloseAllIntermissionPanels(); // 이전 단계에서 열린 패널이 남아있지 않도록 항상 깨끗하게 시작
        SetCombatHudVisible(false);   // CloseAll보다 뒤에 와야 한다 - 패널의 Close()가 HUD를 다시 켤 수 있으므로
        ShowNextIntermissionStep();
    }

    // 정비 단계 내내 전투 HUD를 숨긴다. 각 Show*() 단계에서도 CloseAllIntermissionPanels() 뒤에
    // 다시 호출해, 패널이 닫히며 HUD를 되살리는 일이 없도록 한다.
    private void SetCombatHudVisible(bool visible)
    {
        foreach (GameObject hud in combatHudObjects)
        {
            if (hud != null) hud.SetActive(visible);
        }
    }

    /// <summary>
    /// 정비 화면에 들어가기 전 필드를 깨끗하게 만든다.
    ///
    /// 골드·경험치는 그냥 지우면 플레이어가 못 주운 보상이 증발해 손해이므로 지우기 전에
    /// 자동으로 수령 처리한다(= 자석 흡수). 예전에는 <c>CollectImmediately()</c>를 바로
    /// 불러 시각적 연출 없이 조용히 사라지기만 했는데, 사용자가 "자석으로 끌어모으는 연출이
    /// 없다"고 지적해(2026-08-12) 실제로 플레이어 쪽으로 날아가는 애니메이션을 재생한 뒤
    /// 수령하도록 바꿨다. <see cref="Time.timeScale"/>이 아직 1인 상태에서(이 시점에는 아직
    /// 멈추지 않았다) 코루틴으로 여러 프레임에 걸쳐 재생한다.
    ///
    /// <b>단, 부품 상자는 자석 흡수 대상이 아니다</b>(2026-08-10 사용자 지정) - 상자는
    /// 직접 가서 주워야 얻는 보상이라, 웨이브가 끝날 때까지 줍지 못했으면 그냥 사라진다.
    /// 화면 밖 상자를 화살표로 안내하는 <see cref="PartBoxIndicatorUI"/>가 의미를 갖는 것도
    /// 이 규칙 때문이다(자동으로 받아지면 굳이 찾아갈 이유가 없다).
    /// </summary>
    private IEnumerator ResetFieldForIntermissionRoutine()
    {
        PlayerRobotController player = FindFirstObjectByType<PlayerRobotController>();

        var rewardTargets = new List<RewardPickup>();
        int discardedPartBoxes = 0;
        foreach (RewardPickup pickup in FindObjectsByType<RewardPickup>(FindObjectsSortMode.None))
        {
            if (pickup == null) continue;

            if (pickup.Type == RewardType.PartBox)
            {
                Destroy(pickup.gameObject); // 수령하지 않고 버린다 - 직접 주웠어야 하는 보상
                discardedPartBoxes++;
                continue;
            }

            rewardTargets.Add(pickup);
        }

        int collectedRewards = rewardTargets.Count;

        if (collectedRewards > 0 && magnetCollectDuration > 0f && player != null)
        {
            yield return StartCoroutine(PlayMagnetFlightRoutine(rewardTargets, player.transform));
        }

        // 애니메이션 도중 플레이어와 실제로 겹쳐 트리거로 먼저 수령됐을 수도 있으므로
        // CollectImmediately()의 idempotent 가드(collected 플래그)를 그대로 믿고 다시 호출한다.
        foreach (RewardPickup pickup in rewardTargets)
        {
            if (pickup != null) pickup.CollectImmediately();
        }

        int clearedProjectiles = 0;
        foreach (Projectile projectile in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            Destroy(projectile.gameObject);
            clearedProjectiles++;
        }

        if (player != null) player.ReturnToStartPosition();

        if (collectedRewards > 0 || clearedProjectiles > 0 || discardedPartBoxes > 0)
        {
            Debug.Log($"정비 진입 - 필드 초기화 (보상 픽업 {collectedRewards}개 자동 수령, " +
                      $"투사체 {clearedProjectiles}개 정리, 못 주운 부품 상자 {discardedPartBoxes}개 소멸)");
        }

        EnterIntermissionScreens();
    }

    /// <summary>
    /// 골드/경험치 픽업을 플레이어 위치로 끌어당기는 시각 연출만 담당한다(실제 수령/파괴는
    /// 호출부가 애니메이션이 끝난 뒤 처리). 도중에 플레이어와 물리적으로 겹쳐 트리거가 먼저
    /// 발동해도 안전하도록, 애니메이션 시작 전 각 픽업의 Collider를 꺼서 이동 중 재수령을 막는다
    /// (트리거가 없으면 OnTriggerEnter 자체가 안 불린다 - CollectImmediately의 collected 가드에만
    /// 의존하는 것보다 확실하다).
    /// </summary>
    private IEnumerator PlayMagnetFlightRoutine(List<RewardPickup> rewards, Transform target)
    {
        var movers = new List<Transform>(rewards.Count);
        var starts = new List<Vector3>(movers.Capacity);

        foreach (RewardPickup pickup in rewards) CollectMover(pickup.GetComponent<Collider>(), pickup.transform, movers, starts);

        float elapsed = 0f;
        while (elapsed < magnetCollectDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 이 시점엔 아직 timeScale=1이지만, 정지 직전이라도 안전하게 unscaled 사용
            float t = Mathf.Clamp01(elapsed / magnetCollectDuration);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic: 처음엔 빠르게 딸려가고 끝에 감속

            Vector3 targetPos = target != null ? target.position : Vector3.zero;
            for (int i = 0; i < movers.Count; i++)
            {
                if (movers[i] == null) continue; // 애니메이션 도중 다른 경로로 파괴됐을 가능성 방어
                movers[i].position = Vector3.Lerp(starts[i], targetPos, eased);
            }

            yield return null;
        }
    }

    private static void CollectMover(Collider col, Transform t, List<Transform> movers, List<Vector3> starts)
    {
        if (col != null) col.enabled = false;
        movers.Add(t);
        starts.Add(t.position);
    }

    // 정비 단계는 세 화면(AI 코어/로봇 정비/상점)이 순서대로 하나씩만 보여야 하므로,
    // 다음 화면을 열기 전에 항상 나머지를 닫는다.
    private void CloseAllIntermissionPanels()
    {
        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
        if (moddingPanel != null) moddingPanel.Close();
        if (shopPanel != null) shopPanel.Close();
    }

    // 대기 중인 AI 코어 업그레이드 선택이 있으면 그것부터 전부 처리하고,
    // 그 다음 부품 상자가 있으면 로봇 정비 화면을, 없으면 바로 상점을 연다.
    private void ShowNextIntermissionStep()
    {
        if (RunState.PendingCoreUpgradeChoices > 0 && aiCoreManager != null && aiCoreUpgradePanel != null)
        {
            ShowAiCoreUpgradeStep();
        }
        else if (RunState.UnopenedPartBoxCount > 0 && moddingPanel != null)
        {
            ShowModdingStep();
        }
        else
        {
            ShowShop();
        }
    }

    private void ShowAiCoreUpgradeStep()
    {
        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);

        Button[] buttons = { option1Button, option2Button, option3Button };
        TextMeshProUGUI[] texts = { option1Text, option2Text, option3Text };

        List<AiCoreUpgradePool.Option> choices = aiCoreManager.DrawChoices(buttons.Length);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;

            button.onClick.RemoveAllListeners();

            if (i < choices.Count)
            {
                AiCoreUpgradePool.Option option = choices[i];
                if (texts[i] != null) texts[i].text = $"{option.displayName}\n{option.description}";
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => HandleUpgradeChosen(option));
            }
            else
            {
                button.gameObject.SetActive(false);
            }
        }

        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(true);
    }

    private void HandleUpgradeChosen(AiCoreUpgradePool.Option option)
    {
        aiCoreManager.ApplyChoice(option);

        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
        ShowNextIntermissionStep(); // 레벨업이 여러 번 밀려있으면 다음 선택 카드를 이어서 보여준다
    }

    private void ShowModdingStep()
    {
        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);
        moddingPanel.Open();
    }

    // 로봇 정비 화면의 "상점으로" 버튼이 눌렸을 때. 부품 상자가 남아있어도 강제로 다 열게
    // 하지 않고 바로 상점으로 넘어갈 수 있다(다음 웨이브 정비 때 다시 열 수 있다).
    private void HandleModdingProceedRequested() => ShowShop();

    private void ShowShop()
    {
        if (shopPanel == null)
        {
            Debug.LogWarning($"상점 패널이 연결되지 않아 웨이브 {lastEndedWaveNumber} 종료 후 화면을 띄우지 못했습니다.");
            return;
        }

        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);
        shopPanel.Open();
    }

    private void HandleNextWaveRequested()
    {
        CurrentState = State.Combat;
        IsIntermission = false;
        SetCombatHudVisible(true);
        Time.timeScale = 1f; // 정지 해제 - freezeTimeDuringIntermission이 꺼져 있어도 안전한 값

        if (waveManager != null) waveManager.StartNextWave();
    }
}
