using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 보유 중인 무기·파츠·디스크의 상세 능력치를 보여주는 팝업(조회 전용).
///
/// 씬에 미리 배치하지 않고 <see cref="Create"/>가 캔버스 밑에 통째로 만들어 쓴다
/// (<c>ModdingPanelUI</c>가 정비 화면 칸을 코드로 생성하는 것과 같은 방식). 상점 UI는 이미
/// 씬 오브젝트가 30개 가까이 되고, 팝업은 조회 전용이라 디자이너가 씬에서 만질 일이 없다.
///
/// 레이아웃은 전부 <b>앵커 비율</b>로 잡는다 - 이 프로젝트 캔버스는 <c>ConstantPixelSize</c>라
/// 절대 픽셀로 만들면 Game View 해상도에 따라 크기가 제각각이 된다(같은 이유로 글자는 TMP
/// 자동 크기 조절을 켠다).
/// </summary>
public class EquipmentDetailPopup : MonoBehaviour
{
    private TextMeshProUGUI title_text;
    private TextMeshProUGUI body_text;
    private Image icon_image;

    /// <summary>캔버스 최상단에 팝업을 만들어 돌려준다(처음에는 꺼진 상태).</summary>
    public static EquipmentDetailPopup Create(Transform canvasRoot)
    {
        if (canvasRoot == null) return null;

        var root = new GameObject("EquipmentDetailPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvasRoot, false);

        var root_rect = (RectTransform)root.transform;
        Stretch(root_rect, Vector2.zero, Vector2.one);

        // 배경(반투명 검정) 자체가 버튼이라 팝업 바깥을 누르면 닫힌다.
        var blocker = root.GetComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.72f);

        var popup = root.AddComponent<EquipmentDetailPopup>();
        Button blocker_button = root.AddComponent<Button>();
        blocker_button.transition = Selectable.Transition.None;
        blocker_button.onClick.AddListener(popup.Hide);

        // 본문 창
        var window = new GameObject("Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        window.transform.SetParent(root.transform, false);
        var window_rect = (RectTransform)window.transform;
        Stretch(window_rect, new Vector2(0.28f, 0.12f), new Vector2(0.72f, 0.88f));
        ApplySkin(window.GetComponent<Image>(), "Purple_ui04", new Color(0.10f, 0.11f, 0.14f, 0.98f));

        // 창을 누른 클릭이 배경 버튼까지 내려가 닫히지 않도록 창도 (아무 동작 없는) 버튼으로 막는다.
        Button window_blocker = window.AddComponent<Button>();
        window_blocker.transition = Selectable.Transition.None;

        popup.icon_image = CreateIcon(window_rect);
        popup.title_text = CreateText(window_rect, "Title",
            new Vector2(0.28f, 0.86f), new Vector2(0.97f, 0.98f),
            TextAlignmentOptions.TopLeft, 14f, 30f);
        popup.body_text = CreateText(window_rect, "Body",
            new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.84f),
            TextAlignmentOptions.TopLeft, 10f, 22f);

        CreateCloseButton(window_rect, popup.Hide);

        root.SetActive(false);
        return popup;
    }

    /// <summary>제목·본문·아이콘을 채우고 팝업을 연다.</summary>
    public void Show(string title, string body, Sprite icon)
    {
        if (title_text != null) title_text.text = title;
        if (body_text != null) body_text.text = body;

        if (icon_image != null)
        {
            icon_image.sprite = icon;
            icon_image.enabled = icon != null;
            icon_image.preserveAspect = true;
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // 다른 UI(소켓 선택 줄 등) 위에 확실히 올라오게
    }

    public void Hide() => gameObject.SetActive(false);

    public bool IsOpen => gameObject.activeSelf;

    // ── 이하 생성 헬퍼 ────────────────────────────────────────────────

    /// <summary>
    /// `Assets/Resources/UI`의 9-슬라이스 아트를 입힌다. 아트를 못 찾으면 예전처럼 단색으로
    /// 남는다 - 이미지가 없다고 팝업이 안 보이는 사고를 막기 위한 폴백이다.
    /// </summary>
    private static void ApplySkin(Image image, string spriteName, Color fallbackColor)
    {
        if (image == null) return;

        Sprite sprite = Resources.Load<Sprite>("UI/" + spriteName);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            return;
        }

        image.color = fallbackColor;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin,
                                              Vector2 anchorMax, TextAlignmentOptions alignment,
                                              float minFontSize, float maxFontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableWordWrapping = true;

        // ConstantPixelSize 캔버스라 해상도에 따라 칸 크기가 변한다 - 자동 크기 조절로 넘침 방지
        text.enableAutoSizing = true;
        text.fontSizeMin = minFontSize;
        text.fontSizeMax = maxFontSize;

        return text;
    }

    private static Image CreateIcon(RectTransform parent)
    {
        var go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, new Vector2(0.04f, 0.855f), new Vector2(0.24f, 0.985f));

        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.enabled = false;
        return image;
    }

    private static void CreateCloseButton(RectTransform parent, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, new Vector2(0.34f, 0.025f), new Vector2(0.66f, 0.10f));
        ApplySkin(go.GetComponent<Image>(), "Purple_button00", new Color(0.26f, 0.28f, 0.34f, 1f));

        Button button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);

        TextMeshProUGUI label = CreateText((RectTransform)go.transform, "Label",
            Vector2.zero, Vector2.one, TextAlignmentOptions.Center, 10f, 22f);
        label.text = Loc.T("common.close");
    }
}
