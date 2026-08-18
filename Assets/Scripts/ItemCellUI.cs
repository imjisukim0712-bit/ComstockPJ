using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 칸(인벤토리·장착 슬롯·상점 보유 목록)의 <b>생김새를 한곳에서</b> 만든다.
///
/// 2026-08-18 사용자 요청으로 정비 화면과 상점 화면이 같은 규칙을 쓰게 되면서 뽑아냈다.
///  - 보유·장착 중인 아이템은 <b>아이콘만</b> 보여준다(아이콘 뒤에 별도 사각형을 깔지 않는다 -
///    칸 자체가 배경이다).
///  - <b>일반 등급이 아니면 칸을 등급색으로</b> 칠한다(<see cref="ItemGradeExtensions.ToCellColor"/>).
///  - 칸은 "테두리 아트(흰색 고정) + 안쪽 상태색" 두 겹이다. 한 겹으로 하면 테두리 스프라이트가
///    거의 검정이라 색을 곱하는 순간 등급색·강조색이 전부 검게 죽는다(2026-08-13에 겪은 함정).
/// </summary>
public static class ItemCellUI
{
    private const string FrameSpriteName = "UI/Black_ui03";

    /// <summary>
    /// 칸 안 글씨의 자동 크기 조절 설정. Canvas가 ConstantPixelSize라 해상도에 따라 고정 픽셀
    /// 폰트가 칸을 넘친다(프로젝트 안내.md 참고). 자동 조절만으로는 <b>최소 크기에서도 안 들어가는
    /// 긴 글이 그대로 칸 밖으로 흘러나오므로</b>(사용자 지적) 넘치면 잘라내도록 함께 지정한다.
    /// </summary>
    public static void ApplyTextSizing(TMP_Text text, float maxFontSize = 42f)
    {
        text.richText = true;
        text.enableAutoSizing = true;
        text.fontSizeMin = 6f;
        text.fontSizeMax = maxFontSize;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    /// <summary>칸의 공통 뼈대(테두리 아트 + 안쪽 상태색 + 클릭 버튼). 돌려주는 것은 <b>안쪽</b> 이미지다.</summary>
    public static Image CreateShell(RectTransform parent, string name, Color color,
                                    System.Action onClick, out GameObject cell)
    {
        cell = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cell.transform.SetParent(parent, false);

        Image frame = cell.GetComponent<Image>();
        Sprite frameSprite = Resources.Load<Sprite>(FrameSpriteName);
        if (frameSprite != null)
        {
            frame.sprite = frameSprite;
            frame.type = Image.Type.Sliced;
            frame.color = Color.white;
        }
        else
        {
            frame.color = color; // 아트를 못 찾으면 단색 칸으로 동작
        }

        var stateGo = new GameObject("State", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        stateGo.transform.SetParent(cell.transform, false);
        var stateRect = (RectTransform)stateGo.transform;
        stateRect.anchorMin = new Vector2(0.07f, 0.07f);
        stateRect.anchorMax = new Vector2(0.93f, 0.93f);
        stateRect.offsetMin = Vector2.zero;
        stateRect.offsetMax = Vector2.zero;

        Image image = stateGo.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false; // 클릭은 바깥 칸(프레임)이 받는다

        if (onClick != null)
        {
            Button button = cell.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());
        }

        return image;
    }

    /// <summary>아이콘 칸 하나를 만든다.</summary>
    /// <param name="caption">칸 위에 작게 붙일 이름(슬롯 칸용). null이면 아이콘만.</param>
    /// <param name="iconBright">false면 아이콘을 흐리게 그린다(빈 슬롯 표시용).</param>
    public static Image CreateIconCell(RectTransform parent, string name, Sprite icon, Color color,
                                       string caption, bool iconBright, System.Action onClick)
    {
        Image state = CreateShell(parent, name, color, onClick, out GameObject cell);

        bool hasCaption = !string.IsNullOrEmpty(caption);

        if (hasCaption)
        {
            var capGo = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            capGo.transform.SetParent(cell.transform, false);
            var capRect = (RectTransform)capGo.transform;
            capRect.anchorMin = new Vector2(0.06f, 0.66f);
            capRect.anchorMax = new Vector2(0.94f, 0.97f);
            capRect.offsetMin = Vector2.zero;
            capRect.offsetMax = Vector2.zero;

            var cap = capGo.GetComponent<TextMeshProUGUI>();
            cap.text = caption;
            cap.alignment = TextAlignmentOptions.Center;
            cap.color = Color.white;
            cap.raycastTarget = false;
            ApplyTextSizing(cap);
        }

        if (icon != null)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(cell.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = hasCaption ? new Vector2(0.22f, 0.06f) : new Vector2(0.16f, 0.16f);
            iconRect.anchorMax = hasCaption ? new Vector2(0.78f, 0.62f) : new Vector2(0.84f, 0.84f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            var img = iconGo.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = iconBright ? Color.white : new Color(1f, 1f, 1f, 0.28f);
        }

        return state;
    }

    /// <summary>
    /// 컨테이너에 GridLayoutGroup이 없으면 붙이고 칸 크기/열 수를 맞춘다.
    ///
    /// Canvas가 ConstantPixelSize라 Game View 해상도에 따라 캔버스 픽셀 크기가 크게 달라진다
    /// (실측 640x480 ~ 3840x2160). 칸 크기를 고정 픽셀로 두면 해상도에 따라 칸이 우스꽝스럽게
    /// 커지거나 작아지므로, 컨테이너의 실제 폭에서 열 수에 맞춰 계산한다.
    /// </summary>
    /// <param name="fitRows">
    /// 0보다 크면 칸 높이도 컨테이너 높이를 이 행 수로 나눠 맞춘다(스크롤이 없는 격자용).
    /// 0이면 가로세로 비율만 유지한다 - 스크롤이 있어 세로로 넘쳐도 되는 경우.
    /// </param>
    public static void EnsureGrid(RectTransform container, Vector2 fallbackCellSize, int columns, int fitRows = 0)
    {
        GridLayoutGroup grid = container.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = container.gameObject.AddComponent<GridLayoutGroup>();

        const float spacing = 6f;
        int columnCount = Mathf.Max(1, columns);

        // ContentSizeFitter는 스크롤 뷰의 content일 때만 필요하다. 스크롤이 없는 격자에도 붙어
        // 있으면 "컨테이너 높이 → 칸 높이 → (fitter가) 컨테이너 높이" 되먹임이 생겨 화면을 열
        // 때마다 칸이 점점 납작해진다(2026-08-13 실측: rect 높이가 -106까지 내려갔다).
        bool insideScrollRect = container.GetComponentInParent<ScrollRect>() != null;
        ContentSizeFitter fitter = container.GetComponent<ContentSizeFitter>();

        if (!insideScrollRect)
        {
            if (fitter != null) fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            container.sizeDelta = new Vector2(container.sizeDelta.x, 0f); // 앵커가 정한 높이로 복귀
        }

        Vector2 cellSize = fallbackCellSize;
        float availableWidth = container.rect.width;
        if (availableWidth > 1f && fallbackCellSize.x > 0f)
        {
            float cellWidth = (availableWidth - spacing * (columnCount - 1)) / columnCount;
            if (cellWidth > 1f)
            {
                float aspect = fallbackCellSize.y / fallbackCellSize.x;
                float cellHeight = cellWidth * aspect;

                if (fitRows > 0)
                {
                    float availableHeight = container.rect.height;
                    if (availableHeight > 1f)
                    {
                        float fitted = (availableHeight - spacing * (fitRows - 1)) / fitRows;
                        if (fitted > 1f) cellHeight = fitted;
                    }
                }

                cellSize = new Vector2(cellWidth, cellHeight);
            }
        }

        grid.cellSize = cellSize;
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        grid.childAlignment = TextAnchor.UpperLeft;

        if (insideScrollRect)
        {
            if (fitter == null) fitter = container.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    /// <summary>
    /// 컨테이너의 자식 칸을 전부 지운다. Destroy()는 프레임 끝에야 실제로 파괴되므로 같은 프레임에
    /// 두 번 갱신하면 살아 있는 이전 칸 위에 새 칸이 겹쳐 쌓인다(2026-08-13 실측: 슬롯이 9개가
    /// 아니라 18개로 잡혔다). 부모에서 먼저 떼어내 childCount를 즉시 정확하게 만든다.
    /// DestroyImmediate는 물리 트리거 콜백 중에 금지되어 쓸 수 없다.
    /// </summary>
    public static void ClearChildren(RectTransform container)
    {
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            Object.Destroy(child);
        }
    }
}
