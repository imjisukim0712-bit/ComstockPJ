using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 로봇 파츠 슬롯별 아이콘을 돌려준다.
///
/// <b>진짜 아이콘으로 교체하는 법</b>: `Assets/Resources/PartIcons/` 폴더를 만들고 슬롯 이름
/// 그대로 PNG를 넣으면 된다 — 코드는 건드릴 필요가 없다.
///   ArmWeaponSocket / ArmArmor / MagneticCore / Leg / LegArmor / Foot / Helmet / DiscSlot
/// (텍스처 임포트 설정은 Sprite(2D and UI)여야 한다.)
///
/// 파일이 없으면 아래에서 <b>코드로 그린 임시 아이콘</b>을 대신 쓴다(2026-08-18, 사용자가
/// "파츠는 임시 아이콘으로 대체, 나중에 올릴거임"으로 확정). 임시 아이콘은 흰색 단색 실루엣이라
/// 칸 배경에 칠하는 등급색 위에서 잘 보인다.
/// </summary>
public static class PartIconLibrary
{
    private const string ResourceFolder = "PartIcons/";
    private const int IconSize = 64;

    private static readonly Dictionary<PartSlot, Sprite> cache = new Dictionary<PartSlot, Sprite>();

    /// <summary>슬롯에 해당하는 아이콘. 실제 에셋이 있으면 그것을, 없으면 임시 아이콘을 돌려준다.</summary>
    public static Sprite Get(PartSlot slot)
    {
        if (cache.TryGetValue(slot, out Sprite cached) && cached != null) return cached;

        Sprite sprite = Resources.Load<Sprite>(ResourceFolder + slot);
        if (sprite == null) sprite = BuildPlaceholder(slot);

        cache[slot] = sprite;
        return sprite;
    }

    /// <summary>에셋을 새로 넣은 뒤 캐시를 비우고 싶을 때 사용.</summary>
    public static void ClearCache() => cache.Clear();

    // ------------------------------------------------------------------
    // 임시 아이콘 생성 (슬롯마다 구분되는 단순 도형)
    // ------------------------------------------------------------------

    private static Sprite BuildPlaceholder(PartSlot slot)
    {
        var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "PartIcon_" + slot
        };

        var pixels = new Color32[IconSize * IconSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);

        switch (slot)
        {
            case PartSlot.Helmet: // 돔 + 챙
                Disc(pixels, 32, 26, 20);
                Rect(pixels, 10, 16, 44, 8);
                break;

            case PartSlot.ArmWeaponSocket: // 총열 + 손잡이
                Rect(pixels, 8, 30, 48, 11);
                Rect(pixels, 18, 18, 11, 13);
                break;

            case PartSlot.ArmArmor: // 방패
                Shield(pixels);
                break;

            case PartSlot.MagneticCore: // 두꺼운 고리
                Ring(pixels, 32, 32, 23, 11);
                break;

            case PartSlot.Leg: // 세로 막대 두 개
                Rect(pixels, 18, 8, 10, 48);
                Rect(pixels, 36, 8, 10, 48);
                break;

            case PartSlot.LegArmor: // 가로 띠 세 개
                Rect(pixels, 16, 12, 32, 9);
                Rect(pixels, 16, 28, 32, 9);
                Rect(pixels, 16, 44, 32, 9);
                break;

            case PartSlot.Foot: // L자 부츠
                Rect(pixels, 18, 20, 11, 34);
                Rect(pixels, 18, 12, 30, 10);
                break;

            case PartSlot.DiscSlot: // 얇은 고리(디스크)
                Ring(pixels, 32, 32, 23, 6);
                break;

            case PartSlot.Memory: // 메모리 칩 - 몸통 + 상하좌우로 튀어나온 핀
                Rect(pixels, 16, 16, 32, 32);
                Rect(pixels, 22, 8, 6, 8);
                Rect(pixels, 36, 8, 6, 8);
                Rect(pixels, 22, 48, 6, 8);
                Rect(pixels, 36, 48, 6, 8);
                Rect(pixels, 8, 22, 8, 6);
                Rect(pixels, 8, 36, 8, 6);
                Rect(pixels, 48, 22, 8, 6);
                Rect(pixels, 48, 36, 8, 6);
                break;

            default:
                Rect(pixels, 16, 16, 32, 32);
                break;
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        return Sprite.Create(tex, new UnityEngine.Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
    }

    private static void Set(Color32[] p, int x, int y)
    {
        if (x < 0 || y < 0 || x >= IconSize || y >= IconSize) return;
        p[y * IconSize + x] = new Color32(255, 255, 255, 255);
    }

    private static void Rect(Color32[] p, int x, int y, int w, int h)
    {
        for (int iy = y; iy < y + h; iy++)
            for (int ix = x; ix < x + w; ix++)
                Set(p, ix, iy);
    }

    private static void Disc(Color32[] p, int cx, int cy, int r)
    {
        int rr = r * r;
        for (int iy = cy - r; iy <= cy + r; iy++)
            for (int ix = cx - r; ix <= cx + r; ix++)
            {
                int dx = ix - cx, dy = iy - cy;
                if (dx * dx + dy * dy <= rr) Set(p, ix, iy);
            }
    }

    private static void Ring(Color32[] p, int cx, int cy, int rOuter, int thickness)
    {
        int rInner = Mathf.Max(0, rOuter - thickness);
        int ro = rOuter * rOuter, ri = rInner * rInner;
        for (int iy = cy - rOuter; iy <= cy + rOuter; iy++)
            for (int ix = cx - rOuter; ix <= cx + rOuter; ix++)
            {
                int dx = ix - cx, dy = iy - cy;
                int d = dx * dx + dy * dy;
                if (d <= ro && d >= ri) Set(p, ix, iy);
            }
    }

    // 위는 넓고 아래로 갈수록 좁아지는 방패꼴
    private static void Shield(Color32[] p)
    {
        const int top = 54, bottom = 10;
        for (int y = bottom; y <= top; y++)
        {
            float t = (top - y) / (float)(top - bottom); // 0(위) ~ 1(아래)
            int halfWidth = Mathf.RoundToInt(Mathf.Lerp(17f, 3f, t * t));
            for (int x = 32 - halfWidth; x <= 32 + halfWidth; x++) Set(p, x, y);
        }
    }
}
