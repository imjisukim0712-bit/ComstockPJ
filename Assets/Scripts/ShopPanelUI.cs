using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 화면 표시/입력 담당(로직은 ShopManager). 기획서 p.13의 8개 요소를 모두 채운다.
///  1. 현재 웨이브        2. 골드            3. "정비 종료" 버튼(= 다음 웨이브 시작)
///  4. 로봇 모딩 상태     5. 장착된 무기     6. 장착된 디스크
///  7. 현재 능력치        8. 상점 품목 4칸 + 상점 초기화(비용) + 개별 잠금
///
/// 헤더 문구는 2026-08-18 `UI 기획서.pdf` Phase A에서 로봇 정비 화면과 통일했다
/// (제목 / `WAVE 07 / 20` / 코인 아이콘 + 숫자).
///
/// 4번(로봇 모딩 상태)은 팔/다리 파츠 시스템이 아직 없어서(Phase 4) 머리 파츠가 정하는 값만
/// 실제로 보여주고 나머지 부위는 "기본"으로 표기한다. 필살기도 데모 범위 밖이라 슬롯만 노출한다.
///
/// 무기를 사면 어느 소켓을 교체할지 골라야 하므로, 품목 카드를 누르면 소켓 선택 줄이 열리고
/// 소켓 버튼을 눌러야 실제 구매가 끝난다. 디스크는 슬롯에 자리만 있으면 바로 구매된다.
/// </summary>
public class ShopPanelUI : MonoBehaviour
{
    /// <summary>상점 품목 칸 하나를 구성하는 UI 묶음(4칸이 같은 구조를 갖는다).</summary>
    [System.Serializable]
    public struct OfferSlotUI
    {
        [Tooltip("품목 카드 전체 버튼(누르면 구매 시도)")]
        public Button cardButton;

        [Tooltip("무기 손 이미지 또는 디스크 아이콘을 보여줄 이미지. 못 찾으면 자동으로 숨긴다")]
        public Image iconImage;

        [Tooltip("'전설 · 무기' 처럼 등급과 카테고리를 보여줄 텍스트")]
        public TextMeshProUGUI headerText;

        [Tooltip("품목 이름 + 성능 요약 + 가격을 보여줄 텍스트")]
        public TextMeshProUGUI bodyText;

        [Tooltip("잠금 토글 버튼")]
        public Button lockButton;

        [Tooltip("잠금 버튼에 '잠금'/'잠금 해제'를 표시할 텍스트")]
        public TextMeshProUGUI lockText;
    }

    [Header("연결")]
    [SerializeField] private ShopManager shopManager;

    [Header("1~3. 상단 정보")]
    [SerializeField] private TextMeshProUGUI waveText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private Button nextWaveButton;

    [Header("4. 로봇 모딩 상태 (조회 전용)")]
    [SerializeField] private TextMeshProUGUI moddingStatusText;

    [Header("5~6. 장착 현황")]
    [SerializeField] private TextMeshProUGUI equippedWeaponsText;
    [SerializeField] private TextMeshProUGUI equippedDiscsText;

    [Header("7. 현재 능력치")]
    [SerializeField] private TextMeshProUGUI statsText;

    [Header("8. 상점 품목 + 새로고침")]
    [SerializeField] private OfferSlotUI[] offerSlots = new OfferSlotUI[4];
    [SerializeField] private Button refreshButton;
    [SerializeField] private TextMeshProUGUI refreshText;

    [Header("무기 소켓 선택 (무기 구매 시 열림)")]
    [SerializeField] private GameObject socketPickerRoot;
    [SerializeField] private TextMeshProUGUI socketPickerTitleText;
    [Tooltip("2026-08-18 기본 무기 소켓 4개로 확장 - 배열 크기가 곧 소켓 선택 UI에 노출되는 버튼 수다. " +
             "실제로 몇 개까지 쓰이는지는 PlayerShootManager.SocketCount(리깅된 소켓 수와 로봇 " +
             "weaponSocketCount 중 작은 값)가 정하며, 그보다 뒤 인덱스의 버튼은 자동으로 숨겨진다")]
    [SerializeField] private Button[] socketButtons = new Button[4];
    [SerializeField] private TextMeshProUGUI[] socketButtonTexts = new TextMeshProUGUI[4];
    [SerializeField] private Button socketPickerCancelButton;

