using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 환경설정 화면(기획서 p.4). 일시정지 메뉴의 "설정" 버튼을 누르면 그 위에 뜨는 모달이다.
/// 씬에 배치하지 않고 <see cref="PauseMenuUI"/>가 코드로 만들어 붙인다(이 프로젝트의 관례 -
/// <c>EquipmentDetailPopup</c>·<c>MusicVolumeSliderUI</c>와 같은 방식).
///
/// 배경음/효과음 슬라이더는 값이 바뀌는 즉시 저장·반영된다(기존 배경음 슬라이더와 동일).
/// 화면 조정(전체화면 모드·화면 비율)은 <b>"저장 후 닫기"를 눌러야 실제로 적용</b>된다 -
/// 슬라이더와 달리 화면 모드 전환은 즉시 바뀌면 조작 중 화면이 계속 깜빡여 불편하기 때문이다.
///
/// <b>참고(Editor 한계)</b>: <see cref="Screen.SetResolution"/>/<see cref="Screen.fullScreenMode"/>는
/// 빌드된 실행 파일에서만 실제로 창 크기·모드를 바꾼다. Unity 에디터의 Game 뷰 크기는 에디터
/// 자체가 관리하므로 플레이모드에서 호출해도 Game 뷰가 바뀌지 않는다(널리 알려진 에디터 한계).
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    private const string ScreenModeKey = "comstock_screen_mode";
    private const string AspectKey = "comstock_aspect_ratio";

    // 화면 모드 이름은 언어에 따라 달라지므로 static 배열에 문자열을 굳혀두면 안 된다
    // (static 초기화는 언어 결정 전에 한 번만 돌기 때문). 필요할 때마다 조회한다.
    private static readonly string[] ScreenModeKeys = { "settings.screen.fullscreen", "settings.screen.borderless", "settings.screen.windowed" };
    private static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.FullScreenWindow,
        FullScreenMode.Windowed
    };

    private static readonly string[] AspectLabels = { "16:10", "16:9", "4:3", "1:1" };
    private static readonly float[] AspectRatios = { 16f / 10f, 16f / 9f, 4f / 3f, 1f };

    public bool IsOpen => gameObject.activeSelf;

    private readonly Button[] screenModeButtons = new Button[3];
    private readonly Image[] screenModeBgs = new Image[3];
    private readonly Button[] aspectButtons = new Button[4];
    private readonly Image[] aspectBgs = new Image[4];

    private int screenModeIndex;
    private int aspectIndex;

    private static readonly Color SelectedColor = new Color(0.95f, 0.75f, 0.15f, 1f); // 정비 화면 슬롯 강조와 같은 색
    private static readonly Color NormalColor = new Color(0.20f, 0.22f, 0.25f, 1f);
    private static readonly Color DisabledColor = new Color(0.14f, 0.15f, 0.17f, 1f);

    public static SettingsPanelUI Attach(RectTransform parent)
    {
        if (parent == null) return null;

        var existing = parent.GetComponentInChildren<SettingsPanelUI>(true);
        if (existing != null) return existing;

        var go = new GameObject("SettingsPanel", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var ui = go.AddComponent<SettingsPanelUI>();
        ui.Build((RectTransform)go.transform);
        return ui;
    }

    public void Open()
    {
        screenModeIndex = Mathf.Clamp(PlayerPrefs.GetInt(ScreenModeKey, 2), 0, ScreenModeKeys.Length - 1);
        aspectIndex = Mathf.Clamp(PlayerPrefs.GetInt(AspectKey, 1), 0, AspectLabels.Length - 1);
        RefreshScreenButtons();
        gameObject.SetActive(true);
    }

    public void Close() => gameObject.SetActive(false);

    private void Build(RectTransform root)
    {
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(root, false);
        var panelRect = (RectTransform)panelGo.transform;
        panelRect.anchorMin = new Vector2(0.26f, 0.10f);
        panelRect.anchorMax = new Vector2(0.74f, 0.90f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

        // 세로 배치는 패널 안쪽(0~1 로컬)을 빈틈없이 채운다 - 처음엔 화면비율 아래로 패널의
        // 1/3 넘게 빈 공간이 남고 저장 버튼은 패널 밖(y<0)으로 튀어나가 있었다(실측으로 발견).
        CreateLabel(panelRect, "Title", Loc.T("settings.title"), 0.05f, 0.90f, 0.80f, 0.965f, TextAlignmentOptions.MidlineLeft, 32f);
        CreateCloseButton(panelRect);

        VolumeSliderUI.Attach(panelRect, new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.865f),
            Loc.T("settings.music"), () => MusicManager.Volume, v => MusicManager.Volume = v,
            h => MusicManager.OnVolumeChanged += h, h => MusicManager.OnVolumeChanged -= h);

        VolumeSliderUI.Attach(panelRect, new Vector2(0.06f, 0.665f), new Vector2(0.94f, 0.75f),
            Loc.T("settings.sfx"), () => SFXManager.Volume, v => SFXManager.Volume = v,
            h => SFXManager.OnVolumeChanged += h, h => SFXManager.OnVolumeChanged -= h);

        CreateLabel(panelRect, "ScreenLabel", Loc.T("settings.screen"), 0.06f, 0.565f, 0.5f, 0.625f, TextAlignmentOptions.MidlineLeft, 24f);

        float modeWidth = 0.88f / ScreenModeKeys.Length;
        for (int i = 0; i < ScreenModeKeys.Length; i++)
        {
            int index = i;
            float x = 0.06f + modeWidth * i;
            screenModeBgs[i] = CreateRadioButton(panelRect, $"ScreenMode_{i}", Loc.T(ScreenModeKeys[i]),
                x, 0.45f, x + modeWidth - 0.01f, 0.545f, () => SelectScreenMode(index), out screenModeButtons[i]);
        }

        float aspectWidth = 0.88f / AspectLabels.Length;
        for (int i = 0; i < AspectLabels.Length; i++)
        {
            int index = i;
            float x = 0.06f + aspectWidth * i;
            aspectBgs[i] = CreateRadioButton(panelRect, $"Aspect_{i}", AspectLabels[i],
                x, 0.335f, x + aspectWidth - 0.01f, 0.43f, () => SelectAspect(index), out aspectButtons[i]);
        }

        // 피드백 웹사이트 버튼 - 열람할 URL을 아직 받지 못해 비활성으로 둔다(2026-08-18).
        // URL이 정해지면 CreateActionButton의 onClick에 Application.OpenURL(그 주소)를 넣으면 된다.
        CreateActionButton(panelRect, "FeedbackButton", Loc.T("settings.feedback_wip"),
            0.06f, 0.16f, 0.94f, 0.245f, null, false);

        CreateActionButton(panelRect, "SaveButton", Loc.T("settings.saveclose"), 0.06f, 0.03f, 0.94f, 0.115f,
            HandleSaveClicked, true);

        gameObject.SetActive(false);
    }

    private void SelectScreenMode(int index)
    {
        screenModeIndex = index;
        RefreshScreenButtons();
    }

    private void SelectAspect(int index)
    {
        if (screenModeIndex != 0) return; // 전체화면 선택 시에만 화면 비율을 고를 수 있다(기획서 그대로)
        aspectIndex = index;
        RefreshScreenButtons();
    }

    private void RefreshScreenButtons()
    {
        for (int i = 0; i < screenModeBgs.Length; i++)
            screenModeBgs[i].color = i == screenModeIndex ? SelectedColor : NormalColor;

        bool aspectEnabled = screenModeIndex == 0;
        for (int i = 0; i < aspectBgs.Length; i++)
        {
            aspectButtons[i].interactable = aspectEnabled;
            aspectBgs[i].color = !aspectEnabled ? DisabledColor : (i == aspectIndex ? SelectedColor : NormalColor);
        }
    }

    private void HandleSaveClicked()
    {
        PlayerPrefs.SetInt(ScreenModeKey, screenModeIndex);
        PlayerPrefs.SetInt(AspectKey, aspectIndex);
        PlayerPrefs.Save();

        ApplyScreenSettings();
        Close();
    }

    /// <summary>
    /// 저장된 화면 모드/비율을 실제 Screen API에 적용한다. 전체화면일 때만 비율이 의미가 있어
    /// 해상도를 다시 계산하고, 나머지 모드는 모드만 바꾼다(기획서: 비율은 전체화면 전용).
    /// </summary>
    private void ApplyScreenSettings()
    {
        Resolution native = Screen.currentResolution;

        if (screenModeIndex == 0)
        {
            int height = native.height;
            int width = Mathf.RoundToInt(height * AspectRatios[aspectIndex]);
            Screen.SetResolution(width, height, ScreenModes[0]);
        }
        else if (screenModeIndex == 1)
        {
            Screen.SetResolution(native.width, native.height, ScreenModes[1]);
        }
        else
        {
            Screen.SetResolution(1280, 720, ScreenModes[2]);
        }
    }

    // ── 생성 헬퍼 ────────────────────────────────────────────────────

    private void CreateCloseButton(RectTransform parent)
    {
        var go = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.90f, 0.93f);
        rect.anchorMax = new Vector2(0.965f, 0.985f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.35f, 0.10f, 0.10f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(Close);

        TextMeshProUGUI label = CreateText(rect, "X", TextAlignmentOptions.Center);
        label.text = "X";
        label.fontSizeMax = 28f;
    }

    private static void CreateLabel(RectTransform parent, string name, string text,
                                    float xMin, float yMin, float xMax, float yMax,
                                    TextAlignmentOptions alignment, float maxFontSize)
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
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(label, maxFontSize);
    }

    private static Image CreateRadioButton(RectTransform parent, string name, string label,
                                           float xMin, float yMin, float xMax, float yMax,
                                           System.Action onClick, out Button button)
    {
        Image bg = ItemCellUI.CreateShell(parent, name, NormalColor, onClick, out GameObject cell);
        button = cell.GetComponent<Button>();

        // ItemCellUI.CreateShell은 GridLayoutGroup이 위치를 정해주는 걸 전제로 anchor를 건드리지
        // 않는다 - 여기는 격자가 아니라 좌표를 직접 지정해야 하므로 만든 뒤에 앵커를 채워 넣는다
        // (처음에 이걸 빠뜨려 버튼 4개가 전부 (0.5,0.5) 한 점에 뭉쳐 안 보이는 채로 남았었다).
        var rect = (RectTransform)cell.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = CreateText((RectTransform)cell.transform, "Label", TextAlignmentOptions.Center);
        text.text = label;
        text.fontSizeMax = 22f;

        return bg;
    }

    private static void CreateActionButton(RectTransform parent, string name, string label,
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
        if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; img.color = interactable ? Color.white : new Color(1f, 1f, 1f, 0.4f); }
        else img.color = interactable ? new Color(0.33f, 0.29f, 0.55f, 1f) : DisabledColor;

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.interactable = interactable;
        if (onClick != null) button.onClick.AddListener(() => onClick());

        TextMeshProUGUI text = CreateText(rect, "Label", TextAlignmentOptions.Center);
        text.text = label;
        text.color = interactable ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.7f);
        text.fontSizeMax = 24f;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, 24f);
        return text;
    }
}
