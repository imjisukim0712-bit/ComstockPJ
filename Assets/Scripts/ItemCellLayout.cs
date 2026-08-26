using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 칸(<see cref="ItemCellUI"/>)의 <b>안쪽 배치를 실제 렌더된 테두리 두께에서 역산</b>한다.
/// 칸마다 하나씩 붙고, 칸 크기가 정해지거나 바뀔 때마다 이름표·아이콘의 사각형을 다시 잡는다.
///
/// <para><b>왜 필요한가</b>(2026-08-25 사용자 지적: "상점에서 여전히 이미지 어긋난다. 각 파츠들
/// 이미지가 파츠 UI 테두리부분에 안겹쳤으면 하는데 레이아웃 다시 넣어봐"). 예전에는 이름표·아이콘을
/// <b>정규화 앵커 상수</b>(아이콘 y 0.05~0.48 등)로 놓았다. 그런데 9-slice 코너는
/// <b>rect 크기와 무관하게 항상 <c>border / pixelsPerUnitMultiplier</c> 픽셀</b>로 그려지므로
/// (프로젝트 안내 "UI 제작 규칙" 4번, <see cref="UiSafeArea"/>), 같은 비율이 칸 크기에 따라
/// 전혀 다른 결과가 된다. 1080p 실측이 그 증거다 - 파츠 칸은 175x98px인데 `Black_ui03`의 베젤은
/// 사방 <b>30px</b>이라 세로로만 61%가 테두리였고, 아이콘(y 4.9~47.1px)은 아래 베젤(0~30px)에
/// <b>25px이나 파묻혀</b> 있었다. 이름표(49~88px)도 위 베젤(68~98px)을 20px 침범했다.</para>
///
/// <para><b>고친 방식은 두 갈래다.</b>
/// (1) <b>베젤을 칸 크기에 맞게 얇게 만든다</b> - <c>pixelsPerUnitMultiplier</c>로 9-slice 두께를
/// <see cref="TargetBorderPixels"/>까지 줄인다. 133px 아트를 위해 그려진 30px 테두리를 98px 칸에
/// 그대로 쓰는 것 자체가 비례가 안 맞는 것이고, 이 배율이 Unity가 그 용도로 주는 손잡이다.
/// 값은 <b>1 미만으로 내려가지 않으므로</b>(= 절대 두꺼워지지 않는다) 크게 늘여 쓰는 패널은
/// 아트 원래 두께를 유지한다.
/// (2) <b>남은 안쪽 영역을 픽셀 단위로 나눠 쓴다</b> - 앵커는 0~1 전체로 두고 offset(px)로
/// 밀어 넣어, 칸 크기가 바뀌어도 테두리와의 간격이 항상 같게 유지된다.</para>
///
/// 임의의 여백 숫자를 다시 만들지 않기 위해 <b>모든 값은 실제 스프라이트의 border에서 나온다</b> -
/// 아트를 바꾸면 이 코드를 건드리지 않고도 따라온다.
/// </summary>
[DisallowMultipleComponent]
public class ItemCellLayout : MonoBehaviour
{
    /// <summary>
    /// 9-slice 베젤이 화면에서 차지할 목표 두께(px). 아이템 칸은 실측 83~151px 높이라
    /// 10px이면 테두리로 또렷하게 보이면서도 안쪽에 이름표(26pt 한 줄 = 약 33px)와 아이콘이
    /// 함께 들어간다. (원본 아트 30px을 그대로 쓰면 98px 칸에서 위아래 60px이 테두리라
    /// 애초에 자리가 없다 - 12px로 뒀을 때는 이름표가 28px밖에 못 받아 글자가 하한 20pt까지
    /// 줄어들었다. 사용자 기준은 "최소 Inventory 만큼"(26pt)이다.)
    /// </summary>
    public const float TargetBorderPixels = 10f;

    /// <summary>베젤 안쪽으로 한 번 더 띄우는 여백(px). 테두리 선에 아슬아슬하게 닿는 것을 막는다.</summary>
    private const float InnerPadding = 2f;

    /// <summary>이름표가 안전 영역 높이에서 차지하는 비율. 아래 <see cref="CaptionHeight"/> 참고.</summary>
    private const float CaptionHeightRatio = 0.52f;

