from __future__ import annotations

import argparse
import math
import os
import subprocess
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "dev" / "shorts" / "source" / "ankara_messi.mp4"
HEAD_PATH = ROOT / "Assets" / "Resources" / "Heads" / "ComstockMk01.png"
LOGO_PATH = ROOT / "Assets" / "Resources" / "UI" / "title_logo.png"
FFMPEG = ROOT / "dev" / "pv" / "_vendor" / "imageio_ffmpeg" / "binaries" / "ffmpeg-win-x86_64-v7.1.exe"

OUT_W, OUT_H = 1080, 1920
SRC_W, SRC_H = 1280, 720
STRIP_W, STRIP_H = 1080, 608
STRIP_Y = 592
STRIP_SCALE = STRIP_W / SRC_W
ORANGE = (255, 111, 0)
CREAM = (242, 238, 231)
DARK = (17, 19, 22)


# (time, head center x, head center y, nominal overlay width in source pixels).
# Hard cuts get duplicate timestamps so interpolation never crosses two camera angles.
TRACK = [
    (0.00, 770, 245, 92), (0.50, 720, 250, 92), (1.00, 735, 250, 92),
    (1.50, 760, 260, 94), (2.00, 750, 255, 94), (2.50, 820, 295, 96),
    (3.00, 725, 305, 98), (3.50, 765, 290, 100), (4.10, 680, 280, 104),
    (4.11, 650, 200, 180), (4.50, 695, 190, 184), (5.00, 710, 155, 188),
    (5.50, 810, 130, 188), (6.00, 870, 165, 190), (6.50, 860, 130, 188),
    (7.15, 945, 100, 184), (7.25, 1000, 105, 180),
    (7.26, 990, 150, 92), (7.50, 980, 150, 92), (8.00, 830, 200, 92),
    (8.50, 845, 220, 94), (9.00, 810, 240, 94), (9.50, 825, 230, 96),
    (10.00, 800, 220, 98), (10.50, 775, 215, 100), (11.00, 730, 215, 102),
    (11.50, 810, 245, 104), (12.00, 850, 235, 106), (12.50, 925, 180, 108),
    (13.00, 1000, 240, 112), (13.50, 1095, 310, 116), (14.00, 990, 350, 120),
    (14.50, 945, 340, 122), (15.00, 880, 310, 126), (15.50, 880, 270, 132),
    (16.20, 845, 340, 138),
    (16.21, 330, 190, 290), (16.70, 405, 210, 310), (17.20, 540, 230, 330),
    (17.80, 650, 260, 350), (18.15, 700, 275, 360),
    (22.00, 780, 285, 150), (22.50, 740, 290, 154), (23.00, 670, 315, 158),
    (23.50, 635, 315, 164), (24.00, 650, 300, 170), (24.55, 720, 275, 176),
]

VISIBLE_RANGES = ((0.0, 18.2), (22.0, 24.6))


def ease_out_back(x: float) -> float:
    x = max(0.0, min(1.0, x))
    c1 = 1.70158
    c3 = c1 + 1
    return 1 + c3 * (x - 1) ** 3 + c1 * (x - 1) ** 2


def lerp_track(t: float) -> tuple[float, float, float] | None:
    if not any(a <= t <= b for a, b in VISIBLE_RANGES):
        return None
    candidates = [p for p in TRACK if (p[0] <= 18.2) == (t <= 18.2)]
    if not candidates:
        return None
    if t <= candidates[0][0]:
        return candidates[0][1:]
    if t >= candidates[-1][0]:
        return candidates[-1][1:]
    for a, b in zip(candidates, candidates[1:]):
        if a[0] <= t <= b[0]:
            q = 0.0 if b[0] == a[0] else (t - a[0]) / (b[0] - a[0])
            q = q * q * (3 - 2 * q)
            return tuple(a[i] + (b[i] - a[i]) * q for i in (1, 2, 3))
    return None


def alpha_crop(im: Image.Image) -> Image.Image:
    alpha = im.getchannel("A")
    bbox = alpha.getbbox()
    return im.crop(bbox) if bbox else im


def fit_rgba(im: Image.Image, width: int) -> Image.Image:
    width = max(1, int(width))
    height = max(1, round(im.height * width / im.width))
    return im.resize((width, height), Image.Resampling.LANCZOS)


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / name
    return ImageFont.truetype(str(path), size)


