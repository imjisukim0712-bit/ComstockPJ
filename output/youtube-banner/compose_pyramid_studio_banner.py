from pathlib import Path
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
W, H = 2560, 1440
SAFE = (508, 509, 2052, 932)


def brick_wall() -> Image.Image:
    rng = random.Random(240828)
    wall = Image.new("RGBA", (W, H), (90, 28, 5, 255))
    draw = ImageDraw.Draw(wall, "RGBA")

    brick_h = 154
    brick_w = 390
    mortar = 11
    palette = [
        (164, 66, 7, 255),
        (185, 77, 7, 255),
        (204, 91, 8, 255),
        (218, 104, 10, 255),
        (147, 53, 6, 255),
    ]
    for row, y in enumerate(range(-brick_h, H + brick_h, brick_h)):
        offset = -brick_w // 2 if row % 2 else 0
        for x in range(offset - brick_w, W + brick_w, brick_w):
            jitter = rng.randint(-18, 18)
            box = (x + mortar, y + mortar, x + brick_w - mortar + jitter, y + brick_h - mortar)
            fill = palette[rng.randrange(len(palette))]
            draw.rounded_rectangle(box, radius=20, fill=fill, outline=(68, 20, 5, 255), width=8)
            # Uneven warm face and chipped stone marks keep the wall from
            # looking like a flat vector pattern.
            draw.line((box[0] + 20, box[1] + 18, box[2] - 18, box[1] + 18), fill=(248, 151, 31, 48), width=5)
            x_lo, x_hi = max(box[0] + 22, 0), min(box[2] - 22, W - 1)
            y_lo, y_hi = max(box[1] + 22, 0), min(box[3] - 22, H - 1)
            if x_lo <= x_hi and y_lo <= y_hi:
                for _ in range(4):
                    px = rng.randint(x_lo, x_hi)
                    py = rng.randint(y_lo, y_hi)
                    length = rng.randint(14, 48)
                    draw.line((px, py, px + length, py + rng.randint(-8, 8)), fill=(55, 18, 5, 58), width=3)

    # Amber central spotlight, matching the studio ident without copying a
    # video frame or introducing unrelated game imagery.
    glow_mask = Image.new("L", (W, H), 0)
    gm = ImageDraw.Draw(glow_mask)
    gm.ellipse((280, 105, W - 280, H + 420), fill=220)
    glow_mask = glow_mask.filter(ImageFilter.GaussianBlur(230))
    wall = Image.composite(Image.new("RGBA", (W, H), (245, 151, 19, 255)), wall, glow_mask)

    # Subtle stone grain and dark theatrical vignette.
    noise = Image.effect_noise((W, H), 24).convert("L")
    noise = ImageOps.colorize(noise, black=(35, 8, 0), white=(255, 187, 67)).convert("RGBA")
    noise.putalpha(34)
    wall.alpha_composite(noise)

    vignette = Image.radial_gradient("L").resize((W, H), Image.Resampling.BILINEAR)
    vignette = vignette.point(lambda p: max(0, min(205, int((p / 255) ** 1.6 * 215))))
    wall = Image.composite(Image.new("RGBA", (W, H), (8, 3, 2, 255)), wall, vignette)

    # Very light film scan texture from the channel ident.
    scan = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(scan)
    for y in range(0, H, 6):
        sd.line((0, y, W, y), fill=(0, 0, 0, 16), width=2)
    wall.alpha_composite(scan)
    return wall


def main() -> None:
    canvas = brick_wall()

    # Background only: no logo, studio name, wordmark, symbol, character, or
    # decorative emblem. The avatar and channel UI carry the identity.
    final = OUT / "pyramid-studio-youtube-banner-no-logo.jpg"
    canvas.convert("RGB").save(final, "JPEG", quality=94, subsampling=0, optimize=True)

    preview = canvas.copy()
    pd = ImageDraw.Draw(preview, "RGBA")
    pd.rectangle(SAFE, outline=(255, 60, 60, 255), width=8)
    pd.rectangle((SAFE[0], SAFE[1] - 42, SAFE[0] + 300, SAFE[1]), fill=(255, 60, 60, 230))
    label = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 26)
    pd.text((SAFE[0] + 12, SAFE[1] - 36), "MOBILE SAFE AREA", font=label, fill=(255, 255, 255, 255))
    preview.convert("RGB").save(
        OUT / "pyramid-studio-youtube-banner-no-logo-safe-preview.jpg",
        "JPEG",
        quality=88,
        optimize=True,
    )

    size_mb = final.stat().st_size / 1024 / 1024
    if size_mb > 6:
        raise RuntimeError(f"file too large: {size_mb:.2f} MB")
    print(f"created={final}")
    print(f"size={W}x{H}")
    print(f"file_size_mb={size_mb:.2f}")


if __name__ == "__main__":
    main()
