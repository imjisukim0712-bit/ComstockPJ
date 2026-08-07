using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 화면 표시/입력 담당(로직은 ShopManager). 기획서 p.13의 8개 요소를 모두 채운다.
///  1. 현재 웨이브        2. 골드            3. 다음 웨이브 시작 버튼
///  4. 로봇 모딩 상태     5. 장착된 무기     6. 장착된 디스크
///  7. 현재 능력치        8. 상점 품목 4칸 + 새로고침(비용) + 개별 잠금
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
    [SerializeField] private Button[] socketButtons = new Button[2];
    [SerializeField] private TextMeshProUGUI[] socketButtonTexts = new TextMeshProUGUI[2];
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
            SetMessage($"골드가 부족합니다 (새로고침 {shopManager.CurrentRefreshCost}골드)");
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

            // 무기 타입 제한/무게 제한 때문에 이 소켓엔 못 넣는 경우 버튼을 비활성화하고 이유를 보여준다.
            bool allowed = shopManager.CanPurchaseWeaponIntoSocket(offerIndex, i, out string reason);
            if (socketButtons[i] != null) socketButtons[i].interactable = allowed;

            socketButtonTexts[i].text = allowed
                ? $"소켓 {i + 1}\n{current}"
                : $"소켓 {i + 1}\n{current}\n<color=#FF8080>{reason}</color>";
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
        if (waveText != null) waveText.text = $"웨이브 {RunState.WaveNumber}";
        if (goldText != null) goldText.text = $"골드 {RunState.Gold}";

        if (refreshText != null && shopManager != null)
        {
            refreshText.text = $"새로고침 ({shopManager.CurrentRefreshCost}골드)";
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

    private void RefreshEquipment()
    {
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();

        // 5. 장착된 무기 (소켓 개수는 머리 파츠가 정해야 하지만 지금은 인스펙터 설정 그대로 - 알려진 이슈)
        if (equippedWeaponsText != null)
        {
            PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
            var lines = new List<string> { "[장착 무기]" };

            if (shootManager == null)
            {
                lines.Add("(무기 정보를 찾을 수 없음)");
            }
            else
            {
                for (int i = 0; i < shootManager.SocketCount; i++)
                {
                    if (shootManager.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade))
                    {
                        lines.Add($"소켓 {i + 1}: <color={grade.ToColorHex()}>{grade.ToKorean()}</color> {weapon.weapon_name}");
                    }
                    else
                    {
                        lines.Add($"소켓 {i + 1}: (비어 있음)");
                    }
                }
            }

            equippedWeaponsText.text = string.Join("\n", lines);
        }

        // 6. 장착된 디스크
        if (equippedDiscsText != null)
        {
            int slotCount = shopManager != null ? shopManager.DiscSlotCount : 0;
            var lines = new List<string> { $"[디스크] {RunState.EquippedDiscIds.Count}/{slotCount}" };

            if (RunState.EquippedDiscIds.Count == 0)
            {
                lines.Add("(없음)");
            }
            else if (shopManager != null && shopManager.Catalog != null)
            {
                foreach (int discId in RunState.EquippedDiscIds)
                {
                    string name = discId.ToString();
                    foreach (DiscData disc in shopManager.Catalog.Discs)
                    {
                        if (disc.discId != discId) continue;
                        name = $"<color={disc.grade.ToColorHex()}>{disc.grade.ToKorean()}</color> {disc.discName}";
                        break;
                    }
                    lines.Add(name);
                }
            }

            equippedDiscsText.text = string.Join("\n", lines);
        }

        // 4. 로봇 모딩 상태 (조회 전용) - 팔/다리 6부위는 Phase 4에서 ModdingManager로부터 실제 값을 가져온다
        if (moddingStatusText != null)
        {
            int discSlots = shopManager != null ? shopManager.DiscSlotCount : 0;
            int socketCount = 0;
            PlayerShootManager shootManager = FindFirstObjectByType<PlayerShootManager>();
            if (shootManager != null) socketCount = shootManager.SocketCount;

            moddingStatusText.text =
                "[로봇 모딩 상태]\n" +
                $"헤드: {GetRobotName()}\n" +
                "헬멧: 기본\n" +
                $"메모리 카드: AI 코어 Lv {RunState.CoreLevel}\n" +
                $"무기 소켓: {socketCount}칸 ({PartLine(modding, PartSlot.ArmWeaponSocket)})\n" +
                $"팔 장갑: {PartLine(modding, PartSlot.ArmArmor)}\n" +
                $"디스크 슬롯: {discSlots}칸\n" +
                "필살기 슬롯: 1칸 (데모 범위 밖)\n" +
                $"자기장 코어: {PartLine(modding, PartSlot.MagneticCore)}\n" +
                $"다리: {PartLine(modding, PartSlot.Leg)}\n" +
                $"다리 장갑: {PartLine(modding, PartSlot.LegArmor)}\n" +
                $"발: {PartLine(modding, PartSlot.Foot)}\n" +
                $"무게 지탱 {(modding != null ? modding.GetTotalWeight() : 0f):0.#} / " +
                $"{(modding != null ? modding.GetTotalWeightCapacity() : 0f):0.#}";
        }
    }

    // 파츠 하나를 "등급 이름 (설명)" 형태로 요약한다.
    private static string PartLine(ModdingManager modding, PartSlot slot)
    {
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return "(없음)";

        return $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName}";
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
}
