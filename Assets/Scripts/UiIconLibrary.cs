using UnityEngine;

/// <summary>
/// 아트 에셋이 아직 없는 UI 장식(자물쇠, 테두리)을 <b>코드로 그려서</b> 돌려준다.
///
/// <b>진짜 아트로 교체하는 법</b>: `Assets/Resources/UI/`에 아래 이름으로 PNG를 넣으면
/// 코드를 건드리지 않고 자동으로 교체된다(텍스처 임포트 설정은 Sprite(2D and UI)).
///   Lock_icon.png   — 잠금 버튼의 자물쇠
///   Stamp_frame.png — "구매 완료" 스탬프 테두리(9-slice 권장)
///
/// <see cref="PartIconLibrary"/>가 파츠 아이콘에 쓰는 것과 같은 방식이다(2026-08-18 Phase B에서
/// 확립). 임시 도형은 전부 <b>흰색 단색 실루엣</b>이라 Image.color로 아무 색이나 입힐 수 있다 -
/// 프로젝트의 기존 테두리 아트(`UI/Black_ui03`)는 거의 검정이라 색을 곱하면 죽어버려서
/// 등급색·강조색 용도로는 못 쓴다(ItemCellUI 주석의 2026-08-13 함정과 같은 이유).
/// </summary>
public static class UiIconLibrary
{
    private const int IconSize = 64;

    private static Sprite lock_icon;
    private static Sprite stamp_frame;

    /// <summary>잠금 버튼에 쓸 자물쇠 아이콘.</summary>
    public static Sprite Lock()
    {
        if (lock_icon != null) return lock_icon;

        lock_icon = Resources.Load<Sprite>("UI/Lock_icon");
        if (lock_icon == null) lock_icon = BuildLock();
        return lock_icon;
    }

    /// <summary>
    /// 속이 빈 사각 테두리. "구매 완료" 스탬프 테두리와 잠긴 카드 강조 테두리에 함께 쓴다.
    /// 9-slice 경계를 넣어 두었으므로 <c>Image.type = Sliced</c>로 늘려도 선 두께가 유지된다.
    /// </summary>
    public static Sprite Frame()
    {
        if (stamp_frame != null) return stamp_frame;

        stamp_frame = Resources.Load<Sprite>("UI/Stamp_frame");
        if (stamp_frame == null) stamp_frame = BuildFrame();
        return stamp_frame;
    }

    /// <summary>에셋을 새로 넣은 뒤 캐시를 비우고 싶을 때 사용.</summary>
    public static void ClearCache()
    {
        lock_icon = null;
        stamp_frame = null;
    }

    // ------------------------------------------------------------------
    // 임시 도형 생성
    // ------------------------------------------------------------------

    // 자물쇠 = 몸통(사각) + 고리(반원 링) + 열쇠구멍(몸통을 뚫는다)
    private static Sprite BuildLock()
    {
        Color32[] p = NewCanvas("UiIcon_Lock", out Texture2D tex);

        UpperHalfRing(p, 32, 38, 15, 6); // 고리
        Rect(p, 11, 6, 42, 32);          // 몸통
        ClearDisc(p, 32, 24, 5);         // 열쇠구멍(원)
        ClearRect(p, 30, 13, 5, 11);     // 열쇠구멍(아래로 뻗은 홈)

        return Finish(tex, p, Vector4.zero);
    }

    // 속이 빈 사각 테두리(선 두께 6px). border를 선 두께보다 살짝 크게 잡아야
    // 9-slice로 늘렸을 때 모서리가 뭉개지지 않는다.
    private static Sprite BuildFrame()
    {
        const int thickness = 6;
        Color32[] p = NewCanvas("UiIcon_Frame", out Texture2D tex);

        Rect(p, 0, 0, IconSize, thickness);                          // 아래
        Rect(p, 0, IconSize - thickness, IconSize, thickness);       // 위
        Rect(p, 0, 0, thickness, IconSize);                          // 왼쪽
        Rect(p, IconSize - thickness, 0, thickness, IconSize);       // 오른쪽

        const float border = thickness + 2;
        return Finish(tex, p, new Vector4(border, border, border, border));
    }

    // ------------------------------------------------------------------
    // 픽셀 그리기 도구 (PartIconLibrary와 같은 방식)
    // ------------------------------------------------------------------

    private static Color32[] NewCanvas(string name, out Texture2D tex)
    {
        tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = name
        };

        var pixels = new Color32[IconSize * IconSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);
        return pixels;
    }

    private static Sprite Finish(Texture2D tex, Color32[] pixels, Vector4 border)
    {
        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        return Sprite.Create(tex, new UnityEngine.Rect(0, 0, IconSize, IconSize),
                             new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static void Set(Color32[] p, int x, int y, byte alpha)
    {
        if (x < 0 || y < 0 || x >= IconSize || y >= IconSize) return;
        p[y * IconSize + x] = new Color32(255, 255, 255, alpha);
    }

    private static void Rect(Color32[] p, int x, int y, int w, int h)
    {
        for (int iy = y; iy < y + h; iy++)
            for (int ix = x; ix < x + w; ix++)
                Set(p, ix, iy, 255);
    }

    private static void ClearRect(Color32[] p, int x, int y, int w, int h)
    {
        for (int iy = y; iy < y + h; iy++)
            for (int ix = x; ix < x + w; ix++)
                Set(p, ix, iy, 0);
    }

    private static void ClearDisc(Color32[] p, int cx, int cy, int r)
    {
        int rr = r * r;
        for (int iy = cy - r; iy <= cy + r; iy++)
            for (int ix = cx - r; ix <= cx + r; ix++)
            {
                int dx = ix - cx, dy = iy - cy;
                if (dx * dx + dy * dy <= rr) Set(p, ix, iy, 0);
            }
    }

    // 링의 위쪽 절반만 그린다(자물쇠 고리).
    private static void UpperHalfRing(Color32[] p, int cx, int cy, int rOuter, int thickness)
    {
        int rInner = Mathf.Max(0, rOuter - thickness);
        int ro = rOuter * rOuter, ri = rInner * rInner;

        for (int iy = cy; iy <= cy + rOuter; iy++)
            for (int ix = cx - rOuter; ix <= cx + rOuter; ix++)
            {
                int dx = ix - cx, dy = iy - cy;
                int d = dx * dx + dy * dy;
                if (d <= ro && d >= ri) Set(p, ix, iy, 255);
            }
    }
}
