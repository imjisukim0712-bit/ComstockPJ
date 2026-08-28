# -*- coding: utf-8 -*-
"""컴스톡 15초 미국 로컬-TV 광고풍 세로 숏츠 렌더러."""
from __future__ import annotations

import hashlib
import json
import math
import os
import random
import re
import shutil
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
RES = ROOT / "Assets" / "Resources"
VENDOR = ROOT / "dev" / "pv" / "_vendor"
sys.path.insert(0, str(VENDOR))

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont
import imageio_ffmpeg

W, H = 540, 960
OUT_W, OUT_H = 1080, 1920
FPS, DUR = 30, 15.0
FRAMES = int(FPS * DUR)
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
LANCZOS = Image.Resampling.LANCZOS
BILINEAR = Image.Resampling.BILINEAR

RED = (202, 39, 49)
BLUE = (26, 55, 104)
CREAM = (252, 239, 205)
YELLOW = (255, 205, 45)
INK = (17, 18, 22)
WHITE = (255, 255, 255)

FONT_IMPACT = Path(r"C:\Windows\Fonts\impact.ttf")
FONT_BOLD = Path(r"C:\Windows\Fonts\arialbd.ttf")
FONT_REG = Path(r"C:\Windows\Fonts\arial.ttf")

_fonts: dict[tuple[str, int], ImageFont.FreeTypeFont] = {}
_sprites: dict[str, Image.Image] = {}


def font(kind: str, size: int) -> ImageFont.FreeTypeFont:
    key = (kind, size)
    if key not in _fonts:
        p = FONT_IMPACT if kind == "impact" else (FONT_BOLD if kind == "bold" else FONT_REG)
        _fonts[key] = ImageFont.truetype(str(p), size)
    return _fonts[key]


def clamp(v: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, v))


def ease_out(v: float) -> float:
    v = clamp(v)
    return 1.0 - (1.0 - v) ** 3


def sprite(rel: str, height: int | None = None, width: int | None = None,
           flip: bool = False, rotate: float = 0.0) -> Image.Image:
    key = f"{rel}|{height}|{width}|{flip}|{rotate:.1f}"
    if key in _sprites:
        return _sprites[key]
    im = Image.open(RES / rel).convert("RGBA")
    box = im.getbbox()
    if box:
        im = im.crop(box)
    if height:
        im = im.resize((max(1, round(im.width * height / im.height)), height), LANCZOS)
    elif width:
        im = im.resize((width, max(1, round(im.height * width / im.width))), LANCZOS)
    if flip:
        im = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if rotate:
        im = im.rotate(rotate, BILINEAR, expand=True)
    _sprites[key] = im
    return im


def paste(dst: Image.Image, src: Image.Image, x: float, y: float, anchor: str = "cc",
          alpha: float = 1.0) -> None:
    px = int(x - src.width / 2) if anchor[0] == "c" else (int(x) if anchor[0] == "l" else int(x - src.width))
    py = int(y - src.height / 2) if anchor[1] == "c" else (int(y) if anchor[1] == "t" else int(y - src.height))
    if alpha < 1:
        src = src.copy()
        src.putalpha(src.getchannel("A").point(lambda a: int(a * alpha)))
    dst.alpha_composite(src, (px, py))


def text_size(txt: str, fnt: ImageFont.FreeTypeFont, stroke: int = 0) -> tuple[int, int]:
    b = fnt.getbbox(txt, stroke_width=stroke)
    return b[2] - b[0], b[3] - b[1]


def fit_font(txt: str, max_width: int, max_size: int, kind: str = "impact",
             stroke: int = 0) -> ImageFont.FreeTypeFont:
    for size in range(max_size, 11, -2):
        f = font(kind, size)
        if text_size(txt, f, stroke)[0] <= max_width:
            return f
    return font(kind, 12)


def draw_text(cnv: Image.Image, txt: str, xy: tuple[int, int], max_width: int,
              max_size: int, fill=WHITE, stroke_fill=INK, stroke=5,
              kind="impact", anchor="mm", shadow=True) -> None:
    d = ImageDraw.Draw(cnv)
    f = fit_font(txt, max_width, max_size, kind, stroke)
    x, y = xy
    if shadow:
        d.text((x + 5, y + 7), txt, font=f, fill=(0, 0, 0, 150), anchor=anchor,
               stroke_width=stroke + 2, stroke_fill=(0, 0, 0, 130))
    d.text((x, y), txt, font=f, fill=fill, anchor=anchor,
           stroke_width=stroke, stroke_fill=stroke_fill)