    /// <summary>
    /// 이름표 칸 높이의 하한/상한(px). 상한 38px은 <b>26pt 한 줄이 들어가는 높이</b>다 -
    /// TMP 자동 축소는 줄 높이(글자 크기의 약 1.45배)까지 칸에 들어와야 그 크기를 쓴다.
    /// 사용자가 정한 기준이 "최소 Inventory 라고 써있는 부분의 글씨 만큼"(26pt)이라
    /// <b>이름표 높이를 먼저 보장하고 남는 자리를 아이콘에 준다</b>(34px 띠로 뒀을 때는
    /// 23.5pt까지만 올라갔다). 세로로 긴 칸에서 이름표가 아이콘 자리를 다 먹지 않도록
    /// 여기서 멈춘다.
    /// </summary>
    private const float CaptionMinHeight = 26f;
    private const float CaptionMaxHeight = 38f;

    /// <summary>이름표와 아이콘 사이 간격(px).</summary>
    private const float RowGap = 2f;

    /// <summary>이름표를 살리려면 아이콘 쪽에 최소한 남아야 하는 높이(px).
    /// 이보다 얇아지면 이름표를 숨기고 아이콘만 보여준다(<see cref="Apply"/> 참고).</summary>
    private const float MinIconBandHeight = 20f;

    /// <summary>우상단 배지가 칸 안쪽에서 차지하는 한 변의 비율(<see cref="SetCornerCaption"/>).
    /// 도감·머리선택의 잠금 배지(칸의 22%x18%)와 비슷한 크기로 잡았다.</summary>
    private const float CornerBadgeRatio = 0.32f;

    /// <summary>이름표 좌우 TMP 여백을 이름표 폭의 몇 %로 둘지. <see cref="ApplyCaptionSideMargin"/> 참고.</summary>
    private const float CaptionSideMarginRatio = 0.04f;

    /// <summary>그 여백의 상한(px). 넓은 이름표에서는 기존 고정값(6px)과 같아진다.</summary>
    private const float CaptionSideMarginMax = 6f;

    /// <summary>
    /// 아이콘을 옆에 두는 배치로 갈 때 <b>이름표에 최소한 남아야 하는 폭</b>(px).
    ///
    /// <para>150px은 <b>실측으로 정한 값이다</b>(2026-08-25). 처음에 70px로 두어 상점 파츠 칸
    /// (안전 영역 151x74 → 이름표 75px)까지 나란히 배치로 보냈더니 아이콘은 34 → 74px로 두 배가
    /// 됐지만 이름표가 <b>단어 중간에서 잘려</b> "팔 장 / 갑", "Socke / t1", "Memo / ry",
    /// "Magne / tic Co…"처럼 읽혔다(TMP는 CJK를 아무 글자에서나 끊고, 영어 한 단어는 아예 못
    /// 끊어서 하한 20pt까지 줄어든다 - 실측 당시의 예시 문구이며 팔장갑·자기장 코어는
    /// 2026-08-26에 삭제됐다). 이름표 폭이 150px 이상이면 "Weapon Socket 1"·"Leg Armor"가
    /// 접히지 않고 26pt로 들어간다 - 정비 슬롯(안전 영역 295x74 → 이름표 219px)이 여기 해당한다.</para>
    /// </summary>
    private const float MinSideCaptionWidth = 150f;

    private Image frame;
    private RectTransform caption;
    private RectTransform icon;
    private RectTransform fill;

    /// <summary>이미 이 크기로 배치를 마쳤다는 표시. 매 프레임 다시 계산하지 않기 위한 것.</summary>
    private Vector2 applied_size = new Vector2(-1f, -1f);

    /// <summary>칸의 테두리 아트. 여기서 실제 베젤 두께를 읽고 필요하면 얇게 만든다.</summary>
    public void SetFrame(Image frameImage)
    {
        frame = frameImage;
        MarkDirty();
    }

    /// <summary>위쪽 이름표(슬롯 이름). 없으면 부르지 않는다.</summary>
    public void SetCaption(RectTransform rect)
    {
        caption = rect;
        MarkDirty();
    }

    /// <summary>
    /// 이름표를 <b>우상단 모서리 배지</b>로 둔다(2026-08-26 사용자 지시: 무기 소켓 칸을
    /// "정사각형 + 번호만 오른쪽 위, 소켓 글자 없이"). 이름표가 위쪽 띠를 차지하지 않으므로
    /// <b>아이콘이 칸 안쪽을 전부 쓴다</b> - 도감·상점 카드의 잠금 배지와 같은 관례다.
    /// </summary>
    public void SetCornerCaption(bool on)
    {
        if (corner_caption == on) return;
        corner_caption = on;
        MarkDirty();
    }

    private bool corner_caption;

    /// <summary>아이콘. 없으면 부르지 않는다.</summary>
    public void SetIcon(RectTransform rect)
    {
        icon = rect;
        MarkDirty();
    }