def text_center(draw: ImageDraw.ImageDraw, xy: tuple[int, int], value: str,
                fnt: ImageFont.FreeTypeFont, fill, stroke: int = 0,
                stroke_fill=(0, 0, 0, 255)) -> None:
    draw.text(xy, value, font=fnt, fill=fill, anchor="mm",
              stroke_width=stroke, stroke_fill=stroke_fill)


def paste_center(base: Image.Image, layer: Image.Image, x: float, y: float) -> None:
    base.alpha_composite(layer, (round(x - layer.width / 2), round(y - layer.height / 2)))


def render_frame(frame_bgr: np.ndarray, t: float, head: Image.Image,
                 logo: Image.Image) -> Image.Image:
    frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

    # Full-height blurred motion backdrop. It keeps the original broadcast context
    # visible while the sharp 16:9 strip stays intact for the football action.
    bg_w = round(OUT_H * SRC_W / SRC_H)
    bg = cv2.resize(frame_rgb, (bg_w, OUT_H), interpolation=cv2.INTER_LINEAR)
    track = lerp_track(t)
    focus_x = track[0] if track else SRC_W / 2
    crop_x = int(max(0, min(bg_w - OUT_W, focus_x / SRC_W * bg_w - OUT_W / 2)))
    bg = bg[:, crop_x:crop_x + OUT_W]
    bg = cv2.GaussianBlur(bg, (0, 0), 24)
    bg = np.clip(bg.astype(np.float32) * 0.42, 0, 255).astype(np.uint8)
    canvas = Image.fromarray(bg, "RGB").convert("RGBA")

    # Dark readable zones, orange broadcast frame, and scanlines.
    draw = ImageDraw.Draw(canvas, "RGBA")
    draw.rectangle((0, 0, OUT_W, 520), fill=(9, 11, 14, 205))
    draw.rectangle((0, 1270, OUT_W, OUT_H), fill=(9, 11, 14, 210))
    draw.rectangle((0, STRIP_Y - 10, OUT_W, STRIP_Y + STRIP_H + 10), fill=ORANGE + (255,))
    for y in range(0, OUT_H, 6):
        draw.line((0, y, OUT_W, y), fill=(0, 0, 0, 22), width=1)

    sharp = cv2.resize(frame_rgb, (STRIP_W, STRIP_H), interpolation=cv2.INTER_LANCZOS4)
    strip = Image.fromarray(sharp, "RGB").convert("RGBA")

    # The deliberately over-large bobble-head is the primary joke.
    if track:
        x, y, nominal = track
        intro = ease_out_back(t / 0.42) if t < 0.42 else 1.0
        pulse = 1.0
        if 13.2 <= t <= 16.35:
            pulse += 0.14 * max(0.0, math.sin((t - 13.2) * 9.0))
        if 16.2 < t < 18.2:
            pulse += 0.16 * math.sin((t - 16.2) * 10.0) ** 2
        width = nominal * STRIP_SCALE * intro * pulse
        angle = 9.5 * math.sin(t * 10.5) + 3.2 * math.sin(t * 23.0)
        if 16.2 < t < 18.2:
            angle += 18 * math.sin((t - 16.2) * 7)
        head_layer = fit_rgba(head, round(width)).rotate(angle, Image.Resampling.BICUBIC, expand=True)
        hx = x * STRIP_SCALE
        hy = y * STRIP_SCALE
        # Tiny shadow makes the clean game art look even more absurd against old TV footage.
        shadow = Image.new("RGBA", head_layer.size, (0, 0, 0, 0))
        shadow.putalpha(head_layer.getchannel("A").point(lambda a: a * 110 // 255))
        paste_center(strip, shadow, hx + 5, hy + 7)
        paste_center(strip, head_layer, hx, hy)

        # Freeze-frame-style duplicate echoes during the chaotic celebration close-up.
        if 16.35 < t < 17.85:
            for n, alpha in ((1, 78), (2, 38)):
                ghost = head_layer.copy()
                ghost.putalpha(ghost.getchannel("A").point(lambda a, aa=alpha: a * aa // 255))
                paste_center(strip, ghost, hx - n * 34, hy + n * 7)

    canvas.alpha_composite(strip, (0, STRIP_Y))

    draw = ImageDraw.Draw(canvas, "RGBA")
    f_ankara = font("impact.ttf", 126)
    f_hud = font("ariblk.ttf", 64)
    f_small = font("arialbd.ttf", 42)
    text_center(draw, (OUT_W // 2, 102), "ANKARA", f_ankara, ORANGE + (255,), 6)
    logo_w = 900
    logo_fit = fit_rgba(logo, logo_w)
    paste_center(canvas, logo_fit, OUT_W / 2, 330)

    # Bottom game HUD: comprehensible even without language.
    panel = (72, 1328, 1008, 1808)
    draw.rounded_rectangle(panel, radius=34, fill=(17, 19, 22, 232),
                           outline=ORANGE + (255,), width=8)
    status = "HEAD EQUIPPED"
    if 13.1 <= t < 18.2:
        status = "GOAL  +999"
    elif 18.2 <= t < 22.0:
        status = "SYSTEM OVERHEATING"
    elif t >= 22.0:
        status = "HEAD UNLOCKED"
    text_center(draw, (OUT_W // 2, 1397), status, f_hud, CREAM + (255,), 4)

    bar_x, bar_y, bar_w, bar_h = 132, 1506, 816, 76
    draw.rounded_rectangle((bar_x, bar_y, bar_x + bar_w, bar_y + bar_h), 18,
                           fill=(42, 45, 50, 255), outline=(220, 220, 220, 170), width=4)
    if t < 16.2:
        progress = min(1.0, t / 15.2)
    elif t < 18.2:
        progress = 1.0
    elif t < 22.0:
        progress = 0.18 + 0.08 * math.sin(t * 8)
    else:
        progress = min(1.0, 0.5 + (t - 22.0) / 8.0)
    fill_w = max(18, int((bar_w - 12) * progress))
    draw.rounded_rectangle((bar_x + 6, bar_y + 6, bar_x + 6 + fill_w, bar_y + bar_h - 6), 12,
                           fill=ORANGE + (255,))
    for n in range(1, 8):
        x = bar_x + round(bar_w * n / 8)
        draw.line((x, bar_y + 6, x, bar_y + bar_h - 6), fill=(15, 17, 20, 120), width=3)
    text_center(draw, (OUT_W // 2, 1618), "DRIBBLE.EXE", f_small, (205, 210, 216, 255), 2)

    icon_w = 150 + round(12 * math.sin(t * 4.0))
    icon = fit_rgba(head, icon_w)
    paste_center(canvas, icon, OUT_W / 2, 1728)

    return canvas.convert("RGB")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=str(ROOT / "dev" / "shorts" / "output"))
    args = parser.parse_args()
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    cap = cv2.VideoCapture(str(SOURCE))
    if not cap.isOpened():
        raise RuntimeError(f"Could not open {SOURCE}")
    fps = cap.get(cv2.CAP_PROP_FPS)
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    if (width, height) != (SRC_W, SRC_H):
        raise RuntimeError(f"Unexpected source size: {width}x{height}")

    head = alpha_crop(Image.open(HEAD_PATH).convert("RGBA"))
    logo = alpha_crop(Image.open(LOGO_PATH).convert("RGBA"))
    silent = out_dir / "Ankara_Comstock_1080x1920_silent.mp4"
    ffmpeg_cmd = [
        str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{OUT_W}x{OUT_H}",
        "-r", f"{fps:.8f}", "-i", "-",
        "-an", "-c:v", "libx264", "-preset", "medium", "-crf", "18",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(silent),
    ]
    encoder = subprocess.Popen(ffmpeg_cmd, stdin=subprocess.PIPE)

    preview_targets = {
        "Ankara_Comstock_Poster_1080x1920.png": 6.0,
        "Ankara_Comstock_Dribble_1080x1920.png": 10.5,
        "Ankara_Comstock_Goal_1080x1920.png": 15.2,
    }
    saved: set[str] = set()
    for index in range(frame_count):
        ok, frame = cap.read()
        if not ok:
            break
        t = index / fps
        rendered = render_frame(frame, t, head, logo)
        assert encoder.stdin is not None
        encoder.stdin.write(np.asarray(rendered, dtype=np.uint8).tobytes())
        for name, target in preview_targets.items():
            if name not in saved and t >= target:
                rendered.save(out_dir / name, quality=95)
                saved.add(name)
        if index % 120 == 0:
            print(f"rendered {index}/{frame_count}", flush=True)

    cap.release()
    assert encoder.stdin is not None
    encoder.stdin.close()
    code = encoder.wait()
    if code:
        raise RuntimeError(f"ffmpeg encoder failed: {code}")

    final = out_dir / "Ankara_Comstock_1080x1920.mp4"
    mux = [
        str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
        "-i", str(silent), "-i", str(SOURCE),
        "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "copy",
        "-movflags", "+faststart", str(final),
    ]
    subprocess.run(mux, check=True)
    print(final)


if __name__ == "__main__":
    main()
