# -*- coding: utf-8 -*-
"""컴스톡 세로 숏츠 광고 V2 - 3개 밈 × 한/영 2벌.

가로 광고 슬라이드 문법을 버리고 9:16 세로 화면, 0.4~1.2초 상태 변화, 줌/흔들림/펀치 컷,
한 화면 한 문장으로 구성한다. 최종 2.4초는 사용자 제공 엔드카드를 언어별로 재합성한다.
"""
from __future__ import annotations

import argparse
import array
import hashlib
import io
import json
import math
import random
import re
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
RES = ROOT / "Assets" / "Resources"
HERO = ROOT / "dev" / "pv" / "assets" / "comstock_hero.png"
ENDCARD = HERE / "endcard_source.png"
VENDOR = ROOT / "dev" / "pv" / "_vendor"
sys.path.insert(0, str(VENDOR))

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont
import imageio_ffmpeg

W, H = 540, 960
OUT_W, OUT_H = 1080, 1920
FPS, DUR = 30, 15.0
FRAMES = int(FPS * DUR)
END_AT = 12.6
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
LANCZOS = Image.Resampling.LANCZOS
BILINEAR = Image.Resampling.BILINEAR

FONT_IMPACT = Path(r"C:\Windows\Fonts\impact.ttf")
FONT_EN_BOLD = Path(r"C:\Windows\Fonts\arialbd.ttf")
FONT_EN = Path(r"C:\Windows\Fonts\arial.ttf")
FONT_KO_BOLD = Path(r"C:\Windows\Fonts\malgunbd.ttf")
FONT_KO = Path(r"C:\Windows\Fonts\malgun.ttf")

LANG = {
    "en": {
        "cta": "PLAY IT BEFORE THE ZOMBIES DO.",
        "price": "REGULAR PRICE $59.99? NOPE.",
        "free": "FREE",
    },
    "ko": {
        "cta": "좀비보다 먼저 플레이하세요.",
        "price": "정가 59,990원? 아니죠.",
        "free": "무료",
    },
}

COPY = {
    "delivery": {
        "en": {
            "hook": "YOUR DELIVERY IS HERE",
            "motion": "MOTION DETECTED",
            "time": "FRONT DOOR  •  2:13 AM",
            "leave": "LEAVE IT AT THE DOOR.",
            "ok": "OK.",
            "didnt": "HE DID NOT.",
            "wrong": "WRONG HOUSE.",
            "done": "DELIVERED",
            "speed": "SPEED",
            "survival": "SURVIVAL",
        },
        "ko": {
            "hook": "배달이 도착했습니다",
            "motion": "움직임 감지",
            "time": "현관문  •  오전 2:13",
            "leave": "문 앞에 두고 가주세요.",
            "ok": "네.",
            "didnt": "안 두고 감.",
            "wrong": "집 잘못 찾음.",
            "done": "배송 완료",
            "speed": "배송 속도",
            "survival": "생존률",
        },
    },
    "one_more": {
        "en": {
            "friend": "BRO: ONE GUN IS ENOUGH.",
            "me": "ME: CORRECT.",
            "guns": "GUNS",
            "one": "ONE...",
            "more": "MORE.",
            "recoil": "RECOIL: YES",
            "look": "THEY LOOKED UP.",
            "travel": "TRAVEL METHOD\nUNLOCKED",
        },
        "ko": {
            "friend": "친구: 무기 하나면 충분해.",
            "me": "나: 맞아.",
            "guns": "무기",
            "one": "하나...",
            "more": "더.",
            "recoil": "반동: 있음",
            "look": "좀비들이 위를 봄.",
            "travel": "이동기\n해금",
        },
    },
    "interview": {
        "en": {
            "hook": "ZOMBIE INTERVIEW\n1-SECOND SPEEDRUN",
            "question": "YOUR BIGGEST STRENGTH?",
            "answer": "AUTOMATIC.",
            "what": "WHAT IS?",
            "yes": "YES.",
            "auto": "AUTO",
            "hired": "HIRED",
            "ending": "NO INTERVIEWER.\nSTILL HIRED.",
        },
        "ko": {
            "hook": "좀비 면접\n1초컷",
            "question": "본인 장점은?",
            "answer": "자동입니다.",
            "what": "뭐가요?",
            "yes": "네.",
            "auto": "자동",
            "hired": "합격",
            "ending": "면접관 없음.\n그래도 합격.",
        },
    },
}

_fonts: dict[tuple[str, str, int], ImageFont.FreeTypeFont] = {}
_images: dict[str, Image.Image] = {}


def clamp(v: float, lo=0.0, hi=1.0) -> float:
    return max(lo, min(hi, v))


def ease_out(v: float) -> float:
    v = clamp(v)
    return 1 - (1 - v) ** 3


def ease_in(v: float) -> float:
    v = clamp(v)
    return v ** 3


def font(lang: str, kind: str, size: int) -> ImageFont.FreeTypeFont:
    key = (lang, kind, size)
    if key not in _fonts:
        if lang == "ko":
            p = FONT_KO_BOLD if kind in ("impact", "bold") else FONT_KO
        else:
            p = FONT_IMPACT if kind == "impact" else (FONT_EN_BOLD if kind == "bold" else FONT_EN)
        _fonts[key] = ImageFont.truetype(str(p), size)
    return _fonts[key]


def load(path: Path, height=None, width=None, flip=False, rotate=0.0) -> Image.Image:
    key = f"{path}|{height}|{width}|{flip}|{rotate:.1f}"
    if key in _images:
        return _images[key]
    im = Image.open(path).convert("RGBA")
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
    _images[key] = im
    return im


def sprite(rel: str, **kwargs) -> Image.Image:
    return load(RES / rel, **kwargs)


def paste(dst: Image.Image, src: Image.Image, x: float, y: float, anchor="cc", alpha=1.0) -> None:
    px = int(x - src.width / 2) if anchor[0] == "c" else (int(x) if anchor[0] == "l" else int(x - src.width))
    py = int(y - src.height / 2) if anchor[1] == "c" else (int(y) if anchor[1] == "t" else int(y - src.height))
    if alpha < 1:
        src = src.copy()
        src.putalpha(src.getchannel("A").point(lambda a: int(a * alpha)))
    dst.alpha_composite(src, (px, py))


