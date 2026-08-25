using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 9-slice 배경 아트의 <b>실제 테두리</b>를 피한 안전 영역을 계산한다(2026-08-25, 다국어 폴리싱).
///
/// <para><b>왜 필요한가</b>: "UI 제작 규칙"은 글자 여백을 임의의 숫자(0.05 같은)로 정하지 말고
/// 실제 아트의 테두리 두께에서 역산하라고 정한다. 한글은 짧아서 넘치지 않던 문구가 영어로 바뀌자
/// 여러 화면에서 테두리를 침범했다(2026-08-25 사용자 지적 - 정비 화면 5곳).</para>
///
/// <para><b>핵심</b>: 9-slice 스프라이트의 코너는 <b>rect 크기와 무관하게 항상
/// <c>border / pixelsPerUnitMultiplier</c> 픽셀로</b> 그려진다. 그래서 안전 여백 비율은
/// "텍스처 안에서의 border 비율"이 아니라 <b>실제 렌더 픽셀 ÷ 실제 rect 크기</b>로 구해야 한다.
/// 예: <c>Purple_ui02</c>(982px 폭, border 75px)를 1804px 폭으로 늘려 쓰면 뾰족한 끝은
/// 여전히 75px이므로 4.2%이지, 텍스처 비율인 7.6%가 아니다.
/// <see cref="ItemCellUI"/>의 칸은 아트와 렌더 크기가 비슷해 텍스처 비율로도 맞았지만,
/// 크게 늘여 쓰는 패널에서는 이 차이가 눈에 띈다.</para>
/// </summary>
public static class UiSafeArea
{
    /// <summary>
    /// 배경 이미지의 테두리 두께를 <b>그 RectTransform 크기 기준 0~1 비율</b>로 돌려준다.
    /// 반환값은 <c>(left, bottom, right, top)</c>이며, Sliced가 아니거나 정보가 없으면 0이다.
    /// </summary>
    public static Vector4 GetBorderRatio(Image background)
    {
        if (background == null || background.sprite == null) return Vector4.zero;
        if (background.type != Image.Type.Sliced && background.type != Image.Type.Tiled) return Vector4.zero;

        Vector4 border = background.sprite.border; // (left, bottom, right, top) - 텍스처 픽셀
        if (border == Vector4.zero) return Vector4.zero;

        float ppu = Mathf.Max(0.0001f, background.pixelsPerUnitMultiplier);
        Rect rect = ((RectTransform)background.transform).rect;
        if (rect.width <= 0f || rect.height <= 0f) return Vector4.zero;

        return new Vector4(
            (border.x / ppu) / rect.width,
            (border.y / ppu) / rect.height,
            (border.z / ppu) / rect.width,
            (border.w / ppu) / rect.height);
    }

    /// <summary>
    /// <paramref name="content"/>의 앵커를 <b>부모 배경의 테두리 안쪽으로</b> 밀어 넣는다.
    /// 이미 안쪽에 있으면 건드리지 않는다(디자인 의도를 함부로 넓히지 않기 위해 clamp만 한다).
    /// </summary>
    /// <param name="padding">테두리에 딱 붙지 않도록 추가로 띄울 비율(0~1). 기본 1%.</param>
    /// <param name="vertical">false면 좌우만 맞춘다. 얇은 배너처럼 세로 테두리가 장식용 베벨이라
    /// 그대로 써도 되는 경우에 쓴다.</param>
    public static void ClampIntoBackground(RectTransform content, float padding = 0.01f, bool vertical = true)
    {
        if (content == null) return;

        var parent = content.parent as RectTransform;
        if (parent == null) return;

        Image bg = parent.GetComponent<Image>();
        Vector4 b = GetBorderRatio(bg);
        if (b == Vector4.zero) return;

        Vector2 min = content.anchorMin;
        Vector2 max = content.anchorMax;

        min.x = Mathf.Max(min.x, b.x + padding);
        max.x = Mathf.Min(max.x, 1f - b.z - padding);

        if (vertical)
        {
            min.y = Mathf.Max(min.y, b.y + padding);
            max.y = Mathf.Min(max.y, 1f - b.w - padding);
        }

        // 앵커가 뒤집히면(테두리가 너무 두꺼운 아주 작은 패널) 손대지 않는다.
        if (min.x >= max.x) { min.x = content.anchorMin.x; max.x = content.anchorMax.x; }
        if (min.y >= max.y) { min.y = content.anchorMin.y; max.y = content.anchorMax.y; }

        content.anchorMin = min;
        content.anchorMax = max;
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// TMP 글리프가 9-slice 장식 테두리 위에 올라가지 않도록 실제 border 픽셀만큼 margin을 준다.
    /// RectTransform이 박스 안에 있더라도 글자가 베젤에 너무 붙는 문제를 별도로 막는다.
    /// 여러 번 호출해도 여백이 누적되지 않도록 기존 값과 큰 쪽만 사용한다.
    /// </summary>
    public static void ApplyTextMargins(TMP_Text text, Image background, float extraPadding = 8f,
                                        bool vertical = false)
    {
        if (text == null || background == null || background.sprite == null) return;

        Vector4 border = background.sprite.border; // (left, bottom, right, top)
        float ppu = Mathf.Max(0.0001f, background.pixelsPerUnitMultiplier);
        float extra = Mathf.Max(0f, extraPadding);
        Vector4 old = text.margin; // TMP 순서: (left, top, right, bottom)

        float left = Mathf.Max(old.x, border.x / ppu + extra);
        float right = Mathf.Max(old.z, border.z / ppu + extra);
        float top = vertical ? Mathf.Max(old.y, border.w / ppu + extra) : old.y;
        float bottom = vertical ? Mathf.Max(old.w, border.y / ppu + extra) : old.w;
        text.margin = new Vector4(left, top, right, bottom);
    }

    /// <summary>
    /// 씬 관례인 <c>TextName_BG</c> 형제 Image를 찾아 <see cref="ApplyTextMargins"/>를 적용한다.
    /// 배경이 없으면 조용히 넘어가므로 코드 생성 UI와 씬 UI 양쪽에서 안전하게 호출할 수 있다.
    /// </summary>
    public static void ApplyTextMarginsFromSibling(TMP_Text text, float extraPadding = 8f,
                                                   bool vertical = false)
    {
        if (text == null || text.transform.parent == null) return;

        Transform bgTransform = text.transform.parent.Find(text.name + "_BG");
        if (bgTransform == null) return;
        ApplyTextMargins(text, bgTransform.GetComponent<Image>(), extraPadding, vertical);
    }
}
