using System;
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

    public State CurrentState { get; private set; } = State.Combat;

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
        lastEndedWaveNumber = waveNumber;
        CloseAllIntermissionPanels(); // 이전 단계에서 열린 패널이 남아있지 않도록 항상 깨끗하게 시작
        ShowNextIntermissionStep();
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
        shopPanel.Open();
    }

    private void HandleNextWaveRequested()
    {
        CurrentState = State.Combat;

        if (waveManager != null) waveManager.StartNextWave();
    }
}