    /// <summary>칸 안쪽을 통째로 쓰는 요소(가운데 글자만 있는 칸 등).</summary>
    public void SetFill(RectTransform rect)
    {
        fill = rect;
        MarkDirty();
    }

    /// <summary>지금 칸의 안전 영역(테두리 안쪽) 크기(px). 검증·측정용.</summary>
    public Vector2 InnerSize
    {
        get
        {
            Vector2 size = ((RectTransform)transform).rect.size;
            Vector4 bezel = ResolveBezel();
            return new Vector2(
                size.x - bezel.x - bezel.z - InnerPadding * 2f,
                size.y - bezel.y - bezel.w - InnerPadding * 2f);
        }
    }

    private void MarkDirty() => applied_size = new Vector2(-1f, -1f);

    private void OnEnable() => MarkDirty();

    // GridLayoutGroup이 칸 크기를 정하는 것은 생성 프레임 이후이고, 해상도가 바뀌면 또 달라진다.
    private void OnRectTransformDimensionsChange() => MarkDirty();

    private void LateUpdate() => Apply();

    private void Apply()
    {
        var rect = (RectTransform)transform;
        Vector2 size = rect.rect.size;
        if (size.x <= 1f || size.y <= 1f) return;                       // 아직 레이아웃 전
        if ((size - applied_size).sqrMagnitude < 0.01f) return;         // 이미 이 크기로 배치했다
        applied_size = size;

        Vector4 bezel = ResolveBezel();
        float left = bezel.x + InnerPadding;
        float bottom = bezel.y + InnerPadding;
        float right = bezel.z + InnerPadding;
        float top = bezel.w + InnerPadding;

        float innerWidth = size.x - left - right;
        float innerHeight = size.y - bottom - top;

        // <b>칸이 자기 테두리보다도 작은 경우</b>(작은 창 + 칸이 많을 때). 예전에는 여기서 그냥
        // return해서 <b>자식들이 기본 위치(칸 전체)에 겹친 채 남았다</b> - 2026-08-26 사용자 지적
        // "패널 안 카드끼리 겹친다"의 원인 중 하나였다. 실측: 1366x768 · 무기 소켓 6칸에서
        // 칸 높이가 23px까지 눌려 이름표와 무기 아이콘이 완전히 포개졌다.
        // 이럴 때는 이름표를 숨기고 아이콘만 칸 전체에 채운다(사용자 확정: "작은 창에서는
        // 이름표를 숨긴다") - 무엇이 들어 있는지는 아이콘으로 알 수 있다.
        if (innerWidth <= 4f || innerHeight <= 4f)
        {
            SetCaptionVisible(false);
            if (icon != null) SetInset(icon, 0f, 0f, 0f, 0f);
            return;
        }

        if (fill != null) SetInset(fill, left, bottom, right, top);

        // 우상단 배지 모드 - 이름표가 위쪽 띠를 안 먹으므로 아이콘이 안쪽을 전부 쓴다.
        // 칸이 아무리 납작해져도 둘이 세로 자리를 다투지 않아 아래의 "이름표 숨김" 검사도 필요 없다.
        if (corner_caption && caption != null)
        {
            SetCaptionVisible(true);
            SetCaptionWrapping(false);
            if (icon != null) SetInset(icon, left, bottom, right, top);

            float badge = Mathf.Min(innerWidth, innerHeight) * CornerBadgeRatio;
            SetInset(caption, size.x - right - badge, size.y - top - badge, right, top);
            ApplyCaptionSideMargin(badge);
            return;
        }

        // 가로로 넓은 칸은 아이콘을 왼쪽·이름표를 오른쪽에 나란히 둔다(아래 UseSideBySide 참고).
        // <b>이 배치는 아래의 세로 공간 검사를 하지 않는다</b> - 이름표가 아이콘 옆에 있어서
        // 세로 자리를 다투지 않기 때문이다. 검사를 여기까지 적용했더니 1920x1080의 무기 칸
        // (371x67, 안쪽 높이 43px)에서 "Socket 1/2"가 통째로 사라졌다(2026-08-26 실측 회귀).
        if (caption != null && icon != null && UseSideBySide(innerWidth, innerHeight))
        {
            SetCaptionVisible(true);
            float side = innerHeight;                                    // 아이콘은 안전 영역 높이를 다 쓴다
            SetInset(icon, left, bottom, size.x - left - side, top);
            SetInset(caption, left + side + RowGap, bottom, right, top);
            SetCaptionWrapping(true);                                    // 좁고 높은 칸이라 접히는 게 낫다
            ApplyCaptionSideMargin(innerWidth - side - RowGap);
            return;
        }

        // 위아래로 쌓는 배치에서는 이름표와 아이콘이 <b>둘 다</b> 들어갈 만큼은 되어야 이름표를
        // 살린다. 아슬아슬하게 남기면 이름표 띠가 아이콘 자리를 다 먹어 서로 겹친다.
        //
        // 기준값은 1080p 설계 픽셀이므로 <b>화면 배율을 곱해야 한다</b> - 작은 창에서는 글자도
        // 같은 배율로 작아지기 때문이다(ResponsiveTextScaler와 같은 배율·같은 상하한).
        // 안 곱했더니 1366x768에서 파츠 칸(안쪽 44.9px)까지 이름표가 사라졌다 - 그 칸은
        // 원래 잘 들어가던 자리라 과잉이었다(2026-08-26 실측).
        float scale = Mathf.Clamp(Screen.height / ResponsiveTextScaler.DesignHeight, 0.6f, 2f);
        bool captionFits = innerHeight >= (CaptionMinHeight + RowGap + MinIconBandHeight) * scale;
        bool hasCaption = caption != null && captionFits;
        SetCaptionVisible(hasCaption);

        SetCaptionWrapping(false);                                       // 위아래 배치는 한 줄 제목
        float captionHeight = hasCaption ? CaptionHeight(innerHeight) : 0f;

        if (hasCaption)
        {
            // 이름표는 안전 영역의 맨 위 띠. 아래쪽 여백 = 칸 높이에서 위 여백과 띠 높이를 뺀 값.
            SetInset(caption, left, size.y - top - captionHeight, right, top);
            ApplyCaptionSideMargin(innerWidth);
        }

        if (icon != null)
        {
            float bandBottom = bottom;
            float bandHeight = hasCaption ? innerHeight - captionHeight - RowGap : innerHeight;
            if (bandHeight <= 2f) bandHeight = innerHeight;              // 이름표가 다 먹었으면 겹치더라도 표시

            // 아이콘은 안전 영역 안에서 정사각형으로, 남은 띠의 가운데에 놓는다.
            // preserveAspect가 켜져 있어 실제 그림은 이 사각형 안에 비율 그대로 들어간다.
            float side = Mathf.Min(innerWidth, bandHeight);
            float sideMarginX = (innerWidth - side) * 0.5f;
            float sideMarginY = (bandHeight - side) * 0.5f;

            SetInset(icon,
                     left + sideMarginX,
                     bandBottom + sideMarginY,
                     right + sideMarginX,
                     size.y - bandBottom - bandHeight + sideMarginY);
        }
    }

