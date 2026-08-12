using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "음악 볼륨" 라벨 + 슬라이더 + 퍼센트 표시로 이뤄진 작은 설정 컨트롤.
///
/// 씬에 미리 배치하지 않고 <see cref="Attach"/>가 코드로 만들어 붙인다
/// (<see cref="EquipmentDetailPopup"/>·<c>ModdingPanelUI</c>와 같은 방식). 볼륨 설정은 타이틀
/// 화면과 인게임 상점 화면 두 곳에 똑같이 필요한데, 씬을 두 번 편집하는 대신 한 구현을
/// 양쪽에서 호출한다.
///
/// 값은 <see cref="MusicManager.Volume"/>(PlayerPrefs 저장)에 바로 반영되고, 반대로
/// 다른 화면에서 볼륨이 바뀌면 <see cref="MusicManager.OnVolumeChanged"/>로 이 슬라이더도 따라간다.
/// </summary>
public class MusicVolumeSliderUI : MonoBehaviour
{
    private Slider slider;
    private TextMeshProUGUI value_text;
    private bool suppress_callback;

    /// <summary>
    /// 부모 아래에 볼륨 컨트롤을 만든다. 위치/크기는 앵커 비율로 지정한다
    /// (캔버스가 ConstantPixelSize라 절대 픽셀로 만들면 해상도마다 크기가 달라진다).
    /// </summary>
    public static MusicVolumeSliderUI Attach(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (parent == null) return null;

        var root = new GameObject("MusicVolumeSetting", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = anchorMin;
        rootRect.anchorMax = anchorMax;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var ui = root.AddComponent<MusicVolumeSliderUI>();
        ui.Build(rootRect);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        // 다른 UI와 같은 판때기 아트를 깔아 화면에 겉돌지 않게 한다(없으면 그냥 안 깔린다).
        Sprite plate = Resources.Load<Sprite>("UI/Panel02");
        if (plate != null)
        {
            Image bg = CreateImage(rootRect, "Plate", new Vector2(-0.03f, -0.25f), new Vector2(1.03f, 1.25f), Color.white);
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            bg.transform.SetAsFirstSibling();
        }

        TextMeshProUGUI label = CreateText(rootRect, "Label", new Vector2(0.03f, 0f), new Vector2(0.33f, 1f),
            TextAlignmentOptions.MidlineLeft);
        label.text = "음악 볼륨";

        value_text = CreateText(rootRect, "Value", new Vector2(0.85f, 0f), new Vector2(0.97f, 1f),
            TextAlignmentOptions.MidlineRight);

        // 슬라이더 - Unity 기본 프리팹이 없으므로 Background / Fill / Handle을 직접 조립한다.
        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(rootRect, false);
        var sliderRect = (RectTransform)sliderGo.transform;
        Stretch(sliderRect, new Vector2(0.35f, 0.22f), new Vector2(0.83f, 0.78f));

        Image background = CreateImage(sliderRect, "Background", Vector2.zero, Vector2.one,
            new Color(0.08f, 0.08f, 0.10f, 1f));

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderRect, false);
        Stretch((RectTransform)fillArea.transform, Vector2.zero, Vector2.one);

        Image fill = CreateImage((RectTransform)fillArea.transform, "Fill", Vector2.zero, Vector2.one,
            new Color(0.45f, 0.32f, 0.78f, 1f));

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderRect, false);
        Stretch((RectTransform)handleArea.transform, Vector2.zero, Vector2.one);

        Image handle = CreateImage((RectTransform)handleArea.transform, "Handle", Vector2.zero, Vector2.one,
            new Color(0.88f, 0.86f, 0.95f, 1f));
        var handleRect = (RectTransform)handle.transform;
        handleRect.sizeDelta = new Vector2(14f, 0f); // 슬라이더 핸들만은 폭이 고정이라야 잡기 좋다

        slider = sliderGo.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;

        SetValueSilently(MusicManager.Volume);
        slider.onValueChanged.AddListener(HandleSliderChanged);
    }

    private void OnEnable()
    {
        MusicManager.OnVolumeChanged += SetValueSilently;
        SetValueSilently(MusicManager.Volume); // 다른 화면에서 바뀐 값을 열 때 반영
    }

    private void OnDisable() => MusicManager.OnVolumeChanged -= SetValueSilently;

    private void HandleSliderChanged(float value)
    {
        if (suppress_callback) return;

        MusicManager.Volume = value; // PlayerPrefs 저장 + 즉시 반영은 MusicManager가 담당
        UpdateValueText(value);
    }

    // 이벤트로 값을 되돌려 받을 때 다시 onValueChanged가 도는 무한 루프를 막는다.
    private void SetValueSilently(float value)
    {
        if (slider == null) return;

        suppress_callback = true;
        slider.value = value;
        suppress_callback = false;

        UpdateValueText(value);
    }

    private void UpdateValueText(float value)
    {
        if (value_text != null) value_text.text = $"{value * 100f:0}%";
    }

    // ── 생성 헬퍼 ────────────────────────────────────────────────────

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin,
                                              Vector2 anchorMax, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 8f;
        text.fontSizeMax = 18f;
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
}
