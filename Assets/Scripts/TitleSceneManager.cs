using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 타이틀 화면. "게임 시작"을 누르면 <b>머리(로봇) 선택 화면</b>이 먼저 열리고, 거기서 "출발"을
/// 눌러야 플레이 씬(Ground01)으로 넘어간다(2026-08-19 머리 기획서 Ver04 반영).
///
/// 선택 화면은 별도 씬이 아니라 <b>이 씬 위에 뜨는 패널</b>이다(사용자 확정) - 씬을 새로 만들면
/// 빌드 세팅을 바꿔야 하고(협업 규칙상 프로젝트 설정 변경은 사용자 확인 필요) BGM 연속성도
/// 끊긴다. 패널은 씬에 배치하지 않고 <see cref="HeadSelectPanelUI"/>가 코드로 만든다 -
/// 머리 개수가 데이터에서 오므로 씬에 칸을 미리 깔 수 없다.
///
/// 기존 <c>RobotSelectManager</c>는 인스펙터에 로봇ID·버튼을 하나하나 연결하는 방식이라
/// 머리 12종에 쓸 수 없었고, 어떤 씬에도 배치되지 않은 미사용 스크립트였다. 지우지는 않았다.
/// </summary>
public class TitleSceneManager : MonoBehaviour
{
    [Tooltip("머리 선택 후 '출발'을 누르면 로드할 씬 이름")]
    [SerializeField] private string nextSceneName = "Ground01";

    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    [Tooltip("설정 버튼(씬에 배치된 실제 오브젝트). 2026-08-20 사용자 지적 - 예전에는 이 버튼을 " +
             "코드로 만들어서(다른 버튼과 크기가 안 맞고 좌우로 길었다) 플레이 모드에서만 하이라키에 " +
             "보였다. 이제 시작/종료 버튼과 같은 방식(씬에 배치 + 같은 크기)으로 만들어 여기 연결한다.")]
    [SerializeField] private Button settingsButton;

    [Tooltip("설정 패널(과 도감/랭킹 등 코드 생성 UI)을 붙일 캔버스. 비워두면 씬의 첫 번째 캔버스를 찾아 쓴다. " +
             "필드 이름은 예전 볼륨 슬라이더 시절 그대로 남겨뒀다(씬에 이미 연결돼 있을 수 있어 이름을 바꾸면 참조가 끊긴다).")]
    [SerializeField] private RectTransform volumeSliderParent;

    [Header("머리 선택 화면")]
    [Tooltip("머리 목록·소켓 수·고유 효과가 담긴 파츠 카탈로그(Assets/Data/PartsCatalog.asset). " +
             "비어 있으면 머리 선택을 건너뛰고 바로 다음 씬으로 넘어간다")]
    [SerializeField] private PartsCatalog partsCatalog;

    [Header("도감 (2026-08-19 Phase E)")]
    [Tooltip("디스크 이름·아이콘을 도감에서 보여주기 위한 상점 카탈로그(Assets/Data/ShopCatalog.asset). " +
             "비어 있으면 디스크 칸이 이름만 나오고 아이콘이 비어 보인다")]
    [SerializeField] private ShopCatalog shopCatalog;

    private HeadSelectPanelUI headSelectPanel;
    private CollectionPanelUI collectionPanel;
    private Button collectionButton;
    private RankingPanelUI rankingPanel;
    private Button rankingButton;

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // 머리 스프라이트/효과 조회가 카탈로그를 필요로 한다. Ground01에서는
        // ModdingManager.Awake가 같은 일을 한다(타이틀 씬에는 ModdingManager가 없다).
        if (partsCatalog != null) HeadEffects.Bind(partsCatalog);