    /// <summary>
    /// 아이콘을 왼쪽에 세로 꽉 채우고 이름표를 그 옆에 둘지 판정한다(2026-08-25 사용자 지시:
    /// "그걸 한번 하고 보여줘야지").
    ///
    /// <para>위아래로 쌓으면 아이콘이 <b>남은 세로 자리</b>(= 안전 높이 - 이름표 띠)만큼밖에 못
    /// 커진다. 파츠 칸은 가로로 아주 넓고 세로로 짧아서(실측 안전 영역 151x74 / 295x74) 이 값이
    /// 34px에 불과했다. 나란히 놓으면 아이콘이 <b>안전 높이 전체</b>(74px)를 쓴다.</para>
    ///
    /// <para>조건은 <b>이름표에 남는 폭</b>이다 - 아이콘이 정사각형으로 높이를 다 쓰고도
    /// <see cref="MinSideCaptionWidth"/>가 남을 때만 나란히 놓는다. 세로로 긴 칸(무기 칸:
    /// 안전 영역 151x127)에서는 아이콘이 폭을 거의 다 먹어 이름표 자리가 안 남으므로 위아래
    /// 배치로 돌아간다 - 그쪽은 원래도 아이콘이 87px로 충분히 크다.</para>
    /// </summary>
    private static bool UseSideBySide(float innerWidth, float innerHeight)
        => innerWidth - innerHeight - RowGap >= MinSideCaptionWidth;

    /// <summary>
    /// 이름표 줄바꿈. 위아래 배치는 띠가 한 줄 높이뿐이라 <b>NoWrap</b>이어야 자동 축소가
    /// 폭에만 반응한다("UI 제작 규칙" 3번). 나란히 배치는 이름표 칸이 좁고 높아서 <b>접히는 것이
    /// 유리하다</b> - 한 줄로 묶으면 "Weapon Socket 1"이 폭에 맞춰 잘게 줄어들지만, 두 줄로 접으면
    /// 남는 높이를 써서 더 큰 글자를 유지한다.
    /// </summary>
    private void SetCaptionWrapping(bool wrap)
    {
        if (caption == null) return;

        var text = caption.GetComponent<TMPro.TMP_Text>();
        if (text == null) return;

        TMPro.TextWrappingModes wanted = wrap ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
        if (text.textWrappingMode != wanted) text.textWrappingMode = wanted;
    }

