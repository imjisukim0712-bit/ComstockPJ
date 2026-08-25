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
    private static Sprite settings_icon;
    private static Sprite unlock_icon;

    /// <summary>잠금 버튼에 쓸 자물쇠 아이콘.</summary>
    public static Sprite Lock()
    {
        if (lock_icon != null) return lock_icon;

        lock_icon = Resources.Load<Sprite>("UI/Lock_icon");
        if (lock_icon == null) lock_icon = BuildLock();
        return lock_icon;
    }

    /// <summary>
    /// 잠금 <b>해제</b> 상태(열린 자물쇠) 아이콘. 2026-08-25 사용자가 아트를 올려주면서 신설했다 -
    /// 예전에는 자물쇠 하나를 노란색으로 물들여 잠김/해제를 구분했는데, 이제 그림 자체가 다르다.
    /// </summary>
    public static Sprite Unlock()
    {
        if (unlock_icon != null) return unlock_icon;

        unlock_icon = Resources.Load<Sprite>("UI/Unlock_icon");
        if (unlock_icon == null) unlock_icon = Lock(); // 아트가 없으면 잠금 아이콘으로 대체
        return unlock_icon;
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

    /// <summary>
    /// 우상단 설정 버튼에 쓸 톱니바퀴 아이콘(2026-08-19 신설).
    /// <c>Assets/Resources/UI/Settings_icon.png</c>를 넣으면 코드 수정 없이 자동 교체된다.
    /// </summary>
    public static Sprite Settings()
    {
        if (settings_icon != null) return settings_icon;

        settings_icon = Resources.Load<Sprite>("UI/Settings_icon");
        if (settings_icon == null) settings_icon = BuildSettings();
        return settings_icon;
    }

    /// <summary>에셋을 새로 넣은 뒤 캐시를 비우고 싶을 때 사용.</summary>
    public static void ClearCache()
    {
        lock_icon = null;
        unlock_icon = null;
        stamp_frame = null;
        settings_icon = null;
        edgeRingCache.Clear();
    }

    private static readonly System.Collections.Generic.Dictionary<Sprite, Sprite> edgeRingCache = new();

    /// <summary>
    /// 기본 프레임 스프라이트(<paramref name="baseSprite"/>)의 <b>실제 9-slice border 두께와
    /// 알파 실루엣</b>을 그대로 따르는 색칠 가능한 테두리 링을 만든다.
    ///
    /// <see cref="Frame()"/>(고정 6px 사각 링, 64px 캔버스 기준)은 실제 프레임 아트와 무관하게
    /// 임의로 그린 도형이라, 실제 아트(예: `UI/Black_ui03`, border=30/133 ≈ 22%, 모서리가 둥글다)
    /// 보다 훨씬 얇고 모서리도 각져서 등급색 테두리가 실제 베젤과 안 맞았다(2026-08-25 사용자 지적:
    /// "이미지를 무시하고 전체 리소스 사이즈를 기준으로 테두리를 만들어버렸음. 이미지의 두꺼운
    /// 부분을 테두리라 치고 그 색을 바꿨어야 맞는데").
    ///
    /// 원본과 <b>완전히 같은 픽셀 크기·border 메타데이터</b>로 결과 스프라이트를 만들기 때문에
    /// 9-slice 코너/엣지 영역이 원본과 1:1로 겹친다 - 그래서 "border 두께 안쪽" 픽셀만 남기면
    /// 둥근 모서리를 포함한 실제 베젤 모양을 그대로 따라간다.
    /// </summary>
    public static Sprite DeriveEdgeRing(Sprite baseSprite)
    {
        if (baseSprite == null) return Frame();
        if (edgeRingCache.TryGetValue(baseSprite, out Sprite cached) && cached != null) return cached;

        Texture2D readable = MakeReadableCopy(baseSprite.texture);
        Rect rect = baseSprite.textureRect;
        int rx = Mathf.RoundToInt(rect.x), ry = Mathf.RoundToInt(rect.y);
        int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
        Vector4 border = baseSprite.border; // (left, bottom, right, top)

        Color32[] src = readable.GetPixels32();
        int texWidth = readable.width;
        var outPixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte a = src[(ry + y) * texWidth + (rx + x)].a;
                bool nearEdge = x < border.x || (w - 1 - x) < border.z || y < border.y || (h - 1 - y) < border.w;
                outPixels[y * w + x] = new Color32(255, 255, 255, nearEdge ? a : (byte)0);
            }
        }
        Object.DestroyImmediate(readable);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = baseSprite.name + "_EdgeRing"
        };
        tex.SetPixels32(outPixels);
        tex.Apply(false, false);

        Vector2 normalizedPivot = new Vector2(baseSprite.pivot.x / w, baseSprite.pivot.y / h);
        Sprite result = Sprite.Create(tex, new Rect(0, 0, w, h), normalizedPivot,
                                       baseSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        edgeRingCache[baseSprite] = result;
        return result;
    }

    /// <summary>Read/Write가 꺼진 텍스처(대부분의 UI 스프라이트)도 픽셀을 읽을 수 있도록 GPU
    /// 경유(Blit → RenderTexture → ReadPixels)로 읽기 가능한 사본을 만든다.</summary>
    private static Texture2D MakeReadableCopy(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
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

    // 톱니바퀴 = 바깥 톱니 8개 + 원판 + 중앙 구멍.
    // 톱니를 먼저 찍고 원판을 덮어 이음매가 보이지 않게 한 뒤, 마지막에 가운데를 뚫는다.
    private static Sprite BuildSettings()
    {
        const int cx = IconSize / 2, cy = IconSize / 2;
        const int bodyRadius = 19;   // 원판
        const int toothRing = 24;    // 톱니 중심이 놓이는 반지름
        const int toothHalf = 5;     // 톱니 한 변의 절반
        const int holeRadius = 7;    // 중앙 구멍

        Color32[] p = NewCanvas("UiIcon_Settings", out Texture2D tex);

        // 45도 간격 8개. 사각 톱니라 대각선 톱니도 축 정렬 사각형으로 근사하는데,
        // 64x64에서는 육안으로 충분히 톱니바퀴로 읽힌다.
        for (int i = 0; i < 8; i++)
        {
            float rad = i * 45f * Mathf.Deg2Rad;
            int tx = cx + Mathf.RoundToInt(Mathf.Cos(rad) * toothRing);
            int ty = cy + Mathf.RoundToInt(Mathf.Sin(rad) * toothRing);
            Rect(p, tx - toothHalf, ty - toothHalf, toothHalf * 2, toothHalf * 2);
        }

        Disc(p, cx, cy, bodyRadius);
        ClearDisc(p, cx, cy, holeRadius);

        return Finish(tex, p, Vector4.zero);
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

    // 채워진 원(ClearDisc의 반대). 톱니바퀴 원판에 쓴다.
    private static void Disc(Color32[] p, int cx, int cy, int r)
    {
        int rr = r * r;
        for (int iy = cy - r; iy <= cy + r; iy++)
            for (int ix = cx - r; ix <= cx + r; ix++)
            {
                int dx = ix - cx, dy = iy - cy;
                if (dx * dx + dy * dy <= rr) Set(p, ix, iy, 255);
            }
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
