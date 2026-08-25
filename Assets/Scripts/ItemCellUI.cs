using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 칸(인벤토리·장착 슬롯·상점 보유 목록)의 <b>생김새를 한곳에서</b> 만든다.
///
/// 2026-08-18 사용자 요청으로 정비 화면과 상점 화면이 같은 규칙을 쓰게 되면서 뽑아냈다.
///  - 보유·장착 중인 아이템은 <b>아이콘만</b> 보여준다(아이콘 뒤에 별도 사각형을 깔지 않는다 -
///    칸 자체가 배경이다).
///  - <b>일반 등급이 아니면 등급색으로 강조</b>한다(<see cref="ItemGradeExtensions.ToCellColor"/>).
///  - 칸은 "테두리 아트(흰색 고정, <see cref="FrameSpriteName"/>) + 색 테두리 링(AccentRing)"
///    두 겹뿐이다. 캡션·아이콘은 <b>그 위에 바로</b> 그린다 - 사이에 별도로 채색한 사각형을
///    끼워 넣지 않는다(2026-08-21, 사용자 지적: "기존 UI 리소스 위에 회색 사각형을
///    덮어버리면 안 된다" - 정비 화면 "머리" 칸 스크린샷. 처음엔 안쪽 사각형의 색만
///    옅게 죽였는데, 그것도 "사각형이 하나 더 있다"는 문제 자체는 그대로였다).
///    등급/강조색은 테두리 아트에 직접 곱하면 안 보이므로(2026-08-13에 겪은 함정 - 스프라이트가
///    거의 검정이라 색을 곱하는 순간 죽는다) 전용 흰색 실루엣 링을 테두리 위에 덧그리는 방식으로만
///    표현한다. 이 링은 <see cref="FrameSpriteName"/>의 실제 베젤 두께·모양을 그대로 따르는
///    <see cref="UiIconLibrary.DeriveEdgeRing"/>로 만든다(2026-08-25 - 예전엔 캔버스 크기 기준
///    임의 사각 링(<see cref="UiIconLibrary.Frame"/>)이라 실제 둥근 베젤과 안 맞았다).
///    <b>임시방편</b>이다 - 사용자가 나중에 등급별로 실제 색이 다른 UI 리소스를 직접 올리면
///    이 tint 방식 자체가 필요 없어질 수 있다.
/// </summary>
public static class ItemCellUI
{
    private const string FrameSpriteName = "UI/Black_ui03";

    /// <summary>테두리 아트를 못 찾았을 때만 쓰는 대체 배경색(정상적인 경우엔 안 쓰인다).</summary>
    private static readonly Color CellBaseColor = new Color(0.10f, 0.10f, 0.12f, 1f);

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

    /// <summary>
    /// 칸의 공통 뼈대(테두리 아트 + 색 테두리 링 + 클릭 버튼). 돌려주는 것은 배경 이미지
    /// (프레임) 그 자체다 - 캡션·아이콘은 이 위에 바로 그려진다.
    ///
    /// 등급/강조색(<paramref name="color"/>)은 <see cref="FrameSpriteName"/>(거의 검정이라
    /// 색을 곱해도 안 보인다 - 2026-08-13에 겪은 함정)에 직접 입히지 않고, <see cref="UiIconLibrary.Frame"/>
    /// (흰색 실루엣 전용 링)으로 만든 별도 "AccentRing" 레이어에만 입힌다. 이 링은 테두리
    /// 자리에 겹쳐 그려질 뿐 안쪽 면을 덮지 않으므로, 캡션·아이콘은 항상 프레임 위에 직접
    /// 놓인다(2026-08-21, 사용자 지적: 캡션 밑에 별도로 채색한 사각형을 깔면 안 된다).
    /// </summary>
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
            frame.color = CellBaseColor; // 아트를 못 찾았을 때만 단색 배경으로 대체
        }

        var ringGo = new GameObject("AccentRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ringGo.transform.SetParent(cell.transform, false);
        var ringRect = (RectTransform)ringGo.transform;
        ringRect.anchorMin = Vector2.zero;
        ringRect.anchorMax = Vector2.one;
        ringRect.offsetMin = Vector2.zero;
        ringRect.offsetMax = Vector2.zero;

        Image ring = ringGo.GetComponent<Image>();
        // 2026-08-25 사용자 지적: 등급색 테두리가 실제 프레임 아트(둥근 모서리)와 안 맞았다 -
        // frameSprite의 실제 알파 실루엣·9-slice border 두께를 그대로 따르는 링으로 교체.
        ring.sprite = frameSprite != null ? UiIconLibrary.DeriveEdgeRing(frameSprite) : UiIconLibrary.Frame();
        ring.type = Image.Type.Sliced;
        ring.color = color;
        ring.raycastTarget = false;

        if (onClick != null)
        {
            Button button = cell.AddComponent<Button>();
            button.targetGraphic = frame; // 클릭 피드백도 프레임 자체가 받는다(별도 레이어 없음)
            button.onClick.AddListener(() => onClick());
        }

        return frame;
    }

    /// <summary>아이콘 칸 하나를 만든다.</summary>
    /// <param name="caption">칸 위에 작게 붙일 이름(슬롯 칸용). null이면 아이콘만.</param>
    /// <param name="iconBright">false면 아이콘을 흐리게 그린다(빈 슬롯 표시용).</param>
    public static Image CreateIconCell(RectTransform parent, string name, Sprite icon, Color color,
                                       string caption, bool iconBright, System.Action onClick)
    {
        Image background = CreateShell(parent, name, color, onClick, out GameObject cell);

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

        return background;
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
