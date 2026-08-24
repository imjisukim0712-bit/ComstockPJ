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

        if (offer.IsAccessory)
        {
            // 악세사리는 소켓 선택도, 구매 완료 잠금도 없다 - 즉시 사고 즉시 다시 살 수 있다
            // (ShopManager.TryPurchaseAccessory 참고).
            if (shopManager.TryPurchaseAccessory(index)) SetMessage($"{offer.DisplayName} 구매 완료 (점수 +{offer.Accessory.score})");
            Refresh();
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
            SetMessage("무기를 장착할 대상을 찾을 수 없습니다");
            return;
        }

        pendingWeaponOfferIndex = offerIndex;

        ShopManager.Offer offer = shopManager.Offers[offerIndex];
        if (socketPickerTitleText != null)
        {
            socketPickerTitleText.text = $"'{offer.DisplayName}'({offer.Grade.ToKorean()})을(를) 어느 소켓에 장착할까요?";
        }

        EnsureSocketButtons(shootManager.SocketCount);

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
            // 엔드리스 모드(2026-08-19)에서는 20이 더 이상 진짜 끝이 아니므로 분모를 숨긴다.
            waveText.text = RunState.IsEndless
                ? $"WAVE {RunState.WaveNumber:00} / 무한"
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
            refreshText.text = $"상점 초기화 - {shopManager.CurrentRefreshCost}";
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

            // ② 종류 - 기획서 표기 형식: "전설 · 무기"
            if (ui.headerText != null)
            {
                ui.headerText.text = $"<color={offer.Grade.ToColorHex()}>{offer.Grade.ToKorean()}</color> · {offer.CategoryName}";
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
                decor.priceText.text = offer.Purchased ? "구매함" : offer.Price.ToString();
                decor.priceText.color = offer.Purchased ? PurchasedGray : Color.white;
            }
            if (decor.priceCoin != null) decor.priceCoin.enabled = !offer.Purchased && decor.priceCoin.sprite != null;

            // 구매 완료 스탬프 / 잠금 강조
            if (decor.stamp != null) decor.stamp.SetActive(offer.Purchased);
            if (decor.lockGlow != null) decor.lockGlow.SetActive(offer.Locked && !offer.Purchased);
            if (decor.lockIcon != null) decor.lockIcon.color = offer.Locked ? LockYellow : Color.white;

            if (ui.cardButton != null) ui.cardButton.interactable = !offer.Purchased;
            if (ui.lockButton != null) ui.lockButton.interactable = !offer.Purchased;
            if (ui.lockText != null) ui.lockText.text = offer.Locked ? "잠금 해제" : "잠금";
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

        // 씬의 BodyText를 "능력치" 자리로 좁힌다. 이 파일의 EnsureGridContainer가 보유 목록
        // 제목 텍스트를 같은 방식으로 옮기고 있어서 새로운 수법은 아니다.
        if (offerSlots[index].bodyText != null)
        {
            RectTransform body = offerSlots[index].bodyText.rectTransform;
            body.anchorMin = new Vector2(0.05f, 0.26f);
            body.anchorMax = new Vector2(0.95f, 0.54f);
            body.offsetMin = Vector2.zero;
            body.offsetMax = Vector2.zero;
            offerSlots[index].bodyText.alignment = TextAlignmentOptions.TopLeft;
            ItemCellUI.ApplyTextSizing(offerSlots[index].bodyText, 20f);
        }

        decor.nameText = MakeText(root, "NameText", 0.05f, 0.55f, 0.95f, 0.70f, 30f, TextAlignmentOptions.Left);

        // 가격 박스 - 카드 안쪽 박스라 아이콘 칸(IconImage_BG)과 같은 아트를 써서 톤을 맞춘다.
        RectTransform priceBox = MakeChild(root, "PriceBox", 0.05f, 0.05f, 0.95f, 0.24f,
                                           typeof(CanvasRenderer), typeof(Image));
        var boxImage = priceBox.GetComponent<Image>();
        Sprite boxSprite = Resources.Load<Sprite>("UI/Black_ui04");
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
        boxImage.raycastTarget = false;

        RectTransform coin = MakeChild(priceBox, "Coin", 0.03f, 0.14f, 0.20f, 0.86f,
                                        typeof(CanvasRenderer), typeof(Image));
        decor.priceCoin = coin.GetComponent<Image>();
        decor.priceCoin.sprite = Resources.Load<Sprite>("Gold"); // 헤더 골드 = 인게임 드랍 금화와 같은 코인
        decor.priceCoin.preserveAspect = true;
        decor.priceCoin.raycastTarget = false;
        decor.priceCoin.enabled = decor.priceCoin.sprite != null;

        decor.priceText = MakeText(priceBox, "PriceText", 0.23f, 0.05f, 0.97f, 0.95f, 28f,
                                    TextAlignmentOptions.Left);

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
        stampText.text = "구매 완료";
        stampText.color = StampRed;
        stampText.fontStyle = FontStyles.Bold;

        decor.stamp = stamp.gameObject;
        stamp.gameObject.SetActive(false);

        // 잠금 버튼에 자물쇠 아이콘을 붙이고 글자 자리를 오른쪽으로 밀어 준다.
        Button lockButton = offerSlots[index].lockButton;
        if (lockButton != null)
        {
            var lockRoot = (RectTransform)lockButton.transform;
            DestroyIfExists(lockRoot, "LockIcon");

            if (offerSlots[index].lockText != null)
            {
                RectTransform label = offerSlots[index].lockText.rectTransform;
                label.anchorMin = new Vector2(0.30f, 0f);
                label.anchorMax = new Vector2(1f, 1f);
                label.offsetMin = Vector2.zero;
                label.offsetMax = Vector2.zero;
            }

            RectTransform icon = MakeChild(lockRoot, "LockIcon", 0.06f, 0.14f, 0.26f, 0.86f,
                                            typeof(CanvasRenderer), typeof(Image));
            decor.lockIcon = icon.GetComponent<Image>();
            decor.lockIcon.sprite = UiIconLibrary.Lock();
            decor.lockIcon.preserveAspect = true;
            decor.lockIcon.raycastTarget = false;
        }

        return decor;
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

        partsGrid = EnsureGridContainer(panel, "PartsGrid", moddingStatusText,
                                        0.02f, 0.44f, 0.30f, 0.88f, 0.825f);
        weaponsGrid = EnsureGridContainer(panel, "WeaponsGrid", equippedWeaponsText,
                                          0.32f, 0.68f, 0.60f, 0.88f, 0.825f);
        discsGrid = EnsureGridContainer(panel, "DiscsGrid", equippedDiscsText,
                                        0.32f, 0.44f, 0.60f, 0.66f, 0.605f);

        // 장착 무기 제목 줄의 오른쪽 끝에 "위치 교체" 버튼이 들어가므로 제목 영역을 그만큼 좁힌다
        // (안 좁히면 제목 글자와 버튼이 겹친다 - 실측 캡처로 확인).
        if (equippedWeaponsText != null)
        {
            RectTransform titleRect = equippedWeaponsText.rectTransform;
            titleRect.anchorMax = new Vector2(0.495f, titleRect.anchorMax.y);
        }

        EnsureWeaponSwapButton(panel);
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

    private void EnsureWeaponSwapButton(RectTransform panel)
    {
        if (weapon_swap_button != null) return;

        DestroyIfExists(panel, "WeaponSwapButton");

        RectTransform rect = MakeChild(panel, "WeaponSwapButton", 0.500f, 0.828f, 0.600f, 0.880f,
                                       typeof(CanvasRenderer), typeof(Image), typeof(Button));

        var image = rect.GetComponent<Image>();
        Sprite plate = Resources.Load<Sprite>("UI/Purple_button00");
        if (plate != null)
        {
            image.sprite = plate;
            image.type = Image.Type.Sliced; // UI 아트는 전부 9-슬라이스다(프로젝트 안내.md 참고)
        }
        image.color = Color.white;

        weapon_swap_label = MakeText(rect, "Label", 0.05f, 0.05f, 0.95f, 0.95f, 20f, TextAlignmentOptions.Center);
        weapon_swap_label.text = "위치 교체";

        weapon_swap_button = rect.GetComponent<Button>();
        weapon_swap_button.onClick.AddListener(ToggleWeaponSwapMode);
    }

    private void ToggleWeaponSwapMode()
    {
        weapon_swap_mode = !weapon_swap_mode;
        weapon_swap_source = -1;

        SetMessage(weapon_swap_mode
            ? "무기 위치 교체: 옮길 무기 칸을 누르고, 이어서 바꿀 소켓 칸을 누르세요."
            : "무기 위치 교체를 취소했습니다.");

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
                SetMessage($"소켓 {socketIndex + 1}은(는) 비어 있어 옮길 무기가 없습니다.");
                return;
            }

            weapon_swap_source = socketIndex;
            SetMessage($"소켓 {socketIndex + 1}의 무기를 고름 - 바꿀 소켓 칸을 누르세요(다시 누르면 취소).");
            RefreshEquipment();
            return;
        }

        if (weapon_swap_source == socketIndex)
        {
            weapon_swap_source = -1;
            SetMessage("선택을 취소했습니다. 옮길 무기 칸을 다시 누르세요.");
            RefreshEquipment();
            return;
        }

        int from = weapon_swap_source;
        weapon_swap_source = -1;

        if (shootManager.SwapWeapons(from, socketIndex))
        {
            weapon_swap_mode = false;
            SetMessage($"소켓 {from + 1} ↔ 소켓 {socketIndex + 1} 무기 위치를 바꿨습니다.");
        }
        else
        {
            SetMessage("무기 위치를 바꾸지 못했습니다.");
        }

        RefreshEquipment();
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

        // 아이콘은 지금 선택된 머리의 실제 아트다(2026-08-19 - 이전에는 Parts/Body 하드코딩).
        ItemCellUI.CreateIconCell(partsGrid, "Cell_Head", HeadSpriteLibrary.GetCurrentIcon(),
                                  new Color(0.16f, 0.17f, 0.19f, 1f), "머리", true, () => ShowDetail("head"));

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            PartData socketPart = default;
            bool has = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out socketPart);
            ItemGrade grade = has ? socketPart.grade : ItemGrade.Normal;

            ItemCellUI.CreateIconCell(partsGrid, $"Cell_SocketPart_{i}",
                                      has ? PartIconLibrary.Get(socketPart) : PartIconLibrary.Get(PartSlot.ArmWeaponSocket),
                                      grade.ToCellColor(CellPlainColor), $"소켓 {i + 1}", has,
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
            // 제목 영역이 "위치 교체" 버튼 자리만큼 좁아졌으므로 이 줄만 짧은 안내를 쓴다.
            // 교체 모드에서는 문구를 바꿔 "지금 무엇을 하는 중인지" 제목 줄에서 바로 보이게 한다.
            string hint = weapon_swap_mode
                ? "<size=70%><color=#FFD37A>(교체 선택)</color></size>"
                : "<size=70%><color=#8FB8FF>(상세)</color></size>";
            equippedWeaponsText.text = $"[장착 무기] {equippedWeapons}/{socketCount} {hint}";
        }

        if (weapon_swap_label != null) weapon_swap_label.text = weapon_swap_mode ? "교체 취소" : "위치 교체";
        if (weapon_swap_button != null) weapon_swap_button.gameObject.SetActive(socketCount >= 2);

        if (shootManager == null) return;

        for (int i = 0; i < socketCount; i++)
        {
            int index = i;
            bool has = shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade);

            // 무기를 안 낀 소켓은 완전히 빈 칸으로 두면 "칸이 왜 있지?" 싶으므로,
            // 무기 소켓 아이콘을 흐리게 깔아 "여기에 무기를 낄 수 있다"는 것을 보여준다.
            Sprite icon = has ? ResolveWeaponIcon(weapon) : PartIconLibrary.Get(PartSlot.ArmWeaponSocket);

            // 교체 모드에서는 빈 소켓도 눌러야 한다(그 자리로 옮기기). 고른 출발 칸은 노란색으로
            // 강조한다 - 정비 화면이 교체 가능한 슬롯을 노란색으로 여는 것과 같은 관례.
            Color cellColor = has ? grade.ToCellColor(CellPlainColor) : CellPlainColor;
            if (weapon_swap_mode && weapon_swap_source == i) cellColor = SwapSelectedColor;

            System.Action onClick = (weapon_swap_mode || has)
                ? (System.Action)(() => HandleWeaponCellClicked(index))
                : null;

            ItemCellUI.CreateIconCell(weaponsGrid, $"Cell_Weapon_{i}", icon, cellColor,
                                      $"소켓 {i + 1}", has, onClick);
        }
    }

    /// <summary>무기 위치 교체에서 고른 출발 칸의 강조색(정비 화면의 슬롯 강조와 같은 노란색).</summary>
    private static readonly Color SwapSelectedColor = new Color(0.95f, 0.75f, 0.15f, 1f);

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
            // 2026-08-24 사용자 지정 표기 규칙(StatFormat 참고).
            $"체력 {StatFormat.Int(player.CurrentHp)}/{StatFormat.Int(player.MaxHp)}\n" +
            $"공격력 {StatFormat.Int(player.Atk)}\n" +
            $"방어력 {StatFormat.Int(player.Def)}\n" +
            $"치명타 확률 {StatFormat.Percent(player.Cc)}\n" +
            $"치명타 피해 {StatFormat.RatioPercent(player.Cd)}\n" +
            $"이동속도 {StatFormat.Decimal(player.MoveSpeed)}\n" +
            $"회피율 {StatFormat.Percent(player.Avoid)}\n" +
            $"행운 {StatFormat.Int(player.Luck)}\n" +
            $"질량 {StatFormat.Decimal(player.Mess)}";
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
            $"장착 가능 카테고리: {(part.restrictsWeaponType ? part.allowedWeaponType.ToKorean() : "전체(범용)")}"
        };

        // 2026-08-20 소켓 명세 - 등급 효과가 사거리/감지/회전 배율에서 아래 5종으로 바뀌었다.
        if (part.socketAttackSpeedPercent != 0f) lines.Add($"공격 속도 +{part.socketAttackSpeedPercent:0.##}%");
        if (part.socketDamageFlat != 0f) lines.Add($"공격력 +{part.socketDamageFlat:0.##}");
        if (part.socketDamagePercent != 0f) lines.Add($"공격력 +{part.socketDamagePercent:0.##}%");
        if (part.socketCritChancePercent != 0f) lines.Add($"치명타 확률 +{part.socketCritChancePercent:0.##}%");
        if (part.socketSplashPercent != 0f) lines.Add($"스플래시 범위 +{part.socketSplashPercent:0.##}%");
        if (part.socketDefIgnorePercent != 0f) lines.Add($"방어력 무시 +{part.socketDefIgnorePercent:0.##}%p");

        if (part.weight != 0f) lines.Add($"무게 {part.weight:0.##}");

        // 이 소켓에 실제로 낀 무기와 타입이 맞는지까지 같이 보여준다(불일치면 무게 배율이 붙는다).
        PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
        if (shootManager != null && shootManager.TryGetSocketInfo(socketIndex, out WeaponData weapon, out _))
        {
            lines.Add(modding.IsWeaponMismatched(socketIndex, weapon.weapon_id)
                ? $"<color=#F2BF26>현재 장착 '{weapon.weapon_name}' - 카테고리 불일치 (무게 x{modding.MismatchWeightMultiplier:0.##}, 소켓 보정 없음)</color>"
                : $"현재 장착 '{weapon.weapon_name}' - 카테고리 일치");
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

        // 2026-08-20 파츠가 효과를 2개 이상 갖게 되면서 항목을 여기서 하나하나 나열하는 방식을
        // 버렸다 - PartData.BuildDescription()이 데이터에서 매번 생성하므로 화면과 실제 값이
        // 어긋날 수 없다(정비 화면도 같은 함수를 쓴다).
        var lines = new List<string>
        {
            $"부위: {slot.ToKorean()}",
            part.BuildDescription()
        };

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

            AppendDiscStat(lines, disc, disc.statA, disc.amountA, isStackStat: true);
            AppendDiscStat(lines, disc, disc.statB, disc.amountB);
            AppendDiscStat(lines, disc, disc.statC, disc.amountC);

            detail_popup.Show(
                $"<color={disc.grade.ToColorHex()}>{disc.grade.ToKorean()}</color> {disc.discName}",
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

            lines.Add($"<color=#88FF88>{StatTypeNames.ToKorean(stat)} +{progress:0.##}</color>" +
                      $"<size=80%><color=#9AA3AB> (처치당 +{amount:0.###} · 최대 +{totalCap:0.##})</color></size>");
            return;
        }

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