def burst(cnv: Image.Image, center=(270, 500), rays=22, colors=(RED, CREAM)) -> None:
    d = ImageDraw.Draw(cnv)
    cx, cy = center
    radius = 1250
    for i in range(rays):
        a0 = math.tau * i / rays
        a1 = math.tau * (i + 1) / rays
        col = colors[i % len(colors)]
        d.polygon([(cx, cy), (cx + math.cos(a0) * radius, cy + math.sin(a0) * radius),
                   (cx + math.cos(a1) * radius, cy + math.sin(a1) * radius)], fill=col)


def star_field(cnv: Image.Image, seed: int, count=34, alpha=120) -> None:
    rng = random.Random(seed)
    d = ImageDraw.Draw(cnv)
    for _ in range(count):
        x, y = rng.randrange(18, W - 18), rng.randrange(20, H - 20)
        r = rng.randrange(3, 11)
        col = (*WHITE, alpha)
        d.line((x - r, y, x + r, y), fill=col, width=max(1, r // 3))
        d.line((x, y - r, x, y + r), fill=col, width=max(1, r // 3))


def ribbon(cnv: Image.Image, y: int, label: str, bg=BLUE, fg=WHITE, h=74) -> None:
    d = ImageDraw.Draw(cnv)
    d.polygon([(0, y), (W, y - 16), (W, y + h), (0, y + h + 16)], fill=bg)
    draw_text(cnv, label, (W // 2, y + h // 2), W - 36, 54, fill=fg,
              stroke_fill=INK, stroke=3, kind="impact")


def pop_sprite(im: Image.Image, t: float, start: float, base_h: int,
               overshoot=1.12) -> Image.Image:
    p = clamp((t - start) / 0.28)
    q = ease_out(p)
    s = q * (overshoot - (overshoot - 1.0) * p)
    h = max(2, int(base_h * s))
    return im.resize((max(2, int(im.width * h / im.height)), h), LANCZOS)


def base_canvas(color=CREAM) -> Image.Image:
    return Image.new("RGBA", (W, H), (*color, 255))


def scene_zombie(t: float) -> Image.Image:
    c = base_canvas(BLUE)
    burst(c, (270, 520), 26, (BLUE, RED, CREAM))
    z = sprite("Enemy_zombie.png", height=620, rotate=-4 + math.sin(t * 10) * 2)
    z = pop_sprite(z, t, 0.10, 620)
    paste(c, z, 275, 740, "cb")
    ribbon(c, 85, "AMERICA!", RED, WHITE, 78)
    draw_text(c, "TOO MANY", (270, 220), 500, 104, fill=YELLOW, stroke=8)
    draw_text(c, "ZOMBIES?", (270, 320), 500, 120, fill=WHITE, stroke=8)
    return c


def scene_guns(t: float) -> Image.Image:
    c = base_canvas(CREAM)
    burst(c, (270, 510), 28, (CREAM, RED))
    robot = sprite("Comstock.png", height=470)
    bob = math.sin(t * 12) * 8
    paste(c, robot, 270, 720 + bob, "cb")
    weapons = [
        ("RightRocketLauncher.png", 205, 30, 520, -13),
        ("RightPlasmaCannon.png", 190, 505, 545, 12),
        ("RightCombatShotgun.png", 180, 120, 730, -22),
    ]
    for i, (name, h, x, y, rot) in enumerate(weapons):
        src = sprite(name, height=h, rotate=rot)
        src = pop_sprite(src, t, 0.15 + i * 0.10, h)
        paste(c, src, x, y)
    draw_text(c, "ADD MORE GUNS.", (270, 125), 510, 96, fill=YELLOW, stroke=8)
    ribbon(c, 800, "THIS IS SCIENCE.", BLUE, WHITE, 72)
    return c


def scene_number(t: float, number: str, label: str, assets: list[str], color=BLUE) -> Image.Image:
    c = base_canvas(color)
    star_field(c, int(t * 1000) + len(number), 42, 100)
    p = ease_out((t - 0.05) / 0.26)
    size = int(245 * max(0.05, p))
    draw_text(c, number, (270, 315), 510, size, fill=YELLOW, stroke=10)
    draw_text(c, label, (270, 485), 500, 98, fill=WHITE, stroke=7)
    for i, name in enumerate(assets):
        ang = (i - (len(assets) - 1) / 2) * 0.24
        x = 270 + math.sin(ang) * 410
        y = 720 + abs(i - (len(assets) - 1) / 2) * 22
        h = 145 if "PartIcons" in name else 165
        im = sprite(name, height=h, rotate=math.degrees(ang) * 0.7)
        paste(c, im, x, y)
    ribbon(c, 835, "MIX. MATCH. REGRET NOTHING.", RED, WHITE, 62)
    return c


def scene_autofire(t: float) -> Image.Image:
    c = base_canvas((34, 36, 42))
    d = ImageDraw.Draw(c)
    for y in range(0, H, 40):
        d.rectangle((0, y, W, y + 20), fill=(45, 48, 56, 255))
    robot = sprite("Comstock.png", height=430)
    paste(c, robot, 275, 680 + math.sin(t * 13) * 9, "cb")
    for i in range(7):
        x = (600 + i * 155 - t * 1050) % 820 - 140
        z = sprite("Enemy_zombie.png", height=175, flip=(i % 2 == 0))
        paste(c, z, x, 785, "cb")
    rng = random.Random(int(t * FPS))
    for _ in range(16):
        x = rng.randrange(20, W - 20); y = rng.randrange(360, 750)
        d.line((x, y, x + rng.randrange(35, 90), y + rng.randrange(-10, 11)), fill=YELLOW, width=5)
    draw_text(c, "AUTO-FIRES.", (270, 120), 500, 108, fill=YELLOW, stroke=8)
    draw_text(c, "AIMING IS A MEETING.", (270, 235), 510, 58, fill=WHITE, stroke=5, kind="bold")
    ribbon(c, 820, "HR WAS NOT CONSULTED.", RED, WHITE, 68)
    return c


def scene_boss(t: float) -> Image.Image:
    c = base_canvas(INK)
    burst(c, (270, 560), 24, ((63, 30, 92), INK, RED))
    boss = sprite("BossGroggy/frame_16.png", height=520)
    boss = pop_sprite(boss, t, 0.18, 520)
    paste(c, boss, 270, 815, "cb")
    draw_text(c, "20 WAVES", (270, 125), 510, 112, fill=YELLOW, stroke=8)
    draw_text(c, "1 LARGE PROBLEM", (270, 250), 510, 78, fill=WHITE, stroke=7)
    d = ImageDraw.Draw(c)
    d.rounded_rectangle((28, 810, 512, 872), 18, fill=INK, outline=WHITE, width=4)
    hp = int(456 * clamp(1 - t / 2.2))
    d.rounded_rectangle((42, 824, 42 + hp, 858), 12, fill=RED)
    return c


def scene_cta(t: float) -> Image.Image:
    c = base_canvas(BLUE)
    burst(c, (270, 490), 30, (BLUE, RED, CREAM))
    robot = sprite("Comstock.png", height=390)
    s = 1 + 0.035 * math.sin(t * 9)
    robot = robot.resize((int(robot.width * s), int(robot.height * s)), LANCZOS)
    paste(c, robot, 270, 610, "cc")
    draw_text(c, "COMSTOCK", (270, 135), 520, 135, fill=YELLOW, stroke=10)
    ribbon(c, 720, "BUILD WORSE. WIN BETTER.", INK, WHITE, 78)
    draw_text(c, "PLAY NOW", (270, 835), 480, 86, fill=YELLOW, stroke=7)
    d = ImageDraw.Draw(c)
    fine = "Side effects may include extra guns, poor judgment, and surviving."
    d.text((270, 895), fine, font=font("bold", 15), fill=WHITE, anchor="ms",
           stroke_width=2, stroke_fill=INK)
    return c


def content_at(t: float) -> Image.Image:
    if t < 2.35:
        return scene_zombie(t)
    if t < 4.35:
        return scene_guns(t - 2.35)
    if t < 6.25:
        return scene_number(t - 4.35, "134", "PARTS", [
            "PartIcons/Leg_Rocket.png", "Parts/Rocket/0.png", "Parts/Rocket/3.png"])
    if t < 7.95:
        return scene_number(t - 6.25, "14", "WEAPONS", [
            "RightLaserPistol.png", "RightRocketLauncher.png", "RightPlasmaCannon.png"], RED)
    if t < 10.35:
        return scene_autofire(t - 7.95)
    if t < 12.55:
        return scene_boss(t - 10.35)
    return scene_cta(t - 12.55)


def vhs_process(im: Image.Image, frame: int) -> Image.Image:
    t = frame / FPS
    rng = random.Random(frame * 7919 + 41)
    rgb = im.convert("RGB")
    # 채도와 대비를 올려 90년대 저가 방송 그래픽처럼 만든다.
    rgb = ImageEnhance.Color(rgb).enhance(1.22)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.10)
    # 수평 색 번짐.
    if frame % 5 == 0:
        r, g, b = rgb.split()
        r = ImageChops.offset(r, 2, 0)
        b = ImageChops.offset(b, -2, 0)
        rgb = Image.merge("RGB", (r, g, b))
    # 컷마다 짧은 동기 이탈.
    strength = 0.0
    for cut in (0.0, 2.35, 4.35, 6.25, 7.95, 10.35, 12.55):
        strength += math.exp(-((t - cut) / 0.055) ** 2)
    if strength > 0.15:
        for _ in range(2 + int(4 * min(1, strength))):
            y = rng.randrange(0, H - 12)
            hh = rng.randrange(5, 32)
            band = rgb.crop((0, y, W, y + hh))
            rgb.paste(ImageChops.offset(band, rng.randrange(-28, 29), 0), (0, y))
    # 스캔라인과 얕은 잡음.
    d = ImageDraw.Draw(rgb, "RGBA")
    phase = frame % 3
    for y in range(phase, H, 3):
        d.line((0, y, W, y), fill=(0, 0, 0, 36))
    for _ in range(100):
        x, y = rng.randrange(W), rng.randrange(H)
        v = rng.choice((255, 0))
        d.point((x, y), fill=(v, v, v, rng.randrange(12, 42)))
    # 전체 화면의 과장된 손떨림.
    sx = rng.randrange(-2, 3)
    sy = rng.randrange(-1, 2)
    return ImageChops.offset(rgb, sx, sy)


def render_silent(path: Path) -> None:
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
           "-r", str(FPS), "-i", "-", "-an", "-vf", f"scale={OUT_W}:{OUT_H}:flags=lanczos",
           "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p",
           "-r", str(FPS), "-movflags", "+faststart", str(path)]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    assert proc.stdin is not None
    for frame in range(FRAMES):
        im = vhs_process(content_at(frame / FPS), frame)
        proc.stdin.write(im.tobytes())
        if frame % 90 == 0:
            print(f"render {frame:03d}/{FRAMES}", flush=True)
    proc.stdin.close()
    rc = proc.wait()
    if rc:
        raise RuntimeError(f"video encode failed: {rc}")


def synth_voice(path: Path) -> None:
    ps = shutil.which("pwsh")
    if not ps:
        raise RuntimeError("PowerShell 7 (pwsh) was not found")
    subprocess.run([ps, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    str(HERE / "make_voice.ps1"), "-OutputPath", str(path)], check=True)


def voice_duration(path: Path) -> float:
    with wave.open(str(path), "rb") as w:
        return w.getnframes() / w.getframerate()


def mix_audio(silent: Path, voice: Path, out: Path) -> None:
    inputs = [
        (RES / "SFX" / "Weapon_Explosive.wav", 2300, 0.58),
        (RES / "SFX" / "LevelUp.wav", 4350, 0.55),
        (RES / "SFX" / "UI_Click.wav", 6250, 0.80),
        (RES / "SFX" / "Weapon_RapidFire.wav", 8000, 0.32),
        (RES / "SFX" / "Boss_Hit_A.wav", 10350, 0.62),
        (RES / "SFX" / "Boss_Death.wav", 12200, 0.70),
        (RES / "SFX" / "Weapon_Explosive.wav", 12550, 0.64),
    ]
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error", "-i", str(silent), "-i", str(voice)]
    for p, _, _ in inputs:
        cmd += ["-i", str(p)]
    filters = [
        "aevalsrc=0.10*sin(2*PI*55*t)+0.045*sin(2*PI*110*t):s=48000:d=15,"
        "tremolo=f=7:d=0.35,acompressor=threshold=-20dB:ratio=4[bed]",
        "[1:a]highpass=f=110,lowpass=f=7600,volume=1.55,adelay=120|120[vo]",
    ]
    labels = ["[bed]", "[vo]"]
    for idx, (_, delay, gain) in enumerate(inputs, start=2):
        label = f"s{idx}"
        filters.append(f"[{idx}:a]volume={gain},adelay={delay}|{delay}[{label}]")
        labels.append(f"[{label}]")
    filters.append("".join(labels) + f"amix=inputs={len(labels)}:duration=longest:normalize=0,"
                   "alimiter=limit=0.92,atrim=0:15[aout]")
    cmd += ["-filter_complex", ";".join(filters), "-map", "0:v:0", "-map", "[aout]",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-t", "15",
            "-movflags", "+faststart", str(out)]
    subprocess.run(cmd, check=True)


def make_previews() -> list[str]:
    times = [0.8, 2.8, 4.8, 6.7, 8.7, 10.8, 13.4]
    names = []
    thumbs = []
    for t in times:
        name = f"preview_{t:04.1f}s.jpg"
        p = HERE / name
        im = vhs_process(content_at(t), int(t * FPS))
        im.resize((540, 960), LANCZOS).save(p, quality=92)
        names.append(name)
        thumbs.append(im.resize((270, 480), LANCZOS))
    sheet = Image.new("RGB", (270 * 4, 480 * 2), (20, 20, 20))
    for i, im in enumerate(thumbs):
        sheet.paste(im, ((i % 4) * 270, (i // 4) * 480))
    sheet.save(HERE / "contact-sheet.jpg", quality=92)
    return names


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def probe_duration(path: Path) -> float:
    proc = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path)], text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    m = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", proc.stderr)
    if not m:
        raise RuntimeError("duration probe failed")
    return int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))


def main() -> None:
    silent = HERE / "_silent.mp4"
    voice = HERE / "voiceover.wav"
    final = HERE / "Comstock_US_Shorts_15s.mp4"
    if not voice.exists() or voice.stat().st_size < 1024:
        synth_voice(voice)
    print(f"voice {voice_duration(voice):.2f}s")
    render_silent(silent)
    mix_audio(silent, voice, final)
    previews = make_previews()
    duration = probe_duration(final)
    if abs(duration - DUR) > 0.05:
        raise RuntimeError(f"unexpected duration: {duration}")
    manifest = {
        "title": "Comstock — Build Worse. Win Better.",
        "format": "vertical short ad",
        "style": "1990s American local-TV / monster-truck dealer parody",
        "size": [OUT_W, OUT_H],
        "fps": FPS,
        "duration_seconds": duration,
        "language": "English (en-US)",
        "claims": ["134 parts", "14 weapons", "20 waves", "automatic firing"],
        "copy": ["TOO MANY ZOMBIES?", "ADD MORE GUNS.", "134 PARTS", "14 WEAPONS",
                 "AUTO-FIRES. AIMING IS A MEETING.", "20 WAVES. 1 LARGE PROBLEM.",
                 "COMSTOCK", "BUILD WORSE. WIN BETTER.", "PLAY NOW"],
        "sources": [
            "Assets/Resources/Comstock.png", "Assets/Resources/Enemy_zombie.png",
            "Assets/Resources/BossGroggy/frame_16.png", "Assets/Resources/RightRocketLauncher.png",
            "Assets/Resources/RightPlasmaCannon.png", "Assets/Resources/RightCombatShotgun.png",
            "Assets/Resources/RightLaserPistol.png", "Assets/Resources/PartIcons/Leg_Rocket.png",
            "Assets/Resources/Parts/Rocket/0.png", "Assets/Resources/Parts/Rocket/3.png",
            "Assets/Resources/SFX/*.wav"
        ],
        "voice": "Microsoft Zira Desktop (en-US), locally synthesized",
        "output": final.name,
        "sha256": sha256(final),
        "previews": previews + ["contact-sheet.jpg"],
        "generator": Path(__file__).name,
    }
    (HERE / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    silent.unlink(missing_ok=True)
    print(f"complete: {final}")
    print(f"duration: {duration:.2f}s, sha256: {manifest['sha256']}")


if __name__ == "__main__":
    main()
