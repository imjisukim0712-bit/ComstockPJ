using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 칸(인벤토리·장착 슬롯·상점 보유 목록)의 <b>생김새를 한곳에서</b> 만든다.
///
/// 2026-08-18 사용자 요청으로 정비 화면과 상점 화면이 같은 규칙을 쓰게 되면서 뽑아냈다.
///  - 보유·장착 중인 아이템은 <b>아이콘만</b> 보여준다(아이콘 뒤에 별도 사각형을 깔지 않는다 -
///    칸 자체가 배경이다).
///  - <b>등급색은 아트 자체에 들어 있다</b>(<see cref="FrameSpritePath"/>). 칸은 <b>테두리 아트
///    한 겹</b>뿐이고, 캡션·아이콘은 그 위에 바로 그린다 - 사이에 별도로 채색한 사각형을
///    끼워 넣지 않는다(2026-08-21, 사용자 지적: "기존 UI 리소스 위에 회색 사각형을
///    덮어버리면 안 된다" - 정비 화면 "머리" 칸 스크린샷).
///  - <b>2026-08-25: 코드로 테두리를 그리던 방식을 폐기했다.</b> 사용자가 등급별 UI 아트를 직접
///    올려주면서 "원래는 테두리를 억지로 그렸었는데 이젠 테두리 빼고 저 이미지로 대체하면돼"라고
///    확정했다. 예전에는 흰색 실루엣 링(AccentRing)을 만들어 등급색으로 칠했는데
///    (<c>UiIconLibrary.DeriveEdgeRing</c>), 그 방식은 <b>등급이 없는 UI에서도 쓸데없는 테두리를
///    만들어냈다</b> - 설정 창의 화면모드/화면비율 버튼이 이 칸을 재사용하면서 의미 없는 테두리를
///    달고 있었고, 사용자가 이를 지적해 함께 걷어냈다.
///  - 강조(선택 노란색)·흐리기(빈 칸)는 <b>tint</b>로만 표현한다. 등급 아트는 검정이 아니라
///    등급색이 살아 있어 색을 곱해도 죽지 않는다(옛 검정 아트에서 겪던 2026-08-13 함정이 사라졌다).
///  - <b>2026-08-25: 칸 안쪽 배치를 <see cref="ItemCellLayout"/>에 넘겼다.</b> 이름표·아이콘의
///    사각형을 <b>여기서 정규화 앵커 상수로 정하지 않는다</b> - 9-slice 베젤은 rect 크기와 무관하게
///    고정 픽셀로 그려지므로, 같은 비율이 칸 크기에 따라 전혀 다른 결과가 된다. 1080p 실측에서
///    파츠 칸(175x98px)의 베젤이 사방 30px이라 아이콘이 아래 테두리에 25px 파묻혀 있었고,
///    사용자가 "각 파츠들 이미지가 파츠 UI 테두리부분에 안겹쳤으면 하는데 레이아웃 다시 넣어봐"라고
///    지적했다. 이제 칸 크기가 확정된 뒤 실제 베젤 두께에서 역산해 픽셀 단위로 배치한다.
/// </summary>
public static class ItemCellUI
{
    /// <summary>테두리 아트를 못 찾았을 때만 쓰는 대체 배경색(정상적인 경우엔 안 쓰인다).</summary>
    private static readonly Color CellBaseColor = new Color(0.10f, 0.10f, 0.12f, 1f);

    /// <summary>
    /// 슬롯 이름표 글자 크기 상한. 정비 화면의 "Inventory 5/20"(26pt)과 같게 맞춘 값이다
    /// (2026-08-25 사용자 기준: "최소 Inventory 라고 써있는 부분의 글씨 만큼의 크기는 가져야해").
    /// </summary>
    private const float CaptionFontSize = 26f;

    /// <summary>이름표 글자 크기 하한. 칸이 좁아도 이보다 작아지지 않는다.</summary>
    private const float CaptionMinFontSize = 16f;

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
    /// 등급별 프레임 아트의 Resources 경로. 2026-08-25 사용자가 등급별 UI 아트를 직접 올려주면서
    /// <b>코드로 테두리를 그리던 방식(<c>UiIconLibrary.DeriveEdgeRing</c>)을 폐기</b>했다 -
    /// 사용자 지시: "원래는 테두리를 억지로 그렸었는데 이젠 테두리 빼고 저 이미지로 대체하면돼".
    /// 등급색과 폴더 색의 대응은 <see cref="ItemGradeExtensions.ToColorHex"/>와 같다
    /// (일반=회색/Black, 희귀=파랑/Blue, 서사=보라/Purple, 유일=주황/Gold, 전설=빨강/Red).
    /// </summary>
    public static string FrameSpritePath(ItemGrade grade) => GradeSpritePath(grade, "ui03");