        AttachSettingsButton();
        AttachCollectionButton();
        AttachRankingButton();
    }

    /// <summary>
    /// 타이틀 우상단에 "도감" 버튼을 코드로 붙인다(2026-08-19 Phase E). 씬을 건드리지 않는
    /// 기존 관례(볼륨 슬라이더·설정 아이콘과 동일)를 따랐고, 시작/종료 버튼(y 0.16~0.28)과
    /// 겹치지 않는 빈 구석을 골랐다.
    /// </summary>
    private void AttachCollectionButton()
    {
        RectTransform parent = ResolveCanvasRect();
        if (parent == null) return;

        var go = new GameObject("CollectionButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.80f, 0.895f);
        rect.anchorMax = new Vector2(0.965f, 0.965f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        Sprite art = Resources.Load<Sprite>("UI/Purple_ui02");
        if (art != null)
        {
            image.sprite = art;
            image.type = Image.Type.Sliced;
        }
        else
        {
            image.color = new Color(0.30f, 0.24f, 0.52f, 1f);
        }

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        labelGo.transform.SetParent(rect, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var label = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
        label.text = "도감";
        label.alignment = TMPro.TextAlignmentOptions.Midline;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = 24f;

        collectionButton = go.AddComponent<Button>();
        collectionButton.onClick.AddListener(OnCollectionClicked);
    }

    /// <summary>"도감" - 해금 목록을 연다. 닫으면 타이틀 UI가 다시 보인다.</summary>
    private void OnCollectionClicked()
    {
        RectTransform parent = ResolveCanvasRect();
        if (parent == null) return;

        if (collectionPanel != null) return; // 연타 방어(닫을 때 파괴된다)

        SetTitleUiVisible(false);
        collectionPanel = CollectionPanelUI.Attach(parent, partsCatalog, shopCatalog, OnCollectionClosed);
    }

    private void OnCollectionClosed()
    {
        collectionPanel = null;
        SetTitleUiVisible(true);
    }

    /// <summary>
    /// "설정" 버튼에 <see cref="SettingsPanelUI"/>를 연결한다(2026-08-20). 버튼 자체는 씬에
    /// 시작/종료 버튼과 같은 크기·같은 방식으로 미리 배치돼 있다(<see cref="settingsButton"/>) -
    /// 예전에는 이 버튼을 코드로 만들어서 다른 버튼과 크기가 안 맞고(좌우로 훨씬 길었다)
    /// 플레이 모드에서만 하이라키에 나타났다(사용자 지적). 패널은 일시정지 메뉴와 같은
    /// <see cref="SettingsPanelUI"/>를 그대로 열어 배경음/효과음 슬라이더·화면 조정을 한 곳에서
    /// 다루게 한다(중복 구현 방지).
    /// </summary>
    private void AttachSettingsButton()
    {
        if (settingsButton == null) return;

        RectTransform parent = ResolveCanvasRect();
        if (parent == null) return;

        settingsPanel = SettingsPanelUI.Attach(parent);
        settingsButton.onClick.AddListener(() => settingsPanel.Open());
    }

    /// <summary>
    /// 타이틀 우하단에 "랭킹" 버튼을 붙인다(2026-08-20). 도감 버튼(우상단)과 같은 관례로
    /// 씬을 건드리지 않고 코드로 만든다.
    /// </summary>
    private void AttachRankingButton()
    {
        RectTransform parent = ResolveCanvasRect();
        if (parent == null) return;

        var go = new GameObject("RankingButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.80f, 0.02f);
        rect.anchorMax = new Vector2(0.965f, 0.09f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        Sprite art = Resources.Load<Sprite>("UI/Purple_ui02");
        if (art != null)
        {
            image.sprite = art;
            image.type = Image.Type.Sliced;
        }
        else
        {
            image.color = new Color(0.30f, 0.24f, 0.52f, 1f);
        }

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI));
        labelGo.transform.SetParent(rect, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var label = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
        label.text = "랭킹";
        label.alignment = TMPro.TextAlignmentOptions.Midline;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = 24f;

        rankingButton = go.AddComponent<Button>();
        rankingButton.onClick.AddListener(OnRankingClicked);
    }

    /// <summary>"랭킹" - 도감과 같은 방식으로 연다(닫으면 타이틀 UI가 다시 보인다).
    /// 타이틀에는 "지금 플레이 중인 맵"이 없으므로, 다음에 플레이할 맵(<see cref="nextSceneName"/>
    /// - 지금은 게임 시작 버튼과 같은 값)의 랭킹을 보여준다. 나중에 맵 선택 화면이 생기면 그
    /// 화면이 nextSceneName을 갱신해줄 것이므로 여기는 고칠 필요가 없다(2026-08-20).</summary>
    private void OnRankingClicked()
    {
        RectTransform parent = ResolveCanvasRect();
        if (parent == null) return;

        if (rankingPanel != null) return; // 연타 방어(닫을 때 파괴된다)

        SetTitleUiVisible(false);
        rankingPanel = RankingPanelUI.Attach(parent, nextSceneName, OnRankingClosed);
    }

    private void OnRankingClosed()
    {
        rankingPanel = null;
        SetTitleUiVisible(true);
    }

    /// <summary>
    /// "게임 시작" → 머리 선택 화면을 띄운다. 카탈로그가 연결되지 않았으면(데이터 준비 전 등)
    /// 예전처럼 곧바로 다음 씬으로 넘어가 게임이 막히지 않게 한다.
    /// </summary>
    private void OnStartClicked()
    {
        if (partsCatalog == null)
        {
            Debug.LogWarning("TitleSceneManager.partsCatalog가 비어 있어 머리 선택을 건너뜁니다. " +
                             "인스펙터에서 Assets/Data/PartsCatalog.asset을 연결하세요.");
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        // 이미 열려 있으면 다시 만들지 않고 그대로 보여준다(버튼 연타 방어).
        if (headSelectPanel != null)
        {
            headSelectPanel.gameObject.SetActive(true);
            return;
        }

        RectTransform parent = ResolveCanvasRect();
        if (parent == null)
        {
            Debug.LogWarning("타이틀 씬에서 Canvas를 찾을 수 없어 머리 선택 화면을 띄우지 못했습니다.");
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        SetTitleUiVisible(false);
        headSelectPanel = HeadSelectPanelUI.Attach(parent, partsCatalog, OnHeadConfirmed, OnHeadSelectCancelled);
    }

    /// <summary>
    /// 머리 선택 화면이 열려 있는 동안 타이틀 자체 UI(제목·시작·종료·볼륨 슬라이더)를 숨긴다.
    ///
    /// 암막(반투명 배경)만으로는 부족했다 - 볼륨 슬라이더는 런타임에 캔버스의 마지막 자식으로
    /// 붙어서 형제 순서상 선택 화면보다 <b>뒤</b>에 그려지고(UI는 형제 순서가 곧 그리기 순서다),
    /// 종료 버튼도 암막 위로 비쳐 보였다. z-order를 다투는 대신 아예 끄는 쪽이 확실하다.
    /// </summary>
    private void SetTitleUiVisible(bool visible)
    {
        if (startButton != null) startButton.gameObject.SetActive(visible);
        if (quitButton != null) quitButton.gameObject.SetActive(visible);

        if (settingsButton != null) settingsButton.gameObject.SetActive(visible);
        if (collectionButton != null) collectionButton.gameObject.SetActive(visible);
        if (rankingButton != null) rankingButton.gameObject.SetActive(visible);

        // 제목 텍스트와 그 뒤 판때기는 인스펙터에 연결돼 있지 않아 이름으로 찾는다.
        // 못 찾아도(이름이 바뀌었어도) 조용히 넘어간다 - 암막이 있어 치명적이지 않다.
        RectTransform canvasRect = ResolveCanvasRect();
        if (canvasRect == null) return;

        foreach (string name in TitleOnlyObjectNames)
        {
            Transform found = canvasRect.Find(name);
            if (found != null) found.gameObject.SetActive(visible);
        }
    }

    // 2026-08-20: 사용자가 제공한 로고 이미지("TitleLogo")로 기존 텍스트 제목(TitleText/TitleText_BG)을
    // 대체했다 - 텍스트 오브젝트는 지우지 않고 비활성化해뒀다(되돌리려면 다시 켜고 TitleLogo를 지우면 됨).
    private static readonly string[] TitleOnlyObjectNames = { "TitleText", "TitleText_BG", "TitleLogo" };

    private SettingsPanelUI settingsPanel;

    /// <summary>머리를 확정하고 플레이 씬으로 넘어간다.</summary>
    private void OnHeadConfirmed(int robotId)
    {
        // 런 전체가 이 값을 읽는다(PlayerRobotController.InitFromSession → RobotStats,
        // ModdingManager의 소켓/디스크/적재량, HeadEffects의 고유 효과, 리그의 몸통 스프라이트).
        PlayerSession.SelectedRobotId = robotId;
        SceneManager.LoadScene(nextSceneName);
    }

    /// <summary>"뒤로" - 선택을 취소하고 타이틀로 돌아간다(패널은 재사용을 위해 숨기기만 한다).</summary>
    private void OnHeadSelectCancelled()
    {
        if (headSelectPanel != null) headSelectPanel.gameObject.SetActive(false);
        SetTitleUiVisible(true);
    }

    private RectTransform ResolveCanvasRect()
    {
        if (volumeSliderParent != null) return volumeSliderParent;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        return canvas != null ? canvas.transform as RectTransform : null;
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
