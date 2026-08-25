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
    [Tooltip("머리 + 무기 소켓 카드가 생성될 부모(2026-08-18 UI 기획서 반영 - 한 줄로 따로 묶는다). " +
             "비워두면 예전처럼 slotContent 맨 앞에 섞어 그린다(호환용)")]
    [SerializeField] private RectTransform headSocketRow;
    [Tooltip("나머지 파츠 슬롯 칸이 생성될 부모. GridLayoutGroup이 없으면 자동으로 붙는다")]
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

    // <b>장착 파츠 조회 모드</b>(2026-08-19 사용자 리포트: "이미 장착된 로봇 파츠의 설명을
    // 클릭해도 볼 수 없다"). 예전에는 슬롯 클릭이 오직 "교체"만 담당해서, 인벤토리에서 뭔가를
    // 고르지 않은 상태로 슬롯을 누르면 "먼저 인벤토리에서 파츠를 선택하세요" 안내만 나오고
    // 이미 끼워둔 파츠의 능력치는 확인할 방법이 아예 없었다.
    // 이제 인벤토리 선택이 없을 때 슬롯을 누르면 그 슬롯의 장착 파츠 설명을 설명 칸에 띄운다.
    // (인벤토리 선택이 있을 때는 기존대로 교체가 우선 - 교체 흐름을 방해하지 않는다.)
    private bool inspectingEquipped;
    private PartSlot inspectedSlot;
    private int inspectedWeaponSocket = -1; // >=0이면 무기 소켓을 조회 중(inspectedSlot은 무시)

    /// <summary>장착 파츠 조회 상태를 해제한다(인벤토리 선택·교체·화면 열기 시).</summary>
    private void ClearEquippedInspection()
    {
        inspectingEquipped = false;
        inspectedWeaponSocket = -1;
    }

    private readonly List<Image> inventoryCellImages = new List<Image>();

    private PlayerRobotController player;
    private AiCoreManager aiCoreManager; // 2026-08-18 "레벨: N (MAX:M)" 표시용

    // 인벤토리 오른쪽에 코드로 만드는 설명 칸(2026-08-18 사용자 요청: 팝업이 아니라 옆 공간에
    // 설명을 띄우고, 교체 대상 파츠 설명도 함께 보여준다). 씬에 배치하지 않으므로 에디터
    // 도메인 리로드로 참조가 날아갈 수 있다 - AiCoreExtraButtonsUI에서 겪은 함정이라
    // Open()/Refresh()에서 없으면 다시 만든다.
    private RectTransform detailPanel;
    private Image detailSelectedIcon;
    private TextMeshProUGUI detailSelectedText;
    private Image detailTargetIcon;
    private TextMeshProUGUI detailTargetText;
    private TextMeshProUGUI detailTargetTitle;

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

        // 2026-08-18 UI 기획서 반영 - 머리 + 무기 소켓은 한 줄(1행)에 나란히 그린다.
        // 소켓 수(WeaponSocketCount)만큼 열이 늘어나므로 여기서 매 프레임 열 수를 다시 맞춘다.
        if (headSocketRow != null) EnsureGrid(headSocketRow, slotCellSize, 1 + WeaponSocketCount, fitRows: 1);
    }

    /// <summary>파츠 슬롯(slotContent) 칸 수 기준 행 수. headSocketRow가 따로 있으면 머리·무기
    /// 소켓은 거기서 그려지므로 여기는 DisplayOrder 항목만 센다. headSocketRow가 없으면(씬에
    /// 아직 안 뚫려 있으면) 예전처럼 머리 1 + 소켓 N까지 여기서 함께 채운다(호환용).</summary>
    private int SlotRowCount
    {
        get
        {
            int count = PartSlotExtensions.DisplayOrder.Length;
            if (headSocketRow == null) count += 1 + WeaponSocketCount;
            return Mathf.CeilToInt(count / (float)Mathf.Max(1, slotColumns));
        }
    }

    private int WeaponSocketCount => moddingManager != null ? moddingManager.ActiveSocketCount : 0;

    /// <summary>부품 상자가 있을 때 GameFlowManager가 호출한다.</summary>
    public void Open()
    {
        SetCombatHudVisible(false);
        gameObject.SetActive(true);

        selectedInventoryIndex = -1;
        ClearEquippedInspection();

        // 사용자 확정 사항: 플레이어가 상자를 하나씩 여는 게 아니라 들어오자마자 전부 자동 개봉된다.
        int opened = moddingManager != null ? moddingManager.OpenAllBoxesIntoInventory() : 0;

        SetHint(opened > 0
            ? Loc.T("modding.hint.opened_boxes", opened)
            : Loc.T("modding.hint.select_part"));

        // 방금 켠 패널은 아직 레이아웃이 계산되지 않아 컨테이너 폭이 0이다. 그대로 두면
        // EnsureGrid가 폴백 칸 크기를 쓰게 되므로, 폭을 확정시킨 뒤에 칸을 만든다.
        Canvas.ForceUpdateCanvases();

        // 폭이 확정된 뒤라야 배경 아트의 테두리 두께를 비율로 환산할 수 있다(UiSafeArea 주석 참고).
        ApplyTextSafeArea();

        Refresh();
    }

    /// <summary>
    /// 패널 위 글자들이 <b>배경 아트의 테두리를 침범하지 않도록</b> 앵커를 안쪽으로 밀고,
    /// 넘칠 때 밖으로 흘러나가지 않도록 넘침 처리를 바꾼다(2026-08-25 다국어 폴리싱).
    ///
    /// <para><b>왜 생긴 문제인가</b>: 씬의 글자들이 전부 <c>overflowMode = Overflow</c>였다.
    /// 이 모드에서는 자동 크기 조절이 <b>세로 맞춤을 강제하지 않아</b>, 줄 수가 늘어나면
    /// 그대로 패널 밖으로 흘러나간다. 한글은 짧아서 안 드러났지만 영어로 바꾸자 제목·골드·
    /// 능력치·힌트·파츠 설명 다섯 곳이 한꺼번에 테두리를 넘었다(2026-08-25 사용자 지적).</para>
    ///
    /// <para><b>왜 씬에 숫자를 박지 않고 런타임에 계산하나</b>: "UI 제작 규칙"이 여백을 임의의
    /// 숫자로 정하지 말라고 한다. <see cref="UiSafeArea"/>가 배경 스프라이트의 실제 9-slice
    /// border에서 역산하므로, 나중에 배경 아트가 바뀌어도 이 코드를 고칠 필요가 없다.</para>
    ///
    /// <para>상단 배너(<c>Purple_ui02</c>)는 좌우 끝이 뾰족한 모양이라 <b>좌우만</b> 맞춘다 -
    /// 세로 테두리(25px)는 장식용 베벨이라 그만큼 안쪽으로 밀면 글자가 불필요하게 작아진다.</para>
    /// </summary>
    private void ApplyTextSafeArea()
    {
        // 한 줄짜리 제목·수치 - 좌우만 맞추고 <b>줄바꿈을 끈다</b>.
        // 줄바꿈을 켜둔 채 폭만 좁히면 "Robot Loadout"이 두 줄로 접혀 배너 아래로 삐져나온다
        // (2026-08-25에 한 번 그렇게 만들었다가 고쳤다). 한 줄로 두면 자동 크기 조절이
        // 폭에 맞춰 글자를 줄여준다.
        TextMeshProUGUI topTitle = FindPanelText("TopBar/TitleText");
        ClampText(topTitle, false, true);
        ClampText(waveText, false, true);
        ClampText(goldText, false, true);

        // 제목과 웨이브 표시는 같은 배너 안에 좌우로 나란히 놓인 <b>별개의 칸</b>이고 둘 다
        // 왼쪽 정렬이다. 한글 제목("로봇 정비")은 짧아서 칸을 다 못 채워 저절로 간격이 생겼지만,
        // 영어("Robot Loadout")는 칸을 꽉 채워 두 글자가 맞붙어 한 문장처럼 읽힌다.
        // 칸 경계에 기대지 말고 여백을 명시적으로 준다(골드 표시가 코인 아이콘 자리를
        // margin.x=58로 비워두는 것과 같은 방식).
        // 여백은 <b>한 번만</b> 더한다. Open()은 웨이브마다 불리므로 매번 더하면 누적돼서
        // 20웨이브째엔 제목이 사라진다(앵커 클램프는 안쪽으로만 좁히므로 여러 번 불려도 안전하다).
        if (!text_gutters_applied)
        {
            text_gutters_applied = true;
            AddSideMargin(topTitle, 0f, TitleRightGutter);
            AddSideMargin(waveText, WaveLeftGutter, 0f);
        }
        ClampText(inventoryTitleText, false, true);
        ClampText(FindPanelText("SlotPanel/SlotTitleText"), false, true);

        // 여러 줄 본문 - 위아래로도 넘쳤으므로 사방을 다 맞춘다.
        ClampText(statsText, true, false);
        ClampText(hintText, true, false);

        // 코드로 만든 "파츠 설명" 패널. <b>세로는 건드리지 않는다</b> - 제목/아이콘/본문이
        // 정해진 비율로 쌓여 있어서 세로로 밀면 제목 칸이 9px까지 찌그러진다(실제로 그랬다).
        if (detailPanel != null)
        {
            foreach (TextMeshProUGUI label in detailPanel.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                bool isHeading = label.name == "Title" || label.name == "TargetTitle";
                ClampText(label, false, isHeading);
            }
        }
    }

    /// <summary>상단 배너 좌우 여백을 이미 더했는지(누적 방지).</summary>
    private bool text_gutters_applied;

    /// <summary>상단 배너에서 제목 오른쪽에 두는 여백(픽셀).</summary>
    private const float TitleRightGutter = 20f;

    /// <summary>상단 배너에서 웨이브 표시 왼쪽에 두는 여백(픽셀).</summary>
    private const float WaveLeftGutter = 28f;

    /// <summary>글자의 좌우 여백만 더한다(세로 여백과 기존 값은 건드리지 않는다).</summary>
    private static void AddSideMargin(TextMeshProUGUI label, float left, float right)
    {
        if (label == null) return;

        Vector4 m = label.margin;
        label.margin = new Vector4(m.x + left, m.y, m.z + right, m.w);
    }

    /// <summary>글자 하나를 배경 테두리 안쪽으로 밀고, 넘치면 밖으로 새지 않게 한다.</summary>
    /// <param name="singleLine">제목처럼 한 줄로 둬야 하는 글자. 줄바꿈을 끄고 크기로만 맞춘다.</param>
    private static void ClampText(TextMeshProUGUI label, bool vertical, bool singleLine)
    {
        if (label == null) return;

        UiSafeArea.ClampIntoBackground((RectTransform)label.transform, 0.01f, vertical);

        if (singleLine) label.textWrappingMode = TextWrappingModes.NoWrap;

        // Overflow면 자동 크기 조절이 세로 맞춤을 포기한다 - 반드시 다른 모드로 둔다.
        if (label.overflowMode == TextOverflowModes.Overflow)
        {
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
        if (!label.enableAutoSizing)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = 6f;
        }
    }

    /// <summary>씬에 배치돼 있지만 인스펙터에 연결되지 않은 글자를 경로로 찾는다.</summary>
    private TextMeshProUGUI FindPanelText(string relativePath)
    {
        Transform root = transform.Find("Root");
        if (root == null) return null;

        Transform found = root.Find(relativePath);
        return found == null ? null : found.GetComponent<TextMeshProUGUI>();
    }

    public void Close()
    {
        // 임시 인벤토리는 이 화면을 벗어나는 순간 사라진다. "정비 완료" 버튼 외의 경로로
        // 닫히는 경우(GameFlowManager.CloseAllIntermissionPanels 등)에도 내용물이 다음
        // 웨이브로 새지 않도록 여기서도 비운다.
        if (moddingManager != null) moddingManager.ClearInventory();

        selectedInventoryIndex = -1;
        ClearEquippedInspection();
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
        ClearEquippedInspection();
        Close();
        OnProceedRequested?.Invoke();
    }

    // ---------------------------------------------------------------------
    // 선택 / 교체
    // ---------------------------------------------------------------------

    private void HandleInventoryCellClicked(int index)
    {
        // 인벤토리를 고르면 장착 파츠 조회 모드는 끝난다(설명 칸의 주인이 바뀐다).
        ClearEquippedInspection();

        // 같은 칸을 다시 누르면 선택이 풀린다.
        selectedInventoryIndex = selectedInventoryIndex == index ? -1 : index;

        if (selectedInventoryIndex >= 0 &&
            moddingManager != null &&
            moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot slot))
        {
            SetHint(Loc.T("modding.hint.press_slot", $"<color=#F2BF26>{slot.ToDisplayName()}</color>"));
        }
        else
        {
            SetHint(Loc.T("modding.hint.select_part"));
        }

        Refresh();
    }

    private void HandleSlotClicked(PartSlot slot)
    {
        if (moddingManager == null) return;

        // 인벤토리 선택이 없으면 "교체"가 아니라 "조회"다 - 이미 장착된 파츠의 설명을 띄운다
        // (2026-08-19 사용자 리포트. 예전에는 여기서 안내 문구만 내보내고 끝나서 장착 파츠의
        //  능력치를 확인할 방법이 없었다).
        if (selectedInventoryIndex < 0)
        {
            // 같은 슬롯을 다시 누르면 조회가 풀린다(인벤토리 칸과 같은 토글 규칙).
            bool sameSlot = inspectingEquipped && inspectedWeaponSocket < 0 && inspectedSlot == slot;
            if (sameSlot) ClearEquippedInspection();
            else
            {
                inspectingEquipped = true;
                inspectedSlot = slot;
                inspectedWeaponSocket = -1;
            }

            SetHint(moddingManager.TryGetEquippedPart(slot, out PartData _)
                ? Loc.T("modding.hint.slot_filled", slot.ToDisplayName())
                : Loc.T("modding.hint.slot_empty", slot.ToDisplayName()));

            Refresh();
            return;
        }

        if (!moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot allowed) || allowed != slot)
        {
            SetHint(Loc.T("modding.hint.cannot_equip", slot.ToDisplayName()));
            return;
        }

        // 교체 전 이름을 기억해둔다 - 교체하고 나면 슬롯에는 새 파츠가 들어가 있다.
        string previousName = moddingManager.TryGetEquippedPart(slot, out PartData previous) ? previous.Part() : Loc.T("common.empty");
        List<PartData> before = moddingManager.GetInventoryParts();
        string incomingName = selectedInventoryIndex < before.Count ? before[selectedInventoryIndex].Part() : Loc.T("modding.part");

        if (!moddingManager.TrySwapInventoryWithSlot(selectedInventoryIndex, slot, out string reason))
        {
            // 무게 초과처럼 이유가 분명한 경우에는 그대로 보여준다(그냥 "실패"만 뜨면 원인을 알 수 없다).
            SetHint(reason.Length > 0 ? Loc.T("modding.swap_failed_reason", reason) : Loc.T("modding.swap_failed"));
            return;
        }

        selectedInventoryIndex = -1;
        ClearEquippedInspection();
        SetHint(Loc.T("modding.swap_done", slot.ToDisplayName(), previousName, incomingName));

        // TrySwapInventoryWithSlot이 RunState.NotifyChanged()를 부르므로 Refresh는 이미 돌았지만,
        // 위에서 바꾼 안내 문구와 선택 해제 상태를 반영하려면 한 번 더 갱신해야 한다.
        Refresh();
    }

    /// <summary>
    /// HandleSlotClicked와 같은 역할이지만 대상이 PartSlot 하나가 아니라 무기 소켓 인덱스다
    /// (소켓마다 독립적으로 파츠를 낄 수 있어야 하기 때문 - 2026-08-12 "무기 소켓 개별화" 플랜).
    /// </summary>
    private void HandleWeaponSocketCellClicked(int socketIndex)
    {
        if (moddingManager == null) return;

        // HandleSlotClicked와 같은 규칙 - 인벤토리 선택이 없으면 해당 소켓의 장착 파츠를 조회한다.
        if (selectedInventoryIndex < 0)
        {
            bool sameSocket = inspectingEquipped && inspectedWeaponSocket == socketIndex;
            if (sameSocket) ClearEquippedInspection();
            else
            {
                inspectingEquipped = true;
                inspectedWeaponSocket = socketIndex;
            }

            SetHint(moddingManager.TryGetEquippedWeaponSocketPart(socketIndex, out PartData _)
                ? Loc.T("modding.hint.slot_filled", Loc.T("modding.weaponsocket_n", socketIndex + 1))
                : Loc.T("modding.hint.slot_empty", Loc.T("modding.weaponsocket_n", socketIndex + 1)));

            Refresh();
            return;
        }

        if (!moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot allowed) || allowed != PartSlot.ArmWeaponSocket)
        {
            SetHint(Loc.T("modding.hint.cannot_equip", Loc.T("partslot.weaponsocket")));
            return;
        }

        string previousName = moddingManager.TryGetEquippedWeaponSocketPart(socketIndex, out PartData previous) ? previous.Part() : Loc.T("common.empty");
        List<PartData> before = moddingManager.GetInventoryParts();
        string incomingName = selectedInventoryIndex < before.Count ? before[selectedInventoryIndex].Part() : Loc.T("modding.part");

        if (!moddingManager.TrySwapInventoryWithWeaponSocket(selectedInventoryIndex, socketIndex, out string reason))
        {
            SetHint(reason.Length > 0 ? Loc.T("modding.swap_failed_reason", reason) : Loc.T("modding.swap_failed"));
            return;
        }

        selectedInventoryIndex = -1;
        ClearEquippedInspection();
        SetHint(Loc.T("modding.swap_done", Loc.T("modding.weaponsocket_n", socketIndex + 1), previousName, incomingName));

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

        EnsureDetailPanel();

        RefreshHeader();
        RebuildInventory();
        RebuildSlots();
        RefreshStats();
        RefreshDetail();
    }

    private void RefreshHeader()
    {
        if (waveText != null)
        {
            WaveManager waveManager = FindFirstObjectByType<WaveManager>();
            int finalWave = waveManager != null ? waveManager.FinalWaveNumber : 0;
            // 엔드리스 모드(2026-08-19)에서는 20이 더 이상 진짜 끝이 아니므로 분모를 숨긴다.
            waveText.text = RunState.IsEndless
                ? $"WAVE {RunState.WaveNumber:00} / {Loc.T("common.endless")}"
                : finalWave > 0
                    ? $"WAVE {RunState.WaveNumber:00} / {finalWave}"
                    : $"WAVE {RunState.WaveNumber:00}";
        }

        if (goldText != null)
        {
            goldText.text = RunState.Gold.ToString();
            // 골드 아이콘이 숫자를 덮지 않도록 글자 왼쪽 여백을 줄 너비에 비례해 다시 잡는다
            // (씬에는 아이콘이 정규화 앵커, 여백이 절대 픽셀로 섞여 있어 FHD보다 큰
            // 해상도에서 겹쳤다 - 2026-08-23 버그 수정, IconTextRowLayout 주석 참고).
            IconTextRowLayout.FitTextAfterLeadingIcon(goldText);
        }

        if (inventoryTitleText != null)
        {
            int capacity = moddingManager != null ? moddingManager.PartBoxCapacity : 0;
            inventoryTitleText.text = $"{Loc.T("modding.inventory")} {RunState.ModdingInventory.Count}/{capacity}";
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

            // 사용자 확정(2026-08-18): 인벤토리는 글씨 없이 아이콘만 보여주고, 일반 등급이
            // 아니면 칸을 등급색으로 칠한다. 이름·수치는 옆 설명 칸이 맡는다.
            // 선택 표시는 등급색과 섞지 않고 **슬롯 강조와 같은 노란색**으로 덮는다.
            // 섞으면 희귀(파랑)·서사(보라) 등급색과 구분이 안 된다(실측에서 헷갈렸다).
            // 노란색은 어떤 등급색과도 겹치지 않고, "이 파츠가 저 노란 슬롯에 들어간다"는
            // 짝도 눈으로 바로 이어진다.
            Color cellColor = isSelected
                ? slotHighlightColor
                : part.grade.ToCellColor(cellNormalColor);

            Image cellImage = CreateIconCell(inventoryContent, $"InventoryCell_{index}",
                                             PartIconLibrary.Get(part), cellColor, null, true,
                                             () => HandleInventoryCellClicked(index));
            inventoryCellImages.Add(cellImage);
        }

        // 시안처럼 빈 칸도 격자로 보이도록, 적재량까지(마지막 줄이 비지 않게 열 수의 배수로 올려서)
        // 클릭할 수 없는 더미 칸을 채운다.
        int capacity = moddingManager != null ? moddingManager.PartBoxCapacity : 0;
        int emptyCells = Mathf.Max(0, RoundUpToMultiple(Mathf.Max(capacity, parts.Count), inventoryColumns) - parts.Count);

        for (int i = 0; i < emptyCells; i++)
        {
            CreateIconCell(inventoryContent, $"InventoryEmpty_{i}", null, cellNormalColor * 0.6f, null, false, null);
        }
    }

    private void RebuildSlots()
    {
        if (slotContent == null) return;

        // 선택된 파츠가 들어갈 수 있는 슬롯만 노란색으로 연다.
        bool hasSelection = selectedInventoryIndex >= 0 &&
                            moddingManager != null &&
                            moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out PartSlot _);
        PartSlot highlightSlot = default;
        if (hasSelection) moddingManager.TryGetEquipableSlot(selectedInventoryIndex, out highlightSlot);

        // 무기 소켓은 슬롯 하나가 아니라 소켓 인덱스별로 카드를 그린다(2026-08-12 "무기 소켓
        // 개별화" 플랜) - 선택된 파츠가 무기 소켓 종류면 N칸이 전부 노란색으로 열린다.
        bool highlightWeaponSockets = hasSelection && highlightSlot == PartSlot.ArmWeaponSocket;

        // 2026-08-18 UI 기획서 반영 - 머리 + 무기 소켓은 별도 컨테이너(headSocketRow)에 한 줄로
        // 그린다. 씬에 아직 안 뚫려 있으면(headSocketRow == null) 예전처럼 slotContent 맨 앞에
        // 섞어 그려서 호환을 유지한다.
        RectTransform headRow = headSocketRow != null ? headSocketRow : slotContent;
        if (headSocketRow != null)
        {
            EnsureGrid(headSocketRow, slotCellSize, 1 + WeaponSocketCount, fitRows: 1);
            ClearChildren(headSocketRow);
        }

        EnsureGrid(slotContent, slotCellSize, slotColumns, SlotRowCount);
        ClearChildren(slotContent);

        // 머리는 곧 로봇 종류 자체라 런 중 교체할 수 없다(조회 전용). 시안처럼 칸은 보여준다.
        // 아이콘은 <b>지금 선택된 머리</b>의 실제 아트다(2026-08-19 머리 12종 적용 이전에는
        // 리그 기본 몸통 Parts/Body를 하드코딩하고 있어 어떤 머리를 골라도 원통 얼굴이 나왔다).
        CreateIconCell(headRow, "Slot_Head", HeadSpriteLibrary.GetCurrentIcon(),
                       slotReadOnlyColor, Loc.T("modding.head"), true, null);

        // 슬롯 칸도 인벤토리와 같은 규칙으로 그린다 - 슬롯 이름(작게) + 아이콘 + 등급색.
        // 예전에는 칸 안에 이름과 설명까지 밀어 넣어 글씨가 칸 밖으로 삐져나왔다(사용자 지적).
        for (int i = 0; i < WeaponSocketCount; i++)
        {
            int socketIndex = i;

            // PartData는 struct라 &&로 단락되면 out 변수가 미할당으로 잡힌다 - 먼저 default로 둔다.
            PartData socketPart = default;
            bool equipped = moddingManager != null && moddingManager.TryGetEquippedWeaponSocketPart(i, out socketPart);
            ItemGrade grade = equipped ? socketPart.grade : ItemGrade.Normal;

            Color color = highlightWeaponSockets
                ? slotHighlightColor
                : grade.ToCellColor(slotNormalColor);

            CreateIconCell(headRow, $"Slot_WeaponSocket_{i}",
                           equipped ? PartIconLibrary.Get(socketPart) : PartIconLibrary.Get(PartSlot.ArmWeaponSocket), color,
                           Loc.T("modding.weaponsocket_n", i + 1), equipped,
                           () => HandleWeaponSocketCellClicked(socketIndex));
        }

        foreach (PartSlot slot in PartSlotExtensions.DisplayOrder)
        {
            PartSlot captured = slot;
            bool isHighlighted = hasSelection && highlightSlot == slot;

            PartData part = default;
            bool equipped = moddingManager != null && moddingManager.TryGetEquippedPart(slot, out part);
            ItemGrade grade = equipped ? part.grade : ItemGrade.Normal;

            Color color = isHighlighted
                ? slotHighlightColor
                : grade.ToCellColor(slotNormalColor);

            CreateIconCell(slotContent, $"Slot_{slot}",
                           equipped ? PartIconLibrary.Get(part) : PartIconLibrary.Get(slot), color,
                           slot.ToDisplayName(), equipped,
                           () => HandleSlotClicked(captured));
        }
    }

    private void RefreshStats()
    {
        if (statsText == null) return;

        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null)
        {
            statsText.text = $"{Loc.T("stats.header_short")}\n{Loc.T("stats.no_player")}";
            return;
        }

        // 파츠를 교체하면 RunState.OnChanged로 PlayerRobotController가 스탯을 다시 계산하므로,
        // 여기서는 그 결과만 읽으면 교체 결과가 곧바로 보인다.
        float weightSum = moddingManager != null ? moddingManager.GetEquippedWeaponWeightSum() : 0f;
        float weightCapacity = moddingManager != null ? moddingManager.GetTotalWeightCapacity() : 0f;
        int discSlots = moddingManager != null ? moddingManager.DiscSlotCount : 0;
        bool overweight = weightSum > weightCapacity;

        // 2026-08-18 UI 기획서 반영 - 정비 화면엔 레벨 표시가 아예 없었다. 메모리 파츠가 정하는
        // 최대 레벨(AiCoreManager.MaxLevel)과 함께 보여준다(기획서 "레벨: 26 (MAX:50)" 표기).
        if (aiCoreManager == null) aiCoreManager = FindFirstObjectByType<AiCoreManager>();
        string levelLine = aiCoreManager != null
            ? $"{Loc.T("modding.level", RunState.CoreLevel, aiCoreManager.MaxLevel)}\n"
            : string.Empty;

        statsText.text =
            levelLine +
            $"{Loc.T("stats.header_short")}\n" +
            // 2026-08-24 사용자 지정 표기 규칙 - 정수/소수/퍼센트 구분은 StatFormat 참고.
            $"{StatTypeNames.ToDisplayName(StatType.MaxHp)} {StatFormat.Int(player.CurrentHp)}/{StatFormat.Int(player.MaxHp)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Atk)} {StatFormat.Int(player.Atk)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Def)} {StatFormat.Int(player.Def)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritChance)} {StatFormat.Percent(player.Cc)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritDamage)} {StatFormat.RatioPercent(player.Cd)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.MoveSpeed)} {StatFormat.Decimal(player.MoveSpeed)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Avoid)} {StatFormat.Percent(player.Avoid)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Luck)} {StatFormat.Int(player.Luck)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Mass)} {StatFormat.Decimal(player.Mess)}\n" +
            $"{Loc.T("partslot.discslot")} {RunState.EquippedDiscIds.Count}/{discSlots}\n" +
            (overweight
                ? $"<color=#FF5555>{Loc.T("modding.weapon_weight_over", weightSum.ToString("0.#"), weightCapacity.ToString("0.#"))}</color>"
                : Loc.T("modding.weapon_weight", weightSum.ToString("0.#"), weightCapacity.ToString("0.#")));
    }

    private string GetRobotName()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return Loc.T("common.unknown");

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.Robot()
            : $"ID {player.RobotId}";
    }

    // ---------------------------------------------------------------------
    // 파츠 설명 칸 (인벤토리 오른쪽)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 인벤토리 오른쪽 빈 칸에 설명 패널을 만든다(2026-08-18 사용자 요청 - 팝업 대신 옆 공간).
    /// 씬을 건드리지 않으려고 코드로 만들며, 도메인 리로드로 참조가 날아가면 다시 만든다.
    /// 위치는 인벤토리(x 0~0.24)와 파츠 칸(x 0.44~) 사이라 슬롯 격자를 침범하지 않는다.
    /// </summary>
    private void EnsureDetailPanel()
    {
        if (detailPanel != null && detailSelectedText != null && detailTargetText != null) return;

        Transform root = transform.Find("Root");
        if (root == null) return;

        Transform existing = root.Find("DetailPanel");
        if (existing != null) Destroy(existing.gameObject);

        var panelGo = new GameObject("DetailPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(root, false);
        detailPanel = (RectTransform)panelGo.transform;
        detailPanel.anchorMin = new Vector2(0.26f, 0f);
        detailPanel.anchorMax = new Vector2(0.42f, 0.88f);
        detailPanel.offsetMin = Vector2.zero;
        detailPanel.offsetMax = Vector2.zero;

        Image bg = panelGo.GetComponent<Image>();
        Sprite bgSprite = Resources.Load<Sprite>("UI/Black_ui03");
        if (bgSprite != null)
        {
            bg.sprite = bgSprite;
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;
        }
        else
        {
            bg.color = new Color(0.10f, 0.11f, 0.13f, 0.95f);
        }
        bg.raycastTarget = false;

        // 2026-08-21 사용자 지적 - 위쪽 여백이 거의 없어(0.995 = 패널 맨 위 경계에 거의 붙음)
        // "설"/"명"처럼 위로 튀어나오는 한글 자모가 패널 테두리에 닿아 보였다. yMin은 그대로
        // 둬서 바로 아래 SelectedIcon(~0.935까지)과는 여전히 안 겹치게 하고, yMax만 낮춰
        // (0.995→0.98) 위쪽 여백을 만들었다. 글자 최대 크기도 28→20으로 낮춰 좁아진 세로
        // 폭 안에서도 테두리에 닿지 않을 여유를 더 뒀다.
        // 2026-08-25 - yMax를 0.98에서 0.962로 더 내렸다. Black_ui03의 실제 테두리(30px)가
        // 이 패널 높이에서 약 3.4%라 0.98은 여전히 테두리 띠 안이었다("UI 제작 규칙").
        // yMin도 함께 내려 칸 높이(약 22px)는 유지한다 - 바로 아래 SelectedIcon(~0.935)과는 안 겹친다.
        CreateDetailLabel(detailPanel, "Title", Loc.T("modding.part_detail"), 0.06f, 0.937f, 0.94f, 0.962f, TextAlignmentOptions.Center, 20f);

        detailSelectedIcon = CreateDetailIcon(detailPanel, "SelectedIcon", 0.30f, 0.775f, 0.70f, 0.935f);
        detailSelectedText = CreateDetailLabel(detailPanel, "SelectedText", string.Empty,
                                               0.07f, 0.45f, 0.93f, 0.765f, TextAlignmentOptions.Top);

        detailTargetTitle = CreateDetailLabel(detailPanel, "TargetTitle", Loc.T("modding.swap_target"),
                                              0.06f, 0.385f, 0.94f, 0.435f, TextAlignmentOptions.Center, 28f);

        detailTargetIcon = CreateDetailIcon(detailPanel, "TargetIcon", 0.30f, 0.215f, 0.70f, 0.375f);
        detailTargetText = CreateDetailLabel(detailPanel, "TargetText", string.Empty,
                                             0.07f, 0.02f, 0.93f, 0.205f, TextAlignmentOptions.Top);
    }

    /// <param name="maxFontSize">설명 칸은 폭이 좁아 칸 글씨(42)와 같은 상한을 쓰면 몇 글자만으로
    /// 여러 줄이 되어 넘친다(실측). 제목 28 / 본문 22 정도가 적당하다.</param>
    private static TextMeshProUGUI CreateDetailLabel(RectTransform parent, string name, string content,
                                                     float xMin, float yMin, float xMax, float yMax,
                                                     TextAlignmentOptions alignment, float maxFontSize = 22f)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        ApplyCellTextSizing(text, maxFontSize);
        return text;
    }

    private static Image CreateDetailIcon(RectTransform parent, string name,
                                          float xMin, float yMin, float xMax, float yMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    /// <summary>선택한 파츠와 그 파츠가 밀어낼 <b>교체 대상</b>을 나란히 보여준다.</summary>
    private void RefreshDetail()
    {
        if (detailSelectedText == null || detailTargetText == null) return;

        List<PartData> parts = moddingManager != null ? moddingManager.GetInventoryParts() : new List<PartData>();
        bool hasSelection = selectedInventoryIndex >= 0 && selectedInventoryIndex < parts.Count;

        // 인벤토리 선택이 없어도 "장착 파츠 조회 모드"면 그 파츠의 설명을 대신 띄운다(2026-08-19).
        if (!hasSelection && inspectingEquipped)
        {
            RefreshEquippedInspectionDetail();
            return;
        }

        if (!hasSelection)
        {
            detailSelectedIcon.enabled = false;
            detailSelectedText.text = $"<color=#9AA3AB>{Loc.T("modding.detail.placeholder")}</color>";
            detailTargetTitle.gameObject.SetActive(false);
            detailTargetIcon.enabled = false;
            detailTargetText.text = string.Empty;
            return;
        }

        PartData selected = parts[selectedInventoryIndex];

        detailSelectedIcon.enabled = true;
        detailSelectedIcon.sprite = PartIconLibrary.Get(selected);
        detailSelectedIcon.color = Color.white;
        detailSelectedText.text = BuildPartDetail(selected);

        detailTargetTitle.gameObject.SetActive(true);
        detailTargetIcon.enabled = true;
        detailTargetIcon.sprite = PartIconLibrary.Get(selected);

        // 무기 소켓 파츠는 소켓이 여러 개라 "교체 대상"이 하나로 정해지지 않는다 - 소켓별로 나열한다.
        if (selected.slot == PartSlot.ArmWeaponSocket)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < WeaponSocketCount; i++)
            {
                PartData socketPart = default;
                bool has = moddingManager != null && moddingManager.TryGetEquippedWeaponSocketPart(i, out socketPart);
                sb.AppendLine(has
                    ? $"{Loc.T("shop.socket_n", i + 1)}: <color={socketPart.grade.ToColorHex()}>{socketPart.Part()}</color>"
                    : $"{Loc.T("shop.socket_n", i + 1)}: <color=#7B858E>{Loc.T("common.empty")}</color>");
            }
            detailTargetIcon.color = new Color(1f, 1f, 1f, 0.55f);
            detailTargetText.text = sb.ToString().TrimEnd();
            return;
        }

        if (moddingManager != null && moddingManager.TryGetEquippedPart(selected.slot, out PartData equipped))
        {
            detailTargetIcon.color = Color.white;
            detailTargetIcon.sprite = PartIconLibrary.Get(equipped);
            detailTargetText.text = BuildPartDetail(equipped);
        }
        else
        {
            detailTargetIcon.color = new Color(1f, 1f, 1f, 0.28f);
            detailTargetText.text = $"<color=#7B858E>{Loc.T("modding.slot_is_empty", selected.slot.ToDisplayName())}</color>";
        }
    }

    /// <summary>
    /// "장착 파츠 조회 모드"(인벤토리 선택 없이 슬롯을 눌렀을 때)의 설명 칸을 채운다.
    /// 위쪽(선택 칸)에 장착 파츠의 설명을 그대로 보여주고, 아래쪽(교체 대상 칸)은
    /// 이 모드에서는 비교 대상이 없으므로 접는다.
    /// </summary>
    private void RefreshEquippedInspectionDetail()
    {
        bool isWeaponSocket = inspectedWeaponSocket >= 0;
        PartSlot slot = isWeaponSocket ? PartSlot.ArmWeaponSocket : inspectedSlot;

        PartData equipped = default;
        bool has = moddingManager != null && (isWeaponSocket
            ? moddingManager.TryGetEquippedWeaponSocketPart(inspectedWeaponSocket, out equipped)
            : moddingManager.TryGetEquippedPart(inspectedSlot, out equipped));

        // 이 모드에서는 "교체 대상" 칸을 쓰지 않는다(비교할 인벤토리 파츠가 없다).
        detailTargetTitle.gameObject.SetActive(false);
        detailTargetIcon.enabled = false;
        detailTargetText.text = string.Empty;

        string header = isWeaponSocket ? Loc.T("modding.weaponsocket_n", inspectedWeaponSocket + 1) : slot.ToDisplayName();

        if (!has)
        {
            detailSelectedIcon.enabled = false;
            detailSelectedText.text = $"<color=#9AA3AB>{Loc.T("modding.slot_is_empty", header)}</color>";
            return;
        }

        detailSelectedIcon.enabled = true;
        detailSelectedIcon.sprite = PartIconLibrary.Get(equipped);
        detailSelectedIcon.color = Color.white;
        detailSelectedText.text = $"<size=85%><color=#F2BF26>{Loc.T("modding.equipped_now", header)}</color></size>\n" + BuildPartDetail(equipped);
    }

    private static string BuildPartDetail(PartData part)
    {
        return $"<color={part.grade.ToColorHex()}>{part.grade.ToDisplayName()}</color> {part.Part()}\n" +
               $"<size=85%><color=#9AA3AB>{part.slot.ToDisplayName()}</color></size>\n" +
               $"<size=90%>{part.BuildDescription()}</size>";
    }

    // ---------------------------------------------------------------------
    // UI 생성 헬퍼 (칸 개수가 데이터에 따라 달라져 씬에 미리 배치할 수 없다)
    // ---------------------------------------------------------------------

    // 격자 설정과 칸 비우기도 상점 화면과 공유한다(ItemCellUI).
    private static void EnsureGrid(RectTransform container, Vector2 fallbackCellSize, int columns, int fitRows = 0)
        => ItemCellUI.EnsureGrid(container, fallbackCellSize, columns, fitRows);

    private void ClearChildren(RectTransform container) => ItemCellUI.ClearChildren(container);

    /// <summary>
    /// 칸 하나(배경 Image + 클릭 Button + 가운데 정렬 TMP 텍스트)를 만든다.
    /// onClick이 null이면 클릭할 수 없는 조회 전용 칸이 된다.
    /// </summary>
    private Image CreateCell(RectTransform parent, string name, string label, Color color, System.Action onClick)
    {
        Image image = ItemCellUI.CreateShell(parent, name, color, onClick, out GameObject cell);

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(cell.transform, false);

        var rect = (RectTransform)textGo.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -6f);

        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        ApplyCellTextSizing(text);

        return image;
    }

    /// <summary>
    /// 아이콘 칸 하나를 만든다. 사용자 확정(2026-08-18): 인벤토리·슬롯의 아이템은 <b>아이콘만</b>
    /// 보여주고(아이콘 뒤에 별도 사각형을 깔지 않는다 - 칸 자체가 배경이다), <b>일반 등급이 아니면
    /// 칸을 등급색으로</b> 칠한다. 이름·수치는 옆 설명 칸에서 본다.
    /// </summary>
    /// <param name="caption">칸 위에 작게 붙일 이름(슬롯 칸용). null이면 아이콘만.</param>
    /// <param name="iconBright">false면 아이콘을 흐리게 그린다(빈 슬롯 표시용).</param>
    // 칸 생김새는 상점 화면과 공유한다(ItemCellUI). 여기서는 얇게 감싸기만 한다.
    private Image CreateIconCell(RectTransform parent, string name, Sprite icon, Color color,
                                 string caption, bool iconBright, System.Action onClick)
        => ItemCellUI.CreateIconCell(parent, name, icon, color, caption, iconBright, onClick);

    private static void ApplyCellTextSizing(TextMeshProUGUI text, float maxFontSize = 42f)
        => ItemCellUI.ApplyTextSizing(text, maxFontSize);

    private static int RoundUpToMultiple(int value, int multiple)
    {
        if (multiple <= 0) return value;
        int remainder = value % multiple;
        return remainder == 0 ? value : value + (multiple - remainder);
    }
}