    /// <summary>
    /// 등급 -> 아트 폴더 이름. <c>Assets/Resources/UI/Grade/&lt;색&gt;/</c> 아래에 같은 세트가
    /// 색깔별로 들어 있다(2026-08-25 사용자 제공). 대응은 <see cref="ItemGradeExtensions.ToColorHex"/>와 같다.
    /// </summary>
    public static string GradeFolder(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Rare: return "Blue";        // 파랑
            case ItemGrade.Epic: return "Purple";      // 보라
            case ItemGrade.Unique: return "Gold";      // 주황
            case ItemGrade.Legendary: return "Red";    // 빨강
            default: return "Black";                   // 일반 = 등급색 없음
        }
    }

    /// <summary>
    /// 등급별 아트 한 장의 Resources 경로. <paramref name="suffix"/>는 색 접두어 뒤의 이름이다
    /// (<c>"ui03"</c> = 아이템 칸, <c>"ui01"</c> = 상점 품목 카드 배경 등).
    /// </summary>
    public static string GradeSpritePath(ItemGrade grade, string suffix)
    {
        string folder = GradeFolder(grade);
        return $"UI/Grade/{folder}/{folder}_{suffix}";
    }

    /// <summary>
    /// 등급별 아트를 불러온다. 없으면 null - 호출부가 기존 아트를 그대로 두면 된다.
    /// </summary>
    public static Sprite GradeSprite(ItemGrade grade, string suffix)
        => Resources.Load<Sprite>(GradeSpritePath(grade, suffix));

    /// <summary>
    /// 칸의 공통 뼈대(등급별 테두리 아트 + 클릭 버튼). 돌려주는 것은 배경 이미지 그 자체다 -
    /// 캡션·아이콘은 이 위에 바로 그려진다.
    ///
    /// <para><b>더 이상 테두리를 코드로 그리지 않는다</b>(2026-08-25). 등급색은 아트 자체에
    /// 들어 있으므로 별도 링 레이어가 없다. 예전에는 흰 실루엣 링을 만들어 등급색으로 칠했는데,
    /// 그 방식은 등급이 없는 UI(설정 창의 라디오 버튼 등)에서도 쓸데없는 테두리를 만들었다.</para>
    /// </summary>
    /// <param name="grade">칸의 등급. 일반(Normal)이면 등급색 없는 기본 아트를 쓴다.</param>
    /// <param name="tint">강조/흐리기 상태일 때만 준다(선택 노란색, 빈 칸 어둡게 등).
    /// null이면 아트 원색 그대로 그린다.</param>
    public static Image CreateShell(RectTransform parent, string name, ItemGrade grade, Color? tint,
                                    System.Action onClick, out GameObject cell)
    {
        cell = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cell.transform.SetParent(parent, false);

        Image frame = cell.GetComponent<Image>();
        Sprite frameSprite = Resources.Load<Sprite>(FrameSpritePath(grade));
        if (frameSprite != null)
        {
            frame.sprite = frameSprite;
            frame.type = Image.Type.Sliced;
            frame.color = tint ?? Color.white;
        }
        else
        {
            frame.color = tint ?? CellBaseColor; // 아트를 못 찾았을 때만 단색 배경으로 대체
        }

        if (onClick != null)
        {
            Button button = cell.AddComponent<Button>();
            button.targetGraphic = frame; // 클릭 피드백도 프레임 자체가 받는다(별도 레이어 없음)
            button.onClick.AddListener(() => onClick());
        }

        // 안쪽 배치는 전부 이 컴포넌트가 담당한다 - 칸 크기가 정해진 뒤에야 실제 테두리 두께를
        // 알 수 있으므로(9-slice는 rect와 무관하게 고정 픽셀로 그려진다) 생성 시점에 앵커 상수로
        // 정해 둘 수 없다. 2026-08-25 사용자 지적("파츠 이미지가 테두리에 겹친다")의 근본 수정이다.
        cell.AddComponent<ItemCellLayout>().SetFrame(frame);

        return frame;
    }

    /// <summary>아이콘 칸 하나를 만든다.</summary>
    /// <param name="caption">칸 위에 작게 붙일 이름(슬롯 칸용). null이면 아이콘만.</param>
    /// <param name="iconBright">false면 아이콘을 흐리게 그린다(빈 슬롯 표시용).</param>
    /// <param name="grade">칸의 등급. 등급별 테두리 아트를 고른다(일반이면 등급색 없는 기본 칸).</param>
    /// <param name="tint">강조(선택)·흐리기(빈 칸)일 때만 준다. null이면 아트 원색 그대로.</param>
    /// <param name="cornerCaption">true면 이름표를 위쪽 띠가 아니라 <b>우상단 배지</b>로 둔다
    /// (무기 소켓 칸의 번호 - ItemCellLayout.SetCornerCaption 참고). 아이콘이 칸 안쪽을 다 쓴다.</param>
    public static Image CreateIconCell(RectTransform parent, string name, Sprite icon, ItemGrade grade,
                                       Color? tint, string caption, bool iconBright, System.Action onClick,
                                       bool cornerCaption = false)
    {
        Image background = CreateShell(parent, name, grade, tint, onClick, out GameObject cell);
        ItemCellLayout layout = cell.GetComponent<ItemCellLayout>();
        if (layout != null) layout.SetCornerCaption(cornerCaption);

        bool hasCaption = !string.IsNullOrEmpty(caption);

        if (hasCaption)
        {
            var capGo = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            capGo.transform.SetParent(cell.transform, false);
            var capRect = (RectTransform)capGo.transform;
            // 사각형은 여기서 정하지 않는다 - <see cref="ItemCellLayout"/>가 칸 크기가 확정된 뒤
            // 실제 테두리 두께에서 역산해 잡는다. 예전에는 이 자리에서 정규화 앵커 상수
            // (y 0.50~0.90 등)를 박았는데, 9-slice 베젤은 rect 크기와 무관하게 고정 픽셀이라
            // 1080p 실측에서 이름표가 위 테두리를 20px 침범하고 있었다(칸 175x98, 베젤 30px).

            var cap = capGo.GetComponent<TextMeshProUGUI>();
            cap.text = caption;
            cap.alignment = TextAlignmentOptions.Center;
            cap.color = Color.white;
            cap.raycastTarget = false;
            // "Inventory 5/20"과 같은 크기(26pt)를 상한으로 두고, 하한도 충분히 올려 더는
            // 읽을 수 없을 만큼 줄어들지 않게 한다. 다만 "Magnetic Core"처럼 영문이 긴 이름은
            // 20pt에서도 말줄임표가 생겨, 전체 이름과 테두리 안전 여백을 함께 보장할 수 있도록
            // 영문 긴 이름에 한해 자동 조절이 16pt까지 내려갈 수 있게 한다.
            ApplyTextSizing(cap, CaptionFontSize);
            cap.fontSizeMin = CaptionMinFontSize;
            // ItemCellLayout의 2px 안쪽 여백에 더해 글리프 자체도 베젤에서 8px 떨어지게 한다.
            // 좌우 6px만 TMP margin으로 추가하면 아이콘 영역은 줄이지 않고 영문 이름만 안전하다.
            cap.margin = new Vector4(6f, 0f, 6f, 0f);
            // 슬롯 이름은 한 줄짜리 제목이다 - 줄바꿈을 켜 두면 좁은 칸에서 두 줄로 접혀
            // 이름표 띠를 위아래로 삐져나간다("UI 제작 규칙" 3번). NoWrap이면 자동 조절이
            // 폭에 맞춰 크기만 줄인다.
            cap.textWrappingMode = TextWrappingModes.NoWrap;

            if (layout != null) layout.SetCaption(capRect);
        }

        if (icon != null)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(cell.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            // 아이콘 사각형도 ItemCellLayout이 잡는다(이름표가 있으면 그 아래 띠의 정사각형,
            // 없으면 안전 영역 가운데의 정사각형). 예전의 y 0.05~0.48 앵커는 아래 베젤(0~30px)에
            // 25px이 파묻혀 있었다 - 사용자가 지적한 "파츠 이미지가 테두리에 겹친다"의 원인이다.

            var img = iconGo.GetComponent<Image>();
            img.sprite = icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = iconBright ? Color.white : new Color(1f, 1f, 1f, 0.28f);

            if (layout != null) layout.SetIcon(iconRect);
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
    /// <param name="square">true면 칸을 <b>정사각형</b>으로 만든다 - 가로/세로 중 작은 쪽에 맞춘 뒤
    /// 남는 자리는 비워 둔다(2026-08-26 사용자 지시: 무기 소켓 칸을 정사각형으로).
    /// <paramref name="fitRows"/>와 함께 주면 세로도 행 수에 맞춘 뒤 정사각형으로 줄인다.</param>
    public static void EnsureGrid(RectTransform container, Vector2 fallbackCellSize, int columns, int fitRows = 0,
                                  bool square = false)
    {
        GridLayoutGroup grid = container.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = container.gameObject.AddComponent<GridLayoutGroup>();

        // 칸 사이 간격(px). 2026-08-26 사용자가 "간격을 반 정도로"라고 해서 6 → 0까지 내렸다가,
        // <b>칸끼리 붙어 보인다</b>는 재지적을 받고 3으로 되돌렸다. 화면에 실제로 보이는 틈은
        // 이 값보다 넓다 - 칸 프레임 아트가 자기 rect 안쪽으로 약 3px 들어가 그려지기 때문이다
        // (1920 실측: 이 값이 6일 때 테두리와 테두리 사이가 12px, 3일 때 9px).
        const float spacing = 3f;
        int columnCount = Mathf.Max(1, columns);

        // ContentSizeFitter는 스크롤 뷰의 content일 때만 필요하다. 스크롤이 없는 격자에도 붙어
        // 있으면 "컨테이너 높이 → 칸 높이 → (fitter가) 컨테이너 높이" 되먹임이 생겨 화면을 열
        // 때마다 칸이 점점 납작해진다(2026-08-13 실측: rect 높이가 -106까지 내려갔다).
        bool insideScrollRect = container.GetComponentInParent<ScrollRect>() != null;
        ContentSizeFitter fitter = container.GetComponent<ContentSizeFitter>();

        if (!insideScrollRect && fitter != null)
        {
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            container.sizeDelta = new Vector2(container.sizeDelta.x, 0f); // fitter가 늘려 둔 높이 복귀
        }
        // <b>fitter가 없으면 sizeDelta를 건드리지 않는다</b>(2026-08-26). 예전에는 무조건
        // sizeDelta.y = 0으로 밀었는데, 그러면 <b>stretch 앵커 + 픽셀 offset</b>으로 잡아 둔
        // 컨테이너의 offsetMin/Max가 함께 뭉개진다(sizeDelta = offsetMax - offsetMin이라
        // 0으로 만들면 Unity가 두 값을 pivot 기준으로 다시 맞춘다). 상점의 격자를 배경 패널의
        // 9-slice 테두리 안쪽 픽셀로 앉히면서 드러났다 - 실측으로 아래쪽이 65~74px 삐져나왔다.
        // 늘려 놓는 주체가 fitter뿐이므로 fitter가 없을 때는 되돌릴 것도 없다.

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

                if (square)
                {
                    float side = Mathf.Min(cellWidth, cellHeight);
                    cellWidth = side;
                    cellHeight = side;
                }

                cellSize = new Vector2(cellWidth, cellHeight);
            }
        }

        grid.cellSize = cellSize;
        grid.spacing = new Vector2(spacing, spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;
        // 정사각형 칸은 컨테이너 폭을 다 못 채우므로 가운데로 모은다(왼쪽에 몰리면 오른쪽이
        // 통째로 비어 "빠진 칸"처럼 보인다). 그 외에는 예전대로 왼쪽 위부터 채운다.
        grid.childAlignment = square ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;

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

    /// <summary>
    /// 잠긴 아이템 아이콘을 "형태만 남은 단색 실루엣"으로 만든다(2026-08-26 사용자 지적:
    /// "실루엣만 보여야 한다니까 왜 더 선명해져. 회색으로 덮어").
    ///
    /// <para><b>왜 <c>Image.color</c> 틴트로는 안 되는가</b>: color는 원본 픽셀에 곱해지는
    /// 값이라, 밝은 회색을 주면 원본의 명암 대비(눈·윤곽선 등)가 오히려 더 선명하게 살아난다
    /// (어두운 색을 곱하면 반대로 뭉개져 안 보인다 - 이전 값 0.10이 그 경우였다). 곱연산으로는
    /// "밝으면서도 디테일이 안 보이는" 상태를 만들 수 없다.</para>
    ///
    /// <para><b>해법</b>: 아이콘 스프라이트를 <see cref="Mask"/>의 스텐실로만 쓰고(알파 모양만
    /// 취하고 원본 색은 안 보이게 <c>showMaskGraphic = false</c>) 그 안을 단색 자식 이미지로
    /// 채운다 - 진짜 실루엣(모양은 그대로, 색상 정보는 전혀 없음)이 된다.</para>
    /// </summary>
    public static void SetIconLockState(Image icon, bool unlocked, Color silhouetteColor)
    {
        if (icon == null) return;

        Mask mask = icon.GetComponent<Mask>();
        Transform fillTransform = icon.transform.Find("SilhouetteFill");

        if (unlocked)
        {
            icon.color = Color.white;
            if (mask != null) mask.enabled = false;
            if (fillTransform != null) fillTransform.gameObject.SetActive(false);
            return;
        }

        icon.color = Color.white; // 마스크 스텐실 용도 - 원본 색은 showMaskGraphic=false라 안 보인다
        if (mask == null) mask = icon.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        mask.enabled = true;

        Image fill;
        if (fillTransform != null)
        {
            fill = fillTransform.GetComponent<Image>();
            fillTransform.gameObject.SetActive(true);
        }
        else
        {
            var fillGo = new GameObject("SilhouetteFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(icon.transform, false);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill = fillGo.GetComponent<Image>();
            fill.raycastTarget = false;
        }
        fill.color = silhouetteColor;
    }
}
