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
            // 카드 전체가 아니라 "구매" 버튼(가격 표시부)을 눌러야 구매되게 한다(2026-08-26
            // 사용자 지적: "카드 눌러서 바로 사져버리는데 구매버튼 눌러서 사게 만들어") -
            // 실제 구매 리스너는 BuildCardDecor가 만드는 PriceBox 버튼에 연결한다.
            if (offerSlots[i].lockButton != null) offerSlots[i].lockButton.onClick.AddListener(() => HandleLockClicked(index));
        }

        // 소켓 버튼 리스너는 여기서 달지 않는다 - 소켓 수가 로봇마다 다르고(최대 6) 씬에 깔린
        // 개수보다 많을 수 있어, 창을 열 때 EnsureSocketButtons()가 만들면서 함께 연결한다.

        SetupDetailInspector();

        // 2026-08-24 사용자 지정으로 <b>상점의 음악 볼륨 슬라이더를 삭제</b>했다("사운드 설정이
        // 상점에 있으면 안돼. 삭제해."). 2026-08-13에 "런 도중 볼륨을 조절할 유일한 지점"으로
        // 넣었던 것인데, 그 사이에 일시정지 메뉴의 설정창(SettingsPanelUI)이 생겨 인게임에서도
        // 볼륨을 조절할 수 있으므로 상점에 둘 이유가 없어졌다(타이틀 화면에도 같은 컨트롤이 있다).
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
        ApplyTextPolish();
        if (detail_popup != null) detail_popup.Hide(); // 상세 팝업은 캔버스 직속이라 패널과 같이 꺼지지 않는다
        Refresh();
    }

    /// <summary>
    /// 상점의 씬 텍스트가 각 9-slice 배경 베젤을 침범하지 않도록 실제 border 기반 여백을 준다.
    /// 기존 RectTransform 범위 검사는 통과해도 글리프가 장식 위에 붙는 영문 UI 문제를 보완한다.
    /// </summary>
    private void ApplyTextPolish()
    {
        TextMeshProUGUI title = transform.Find("TitleText")?.GetComponent<TextMeshProUGUI>();

        UiSafeArea.ApplyTextMarginsFromSibling(title);
        UiSafeArea.ApplyTextMarginsFromSibling(waveText);
        UiSafeArea.ApplyTextMarginsFromSibling(goldText);
        UiSafeArea.ApplyTextMarginsFromSibling(moddingStatusText);
        UiSafeArea.ApplyTextMarginsFromSibling(equippedWeaponsText);
        UiSafeArea.ApplyTextMarginsFromSibling(equippedDiscsText);
        UiSafeArea.ApplyTextMarginsFromSibling(statsText, 8f, true);
        UiSafeArea.ApplyTextMarginsFromSibling(refreshText, 5f);
        UiSafeArea.ApplyTextMarginsFromSibling(messageText, 5f);

        // 2026-08-26 사용자 지적: 메시지가 없을 때도 빈 배경 막대가 항상 떠 있어 "필요 없는
        // UI"로 보였다("상점의 UI 안의 UI와 바깥 UI의 간섭이 너무 심함" 지적과 같은 맥락).
        // 배경만 끄고 글자는 그대로 둔다 - 실제 안내 문구(구매 완료/실패 등)가 뜰 때는 배경
        // 없이 글자만 나타나고, 비어 있을 때는 아무것도 보이지 않는다. 다른 패널이 아래로
        // 늘어나며 이 자리를 덮게 되므로 글자가 그 위에 그려지도록 맨 앞으로 올린다.
        Image messageBackground = transform.Find("MessageText_BG")?.GetComponent<Image>();
        if (messageBackground != null) messageBackground.enabled = false;
        if (messageText != null)
        {
            // 2026-08-26: 패널과 겹치던 자리를 떠나 카드 위 얇은 전체폭 띠로 옮겼다 - 가운데
            // 정렬이 아니면 넓은 띠의 왼쪽 끝에 글자가 외롭게 붙는다.
            messageText.alignment = TextAlignmentOptions.Center;
            messageText.transform.SetAsLastSibling();
        }
    }

    public void Close()
    {
        SetCombatHudVisible(true);
        if (detail_popup != null) detail_popup.Hide();
        gameObject.SetActive(false);
    }

    // 상점이 <b>열려 있는 동안</b> 창 크기가 바뀌면 격자가 옛 픽셀 크기 그대로 남는다
    // (2026-08-26 사용자 리포트 "창모드 플레이 시 UI 내부 텍스트 및 리소스 축소로 비율이
    // 맞지 않음"의 원인). 실측: 1920x1080에서 연 상점을 1366x768로 줄이면 [Discs] 6칸이
    // [Equipped Weapons]·[Current Stats]·[Reroll Shop] 위를 덮었다. <b>같은 해상도에서
    // 새로 열면 멀쩡했으므로 레이아웃 자체가 아니라 "다시 계산하지 않는 것"이 원인이다.</b>
    //
    // Canvas가 ConstantPixelSize라 컨테이너의 픽셀 폭은 화면을 따라 줄어드는데,
    // GridLayoutGroup.cellSize는 ItemCellUI.EnsureGrid가 <b>만들 때 한 번</b> 계산한 고정
    // 픽셀이라 따라오지 않는다(ItemCellUI.EnsureGrid 주석 참고). ModdingPanelUI는 Update()에서
    // 매 프레임 EnsureGrid를 다시 돌려 이 문제가 없었고, 이 화면에만 그 장치가 없었다.
    //
    // 다만 여기는 칸 수가 데이터에 따라 달라져(소켓 수·디스크 수) EnsureGrid만으로는 부족하고
    // 칸을 다시 만드는 Refresh()가 필요하다. 그래서 매 프레임이 아니라 <b>해상도가 실제로
    // 바뀐 프레임에만</b> 돈다. 패널이 꺼져 있으면 Update 자체가 호출되지 않으므로 전투 중
    // 비용은 0이다.
    private Vector2 last_layout_size;

    private void LateUpdate()
    {
        // CanvasScaler.Update가 끝난 뒤 실제 설계 좌표를 읽는다. Screen 크기만 먼저 읽으면
        // 이전 배율로 계산된 임시 rect를 사용하고, 다음 프레임에는 재계산하지 않아 깨진 채 남는다.
        var panel = (RectTransform)transform;
        Vector2 now = panel.rect.size;
        if ((now - last_layout_size).sqrMagnitude < 0.01f) return;
        last_layout_size = now;

        // 격자 컨테이너는 테두리(고정 픽셀)와 제목 띠(화면 비례)를 섞어 놓기 때문에 해상도가
        // 바뀌면 다시 계산해야 한다. 참조를 비워 두면 EnsureEquipGrids가 새로 만든다.
        partsGrid = null;
        weaponsGrid = null;
        discsGrid = null;

        // 여백은 배경 아트의 실제 픽셀 크기에서 역산하므로(UiSafeArea) 이것도 다시 잡아야 한다.
        ApplyTextPolish();
        Refresh();
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
            SetMessage(Loc.T("shop.msg.nogold_refresh", shopManager.CurrentRefreshCost));
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

        if (offer.IsAccessory)
        {
            // 악세사리는 소켓 선택도, 구매 완료 잠금도 없다 - 즉시 사고 즉시 다시 살 수 있다
            // (ShopManager.TryPurchaseAccessory 참고).
            if (shopManager.TryPurchaseAccessory(index)) SetMessage(Loc.T("shop.msg.bought_accessory", offer.DisplayName, offer.Accessory.score));
            Refresh();
            return;
        }

        if (offer.IsDisc)
        {
            if (shopManager.TryPurchaseDisc(index)) SetMessage(Loc.T("shop.msg.bought", offer.DisplayName));
            Refresh();
            return;
        }

        OpenSocketPicker(index);
    }

    /// <summary>
    /// 무기 소켓 시스템 상한(2026-08-18 사용자 확정: "게임 최대는 6개 맞음. 기본 로봇만 4개가
    /// 최대고 다른 로봇은 6개인 로봇도 있음"). 소켓 선택 창은 여기까지 잘리지 않아야 한다.
    /// </summary>
    public const int MaxWeaponSockets = 6;

    /// <summary>
    /// 소켓 선택 버튼을 <b>소켓 수에 맞춰</b> 만들고 배치한다(2026-08-18 `UI 기획서.pdf` Phase C-5).
    ///
    /// 예전에는 씬에 고정 좌표로 깔린 버튼 4개를 그대로 쓰고 남는 것만 숨겼다. 6소켓 로봇이
    /// 생기면 5·6번 버튼이 아예 없어서 그 소켓에는 무기를 못 끼우게 되므로, 모자라면 1번 버튼을
    /// <b>복제</b>해 채우고 자리는 개수에 맞춰 다시 계산한다(정비 화면의 머리+소켓 줄이 열 수를
    /// 동적으로 늘리는 것과 같은 취급).
    /// </summary>
    private void EnsureSocketButtons(int socketCount)
    {
        int count = Mathf.Clamp(socketCount, 1, MaxWeaponSockets);

        if (socketButtons.Length < count)
        {
            System.Array.Resize(ref socketButtons, count);
            System.Array.Resize(ref socketButtonTexts, count);
        }

        Transform parent = socketButtons[0] != null ? socketButtons[0].transform.parent : null;

        for (int i = 0; i < socketButtons.Length; i++)
        {
            if (socketButtons[i] == null)
            {
                if (socketButtons[0] == null || parent == null) continue;

                Button clone = Instantiate(socketButtons[0], parent);
                clone.name = $"SocketButton{i + 1}";
                socketButtons[i] = clone;
                socketButtonTexts[i] = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            // 복제본은 원본의 onClick 리스너까지 복사해 오고(=엉뚱한 소켓에 장착된다), 이 메서드는
            // 창을 열 때마다 불리므로 매번 지우고 자기 인덱스로 다시 단다.
            int index = i;
            socketButtons[i].onClick.RemoveAllListeners();
            socketButtons[i].onClick.AddListener(() => HandleSocketChosen(index));

            if (socketButtonTexts[i] == null)
                socketButtonTexts[i] = socketButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);

            // 6칸이 되면 칸이 작아지는데 소켓 버튼 글은 3줄("소켓 N/현재 무기/경고")이라
            // 자동 크기 조절을 걸어두지 않으면 넘친다.
            if (socketButtonTexts[i] != null) ItemCellUI.ApplyTextSizing(socketButtonTexts[i], 24f);
        }

        LayoutSocketButtons(count);
    }

    // 소켓 선택 창 안쪽의 빈 띠(제목 아래 ~ 취소 버튼 위)를 1열 또는 2열 격자로 나눠 쓴다.
    private void LayoutSocketButtons(int count)
    {
        const float top = 0.84f, bottom = 0.24f, left = 0.06f, right = 0.94f;
        const float gapX = 0.06f, gapY = 0.04f;

        int columns = count == 1 ? 1 : 2;
        int rows = Mathf.CeilToInt(count / (float)columns);

        float cellWidth = (right - left - gapX * (columns - 1)) / columns;
        float cellHeight = (top - bottom - gapY * (rows - 1)) / rows;

        for (int i = 0; i < count; i++)
        {
            if (socketButtons[i] == null) continue;

            int column = i % columns;
            int row = i / columns;

            float xMin = left + column * (cellWidth + gapX);
            float yMax = top - row * (cellHeight + gapY);

            var rect = (RectTransform)socketButtons[i].transform;
            rect.anchorMin = new Vector2(xMin, yMax - cellHeight);
            rect.anchorMax = new Vector2(xMin + cellWidth, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    // 무기는 어느 소켓을 교체할지 골라야 해서 소켓 선택 줄을 연다.
    private void OpenSocketPicker(int offerIndex)
    {
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager == null)
        {
            SetMessage(Loc.T("shop.msg.no_equip_target"));
            return;
        }

        pendingWeaponOfferIndex = offerIndex;

        ShopManager.Offer offer = shopManager.Offers[offerIndex];
        if (socketPickerTitleText != null)
        {
            socketPickerTitleText.text = Loc.T("shop.socketpicker.title", offer.DisplayName, offer.Grade.ToDisplayName());
        }

        EnsureSocketButtons(shootManager.SocketCount);

        for (int i = 0; i < socketButtons.Length; i++)
        {
            bool exists = i < shootManager.SocketCount;

            if (socketButtons[i] != null) socketButtons[i].gameObject.SetActive(exists);
            if (!exists || socketButtonTexts[i] == null) continue;

            string current = shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade currentGrade)
                ? $"{weapon.Weapon()} ({currentGrade.ToDisplayName()})"
                : Loc.T("common.empty");

            // 2026-08-12 "무기 소켓 개별화" 플랜부터 타입 불일치/무게 초과는 더 이상 버튼을
            // 막지 않는다(언제나 장착 가능) - 대신 비차단 경고 문구로 어떤 패널티가 붙는지 보여준다.
            bool allowed = shopManager.CanPurchaseWeaponIntoSocket(offerIndex, i, out string reason);
            if (socketButtons[i] != null) socketButtons[i].interactable = allowed;

            if (!allowed)
            {
                socketButtonTexts[i].text = $"{Loc.T("shop.socket_n", i + 1)}\n{current}\n<color=#FF8080>{reason}</color>";
                continue;
            }

            string warning = shopManager.BuildSocketWarning(offerIndex, i);
            socketButtonTexts[i].text = warning.Length > 0
                ? $"{Loc.T("shop.socket_n", i + 1)}\n{current}\n<color=#F2BF26>{warning}</color>"
                : $"{Loc.T("shop.socket_n", i + 1)}\n{current}";
        }

        if (socketPickerRoot != null)
        {
            // 보유·장착 목록 격자(PartsGrid/WeaponsGrid/DiscsGrid)와 음악 볼륨 슬라이더는 전부
            // 런타임에 만들어져 이 패널의 <b>마지막 자식</b>으로 붙는다. 씬에 있는 SocketPicker는
            // 그보다 앞 형제라 UI 그리기 순서상 격자에 가려진다(2026-08-18 아이콘화 이후 생긴
            // 문제 - 실측 캡처로 발견). 열 때마다 맨 뒤로 보내 항상 위에 그려지게 한다.
            socketPickerRoot.transform.SetAsLastSibling();
            socketPickerRoot.SetActive(true);
        }
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
            SetMessage(Loc.T("shop.msg.equipped", socketIndex + 1, offer.DisplayName, offer.Grade.ToDisplayName()));
        }
        else
        {
            shopManager.CanPurchaseWeaponIntoSocket(offerIndex, socketIndex, out string reason);
            SetMessage(string.IsNullOrEmpty(reason) ? Loc.T("shop.msg.buy_failed") : reason);
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

        if (refreshText != null && shopManager != null)
        {
            refreshText.text = Loc.T("shop.refresh_cost", shopManager.CurrentRefreshCost);
        }
    }

    private void RefreshOffers()
    {
        if (shopManager == null) return;

        EnsureCardDecor();

        for (int i = 0; i < offerSlots.Length; i++)
        {
            OfferSlotUI ui = offerSlots[i];
            CardDecor decor = i < card_decor.Length ? card_decor[i] : default;
            bool hasOffer = i < shopManager.Offers.Count && shopManager.Offers[i] != null;

            if (ui.cardButton != null) ui.cardButton.gameObject.SetActive(hasOffer);
            if (ui.lockButton != null) ui.lockButton.gameObject.SetActive(hasOffer);

            if (!hasOffer) continue;

            ShopManager.Offer offer = shopManager.Offers[i];

            // ① 아이콘
            if (ui.iconImage != null)
            {
                Sprite icon = ResolveOfferIcon(offer);
                ui.iconImage.sprite = icon;
                ui.iconImage.enabled = icon != null;
            }

            // ①-b 카드 배경도 등급별 아트로 바꾼다(2026-08-25 - "등급이 존재하는 모든 아이템
            // 카드 ui를 교체하면돼"). 씬은 Black_ui01을 물고 있고, 같은 세트의 색깔 변형이
            // UI/Grade/<색>/ 아래에 있다. 아트를 못 찾으면 씬의 원래 배경을 그대로 둔다.
            if (ui.cardButton != null)
            {
                Image cardBackground = ui.cardButton.GetComponent<Image>();
                Sprite gradeCard = ItemCellUI.GradeSprite(offer.Grade, "ui01");
                if (cardBackground != null && gradeCard != null) cardBackground.sprite = gradeCard;
            }

            // ② 종류 - 기획서 표기 형식: "전설 · 무기"
            if (ui.headerText != null)
            {
                ui.headerText.text = $"<color={offer.Grade.ToColorHex()}>{offer.Grade.ToDisplayName()}</color> · {offer.CategoryName}";
            }

            // ③ 이름 - 등급색으로 칠해 카드 안에서 가장 먼저 눈에 들어오게 한다.
            if (decor.nameText != null)
            {
                decor.nameText.text = $"<color={offer.Grade.ToColorHex()}>{offer.DisplayName}</color>";
            }

            // ④ 능력치 - 씬의 BodyText는 이제 성능 요약만 담는다.
            if (ui.bodyText != null) ui.bodyText.text = offer.BuildDescription();

            // ⑤ 가격 - 카드 하단 박스. 이미 산 카드는 가격 대신 "구매함"이라 코인도 숨긴다.
            if (decor.priceText != null)
            {
                decor.priceText.text = offer.Purchased ? Loc.T("shop.purchased_short") : offer.Price.ToString();
                decor.priceText.color = offer.Purchased ? PurchasedGray : Color.white;
            }
            if (decor.priceCoin != null) decor.priceCoin.enabled = !offer.Purchased && decor.priceCoin.sprite != null;

            // 구매 완료 스탬프 / 잠금 강조
            if (decor.stamp != null) decor.stamp.SetActive(offer.Purchased);
            if (decor.lockGlow != null) decor.lockGlow.SetActive(offer.Locked && !offer.Purchased);
            // 2026-08-25 사용자가 잠김/해제 아이콘을 따로 올려줬다 - 예전처럼 자물쇠 하나를
            // 노란색으로 물들여 구분하지 않고 그림 자체를 바꾼다(색은 원색 그대로 둔다).
            if (decor.lockIcon != null)
            {
                decor.lockIcon.sprite = offer.Locked ? UiIconLibrary.Lock() : UiIconLibrary.Unlock();
                decor.lockIcon.color = Color.white;
            }

            if (decor.buyButton != null) decor.buyButton.interactable = !offer.Purchased;
            if (ui.lockButton != null) ui.lockButton.interactable = !offer.Purchased;
            if (ui.lockText != null) ui.lockText.text = offer.Locked ? Loc.T("shop.unlock") : Loc.T("shop.lock");
        }
    }

    // ── 상점 카드 내부 필드 분리 ─────────────────────────────────────
    // 2026-08-18 `UI 기획서.pdf` Phase C-1~C-3. 씬의 카드에는 아이콘·헤더·본문 3개뿐이라
    // 이름·능력치·가격이 본문 텍스트 하나에 뭉쳐 있었다. 씬을 건드리지 않고 카드마다 하위
    // 요소를 코드로 덧붙여 필드를 나눈다(Phase D/E의 일시정지·환경설정 화면과 같은 방식).
    //
    // 카드 안(로컬 0~1) 세로 배치:
    //   0.72~0.98 아이콘 + 종류      (씬에 이미 있음, 건드리지 않는다)
    //   0.55~0.70 이름               (신규)
    //   0.26~0.54 능력치             (씬의 BodyText를 이 자리로 옮긴다)
    //   0.05~0.24 가격 박스          (신규)

    private struct CardDecor
    {
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI priceText;
        public Image priceCoin;
        public Button buyButton;    // 가격 표시부 자체(카드 전체가 아니라 이 버튼을 눌러야 구매된다)
        public GameObject stamp;    // 빨간 대각선 "구매 완료"
        public GameObject lockGlow; // 잠긴 카드를 감싸는 노란 테두리
        public Image lockIcon;      // 잠금 버튼의 자물쇠
    }

    private CardDecor[] card_decor = new CardDecor[0];

    /// <summary>정비 화면의 슬롯 강조와 같은 노란색(파랑 계열은 희귀 등급과 헷갈린다).</summary>
    private static readonly Color LockYellow = new Color(0.949f, 0.749f, 0.149f, 1f); // #F2BF26

    /// <summary>구매 완료 스탬프의 빨강. 등급색 빨강(전설)보다 진해 서로 구분된다.</summary>
    private static readonly Color StampRed = new Color(0.87f, 0.16f, 0.16f, 1f);

    private static readonly Color PurchasedGray = new Color(0.65f, 0.65f, 0.65f, 1f);

    /// <summary>스탬프를 비스듬히 눕히는 각도(도장을 찍은 느낌).</summary>
    private const float StampAngle = 14f;

    private void EnsureCardDecor()
    {
        // 에디터 도메인 리로드로 참조만 날아가고 오브젝트는 씬에 남아 있을 수 있다
        // (2026-08-18 AiCoreExtraButtonsUI에서 겪은 함정) - 그래서 개수뿐 아니라 실제 참조가
        // 살아 있는지까지 확인하고, 다시 만들 때는 같은 이름의 옛 오브젝트를 먼저 지운다.
        if (card_decor.Length == offerSlots.Length)
        {
            bool alive = true;
            for (int i = 0; i < card_decor.Length; i++)
            {
                if (offerSlots[i].cardButton == null) continue;
                if (card_decor[i].nameText != null) continue;
                alive = false;
                break;
            }
            if (alive) return;
        }

        card_decor = new CardDecor[offerSlots.Length];
        for (int i = 0; i < offerSlots.Length; i++) card_decor[i] = BuildCardDecor(i);
    }

    private CardDecor BuildCardDecor(int index)
    {
        var decor = new CardDecor();

        Button card = offerSlots[index].cardButton;
        if (card == null) return decor;

        var root = (RectTransform)card.transform;

        DestroyIfExists(root, "NameText");
        DestroyIfExists(root, "PriceBox");
        DestroyIfExists(root, "LockGlow");
        DestroyIfExists(root, "PurchasedStamp");

        // 카드 내용물은 아래 설계 좌표(테두리를 무시한 0~1)로 잡고, 실제 앵커는 ShopCardLayout이
        // 카드 배경 아트의 진짜 베젤 안쪽으로 사상한다(2026-08-25 "상점에서 이미지 어긋난다" 수정).
        ShopCardLayout layout = card.GetComponent<ShopCardLayout>();
        if (layout == null) layout = card.gameObject.AddComponent<ShopCardLayout>();
        layout.SetBackground(card.GetComponent<Image>());

        // 아이콘 뒤 박스, 종류 배지 박스를 없앤다(2026-08-26 사용자 지적: "이런 작은 카드
        // 안에 또 UI가 들어가있는 상황은 없어야 해" - 카드 자체가 이미 배경인데 그 위에 테두리
        // 있는 작은 패널(HeaderText_BG)을 얹으면 프레임 안에 프레임이 생긴다. ItemCellUI가
        // 슬롯 칸에서 이미 지키는 규칙("아이콘 뒤에 별도 사각형을 깔지 않는다")을 상점 카드에도
        // 맞춘다. 씬 오브젝트는 지우지 않고 렌더링만 끈다(이 화면의 "씬 수정 0건" 관례 유지).
        Image iconBackground = root.Find("IconImage_BG")?.GetComponent<Image>();
        if (iconBackground != null) iconBackground.enabled = false;

        TextMeshProUGUI header = root.Find("HeaderText")?.GetComponent<TextMeshProUGUI>();
        Image headerBackground = root.Find("HeaderText_BG")?.GetComponent<Image>();
        if (headerBackground != null) headerBackground.enabled = false;

        // 내용 위계 재정렬(2026-08-26 사용자 지적: "등급이 중요한게 아니라 아이템 이름과
        // 그림이 더 중요한데 등급 표시가 제일 크게 나와있어"). 아이콘을 크게 키워 중앙에 두고,
        // 이름을 큼직하게 그 아래 배치한다. 등급·종류는 이름보다 작은 보조 정보로 내린다 -
        // 어차피 등급색은 이름 글자색에 이미 드러나므로 배지를 키워 다시 강조할 필요가 없다.
        // 카드 세로 배분(설계 좌표 0~1 - ShopCardLayout이 카드 베젤 안쪽 밴드로 사상한다).
        //
        // <b>TMP 자동 크기는 "칸이 커져야" 커진다 - 상한만 올려서는 아무 일도 안 일어난다.</b>
        // 2026-08-26 1차에서 등급 표기 상한을 16 → 24pt로 올렸는데 실측 폰트는 14.2pt 그대로였다
        // ("여전히 너무 작아"). 띠가 20.6px뿐이라 20.6 / 1.45 = 14.2pt가 실질 상한이었던 것이다
        // (줄 높이 = 글자 크기 x 1.45). 그래서 2차에서는 <b>띠 높이 자체를 늘렸다</b>:
        //
        //   카드를 세로로 확대(씬 OfferCard1~4 min.y 0.05 → 0.025, 안쪽 밴드 240 → 267px)
        //   아이콘 0.36 → 0.221 / 이름 0.13 / 등급·종류 0.085 → 0.116 /
        //   설명 0.14 → 0.19 / 구매 버튼 0.185 → 0.26
        //
        // 아이콘은 preserveAspect라 띠가 줄어든 만큼 작아질 뿐 잘리지 않는다.
        TrackSceneCardChild(layout, root, "IconImage", 0.28f, 0.764f, 0.72f, 0.985f);

        if (header != null)
        {
            layout.Track(header.rectTransform, new Vector2(0.05f, 0.494f), new Vector2(0.95f, 0.610f));
            header.alignment = TextAlignmentOptions.Center;
            ItemCellUI.ApplyTextSizing(header, 30f);
            header.textWrappingMode = TextWrappingModes.NoWrap;
            header.margin = Vector4.zero;
        }

        decor.nameText = MakeText(root, "NameText", 0.05f, 0.622f, 0.95f, 0.752f, 32f, TextAlignmentOptions.Center);
        layout.Track(decor.nameText.rectTransform, new Vector2(0.05f, 0.622f), new Vector2(0.95f, 0.752f));
        decor.nameText.fontStyle = FontStyles.Bold;
        decor.nameText.margin = new Vector4(8f, 0f, 8f, 0f);

        // 씬의 BodyText를 "능력치" 자리로 좁힌다. 이 파일의 EnsureGridContainer가 보유 목록
        // 제목 텍스트를 같은 방식으로 옮기고 있어서 새로운 수법은 아니다. 아래 구매 버튼을
        // 키운 만큼(2026-08-26) 이 칸은 조금 줄었다.
        if (offerSlots[index].bodyText != null)
        {
            // 설명 칸(위 세로 배분 주석 참고). 자동 크기가 실제로 커지려면 칸 자체가 커져야 한다.
            layout.Track(offerSlots[index].bodyText.rectTransform,
                         new Vector2(0.05f, 0.292f), new Vector2(0.95f, 0.482f));
            offerSlots[index].bodyText.alignment = TextAlignmentOptions.TopLeft;
            ItemCellUI.ApplyTextSizing(offerSlots[index].bodyText, 26f);
            offerSlots[index].bodyText.margin = new Vector4(8f, 4f, 8f, 4f);
        }

        // 구매 버튼 - 예전엔 배경이 카드와 다른 사각 패널(Black_ui04)이라 "카드 안에 또 카드"
        // 처럼 보였다(2026-08-26 사용자 지적). 다른 화면의 진짜 버튼과 같은 아트(Purple_button00)를
        // 쓰고, 실제 클릭 대상도 카드 전체가 아니라 <b>이 버튼 하나</b>로 좁힌다(2026-08-26 사용자
        // 지적: "카드 눌러서 바로 사져버리는데 구매버튼 눌러서 사게 만들어" - 실수 구매 방지).
        // 처음엔 카드 폭 90%짜리 얇고 넓은 막대였는데 사용자가 "상하로 더 길고 좌우로는 짧게,
        // 비율이 이상해 규칙도 어긴다"고 재지적했다 - 폭을 줄이고(60%) 높이를 늘려(21%) 다른
        // 실제 버튼들과 비슷한 세로:가로 비율로 맞추고 가운데 정렬했다.
        // 2026-08-26 (2차) "구매버튼 높이가 너무 짧아" - 0.185 → 0.26으로 늘렸다. 안의 가격
        // 글자도 자동 크기라 버튼이 높아진 만큼 함께 커진다(상한 30pt).
        RectTransform priceBox = MakeChild(root, "PriceBox", 0.20f, 0.02f, 0.80f, 0.28f,
                                           typeof(CanvasRenderer), typeof(Image));
        layout.Track(priceBox, new Vector2(0.20f, 0.02f), new Vector2(0.80f, 0.28f));
        var boxImage = priceBox.GetComponent<Image>();
        Sprite boxSprite = Resources.Load<Sprite>("UI/Purple_button00");
        if (boxSprite != null)
        {
            boxImage.sprite = boxSprite;
            boxImage.type = Image.Type.Sliced;
            boxImage.color = Color.white;
        }
        else
        {
            boxImage.color = new Color(0f, 0f, 0f, 0.45f); // 아트를 못 찾으면 단색 박스
        }
        boxImage.raycastTarget = true; // 이제 이 박스 자체가 버튼이라 클릭을 받아야 한다

        var buyButton = priceBox.gameObject.AddComponent<Button>();
        buyButton.targetGraphic = boxImage;
        var buyColors = buyButton.colors;
        buyColors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        buyColors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        buyButton.colors = buyColors;
        int offerIndex = index; // 람다 클로저 캡처용 로컬
        buyButton.onClick.AddListener(() => HandleOfferClicked(offerIndex));
        decor.buyButton = buyButton;

        // 카드 전체를 덮던 옛 버튼은 완전히 꺼서(클릭도 안 받고 호버 틴트도 안 생기게) 구매
        // 버튼과 헷갈리지 않게 한다 - Button.interactable=false는 targetGraphic을 회색으로
        // 물들이므로(등급색 배경이 죽는다) 컴포넌트 자체를 비활성화한다.
        card.enabled = false;

        RectTransform coin = MakeChild(priceBox, "Coin", 0.06f, 0.16f, 0.22f, 0.84f,
                                        typeof(CanvasRenderer), typeof(Image));
        decor.priceCoin = coin.GetComponent<Image>();
        decor.priceCoin.sprite = Resources.Load<Sprite>("Gold"); // 헤더 골드 = 인게임 드랍 금화와 같은 코인
        decor.priceCoin.preserveAspect = true;
        decor.priceCoin.raycastTarget = false;
        decor.priceCoin.enabled = decor.priceCoin.sprite != null;

        decor.priceText = MakeText(priceBox, "PriceText", 0.26f, 0.05f, 0.97f, 0.95f, 30f,
                                    TextAlignmentOptions.Left);
        // 텍스트가 버튼의 실제 9-slice 테두리를 침범하지 않도록 실측 border에서 좌우 여백을
        // 역산한다("UI 제작 규칙" - 임의의 숫자로 여백을 정하지 않는다). 이 버튼은 카드 폭에 비해
        // 얇은 가로 막대라 세로 테두리는 장식용 베벨이다 - vertical:true를 줬다가 세로 여백이
        // 버튼 높이를 다 먹어 글자가 통째로 사라지는 회귀를 겪었다(2026-08-26). 좌우만 맞춘다.
        UiSafeArea.ApplyTextMargins(decor.priceText, boxImage, 6f);

        // 잠긴 카드 강조 - 카드 전체를 감싸는 노란 테두리.
        RectTransform glow = MakeChild(root, "LockGlow", 0f, 0f, 1f, 1f,
                                        typeof(CanvasRenderer), typeof(Image));
        var glowImage = glow.GetComponent<Image>();
        glowImage.sprite = UiIconLibrary.Frame();
        glowImage.type = Image.Type.Sliced;
        glowImage.color = LockYellow;
        glowImage.raycastTarget = false;
        decor.lockGlow = glow.gameObject;
        glow.gameObject.SetActive(false);

        // 구매 완료 스탬프 - 마지막에 만들어야 카드의 다른 요소 위에 그려진다.
        RectTransform stamp = MakeChild(root, "PurchasedStamp", 0f, 0f, 1f, 1f);

        RectTransform dim = MakeChild(stamp, "Dim", 0f, 0f, 1f, 1f, typeof(CanvasRenderer), typeof(Image));
        var dimImage = dim.GetComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.55f);
        dimImage.raycastTarget = false;

        RectTransform badge = MakeChild(stamp, "Badge", 0.06f, 0.34f, 0.94f, 0.62f,
                                         typeof(CanvasRenderer), typeof(Image));
        var badgeImage = badge.GetComponent<Image>();
        badgeImage.sprite = UiIconLibrary.Frame();
        badgeImage.type = Image.Type.Sliced;
        badgeImage.color = StampRed;
        badgeImage.raycastTarget = false;
        badge.localRotation = Quaternion.Euler(0f, 0f, StampAngle);

        TextMeshProUGUI stampText = MakeText(badge, "Label", 0.05f, 0.05f, 0.95f, 0.95f, 44f,
                                              TextAlignmentOptions.Center);
        stampText.text = Loc.T("shop.purchased_stamp");
        stampText.color = StampRed;
        stampText.fontStyle = FontStyles.Bold;

        decor.stamp = stamp.gameObject;
        stamp.gameObject.SetActive(false);

        // 잠금 버튼 - 2026-08-26 사용자 지적: "상점 잠금은 오른쪽 위로 사각형 UI에 넣어서
        // 옮기고 카드 자체를 위아래로 더 늘려. 잠금이 저렇게 큰 비율을 차지할 이유가 없음."
        // 예전엔 카드 폭 전체를 쓰는 "Lock" 글자 버튼이었는데(씬에서 카드 우상단의 작은 정사각형
        // 배지로 옮겨 앉혔다 - RectTransform 자체는 이 스크립트가 만들지 않고 씬이 소유하므로,
        // 이 함수에서는 글자를 숨기고 아이콘만 정사각형 전체를 채우게 한다.
        Button lockButton = offerSlots[index].lockButton;
        if (lockButton != null)
        {
            var lockRoot = (RectTransform)lockButton.transform;
            DestroyIfExists(lockRoot, "LockIcon");

            // 배지가 작아 "잠금"/"해제" 글자가 들어갈 자리가 없다 - 아이콘 하나로만 상태를
            // 표현한다(도감 화면의 자물쇠 배지와 같은 관례).
            if (offerSlots[index].lockText != null) offerSlots[index].lockText.gameObject.SetActive(false);

            RectTransform icon = MakeChild(lockRoot, "LockIcon", 0.1f, 0.1f, 0.9f, 0.9f,
                                            typeof(CanvasRenderer), typeof(Image));
            decor.lockIcon = icon.GetComponent<Image>();
            decor.lockIcon.sprite = UiIconLibrary.Lock();
            decor.lockIcon.preserveAspect = true;
            decor.lockIcon.raycastTarget = false;
        }

        return decor;
    }

    /// <summary>
    /// 씬이 소유한 카드 자식(아이콘·종류 줄)을 <see cref="ShopCardLayout"/>에 등록한다.
    /// 이름으로 찾으므로 씬에서 그 요소가 사라지면 조용히 넘어간다.
    /// </summary>
    private static void TrackSceneCardChild(ShopCardLayout layout, RectTransform card, string childName,
                                             float xMin, float yMin, float xMax, float yMax)
    {
        Transform child = card.Find(childName);
        if (child == null) return;

        layout.Track((RectTransform)child, new Vector2(xMin, yMin), new Vector2(xMax, yMax));
    }

    // ── 코드로 UI 요소를 만들 때 쓰는 공용 도구 ──────────────────────

    private static RectTransform MakeChild(Transform parent, string name,
                                            float xMin, float yMin, float xMax, float yMax,
                                            params System.Type[] components)
    {
        var types = new System.Type[components.Length + 1];
        types[0] = typeof(RectTransform);
        System.Array.Copy(components, 0, types, 1, components.Length);

        var go = new GameObject(name, types);
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name,
                                             float xMin, float yMin, float xMax, float yMax,
                                             float maxFontSize, TextAlignmentOptions alignment)
    {
        RectTransform rect = MakeChild(parent, name, xMin, yMin, xMax, yMax,
                                        typeof(CanvasRenderer), typeof(TextMeshProUGUI));

        var text = rect.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, maxFontSize);
        return text;
    }

    /// <summary>
    /// 같은 이름의 옛 오브젝트를 지운다. Destroy()는 프레임 끝에야 실제로 파괴되므로 먼저 부모에서
    /// 떼어내야 바로 뒤에 만드는 새 오브젝트와 이름이 겹치지 않는다(ItemCellUI.ClearChildren과 같은 이유).
    /// </summary>
    private static void DestroyIfExists(RectTransform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing == null) return;

        existing.SetParent(null, false);
        Destroy(existing.gameObject);
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
        if (offer.IsAccessory) return offer.Accessory.LoadIcon();
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

        // 격자는 <b>각자의 배경 패널에 맞춰</b> 놓는다 - 좌표를 여기에 박아 두지 않으므로
        // 씬에서 패널을 옮겨도 격자가 저절로 따라온다(예전에는 정규화 상수를 양쪽에 이중으로
        // 적어 두고 "씬 값과 함께 고쳐야 한다"고 주석을 달아야 했다).
        // 테두리를 피하는 방법은 EnsureGridContainer 주석 참고.
        // 제목 띠 높이. 무기·디스크는 2026-08-26 "(상세)" 안내 줄을 빼면서 <b>한 줄</b>이 되어
        // 0.070/0.060 → 0.042로 줄였고, 그만큼 격자가 세로로 넓어진다. 파츠는 둘째 줄
        // (AI 코어 레벨·무게)이 실제 정보라 남아 있어 두 줄 높이를 그대로 쓴다.
        partsGrid = EnsureGridContainer(panel, "PartsGrid", moddingStatusText,
                                        panel.Find("ModdingStatusText_BG") as RectTransform, 0.055f);
        weaponsGrid = EnsureGridContainer(panel, "WeaponsGrid", equippedWeaponsText,
                                          panel.Find("EquippedWeaponsText_BG") as RectTransform, 0.042f);
        discsGrid = EnsureGridContainer(panel, "DiscsGrid", equippedDiscsText,
                                        panel.Find("EquippedDiscsText_BG") as RectTransform, 0.042f);

        EnsureWeaponSwapButton(panel, equippedWeaponsText);
    }

    // ── 장착 무기 위치 교체 (2026-08-24 사용자 요청) ────────────────────────────
    //
    // "장착한 무기 서로 위치 교체기능 만들어줘".
    //
    // 무기 칸을 누르면 원래 <b>상세 팝업</b>이 열리므로, 같은 클릭에 교체를 겹쳐 넣으면 둘 다
    // 예측하기 어려워진다. 그래서 제목 줄 옆에 "위치 교체" 버튼을 두고 <b>교체 모드</b>를
    // 명시적으로 켠다 - 모드가 켜져 있는 동안에는 무기 칸 클릭이 "고르기 → 맞바꾸기"로 동작하고,
    // 꺼져 있으면 예전처럼 상세 팝업이 열린다.
    private Button weapon_swap_button;
    private TextMeshProUGUI weapon_swap_label;
    private bool weapon_swap_mode;
    private int weapon_swap_source = -1;

    /// <summary>"위치 교체" 버튼의 폭(px). 제목 줄 오른쪽 끝에 이만큼을 떼어 준다.</summary>
    private const float WeaponSwapButtonWidth = 150f;

    /// <summary>버튼과 제목 글자 사이 간격(px).</summary>
    private const float WeaponSwapButtonGap = 8f;

    /// <summary>
    /// "위치 교체" 버튼을 <b>무기 제목 줄의 오른쪽 끝</b>에 앉히고, 제목 글자 영역을 그만큼 좁힌다
    /// (안 좁히면 제목이 버튼 밑으로 들어간다).
    ///
    /// <para>2026-08-26: 예전에는 ShopPanel 기준 정규화 상수(0.640~0.727, 0.805~0.865)로 박혀
    /// 있었는데, "(상세)" 안내 줄을 빼면서 제목 띠를 줄이자 <b>버튼이 무기 칸 위에 겹쳤다</b>.
    /// 이제 <b>제목의 앵커·세로 띠를 그대로 물려받아</b> 제목이 어디로 가든 따라간다 -
    /// 이 화면의 다른 좌표들과 같은 방침이다(EnsureGridContainer 주석 참고).</para>
    /// </summary>
    private void EnsureWeaponSwapButton(RectTransform panel, TextMeshProUGUI title)
    {
        if (weapon_swap_button == null)
        {
            DestroyIfExists(panel, "WeaponSwapButton");

            RectTransform created = MakeChild(panel, "WeaponSwapButton", 0f, 0f, 1f, 1f,
                                              typeof(CanvasRenderer), typeof(Image), typeof(Button));

            var image = created.GetComponent<Image>();
            Sprite plate = Resources.Load<Sprite>("UI/Purple_button00");
            if (plate != null)
            {
                image.sprite = plate;
                image.type = Image.Type.Sliced; // UI 아트는 전부 9-슬라이스다(프로젝트 안내.md 참고)
            }
            image.color = Color.white;

            weapon_swap_label = MakeText(created, "Label", 0.05f, 0.05f, 0.95f, 0.95f, 20f, TextAlignmentOptions.Center);
            weapon_swap_label.text = Loc.T("shop.swap");
            UiSafeArea.ApplyTextMargins(weapon_swap_label, image, 3f);

            weapon_swap_button = created.GetComponent<Button>();
            weapon_swap_button.onClick.AddListener(ToggleWeaponSwapMode);
        }

        // 자리 잡기는 <b>매번</b> 한다 - 제목 띠는 해상도에 따라 높이가 달라지고, 격자를 다시
        // 만들 때마다 EnsureGridContainer가 제목 offset을 새로 쓰기 때문이다.
        if (title == null) return;

        RectTransform titleRect = title.rectTransform;
        var buttonRect = (RectTransform)weapon_swap_button.transform;

        // <b>좌우 앵커를 제목의 오른쪽 앵커선 하나로 모은다.</b> 앵커가 벌어져 있으면
        // offsetMin.x는 왼쪽 앵커선, offsetMax.x는 오른쪽 앵커선을 기준으로 재므로 둘을 섞어
        // 폭을 계산할 수 없다(그대로 복사했다가 버튼 폭이 150px 대신 975px이 됐다 - 2026-08-26 실측).
        // 한 선에 모으면 두 offset이 같은 기준을 써서 그 차이가 곧 픽셀 폭이 된다.
        buttonRect.anchorMin = new Vector2(titleRect.anchorMax.x, titleRect.anchorMin.y);
        buttonRect.anchorMax = new Vector2(titleRect.anchorMax.x, titleRect.anchorMax.y);
        buttonRect.offsetMin = new Vector2(titleRect.offsetMax.x - WeaponSwapButtonWidth, titleRect.offsetMin.y);
        buttonRect.offsetMax = new Vector2(titleRect.offsetMax.x, titleRect.offsetMax.y);

        titleRect.offsetMax = new Vector2(titleRect.offsetMax.x - WeaponSwapButtonWidth - WeaponSwapButtonGap,
                                          titleRect.offsetMax.y);
    }

    private void ToggleWeaponSwapMode()
    {
        weapon_swap_mode = !weapon_swap_mode;
        weapon_swap_source = -1;

        SetMessage(weapon_swap_mode
            ? Loc.T("shop.swap.begin")
            : Loc.T("shop.swap.cancelled"));

        RefreshEquipment();
    }

    /// <summary>무기 칸 클릭. 교체 모드가 아니면 예전처럼 상세 팝업을 연다.</summary>
    private void HandleWeaponCellClicked(int socketIndex)
    {
        if (!weapon_swap_mode)
        {
            ShowDetail($"w:{socketIndex}");
            return;
        }

        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager == null) return;

        if (weapon_swap_source < 0)
        {
            // 빈 소켓을 출발점으로 고르면 옮길 것이 없다.
            if (!shootManager.TryGetSocketInfo(socketIndex, out _, out _))
            {
                SetMessage(Loc.T("shop.swap.socket_empty", socketIndex + 1));
                return;
            }

            weapon_swap_source = socketIndex;
            SetMessage(Loc.T("shop.swap.picked", socketIndex + 1));
            RefreshEquipment();
            return;
        }

        if (weapon_swap_source == socketIndex)
        {
            weapon_swap_source = -1;
            SetMessage(Loc.T("shop.swap.deselected"));
            RefreshEquipment();
            return;
        }

        int from = weapon_swap_source;
        weapon_swap_source = -1;

        if (shootManager.SwapWeapons(from, socketIndex))
        {
            weapon_swap_mode = false;
            SetMessage(Loc.T("shop.swap.done", from + 1, socketIndex + 1));
        }
        else
        {
            SetMessage(Loc.T("shop.swap.failed"));
        }

        RefreshEquipment();
    }

    /// <param name="titleBottom">제목 줄의 아래쪽 경계(정규화). 격자는 이 아래를 쓴다.</param>
    /// <summary>
    /// 제목 줄과 아이콘 격자를 <b>배경 패널의 실제 9-slice 테두리 안쪽</b>에 배치한다.
    ///
    /// <para>2026-08-26 사용자 지적("패널과 카드가 겹친다"). 예전에는 좌표를 ShopPanel 기준
    /// 정규화 상수로 박아 두고 배경보다 0.008(약 15px)만 안쪽으로 들여썼는데, 배경 아트
    /// <c>Black_ui01</c>의 테두리는 <b>34px</b>이라 격자가 좌우 18.6px·아래 28.6px씩 테두리를
    /// 파고들고 있었다(실측 1920x1080). <b>9-slice 코너는 rect 크기와 무관하게 항상 같은
    /// 픽셀 수로 그려지므로 정규화 상수로는 원리상 피할 수 없다</b>("UI 제작 규칙" - 정비/상점
    /// 아이템 칸에서 이미 같은 결론을 냈다).</para>
    ///
    /// <para>그래서 앵커는 <b>배경 패널의 앵커를 그대로 복사</b>하고(둘 다 ShopPanel의 자식이라
    /// 기준이 같다), 안쪽으로 미는 양은 <see cref="UiSafeArea.GetBorderPixels"/>가 스프라이트에서
    /// 읽어온 <b>픽셀</b>로 준다. 나중에 배경 아트가 바뀌어도 이 코드는 고칠 필요가 없다.</para>
    /// </summary>
    /// <param name="background">이 격자가 올라앉는 씬의 배경 패널. null이면 패널 전체를 쓴다.</param>
    /// <param name="titleHeightRatio">제목 띠 높이(화면 높이 대비 비율). 글자 크기가
    /// <see cref="ResponsiveTextScaler"/>로 화면에 비례하므로 띠도 비례해야 한다 - 테두리와 달리
    /// 고정 픽셀로 두면 작은 창에서 띠만 두꺼워진다.</param>
    private static RectTransform EnsureGridContainer(RectTransform panel, string name, TextMeshProUGUI title,
                                                     RectTransform background, float titleHeightRatio)
    {
        Vector4 bezel = background != null
            ? UiSafeArea.GetBorderPixels(background.GetComponent<Image>())
            : Vector4.zero;

        const float pad = 6f;  // 테두리 선에 아슬아슬하게 닿지 않도록 한 번 더 띄운다
        float left = bezel.x + pad;
        float bottom = bezel.y + pad;
        float right = bezel.z + pad;
        float top = bezel.w + pad;

        Vector2 anchorMin = background != null ? background.anchorMin : Vector2.zero;
        Vector2 anchorMax = background != null ? background.anchorMax : Vector2.one;

        float titleHeight = Mathf.Max(1f, titleHeightRatio * panel.rect.height);
        const float titleGap = 4f;

        // <b>제목 글자는 격자보다 테두리 쪽으로 더 붙인다</b>(2026-08-26 사용자 지시: "상점에서
        // 글씨가 패널에 조금만 더 가까웠으면 좋겠어. 패널끝과 현재 위치의 중간 정도 위치로").
        // 테두리는 장식 베벨이라 글자가 그 위에 조금 얹혀도 읽히는 데 지장이 없다 - 상단 배너에서
        // 이미 쓰는 관례다(ModdingPanelUI.ApplyTextSafeArea 주석). 칸(아이콘)은 아트가 통째로
        // 잘려 보이므로 그대로 테두리 안쪽에 둔다.
        float titleLeft = left * 0.5f;
        float titleRight = right * 0.5f;
        float titleTop = top * 0.5f;

        if (title != null)
        {
            RectTransform t = title.rectTransform;
            // 제목은 배경 위쪽 앵커선 하나에 min/max를 모두 걸어 <b>offset이 곧 픽셀</b>이 되게 한다.
            // 그래야 테두리 두께(고정 픽셀)를 해상도와 무관하게 그대로 지킬 수 있다.
            t.anchorMin = new Vector2(anchorMin.x, anchorMax.y);
            t.anchorMax = new Vector2(anchorMax.x, anchorMax.y);
            t.offsetMin = new Vector2(titleLeft, -(titleTop + titleHeight));
            t.offsetMax = new Vector2(-titleRight, -titleTop);
            title.alignment = TextAlignmentOptions.Left;
            ItemCellUI.ApplyTextSizing(title, 30f);
            // 제목/보조 안내의 줄 경계는 문자열에서 명시한다. 자동 줄바꿈에 맡기면 영문
            // "[Equipped Weapons]"가 단어 사이에서 어색하게 쪼개진다.
            title.textWrappingMode = TextWrappingModes.NoWrap;
        }

        Transform existing = panel.Find(name);
        if (existing != null) DestroyImmediate(existing.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panel, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(left, bottom);
        // 격자 위쪽은 "테두리"와 "제목 아래" 중 더 아래쪽에 맞춘다 - 제목을 테두리 쪽으로 당겨도
        // (titleLeft/Top 참고) 격자가 제목 밑으로 파고들지 않게 한다.
        float gridTop = title != null ? Mathf.Max(top, titleTop + titleHeight + titleGap) : top;
        rect.offsetMax = new Vector2(-right, -gridTop);
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
            // "(Click for details)"는 2026-08-26 사용자 지시로 뺐다(DetailHint 주석 참고).
            // AI 코어 레벨·무게는 실제 정보라 남긴다.
            moddingStatusText.text = $"{Loc.T("modding.equipped_parts")}\n<size=75%>{Loc.T("modding.core_lv", RunState.CoreLevel)} · {Loc.T("modding.weight")} {weightLine}</size>";
        }

        // 아이콘은 지금 선택된 머리의 실제 아트다(2026-08-19 - 이전에는 Parts/Body 하드코딩).
        ItemCellUI.CreateIconCell(partsGrid, "Cell_Head", HeadSpriteLibrary.GetCurrentIcon(),
                                  ItemGrade.Normal, null, Loc.T("modding.head"), true, () => ShowDetail("head"));

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            PartData socketPart = default;
            bool has = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out socketPart);
            ItemGrade grade = has ? socketPart.grade : ItemGrade.Normal;

            ItemCellUI.CreateIconCell(partsGrid, $"Cell_SocketPart_{i}",
                                      has ? PartIconLibrary.Get(socketPart) : PartIconLibrary.Get(PartSlot.ArmWeaponSocket),
                                      grade, null, $"{Loc.T("shop.socket_n", i + 1)}", has,
                                      () => ShowDetail($"ws:{index}"));
        }

        foreach (PartSlot slot in PartSlotExtensions.DisplayOrder)
        {
            PartSlot captured = slot;
            PartData part = default;
            bool has = modding != null && modding.TryGetEquippedPart(slot, out part);
            ItemGrade grade = has ? part.grade : ItemGrade.Normal;

            ItemCellUI.CreateIconCell(partsGrid, $"Cell_Part_{slot}",
                                      has ? PartIconLibrary.Get(part) : PartIconLibrary.Get(slot),
                                      grade, null, slot.ToDisplayName(), has,
                                      () => ShowDetail($"p:{captured}"));
        }
    }

    // 5. 장착된 무기 - 소켓마다 손에 드는 무기 이미지를 아이콘으로 쓴다(무기 전용 아이콘은 없다).
    private void RefreshWeaponsGrid(PlayerShootManager shootManager, int socketCount)
    {
        if (weaponsGrid == null) return;

        // 열 수를 실제 소켓 수에 맞춘다. 예전엔 3열 고정이라 소켓이 2개인 기본 로봇에서
        // 오른쪽 1/3이 빈 칸으로 남았다(2026-08-26 사용자 지적: "위쪽 UI들이 엉망").
        //
        // <b>상한을 3에서 MaxWeaponSockets(6)로 올려 항상 한 줄로 만든다</b>(2026-08-26).
        // 3열 상한이면 소켓이 4개 이상인 로봇에서 2행이 되는데, 이 컨테이너는 높이가 고정이라
        // 행이 늘면 칸 높이가 절반이 된다. 실측(1366x768 · 소켓 6개): 칸 높이 37.3px →
        // 베젤을 뺀 안쪽 13.3px → 이름표 띠 11.3px인데 자동 크기 하한이 11.38pt여서
        // <b>"Socket 1~6"이 한 글자도 그려지지 않았다</b>(ItemCellLayout.CaptionHeight 및
        // ResponsiveTextScaler의 하한 보정 주석 참고). 한 줄로 두면 칸이 컨테이너 높이를
        // 그대로 쓰므로 이름표 띠가 26px 이상 확보돼 같은 창 크기에서도 전부 읽힌다.
        // 정비 화면의 머리+소켓 줄(headSocketRow)이 이미 쓰는 관례와 같다.
        int columns = Mathf.Clamp(socketCount, 1, MaxWeaponSockets);
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, socketCount) / (float)columns));

        // 정사각형(2026-08-26 사용자 지시). 가로/세로 중 작은 쪽에 맞추고 가운데로 모은다.
        ItemCellUI.EnsureGrid(weaponsGrid, new Vector2(100f, 100f), columns, rows, square: true);
        ItemCellUI.ClearChildren(weaponsGrid);

        // 개수 표기(2026-08-18 `UI 기획서.pdf` Phase C-4) - 디스크 줄의 "N/M"과 같은 형식으로
        // 맞춘다. 분모는 실제로 쓸 수 있는 소켓 수라 6소켓 로봇이면 저절로 "N/6"이 된다.
        int equippedWeapons = 0;
        if (shootManager != null)
        {
            for (int i = 0; i < socketCount; i++)
            {
                if (shootManager.TryGetSocketInfo(i, out _, out _)) equippedWeapons++;
            }
        }

        if (equippedWeaponsText != null)
        {
            // 2026-08-26 사용자 지시로 "(상세)" 안내를 뺐다 - 칸을 누르면 상세가 열린다는 것은
            // 이미 학습된 조작이라 매번 한 줄을 쓸 값어치가 없었고, 그 한 줄이 제목 띠를 두 배로
            // 만들어 <b>격자에서 세로 공간을 빼앗고 있었다</b>(작은 창에서 칸이 23px까지 눌린
            // 원인 중 하나). 교체 모드 안내는 "지금 무엇을 하는 중인지" 알려야 하므로 남긴다.
            string hint = weapon_swap_mode
                ? $"\n<size=70%><color=#FFD37A>({Loc.T("shop.swap.pick_hint")})</color></size>"
                : string.Empty;
            equippedWeaponsText.text = $"{Loc.T("modding.equipped_weapons")} · {equippedWeapons}/{socketCount}{hint}";
        }

        if (weapon_swap_label != null) weapon_swap_label.text = Loc.T(weapon_swap_mode ? "shop.swap.cancel" : "shop.swap");
        if (weapon_swap_button != null) weapon_swap_button.gameObject.SetActive(socketCount >= 2);

        if (shootManager == null) return;

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            bool has = shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade);

            // 무기를 안 낀 소켓은 완전히 빈 칸으로 두면 "칸이 왜 있지?" 싶으므로,
            // 빈 무기 슬롯 아이콘을 깔아 "여기에 무기를 낄 수 있다"는 것을 보여준다.
            // 2026-08-26 사용자 제공 아트로 교체(그 전에는 코드로 그린 임시 실루엣이었다).
            Sprite icon = has ? ResolveWeaponIcon(weapon) : PartIconLibrary.GetEmptyWeaponSlot();

            // 교체 모드에서는 빈 소켓도 눌러야 한다(그 자리로 옮기기). 고른 출발 칸은 노란색으로
            // 강조한다 - 정비 화면이 교체 가능한 슬롯을 노란색으로 여는 것과 같은 관례.
            // 등급색은 칸 아트가 갖고 있다 - 여기서는 교체 모드의 출발 칸 강조만 tint로 얹는다.
            Color? cellTint = (weapon_swap_mode && weapon_swap_source == i) ? SwapSelectedColor : (Color?)null;

            System.Action onClick = (weapon_swap_mode || has)
                ? (System.Action)(() => HandleWeaponCellClicked(index))
                : null;

            // 2026-08-26 사용자 지시: "소켓 기본적으로 다 정사각형 형태로, 소켓 번호만 오른쪽
            // 위로, 소켓 글자는 없이". 이름표를 번호 하나로 줄이고 우상단 배지로 보내면
            // 아이콘이 칸 안쪽을 전부 쓸 수 있다(무기 그림이 주인공인 칸이라 그 편이 맞다).
            // 번호는 서수라 번역이 필요 없어 Loc를 거치지 않는다.
            ItemCellUI.CreateIconCell(weaponsGrid, $"Cell_Weapon_{i}", icon,
                                      has ? grade : ItemGrade.Normal, cellTint,
                                      (i + 1).ToString(), has, onClick, cornerCaption: true);
        }
    }

    /// <summary>무기 위치 교체에서 고른 출발 칸의 강조색(정비 화면의 슬롯 강조와 같은 노란색).</summary>
    private static readonly Color SwapSelectedColor = new Color(0.95f, 0.75f, 0.15f, 1f);

    /// <summary>빈 칸을 흐리게 보이게 하는 tint(등급 아트를 어둡게 곱한다).</summary>
    private static readonly Color EmptyCellTint = new Color(0.55f, 0.55f, 0.55f, 1f);

    // 6. 장착된 디스크 - 슬롯 수만큼 칸을 만들고 낀 디스크만 아이콘을 채운다.
    private void RefreshDiscsGrid()
    {
        if (discsGrid == null) return;

        int slotCount = shopManager != null ? shopManager.DiscSlotCount : 0;
        int cellCount = Mathf.Max(slotCount, RunState.EquippedDiscIds.Count);
        // 3열로 두면 기본 6슬롯이 2행에 정확히 들어차 빈 칸이 남지 않는다(예전 4열에서는
        // 둘째 행에 2칸만 차고 오른쪽 절반이 비었다 - 2026-08-26 사용자 지적).
        int columns = 3;
        int rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, cellCount) / (float)columns));

        ItemCellUI.EnsureGrid(discsGrid, new Vector2(120f, 92f), columns, rows);
        ItemCellUI.ClearChildren(discsGrid);

        // 안내 줄("(Click for details)")은 2026-08-26 사용자 지시로 뺐다 - 제목이 한 줄이 되어
        // 그만큼 격자가 세로로 넓어진다(DetailHint 주석 참고).
        if (equippedDiscsText != null)
            equippedDiscsText.text = $"{Loc.T("modding.discs")} {RunState.EquippedDiscIds.Count}/{slotCount}";

        for (int i = 0; i < cellCount; i++)
        {
            // DiscData도 struct라 null을 못 쓴다(PartData와 같은 이유).
            DiscData disc = default;
            bool has = i < RunState.EquippedDiscIds.Count && TryFindDisc(RunState.EquippedDiscIds[i], out disc);

            if (has)
            {
                int discId = disc.discId;
                ItemCellUI.CreateIconCell(discsGrid, $"Cell_Disc_{i}", disc.LoadIcon(),
                                          disc.grade, null, null, true,
                                          () => ShowDetail($"d:{discId}"));
            }
            else
            {
                ItemCellUI.CreateIconCell(discsGrid, $"Cell_DiscEmpty_{i}", null, ItemGrade.Normal, EmptyCellTint, null, false, null);
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
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return Loc.T("common.none_paren");

        return Clickable($"p:{slot}",
            $"<color={part.grade.ToColorHex()}>{part.grade.ToDisplayName()}</color> {part.Part()}");
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
                    $"<color={socketPart.grade.ToColorHex()}>{socketPart.grade.ToDisplayName()}</color> {socketPart.Part()}")
                : Loc.T("common.none_paren");

            lines.Add($"{Loc.T("modding.weaponsocket_n", i + 1)}: {part}");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : $"{Loc.T("partslot.weaponsocket")}: {Loc.T("common.none_paren")}";
    }

    private string GetRobotName()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return Loc.T("common.unknown");

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.Robot()
            : $"ID {player.RobotId}";
    }

    // 7. 현재 능력치 (기획서 p.13의 10개 스탯)
    private void RefreshStats()
    {
        if (statsText == null) return;

        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null)
        {
            statsText.text = $"{Loc.T("stats.header")}\n{Loc.T("stats.no_player")}";
            return;
        }

        statsText.text =
            $"{Loc.T("stats.header")}\n" +
            // 2026-08-24 사용자 지정 표기 규칙(StatFormat 참고).
            $"{StatTypeNames.ToDisplayName(StatType.MaxHp)} {StatFormat.Int(player.CurrentHp)}/{StatFormat.Int(player.MaxHp)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Atk)} {StatFormat.Int(player.Atk)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Def)} {StatFormat.Int(player.Def)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritChance)} {StatFormat.Percent(player.Cc)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritDamage)} {StatFormat.RatioPercent(player.Cd)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.MoveSpeed)} {StatFormat.Decimal(player.MoveSpeed)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Avoid)} {StatFormat.Percent(player.Avoid)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Luck)} {StatFormat.Int(player.Luck)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Mass)} {StatFormat.Decimal(player.Mess)}";
    }

    // ─────────────────────────────────────────────────────────────────
    // 보유 장비 상세 보기 - 링크 태그 생성과 클릭 처리
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 목록 제목 옆에 붙이던 "클릭하면 상세를 볼 수 있다"는 안내.
    /// <b>2026-08-26 사용자 지시로 화면에서 뺐다</b>("(Click for Details) 이런 문구는 없애고
    /// 공간 넓혀") - 칸을 누르면 상세가 열린다는 것은 이미 학습된 조작이라 매번 한 줄을 쓸
    /// 값어치가 없었고, 그 한 줄이 제목 띠를 두 배로 만들어 <b>격자에서 세로 공간을 빼앗고
    /// 있었다</b>. 번역 키(<c>common.click_detail</c>)와 이 속성은 되돌리기 쉽도록 남겨 둔다.
    /// </summary>
    // 언어가 바뀌면 문구도 바뀌어야 하므로 const 가 아니라 조회식으로 둔다
    // (const 는 컴파일 타임에 굳어 번역이 안 붙는다).
    private static string DetailHint => $"<size=70%><color=#8FB8FF>({Loc.T("common.click_detail")})</color></size>";

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
            lines.Add($"{Loc.T("detail.category")}: {meta.weaponClass.ToDisplayName()} / {meta.type.ToDisplayName()}");
        }

        // 실제로 적에게 들어가는 1발 데미지 = weapon_atk + (로봇 공격력 / 발수). 치명타는 별도.
        float robotAtk = player != null ? player.Atk : 0f;
        float perShot = weapon.weapon_atk + robotAtk / weapon.ProjectileCount;
        float dps = perShot * weapon.ProjectileCount * weapon.weapon_atsp;

        lines.Add(Loc.T("detail.weapon.atk", weapon.weapon_atk.ToString("0.##"), robotAtk.ToString("0.##"), perShot.ToString("0.##")));
        lines.Add(Loc.T("detail.weapon.atsp", weapon.weapon_atsp.ToString("0.##"), dps.ToString("0.#")));

        // 사거리/감지거리는 값 하나만 보여준다(2026-08-12 사용자 요청 - "데이터값 → 실제값"
        // 두 개를 나란히 쓰던 것을 없앴다). 남긴 값은 소켓 파츠 배율까지 먹은 최종 적용값이라
        // 화면에 적힌 숫자가 곧 게임에서 나가는 거리다.
        lines.Add(Loc.T("detail.weapon.range", shootManager.GetEffectiveTravelRange(socketIndex).ToString("0.##")));
        lines.Add(Loc.T("detail.weapon.detect", shootManager.GetEffectiveDetectRange(socketIndex).ToString("0.##")));

        lines.Add($"{Loc.T("detail.weapon.firemode")}: {FireModeName(weapon.weapon_firemode)}");
        if (weapon.ProjectileCount > 1) lines.Add(Loc.T("detail.weapon.multishot", weapon.ProjectileCount, weapon.weapon_aim.ToString("0.##")));
        if (weapon.weapon_speed > 0f) lines.Add(Loc.T("detail.weapon.speed", weapon.ProjectileSpeed.ToString("0.##")));
        if (weapon.weapon_duration > 0f) lines.Add(Loc.T("detail.weapon.duration", weapon.weapon_duration.ToString("0.##")));
        if (weapon.weapon_splash > 0f) lines.Add(Loc.T("detail.weapon.splash", weapon.weapon_splash.ToString("0.##")));
        if (weapon.weapon_pierce != 0)
        {
            string pierce = weapon.weapon_pierce < 0 ? Loc.T("common.unlimited") : Loc.T("common.times", weapon.weapon_pierce);
            if (weapon.weapon_pierce_chance > 0f && weapon.weapon_pierce_chance < 1f)
            {
                pierce += $" ({Loc.T("detail.weapon.pierce_chance", (weapon.weapon_pierce_chance * 100f).ToString("0"))})";
            }
            lines.Add(Loc.T("detail.weapon.pierce", pierce));
        }
        if (weapon.weapon_defignore > 0f) lines.Add(Loc.T("detail.weapon.defignore", (weapon.weapon_defignore * 100f).ToString("0")));
        if (weapon.weapon_knockback > 0f) lines.Add(Loc.T("detail.weapon.knockback", weapon.weapon_knockback.ToString("0.##")));
        lines.Add(Loc.T("detail.weapon.rotspeed", weapon.RotationSpeed.ToString("0.#")));

        // 무게는 소켓 타입이 안 맞으면 배율이 붙는다(장착은 되지만 이동속도가 깎인다).
        if (modding != null)
        {
            float baseWeight = modding.GetWeaponWeight(weapon.weapon_id);
            float effective = modding.GetEffectiveWeaponWeight(socketIndex, weapon.weapon_id);
            lines.Add(Mathf.Approximately(baseWeight, effective)
                ? Loc.T("detail.weight", baseWeight.ToString("0.##"))
                : $"{Loc.T("detail.weight", baseWeight.ToString("0.##"))} → <color=#F2BF26>{effective:0.##} ({Loc.T("detail.weight.mismatch", modding.MismatchWeightMultiplier.ToString("0.##"))})</color>");
        }

        detail_popup.Show(
            $"{Loc.T("shop.socket_n", socketIndex + 1)} · <color={grade.ToColorHex()}>{grade.ToDisplayName()}</color> {weapon.Weapon()}",
            string.Join("\n", lines),
            ResolveWeaponIcon(weapon));
    }

    // WeaponFireMode에는 한글 이름 확장이 없어서(다른 enum들과 달리 UI에 쓰인 적이 없다) 여기서 변환한다.
    private static string FireModeName(WeaponFireMode mode)
    {
        switch (mode)
        {
            case WeaponFireMode.Projectile: return Loc.T("firemode.projectile");
            case WeaponFireMode.Beam: return Loc.T("firemode.beam");
            case WeaponFireMode.MeleeSwing: return Loc.T("firemode.melee");
            default: return mode.ToString();
        }
    }

    private void ShowWeaponSocketPartDetail(int socketIndex)
    {
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        if (modding == null || !modding.TryGetEquippedWeaponSocketPart(socketIndex, out PartData part)) return;

        var lines = new List<string>
        {
            $"{Loc.T("detail.slot")}: {Loc.T("modding.weaponsocket_n", socketIndex + 1)}",
            $"{Loc.T("detail.allowed_category")}: {(part.restrictsWeaponType ? part.allowedWeaponType.ToDisplayName() : Loc.T("common.all_generic"))}"
        };

        // 2026-08-20 소켓 명세 - 등급 효과가 사거리/감지/회전 배율에서 아래 5종으로 바뀌었다.
        if (part.socketAttackSpeedPercent != 0f) lines.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{part.socketAttackSpeedPercent:0.##}%");
        if (part.socketDamageFlat != 0f) lines.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{part.socketDamageFlat:0.##}");
        if (part.socketDamagePercent != 0f) lines.Add($"{StatTypeNames.ToDisplayName(StatType.Atk)} +{part.socketDamagePercent:0.##}%");
        if (part.socketCritChancePercent != 0f) lines.Add($"{StatTypeNames.ToDisplayName(StatType.CritChance)} +{part.socketCritChancePercent:0.##}%");
        if (part.socketSplashPercent != 0f) lines.Add($"{Loc.T("detail.splash_radius")} +{part.socketSplashPercent:0.##}%");
        if (part.socketDefIgnorePercent != 0f) lines.Add($"{Loc.T("detail.defignore")} +{part.socketDefIgnorePercent:0.##}%p");

        if (part.weight != 0f) lines.Add(Loc.T("detail.weight", part.weight.ToString("0.##")));

        // 이 소켓에 실제로 낀 무기와 타입이 맞는지까지 같이 보여준다(불일치면 무게 배율이 붙는다).
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager != null && shootManager.TryGetSocketInfo(socketIndex, out WeaponData weapon, out _))
        {
            lines.Add(modding.IsWeaponMismatched(socketIndex, weapon.weapon_id)
                ? $"<color=#F2BF26>{Loc.T("detail.equipped_mismatch", weapon.Weapon(), modding.MismatchWeightMultiplier.ToString("0.##"))}</color>"
                : Loc.T("detail.equipped_match", weapon.Weapon()));
        }

        detail_popup.Show(
            $"<color={part.grade.ToColorHex()}>{part.grade.ToDisplayName()}</color> {part.Part()}",
            string.Join("\n", lines),
            null);
    }

    private void ShowPartDetail(PartSlot slot)
    {
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return;

        // 2026-08-20 파츠가 효과를 2개 이상 갖게 되면서 항목을 여기서 하나하나 나열하는 방식을
        // 버렸다 - PartData.BuildDescription()이 데이터에서 매번 생성하므로 화면과 실제 값이
        // 어긋날 수 없다(정비 화면도 같은 함수를 쓴다).
        var lines = new List<string>
        {
            $"{Loc.T("detail.slot")}: {slot.ToDisplayName()}",
            part.BuildDescription()
        };

        // 무게는 개별 파츠만 봐서는 감이 안 오므로 로봇 전체 합계를 함께 보여준다.
        float total = modding.GetTotalWeight();
        float capacity = modding.GetTotalWeightCapacity();
        float over = Mathf.Max(0f, total - capacity);
        lines.Add($"\n{Loc.T("detail.total_weight", total.ToString("0.##"), capacity.ToString("0.##"))}");
        if (over > 0f)
        {
            lines.Add($"<color=#F2BF26>{Loc.T("detail.overweight", over.ToString("0.##"), (over * modding.OverweightSpeedPenaltyPerUnit).ToString("0.##"))}</color>");
        }

        detail_popup.Show(
            $"<color={part.grade.ToColorHex()}>{part.grade.ToDisplayName()}</color> {part.Part()}",
            string.Join("\n", lines),
            PartIconLibrary.Get(part));
    }

    private void ShowDiscDetail(int discId)
    {
        if (shopManager == null || shopManager.Catalog == null) return;

        foreach (DiscData disc in shopManager.Catalog.Discs)
        {
            if (disc.discId != discId) continue;

            var lines = new List<string>
            {
                $"{Loc.T("detail.category")}: {Loc.T("detail.category.disc")}",
                disc.BuildDescription()
            };

            // 기획서 21종은 효과 종류가 제각각이라(처치 시 발동·시간제·확률형…) 실제 파라미터를
            // 값이 들어있는 것만 골라 덧붙인다.
            var numbers = new List<string>();
            if (disc.chance01 > 0f) numbers.Add($"{Loc.T("detail.disc.chance")} {disc.chance01 * 100f:0.##}%");
            if (disc.flatValue != 0f) numbers.Add($"{Loc.T("detail.disc.value")} {disc.flatValue:0.##}");
            if (disc.multiplier != 0f) numbers.Add($"{Loc.T("detail.disc.multiplier")} x{disc.multiplier:0.##}");
            if (disc.duration > 0f) numbers.Add(Loc.T("detail.disc.duration", disc.duration.ToString("0.##")));
            if (disc.interval > 0f) numbers.Add(Loc.T("detail.disc.interval", disc.interval.ToString("0.##")));
            if (disc.radius > 0f) numbers.Add($"{Loc.T("detail.disc.radius")} {disc.radius:0.##}");
            if (disc.cap > 0f) numbers.Add($"{Loc.T("detail.disc.cap")} {disc.cap:0.##}");
            if (disc.maxUses > 0) numbers.Add(Loc.T("detail.disc.maxuses", disc.maxUses));
            if (numbers.Count > 0) lines.Add(string.Join(" · ", numbers));

            AppendDiscStat(lines, disc, disc.statA, disc.amountA, isStackStat: true);
            AppendDiscStat(lines, disc, disc.statB, disc.amountB);
            AppendDiscStat(lines, disc, disc.statC, disc.amountC);

            detail_popup.Show(
                $"<color={disc.grade.ToColorHex()}>{disc.grade.ToDisplayName()}</color> {disc.Disc()}",
                string.Join("\n", lines),
                disc.LoadIcon());
            return;
        }
    }

    /// <summary>
    /// 디스크 상세 팝업 아래에 붙는 스탯 한 줄.
    ///
    /// <b>누적형(OnKillStackStat) 디스크는 "지금까지 누적된 총량"을 앞세운다</b>(2026-08-20 사용자 지적).
    /// 예전에는 데이터의 <c>amountA</c>를 그대로 찍어서, 처치당 증가량인 <c>공격력 +0.05</c>만 보이고
    /// "실제로 총 얼마나 올랐는지"는 어디에도 없었다 - 사용자가 "저 숫자는 왜 있는지 모르겠다"고 한 것이
    /// 이것이다. 누적치는 <see cref="RunState.DiscStackProgress"/>가 이미 들고 있으므로 그것을 읽는다.
    /// </summary>
    private static void AppendDiscStat(List<string> lines, DiscData disc, StatType stat, float amount,
                                       bool isStackStat = false)
    {
        if (amount == 0f) return;

        if (isStackStat && disc.effectType == DiscEffectType.OnKillStackStat)
        {
            RunState.DiscStackProgress.TryGetValue(disc.discId, out float progress);

            int copies = 0;
            foreach (int id in RunState.EquippedDiscIds)
            {
                if (id == disc.discId) copies++;
            }

            // 장 수만큼 상한도 함께 늘어난다(DiscData.BuildDescription/ApplyKillStack과 같은 규칙).
            float totalCap = disc.cap * Mathf.Max(1, copies);

            lines.Add($"<color=#88FF88>{StatTypeNames.ToDisplayName(stat)} +{progress:0.##}</color>" +
                      $"<size=80%><color=#9AA3AB> ({Loc.T("detail.disc.perkill", amount.ToString("0.###"), totalCap.ToString("0.##"))})</color></size>");
            return;
        }

        string sign = amount > 0f ? "+" : string.Empty;
        string color = amount > 0f ? "#88FF88" : "#FF8080";
        lines.Add($"<color={color}>{StatTypeNames.ToDisplayName(stat)} {sign}{amount:0.##}</color>");
    }

    private void ShowRobotDetail()
    {
        if (player == null) player = FindFirstObjectByType<PlayerRobotController>();
        if (player == null || GameDataManager.Instance == null) return;
        if (!GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)) return;

        ModdingManager modding = FindFirstObjectByType<ModdingManager>();

        var lines = new List<string>
        {
            $"{Loc.T("detail.category")}: {Loc.T("detail.category.robot")}",
            Loc.T("detail.robot.base1", data.robot_hp, data.robot_atk, data.robot_def),
            Loc.T("detail.robot.base2", data.robot_speed.ToString("0.##"), data.robot_avoid.ToString("0.##"), data.robot_luck.ToString("0.##")),
            Loc.T("detail.robot.crit", data.robot_cc.ToString("0.##"), data.robot_cd.ToString("0.##")),
            $"{StatTypeNames.ToDisplayName(StatType.Mass)} {data.robot_mess:0.##}"
        };

        if (modding != null)
        {
            lines.Add($"\n{Loc.T("detail.robot.sockets", modding.ActiveSocketCount, modding.DiscSlotCount)}");
            lines.Add(Loc.T("detail.robot.capacity", RunState.UnopenedPartBoxCount, modding.PartBoxCapacity));
        }

        detail_popup.Show(data.Robot(), string.Join("\n", lines), null);
    }
}
