using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ESC 일시정지 메뉴(기획서 p.3 "일시정지 / 설정 메뉴"). 씬에 배치하지 않고 GameFlowManager가
/// Canvas 밑에 코드로 붙인다(<c>EquipmentDetailPopup</c>·<c>AiCoreExtraButtonsUI</c>와 같은 방식).
///
/// ESC로 열고 닫는다. <b>Time.timeScale은 열기 직전 값을 그대로 기억했다가 닫을 때 되돌린다</b> -
/// `GameFlowManager.IsIntermission`을 보고 0/1 중 하나로 판단하는 대신, 실제 값을 저장해 두면
/// 앞으로 timeScale을 다른 값(예: 슬로우 모션 연출)으로 쓰게 되어도 안전하다. 다만 일시정지는
/// <see cref="GameFlowManager.CurrentState"/>가 Combat일 때만 열리도록 막아서(정비/상점 화면은
/// 이미 그 자체로 전체 화면 UI이자 timeScale=0 상태다) 지금은 항상 1을 저장하고 1로 복귀한다.
///
/// 랭킹·도감 버튼은 사용자 확정(2026-08-18: "버튼만 배치, 비활성")에 따라 눌러도 아무 동작이
/// 없다 - 화면 기획과 데이터 설계가 아직 없어서 이번 범위 밖이다.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    private const string TitleSceneName = "Title";

    public static PauseMenuUI Instance { get; private set; }

    private RectTransform overlayRoot;
    // 딤 배경 + 옵션 패널을 묶는 자식. 숨길 때 루트가 아니라 이 오브젝트만 끈다
    // (아래 Build/Update 주석의 2026-08-19 버그 수정 참고).
    private GameObject content;
    private SettingsPanelUI settingsPanel;
    private float savedTimeScale = 1f;

    public bool IsOpen => content != null && content.activeSelf;

    /// <summary>이미 있으면 재사용하고, 없으면 Canvas 아래에 코드로 만든다. 에디터 도메인 리로드로
    /// <see cref="Instance"/> 참조가 날아가도 오브젝트 자체는 씬에 남아 있을 수 있으므로 먼저 찾아본다
    /// (2026-08-18 AiCoreExtraButtonsUI에서 겪은 함정과 같은 대응).</summary>
    public static PauseMenuUI EnsureAttached(RectTransform canvasRoot)
    {
        if (Instance != null) return Instance;
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentInChildren<PauseMenuUI>(true);
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("PauseMenu", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);

        var ui = go.AddComponent<PauseMenuUI>();
        ui.Build((RectTransform)go.transform);
        Instance = ui;
        return ui;
    }

    private void Build(RectTransform root)
    {
        overlayRoot = root;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        // <b>2026-08-19 버그 수정 - 딤 배경·옵션 패널을 이 자식 하나에 묶는 이유.</b>
        // 예전에는 Build() 마지막에 `root.gameObject.SetActive(false)`로 <b>이 스크립트 자신이
        // 붙어 있는 GameObject</b>를 껐다(root == 이 컴포넌트의 transform). Unity는 비활성
        // GameObject의 Update()를 아예 호출하지 않으므로 아래 Update()의 ESC 폴링이 단 한 번도
        // 실행되지 않았고, 그래서 <b>ESC로 일시정지 메뉴를 여는 것이 구조적으로 불가능</b>했다
        // (사용자 리포트 "인게임에서 esc누르면 설정창 나오고 일시정지 되어야 하는데 적용이 안됨").
        // 같은 날 DashGaugeUI에서 고친 것과 완전히 동일한 함정이다.
        // 이제 root는 항상 켜 둔 채 이 Content만 껐다 켠다.
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(root, false);
        content = contentGo;
        var contentRect = (RectTransform)contentGo.transform;
        Stretch(contentRect);

        // 1. 게임 일시정지 중임을 보여주는 반투명 배경(인게임 HUD 위에 덮인다).
        var bgGo = new GameObject("DimBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(contentRect, false);
        Stretch((RectTransform)bgGo.transform);
        bgGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        // 2. 옵션 패널
        var panelGo = new GameObject("OptionPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(contentRect, false);
        var panelRect = (RectTransform)panelGo.transform;
        panelRect.anchorMin = new Vector2(0.36f, 0.24f);
        panelRect.anchorMax = new Vector2(0.64f, 0.76f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.97f);

        CreateLabel(panelRect, "Title", "옵션", 0.06f, 0.87f, 0.94f, 0.97f);

        CreateButton(panelRect, "ReturnButton", "돌아가기", 0.10f, 0.685f, 0.90f, 0.80f, ClosePause, true);
        CreateButton(panelRect, "RankingButton", "랭킹 (준비 중)", 0.10f, 0.525f, 0.90f, 0.64f, null, false);
        CreateButton(panelRect, "CodexButton", "도감 (준비 중)", 0.10f, 0.365f, 0.90f, 0.48f, null, false);
        CreateButton(panelRect, "SettingsButton", "설정", 0.10f, 0.205f, 0.90f, 0.32f, OpenSettings, true);
        CreateButton(panelRect, "QuitButton", "나가기", 0.10f, 0.045f, 0.90f, 0.16f, HandleQuitClicked, true);

        // 설정 패널은 Content가 아니라 root 직속으로 둔다 - 자기 활성 상태를 스스로 관리하며,
        // Content보다 뒤 형제라 그리기 순서상 딤 배경·옵션 패널 위에 덮인다(기존 동작 유지).
        settingsPanel = SettingsPanelUI.Attach(root);

        content.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

        // 설정 화면이 열려 있으면 ESC로 그것부터 닫는다(옵션 패널로 한 단계만 돌아간다).
        if (settingsPanel != null && settingsPanel.IsOpen)
        {
            settingsPanel.Close();
            return;
        }

        if (IsOpen)
        {
            ClosePause();
            return;
        }

        TryOpenPause();
    }

    /// <summary>
    /// 열 수 있는 상황이면 일시정지 메뉴를 열고 true를 돌려준다.
    ///
    /// ESC 키(<see cref="Update"/>)와 우상단 설정 아이콘(<see cref="SettingsIconUI"/>)이 <b>둘 다
    /// 이 하나를 호출한다</b> - 게이팅 조건을 양쪽에 복사해 두면 나중에 한쪽만 고쳐 조건이
    /// 어긋나기 때문이다.
    /// </summary>
    public bool TryOpenPause()
    {
        if (IsOpen) return false;

        // 게임오버/승리 화면이나 정비·상점(이미 전체 화면 UI + timeScale=0)에서는 열지 않는다.
        // CurrentState는 인스턴스 프로퍼티라 정적 컨텍스트에서 못 쓰므로, 1:1로 같은 뜻인
        // 정적 플래그 IsIntermission(Combat이 아니면 항상 true)으로 대신 확인한다.
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) return false;
        if (GameFlowManager.IsIntermission) return false;

        OpenPause();
        return true;
    }

    private void OpenPause()
    {
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        GameFlowManager.SetPaused(true);
        content.SetActive(true);
    }

    private void ClosePause()
    {
        if (settingsPanel != null && settingsPanel.IsOpen) settingsPanel.Close();

        content.SetActive(false);
        GameFlowManager.SetPaused(false);
        Time.timeScale = savedTimeScale;
    }

    private void OpenSettings()
    {
        if (settingsPanel != null) settingsPanel.Open();
    }

    private void HandleQuitClicked()
    {
        // 엔드리스 모드 도중 나가기를 누르면(사용자 확정 사항, 2026-08-19 Phase C) 그 시점의
        // 점수를 랭킹에 남긴다 - 정산 팝업의 "타이틀로"와 같은 제출 경로(RunScore.SubmitToLeaderboard)
        // 를 그대로 쓴다. 별도 확인 화면 없이 바로 제출 후 나간다(이미 "나가기"를 눌러 의사를
        // 밝힌 상태라 한 번 더 물어보지 않는다).
        if (RunState.IsEndless) RunScore.SubmitToLeaderboard();

        // 게임오버 요약 화면의 "타이틀로"와 같은 처리 - timeScale을 반드시 되돌려 놓고 이동한다
        // (되돌리지 않으면 타이틀 화면 자체가 멈춘 채로 시작된다).
        Time.timeScale = 1f;
        GameFlowManager.SetPaused(false);
        SceneManager.LoadScene(TitleSceneName);
    }

    // ── 생성 헬퍼 ────────────────────────────────────────────────────

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void CreateLabel(RectTransform parent, string name, string text,
                                    float xMin, float yMin, float xMax, float yMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(label, 34f);
    }

    /// <summary>비활성 버튼(onClick == null)은 눌러도 아무 일도 없는 "준비 중" 표시로 쓴다.</summary>
    private static void CreateButton(RectTransform parent, string name, string label,
                                     float xMin, float yMin, float xMax, float yMax,
                                     System.Action onClick, bool interactable)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        Sprite buttonSprite = Resources.Load<Sprite>("UI/Purple_button00");
        if (buttonSprite != null)
        {
            img.sprite = buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }
        else
        {
            img.color = interactable ? new Color(0.33f, 0.29f, 0.55f, 1f) : new Color(0.20f, 0.18f, 0.24f, 1f);
        }

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.interactable = interactable;
        if (onClick != null) button.onClick.AddListener(() => onClick());

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 4f);
        textRect.offsetMax = new Vector2(-4f, -4f);

        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.color = interactable ? Color.white : new Color(0.75f, 0.75f, 0.75f, 0.8f);
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, 26f);
    }
}
