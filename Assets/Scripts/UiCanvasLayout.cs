using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 화면 픽셀과 UI 설계 좌표를 분리한다. 작은 웹 창에서도 글자·테두리·여백을 함께 축소한다.
/// 씬/프로젝트 설정은 바꾸지 않으며 기존 ScaleWithScreenSize 캔버스는 그대로 사용한다.
/// </summary>
public static class UiCanvasLayout
{
    public static readonly Vector2 DesignSize = new Vector2(1920f, 1080f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                Configure(canvas);
    }

    public static void Configure(Canvas canvas)
    {
        if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) return;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = DesignSize;
        // 어느 축도 설계 크기보다 좁아지지 않게 하여 창 비율이 달라도 내용을 자르지 않는다.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        // Awake 중 생성되는 UI도 첫 프레임부터 올바른 좌표를 읽도록 즉시 반영한다.
        canvas.scaleFactor = Mathf.Max(0.01f,
            Mathf.Min(Screen.width / DesignSize.x, Screen.height / DesignSize.y));
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>캔버스 자체가 축소되는 경우 글자/띠 높이에 화면 배율을 또 곱하지 않는다.</summary>
    public static float ContentScale(Component component)
    {
        Canvas canvas = component != null ? component.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
        {
            CanvasScaler scaler = canvas.rootCanvas.GetComponent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) return 1f;
        }
        return Mathf.Clamp(Screen.height / DesignSize.y, 0.6f, 2f);
    }

    public static float PixelsPerCanvasUnit(Component component)
    {
        Canvas canvas = component != null ? component.GetComponentInParent<Canvas>() : null;
        return canvas != null ? Mathf.Max(0.0001f, canvas.rootCanvas.scaleFactor) : 1f;
    }

    /// <summary>왼쪽 아래 앵커를 사용하는 월드 추적 UI의 화면 좌표를 부모 설계 좌표로 변환한다.</summary>
    public static Vector2 ScreenToBottomLeft(RectTransform target, Vector2 screenPoint)
    {
        RectTransform parent = target.parent as RectTransform;
        if (parent == null) return screenPoint;
        Canvas canvas = target.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, eventCamera, out Vector2 point);
        return point - parent.rect.min;
    }
}
