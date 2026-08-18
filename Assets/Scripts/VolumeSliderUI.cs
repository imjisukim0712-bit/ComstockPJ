using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 라벨 + 슬라이더 + 퍼센트 표시로 이뤄진 범용 볼륨 컨트롤(2026-08-18 환경설정 화면용).
///
/// <see cref="MusicVolumeSliderUI"/>가 이미 같은 모양을 쓰고 있지만 그쪽은 배경음 전용으로
/// <see cref="MusicManager"/>에 직접 묶여 있다 - 이미 두 화면(타이틀/상점)에서 검증된 코드라
/// 배경음 쪽은 그대로 두고, 효과음까지 같은 컨트롤을 써야 하는 환경설정 화면을 위해
/// getter/setter 델리게이트를 받는 범용판을 새로 둔다.
/// </summary>
public class VolumeSliderUI : MonoBehaviour
{
    private Slider slider;
    private TextMeshProUGUI valueText;
    private bool suppressCallback;

    private System.Func<float> getValue;
    private System.Action<float> setValue;
    private System.Action<System.Action<float>> subscribe;
    private System.Action<System.Action<float>> unsubscribe;
    private System.Action<float> changedHandler;

    /// <param name="onExternalChange">다른 화면에서 이 값이 바뀌었을 때 슬라이더를 따라가게
    /// 하려면 구독/해제 델리게이트를 넘긴다(선택). null이면 이 컨트롤이 열려 있는 동안에는
    /// 스스로 바꾼 값만 반영한다.</param>
    public static VolumeSliderUI Attach(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
                                        string label, System.Func<float> getValue, System.Action<float> setValue,
                                        System.Action<System.Action<float>> subscribeChanged = null,
                                        System.Action<System.Action<float>> unsubscribeChanged = null)
    {
        if (parent == null) return null;

        var root = new GameObject("VolumeSlider_" + label, typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = anchorMin;
        rootRect.anchorMax = anchorMax;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        var ui = root.AddComponent<VolumeSliderUI>();
        ui.getValue = getValue;
        ui.setValue = setValue;
        ui.subscribe = subscribeChanged;
        ui.unsubscribe = unsubscribeChanged;
        ui.Build(rootRect, label);
        return ui;
    }

    private void Build(RectTransform rootRect, string label)
    {
        TextMeshProUGUI labelText = CreateText(rootRect, "Label", new Vector2(0f, 0f), new Vector2(0.30f, 1f),
            TextAlignmentOptions.MidlineLeft);
        labelText.text = label;

        valueText = CreateText(rootRect, "Value", new Vector2(0.90f, 0f), new Vector2(1f, 1f),
            TextAlignmentOptions.MidlineRight);

        var sliderGo = new GameObject("Slider", typeof(RectTransform));
        sliderGo.transform.SetParent(rootRect, false);
        var sliderRect = (RectTransform)sliderGo.transform;
        Stretch(sliderRect, new Vector2(0.32f, 0.22f), new Vector2(0.88f, 0.78f));

        CreateImage(sliderRect, "Background", Vector2.zero, Vector2.one, new Color(0.08f, 0.08f, 0.10f, 1f));

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
        handleRect.sizeDelta = new Vector2(14f, 0f);

        slider = sliderGo.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = (RectTransform)fill.transform;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;

        SetValueSilently(getValue != null ? getValue() : 0f);
        slider.onValueChanged.AddListener(HandleSliderChanged);
    }

    private void OnEnable()
    {
        if (slider == null) return; // Build() 이전에 활성화되는 경우 방지

        changedHandler = SetValueSilently;
        subscribe?.Invoke(changedHandler);
        if (getValue != null) SetValueSilently(getValue());
    }

    private void OnDisable()
    {
        if (changedHandler != null) unsubscribe?.Invoke(changedHandler);
    }

    private void HandleSliderChanged(float value)
    {
        if (suppressCallback) return;
        setValue?.Invoke(value);
        UpdateValueText(value);
    }

    private void SetValueSilently(float value)
    {
        if (slider == null) return;
        suppressCallback = true;
        slider.value = value;
        suppressCallback = false;
        UpdateValueText(value);
    }

    private void UpdateValueText(float value)
    {
        if (valueText != null) valueText.text = $"{value * 100f:0}%";
    }

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
        text.fontSizeMax = 20f;
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
