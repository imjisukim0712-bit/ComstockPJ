from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
SOURCE = OUT / "source"

CANVAS = (2560, 1440)
# YouTube's 1235x338 minimum-size safe area scaled to the 2560x1440 export.
SAFE = (508, 509, 2052, 932)

GOLD = (243, 174, 32, 255)
IVORY = (246, 239, 216, 255)
CHARCOAL = (19, 18, 17, 255)


def load_rgba(path: Path) -> Image.Image:
    return Image.open(path).convert("RGBA")


def fit_cover(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    return ImageOps.fit(image.convert("RGB"), size, Image.Resampling.LANCZOS).convert("RGBA")


def contain(image: Image.Image, size: tuple[int, int], resample=Image.Resampling.LANCZOS) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, resample)
    return copy


def recolor_alpha(image: Image.Image, color: tuple[int, int, int, int]) -> Image.Image:
    result = Image.new("RGBA", image.size, color)
    result.putalpha(image.getchannel("A"))
    return result


def paste_center(base: Image.Image, image: Image.Image, center: tuple[int, int]) -> tuple[int, int, int, int]:
    x = int(center[0] - image.width / 2)
    y = int(center[1] - image.height / 2)
    base.alpha_composite(image, (x, y))
    return (x, y, x + image.width, y + image.height)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    background = fit_cover(load_rgba(SOURCE / "generated-retro-city.png"), CANVAS)
    draw = ImageDraw.Draw(background, "RGBA")

    # A calm central broadcast band keeps the mobile crop readable while the
    # generated ruined-city detail remains visible on TV and desktop crops.
    draw.rectangle((0, 500, CANVAS[0], 941), fill=(12, 12, 12, 188))
    draw.rectangle((0, 500, CANVAS[0], 506), fill=GOLD)
    draw.rectangle((0, 935, CANVAS[0], 941), fill=GOLD)

    # Edge-only enemy silhouettes: outside the mobile safe area, decorative on
    # desktop/TV, and intentionally dispensable when YouTube crops the banner.
    zombie_paths = [
        ROOT / "ItchIO/images/50_zombie.png",
        ROOT / "ItchIO/images/51_charger.png",
        ROOT / "ItchIO/images/52_sprinter.png",
        ROOT / "ItchIO/images/53_spitter.png",
        ROOT / "ItchIO/images/54_disruptor.png",
    ]
    edge_centers = [(115, 805), (305, 782), (455, 818), (2110, 815), (2290, 780), (2450, 812)]
    for index, center in enumerate(edge_centers):
        z = load_rgba(zombie_paths[index % len(zombie_paths)])
        z = contain(z, (245, 245), Image.Resampling.NEAREST)
        z = recolor_alpha(z, (238, 173, 37, 215))
        paste_center(background, z, center)

    # Exact Pyramid eye source, recolored deterministically to match the public
    # channel avatar instead of asking generation to redraw the mark.
    logo = contain(load_rgba(ROOT / "dev/pv/assets/pyramid_logo.png"), (250, 190))
    logo = recolor_alpha(logo, CHARCOAL)
    draw.ellipse((543, 592, 803, 852), fill=GOLD)
    logo_box = paste_center(background, logo, (673, 722))

    # Exact channel name. The type and tracking are deterministic.
    bold = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 112)
    regular = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 38)
    draw.text((842, 603), "PYRAMID", font=bold, fill=IVORY, stroke_width=0)
    draw.text((842, 718), "ORIGIN", font=bold, fill=GOLD, stroke_width=0)
    draw.text((848, 838), "COMSTOCK", font=regular, fill=(223, 217, 198, 255), stroke_width=0)

    # Exact game character source, placed wholly within the mobile safe area.
    robot = contain(load_rgba(ROOT / "Assets/Resources/Comstock.png"), (560, 360))
    robot_box = paste_center(background, robot, (1752, 724))

    final_path = OUT / "pyramid-origin-youtube-banner.png"
    background.convert("RGB").save(final_path, "PNG", optimize=True)

    preview = background.copy()
    pdraw = ImageDraw.Draw(preview, "RGBA")
    pdraw.rectangle(SAFE, outline=(255, 50, 50, 255), width=8)
    pdraw.rectangle((SAFE[0], SAFE[1] - 44, SAFE[0] + 310, SAFE[1]), fill=(255, 50, 50, 230))
    pfont = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 28)
    pdraw.text((SAFE[0] + 12, SAFE[1] - 39), "MOBILE SAFE AREA", font=pfont, fill=(255, 255, 255, 255))
    preview.convert("RGB").save(OUT / "pyramid-origin-youtube-banner-safe-preview.jpg", "JPEG", quality=88)

    critical_boxes = {
        "logo": logo_box,
        "channel_name": (842, 603, 1380, 829),
        "game_title": (848, 838, 1082, 883),
        "robot": robot_box,
    }
    for name, box in critical_boxes.items():
        if not (box[0] >= SAFE[0] and box[1] >= SAFE[1] and box[2] <= SAFE[2] and box[3] <= SAFE[3]):
            raise RuntimeError(f"{name} is outside the mobile safe area: {box}")

    size_mb = final_path.stat().st_size / (1024 * 1024)
    if size_mb > 6:
        raise RuntimeError(f"Banner exceeds YouTube's 6 MB limit: {size_mb:.2f} MB")
    print(f"created={final_path}")
    print(f"dimensions={CANVAS[0]}x{CANVAS[1]}")
    print(f"safe_area={SAFE}")
    print(f"file_size_mb={size_mb:.2f}")


if __name__ == "__main__":
    main()