def fit(text: str, lang: str, max_w: int, max_h: int, max_size: int,
        kind="impact", stroke=0) -> ImageFont.FreeTypeFont:
    lines = text.splitlines()
    for size in range(max_size, 11, -1):
        f = font(lang, kind, size)
        bs = [f.getbbox(s, stroke_width=stroke) for s in lines]
        w = max(b[2] - b[0] for b in bs)
        h = sum(b[3] - b[1] for b in bs) + (len(lines) - 1) * int(size * 0.15)
        if w <= max_w and h <= max_h:
            return f
    return font(lang, kind, 12)


def text(cnv: Image.Image, s: str, lang: str, xy: tuple[float, float], max_w: int,
         max_h: int, max_size: int, fill=(255, 255, 255), stroke_fill=(7, 7, 11),
         stroke=5, kind="impact", anchor="mm", shadow=True, spacing=2) -> None:
    d = ImageDraw.Draw(cnv)
    f = fit(s, lang, max_w, max_h, max_size, kind, stroke)
    x, y = xy
    if shadow:
        d.multiline_text((x + 4, y + 7), s, font=f, fill=(0, 0, 0, 160), anchor=anchor,
                         align="center", spacing=spacing, stroke_width=stroke + 2,
                         stroke_fill=(0, 0, 0, 150))
    d.multiline_text((x, y), s, font=f, fill=fill, anchor=anchor, align="center",
                     spacing=spacing, stroke_width=stroke, stroke_fill=stroke_fill)


def bubble(cnv: Image.Image, s: str, lang: str, box: tuple[int, int, int, int],
           side="left", bg=(255, 255, 255), fg=(20, 20, 24), outline=(20, 20, 24)) -> None:
    d = ImageDraw.Draw(cnv)
    x0, y0, x1, y1 = box
    d.rounded_rectangle(box, 22, fill=bg, outline=outline, width=4)
    if side == "left":
        d.polygon([(x0 + 32, y1 - 3), (x0 + 10, y1 + 28), (x0 + 68, y1 - 3)], fill=bg)
    else:
        d.polygon([(x1 - 32, y1 - 3), (x1 - 10, y1 + 28), (x1 - 68, y1 - 3)], fill=bg)
    text(cnv, s, lang, ((x0 + x1) / 2, (y0 + y1) / 2), x1 - x0 - 34, y1 - y0 - 18,
         38, fill=fg, stroke=0, kind="bold", shadow=False)


def burst(cnv: Image.Image, center, colors, rays=28, phase=0.0) -> None:
    d = ImageDraw.Draw(cnv)
    cx, cy = center
    radius = 1800
    for i in range(rays):
        a0 = math.tau * i / rays + phase
        a1 = math.tau * (i + 1) / rays + phase
        d.polygon([(cx, cy), (cx + math.cos(a0) * radius, cy + math.sin(a0) * radius),
                   (cx + math.cos(a1) * radius, cy + math.sin(a1) * radius)],
                  fill=colors[i % len(colors)])


def star_points(cx: float, cy: float, outer: float, inner: float | None = None):
    inner = inner if inner is not None else outer * 0.46
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = outer if i % 2 == 0 else inner
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def draw_stars(cnv: Image.Image, count: int, y: float, total=5, size=25,
               fill=(255, 220, 45), empty=(68, 70, 78), outline=(10, 10, 14)) -> None:
    d = ImageDraw.Draw(cnv)
    gap = size * 2.25
    x0 = W / 2 - gap * (total - 1) / 2
    for i in range(total):
        d.polygon(star_points(x0 + i * gap, y, size),
                  fill=fill if i < count else empty, outline=outline)


def speed_lines(cnv: Image.Image, center, t: float, color=(255, 255, 255, 120), count=34) -> None:
    d = ImageDraw.Draw(cnv, "RGBA")
    rng = random.Random(int(t * 12) + 218)
    cx, cy = center
    for _ in range(count):
        a = rng.uniform(0, math.tau)
        r0 = rng.uniform(80, 280)
        ln = rng.uniform(110, 400)
        d.line((cx + math.cos(a) * r0, cy + math.sin(a) * r0,
                cx + math.cos(a) * (r0 + ln), cy + math.sin(a) * (r0 + ln)),
               fill=color, width=rng.randint(2, 7))


def draw_zombie(cnv: Image.Image, x: float, y: float, h: int, t: float,
                flip=False, rotate=0.0) -> None:
    idx = int(t * 10) % 8
    im = sprite(f"ZombieMove/walk_left_f{idx}.png", height=h, flip=flip, rotate=rotate)
    paste(cnv, im, x, y, "cb")


def draw_hero(cnv: Image.Image, x: float, y: float, h: int, t: float,
              rotate=0.0, flip=False, bob=True) -> None:
    hh = max(2, int(h * (1 + 0.025 * math.sin(t * 12))))
    im = load(HERO, height=hh, rotate=rotate, flip=flip)
    paste(cnv, im, x, y + (math.sin(t * 12) * 4 if bob else 0), "cb")


def explosion(cnv: Image.Image, x: float, y: float, t: float, at: float,
              h=190, alpha=1.0) -> None:
    if not (at <= t < at + 0.65):
        return
    idx = min(10, int((t - at) / 0.65 * 10) + 1)
    paste(cnv, sprite(f"Explosion/frame_{idx:02d}.png", height=h), x, y, alpha=alpha)


def muzzle(cnv: Image.Image, x: float, y: float, t: float, scale=150) -> None:
    idx = int(t * 22) % 3 + 1
    paste(cnv, sprite(f"MuzzleFlash/frame_{idx:02d}.png", height=scale), x, y)


def gradient(c1, c2) -> Image.Image:
    im = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(im)
    for y in range(H):
        q = y / (H - 1)
        c = tuple(int(c1[i] * (1 - q) + c2[i] * q) for i in range(3))
        d.line((0, y, W, y), fill=(*c, 255))
    return im


