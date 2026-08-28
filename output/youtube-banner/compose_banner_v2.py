from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
W, H = 2560, 1440
SAFE = (508, 509, 2052, 932)

IVORY = (247, 241, 224, 255)
ORANGE = (255, 113, 0, 255)


def rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def contain(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, Image.Resampling.LANCZOS)
    return copy


def recolor(image: Image.Image, color: tuple[int, int, int, int]) -> Image.Image:
    out = Image.new("RGBA", image.size, color)
    out.putalpha(image.getchannel("A"))
    return out


def main() -> None:
    # Use the game's real title illustration as the entire visual world. Zooming
    # and cropping moves the robot lineup into YouTube's shallow mobile strip.
    src = rgba(ROOT / "Assets/Resources/UI/titleimage.png")
    scaled_h = 2000
    scaled_w = round(src.width * scaled_h / src.height)
    src = src.resize((scaled_w, scaled_h), Image.Resampling.LANCZOS)
    left = (scaled_w - W) // 2
    canvas = src.crop((left, 560, left + W, 560 + H))

    # Local contrast only behind the wordmark; there is no full-width panel.
    shade = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    for x in range(0, 1450, 4):
        p = x / 1450
        alpha = round(205 * (1 - p) ** 1.8)
        sd.rectangle((x, 0, x + 4, H), fill=(7, 8, 10, alpha))
    canvas.alpha_composite(shade)

    # Exact Pyramid eye source; no generated substitute and no repeated avatar
    # circle. It works as a compact signature beside the channel name.
    eye = contain(rgba(ROOT / "dev/pv/assets/pyramid_logo.png"), (160, 118))
    eye = recolor(eye, ORANGE)
    canvas.alpha_composite(eye, (556, 568))

    draw = ImageDraw.Draw(canvas)
    bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 88)
    draw.text((744, 554), "PYRAMID", font=bold, fill=IVORY)
    draw.text((744, 646), "ORIGIN", font=bold, fill=ORANGE)

    # The game's real logo is secondary to the channel name and sits entirely
    # inside the cross-device safe area.
    game_logo = contain(rgba(ROOT / "Assets/Resources/UI/title_logo.png"), (560, 170))
    canvas.alpha_composite(game_logo, (556, 744))

    final = OUT / "pyramid-origin-youtube-banner-v2.jpg"
    canvas.convert("RGB").save(final, "JPEG", quality=94, subsampling=0, optimize=True)

    preview = canvas.copy()
    pd = ImageDraw.Draw(preview, "RGBA")
    pd.rectangle(SAFE, outline=(255, 60, 60, 255), width=8)
    pd.rectangle((SAFE[0], SAFE[1] - 42, SAFE[0] + 300, SAFE[1]), fill=(255, 60, 60, 230))
    label = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 26)
    pd.text((SAFE[0] + 12, SAFE[1] - 36), "MOBILE SAFE AREA", font=label, fill=(255, 255, 255, 255))
    preview.convert("RGB").save(
        OUT / "pyramid-origin-youtube-banner-v2-safe-preview.jpg",
        "JPEG",
        quality=88,
        optimize=True,
    )

    critical = {
        "eye": (556, 568, 716, 686),
        "channel_name": (744, 554, 1136, 738),
        "game_logo": (556, 744, 1116, 914),
    }
    for name, box in critical.items():
        if not (box[0] >= SAFE[0] and box[1] >= SAFE[1] and box[2] <= SAFE[2] and box[3] <= SAFE[3]):
            raise RuntimeError(f"{name} outside safe area: {box}")

    size_mb = final.stat().st_size / 1024 / 1024
    if size_mb > 6:
        raise RuntimeError(f"file too large: {size_mb:.2f} MB")
    print(f"created={final}")
    print(f"size={W}x{H}")
    print(f"file_size_mb={size_mb:.2f}")


if __name__ == "__main__":
    main()
