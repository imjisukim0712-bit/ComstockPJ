# -*- coding: utf-8 -*-
"""북미풍 병맛 컴스톡 광고 3종 × 한국어/영어 렌더러.

기존 PV 장면은 재사용하지 않고 게임 스프라이트만 읽어 15초짜리 독립 광고를 만든다.
출력: 1280x720 / 30fps / H.264 + AAC / 정확히 15초.
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
import shutil
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
RES = ROOT / "Assets" / "Resources"
HERO_PATH = ROOT / "dev" / "pv" / "assets" / "comstock_hero.png"
ENDCARD_SOURCE = HERE / "endcard_source.png"
VENDOR = ROOT / "dev" / "pv" / "_vendor"
sys.path.insert(0, str(VENDOR))

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont
import imageio_ffmpeg

W, H = 640, 360
OUT_W, OUT_H = 1280, 720
FPS, DUR = 30, 15.0
FRAMES = int(FPS * DUR)
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
LANCZOS = Image.Resampling.LANCZOS
BILINEAR = Image.Resampling.BILINEAR

FONT_IMPACT = Path(r"C:\Windows\Fonts\impact.ttf")
FONT_ARIAL_BOLD = Path(r"C:\Windows\Fonts\arialbd.ttf")
FONT_ARIAL = Path(r"C:\Windows\Fonts\arial.ttf")
FONT_KO_BOLD = Path(r"C:\Windows\Fonts\malgunbd.ttf")
FONT_KO = Path(r"C:\Windows\Fonts\malgun.ttf")

LANG = {
    "en": {
        "voice": "Microsoft Zira Desktop",
        "rate": 4,
        "cta_rate": 3,
        "cta": "Play it before the zombies do.",
        "cta_screen": "PLAY IT BEFORE THE ZOMBIES DO.",
        "price": "REGULAR PRICE $59.99? NOPE.",
        "free": "FREE",
        "weather_vo": "Tonight's forecast: zombies. One hundred percent chance of bad decisions. Then Comstock moved in. Forecast: clear.",
        "lawyer_vo": "Have you or a loved one been mildly eaten? You may be entitled to excessive firepower. Call Comstock. Case dismissed.",
        "pharma_vo": "Feeling under-armed? Ask your mechanic about Comstock. Side effects may include sparks, confidence, and fewer neighbors who are zombies. Results may vary. Zombies may not.",
    },
    "ko": {
        "voice": "Microsoft Heami Desktop",
        "rate": 3,
        "cta_rate": 2,
        "cta": "좀비보다 먼저 플레이하세요.",
        "cta_screen": "좀비보다 먼저 플레이하세요.",
        "price": "정가 59,990원? 아니죠.",
        "free": "무료",
        "weather_vo": "오늘의 예보는 좀비입니다. 나쁜 판단 확률 백 퍼센트. 그런데 컴스톡이 이사 왔습니다. 예보, 맑음.",
        "lawyer_vo": "본인 또는 가족이 좀 살짝 먹혔습니까? 과도한 화력 보상을 받을 수 있습니다. 컴스톡을 부르세요. 사건 종결.",
        "pharma_vo": "화력이 부족하다고 느끼십니까? 정비사에게 컴스톡을 물어보세요. 부작용으로는 불꽃, 자신감, 좀비 이웃 감소가 있을 수 있습니다. 결과는 다를 수 있습니다. 좀비는 아닐 수 있습니다.",
    },
}

COPY = {
    "weather": {
        "en": {
            "hook": "ZOMBIE WEATHER ALERT",
            "forecast": "TONIGHT: ZOMBIES",
            "chance": "100% CHANCE OF BAD DECISIONS",
            "lower": "STAY INDOORS. OR DON'T.",
            "ticker": "LOCAL NEWS 8  •  THIS IS FINE  •  TRAFFIC: ALSO ZOMBIES",
            "clear": "FORECAST: CLEAR",
            "moved": "THE ROBOT MOVED IN.",
        },
        "ko": {
            "hook": "좀비 기상 특보",
            "forecast": "오늘 밤: 좀비",
            "chance": "나쁜 판단 확률 100%",
            "lower": "실내에 계세요. 아니면 말고요.",
            "ticker": "지역 뉴스 8  •  괜찮습니다  •  교통 상황: 좀비",
            "clear": "예보: 맑음",
            "moved": "로봇이 이사 왔습니다.",
        },
    },
    "lawyer": {
        "en": {
            "hook": "BITTEN? CALL NOW!",
            "question": "HAVE YOU BEEN MILDLY EATEN?",
            "award": "YOU MAY BE ENTITLED TO\nEXCESSIVE FIREPOWER.",
            "call": "CALL COMSTOCK",
            "case": "CASE DISMISSED.",
            "fine": "NOT A REAL LAW FIRM. VERY REAL MINIGUN.",
        },
        "ko": {
            "hook": "물렸습니까? 지금 전화!",
            "question": "좀 살짝 먹히셨습니까?",
            "award": "과도한 화력 보상을\n받을 수 있습니다.",
            "call": "컴스톡을 부르세요",
            "case": "사건 종결.",
            "fine": "실제 법률 사무소가 아닙니다. 미니건은 진짜입니다.",
        },
    },
    "pharma": {
        "en": {
            "hook": "FEELING UNDER-ARMED?",
            "ask": "ASK YOUR MECHANIC ABOUT COMSTOCK.",
            "side": "SIDE EFFECTS MAY INCLUDE:",
            "effects": "SPARKS  •  CONFIDENCE  •  FEWER ZOMBIE NEIGHBORS",
            "result": "RESULTS MAY VARY.\nZOMBIES MAY NOT.",
            "fine": "DO NOT OPERATE NEAR UNPROTECTED LAWNS.",
        },
        "ko": {
            "hook": "화력이 부족하십니까?",
            "ask": "정비사에게 컴스톡을 물어보세요.",
            "side": "가능한 부작용:",
            "effects": "불꽃  •  자신감  •  좀비 이웃 감소",
            "result": "결과는 다를 수 있습니다.\n좀비는 아닐 수 있습니다.",
            "fine": "보호되지 않은 잔디 근처에서 사용하지 마십시오.",
        },
    },
}

PALETTES = {
    "weather": ((10, 35, 79), (214, 35, 45), (255, 202, 41), (241, 245, 250)),
    "lawyer": ((24, 20, 16), (235, 178, 22), (180, 22, 28), (255, 246, 214)),
    "pharma": ((153, 213, 226), (97, 174, 118), (248, 177, 190), (255, 252, 236)),
}

_fonts: dict[tuple[str, str, int], ImageFont.FreeTypeFont] = {}
_images: dict[str, Image.Image] = {}


def clamp(v: float, lo: float = 0.0, hi: float = 1.0) -> float:
    return max(lo, min(hi, v))


def ease_out(v: float) -> float:
    v = clamp(v)
    return 1.0 - (1.0 - v) ** 3


def font(lang: str, kind: str, size: int) -> ImageFont.FreeTypeFont:
    key = (lang, kind, size)
    if key not in _fonts:
        if lang == "ko":
            path = FONT_KO_BOLD if kind in ("impact", "bold") else FONT_KO
        else:
            path = FONT_IMPACT if kind == "impact" else (FONT_ARIAL_BOLD if kind == "bold" else FONT_ARIAL)
        _fonts[key] = ImageFont.truetype(str(path), size)
    return _fonts[key]


def load_image(path: Path, height: int | None = None, width: int | None = None,
               flip: bool = False, rotate: float = 0.0) -> Image.Image:
    key = f"{path}|{height}|{width}|{flip}|{rotate:.2f}"
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
    return load_image(RES / rel, **kwargs)


def paste(dst: Image.Image, src: Image.Image, x: float, y: float, anchor: str = "cc",
          alpha: float = 1.0) -> None:
    px = int(x - src.width / 2) if anchor[0] == "c" else (int(x) if anchor[0] == "l" else int(x - src.width))
    py = int(y - src.height / 2) if anchor[1] == "c" else (int(y) if anchor[1] == "t" else int(y - src.height))
    if alpha < 1:
        src = src.copy()
        src.putalpha(src.getchannel("A").point(lambda a: int(a * alpha)))
    dst.alpha_composite(src, (px, py))


def fit_font(text: str, lang: str, max_w: int, max_h: int, max_size: int,
             kind: str = "impact", stroke: int = 0) -> ImageFont.FreeTypeFont:
    lines = text.splitlines() or [text]
    for size in range(max_size, 9, -1):
        f = font(lang, kind, size)
        boxes = [f.getbbox(line, stroke_width=stroke) for line in lines]
        w = max((b[2] - b[0] for b in boxes), default=0)
        h = sum((b[3] - b[1] for b in boxes)) + max(0, len(lines) - 1) * int(size * 0.18)
        if w <= max_w and h <= max_h:
            return f
    return font(lang, kind, 10)


def title(cnv: Image.Image, text: str, lang: str, xy: tuple[float, float], max_w: int,
          max_h: int, max_size: int, fill=(255, 255, 255), stroke_fill=(0, 0, 0),
          stroke=3, kind="impact", anchor="mm", shadow=True, spacing=2) -> None:
    d = ImageDraw.Draw(cnv)
    f = fit_font(text, lang, max_w, max_h, max_size, kind, stroke)
    x, y = xy
    if shadow:
        d.multiline_text((x + 3, y + 4), text, font=f, fill=(0, 0, 0, 170), anchor=anchor,
                         align="center", spacing=spacing, stroke_width=stroke + 1,
                         stroke_fill=(0, 0, 0, 120))
    d.multiline_text((x, y), text, font=f, fill=fill, anchor=anchor, align="center",
                     spacing=spacing, stroke_width=stroke, stroke_fill=stroke_fill)


def burst(cnv: Image.Image, center: tuple[float, float], colors, rays=24, phase=0.0) -> None:
    d = ImageDraw.Draw(cnv)
    cx, cy = center
    radius = 1100
    for i in range(rays):
        a0 = math.tau * i / rays + phase
        a1 = math.tau * (i + 1) / rays + phase
        d.polygon([(cx, cy), (cx + math.cos(a0) * radius, cy + math.sin(a0) * radius),
                   (cx + math.cos(a1) * radius, cy + math.sin(a1) * radius)],
                  fill=colors[i % len(colors)])


def scanlines(cnv: Image.Image, alpha=22) -> None:
    d = ImageDraw.Draw(cnv, "RGBA")
    for y in range(0, H, 3):
        d.line((0, y, W, y), fill=(0, 0, 0, alpha))


def draw_zombie(cnv: Image.Image, x: float, y: float, h: int, t: float,
                flip=False, rotate=0.0, tie=False) -> None:
    idx = int(t * 9) % 8
    z = sprite(f"ZombieMove/walk_left_f{idx}.png", height=h, flip=flip, rotate=rotate)
    paste(cnv, z, x, y, "cb")
    if tie:
        d = ImageDraw.Draw(cnv)
        d.polygon([(x - 4, y - h * 0.54), (x + 5, y - h * 0.54),
                   (x + 12, y - h * 0.32), (x, y - h * 0.22),
                   (x - 10, y - h * 0.33)], fill=(173, 18, 28), outline=(30, 15, 15))


def draw_hero(cnv: Image.Image, x: float, y: float, h: int, t: float,
              flip=False, rotate=0.0, bob=True) -> None:
    hh = max(2, int(h * (1 + 0.025 * math.sin(t * 8))))
    im = load_image(HERO_PATH, height=hh, flip=flip, rotate=rotate)
    paste(cnv, im, x, y + (math.sin(t * 8) * 3 if bob else 0), "cb")


def explosion(cnv: Image.Image, x: float, y: float, t: float, start: float,
              h=130) -> None:
    if t < start or t > start + 0.7:
        return
    idx = min(10, max(1, int((t - start) / 0.7 * 10) + 1))
    im = sprite(f"Explosion/frame_{idx:02d}.png", height=h)
    paste(cnv, im, x, y)


def newsroom_ticker(cnv: Image.Image, text: str, lang: str, y=326, color=(190, 26, 34)) -> None:
    d = ImageDraw.Draw(cnv)
    d.rectangle((0, y, W, H), fill=color)
    title(cnv, text, lang, (W / 2, y + 17), W - 20, 28, 18, fill=(255, 255, 255),
          stroke=1, kind="bold", shadow=False)


def weather_frame(t: float, lang: str) -> Image.Image:
    C = COPY["weather"][lang]
    blue, red, yellow, white = PALETTES["weather"]
    cnv = Image.new("RGBA", (W, H), (*blue, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 1.2:
        pulse = int(t * 8) % 2
        cnv.paste(red if pulse else blue, (0, 0, W, H))
        for i in range(-3, 8):
            x = i * 110 + (t * 240) % 110
            d.polygon([(x, 0), (x + 45, 0), (x - 130, H), (x - 175, H)], fill=(255, 255, 255, 22))
        d.rectangle((18, 16, W - 18, H - 16), outline=yellow, width=8)
        title(cnv, C["hook"], lang, (W / 2, H / 2), W - 70, 160, 74,
              fill=yellow, stroke_fill=(0, 0, 0), stroke=6)
    elif t < 4.8:
        lt = t - 1.2
        # 저가 지역방송용 가짜 레이더 지도.
        d.rectangle((0, 0, W, H), fill=(18, 82, 122))
        for x in range(0, W, 64):
            d.line((x, 0, x, H), fill=(255, 255, 255, 25), width=1)
        for y in range(0, H, 48):
            d.line((0, y, W, y), fill=(255, 255, 255, 25), width=1)
        land = [(55, 78), (170, 49), (270, 78), (350, 61), (466, 102), (514, 175),
                (463, 223), (338, 237), (251, 213), (152, 245), (74, 191)]
        d.polygon(land, fill=(80, 130, 92), outline=(210, 232, 187), width=3)
        rng = random.Random(84)
        for i in range(17):
            x, y = rng.randint(90, 480), rng.randint(88, 225)
            rr = 7 + int(5 * math.sin(lt * 3 + i))
            d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=(red[0], red[1], red[2], 145))
            draw_zombie(cnv, x, y + 18, 38, lt + i * 0.1, flip=i % 2 == 0)
        # 좀비 기상 캐스터와 지휘봉.
        draw_zombie(cnv, 565, 310, 230, lt, flip=True, tie=True)
        d.line((519, 220, 414, 142), fill=yellow, width=5)
        d.ellipse((407, 135, 421, 149), fill=yellow)
        d.rectangle((0, 0, W, 54), fill=(6, 28, 64, 235))
        title(cnv, C["forecast"], lang, (W / 2, 27), W - 50, 42, 32, fill=white, stroke=2)
        title(cnv, C["chance"], lang, (310, 287), 500, 38, 27, fill=yellow, stroke=3)
        newsroom_ticker(cnv, C["ticker"], lang)
    elif t < 8.0:
        lt = t - 4.8
        # 투박한 앵커 데스크와 두 명의 좀비 앵커.
        d.rectangle((0, 0, W, H), fill=(20, 46, 85))
        d.rectangle((22, 28, 618, 232), fill=(41, 92, 135), outline=white, width=3)
        for i, x in enumerate((210, 430)):
            draw_zombie(cnv, x, 252, 190, lt + i * 0.4, flip=bool(i), tie=True)
        d.polygon([(70, 230), (570, 230), (620, 331), (20, 331)], fill=(125, 78, 45), outline=(246, 205, 125))
        d.rectangle((242, 242, 398, 302), fill=(8, 27, 55), outline=yellow, width=3)
        title(cnv, "LOCAL NEWS 8" if lang == "en" else "지역 뉴스 8", lang,
              (320, 272), 145, 45, 25, fill=yellow, stroke=2)
        d.rectangle((35, 22, 605, 74), fill=red)
        title(cnv, C["lower"], lang, (320, 48), 540, 40, 30, fill=white, stroke=2)
        newsroom_ticker(cnv, C["ticker"], lang)
    else:
        lt = t - 8.0
        # 로봇이 그린스크린을 뚫고 들어와 예보를 맑음으로 바꾼다.
        d.rectangle((0, 0, W, H), fill=(75, 174, 202))
        d.ellipse((60, 45, 155, 140), fill=yellow)
        for x in range(0, W, 74):
            d.polygon([(x, 282), (x + 38, 218), (x + 76, 282)], fill=(61, 132, 73))
        for i in range(7):
            x = 75 + i * 84
            draw_zombie(cnv, x, 300, 115, lt + i * 0.2, flip=i % 2 == 0)
        draw_hero(cnv, 345, 324, 235, lt, rotate=math.sin(lt * 7) * 2)
        for i, (x, y, at) in enumerate(((95, 236, 0.25), (520, 246, 0.8), (178, 270, 1.35), (455, 286, 2.0))):
            explosion(cnv, x, y, lt, at, 120 + i * 8)
        d.rounded_rectangle((104, 20, 536, 86), 12, fill=(255, 255, 255, 232), outline=blue, width=4)
        title(cnv, C["clear"], lang, (320, 52), 390, 48, 38, fill=blue, stroke=1)
        title(cnv, C["moved"], lang, (320, 111), 500, 34, 23, fill=white, stroke=3, kind="bold")
    scanlines(cnv, 16)
    return cnv.convert("RGB")


def lawyer_frame(t: float, lang: str) -> Image.Image:
    C = COPY["lawyer"][lang]
    ink, gold, red, cream = PALETTES["lawyer"]
    cnv = Image.new("RGBA", (W, H), (*ink, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 1.2:
        burst(cnv, (320, 195), (gold, cream, red), 30, t * 0.3)
        z = sprite("Enemy_zombie.png", height=325, rotate=-7 + math.sin(t * 8) * 2)
        paste(cnv, z, 320, 355, "cb")
        d.rectangle((0, 19, W, 94), fill=red)
        d.rectangle((0, 279, W, 350), fill=ink)
        title(cnv, C["hook"], lang, (320, 56), 600, 60, 54, fill=cream, stroke=5)
        title(cnv, "1-800-NOT-DEAD", "en", (320, 316), 570, 50, 42, fill=gold, stroke=4)
    elif t < 4.6:
        lt = t - 1.2
        # 값싼 변호사 광고 세트: 목재 벽, 책장, 과도하게 큰 명패.
        d.rectangle((0, 0, W, H), fill=(102, 57, 30))
        for x in range(0, W, 34):
            d.line((x, 0, x + 14, H), fill=(70, 36, 20), width=2)
        d.rectangle((28, 30, 188, 270), fill=(45, 27, 19), outline=gold, width=3)
        for y in range(55, 250, 38):
            d.rectangle((42, y, 174, y + 22), fill=(128, 37 + y % 50, 31))
        draw_zombie(cnv, 468, 322, 285, lt, flip=True, tie=True)
        d.rectangle((205, 235, 623, 338), fill=(61, 32, 20), outline=gold, width=4)
        d.rounded_rectangle((218, 244, 418, 310), 8, fill=gold, outline=ink, width=3)
        title(cnv, "COMSTOCK & COMSTOCK", "en", (318, 277), 185, 48, 22, fill=ink, stroke=0, shadow=False)
        d.rectangle((18, 16, 622, 84), fill=(15, 12, 10, 235), outline=gold, width=4)
        title(cnv, C["question"], lang, (320, 50), 570, 54, 38, fill=cream, stroke=3)
        title(cnv, C["fine"], lang, (320, 345), 590, 20, 14, fill=cream, stroke=1, kind="bold")
    elif t < 8.0:
        lt = t - 4.6
        burst(cnv, (320, 190), ((255, 246, 214), gold, (245, 219, 123)), 26, math.sin(lt) * 0.03)
        # 과도한 화력 보상 수표와 컴스톡 변호사.
        d.rounded_rectangle((30, 43, 610, 261), 14, fill=cream, outline=ink, width=5)
        d.line((55, 103, 585, 103), fill=(93, 107, 90), width=2)
        title(cnv, C["award"], lang, (320, 151), 520, 108, 42, fill=ink, stroke=0, shadow=False)
        d.rectangle((48, 215, 592, 248), fill=(236, 225, 190))
        title(cnv, C["call"], lang, (320, 231), 500, 26, 21, fill=red, stroke=0, kind="bold", shadow=False)
        draw_hero(cnv, 320, 370, 165, lt, bob=True)
        d.ellipse((500, 270, 620, 390), fill=(red[0], red[1], red[2], 225), outline=cream, width=5)
        title(cnv, "APPROVED" if lang == "en" else "승인", lang, (560, 330), 100, 55, 28, fill=cream, stroke=2)
    else:
        lt = t - 8.0
        # 법정 스케치처럼 누런 종이 위에서 좀비 증거가 폭발한다.
        d.rectangle((0, 0, W, H), fill=(224, 205, 160))
        for i in range(18):
            y = i * 23
            d.line((0, y, W, y + random.Random(i).randint(-3, 3)), fill=(110, 86, 55, 45), width=1)
        for i in range(6):
            draw_zombie(cnv, 72 + i * 102, 305, 145, lt + i * 0.13, flip=i % 2 == 0)
        draw_hero(cnv, 330, 337, 210, lt, rotate=math.sin(lt * 6) * 3)
        for i, (x, y, at) in enumerate(((105, 245, 0.2), (515, 250, 0.7), (200, 280, 1.25), (440, 285, 1.85))):
            explosion(cnv, x, y, lt, at, 135)
        # 회전된 스탬프는 별도 레이어로 합성.
        stamp = Image.new("RGBA", (520, 118), (0, 0, 0, 0))
        sd = ImageDraw.Draw(stamp)
        sd.rounded_rectangle((6, 6, 514, 112), 10, outline=red, width=9)
        title(stamp, C["case"], lang, (260, 59), 470, 82, 60, fill=red, stroke=0, shadow=False)
        stamp = stamp.rotate(-7 + math.sin(lt * 5), BILINEAR, expand=True)
        paste(cnv, stamp, 320, 102)
        title(cnv, C["fine"], lang, (320, 345), 610, 22, 14, fill=ink, stroke=1, kind="bold")
    # 아날로그 저가 방송 색 번짐.
    rgb = cnv.convert("RGB")
    if int(t * FPS) % 6 == 0:
        r, g, b = rgb.split()
        rgb = Image.merge("RGB", (ImageChops.offset(r, 2, 0), g, ImageChops.offset(b, -2, 0)))
    return rgb


def meadow(cnv: Image.Image, t: float) -> None:
    d = ImageDraw.Draw(cnv)
    for y in range(H):
        q = y / H
        col = (int(150 + 48 * q), int(210 + 30 * q), int(226 + 20 * q))
        d.line((0, y, W, y), fill=col)
    d.ellipse((64, 34, 154, 124), fill=(255, 236, 140))
    d.rectangle((0, 236, W, H), fill=(111, 184, 118))
    d.ellipse((-70, 203, 300, 450), fill=(90, 162, 103))
    d.ellipse((230, 199, 720, 450), fill=(82, 155, 96))
    rng = random.Random(74)
    for i in range(65):
        x, y = rng.randrange(W), rng.randrange(246, H)
        col = ((255, 246, 170), (250, 170, 184), (239, 239, 255))[i % 3]
        d.ellipse((x - 2, y - 2, x + 3, y + 3), fill=col)


def pharma_frame(t: float, lang: str) -> Image.Image:
    C = COPY["pharma"][lang]
    sky, green, pink, cream = PALETTES["pharma"]
    cnv = Image.new("RGBA", (W, H), (*sky, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    meadow(cnv, t)
    if t < 1.4:
        # 과하게 평화로운 약 광고 오프닝 + 화력이 모자란 작은 물총.
        draw_zombie(cnv, 525, 320, 190, t, flip=True)
        draw_hero(cnv, 230, 334, 205, t, bob=True)
        d.rounded_rectangle((20, 22, 620, 98), 25, fill=(255, 252, 236, 235), outline=green, width=4)
        title(cnv, C["hook"], lang, (320, 60), 550, 55, 44, fill=(36, 86, 61), stroke=0, shadow=False)
    elif t < 5.5:
        lt = t - 1.4
        # 약병 대신 정비소에서 받은 COMSTOCK XR 캔을 보여주는 패러디.
        d.rounded_rectangle((35, 25, 605, 99), 22, fill=(255, 252, 236, 238), outline=green, width=4)
        title(cnv, C["ask"], lang, (320, 62), 535, 52, 35, fill=(36, 86, 61), stroke=0, shadow=False)
        can = Image.new("RGBA", (138, 214), (0, 0, 0, 0))
        cd = ImageDraw.Draw(can)
        cd.rounded_rectangle((18, 20, 120, 202), 18, fill=(248, 246, 238), outline=(52, 97, 77), width=5)
        cd.rectangle((30, 64, 108, 153), fill=pink, outline=(172, 92, 113), width=3)
        title(can, "COMSTOCK", "en", (69, 92), 70, 28, 20, fill=(44, 74, 57), stroke=0, shadow=False)
        title(can, "XR", "en", (69, 127), 70, 44, 38, fill=(44, 74, 57), stroke=0, shadow=False)
        paste(cnv, can, 140, 253)
        draw_hero(cnv, 420, 340, 230, lt, bob=True)
        draw_zombie(cnv, 555, 316, 145, lt, flip=True)
        # 좀비도 평화롭게 피크닉 중이라는 이상한 디테일.
        d.polygon([(495, 304), (617, 304), (605, 345), (482, 345)], fill=(255, 239, 195, 190))
    elif t < 9.2:
        lt = t - 5.5
        # 평온한 화면과 달리 작은 글씨가 무섭게 빨라지고 뒤에서는 계속 폭발한다.
        draw_hero(cnv, 325, 340, 220, lt, bob=True)
        for i in range(7):
            x = 65 + i * 87
            draw_zombie(cnv, x, 310, 120, lt + i * 0.2, flip=i % 2 == 0)
        for i, (x, y, at) in enumerate(((88, 250, 0.1), (545, 245, 0.7), (170, 282, 1.2), (470, 286, 1.8), (240, 250, 2.4))):
            explosion(cnv, x, y, lt, at, 115)
        d.rounded_rectangle((21, 18, 619, 113), 20, fill=(255, 252, 236, 240), outline=pink, width=4)
        title(cnv, C["side"], lang, (320, 44), 560, 30, 24, fill=(120, 64, 77), stroke=0, kind="bold", shadow=False)
        title(cnv, C["effects"], lang, (320, 78), 560, 38, 22, fill=(48, 99, 72), stroke=0, kind="bold", shadow=False)
        title(cnv, C["fine"], lang, (320, 345), 600, 18, 12, fill=(255, 255, 255), stroke=1, kind="bold")
    else:
        lt = t - 9.2
        # 처방약 광고 특유의 행복한 엔딩을 결과 문구로 비튼다.
        draw_hero(cnv, 335, 340, 238, lt, bob=True)
        draw_zombie(cnv, 95, 315, 150, lt, flip=False)
        draw_zombie(cnv, 550, 315, 150, lt + 0.3, flip=True)
        explosion(cnv, 95, 270, lt, 0.65, 160)
        explosion(cnv, 550, 270, lt, 1.45, 160)
        d.rounded_rectangle((62, 26, 578, 157), 26, fill=(255, 252, 236, 238), outline=green, width=5)
        title(cnv, C["result"], lang, (320, 91), 470, 102, 44, fill=(38, 89, 62), stroke=0, shadow=False)
        # 법적 고지처럼 보이게 하되 읽을 수 있는 크기로 유지.
        title(cnv, C["fine"], lang, (320, 345), 610, 18, 12, fill=(255, 255, 255), stroke=1, kind="bold")
    # 부드러운 약 광고 광택.
    glow = Image.new("RGBA", (W, H), (255, 255, 255, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((430, -100, 800, 250), fill=(255, 255, 255, 22))
    cnv = Image.alpha_composite(cnv, glow.filter(ImageFilter.GaussianBlur(20)))
    return ImageEnhance.Color(cnv.convert("RGB")).enhance(1.08)


def endcard_frame(t: float, lang: str) -> Image.Image:
    """첨부 이미지의 중앙 로고를 픽셀 그대로 보존하고 나머지는 언어별로 재합성한다."""
    L = LANG[lang]
    cnv = Image.new("RGBA", (W, H), (22, 8, 34, 255))
    burst(cnv, (320, 190), ((86, 29, 128), (165, 59, 205), (26, 9, 41)), 30, 0.0)
    src = Image.open(ENDCARD_SOURCE).convert("RGBA")
    # 사용자가 준 이미지의 COMSTOCK 워드마크/금속 패널 영역을 그대로 사용한다.
    logo = src.crop((289, 318, 633, 432))
    logo = logo.resize((310, 103), LANCZOS)
    q = ease_out(t / 0.28)
    if q < 1:
        logo = logo.resize((max(2, int(logo.width * q)), max(2, int(logo.height * q))), LANCZOS)
    paste(cnv, logo, 320, 225)
    d = ImageDraw.Draw(cnv)
    title(cnv, L["price"], lang, (320, 45), 560, 42, 30, fill=(238, 238, 238), stroke=3)
    f = fit_font(L["price"], lang, 560, 42, 30, "impact", 3)
    bb = d.textbbox((0, 0), L["price"], font=f, stroke_width=3)
    tw = bb[2] - bb[0]
    d.line((320 - tw / 2 - 8, 53, 320 + tw / 2 + 8, 39), fill=(55, 12, 17), width=8)
    d.line((320 - tw / 2 - 8, 51, 320 + tw / 2 + 8, 37), fill=(227, 67, 28), width=4)
    pulse = 1.0 + 0.025 * math.sin(t * 8)
    free_size = int(76 * pulse)
    title(cnv, L["free"], lang, (320, 125), 330, 82, free_size, fill=(99, 224, 112), stroke_fill=(10, 31, 15), stroke=6)
    title(cnv, L["cta_screen"], lang, (320, 300), 570, 40, 31, fill=(255, 196, 37), stroke=4)
    title(cnv, "pyramid-studio.itch.io/comstock", "en", (320, 335), 540, 28, 22,
          fill=(255, 196, 37), stroke=3, kind="bold")
    scanlines(cnv, 14)
    return cnv.convert("RGB")


def frame_at(concept: str, lang: str, t: float) -> Image.Image:
    if t >= 12.0:
        return endcard_frame(t - 12.0, lang)
    if concept == "weather":
        im = weather_frame(t, lang)
        cuts = (1.2, 4.8, 8.0, 12.0)
    elif concept == "lawyer":
        im = lawyer_frame(t, lang)
        cuts = (1.2, 4.6, 8.0, 12.0)
    else:
        im = pharma_frame(t, lang)
        cuts = (1.4, 5.5, 9.2, 12.0)
    # 컷 첫 프레임의 짧은 흰 플래시는 후킹 광고의 박자를 분명하게 만든다.
    fl = 0.0
    for cut in cuts:
        fl += math.exp(-((t - cut) / 0.038) ** 2)
    if fl > 0.02:
        im = Image.blend(im, Image.new("RGB", (W, H), (255, 255, 255)), min(0.78, fl * 0.72))
    return im


def render_silent(concept: str, lang: str, out: Path) -> None:
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
           "-r", str(FPS), "-i", "-", "-an",
           "-vf", f"scale={OUT_W}:{OUT_H}:flags=lanczos,format=yuv420p",
           "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
           "-r", str(FPS), "-t", str(DUR), "-movflags", "+faststart", str(out)]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    assert proc.stdin is not None
    for frame in range(FRAMES):
        proc.stdin.write(frame_at(concept, lang, frame / FPS).tobytes())
        if frame % 120 == 0:
            print(f"  {concept}/{lang} {frame:03d}/{FRAMES}", flush=True)
    proc.stdin.close()
    if proc.wait():
        raise RuntimeError(f"video encode failed: {concept}/{lang}")


def synth_voice(path: Path, lang: str, text: str, rate: int) -> None:
    """현재 샌드박스는 설치된 SAPI 보이스를 열거하지만 파일 출력은 E_ACCESSDENIED로 막는다.

    영상의 언어 정보는 결정적 화면 자막으로 전달하고, 오디오는 컨셉별 합성 베드와 게임 SFX로
    구성한다. 믹서 입력 구조는 유지하기 위해 50ms 무음 WAV를 만든다.
    """
    del lang, text, rate
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(48000)
        wf.writeframes(b"\x00\x00" * 2400)


def voice_duration(path: Path) -> float:
    with wave.open(str(path), "rb") as wf:
        return wf.getnframes() / wf.getframerate()


def build_bed(concept: str, path: Path) -> None:
    sr = 48000
    n = int(sr * DUR)
    out = array.array("h", [0]) * n
    rng = random.Random({"weather": 70, "lawyer": 71, "pharma": 72}[concept])
    for i in range(n):
        t = i / sr
        v = 0.0
        if concept == "weather":
            if t < 1.2:
                gate = 1.0 if int(t * 5) % 2 == 0 else 0.28
                v += gate * (0.17 * math.sin(math.tau * 820 * t) + 0.10 * math.sin(math.tau * 1040 * t))
            else:
                v += 0.05 * math.sin(math.tau * 82.4 * t) + 0.035 * math.sin(math.tau * 123.5 * t)
                v += 0.018 * math.sin(math.tau * 247 * t) * (0.5 + 0.5 * math.sin(math.tau * 4 * t))
        elif concept == "lawyer":
            chord = (130.8, 164.8, 196.0) if int(t / 1.5) % 2 == 0 else (146.8, 174.6, 220.0)
            v += sum(0.025 * math.sin(math.tau * f * t) for f in chord)
            if t < 1.2:
                ring = 1.0 if int(t * 7) % 2 == 0 else 0.0
                v += ring * 0.10 * math.sin(math.tau * (730 + 120 * math.sin(math.tau * 18 * t)) * t)
        else:
            notes = (261.6, 329.6, 392.0, 523.3)
            idx = int(t * 4) % len(notes)
            phase = (t * 4) % 1
            env = math.exp(-phase * 5.0)
            v += 0.09 * env * math.sin(math.tau * notes[idx] * t)
            v += 0.025 * math.sin(math.tau * 196 * t)
        # 아주 약한 테이프/방송 노이즈로 무음을 피한다.
        v += rng.uniform(-0.006, 0.006)
        out[i] = max(-32768, min(32767, int(v * 32767)))
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(out.tobytes())


SFX_SCHEDULE = {
    "weather": [("UI_Click.wav", 1.2, 0.6), ("UI_Click.wav", 4.8, 0.6),
                ("Weapon_Explosive.wav", 8.0, 0.85), ("Weapon_RapidFire.wav", 8.4, 0.25),
                ("Enemy_Death.wav", 9.4, 0.45), ("LevelUp.wav", 12.0, 0.65)],
    "lawyer": [("UI_Click.wav", 1.2, 0.7), ("LevelUp.wav", 4.6, 0.7),
               ("Weapon_Explosive.wav", 8.0, 0.85), ("Weapon_RapidFire.wav", 8.35, 0.28),
               ("Enemy_Death.wav", 9.8, 0.48), ("LevelUp.wav", 12.0, 0.65)],
    "pharma": [("UI_Click.wav", 1.4, 0.5), ("LevelUp.wav", 5.5, 0.5),
               ("Weapon_Explosive.wav", 6.0, 0.50), ("Weapon_RapidFire.wav", 6.4, 0.18),
               ("Enemy_Death.wav", 8.0, 0.35), ("Weapon_Explosive.wav", 9.85, 0.60),
               ("LevelUp.wav", 12.0, 0.65)],
}


def mix_audio(silent: Path, main_voice: Path, cta_voice: Path, bed: Path,
              concept: str, out: Path) -> None:
    schedule = SFX_SCHEDULE[concept]
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
           "-i", str(silent), "-i", str(main_voice), "-i", str(cta_voice), "-i", str(bed)]
    for name, _, _ in schedule:
        cmd += ["-i", str(RES / "SFX" / name)]
    filters = [
        "[1:a]highpass=f=90,lowpass=f=8500,volume=1.28,adelay=130|130[main]",
        "[2:a]highpass=f=90,lowpass=f=8500,volume=1.35,adelay=12150|12150[cta]",
        "[3:a]volume=0.62[bed]",
    ]
    labels = ["[main]", "[cta]", "[bed]"]
    for idx, (_, at, gain) in enumerate(schedule, start=4):
        label = f"sx{idx}"
        delay = int(at * 1000)
        filters.append(f"[{idx}:a]volume={gain},adelay={delay}|{delay}[{label}]")
        labels.append(f"[{label}]")
    filters.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0:duration=longest,"
                   "acompressor=threshold=0.16:ratio=4:attack=8:release=150:makeup=1.35,"
                   "alimiter=limit=0.94,atrim=0:15,afade=t=out:st=14.78:d=0.22[aout]")
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
    dur = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", p.stderr)
    video = re.search(r"Video: h264.*?, yuv420p.*?, (\d+)x(\d+).*?, (\d+(?:\.\d+)?) fps", p.stderr)
    audio = "Audio: aac" in p.stderr
    if not dur or not video:
        raise RuntimeError(f"probe failed: {path}\n{p.stderr[-2000:]}")
    seconds = int(dur.group(1)) * 3600 + int(dur.group(2)) * 60 + float(dur.group(3))
    return {"duration": seconds, "width": int(video.group(1)), "height": int(video.group(2)),
            "fps": float(video.group(3)), "aac_audio": audio}


def make_contact_sheet(concept: str) -> Path:
    times = [0.55, 2.5, 6.2, 9.8, 12.7]
    thumb_w, thumb_h = 320, 180
    sheet = Image.new("RGB", (thumb_w * len(times), thumb_h * 2 + 54), (18, 18, 22))
    sd = ImageDraw.Draw(sheet)
    for row, lang in enumerate(("en", "ko")):
        encoded = HERE / f"Comstock_Ad_{concept.title()}_{lang.upper()}_15s.mp4"
        for col, t in enumerate(times):
            if encoded.exists():
                p = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-ss", str(t),
                                    "-i", str(encoded), "-frames:v", "1", "-f", "image2pipe",
                                    "-vcodec", "png", "-"], stdout=subprocess.PIPE, check=True)
                im = Image.open(io.BytesIO(p.stdout)).convert("RGB")
            else:
                im = frame_at(concept, lang, t)
            im = im.resize((thumb_w, thumb_h), LANCZOS)
            sheet.paste(im, (col * thumb_w, 27 + row * thumb_h))
        label = f"{concept.upper()}  —  {lang.upper()}"
        sd.text((8, 5 + row * thumb_h), label, font=font("en", "bold", 18), fill=(255, 255, 255))
    path = HERE / f"contact-sheet-{concept}.jpg"
    sheet.save(path, quality=92)
    return path


def render_one(concept: str, lang: str) -> dict:
    stem = f"Comstock_Ad_{concept.title()}_{lang.upper()}_15s"
    silent = HERE / f"_{stem}_silent.mp4"
    main_voice = HERE / f"_{stem}_main.wav"
    cta_voice = HERE / f"_cta_{lang}.wav"
    bed = HERE / f"_bed_{concept}.wav"
    final = HERE / f"{stem}.mp4"
    if not main_voice.exists() or main_voice.stat().st_size < 1000:
        synth_voice(main_voice, lang, LANG[lang][f"{concept}_vo"], LANG[lang]["rate"])
    if not cta_voice.exists() or cta_voice.stat().st_size < 1000:
        synth_voice(cta_voice, lang, LANG[lang]["cta"], LANG[lang]["cta_rate"])
    if not bed.exists():
        build_bed(concept, bed)
    render_silent(concept, lang, silent)
    mix_audio(silent, main_voice, cta_voice, bed, concept, final)
    silent.unlink(missing_ok=True)
    info = probe(final)
    if abs(info["duration"] - DUR) > 0.05 or (info["width"], info["height"]) != (OUT_W, OUT_H) or not info["aac_audio"]:
        raise RuntimeError(f"invalid export: {final}: {info}")
    return {
        "concept": concept, "language": lang, "output": final.name, "sha256": sha256(final),
        "probe": info, "main_voice_seconds": round(voice_duration(main_voice), 3),
        "cta_voice_seconds": round(voice_duration(cta_voice), 3),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--concept", choices=("weather", "lawyer", "pharma", "all"), default="all")
    ap.add_argument("--lang", choices=("en", "ko", "all"), default="all")
    ap.add_argument("--preview", action="store_true", help="콘택트시트만 생성")
    args = ap.parse_args()
    concepts = ("weather", "lawyer", "pharma") if args.concept == "all" else (args.concept,)
    langs = ("en", "ko") if args.lang == "all" else (args.lang,)
    if not ENDCARD_SOURCE.exists() or not HERO_PATH.exists():
        raise FileNotFoundError("endcard_source.png 또는 comstock_hero.png가 없습니다")
    if args.preview:
        for concept in concepts:
            print(make_contact_sheet(concept))
        return
    results = []
    for concept in concepts:
        for lang in langs:
            print(f"render: {concept}/{lang}")
            results.append(render_one(concept, lang))
        make_contact_sheet(concept)
    manifest = {
        "title": "컴스톡 북미풍 병맛 후킹 광고 3종 × 한/영",
        "format": {"size": [OUT_W, OUT_H], "fps": FPS, "duration_seconds": DUR,
                   "video": "H.264/yuv420p", "audio": "AAC 48kHz stereo"},
        "concepts": {
            "weather": "미국 지역방송 좀비 기상특보 패러디",
            "lawyer": "미국 상해 전문 변호사 광고 패러디",
            "pharma": "북미 처방약 광고 패러디",
        },
        "required_copy": {"ko": LANG["ko"]["cta_screen"], "en": LANG["en"]["cta_screen"]},
        "endcard_source": {"file": ENDCARD_SOURCE.name, "sha256": sha256(ENDCARD_SOURCE),
                           "usage": "중앙 COMSTOCK 워드마크를 픽셀 그대로 보존하고 언어별 문구를 결정적 합성"},
        "sources": ["dev/pv/assets/comstock_hero.png", "Assets/Resources/ZombieMove/*.png",
                    "Assets/Resources/Enemy_zombie.png", "Assets/Resources/Explosion/*.png",
                    "Assets/Resources/SFX/*.wav"],
        "voices": {"en": "narration omitted; localized on-screen copy", "ko": "내레이션 생략; 화면 자막 현지화"},
        "exports": results,
        "contact_sheets": [f"contact-sheet-{c}.jpg" for c in concepts],
        "generator": Path(__file__).name,
    }
    (HERE / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print("complete")


if __name__ == "__main__":
    main()
