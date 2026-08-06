using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로봇 정비 화면(기획서 p.12의 "[로봇 정비]" 단계). 획득한 부품 상자가 있을 때만
/// GameFlowManager가 이 화면을 연다. 상자를 열어 파츠를 얻고 바로 장착까지 끝나며(교체
/// 여부를 고르는 선택지는 없음 - 기획서 범위에서 단순화), 남은 상자가 있어도 "상점으로"
/// 버튼으로 언제든 다음 단계로 넘어갈 수 있다(강제로 다 열게 하지 않음).
/// </summary>
public class ModdingPanelUI : MonoBehaviour
{
    [SerializeField] private ModdingManager moddingManager;

    [SerializeField] private TextMeshProUGUI boxCountText;
    [SerializeField] private Button openBoxButton;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI equippedPartsText;
    [SerializeField] private Button proceedButton;

    [Header("이 화면이 열려 있는 동안 숨길 전투 HUD")]
    [Tooltip("HP 표시처럼 전투 중에만 필요한 UI. ShopPanelUI와 같은 이유로 겹쳐 보이지 않게 숨긴다")]
    [SerializeField] private GameObject[] hideWhileOpen = new GameObject[0];

    /// <summary>"상점으로" 버튼이 눌렸을 때 GameFlowManager가 받아갈 이벤트.</summary>
    public event System.Action OnProceedRequested;

    private void Awake()
    {
        if (openBoxButton != null) openBoxButton.onClick.AddListener(HandleOpenBoxClicked);
        if (proceedButton != null) proceedButton.onClick.AddListener(HandleProceedClicked);
    }

    private void OnEnable() => RunState.OnChanged += Refresh;
    private void OnDisable() => RunState.OnChanged -= Refresh;

    /// <summary>부품 상자가 있을 때 GameFlowManager가 호출한다.</summary>
    public void Open()
    {
        if (resultText != null) resultText.text = string.Empty;

        SetCombatHudVisible(false);
        gameObject.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        SetCombatHudVisible(true);
        gameObject.SetActive(false);
    }

    private void SetCombatHudVisible(bool visible)
    {
        foreach (GameObject hud in hideWhileOpen)
        {
            if (hud != null) hud.SetActive(visible);
        }
    }

    private void HandleOpenBoxClicked()
    {
        if (moddingManager == null) return;

        if (!moddingManager.TryOpenBox(out PartData part))
        {
            if (resultText != null) resultText.text = "열 수 있는 부품 상자가 없습니다";
            return;
        }

        if (resultText != null)
        {
            resultText.text = $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> " +
                               $"{part.partName} ({part.slot.ToKorean()}) 획득 - 즉시 장착됨\n{part.BuildDescription()}";
        }

        Refresh();
    }

    private void HandleProceedClicked()
    {
        Close();
        OnProceedRequested?.Invoke();
    }

    public void Refresh()
    {
        if (!gameObject.activeInHierarchy) return;

        if (boxCountText != null) boxCountText.text = $"남은 부품 상자 {RunState.UnopenedPartBoxCount}개";
        if (openBoxButton != null) openBoxButton.interactable = RunState.UnopenedPartBoxCount > 0;

        if (equippedPartsText == null) return;

        var lines = new System.Collections.Generic.List<string> { "[장착된 파츠]" };
        foreach (PartSlot slot in (PartSlot[])System.Enum.GetValues(typeof(PartSlot)))
        {
            string line = moddingManager != null && moddingManager.TryGetEquippedPart(slot, out PartData part)
                ? $"{slot.ToKorean()}: <color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName} - {part.BuildDescription()}"
                : $"{slot.ToKorean()}: (없음)";
            lines.Add(line);
        }

        if (moddingManager != null)
        {
            lines.Add($"무기 무게 지탱: {moddingManager.GetEquippedWeaponWeightSum():0.#} / {moddingManager.GetTotalWeightCapacity():0.#}");
        }

        equippedPartsText.text = string.Join("\n", lines);
    }
}
