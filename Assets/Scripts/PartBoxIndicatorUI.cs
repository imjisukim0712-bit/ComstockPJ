using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 필드에 떨어진 부품 상자(RewardPickup.AlivePartBoxes)가 화면 밖에 있을 때, 화면 가장자리에
/// 그 방향을 가리키는 화살표 + 부품 상자 아이콘을 띄운다. 화면 안에 있는 상자는 이미 필드
/// 스프라이트로 보이므로 표시하지 않는다.
///
/// 사용법: Canvas 밑에 화면 전체를 덮는 RectTransform(anchorMin 0,0 / anchorMax 1,1)을 만들고
/// 이 스크립트를 붙이면 된다. 화살표 스프라이트는 외부 에셋 없이 코드로 생성한다(삼각형).
/// 표시 인스턴스는 필요한 만큼만 켜지는 고정 풀(maxIndicators)로 관리한다.
///
/// <b>크기는 반드시 해상도 비례로 계산한다(referenceScreenHeight).</b> 이 프로젝트의 Canvas는
/// CanvasScaler가 ConstantPixelSize라 UI가 해상도에 따라 자동으로 커지지 않는다. 예전에는
/// 화살표 크기(36px)·여백(60px)을 절대 픽셀로 박아뒀는데, Game View가 4K(3840x2160)이면
/// 화살표가 화면 폭의 0.9%짜리 점이 되고 여백도 1.5%라 화면 맨 끝에 붙어서, 논리는 정상
/// 동작하는데 눈에는 보이지 않는 상태였다(2026-08-10 실측으로 확인한 실제 증상).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PartBoxIndicatorUI : MonoBehaviour
{
    [Tooltip("동시에 표시할 수 있는 최대 화살표 개수")]
    [SerializeField] private int maxIndicators = 8;

    [Tooltip("아래 픽셀 값들의 기준이 되는 화면 세로 해상도. 실제 화면이 이보다 크면 그 비율만큼 같이 커진다")]
    [SerializeField] private float referenceScreenHeight = 1080f;

    [Tooltip("화면 가장자리에서 표시를 얼마나 안쪽으로 띄울지 (기준 해상도에서의 픽셀)")]
    [SerializeField] private float edgeMargin = 90f;

    [Tooltip("화살표 크기 (기준 해상도에서의 픽셀)")]
    [SerializeField] private float arrowSize = 56f;

    [Tooltip("화살표와 함께 표시할 부품 상자 아이콘 크기 (기준 해상도에서의 픽셀). 0이면 아이콘 없이 화살표만 표시한다")]
    [SerializeField] private float iconSize = 48f;

    [Tooltip("화살표 색상")]
    [SerializeField] private Color arrowColor = new Color(1f, 0.72f, 0.15f, 1f); // 부품 상자 아이콘과 어울리는 주황/골드

    [Tooltip("부품 상자 아이콘으로 쓸 Resources 스프라이트 이름. RewardPickupManager가 필드 상자에 쓰는 것과 같은 이미지")]
    [SerializeField] private string iconResourceName = "ItemBox";

    private RectTransform rect;
    private Camera cam;
    private Sprite arrowSprite;
    private Sprite iconSprite;

    private readonly List<Indicator> pool = new List<Indicator>();

    // 화살표는 상자 방향으로 회전하고, 아이콘은 항상 똑바로 서 있어야 읽힌다.
    // 그래서 위치만 잡는 root 밑에 둘을 따로 둔다.
    private struct Indicator
    {
        public RectTransform Root;
        public RectTransform Arrow;
        public RectTransform Icon;
    }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        arrowSprite = BuildArrowSprite();
        if (iconSize > 0f && !string.IsNullOrEmpty(iconResourceName)) iconSprite = Resources.Load<Sprite>(iconResourceName);
        BuildPool();
    }

    /// <summary>현재 화면 해상도에 맞춘 픽셀 배율. ConstantPixelSize Canvas를 직접 보정한다.</summary>
    private float UiScale => referenceScreenHeight > 1f
        ? Mathf.Max(0.1f, Screen.height / referenceScreenHeight) / UiCanvasLayout.PixelsPerCanvasUnit(this) : 1f;

    private void BuildPool()
    {
        // 코드로 만든 UI 오브젝트는 기본 레이어(0)로 생성된다. Canvas가 UI 레이어(5)에 있으면
        // 카메라 컬링 마스크나 렌더 설정에 따라 이 화살표만 빠질 수 있으므로 Canvas의 레이어를 따른다.
        Canvas canvas = GetComponentInParent<Canvas>();
        int uiLayer = canvas != null ? canvas.gameObject.layer : gameObject.layer;
        gameObject.layer = uiLayer;

        for (int i = 0; i < maxIndicators; i++)
        {
            GameObject rootObj = new GameObject($"PartBoxArrow_{i}", typeof(RectTransform));
            rootObj.layer = uiLayer;
            RectTransform root = (RectTransform)rootObj.transform;
            root.SetParent(rect, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;

            RectTransform arrow = CreateImage("Arrow", root, arrowSprite, arrowColor);
            RectTransform icon = iconSprite != null ? CreateImage("Icon", root, iconSprite, Color.white) : null;

            rootObj.SetActive(false);
            pool.Add(new Indicator { Root = root, Arrow = arrow, Icon = icon });
        }

        ApplySizes();
    }

    private static RectTransform CreateImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;

        return rt;
    }

    // 해상도가 바뀌면(창 크기 조절, Game View 해상도 변경) 크기를 다시 잡아야 한다.
    private int last_screen_height;
    private int last_screen_width;

    private void ApplySizes()
    {
        float scale = UiScale;
        float arrowPx = arrowSize * scale;
        float iconPx = iconSize * scale;

        foreach (Indicator ind in pool)
        {
            ind.Arrow.sizeDelta = new Vector2(arrowPx, arrowPx);

            // 화살표는 바깥쪽(가장자리 방향), 아이콘은 그 안쪽에 겹치지 않게 배치한다.
            // 화살표 root가 통째로 회전하지 않으므로 아이콘은 항상 똑바로 선 채로 남는다.
            ind.Arrow.anchoredPosition = Vector2.zero;
            if (ind.Icon != null)
            {
                ind.Icon.sizeDelta = new Vector2(iconPx, iconPx);
                ind.Icon.anchoredPosition = Vector2.zero;
            }
        }

        last_screen_width = Screen.width;
        last_screen_height = Screen.height;
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (Screen.width != last_screen_width || Screen.height != last_screen_height) ApplySizes();

        var boxes = RewardPickup.AlivePartBoxes;
        int used = 0;

        for (int i = 0; i < boxes.Count && used < pool.Count; i++)
        {
            RewardPickup box = boxes[i];
            if (box == null) continue;

            if (TryGetEdgePosition(box.transform.position, out Vector2 localPoint, out float angleDeg))
            {
                Indicator ind = pool[used];
                ind.Root.gameObject.SetActive(true);
                ind.Root.anchoredPosition = localPoint;
                ind.Arrow.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

                // 아이콘은 화살표 반대쪽(화면 안쪽)에 붙여 화살표를 가리지 않게 한다.
                if (ind.Icon != null)
                {
                    float offset = (arrowSize * 0.5f + iconSize * 0.55f) * UiScale;
                    float rad = (angleDeg + 90f) * Mathf.Deg2Rad; // angleDeg는 "위쪽 기준"이라 실제 방향으로 되돌린다
                    ind.Icon.anchoredPosition = new Vector2(-Mathf.Cos(rad), -Mathf.Sin(rad)) * offset;
                }

                used++;
            }
        }

        for (int i = used; i < pool.Count; i++)
        {
            if (pool[i].Root.gameObject.activeSelf) pool[i].Root.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 대상 world position이 화면 안에 있으면 false(표시 불필요), 화면 밖이면
    /// 화면 가장자리(여백 안쪽)로 클램프한 Canvas 로컬 좌표와 그 방향 각도를 채워 true를 반환한다.
    ///
    /// "화면 밖인지" 판정은 <b>화면 전체</b> 기준으로 하고, 클램프만 여백 안쪽으로 한다.
    /// 예전에는 판정도 여백 안쪽 사각형으로 해서, 화면에 멀쩡히 보이는(가장자리 근처의) 상자에도
    /// 화살표가 겹쳐 떴다.
    /// </summary>
    private bool TryGetEdgePosition(Vector3 worldPos, out Vector2 localPoint, out float angleDeg)
    {
        localPoint = Vector2.zero;
        angleDeg = 0f;

        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        float halfW = Screen.width * 0.5f;
        float halfH = Screen.height * 0.5f;

        // 직교 카메라로 항상 z=0 평면(카메라 앞)을 보므로 카메라 뒤 케이스는 별도 처리하지 않는다.
        float x = screenPos.x - halfW;
        float y = screenPos.y - halfH;

        // 화면 안에 보이면 표시하지 않는다.
        if (Mathf.Abs(x) <= halfW && Mathf.Abs(y) <= halfH) return false;

        float margin = edgeMargin * UiScale * UiCanvasLayout.PixelsPerCanvasUnit(this);
        float insetW = Mathf.Max(1f, halfW - margin);
        float insetH = Mathf.Max(1f, halfH - margin);

        float scaleX = Mathf.Abs(x) > 0.0001f ? insetW / Mathf.Abs(x) : float.MaxValue;
        float scaleY = Mathf.Abs(y) > 0.0001f ? insetH / Mathf.Abs(y) : float.MaxValue;
        float scale = Mathf.Min(1f, Mathf.Min(scaleX, scaleY));

        Vector2 dir = new Vector2(x, y);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        dir.Normalize();

        Vector2 clampedScreenPos = new Vector2(x * scale + halfW, y * scale + halfH);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, clampedScreenPos, null, out localPoint);
        angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f; // 화살표 스프라이트가 기본적으로 위(up)를 가리키므로 -90 보정
        return true;
    }

    /// <summary>외부 에셋 없이 위쪽을 가리키는 삼각형 화살표 스프라이트를 코드로 생성한다.</summary>
    private static Sprite BuildArrowSprite()
    {
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(1f, 1f, 1f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        const float baseY = 8f;
        const float apexY = size - 6f;
        const float halfWidthAtBase = size * 0.42f;
        int cx = size / 2;

        for (int y = 0; y < size; y++)
        {
            if (y < baseY || y > apexY) continue;
            float t = (y - baseY) / (apexY - baseY);
            float halfW = Mathf.Lerp(halfWidthAtBase, 0f, t);
            int xMin = Mathf.Max(0, Mathf.RoundToInt(cx - halfW));
            int xMax = Mathf.Min(size - 1, Mathf.RoundToInt(cx + halfW));
            for (int x = xMin; x <= xMax; x++) pixels[y * size + x] = Color.white;
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