def notification(cnv: Image.Image, title_s: str, body: str, lang: str, y: int, p=1.0) -> None:
    p = ease_out(p)
    h = int(128 * p)
    if h < 4:
        return
    d = ImageDraw.Draw(cnv)
    x0, x1 = 28, W - 28
    d.rounded_rectangle((x0, y, x1, y + h), 26, fill=(245, 247, 250, 246),
                        outline=(170, 178, 190), width=3)
    if h > 70:
        d.ellipse((48, y + 28, 94, y + 74), fill=(255, 104, 55))
        text(cnv, "!", "en", (71, y + 51), 32, 38, 30, fill=(255, 255, 255), stroke=0, shadow=False)
        text(cnv, title_s, lang, (112, y + 40), 365, 30, 25, fill=(28, 30, 36), stroke=0,
             kind="bold", anchor="lm", shadow=False)
        text(cnv, body, lang, (112, y + 82), 365, 33, 27, fill=(65, 68, 76), stroke=0,
             kind="bold", anchor="lm", shadow=False)


def delivery_frame(t: float, lang: str) -> Image.Image:
    C = COPY["delivery"][lang]
    cnv = gradient((12, 17, 31), (22, 45, 72))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 0.58:
        d.rectangle((0, 0, W, H), fill=(8, 10, 16))
        notification(cnv, "DOORBELL" if lang == "en" else "초인종", C["hook"], lang, 120, t / 0.22)
        text(cnv, "2:13", "en", (270, 65), 300, 60, 52, fill=(255, 255, 255), stroke=2, kind="bold")
        d.line((210, 295, 330, 295), fill=(255, 255, 255, 60), width=5)
        d.ellipse((250, 350, 290, 390), outline=(255, 255, 255, 120), width=5)
    elif t < 2.15:
        lt = t - 0.58
        # 세로 도어벨 카메라: 얼굴이 화면을 과도하게 채우고 타임코드가 흔들린다.
        for r in range(450, 40, -40):
            a = int(20 + (450 - r) * 0.12)
            d.ellipse((270 - r, 480 - r, 270 + r, 480 + r), outline=(120, 175, 196, a), width=3)
        zh = int(650 + 90 * math.sin(lt * 9))
        draw_zombie(cnv, 272, 860, zh, lt, rotate=math.sin(lt * 13) * 3)
        d.rounded_rectangle((34, 38, 506, 108), 14, fill=(0, 0, 0, 170), outline=(255, 80, 70), width=3)
        text(cnv, C["motion"], lang, (270, 72), 430, 48, 38, fill=(255, 84, 72), stroke=2, kind="bold")
        text(cnv, C["time"], lang, (270, 914), 460, 36, 25, fill=(255, 255, 255), stroke=2, kind="bold")
        d.ellipse((474, 52, 490, 68), fill=(255, 67, 56))
    elif t < 3.35:
        lt = t - 2.15
        d.rectangle((0, 0, W, H), fill=(228, 234, 242))
        text(cnv, "MESSAGES" if lang == "en" else "메시지", lang, (270, 62), 440, 46, 36,
             fill=(31, 35, 44), stroke=0, kind="bold", shadow=False)
        bubble(cnv, C["leave"], lang, (62, 170, 492, 300), "right", bg=(42, 121, 245), fg=(255, 255, 255), outline=(42, 121, 245))
        if lt > 0.48:
            bubble(cnv, C["ok"], lang, (55, 365, 235, 475), "left", bg=(255, 255, 255), fg=(30, 34, 42), outline=(190, 197, 208))
        if lt > 0.82:
            d.ellipse((65, 510, 83, 528), fill=(128, 136, 149))
            d.ellipse((91, 510, 109, 528), fill=(128, 136, 149))
            d.ellipse((117, 510, 135, 528), fill=(128, 136, 149))
    elif t < 4.5:
        lt = t - 3.35
        d.rectangle((0, 0, W, H), fill=(61, 26, 34))
        d.rectangle((62, 0, 478, H), fill=(24, 18, 23))
        d.rectangle((82, 0, 458, H), outline=(132, 84, 54), width=16)
        x = 270 + ease_out(lt / 0.65) * 115
        draw_zombie(cnv, x, 890, 620, lt, flip=True, rotate=-6)
        text(cnv, C["didnt"], lang, (270, 130), 460, 100, 66, fill=(255, 226, 69), stroke=6)
        d.polygon([(0, 780), (540, 715), (540, 960), (0, 960)], fill=(210, 31, 48, 230))
    elif t < 5.25:
        lt = t - 4.5
        burst(cnv, (270, 490), ((255, 203, 37), (234, 55, 44), (23, 25, 33)), 34, lt * 0.3)
        q = ease_out(lt / 0.18)
        draw_hero(cnv, 270, 900, max(10, int(520 * q)), lt, rotate=math.sin(lt * 15) * 3)
        text(cnv, C["wrong"], lang, (270, 120), 470, 100, 72, fill=(255, 255, 255), stroke=7)
        speed_lines(cnv, (270, 520), lt, (255, 255, 255, 150), 42)
    elif t < 8.9:
        lt = t - 5.25
        burst(cnv, (270, 630), ((53, 22, 84), (149, 38, 174), (245, 94, 47)), 30, lt * 0.08)
        for i in range(10):
            draw_zombie(cnv, 35 + i * 55, 905, 220, lt + i * 0.13, flip=i % 2 == 0)
        draw_hero(cnv, 275, 880, 440, lt, rotate=math.sin(lt * 18) * 4)
        speed_lines(cnv, (270, 600), lt, (255, 225, 80, 145), 55)
        muzzle(cnv, 505, 585 + math.sin(lt * 19) * 40, lt, 190)
        for i, (x, y, at) in enumerate(((85, 650, 0.1), (450, 700, 0.55), (135, 800, 1.0),
                                        (410, 580, 1.45), (250, 720, 2.0), (480, 840, 2.55))):
            explosion(cnv, x, y, lt, at, 220 + (i % 2) * 40)
        score = min(5, 1 + int(lt / 0.55))
        draw_stars(cnv, score, 120, total=5, size=25)
    elif t < 10.35:
        lt = t - 8.9
        d.rectangle((0, 0, W, H), fill=(20, 24, 31))
        # 배송 완료 사진: 상자와 연기 나는 구덩이.
        d.ellipse((45, 430, 495, 905), fill=(9, 10, 12), outline=(74, 76, 82), width=8)
        for i in range(8):
            x = 80 + i * 55
            d.ellipse((x, 300 - i % 3 * 20, x + 70, 610), fill=(80, 80, 84, 35))
        d.rectangle((175, 600, 365, 760), fill=(180, 116, 58), outline=(73, 42, 20), width=8)
        d.line((175, 600, 365, 760), fill=(98, 62, 30), width=5)
        d.line((365, 600, 175, 760), fill=(98, 62, 30), width=5)
        notification(cnv, "DELIVERY" if lang == "en" else "배달", C["done"], lang, 55, lt / 0.2)
        text(cnv, "PHOTO PROOF" if lang == "en" else "배송 사진", lang, (270, 860), 400, 44, 30,
             fill=(210, 214, 220), stroke=2, kind="bold")
    else:
        lt = t - 10.35
        burst(cnv, (270, 480), ((255, 226, 89), (255, 116, 55), (41, 30, 55)), 28, 0.0)
        d.rounded_rectangle((38, 150, 502, 790), 36, fill=(249, 246, 238), outline=(25, 25, 31), width=7)
        text(cnv, C["done"], lang, (270, 235), 410, 60, 48, fill=(32, 36, 43), stroke=0, kind="bold", shadow=False)
        text(cnv, C["speed"], lang, (115, 390), 180, 50, 35, fill=(32, 36, 43), stroke=0, kind="bold", shadow=False)
        # 글꼴 기호 대신 벡터 별을 직접 그려 한/영 모두 동일하게 보이게 한다.
        sx = W / 2 + 65
        for i in range(5):
            d.polygon(star_points(sx + i * 37 - 74, 390, 16), fill=(255, 183, 0), outline=(25, 25, 31))
        text(cnv, C["survival"], lang, (115, 545), 180, 50, 35, fill=(32, 36, 43), stroke=0, kind="bold", shadow=False)
        for i in range(5):
            d.polygon(star_points(sx + i * 37 - 74, 545, 16),
                      fill=(225, 65, 55) if i == 0 else (178, 178, 178), outline=(25, 25, 31))
        draw_zombie(cnv, 270, 820, 230, lt, rotate=8)
    return cnv.convert("RGB")


