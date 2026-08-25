using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 언어 선택 모달(Phase 1, 2026-08-25). 타이틀 화면의 "언어" 버튼이 연다.
/// <para>
/// 씬에 배치하지 않고 코드로 만든다 - 이 프로젝트의 UI 관례다
/// (<see cref="SettingsPanelUI"/>/<see cref="PauseMenuUI"/>/<see cref="CollectionPanelUI"/>와 동일).
/// </para>
/// <para>
/// <b>언어 목록을 이 파일에 하드코딩하지 않는다.</b> 버튼은 <see cref="Loc.Supported"/>를 순회해
/// 만들어지므로, 언어를 하나 추가해도 이 파일은 손댈 필요가 없다(Loc.Supported에 한 줄 + JSON 파일 하나).
/// 버튼에 적히는 이름은 <b>그 언어 자신의 표기</b>(한국어 / English)라 번역이 필요 없고,
/// 현재 언어를 못 읽는 상태에서도 자기 언어를 찾을 수 있다.
/// </para>
/// </summary>
public class LanguageSelectPanelUI : MonoBehaviour
{
    private static readonly Color SelectedColor = new Color(0.95f, 0.75f, 0.15f, 1f); // 정비 화면 슬롯 강조와 같은 색
    private static readonly Color NormalColor = new Color(0.20f, 0.22f, 0.25f, 1f);

    private Image[] option_bgs;
    private string[] option_codes;
    private TextMeshProUGUI title_label;
    private TextMeshProUGUI note_label;
    private TextMeshProUGUI close_label;

    private System.Action on_closed;

    public bool IsOpen => gameObject.activeSelf;

    /// <summary>
    /// 부모 캔버스에 패널을 붙인다(이미 있으면 그걸 그대로 돌려준다 - 연타로 여러 장 생기는 것 방지).
    /// </summary>
    public static LanguageSelectPanelUI Attach(RectTransform parent, System.Action onClosed = null)
    {
        if (parent == null) return null;

        var existing = parent.GetComponentInChildren<LanguageSelectPanelUI>(true);
        if (existing != null)
        {
            existing.on_closed = onClosed;
            return existing;
        }

        var go = new GameObject("LanguagePanel", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var ui = go.AddComponent<LanguageSelectPanelUI>();
        ui.on_closed = onClosed;
        ui.Build((RectTransform)go.transform);
        go.SetActive(false);
        return ui;
    }

    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // 다른 코드 생성 패널(도감/랭킹) 위에 오도록
        Refresh();
    }

    public void Close()
    {
        gameObject.SetActive(false);
        if (on_closed != null) on_closed();
    }

    /// <summary>
    /// 구독 해제는 스스로 한다. <see cref="Loc.OnLanguageChanged"/>는 static 이벤트라
    /// 관리자 쪽에서 null로 밀지 않는 것이 이 프로젝트의 규칙이다(3번 재발한 버그 참고).
    /// </summary>
    private void OnEnable() => Loc.OnLanguageChanged += Refresh;
    private void OnDisable() => Loc.OnLanguageChanged -= Refresh;

    private void Build(RectTransform root)
    {
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        // 뒤쪽 화면을 눌러도 통과하지 않도록 반투명 막을 깐다(설정 패널과 같은 처리).
        var blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        blockerGo.transform.SetParent(root, false);
        var blockerRect = (RectTransform)blockerGo.transform;
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;
        blockerGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(root, false);
        var panelRect = (RectTransform)panelGo.transform;
        panelRect.anchorMin = new Vector2(0.32f, 0.28f);
        panelRect.anchorMax = new Vector2(0.68f, 0.72f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

        title_label = CreateLabel(panelRect, "Title", 0.08f, 0.84f, 0.86f, 0.94f, TextAlignmentOptions.MidlineLeft, 30f);
        note_label = CreateLabel(panelRect, "Note", 0.08f, 0.75f, 0.92f, 0.82f, TextAlignmentOptions.MidlineLeft, 17f);
        note_label.color = new Color(0.75f, 0.77f, 0.80f, 1f);

        CreateCloseCross(panelRect);
        BuildOptions(panelRect);

        // 하단 "닫기" - 설정 패널의 "저장 후 닫기"와 같은 자리·같은 크기
        close_label = CreateActionButton(panelRect, "CloseButton", 0.08f, 0.05f, 0.92f, 0.16f, Close);
    }

    /// <summary>
    /// <see cref="Loc.Supported"/>를 순회해 언어 버튼을 만든다. 개수에 맞춰 세로로 균등 분할하므로
    /// 언어가 2개든 5개든 패널 안에 알아서 들어간다(고정 좌표를 쓰면 언어를 늘릴 때마다 여기를 고쳐야 한다).
    /// </summary>
    private void BuildOptions(RectTransform panelRect)
    {
        Loc.LanguageInfo[] langs = Loc.Supported;
        option_bgs = new Image[langs.Length];
        option_codes = new string[langs.Length];

        const float areaTop = 0.72f;
        const float areaBottom = 0.20f;
        const float gap = 0.02f;

        float slot = (areaTop - areaBottom) / langs.Length;

        for (int i = 0; i < langs.Length; i++)
        {
            float yMax = areaTop - slot * i;
            float yMin = yMax - slot + gap;
            string code = langs[i].Code;

            var go = new GameObject("Lang_" + code, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panelRect, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.08f, yMin);
            rect.anchorMax = new Vector2(0.92f, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image img = go.GetComponent<Image>();
            img.color = NormalColor;

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => Loc.SetLanguage(code));

            TextMeshProUGUI label = CreateLabel(rect, "Label", 0f, 0f, 1f, 1f, TextAlignmentOptions.Center, 26f);
            label.text = langs[i].NativeName; // 번역하지 않는다 - 그 언어 자신의 표기

            option_bgs[i] = img;
            option_codes[i] = code;
        }
    }

    /// <summary>언어가 바뀌거나 패널이 열릴 때 문구와 선택 표시를 다시 그린다.</summary>
    private void Refresh()
    {
        if (title_label != null) title_label.text = Loc.T("language.title");
        if (note_label != null) note_label.text = Loc.T("language.note");
        if (close_label != null) close_label.text = Loc.T("common.close");

        if (option_bgs == null) return;
        string cur = Loc.CurrentCode;
        for (int i = 0; i < option_bgs.Length; i++)
        {
            if (option_bgs[i] == null) continue;
            option_bgs[i].color = option_codes[i] == cur ? SelectedColor : NormalColor;
        }
    }

    private void CreateCloseCross(RectTransform parent)
    {
        var go = new GameObject("CloseCross", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.88f, 0.855f);
        rect.anchorMax = new Vector2(0.955f, 0.945f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.35f, 0.10f, 0.10f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(Close);

        TextMeshProUGUI label = CreateLabel(rect, "X", 0f, 0f, 1f, 1f, TextAlignmentOptions.Center, 24f);
        label.text = "X";
    }

    private static TextMeshProUGUI CreateActionButton(RectTransform parent, string name,
                                                      float xMin, float yMin, float xMax, float yMax,
                                                      System.Action onClick)
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
        if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; img.color = Color.white; }
        else img.color = new Color(0.33f, 0.29f, 0.55f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        if (onClick != null) button.onClick.AddListener(() => onClick());

        return CreateLabel(rect, "Label", 0f, 0f, 1f, 1f, TextAlignmentOptions.Center, 24f);
    }

    private static TextMeshProUGUI CreateLabel(RectTransform parent, string name,
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
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(label, maxFontSize);
        return label;
    }
}
