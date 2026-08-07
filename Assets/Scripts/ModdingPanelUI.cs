using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로봇 정비 화면(시안 이미지 기준). 부품 상자를 획득한 웨이브의 정비 시간에 열린다.
///
/// 흐름:
///  1. 화면에 들어오면 보유한 부품 상자가 <b>전부 자동으로 개봉</b>되어 좌측 임시 인벤토리에 쌓인다.
///  2. 인벤토리 칸을 누르면 그 파츠가 들어갈 수 있는 슬롯이 <b>노란색으로 강조</b>된다.
///  3. 강조된 슬롯을 누르면 서로 맞교환된다(슬롯에 있던 파츠가 인벤토리의 그 자리로 들어온다).
///  4. 교체 결과는 우측 능력치 패널에 즉시 반영된다.
///  5. "정비 완료"를 누르면 인벤토리에 남은 파츠는 <b>전부 사라지고</b> 상점으로 넘어간다.
///
/// 인벤토리 칸과 파츠 슬롯 칸은 개수가 데이터에 따라 달라지므로 씬에 미리 배치하지 않고
/// 여기서 코드로 생성한다. 씬에는 컨테이너(RectTransform)만 두면 된다.
/// </summary>
public class ModdingPanelUI : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private ModdingManager moddingManager;

    [Header("상단바")]
    [Tooltip("'로봇 정비' 제목 옆에 'WAVE 07 / 20'을 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI waveText;
    [SerializeField] private TextMeshProUGUI goldText;

    [Header("인벤토리 (좌측)")]
    [Tooltip("'인벤토리 5/20' 형식으로 적재량을 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI inventoryTitleText;
    [Tooltip("인벤토리 칸이 생성될 부모. GridLayoutGroup이 없으면 자동으로 붙는다")]
    [SerializeField] private RectTransform inventoryContent;
    [SerializeField] private Vector2 inventoryCellSize = new Vector2(96f, 96f);
    [SerializeField] private int inventoryColumns = 4;

    [Header("파츠 슬롯 (우측)")]
    [Tooltip("파츠 슬롯 칸이 생성될 부모. GridLayoutGroup이 없으면 자동으로 붙는다")]
    [SerializeField] private RectTransform slotContent;
    [SerializeField] private Vector2 slotCellSize = new Vector2(120f, 92f);
    [SerializeField] private int slotColumns = 3;

    [Header("능력치 / 안내")]
    [SerializeField] private TextMeshProUGUI statsText;
    [Tooltip("선택 상태와 교체 결과를 알려주는 안내 문구")]
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("정비 완료")]
    [SerializeField] private Button completeButton;

    [Header("색상")]
    [SerializeField] private Color cellNormalColor = new Color(0.24f, 0.26f, 0.29f, 1f);
    [SerializeField] private Color cellSelectedColor = new Color(0.45f, 0.62f, 0.85f, 1f);
    [SerializeField] private Color slotNormalColor = new Color(0.20f, 0.22f, 0.25f, 1f);
    [Tooltip("선택한 파츠를 장착할 수 있는 슬롯을 강조하는 색")]
    [SerializeField] private Color slotHighlightColor = new Color(0.95f, 0.75f, 0.15f, 1f);
    [Tooltip("머리 슬롯처럼 런 중 교체할 수 없는 칸의 색")]
    [SerializeField] private Color slotReadOnlyColor = new Color(0.16f, 0.17f, 0.19f, 1f);

    [Header("이 화면이 열려 있는 동안 숨길 전투 HUD")]
    [Tooltip("HP 표시처럼 전투 중에만 필요한 UI. ShopPanelUI와 같은 이유로 겹쳐 보이지 않게 숨긴다")]
    [SerializeField] private GameObject[] hideWhileOpen = new GameObject[0];

    /// <summary>"정비 완료" 버튼이 눌렸을 때 GameFlowManager가 받아갈 이벤트.</summary>
    public event System.Action OnProceedRequested;

    // 현재 선택된 인벤토리 칸(-1이면 선택 없음). 이 값이 있을 때만 슬롯이 노란색으로 열린다.
    private int selectedInventoryIndex = -1;

    private readonly List<Image> inventoryCellImages = new List<Image>();
    private readonly List<GameObject> spawnedCells = new List<GameObject>();

    private PlayerRobotController player;

    private void Awake()
    {
        if (completeButton != null) completeButton.onClick.AddListener(HandleCompleteClicked);
    }

    private void OnEnable() => RunState.OnChanged += Refresh;
    private void OnDisable() => RunState.OnChanged -= Refresh;

    // Canvas가 ConstantPixelSize라 Game View 해상도가 바뀌면 컨테이너의 픽셀 폭이 함께 바뀌는데,
    // GridLayoutGroup의 cellSize는 고정 픽셀이라 자동으로 따라오지 않는다(실측: 캔버스가 절반
    // 크기가 되자 4열이 2열로 무너졌다). 칸을 다시 만들 필요는 없고 크기만 매 프레임 맞춰준다.
    // 정비 중에는 Time.timeScale이 0이지만 Update는 계속 호출되므로 문제없다.
    private void Update()
    {
        if (inventoryContent != null) EnsureGrid(inventoryContent, inventoryCellSize, inventoryColumns);
        if (slotContent != null) EnsureGrid(slotContent, slotCellSize, slotColumns, SlotRowCount);
    }

    /// <summary>파츠 슬롯 칸 수(머리 1 + 모딩 슬롯) 기준 행 수. 슬롯 영역은 스크롤이 없어 이 행이 전부 들어가야 한다.</summary>
    private int SlotRowCount => Mathf.CeilToInt((1 + PartSlotExtensions.DisplayOrder.Length) / (float)Mathf.Max(1, slotColumns));

    /// <summary>부품 상자가 있을 때 GameFlowManager가 호출한다.</summary>
    public void Open()
    {
        SetCombatHudVisible(false);
        gameObject.SetActive(true);

        selectedInventoryIndex = -1;

        // 사용자 확정 사항: 플레이어가 상자를 하나씩 여는 게 아니라 들어오자마자 전부 자동 개봉된다.
        int opened = moddingManager != null ? moddingManager.OpenAllBoxesIntoInventory() : 0;

        SetHint(opened > 0
            ? $"부품 상자 {opened}개를 열어 인벤토리에 담았습니다. 장착할 파츠를 선택하세요."
            : "인벤토리에서 파츠를 선택하면 장착할 수 있는 슬롯이 노란색으로 표시됩니다.");

        // 방금 켠 패널은 아직 레이아웃이 계산되지 않아 컨테이너 폭이 0이다. 그대로 두면
        // EnsureGrid가 폴백 칸 크기를 쓰게 되므로, 폭을 확정시킨 뒤에 칸을 만든다.
        Canvas.ForceUpdateCanvases();

        Refresh();
    }

    public void Close()
    {
        // 임시 인벤토리는 이 화면을 벗어나는 순간 사라진다. "정비 완료" 버튼 외의 경로로
        // 닫히는 경우(GameFlowManager.CloseAllIntermissionPanels 등)에도 내용물이 다음
        // 웨이브로 새지 않도록 여기서도 비운다.
        if (moddingManager != null) moddingManager.ClearInventory();

        selectedInventoryIndex = -1;
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

    private void HandleCompleteClicked()
    {
        // 임시 인벤토리다 - 장착하지 않고 남긴 파츠는 여기서 전부 사라진다(사용자 확정 사항).
        if (moddingManager != null) moddingManager.ClearInventory();

        selectedInventoryIndex = -1;
        Close();
        OnProceedRequested?.Invoke();
    }

    // ---------------------------------------------------------------------
    // 선택 / 교체
    // ---------------------------------------------------------------------

    private void HandleInventoryCellClicked(int index)
    {
        // 같은 칸을 다시 누르면 선택이 풀린다.
        selectedInventoryIndex = selectedInventoryIndex == index ? -1 : index;

        if (selectedInventoryIndex >= 0 &&
            moddingManager != null &&
            moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot slot))
        {
            SetHint($"<color=#F2BF26>{slot.ToKorean()}</color> 슬롯을 누르면 교체됩니다. (다시 누르면 선택 해제)");
        }
        else
        {
            SetHint("인벤토리에서 파츠를 선택하면 장착할 수 있는 슬롯이 노란색으로 표시됩니다.");
        }

        Refresh();
    }

    private void HandleSlotClicked(PartSlot slot)
    {
        if (moddingManager == null) return;

        if (selectedInventoryIndex < 0)
        {
            SetHint("먼저 인벤토리에서 장착할 파츠를 선택하세요.");
            return;
        }

        if (!moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot allowed) || allowed != slot)
        {
            SetHint($"이 파츠는 {slot.ToKorean()} 슬롯에 장착할 수 없습니다.");
            return;
        }

        // 교체 전 이름을 기억해둔다 - 교체하고 나면 슬롯에는 새 파츠가 들어가 있다.
        string previousName = moddingManager.TryGetEquippedPart(slot, out PartData previous) ? previous.partName : "(비어 있음)";
        List<PartData> before = moddingManager.GetInventoryParts();
        string incomingName = selectedInventoryIndex < before.Count ? before[selectedInventoryIndex].partName : "파츠";

        if (!moddingManager.TrySwapInventoryWithSlot(selectedInventoryIndex, slot))
        {
            SetHint("교체하지 못했습니다.");
            return;
        }

        selectedInventoryIndex = -1;
        SetHint($"{slot.ToKorean()} 교체 완료: {previousName} → {incomingName}");

        // TrySwapInventoryWithSlot이 RunState.NotifyChanged()를 부르므로 Refresh는 이미 돌았지만,
        // 위에서 바꾼 안내 문구와 선택 해제 상태를 반영하려면 한 번 더 갱신해야 한다.
        Refresh();
    }

    private void SetHint(string message)
    {
        if (hintText != null) hintText.text = message;
    }

    // ---------------------------------------------------------------------
    // 갱신
    // ---------------------------------------------------------------------

    public void Refresh()
    {
        if (!gameObject.activeInHierarchy) return;

        RefreshHeader();
        RebuildInventory();
        RebuildSlots();
        RefreshStats();
    }

    private void RefreshHeader()
    {
        if (waveText != null)
        {
            WaveManager waveManager = FindFirstObjectByType<WaveManager>();
            int finalWave = waveManager != null ? waveManager.FinalWaveNumber : 0;
            waveText.text = finalWave > 0
                ? $"WAVE {RunState.WaveNumber:00} / {finalWave}"
                : $"WAVE {RunState.WaveNumber:00}";
        }

        if (goldText != null) goldText.text = RunState.Gold.ToString();

        if (inventoryTitleText != null)
        {
            int capacity = moddingManager != null ? moddingManager.PartBoxCapacity : 0;
            inventoryTitleText.text = $"인벤토리 {RunState.ModdingInventory.Count}/{capacity}";
        }
    }

    private void RebuildInventory()
    {
        if (inventoryContent == null) return;

        EnsureGrid(inventoryContent, inventoryCellSize, inventoryColumns);
        ClearChildren(inventoryContent);
        inventoryCellImages.Clear();

        List<PartData> parts = moddingManager != null ? moddingManager.GetInventoryParts() : new List<PartData>();

        for (int i = 0; i < parts.Count; i++)
        {
            PartData part = parts[i];
            int index = i; // 클로저가 반복 변수를 캡처하지 않도록 복사

            bool isSelected = index == selectedInventoryIndex;
            string label = $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color>\n{part.partName}\n<size=70%>{part.slot.ToKorean()}</size>";

            Image cellImage = CreateCell(inventoryContent, $"InventoryCell_{index}", label,
                                          isSelected ? cellSelectedColor : cellNormalColor,
                                          () => HandleInventoryCellClicked(index));
            inventoryCellImages.Add(cellImage);
        }

        // 시안처럼 빈 칸도 격자로 보이도록, 적재량까지(마지막 줄이 비지 않게 열 수의 배수로 올려서)
        // 클릭할 수 없는 더미 칸을 채운다.
        int capacity = moddingManager != null ? moddingManager.PartBoxCapacity : 0;
        int emptyCells = Mathf.Max(0, RoundUpToMultiple(Mathf.Max(capacity, parts.Count), inventoryColumns) - parts.Count);

        for (int i = 0; i < emptyCells; i++)
        {
            CreateCell(inventoryContent, $"InventoryEmpty_{i}", string.Empty, cellNormalColor * 0.6f, null);
        }
    }

    private void RebuildSlots()
    {
        if (slotContent == null) return;

        EnsureGrid(slotContent, slotCellSize, slotColumns, SlotRowCount);
        ClearChildren(slotContent);

        // 머리는 곧 로봇 종류 자체라 런 중 교체할 수 없다(조회 전용). 시안처럼 칸은 보여준다.
        CreateCell(slotContent, "Slot_Head", $"머리\n{GetRobotName()}\n<size=70%>런 중 교체 불가</size>",
                   slotReadOnlyColor, null);

        // 선택된 파츠가 들어갈 수 있는 슬롯만 노란색으로 연다.
        bool hasSelection = selectedInventoryIndex >= 0 &&
                            moddingManager != null &&
                            moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot _);
        PartSlot highlightSlot = default;
        if (hasSelection) moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out highlightSlot);

        foreach (PartSlot slot in PartSlotExtensions.DisplayOrder)
        {
            PartSlot captured = slot;
            bool isHighlighted = hasSelection && highlightSlot == slot;

            string body = moddingManager != null && moddingManager.TryGetEquippedPart(slot, out PartData part)
                ? $"<color={part.grade.ToColorHex()}>{part.partName}</color>\n<size=70%>{part.BuildDescription()}</size>"
                : "<size=70%>(비어 있음)</size>";

            CreateCell(slotContent, $"Slot_{slot}", $"{slot.ToKorean()}\n{body}",
                       isHighlighted ? slotHighlightColor : slotNormalColor,
                       () => HandleSlotClicked(captured));
        }
    }

    private void RefreshStats()
    {
        if (statsText == null) return;

        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null)
        {
            statsText.text = "[능력치]\n(플레이어를 찾을 수 없음)";
            return;
        }

        // 파츠를 교체하면 RunState.OnChanged로 PlayerRobotController가 스탯을 다시 계산하므로,
        // 여기서는 그 결과만 읽으면 교체 결과가 곧바로 보인다.
        float weightSum = moddingManager != null ? moddingManager.GetEquippedWeaponWeightSum() : 0f;
        float weightCapacity = moddingManager != null ? moddingManager.GetTotalWeightCapacity() : 0f;
        int discSlots = moddingManager != null ? moddingManager.DiscSlotCount : 0;
        bool overweight = weightSum > weightCapacity;

        statsText.text =
            "[능력치]\n" +
            $"체력 {player.CurrentHp}/{player.MaxHp}\n" +
            $"공격력 {player.Atk}\n" +
            $"방어력 {player.Def}\n" +
            $"치명타 확률 {player.Cc:0.##}%\n" +
            $"치명타 데미지 {player.Cd:0.##}\n" +
            $"이동속도 {player.MoveSpeed:0.##}\n" +
            $"회피율 {player.Avoid:0.##}\n" +
            $"행운 {player.Luck:0.##}\n" +
            $"질량 {player.Mess:0.##}\n" +
            $"디스크 슬롯 {RunState.EquippedDiscIds.Count}/{discSlots}\n" +
            (overweight
                ? $"<color=#FF5555>무기 무게 {weightSum:0.#} / {weightCapacity:0.#} (초과)</color>"
                : $"무기 무게 {weightSum:0.#} / {weightCapacity:0.#}");
    }

    private string GetRobotName()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return "(알 수 없음)";

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.robot_name
            : $"ID {player.RobotId}";
    }

    // ---------------------------------------------------------------------
    // UI 생성 헬퍼 (칸 개수가 데이터에 따라 달라져 씬에 미리 배치할 수 없다)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 컨테이너에 GridLayoutGroup이 없으면 붙이고 칸 크기/열 수를 맞춘다.
    ///
    /// Canvas가 ConstantPixelSize라 Game View 해상도에 따라 캔버스 픽셀 크기가 크게 달라진다
    /// (실측 640x480 ~ 3840x2160). 칸 크기를 고정 픽셀로 두면 해상도에 따라 칸이 우스꽝스럽게
    /// 커지거나 작아지므로, 컨테이너의 실제 폭에서 열 수에 맞춰 계산한다. 인스펙터의
    /// cellSize는 레이아웃이 아직 계산되지 않았을 때 쓰는 폴백값 겸 가로세로 비율 기준이다.
    /// </summary>
    /// <param name="fitRows">
    /// 0보다 크면 칸 높이도 컨테이너 높이를 이 행 수로 나눠 맞춘다(스크롤이 없는 슬롯 영역용).
    /// 0이면 가로세로 비율만 유지한다 - 인벤토리는 스크롤이 있어 세로로 넘쳐도 되기 때문.
    /// </param>
    private static void EnsureGrid(RectTransform container, Vector2 fallbackCellSize, int columns, int fitRows = 0)
    {
        GridLayoutGroup grid = container.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = container.gameObject.AddComponent<GridLayoutGroup>();

        const float spacing = 6f;
        int columnCount = Mathf.Max(1, columns);

        Vector2 cellSize = fallbackCellSize;
        float availableWidth = container.rect.width;
        if (availableWidth > 1f && fallbackCellSize.x > 0f)
        {
            float cellWidth = (availableWidth - spacing * (columnCount - 1)) / columnCount;
            if (cellWidth > 1f)
            {
                float aspect = fallbackCellSize.y / fallbackCellSize.x;
                float cellHeight = cellWidth * aspect;

                // 슬롯 영역은 스크롤이 없어서 비율대로 두면 아래로 넘쳐 다른 패널을 덮는다
                // (실측: 3행이 컨테이너 높이의 1.7배가 됐다). 높이도 컨테이너에 맞춘다.
                if (fitRows > 0)
                {
                    float availableHeight = container.rect.height;
                    if (availableHeight > 1f)
                    {
                        float fitted = (availableHeight - spacing * (fitRows - 1)) / fitRows;
                        if (fitted > 1f) cellHeight = fitted;
                    }
                }

                cellSize = new Vector2(cellWidth, cellHeight);
            }
        }

        grid.cellSize = cellSize;
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        grid.childAlignment = TextAnchor.UpperLeft;

        // 스크롤 뷰의 content로 쓰일 때 칸이 늘어난 만큼 높이가 자라야 한다.
        ContentSizeFitter fitter = container.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = container.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void ClearChildren(RectTransform container)
    {
        // Destroy()는 프레임 끝에야 실제로 파괴되므로, 같은 프레임에 Refresh가 두 번 돌면
        // (Open()의 명시적 호출 + OpenAllBoxesIntoInventory()의 NotifyChanged) 아직 살아있는
        // 이전 칸 위에 새 칸이 겹쳐 쌓인다 - 실측에서 슬롯이 9개가 아니라 18개로 잡혔다.
        //
        // DestroyImmediate는 물리 트리거 콜백 중에는 금지되어 쓸 수 없다(정비 화면이 열린 채
        // 보상 픽업이 흡수되면 OnTriggerEnter → NotifyChanged → 여기로 들어와 예외가 났다).
        // 그래서 부모에서 먼저 떼어내 childCount를 즉시 정확하게 만들고, 파괴는 Unity에 맡긴다.
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            spawnedCells.Remove(child);
            child.transform.SetParent(null, false);
            Destroy(child);
        }
    }

    /// <summary>
    /// 칸 하나(배경 Image + 클릭 Button + 가운데 정렬 TMP 텍스트)를 만든다.
    /// onClick이 null이면 클릭할 수 없는 조회 전용 칸이 된다.
    /// </summary>
    private Image CreateCell(RectTransform parent, string name, string label, Color color, System.Action onClick)
    {
        var cell = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cell.transform.SetParent(parent, false);

        Image image = cell.GetComponent<Image>();
        image.color = color;

        if (onClick != null)
        {
            Button button = cell.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
        }

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(cell.transform, false);

        var rect = (RectTransform)textGo.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);

        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.richText = true;

        // Canvas가 ConstantPixelSize라 해상도에 따라 고정 픽셀 폰트가 칸을 넘친다
        // (프로젝트 안내.md 참고). 새로 만드는 텍스트는 자동 크기 조절을 켠다.
        // 상한을 낮게 잡으면 고해상도에서 큰 칸에 깨알 같은 글씨가 남으므로(실측 3840x2160에서
        // 칸 352px에 16px 글씨) 넉넉히 두고, 실제 크기는 자동 조절에 맡긴다.
        text.enableAutoSizing = true;
        text.fontSizeMin = 6f;
        text.fontSizeMax = 42f;
        text.enableWordWrapping = true;

        spawnedCells.Add(cell);
        return image;
    }

    private static int RoundUpToMultiple(int value, int multiple)
    {
        if (multiple <= 0) return value;
        int remainder = value % multiple;
        return remainder == 0 ? value : value + (multiple - remainder);
    }
}