    /// <summary>
    /// 이름표 글리프가 좌우 테두리에 붙지 않도록 두는 <b>TMP 자체 여백</b>을 이름표 폭에
    /// 비례시킨다(2026-08-26).
    ///
    /// <para><see cref="ItemCellUI"/>가 만들 때 6px 고정으로 주는데, 그 값은 정비 슬롯의
    /// 넓은 이름표(219px)를 기준으로 정한 것이라 <b>좁은 칸에서는 폭의 5분의 1을 먹는다.</b>
    /// 실측(1366x768 · 무기 소켓 6칸 한 줄): 이름표 폭 64.3px 중 12px이 여백이라 쓸 수 있는
    /// 폭이 52.3px뿐이었고, 자동 크기가 하한(11.38pt)까지 내려가고도 모자라
    /// <b>"Socket 1"이 "Socket…"으로 잘렸다</b>. 여백을 없애면 같은 칸에서 13.9pt로 <b>오히려
    /// 커지면서</b> 전부 들어간다 - 여백이 글자를 키울 자리를 뺏고 있었던 것이다.</para>
    ///
    /// <para>비율(4%)은 원래 기준을 유지하도록 잡았다 - 219px 이름표에서 8.8px이라 상한 6px에
    /// 걸려 <b>기존 화면은 값이 그대로다</b>. 좁아질수록 함께 줄어 64px에서는 2.6px이 된다.</para>
    /// </summary>
    /// <summary>이름표를 켜고 끈다. 칸이 너무 작아 이름표와 아이콘이 겹칠 때 끈다
    /// (<see cref="Apply"/> 참고). 컴포넌트는 그대로 두고 오브젝트만 비활성화하므로
    /// 칸이 다시 커지면 그대로 되살아난다.</summary>
    private void SetCaptionVisible(bool visible)
    {
        if (caption == null) return;
        if (caption.gameObject.activeSelf != visible) caption.gameObject.SetActive(visible);
    }

    private void ApplyCaptionSideMargin(float captionWidth)
    {
        if (caption == null || captionWidth <= 0f) return;

        var text = caption.GetComponent<TMPro.TMP_Text>();
        if (text == null) return;

        float side = Mathf.Clamp(captionWidth * CaptionSideMarginRatio, 1f, CaptionSideMarginMax);
        Vector4 wanted = new Vector4(side, text.margin.y, side, text.margin.w);
        if ((text.margin - wanted).sqrMagnitude > 0.01f) text.margin = wanted;
    }

    private static float CaptionHeight(float innerHeight)
    {
        float wanted = Mathf.Clamp(innerHeight * CaptionHeightRatio, CaptionMinHeight, CaptionMaxHeight);
        // 아이콘 자리를 최소한 남긴다(이름표만 있는 칸은 아이콘이 null이라 이 clamp가 무해하다).
        return Mathf.Min(wanted, innerHeight - RowGap);
    }

    /// <summary>
    /// 테두리 아트의 <b>실제 렌더 두께</b>(px, left/bottom/right/top)를 돌려준다. 두꺼우면
    /// <c>pixelsPerUnitMultiplier</c>를 올려 <see cref="TargetBorderPixels"/>까지 얇게 만든 뒤
    /// 그 결과를 돌려준다(배율은 1 미만으로 내려가지 않아 테두리가 두꺼워지는 일은 없다).
    /// </summary>
    private Vector4 ResolveBezel()
    {
        if (frame == null || frame.sprite == null) return Vector4.zero;
        if (frame.type != Image.Type.Sliced && frame.type != Image.Type.Tiled) return Vector4.zero;

        Vector4 border = frame.sprite.border; // (left, bottom, right, top) - 텍스처 px
        if (border == Vector4.zero) return Vector4.zero;

        float thickest = Mathf.Max(Mathf.Max(border.x, border.z), Mathf.Max(border.y, border.w));
        float multiplier = Mathf.Max(1f, thickest / TargetBorderPixels);
        frame.pixelsPerUnitMultiplier = multiplier;

        return border / multiplier;
    }

    /// <summary>
    /// 앵커를 칸 전체(0~1)로 두고 <b>offset(px)</b>으로만 밀어 넣는다 - 이렇게 하면 칸 크기가
    /// 어떻게 바뀌어도 테두리와의 간격이 픽셀 단위로 일정하다(정규화 앵커로는 불가능하다).
    /// </summary>
    private static void SetInset(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
}