    [Header("안내 메시지")]
    [Tooltip("골드 부족 등 구매 실패 사유를 잠깐 보여줄 텍스트")]
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("상점이 열려 있는 동안 숨길 전투 HUD")]
    [Tooltip("HP 표시처럼 전투 중에만 필요한 UI. 상점 화면과 겹쳐 보이지 않도록 여는 동안 숨긴다")]
    [SerializeField] private GameObject[] hideWhileOpen = new GameObject[0];

    private PlayerRobotController player;

    // 소켓 선택 창을 띄운 원인이 된 품목 칸 번호. -1이면 선택 중이 아니다.
    private int pendingWeaponOfferIndex = -1;

    /// <summary>다음 웨이브 시작 버튼이 눌렸을 때 GameFlowManager가 받아갈 이벤트.</summary>
    public event System.Action OnNextWaveRequested;

    private void OnEnable() => RunState.OnChanged += Refresh;
    private void OnDisable() => RunState.OnChanged -= Refresh;

    // 이 패널은 씬에서 비활성 상태로 시작하므로 Awake는 "처음 열릴 때" 딱 한 번 실행된다.
    // 따라서 여기서 gameObject.SetActive(false)를 부르면 Open()으로 켜자마자 다시 꺼져버린다.
    // 버튼 리스너도 Start가 아니라 여기서 등록해야 열자마자 클릭이 동작한다.
    private void Awake()
    {
        if (socketPickerRoot != null) socketPickerRoot.SetActive(false);

        if (nextWaveButton != null) nextWaveButton.onClick.AddListener(HandleNextWaveClicked);
        if (refreshButton != null) refreshButton.onClick.AddListener(HandleRefreshClicked);
        if (socketPickerCancelButton != null) socketPickerCancelButton.onClick.AddListener(CloseSocketPicker);

        for (int i = 0; i < offerSlots.Length; i++)
        {
            int index = i; // 클로저가 반복 변수를 그대로 잡지 않도록 복사
            if (offerSlots[i].cardButton != null) offerSlots[i].cardButton.onClick.AddListener(() => HandleOfferClicked(index));
            if (offerSlots[i].lockButton != null) offerSlots[i].lockButton.onClick.AddListener(() => HandleLockClicked(index));
        }

        for (int i = 0; i < socketButtons.Length; i++)
        {
            int index = i;
            if (socketButtons[i] != null) socketButtons[i].onClick.AddListener(() => HandleSocketChosen(index));
        }

        SetupDetailInspector();

        // 음악 볼륨 설정(2026-08-13). 상점은 웨이브마다 반드시 거치는 화면이라 런 도중 볼륨을
        // 조절할 수 있는 유일한 지점이다(타이틀 화면에도 같은 컨트롤이 있다).
        // 위치는 상단의 비어 있는 구간(골드 표시와 '다음 웨이브 시작' 버튼 사이).
        MusicVolumeSliderUI.Attach((RectTransform)transform, new Vector2(0.53f, 0.90f), new Vector2(0.70f, 0.97f));
    }

    // ── 보유 장비 상세 보기 ──────────────────────────────────────────
    // 장착 무기 / 모딩 상태 / 디스크 목록의 각 줄을 클릭하면 상세 능력치 팝업이 열린다.
    // 목록은 원래부터 "여러 줄이 든 TMP 텍스트 1개"라 줄마다 버튼을 두려면 씬 작업이 필요한데,
    // 항목 수가 런타임에 변해서(무기 소켓 개수·디스크 슬롯 수) 링크 태그 방식을 택했다.
    private EquipmentDetailPopup detail_popup;

    private void SetupDetailInspector()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) detail_popup = EquipmentDetailPopup.Create(canvas.transform);

        TextLinkClickRelay.Attach(equippedWeaponsText, ShowDetail);
        TextLinkClickRelay.Attach(equippedDiscsText, ShowDetail);
        TextLinkClickRelay.Attach(moddingStatusText, ShowDetail);
    }

    /// <summary>웨이브가 끝나 상점을 열 때 GameFlowManager가 호출한다.</summary>
    public void Open()
    {
        if (shopManager != null) shopManager.OpenForNewWave();

        pendingWeaponOfferIndex = -1;
        if (socketPickerRoot != null) socketPickerRoot.SetActive(false);
        SetMessage(string.Empty);

        SetCombatHudVisible(false);

        gameObject.SetActive(true);
        if (detail_popup != null) detail_popup.Hide(); // 상세 팝업은 캔버스 직속이라 패널과 같이 꺼지지 않는다
        Refresh();
    }

    public void Close()
    {
        SetCombatHudVisible(true);
        if (detail_popup != null) detail_popup.Hide();
        gameObject.SetActive(false);
    }

    private void SetCombatHudVisible(bool visible)
    {
        foreach (GameObject hud in hideWhileOpen)
        {
            if (hud != null) hud.SetActive(visible);
        }
    }

    private void HandleNextWaveClicked()
    {
        Close();
        OnNextWaveRequested?.Invoke();
    }

    private void HandleRefreshClicked()
    {
        if (shopManager == null) return;

        if (!shopManager.TryRefresh())
        {
            SetMessage($"골드가 부족합니다 (상점 초기화 {shopManager.CurrentRefreshCost}골드)");
            return;
        }

        CloseSocketPicker();
        SetMessage(string.Empty);
        Refresh();
    }

    private void HandleLockClicked(int index)
    {
        if (shopManager == null) return;

        shopManager.ToggleLock(index);
        Refresh();
    }

    private void HandleOfferClicked(int index)
    {
        if (shopManager == null) return;
        if (index < 0 || index >= shopManager.Offers.Count) return;

        ShopManager.Offer offer = shopManager.Offers[index];
        if (offer == null) return;

        if (!shopManager.CanPurchase(index, out string reason))
        {
            SetMessage(reason);
            return;
        }

        if (offer.IsDisc)
        {
            if (shopManager.TryPurchaseDisc(index)) SetMessage($"{offer.DisplayName} 구매 완료");
            Refresh();
            return;
        }

        OpenSocketPicker(index);
    }

    // 무기는 어느 소켓을 교체할지 골라야 해서 소켓 선택 줄을 연다.
    private void OpenSocketPicker(int offerIndex)
    {
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager == null)
        {
            SetMessage("무기를 장착할 대상을 찾을 수 없습니다");
            return;
        }

        pendingWeaponOfferIndex = offerIndex;

        ShopManager.Offer offer = shopManager.Offers[offerIndex];
        if (socketPickerTitleText != null)
        {
            socketPickerTitleText.text = $"'{offer.DisplayName}'({offer.Grade.ToKorean()})을(를) 어느 소켓에 장착할까요?";
        }

        for (int i = 0; i < socketButtons.Length; i++)
        {
            bool exists = i < shootManager.SocketCount;

            if (socketButtons[i] != null) socketButtons[i].gameObject.SetActive(exists);
            if (!exists || socketButtonTexts[i] == null) continue;

            string current = shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade currentGrade)
                ? $"{weapon.weapon_name} ({currentGrade.ToKorean()})"
                : "(비어 있음)";

            // 2026-08-12 "무기 소켓 개별화" 플랜부터 타입 불일치/무게 초과는 더 이상 버튼을
            // 막지 않는다(언제나 장착 가능) - 대신 비차단 경고 문구로 어떤 패널티가 붙는지 보여준다.
            bool allowed = shopManager.CanPurchaseWeaponIntoSocket(offerIndex, i, out string reason);
            if (socketButtons[i] != null) socketButtons[i].interactable = allowed;

            if (!allowed)
            {
                socketButtonTexts[i].text = $"소켓 {i + 1}\n{current}\n<color=#FF8080>{reason}</color>";
                continue;
            }

            string warning = shopManager.BuildSocketWarning(offerIndex, i);
            socketButtonTexts[i].text = warning.Length > 0
                ? $"소켓 {i + 1}\n{current}\n<color=#F2BF26>{warning}</color>"
                : $"소켓 {i + 1}\n{current}";
        }

        if (socketPickerRoot != null) socketPickerRoot.SetActive(true);
    }

    private void CloseSocketPicker()
    {
        pendingWeaponOfferIndex = -1;
        if (socketPickerRoot != null) socketPickerRoot.SetActive(false);
    }

    private void HandleSocketChosen(int socketIndex)
    {
        if (shopManager == null || pendingWeaponOfferIndex < 0) return;

        int offerIndex = pendingWeaponOfferIndex;
        ShopManager.Offer offer = shopManager.Offers[offerIndex];

        if (shopManager.TryPurchaseWeaponIntoSocket(offerIndex, socketIndex))
        {
            SetMessage($"소켓 {socketIndex + 1}에 {offer.DisplayName}({offer.Grade.ToKorean()}) 장착 완료");
        }
        else
        {
            shopManager.CanPurchaseWeaponIntoSocket(offerIndex, socketIndex, out string reason);
            SetMessage(string.IsNullOrEmpty(reason) ? "구매에 실패했습니다" : reason);
        }

        CloseSocketPicker();
        Refresh();
    }

    private void SetMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    /// <summary>화면 전체를 현재 상태로 다시 그린다.</summary>
    public void Refresh()
    {
        if (!gameObject.activeInHierarchy) return;

        RefreshHeader();
        RefreshOffers();
        RefreshEquipment();
        RefreshStats();
    }

    private void RefreshHeader()
    {
        // 헤더 표기는 로봇 정비 화면(ModdingPanelUI.RefreshHeader)과 같은 형식으로 맞춘다.
        // 골드는 옆에 코인 아이콘 오브젝트가 따로 있으므로 숫자만 출력한다.
        if (waveText != null)
        {
            WaveManager waveManager = FindFirstObjectByType<WaveManager>();
            int finalWave = waveManager != null ? waveManager.FinalWaveNumber : 0;
            waveText.text = finalWave > 0
                ? $"WAVE {RunState.WaveNumber:00} / {finalWave}"
                : $"WAVE {RunState.WaveNumber:00}";
        }

        if (goldText != null) goldText.text = RunState.Gold.ToString();

        if (refreshText != null && shopManager != null)
        {
            refreshText.text = $"상점 초기화 - {shopManager.CurrentRefreshCost}";
        }
    }

    private void RefreshOffers()
    {
        if (shopManager == null) return;

        for (int i = 0; i < offerSlots.Length; i++)
        {
            OfferSlotUI ui = offerSlots[i];
            bool hasOffer = i < shopManager.Offers.Count && shopManager.Offers[i] != null;

            if (ui.cardButton != null) ui.cardButton.gameObject.SetActive(hasOffer);
            if (ui.lockButton != null) ui.lockButton.gameObject.SetActive(hasOffer);

            if (!hasOffer) continue;

            ShopManager.Offer offer = shopManager.Offers[i];

            if (ui.iconImage != null)
            {
                Sprite icon = ResolveOfferIcon(offer);
                ui.iconImage.sprite = icon;
                ui.iconImage.enabled = icon != null;
            }

            // 기획서 표기 형식: "전설 · 무기"
            if (ui.headerText != null)
            {
                ui.headerText.text = $"<color={offer.Grade.ToColorHex()}>{offer.Grade.ToKorean()}</color> · {offer.CategoryName}";
            }

            if (ui.bodyText != null)
            {
                ui.bodyText.text = offer.Purchased
                    ? $"{offer.DisplayName}\n{offer.BuildDescription()}\n<color=#88FF88>구매 완료</color>"
                    : $"{offer.DisplayName}\n{offer.BuildDescription()}\n{offer.Price}골드";
            }

            if (ui.cardButton != null) ui.cardButton.interactable = !offer.Purchased;
            if (ui.lockButton != null) ui.lockButton.interactable = !offer.Purchased;
            if (ui.lockText != null) ui.lockText.text = offer.Locked ? "잠금 해제" : "잠금";
        }
    }

    // 이미지 이름 → 스프라이트 캐시(Resources.Load 반복 호출 방지). PlayerShootManager의
    // sprite_by_name과 같은 패턴.
    private readonly Dictionary<string, Sprite> offer_icon_cache = new Dictionary<string, Sprite>();

    /// <summary>
    /// 품목 카드에 보여줄 아이콘을 찾는다. 디스크는 DiscData.LoadIcon()(Discs/디스크01~21로
    /// 매핑된 전용 아이콘)을, 무기는 전용 아이콘이 없어 손에 드는 이미지(weapon_rgwpimg)를
    /// 그대로 재사용한다 - PlayerShootManager가 무기를 장착할 때 쓰는 것과 같은 스프라이트라
    /// 별도 아트 없이도 무기를 알아볼 수 있다.
    /// </summary>
    private Sprite ResolveOfferIcon(ShopManager.Offer offer)
    {
        return offer.IsDisc ? offer.Disc.LoadIcon() : ResolveWeaponIcon(offer.Weapon);
    }

    /// <summary>무기 아이콘(= 손에 드는 오른손 이미지)을 캐시를 거쳐 찾는다. 상세 팝업도 같이 쓴다.</summary>
    private Sprite ResolveWeaponIcon(WeaponData weapon)
    {
        string spriteName = weapon.weapon_rgwpimg;
        if (string.IsNullOrWhiteSpace(spriteName)) return null;

        if (!offer_icon_cache.TryGetValue(spriteName, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>(spriteName);
            offer_icon_cache[spriteName] = sprite; // 못 찾아도(null) 캐시해서 매번 재시도하지 않는다
        }

        return sprite;
    }

    // 보유·장착 목록을 그리는 아이콘 격자. 씬을 건드리지 않고 코드로 만들며, 에디터 도메인
    // 리로드로 참조가 날아가면 다시 만든다(2026-08-18 AiCoreExtraButtonsUI에서 겪은 함정).
    private RectTransform partsGrid;
    private RectTransform weaponsGrid;
    private RectTransform discsGrid;

    /// <summary>정비 화면의 기본 칸 색과 같은 값(일반 등급일 때 쓰는 바탕색).</summary>
    private static readonly Color CellPlainColor = new Color(0.20f, 0.22f, 0.25f, 1f);

    /// <summary>
    /// 세 목록(장착 파츠 / 장착 무기 / 디스크)을 아이콘 격자로 바꾼다(2026-08-18 사용자 요청:
    /// "상점에서도 보유, 장착 파츠와 아이템은 인벤토리와 같이 전부 아이콘으로 표시").
    /// 기존 TMP 텍스트는 <b>제목 줄</b>로만 남기고, 그 아래를 격자가 차지한다.
    /// </summary>
    private void EnsureEquipGrids()
    {
        if (partsGrid != null && weaponsGrid != null && discsGrid != null) return;

        var panel = (RectTransform)transform;

        partsGrid = EnsureGridContainer(panel, "PartsGrid", moddingStatusText,
                                        0.02f, 0.44f, 0.30f, 0.88f, 0.825f);
        weaponsGrid = EnsureGridContainer(panel, "WeaponsGrid", equippedWeaponsText,
                                          0.32f, 0.68f, 0.60f, 0.88f, 0.825f);
        discsGrid = EnsureGridContainer(panel, "DiscsGrid", equippedDiscsText,
                                        0.32f, 0.44f, 0.60f, 0.66f, 0.605f);
    }

    /// <param name="titleBottom">제목 줄의 아래쪽 경계(정규화). 격자는 이 아래를 쓴다.</param>
    private static RectTransform EnsureGridContainer(RectTransform panel, string name, TextMeshProUGUI title,
                                                     float xMin, float yMin, float xMax, float yMax,
                                                     float titleBottom)
    {
        if (title != null)
        {
            RectTransform t = title.rectTransform;
            t.anchorMin = new Vector2(xMin, titleBottom);
            t.anchorMax = new Vector2(xMax, yMax);
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;
            title.alignment = TextAlignmentOptions.Left;
            ItemCellUI.ApplyTextSizing(title, 30f);
        }

        Transform existing = panel.Find(name);
        if (existing != null) Destroy(existing.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, titleBottom - 0.005f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private void RefreshEquipment()
    {
        EnsureEquipGrids();

        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        int socketCount = shootManager != null ? shootManager.SocketCount : 0;

        RefreshPartsGrid(modding, socketCount);
        RefreshWeaponsGrid(shootManager, socketCount);
        RefreshDiscsGrid();
    }

    // 4. 로봇 모딩 상태 - 머리 + 무기 소켓 파츠 N개 + 나머지 파츠 슬롯을 아이콘 칸으로.
    private void RefreshPartsGrid(ModdingManager modding, int socketCount)
    {
        if (partsGrid == null) return;

        int cellCount = 1 + socketCount + PartSlotExtensions.DisplayOrder.Length;
        int columns = 3;
        int rows = Mathf.CeilToInt(cellCount / (float)columns);

        ItemCellUI.EnsureGrid(partsGrid, new Vector2(120f, 92f), columns, rows);
        ItemCellUI.ClearChildren(partsGrid);

        if (moddingStatusText != null)
        {
            float weight = modding != null ? modding.GetTotalWeight() : 0f;
            float capacity = modding != null ? modding.GetTotalWeightCapacity() : 0f;
            string weightLine = weight > capacity
                ? $"<color=#FF5555>{weight:0.#}/{capacity:0.#}</color>"
                : $"{weight:0.#}/{capacity:0.#}";
            moddingStatusText.text = $"[장착 파츠] <size=75%>AI 코어 Lv {RunState.CoreLevel} · 무게 {weightLine} {DetailHint}</size>";
        }

        ItemCellUI.CreateIconCell(partsGrid, "Cell_Head", Resources.Load<Sprite>("Parts/Body"),
                                  new Color(0.16f, 0.17f, 0.19f, 1f), "머리", true, () => ShowDetail("head"));

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            PartData socketPart = default;
            bool has = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out socketPart);
            ItemGrade grade = has ? socketPart.grade : ItemGrade.Normal;

            ItemCellUI.CreateIconCell(partsGrid, $"Cell_SocketPart_{i}",
                                      PartIconLibrary.Get(PartSlot.ArmWeaponSocket),
                                      grade.ToCellColor(CellPlainColor), $"소켓 {i + 1}", has,
                                      () => ShowDetail($"ws:{index}"));
        }

        foreach (PartSlot slot in PartSlotExtensions.DisplayOrder)
        {
            PartSlot captured = slot;
            PartData part = default;
            bool has = modding != null && modding.TryGetEquippedPart(slot, out part);
            ItemGrade grade = has ? part.grade : ItemGrade.Normal;

            ItemCellUI.CreateIconCell(partsGrid, $"Cell_Part_{slot}", PartIconLibrary.Get(slot),
                                      grade.ToCellColor(CellPlainColor), slot.ToKorean(), has,
                                      () => ShowDetail($"p:{captured}"));
        }
    }

    // 5. 장착된 무기 - 소켓마다 손에 드는 무기 이미지를 아이콘으로 쓴다(무기 전용 아이콘은 없다).
    private void RefreshWeaponsGrid(PlayerShootManager shootManager, int socketCount)
    {
        if (weaponsGrid == null) return;

        int columns = 3;
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, socketCount) / (float)columns));

        ItemCellUI.EnsureGrid(weaponsGrid, new Vector2(120f, 92f), columns, rows);
        ItemCellUI.ClearChildren(weaponsGrid);

        if (equippedWeaponsText != null)
            equippedWeaponsText.text = $"[장착 무기] {DetailHint}";

        if (shootManager == null) return;

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            bool has = shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade);

            // 무기를 안 낀 소켓은 완전히 빈 칸으로 두면 "칸이 왜 있지?" 싶으므로,
            // 무기 소켓 아이콘을 흐리게 깔아 "여기에 무기를 낄 수 있다"는 것을 보여준다.
            Sprite icon = has ? ResolveWeaponIcon(weapon) : PartIconLibrary.Get(PartSlot.ArmWeaponSocket);

            ItemCellUI.CreateIconCell(weaponsGrid, $"Cell_Weapon_{i}", icon,
                                      has ? grade.ToCellColor(CellPlainColor) : CellPlainColor,
                                      $"소켓 {i + 1}", has,
                                      has ? (System.Action)(() => ShowDetail($"w:{index}")) : null);
        }
    }

    // 6. 장착된 디스크 - 슬롯 수만큼 칸을 만들고 낀 디스크만 아이콘을 채운다.
    private void RefreshDiscsGrid()
    {
        if (discsGrid == null) return;

        int slotCount = shopManager != null ? shopManager.DiscSlotCount : 0;
        int cellCount = Mathf.Max(slotCount, RunState.EquippedDiscIds.Count);
        int columns = 4;
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, cellCount) / (float)columns));

        ItemCellUI.EnsureGrid(discsGrid, new Vector2(120f, 92f), columns, rows);
        ItemCellUI.ClearChildren(discsGrid);

        if (equippedDiscsText != null)
            equippedDiscsText.text = $"[디스크] {RunState.EquippedDiscIds.Count}/{slotCount} {DetailHint}";

        for (int i = 0; i < cellCount; i++)
        {
            // DiscData도 struct라 null을 못 쓴다(PartData와 같은 이유).
            DiscData disc = default;
            bool has = i < RunState.EquippedDiscIds.Count && TryFindDisc(RunState.EquippedDiscIds[i], out disc);

            if (has)
            {
                int discId = disc.discId;
                ItemCellUI.CreateIconCell(discsGrid, $"Cell_Disc_{i}", disc.LoadIcon(),
                                          disc.grade.ToCellColor(CellPlainColor), null, true,
                                          () => ShowDetail($"d:{discId}"));
            }
            else
            {
                ItemCellUI.CreateIconCell(discsGrid, $"Cell_DiscEmpty_{i}", null, CellPlainColor * 0.6f, null, false, null);
            }
        }
    }

    private bool TryFindDisc(int discId, out DiscData found)
    {
        found = default;
        if (shopManager == null || shopManager.Catalog == null) return false;

        foreach (DiscData disc in shopManager.Catalog.Discs)
        {
            if (disc.discId != discId) continue;
            found = disc;
            return true;
        }
        return false;
    }

    // 파츠 하나를 "등급 이름 (설명)" 형태로 요약한다. 장착된 파츠가 있으면 클릭해서
    // 상세 능력치를 볼 수 있도록 링크로 감싼다.
    private static string PartLine(ModdingManager modding, PartSlot slot)
    {
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return "(없음)";

        return Clickable($"p:{slot}",
            $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName}");
    }

    /// <summary>
    /// "무기 소켓: N칸" 한 줄로는 소켓별로 다른 파츠를 표현할 수 없어서(2026-08-12 "무기 소켓
    /// 개별화" 플랜), 소켓마다 한 줄씩("무기 소켓 1: ...") 여러 줄로 펼친다. "장착 무기" 섹션의
    /// for i < SocketCount 순회 스타일을 그대로 따른다.
    /// </summary>
    private static string BuildWeaponSocketPartsBlock(ModdingManager modding, int socketCount)
    {
        var lines = new List<string>();

        for (int i = 0; i < socketCount; i++)
        {
            string part = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out PartData socketPart)
                ? Clickable($"ws:{i}",
                    $"<color={socketPart.grade.ToColorHex()}>{socketPart.grade.ToKorean()}</color> {socketPart.partName}")
                : "(없음)";

            lines.Add($"무기 소켓 {i + 1}: {part}");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "무기 소켓: (없음)";
    }

    private string GetRobotName()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return "(알 수 없음)";

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.robot_name
            : $"ID {player.RobotId}";
    }

    // 7. 현재 능력치 (기획서 p.13의 10개 스탯)
    private void RefreshStats()
    {
        if (statsText == null) return;

        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null)
        {
            statsText.text = "[현재 능력치]\n(플레이어를 찾을 수 없음)";
            return;
        }

        statsText.text =
            "[현재 능력치]\n" +
            $"체력 {player.CurrentHp}/{player.MaxHp}\n" +
            $"공격력 {player.Atk}\n" +
            $"방어력 {player.Def}\n" +
            $"치명타 확률 {player.Cc:0.##}%\n" +
            $"치명타 피해 {player.Cd:0.##}\n" +
            $"이동속도 {player.MoveSpeed:0.##}\n" +
            $"회피율 {player.Avoid:0.##}\n" +
            $"행운 {player.Luck:0.##}\n" +
            $"질량 {player.Mess:0.##}";
    }

    // ─────────────────────────────────────────────────────────────────
    // 보유 장비 상세 보기 - 링크 태그 생성과 클릭 처리
    // ─────────────────────────────────────────────────────────────────

    /// <summary>목록 제목 옆에 붙이는 "클릭하면 상세를 볼 수 있다"는 안내.</summary>
    private const string DetailHint = "<size=70%><color=#8FB8FF>(클릭 = 상세)</color></size>";

    /// <summary>클릭 가능한 항목으로 감싼다. 밑줄은 "여기 누를 수 있다"는 시각 신호다.</summary>
    private static string Clickable(string linkId, string label) => $"<link=\"{linkId}\"><u>{label}</u></link>";

    /// <summary>
    /// 링크 id를 해석해 알맞은 상세 팝업을 연다. id 형식:
    /// <c>w:소켓번호</c>(장착 무기) / <c>ws:소켓번호</c>(무기 소켓 파츠) /
    /// <c>p:슬롯이름</c>(그 외 파츠) / <c>d:디스크id</c> / <c>head</c>(로봇 본체).
    /// </summary>
    private void ShowDetail(string linkId)
    {
        if (detail_popup == null || string.IsNullOrEmpty(linkId)) return;

        string[] token = linkId.Split(':');
        string kind = token[0];
        string arg = token.Length > 1 ? token[1] : string.Empty;

        switch (kind)
        {
            case "w":
                if (int.TryParse(arg, out int weaponSocket)) ShowWeaponDetail(weaponSocket);
                break;

            case "ws":
                if (int.TryParse(arg, out int partSocket)) ShowWeaponSocketPartDetail(partSocket);
                break;

            case "p":
                if (System.Enum.TryParse(arg, out PartSlot slot)) ShowPartDetail(slot);
                break;

            case "d":
                if (int.TryParse(arg, out int discId)) ShowDiscDetail(discId);
                break;

            case "head":
                ShowRobotDetail();
                break;
        }
    }

    private void ShowWeaponDetail(int socketIndex)
    {
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager == null) return;
        if (!shootManager.TryGetSocketInfo(socketIndex, out WeaponData weapon, out ItemGrade grade)) return;

        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();

        var lines = new List<string>();

        // 무기 타입/투사체 타입은 무기 데이터가 아니라 PartsCatalog의 메타 표에 있다.
        if (modding != null && modding.Catalog != null &&
            modding.Catalog.TryGetWeaponMeta(weapon.weapon_id, out PartsCatalog.WeaponMetaEntry meta))
        {
            lines.Add($"분류: {meta.weaponClass.ToKorean()} / {meta.type.ToKorean()}");
        }

        // 실제로 적에게 들어가는 1발 데미지 = weapon_atk + (로봇 공격력 / 발수). 치명타는 별도.
        float robotAtk = player != null ? player.Atk : 0f;
        float perShot = weapon.weapon_atk + robotAtk / weapon.ProjectileCount;
        float dps = perShot * weapon.ProjectileCount * weapon.weapon_atsp;

        lines.Add($"공격력 {weapon.weapon_atk:0.##} (+로봇 {robotAtk:0.##} 분배 → 1발 {perShot:0.##})");
        lines.Add($"공격속도 {weapon.weapon_atsp:0.##}회/초 · 초당 피해 약 {dps:0.#}");

        // 사거리/감지거리는 값 하나만 보여준다(2026-08-12 사용자 요청 - "데이터값 → 실제값"
        // 두 개를 나란히 쓰던 것을 없앴다). 남긴 값은 소켓 파츠 배율까지 먹은 최종 적용값이라
        // 화면에 적힌 숫자가 곧 게임에서 나가는 거리다.
        lines.Add($"사거리 {shootManager.GetEffectiveTravelRange(socketIndex):0.##}");
        lines.Add($"감지거리 {shootManager.GetEffectiveDetectRange(socketIndex):0.##}");

        lines.Add($"발사 방식: {FireModeName(weapon.weapon_firemode)}");
        if (weapon.ProjectileCount > 1) lines.Add($"동시 발사 {weapon.ProjectileCount}발 (탄퍼짐 {weapon.weapon_aim:0.##}도)");
        if (weapon.weapon_speed > 0f) lines.Add($"탄속 {weapon.ProjectileSpeed:0.##}");
        if (weapon.weapon_duration > 0f) lines.Add($"지속시간 {weapon.weapon_duration:0.##}초");
        if (weapon.weapon_splash > 0f) lines.Add($"폭발 반경 {weapon.weapon_splash:0.##}");
        if (weapon.weapon_pierce != 0)
        {
            string pierce = weapon.weapon_pierce < 0 ? "무제한" : $"{weapon.weapon_pierce}회";
            if (weapon.weapon_pierce_chance > 0f && weapon.weapon_pierce_chance < 1f)
            {
                pierce += $" (확률 {weapon.weapon_pierce_chance * 100f:0}%)";
            }
            lines.Add($"관통 {pierce}");
        }
        if (weapon.weapon_defignore > 0f) lines.Add($"방어력 무시 {weapon.weapon_defignore * 100f:0}%");
        if (weapon.weapon_knockback > 0f) lines.Add($"넉백 {weapon.weapon_knockback:0.##}");
        lines.Add($"조준 회전속도 {weapon.RotationSpeed:0.#}도/초");

        // 무게는 소켓 타입이 안 맞으면 배율이 붙는다(장착은 되지만 이동속도가 깎인다).
        if (modding != null)
        {
            float baseWeight = modding.GetWeaponWeight(weapon.weapon_id);
            float effective = modding.GetEffectiveWeaponWeight(socketIndex, weapon.weapon_id);
            lines.Add(Mathf.Approximately(baseWeight, effective)
                ? $"무게 {baseWeight:0.##}"
                : $"무게 {baseWeight:0.##} → <color=#F2BF26>{effective:0.##} (소켓 타입 불일치 x{modding.MismatchWeightMultiplier:0.##})</color>");
        }

        detail_popup.Show(
            $"소켓 {socketIndex + 1} · <color={grade.ToColorHex()}>{grade.ToKorean()}</color> {weapon.weapon_name}",
            string.Join("\n", lines),
            ResolveWeaponIcon(weapon));
    }

    // WeaponFireMode에는 한글 이름 확장이 없어서(다른 enum들과 달리 UI에 쓰인 적이 없다) 여기서 변환한다.
    private static string FireModeName(WeaponFireMode mode)
    {
        switch (mode)
        {
            case WeaponFireMode.Projectile: return "투사체";
            case WeaponFireMode.Beam: return "지속 빔";
            case WeaponFireMode.MeleeSwing: return "근접 휘두르기";
            default: return mode.ToString();
        }
    }

    private void ShowWeaponSocketPartDetail(int socketIndex)
    {
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        if (modding == null || !modding.TryGetEquippedWeaponSocketPart(socketIndex, out PartData part)) return;

        var lines = new List<string>
        {
            $"부위: 무기 소켓 {socketIndex + 1}",
            $"장착 가능 무기: {(part.restrictsWeaponClass ? part.allowedWeaponClass.ToKorean() : "전체")}",
            $"사거리 x{part.RangeMultiplier:0.##}",
            $"감지거리 x{part.DetectRangeMultiplier:0.##}",
            $"조준 회전속도 x{part.RotationSpeedMultiplier:0.##}"
        };

        if (part.weight != 0f) lines.Add($"무게 {part.weight:0.##}");

        // 이 소켓에 실제로 낀 무기와 타입이 맞는지까지 같이 보여준다(불일치면 무게 배율이 붙는다).
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager != null && shootManager.TryGetSocketInfo(socketIndex, out WeaponData weapon, out _))
        {
            lines.Add(modding.IsWeaponMismatched(socketIndex, weapon.weapon_id)
                ? $"<color=#F2BF26>현재 장착 '{weapon.weapon_name}' - 타입 불일치 (무게 x{modding.MismatchWeightMultiplier:0.##})</color>"
                : $"현재 장착 '{weapon.weapon_name}' - 타입 일치");
        }

        detail_popup.Show(
            $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName}",
            string.Join("\n", lines),
            null);
    }

    private void ShowPartDetail(PartSlot slot)
    {
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return;

        var lines = new List<string> { $"부위: {slot.ToKorean()}" };

        if (part.bonusAmount != 0f) lines.Add($"{StatTypeNames.ToKorean(part.bonusStat)} +{part.bonusAmount:0.##}");
        if (part.weightCapacity != 0f) lines.Add($"무게 지탱 +{part.weightCapacity:0.##}");
        if (part.weight != 0f) lines.Add($"무게 {part.weight:0.##}");
        if (slot == PartSlot.DiscSlot) lines.Add($"디스크 슬롯 {part.discSlotCount}칸");
        if (lines.Count == 1) lines.Add("(보너스 없음)");

        // 무게는 개별 파츠만 봐서는 감이 안 오므로 로봇 전체 합계를 함께 보여준다.
        float total = modding.GetTotalWeight();
        float capacity = modding.GetTotalWeightCapacity();
        float over = Mathf.Max(0f, total - capacity);
        lines.Add($"\n로봇 전체 무게 {total:0.##} / 지탱력 {capacity:0.##}");
        if (over > 0f)
        {
            lines.Add($"<color=#F2BF26>초과 {over:0.##} → 이동속도 -{over * modding.OverweightSpeedPenaltyPerUnit:0.##}</color>");
        }

        detail_popup.Show(
            $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName}",
            string.Join("\n", lines),
            null);
    }

    private void ShowDiscDetail(int discId)
    {
        if (shopManager == null || shopManager.Catalog == null) return;

        foreach (DiscData disc in shopManager.Catalog.Discs)
        {
            if (disc.discId != discId) continue;

            var lines = new List<string>
            {
                "분류: 디스크",
                disc.BuildDescription()
            };

            // 기획서 21종은 효과 종류가 제각각이라(처치 시 발동·시간제·확률형…) 실제 파라미터를
            // 값이 들어있는 것만 골라 덧붙인다.
            var numbers = new List<string>();
            if (disc.chance01 > 0f) numbers.Add($"발동 확률 {disc.chance01 * 100f:0.##}%");
            if (disc.flatValue != 0f) numbers.Add($"수치 {disc.flatValue:0.##}");
            if (disc.multiplier != 0f) numbers.Add($"배율 x{disc.multiplier:0.##}");
            if (disc.duration > 0f) numbers.Add($"지속 {disc.duration:0.##}초");
            if (disc.interval > 0f) numbers.Add($"주기 {disc.interval:0.##}초");
            if (disc.radius > 0f) numbers.Add($"범위 {disc.radius:0.##}");
            if (disc.cap > 0f) numbers.Add($"상한 {disc.cap:0.##}");
            if (disc.maxUses > 0) numbers.Add($"최대 {disc.maxUses}회");
            if (numbers.Count > 0) lines.Add(string.Join(" · ", numbers));

            AppendDiscStat(lines, disc.statA, disc.amountA);
            AppendDiscStat(lines, disc.statB, disc.amountB);
            AppendDiscStat(lines, disc.statC, disc.amountC);

            detail_popup.Show(
                $"<color={disc.grade.ToColorHex()}>{disc.grade.ToKorean()}</color> {disc.discName}",
                string.Join("\n", lines),
                disc.LoadIcon());
            return;
        }
    }

    private static void AppendDiscStat(List<string> lines, StatType stat, float amount)
    {
        if (amount == 0f) return;

        string sign = amount > 0f ? "+" : string.Empty;
        string color = amount > 0f ? "#88FF88" : "#FF8080";
        lines.Add($"<color={color}>{StatTypeNames.ToKorean(stat)} {sign}{amount:0.##}</color>");
    }

    private void ShowRobotDetail()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return;
        if (!GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)) return;

        ModdingManager modding = FindFirstObjectByType<ModdingManager>();

        var lines = new List<string>
        {
            "분류: 로봇(머리) - 런 중 교체 불가",
            $"기본 체력 {data.robot_hp} / 공격력 {data.robot_atk} / 방어력 {data.robot_def}",
            $"기본 이동속도 {data.robot_speed:0.##} / 회피 {data.robot_avoid:0.##} / 행운 {data.robot_luck:0.##}",
            $"치명타 {data.robot_cc:0.##}% · 배율 {data.robot_cd:0.##}",
            $"질량 {data.robot_mess:0.##}"
        };

        if (modding != null)
        {
            lines.Add($"\n무기 소켓 {modding.ActiveSocketCount}칸 · 디스크 슬롯 {modding.DiscSlotCount}칸");
            lines.Add($"부품 상자 적재량 {RunState.UnopenedPartBoxCount}/{modding.PartBoxCapacity}");
        }

        detail_popup.Show(data.robot_name, string.Join("\n", lines), null);
    }
}
