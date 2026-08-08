using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 필드에 떨어진 부품 상자(RewardPickup.AlivePartBoxes)가 화면 밖에 있을 때, 화면 가장자리에
/// 그 방향을 가리키는 화살표를 띄운다. 화면 안에 있는 상자는 이미 필드 스프라이트로 보이므로
/// 화살표를 띄우지 않는다.
///
/// 사용법: Canvas 밑에 화면 전체를 덮는 RectTransform(anchorMin 0,0 / anchorMax 1,1)을 만들고
/// 이 스크립트를 붙이면 된다. 화살표 스프라이트는 외부 에셋 없이 코드로 생성한다(삼각형).
/// 화살표 인스턴스는 필요한 만큼만 켜지는 고정 풀(maxIndicators)로 관리한다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PartBoxIndicatorUI : MonoBehaviour
{
    [Tooltip("동시에 표시할 수 있는 최대 화살표 개수")]
    [SerializeField] private int maxIndicators = 8;

    [Tooltip("화면 가장자리에서 화살표를 얼마나 안쪽으로 띄울지 (픽셀)")]
    [SerializeField] private float edgeMargin = 60f;

    [Tooltip("화살표 크기 (픽셀)")]
    [SerializeField] private float arrowSize = 36f;

    [Tooltip("화살표 색상")]
    [SerializeField] private Color arrowColor = new Color(1f, 0.72f, 0.15f, 1f); // 부품 상자 아이콘과 어울리는 주황/골드

    private RectTransform rect;
    private Camera cam;
    private Sprite arrowSprite;
    private readonly List<RectTransform> pool = new List<RectTransform>();

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        arrowSprite = BuildArrowSprite();
        BuildPool();
    }

    private void BuildPool()
    {
        for (int i = 0; i < maxIndicators; i++)
        {
            GameObject go = new GameObject($"PartBoxArrow_{i}", typeof(RectTransform), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(rect, false);
            rt.sizeDelta = new Vector2(arrowSize, arrowSize);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image img = go.GetComponent<Image>();
            img.sprite = arrowSprite;
            img.color = arrowColor;
            img.raycastTarget = false;

            go.SetActive(false);
            pool.Add(rt);
        }
    }

    private void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        var boxes = RewardPickup.AlivePartBoxes;
        int used = 0;

        for (int i = 0; i < boxes.Count && used < pool.Count; i++)
        {
            RewardPickup box = boxes[i];
            if (box == null) continue;

            if (TryGetEdgePosition(box.transform.position, out Vector2 localPoint, out float angleDeg))
            {
                RectTransform indicator = pool[used];
                indicator.gameObject.SetActive(true);
                indicator.anchoredPosition = localPoint;
                indicator.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
                used++;
            }
        }

        for (int i = used; i < pool.Count; i++)
        {
            if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 대상 world position이 화면 안에 있으면 false(화살표 불필요), 화면 밖이면
    /// 화면 가장자리(margin 안쪽)로 클램프한 Canvas 로컬 좌표와 그 방향 각도를 채워 true를 반환한다.
    /// </summary>
    private bool TryGetEdgePosition(Vector3 worldPos, out Vector2 localPoint, out float angleDeg)
    {
        localPoint = Vector2.zero;
        angleDeg = 0f;

        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        float halfW = Screen.width * 0.5f;
        float halfH = Screen.height * 0.5f;

        float x = screenPos.x - halfW;
        float y = screenPos.y - halfH;

        // 직교 카메라로 항상 z=0 평면(카메라 앞)을 보므로 카메라 뒤 케이스는 별도 처리하지 않는다.
        float insetW = halfW - edgeMargin;
        float insetH = halfH - edgeMargin;

        float scaleX = Mathf.Abs(x) > 0.0001f ? insetW / Mathf.Abs(x) : float.MaxValue;
        float scaleY = Mathf.Abs(y) > 0.0001f ? insetH / Mathf.Abs(y) : float.MaxValue;
        float scale = Mathf.Min(1f, Mathf.Min(scaleX, scaleY));

        if (scale >= 1f) return false; // 이미 화면(여백) 안에 있음 - 화살표 불필요

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
