using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임이 끝나(사망/타이틀로 복귀) 랭킹에 점수를 올리기 직전에 닉네임을 입력받는 팝업
/// (2026-08-20 사용자 요청 - "게임 끝나서 랭킹 등록할 때 닉네임 넣는 기능도 만드셈").
///
/// 지금까지는 플레이어 이름 입력이 없어 선택한 로봇(머리) 이름을 그대로 썼다
/// (<see cref="RunScore.SubmitToLeaderboard"/> 참고). 입력창은 <b>로봇 이름(또는 마지막으로
/// 쓴 닉네임)으로 미리 채워둔다</b> - 그냥 확인만 눌러도 예전과 동일하게 동작해서, 매판 타이핑을
/// 강제하지 않는다. 마지막으로 쓴 닉네임은 PlayerPrefs에 저장해 다음 판에도 이어서 쓴다.
///
/// 씬에 배치하지 않고 <see cref="RankingPanelUI"/>·<see cref="CollectionPanelUI"/>와 같은
/// 관례로 코드로 만든다(호출 시점 캔버스 아래에 붙었다가 확인하면 파괴된다).
/// </summary>
public class NicknameInputPopup : MonoBehaviour
{
    private const string PrefsKey = "comstock_nickname";
    private const int MaxLength = 20; // Firebase 검증 규칙(PlayerName <= 30자)보다 넉넉히 아래로 잡은 값

    private static readonly Color AccentColor = new Color(0.95f, 0.75f, 0.15f, 1f);

    private TMP_InputField inputField;
    private System.Action<string> onConfirm;
    private string fallbackName;

    /// <summary>부모(보통 최상위 캔버스) 아래에 팝업을 만들어 돌려준다. parent가 없으면(캔버스를
    /// 못 찾은 등의 예외 상황) 팝업 없이 바로 defaultName으로 확인 콜백을 불러 흐름이 막히지
    /// 않게 한다.</summary>
    public static NicknameInputPopup Attach(RectTransform parent, string defaultName, System.Action<string> onConfirm)
    {
        if (parent == null)
        {
            onConfirm?.Invoke(defaultName);
            return null;
        }

        var root = new GameObject("NicknameInputPopup", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        Stretch(rootRect, Vector2.zero, Vector2.one);
        rootRect.SetAsLastSibling(); // UI는 형제 순서가 곧 그리기 순서다

        var ui = root.AddComponent<NicknameInputPopup>();
        ui.onConfirm = onConfirm;
        ui.fallbackName = string.IsNullOrWhiteSpace(defaultName) ? "플레이어" : defaultName;
        ui.Build(rootRect);
        return ui;
    }

    private void Build(RectTransform root)
    {
        Image backdrop = CreateImage(root, "Backdrop", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.75f));
        backdrop.raycastTarget = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(root, false);
        var panelRect = (RectTransform)panelGo.transform;
        Stretch(panelRect, new Vector2(0.28f, 0.36f), new Vector2(0.72f, 0.64f));

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

        TextMeshProUGUI title = CreateText(panelRect, "Title", new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.93f),
                                           TextAlignmentOptions.Midline, 30f);
        title.text = "랭킹에 올릴 닉네임";
        title.color = AccentColor;

        inputField = CreateInputField(panelRect, new Vector2(0.08f, 0.42f), new Vector2(0.92f, 0.65f));

        string saved = PlayerPrefs.GetString(PrefsKey, string.Empty);
        inputField.text = string.IsNullOrEmpty(saved) ? fallbackName : saved;
        inputField.onSubmit.AddListener(_ => Confirm()); // 입력창에서 Enter를 눌러도 확인된다

        Button confirmButton = CreateButton(panelRect, "ConfirmButton", new Vector2(0.30f, 0.08f), new Vector2(0.70f, 0.30f), "확인");
        confirmButton.onClick.AddListener(Confirm);
    }

    private void Confirm()
    {
        string name = inputField != null ? inputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(name)) name = fallbackName;
        if (name.Length > MaxLength) name = name.Substring(0, MaxLength);

        PlayerPrefs.SetString(PrefsKey, name);
        PlayerPrefs.Save();

        onConfirm?.Invoke(name);
        Destroy(gameObject);
    }

    // ── UI 헬퍼 (RankingPanelUI·CollectionPanelUI와 같은 관례) ──────────────────────────

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                              TextAlignmentOptions alignment, float maxSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, maxSize);
        return text;
    }

    private static Image CreateImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Button CreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        Sprite art = Resources.Load<Sprite>("UI/Purple_ui02");
        if (art != null) { image.sprite = art; image.type = Image.Type.Sliced; }
        else image.color = new Color(0.30f, 0.24f, 0.52f, 1f);

        TextMeshProUGUI labelText = CreateText((RectTransform)go.transform, "Label", Vector2.zero, Vector2.one,
                                               TextAlignmentOptions.Midline, 26f);
        labelText.text = label;

        return go.AddComponent<Button>();
    }

    /// <summary>TMP_InputField는 텍스트/플레이스홀더 자식이 따로 필요해서 다른 위젯보다 조립이
    /// 길다 - Unity 기본 TMP 입력창 프리팹과 같은 최소 구조(TextArea + Placeholder + Text)만 만든다.</summary>
    private static TMP_InputField CreateInputField(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject("InputField", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        Image bg = go.GetComponent<Image>();
        bg.color = new Color(0.16f, 0.17f, 0.20f, 1f);

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;

        var textAreaGo = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textAreaGo.transform.SetParent(go.transform, false);
        var textAreaRect = (RectTransform)textAreaGo.transform;
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(12f, 4f);
        textAreaRect.offsetMax = new Vector2(-12f, -4f);

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textAreaRect, false);
        Stretch((RectTransform)placeholderGo.transform, Vector2.zero, Vector2.one);
        var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.text = "닉네임을 입력하세요";
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(placeholder, 28f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textAreaRect, false);
        Stretch((RectTransform)textGo.transform, Vector2.zero, Vector2.one);
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, 28f);

        input.textViewport = textAreaRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = MaxLength;
        input.lineType = TMP_InputField.LineType.SingleLine;

        return input;
    }
}