WEAPONS = [
    ("RightRocketLauncher.png", 350, -35, 535, -18),
    ("RightPlasmaCannon.png", 310, 575, 535, 18),
    ("RightCombatShotgun.png", 260, 55, 740, -28),
    ("RightLaserPistol.png", 245, 492, 750, 24),
]


def one_more_frame(t: float, lang: str) -> Image.Image:
    C = COPY["one_more"][lang]
    cnv = Image.new("RGBA", (W, H), (24, 28, 39, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 0.72:
        d.rectangle((0, 0, W, H), fill=(229, 234, 242))
        text(cnv, "CHAT" if lang == "en" else "채팅", lang, (270, 70), 420, 50, 38,
             fill=(35, 40, 50), stroke=0, kind="bold", shadow=False)
        bubble(cnv, C["friend"], lang, (35, 190, 505, 350), "left", bg=(255, 255, 255),
               fg=(30, 35, 43), outline=(181, 188, 200))
        if t > 0.36:
            d.ellipse((70, 430, 88, 448), fill=(130, 138, 150))
            d.ellipse((98, 430, 116, 448), fill=(130, 138, 150))
            d.ellipse((126, 430, 144, 448), fill=(130, 138, 150))
    elif t < 1.55:
        lt = t - 0.72
        burst(cnv, (270, 560), ((250, 197, 39), (251, 101, 49), (85, 37, 104)), 32, lt * 0.18)
        draw_hero(cnv, 270, 900, 460, lt)
        bubble(cnv, C["me"], lang, (65, 70, 475, 190), "right", bg=(37, 117, 244),
               fg=(255, 255, 255), outline=(37, 117, 244))
        text(cnv, f"{C['guns']}: 1", lang, (270, 795), 390, 56, 44, fill=(255, 244, 110), stroke=5)
    elif t < 4.45:
        lt = t - 1.55
        burst(cnv, (270, 590), ((255, 203, 36), (229, 52, 65), (48, 24, 75)), 34, lt * 0.12)
        draw_hero(cnv, 270, 900, 430, lt)
        count = min(5, 1 + int(lt / 0.48))
        for i, (name, h, x, y, rot) in enumerate(WEAPONS[:max(0, count - 1)]):
            q = ease_out((lt - i * 0.48) / 0.13)
            if q > 0:
                im = sprite(name, height=max(4, int(h * q)), rotate=rot)
                paste(cnv, im, x, y)
        text(cnv, f"{C['guns']}: {count}", lang, (270, 115), 430, 74, 58, fill=(255, 255, 255), stroke=7)
        if count >= 4:
            speed_lines(cnv, (270, 570), lt, (255, 238, 101, 155), 44)
    elif t < 5.25:
        lt = t - 4.45
        d.rectangle((0, 0, W, H), fill=(7, 8, 12))
        if lt < 0.38:
            text(cnv, C["one"], lang, (270, 430), 460, 130, 92, fill=(255, 255, 255), stroke=7)
        else:
            burst(cnv, (270, 480), ((245, 49, 63), (255, 207, 37), (22, 22, 30)), 38, lt)
            text(cnv, C["more"], lang, (270, 430), 480, 170, 132, fill=(255, 255, 255), stroke=9)
            speed_lines(cnv, (270, 480), lt, (255, 255, 255, 190), 60)
    elif t < 7.55:
        lt = t - 5.25
        burst(cnv, (270, 720), ((45, 23, 79), (123, 45, 172), (244, 79, 43)), 30, lt * 0.1)
        y = 900 - ease_in(lt / 2.3) * 880
        draw_hero(cnv, 270 + math.sin(lt * 15) * 15, y, 430, lt, rotate=math.sin(lt * 24) * 7)
        for name, h, x, yy, rot in WEAPONS:
            paste(cnv, sprite(name, height=h, rotate=rot), x, yy + y - 900)
        for k in range(3):
            muzzle(cnv, 475 - k * 145, y - 180 + k * 55, lt + k * 0.1, 150)
        speed_lines(cnv, (270, y), lt, (255, 221, 80, 180), 60)
        text(cnv, C["recoil"], lang, (270, 115), 450, 70, 55, fill=(255, 232, 55), stroke=7)
    elif t < 8.7:
        lt = t - 7.55
        d.rectangle((0, 0, W, H), fill=(111, 178, 213))
        d.rectangle((0, 700, W, H), fill=(88, 120, 64))
        for i in range(7):
            draw_zombie(cnv, 45 + i * 75, 900, 235, lt + i * 0.1, flip=i % 2 == 0, rotate=-10 if i < 3 else 10)
        text(cnv, C["look"], lang, (270, 140), 470, 74, 52, fill=(255, 255, 255), stroke=6)
        d.polygon([(255, 70), (285, 70), (270, 20)], fill=(255, 232, 76))
    elif t < 10.15:
        lt = t - 8.7
        d.rectangle((0, 0, W, H), fill=(15, 17, 29))
        # 대기권 재진입: 불타는 컴스톡 실루엣이 위에서 내려온다.
        for i in range(25):
            x = (i * 67 + int(lt * 380)) % 700 - 80
            d.line((x, 0, x - 230, 960), fill=(255, 255, 255, 70), width=3)
        y = -300 + ease_in(lt / 1.45) * 1120
        glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        gd = ImageDraw.Draw(glow)
        gd.ellipse((85, y - 290, 455, y + 120), fill=(255, 92, 26, 110))
        cnv = Image.alpha_composite(cnv, glow.filter(ImageFilter.GaussianBlur(35)))
        draw_hero(cnv, 270, y, 390, lt, rotate=180 + lt * 240, bob=False)
        text(cnv, "RETURNING..." if lang == "en" else "복귀 중...", lang, (270, 870), 430, 60, 44,
             fill=(255, 183, 58), stroke=5)
    else:
        lt = t - 10.15
        burst(cnv, (270, 640), ((255, 209, 43), (234, 49, 59), (43, 20, 68)), 34, lt * 0.1)
        explosion(cnv, 270, 590, lt, 0.0, 650)
        for i in range(8):
            x = 35 + i * 67
            draw_zombie(cnv, x, 920, 210, lt + i * 0.12, flip=i % 2 == 0)
        if lt > 0.35:
            draw_hero(cnv, 270, 900, 430, lt, rotate=math.sin(lt * 10) * 3)
        text(cnv, C["travel"], lang, (270, 180), 470, 180, 82, fill=(255, 255, 255), stroke=8)
        d.rounded_rectangle((110, 330, 430, 390), 14, fill=(17, 18, 24, 220), outline=(255, 220, 65), width=4)
        text(cnv, "+1" if lang == "en" else "+1", "en", (270, 360), 260, 42, 38,
             fill=(255, 220, 65), stroke=2, kind="bold")
    return cnv.convert("RGB")


def desk_scene(cnv: Image.Image, zombie_present: bool, hero_present: bool,
               t: float, smoke=False) -> None:
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle((0, 0, W, H), fill=(34, 45, 69))
    d.rectangle((0, 650, W, H), fill=(73, 47, 31))
    d.rectangle((35, 690, 505, 900), fill=(110, 65, 34), outline=(237, 181, 83), width=7)
    d.line((270, 690, 270, 900), fill=(237, 181, 83), width=4)
    d.rectangle((50, 70, 490, 490), fill=(50, 66, 96), outline=(149, 172, 205), width=5)
    d.line((270, 70, 270, 490), fill=(149, 172, 205), width=3)
    if zombie_present:
        draw_zombie(cnv, 135, 720, 430, t, flip=True)
    if hero_present:
        draw_hero(cnv, 405, 725, 390, t, flip=True)
    if smoke:
        for i in range(7):
            x = 95 + i * 22
            y = 560 - i * 35 + math.sin(t * 3 + i) * 20
            d.ellipse((x - 45, y - 55, x + 45, y + 55), fill=(180, 180, 185, 35))


def interview_frame(t: float, lang: str) -> Image.Image:
    C = COPY["interview"][lang]
    cnv = Image.new("RGBA", (W, H), (22, 25, 35, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 0.6:
        burst(cnv, (270, 480), ((255, 211, 42), (238, 57, 53), (36, 24, 61)), 36, t * 0.5)
        text(cnv, C["hook"], lang, (270, 410), 480, 260, 100, fill=(255, 255, 255), stroke=8)
        speed_lines(cnv, (270, 480), t, (255, 255, 255, 180), 55)
    elif t < 2.15:
        lt = t - 0.6
        desk_scene(cnv, True, True, lt)
        bubble(cnv, C["question"], lang, (40, 105, 500, 250), "left",
               bg=(255, 243, 197), fg=(35, 31, 28), outline=(35, 31, 28))
        text(cnv, "Q1", "en", (82, 305), 80, 50, 38, fill=(255, 217, 54), stroke=4)
    elif t < 3.15:
        lt = t - 2.15
        desk_scene(cnv, True, True, lt)
        bubble(cnv, C["answer"], lang, (130, 110, 500, 250), "right",
               bg=(55, 133, 248), fg=(255, 255, 255), outline=(55, 133, 248))
        text(cnv, "A1", "en", (455, 310), 80, 50, 38, fill=(95, 190, 255), stroke=4)
    elif t < 3.82:
        lt = t - 3.15
        # 좀비 얼굴 점프 줌.
        d.rectangle((0, 0, W, H), fill=(46, 59, 83))
        q = ease_out(lt / 0.2)
        draw_zombie(cnv, 270, 940, max(20, int(820 * q)), lt, flip=True)
        bubble(cnv, C["what"], lang, (72, 80, 468, 220), "left",
               bg=(255, 243, 197), fg=(35, 31, 28), outline=(35, 31, 28))
    elif t < 4.35:
        lt = t - 3.82
        burst(cnv, (270, 520), ((48, 119, 245), (25, 30, 46), (255, 214, 55)), 34, lt)
        draw_hero(cnv, 270, 930, 720, lt, flip=True, bob=False)
        text(cnv, C["yes"], lang, (270, 150), 440, 120, 96, fill=(255, 226, 55), stroke=8)
    elif t < 8.25:
        lt = t - 4.35
        burst(cnv, (270, 620), ((52, 21, 83), (162, 42, 177), (245, 78, 42)), 32, lt * 0.12)
        for i in range(9):
            draw_zombie(cnv, 30 + i * 61, 920, 210, lt + i * 0.12, flip=i % 2 == 0)
        draw_hero(cnv, 285, 900, 455, lt, rotate=math.sin(lt * 22) * 4)
        muzzle(cnv, 505, 580 + math.sin(lt * 22) * 55, lt, 190)
        speed_lines(cnv, (270, 620), lt, (255, 225, 75, 160), 58)
        for i, (x, y, at) in enumerate(((85, 680, 0.05), (445, 720, 0.5), (140, 815, 0.95),
                                        (410, 585, 1.4), (245, 730, 1.85), (475, 850, 2.3),
                                        (75, 560, 2.8), (350, 820, 3.25))):
            explosion(cnv, x, y, lt, at, 210 + (i % 2) * 50)
        flash = int(lt / 0.36) % 2 == 0
        text(cnv, C["auto"], lang, (270, 115), 450, 90, 72,
             fill=(255, 229, 55) if flash else (255, 255, 255), stroke=8)
    elif t < 9.35:
        lt = t - 8.25
        desk_scene(cnv, False, True, lt, smoke=True)
        # 면접관 자리에 연기와 넥타이만 남는다.
        d.polygon([(118, 520), (150, 520), (163, 610), (134, 650), (105, 610)],
                  fill=(179, 25, 40), outline=(50, 18, 22))
        text(cnv, "...", "en", (270, 135), 300, 90, 80, fill=(255, 255, 255), stroke=5)
    elif t < 10.55:
        lt = t - 9.35
        d.rectangle((0, 0, W, H), fill=(232, 222, 197))
        d.rounded_rectangle((45, 70, 495, 880), 18, fill=(252, 248, 236), outline=(48, 45, 41), width=5)
        text(cnv, "RESUME" if lang == "en" else "이력서", lang, (270, 145), 360, 70, 48,
             fill=(44, 43, 41), stroke=0, kind="bold", shadow=False)
        for y in (250, 320, 390, 460, 530):
            d.line((105, y, 435, y), fill=(130, 126, 117), width=4)
        stamp = Image.new("RGBA", (420, 180), (0, 0, 0, 0))
        sd = ImageDraw.Draw(stamp)
        sd.rounded_rectangle((8, 8, 412, 172), 20, outline=(210, 29, 45), width=12)
        text(stamp, C["hired"], lang, (210, 90), 360, 120, 92, fill=(210, 29, 45), stroke=0, shadow=False)
        stamp = stamp.rotate(-9 + math.sin(lt * 10) * 2, BILINEAR, expand=True)
        paste(cnv, stamp, 270, 670)
    else:
        lt = t - 10.55
        burst(cnv, (270, 540), ((255, 211, 44), (236, 54, 60), (43, 23, 70)), 34, lt * 0.08)
        draw_hero(cnv, 270, 920, 470, lt)
        text(cnv, C["ending"], lang, (270, 200), 470, 210, 78, fill=(255, 255, 255), stroke=8)
        d.rounded_rectangle((95, 380, 445, 450), 18, fill=(18, 19, 25, 230), outline=(255, 219, 62), width=4)
        text(cnv, C["hired"], lang, (270, 415), 310, 48, 38,
             fill=(255, 219, 62), stroke=2, kind="bold")
    return cnv.convert("RGB")


def endcard_frame(t: float, lang: str) -> Image.Image:
    L = LANG[lang]
    cnv = Image.new("RGBA", (W, H), (22, 7, 35, 255))
    burst(cnv, (270, 490), ((87, 27, 130), (166, 58, 207), (24, 8, 40)), 32, 0.0)
    speed_lines(cnv, (270, 490), t, (255, 255, 255, 55), 36)
    src = Image.open(ENDCARD).convert("RGBA")
    logo = src.crop((289, 318, 633, 432)).resize((430, 143), LANCZOS)
    q = ease_out(t / 0.2)
    if q < 1:
        logo = logo.resize((max(2, int(logo.width * q)), max(2, int(logo.height * q))), LANCZOS)
    paste(cnv, logo, 270, 545)
    d = ImageDraw.Draw(cnv)
    text(cnv, L["price"], lang, (270, 160), 480, 60, 35, fill=(240, 240, 240), stroke=4)
    f = fit(L["price"], lang, 480, 60, 35, "impact", 4)
    bb = d.textbbox((0, 0), L["price"], font=f, stroke_width=4)
    tw = bb[2] - bb[0]
    d.line((270 - tw / 2 - 8, 170, 270 + tw / 2 + 8, 151), fill=(53, 10, 17), width=10)
    d.line((270 - tw / 2 - 8, 168, 270 + tw / 2 + 8, 149), fill=(231, 65, 29), width=5)
    pulse = 1 + 0.035 * math.sin(t * 10)
    text(cnv, L["free"], lang, (270, 350), 430, 160, int(125 * pulse),
         fill=(100, 226, 113), stroke_fill=(8, 30, 14), stroke=10)
    text(cnv, L["cta"], lang, (270, 735), 490, 88, 48, fill=(255, 201, 38), stroke=6)
    text(cnv, "pyramid-studio.itch.io/comstock", "en", (270, 815), 480, 50, 27,
         fill=(255, 201, 38), stroke=4, kind="bold")
    return cnv.convert("RGB")


CUTS = {
    "delivery": (0.58, 2.15, 3.35, 4.5, 5.25, 8.9, 10.35, END_AT),
    "one_more": (0.72, 1.55, 4.45, 5.25, 7.55, 8.7, 10.15, END_AT),
    "interview": (0.6, 2.15, 3.15, 3.82, 4.35, 8.25, 9.35, 10.55, END_AT),
}


def raw_frame(concept: str, lang: str, t: float) -> Image.Image:
    if t >= END_AT:
        return endcard_frame(t - END_AT, lang)
    if concept == "delivery":
        return delivery_frame(t, lang)
    if concept == "one_more":
        return one_more_frame(t, lang)
    return interview_frame(t, lang)


def post_process(im: Image.Image, concept: str, t: float, frame: int) -> Image.Image:
    # 숏츠 특유의 펀치 컷: 컷마다 2~3프레임 플래시 + 액션 구간의 정수 픽셀 흔들림.
    fl = 0.0
    for cut in CUTS[concept]:
        fl += math.exp(-((t - cut) / 0.035) ** 2)
    if fl > 0.02:
        im = Image.blend(im, Image.new("RGB", im.size, (255, 255, 255)), min(0.80, fl * 0.72))
    action = ((concept == "delivery" and 5.25 < t < 8.9) or
              (concept == "one_more" and 5.25 < t < 7.55) or
              (concept == "interview" and 4.35 < t < 8.25))
    if action:
        rng = random.Random(frame * 7919 + 55)
        im = ImageChops.offset(im, rng.randint(-7, 7), rng.randint(-5, 5))
    # 매 0.5초 한 번 1.5% 펀치 줌. 자막과 피사체가 함께 튀어 숏폼 리듬이 난다.
    phase = (t * 2) % 1
    z = 1.0 + 0.018 * math.exp(-phase * 7)
    if z > 1.002:
        nw, nh = int(W * z), int(H * z)
        big = im.resize((nw, nh), BILINEAR)
        im = big.crop(((nw - W) // 2, (nh - H) // 2, (nw - W) // 2 + W, (nh - H) // 2 + H))
    return ImageEnhance.Color(im).enhance(1.12)


def frame_at(concept: str, lang: str, t: float) -> Image.Image:
    return post_process(raw_frame(concept, lang, t), concept, t, int(t * FPS))


def render_silent(concept: str, lang: str, out: Path) -> None:
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
           "-an", "-vf", f"scale={OUT_W}:{OUT_H}:flags=lanczos,format=yuv420p",
           "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p",
           "-r", str(FPS), "-t", str(DUR), "-movflags", "+faststart", str(out)]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    assert p.stdin is not None
    for f in range(FRAMES):
        p.stdin.write(frame_at(concept, lang, f / FPS).tobytes())
        if f % 120 == 0:
            print(f"  {concept}/{lang} {f:03d}/{FRAMES}", flush=True)
    p.stdin.close()
    if p.wait():
        raise RuntimeError(f"비디오 인코딩 실패: {concept}/{lang}")


def build_bed(concept: str, path: Path) -> None:
    sr, n = 48000, int(48000 * DUR)
    out = array.array("h", [0]) * n
    seed = {"delivery": 441, "one_more": 442, "interview": 443}[concept]
    rng = random.Random(seed)
    bpm = {"delivery": 148, "one_more": 156, "interview": 152}[concept]
    beat = 60 / bpm
    for i in range(n):
        t = i / sr
        ph = (t / beat) % 1
        kick = math.exp(-ph * 16) * math.sin(math.tau * (58 - 22 * ph) * t)
        clap_ph = ((t / beat) + 0.5) % 1
        clap = math.exp(-clap_ph * 28) * rng.uniform(-1, 1)
        note_idx = int(t / beat) % 4
        base = (110, 130.8, 146.8, 98)[note_idx]
        bass = math.sin(math.tau * base * t) * (0.5 + 0.5 * math.exp(-ph * 4))
        v = 0.15 * kick + 0.055 * clap + 0.055 * bass
        if concept == "delivery" and t < 0.58:
            v += 0.11 * math.sin(math.tau * 880 * t) * math.exp(-((t - 0.18) / 0.16) ** 2)
        if concept == "interview" and 0.6 < t < 4.35:
            v += 0.035 * math.sin(math.tau * 3.2 * t) * math.sin(math.tau * 220 * t)
        out[i] = max(-32768, min(32767, int(v * 32767)))
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1); wf.setsampwidth(2); wf.setframerate(sr); wf.writeframes(out.tobytes())


SFX = {
    "delivery": [
        ("UI_Click.wav", 0.08, 0.9), ("UI_Click.wav", 2.15, 0.7),
        ("UI_Click.wav", 3.35, 0.8), ("Weapon_Explosive.wav", 4.50, 1.0),
        ("Weapon_RapidFire.wav", 5.25, 0.45), ("Weapon_Explosive.wav", 5.70, 0.75),
        ("Enemy_Death.wav", 7.10, 0.6), ("LevelUp.wav", 8.90, 0.8),
        ("UI_Click.wav", 10.35, 0.8), ("LevelUp.wav", 12.60, 0.8),
    ],
    "one_more": [
        ("UI_Click.wav", 0.20, 0.8), ("LevelUp.wav", 0.72, 0.55),
        ("UI_Click.wav", 1.55, 0.8), ("UI_Click.wav", 2.03, 0.8),
        ("UI_Click.wav", 2.51, 0.8), ("UI_Click.wav", 2.99, 0.8),
        ("Weapon_Explosive.wav", 4.45, 0.9), ("Weapon_RapidFire.wav", 5.25, 0.52),
        ("Weapon_Explosive.wav", 10.15, 1.0), ("Enemy_Death.wav", 10.55, 0.65),
        ("LevelUp.wav", 12.60, 0.8),
    ],
    "interview": [
        ("Weapon_Explosive.wav", 0.02, 0.75), ("UI_Click.wav", 0.60, 0.8),
        ("UI_Click.wav", 2.15, 0.8), ("UI_Click.wav", 3.15, 0.8),
        ("Weapon_Explosive.wav", 3.82, 0.9), ("Weapon_RapidFire.wav", 4.35, 0.52),
        ("Enemy_Death.wav", 6.15, 0.65), ("UI_Click.wav", 9.35, 0.9),
        ("LevelUp.wav", 10.55, 0.75), ("LevelUp.wav", 12.60, 0.8),
    ],
}


def mix_audio(silent: Path, bed: Path, concept: str, out: Path) -> None:
    schedule = SFX[concept]
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error", "-i", str(silent), "-i", str(bed)]
    for name, _, _ in schedule:
        cmd += ["-i", str(RES / "SFX" / name)]
    filters = ["[1:a]volume=1.15[bed]"]
    labels = ["[bed]"]
    for idx, (_, at, gain) in enumerate(schedule, start=2):
        lab = f"s{idx}"
        delay = int(at * 1000)
        filters.append(f"[{idx}:a]volume={gain},adelay={delay}|{delay}[{lab}]")
        labels.append(f"[{lab}]")
    filters.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0:duration=longest,"
                   "acompressor=threshold=0.13:ratio=4:attack=5:release=120:makeup=1.6,"
                   "alimiter=limit=0.94,atrim=0:15,afade=t=out:st=14.82:d=0.18[aout]")
    cmd += ["-filter_complex", ";".join(filters), "-map", "0:v:0", "-map", "[aout]",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
            "-t", "15", "-movflags", "+faststart", str(out)]
    subprocess.run(cmd, check=True)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def probe(path: Path) -> dict:
    p = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path)], text=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    dm = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", p.stderr)
    vm = re.search(r"Video: h264.*?, yuv420p.*?, (\d+)x(\d+).*?, (\d+(?:\.\d+)?) fps", p.stderr)
    if not dm or not vm:
        raise RuntimeError(f"ffprobe 실패: {path}\n{p.stderr[-1500:]}")
    dur = int(dm.group(1)) * 3600 + int(dm.group(2)) * 60 + float(dm.group(3))
    return {"duration": dur, "width": int(vm.group(1)), "height": int(vm.group(2)),
            "fps": float(vm.group(3)), "aac_audio": "Audio: aac" in p.stderr}


SHEET_TIMES = [0.35, 1.25, 2.35, 3.45, 4.55, 5.65, 6.75, 7.85, 9.0, 10.2, 11.5, 13.5]


def decoded_frame(path: Path, t: float) -> Image.Image:
    p = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-ss", str(t),
                        "-i", str(path), "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "-"],
                       stdout=subprocess.PIPE, check=True)
    return Image.open(io.BytesIO(p.stdout)).convert("RGB")


def make_sheet(concept: str) -> Path:
    tw, th = 270, 480
    cols, rows = 6, 4
    sheet = Image.new("RGB", (cols * tw, rows * th + 30), (13, 13, 17))
    d = ImageDraw.Draw(sheet)
    for li, lang in enumerate(("en", "ko")):
        video = HERE / f"Comstock_ShortsV2_{concept.title()}_{lang.upper()}_15s.mp4"
        for i, t in enumerate(SHEET_TIMES):
            im = decoded_frame(video, t) if video.exists() else frame_at(concept, lang, t)
            im = im.resize((tw, th), LANCZOS)
            row = li * 2 + i // cols
            col = i % cols
            sheet.paste(im, (col * tw, 30 + row * th))
        d.text((8, 5 + li * 2 * th), f"{concept.upper()} — {lang.upper()}",
               font=font("en", "bold", 18), fill=(255, 255, 255))
    out = HERE / f"contact-sheet-{concept}.jpg"
    sheet.save(out, quality=91)
    return out


def render_one(concept: str, lang: str) -> dict:
    stem = f"Comstock_ShortsV2_{concept.title()}_{lang.upper()}_15s"
    silent = HERE / f"_{stem}_silent.mp4"
    bed = HERE / f"_bed_{concept}.wav"
    out = HERE / f"{stem}.mp4"
    if not bed.exists():
        build_bed(concept, bed)
    render_silent(concept, lang, silent)
    mix_audio(silent, bed, concept, out)
    silent.unlink(missing_ok=True)
    info = probe(out)
    if abs(info["duration"] - DUR) > 0.05 or (info["width"], info["height"]) != (OUT_W, OUT_H) or not info["aac_audio"]:
        raise RuntimeError(f"출력 규격 오류: {out}: {info}")
    return {"concept": concept, "language": lang, "output": out.name,
            "sha256": sha256(out), "probe": info}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--concept", choices=("delivery", "one_more", "interview", "all"), default="all")
    ap.add_argument("--lang", choices=("en", "ko", "all"), default="all")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    concepts = ("delivery", "one_more", "interview") if args.concept == "all" else (args.concept,)
    langs = ("en", "ko") if args.lang == "all" else (args.lang,)
    if not HERO.exists() or not ENDCARD.exists():
        raise FileNotFoundError("comstock_hero.png 또는 endcard_source.png가 없습니다")
    if args.preview:
        for c in concepts:
            print(make_sheet(c))
        return
    exports = []
    for c in concepts:
        for lang in langs:
            print(f"render: {c}/{lang}")
            exports.append(render_one(c, lang))
        make_sheet(c)
    manifest = {
        "title": "컴스톡 세로 숏츠 V2 3종 × 한/영",
        "format": {"size": [OUT_W, OUT_H], "aspect": "9:16", "fps": FPS,
                   "duration_seconds": DUR, "video": "H.264/yuv420p", "audio": "AAC 48kHz stereo"},
        "concepts": {
            "delivery": "좀비 배달기사 도어벨 POV",
            "one_more": "무기 하나면 충분하다는 친구와 카운터 폭증/반동 비행",
            "interview": "1초 좀비 면접과 자동사격 답변",
        },
        "editing": "0.4~1.2초 상태 변화, 0.5초 펀치 줌, 컷 플래시, 액션 흔들림, 한 화면 한 문장",
        "required_copy": {"ko": LANG["ko"]["cta"], "en": LANG["en"]["cta"]},
        "endcard": {"start_seconds": END_AT, "source": ENDCARD.name, "sha256": sha256(ENDCARD)},
        "sources": ["dev/pv/assets/comstock_hero.png", "Assets/Resources/ZombieMove/*.png",
                    "Assets/Resources/Explosion/*.png", "Assets/Resources/MuzzleFlash/*.png",
                    "Assets/Resources/Right*.png", "Assets/Resources/SFX/*.wav"],
        "narration": "없음 — 무음 자동재생을 전제로 큰 자막 + 비트 + 게임 SFX",
        "exports": exports,
        "contact_sheets": [f"contact-sheet-{c}.jpg" for c in concepts],
        "generator": Path(__file__).name,
    }
    (HERE / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print("complete")


if __name__ == "__main__":
    main()
