#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
컴스톡(Comstock) 게임 PV 렌더러 — 15초 양산형 모바일 게임 광고
================================================================

어디서 본 것 같은 모바일 게임 광고를 그대로 흉내 낸 15초 세로 영상을 만든다.
가짜 조작 UI, 손가락 커서, 미친 듯이 오르는 숫자, SSR 뽑기 연출, 가짜 선택지,
가짜 랭킹까지 장르 클리셰를 전부 담는다. 게임에 실제로 들어 있는 스프라이트
(`Assets/Resources/`)만 재료로 쓰고, 프레임을 한 장씩 합성한 뒤 ffmpeg로
H.264 MP4를 뽑는다.

    python3 PV/make_pv.py            # 전체 영상 렌더 (PV/out/comstock_pv.mp4)
    python3 PV/make_pv.py --stills   # 확인용 정지 프레임만 뽑기
    python3 PV/make_pv.py --preview 7.5   # 특정 시각 한 장만

구조
----
- 세로 9:16(1080x1920)이다. 이 장르는 세로 화면 자체가 신호라서, 비율만
  바꿔도 "모바일 광고"로 읽힌다.
- 장면은 `TIMELINE`이 `(시작초, 길이, 함수, 이름)`으로 들고 있다.
- 합성이 끝난 프레임에 후처리(채도 부스트 → 블룸 → 줌 펀치 → 화면 흔들림 →
  플래시)를 얹는다. 흑백 필름과 정반대로, 눈이 아플 만큼 밝고 선명하게 간다.

에셋은 Git LFS로 관리되므로 실행 전에 `git lfs pull`이 되어 있어야 한다.
"""

import argparse
import math
import os
import random
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# ---------------------------------------------------------------- 경로 / 상수

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(ROOT, "Assets", "Resources")
FONT_DIR = os.path.join(ROOT, "Assets", "Fonts")
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

FPS = 30
DURATION = 15.0
TOTAL_FRAMES = int(round(FPS * DURATION))

W, H = 1080, 1920          # 세로 9:16

FONT_KR = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Bold.ttf")
FONT_KR_R = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Regular.ttf")
FONT_EN = os.path.join(FONT_DIR, "Orbitron", "Orbitron-Black.ttf")

SEED = 20260827

# 양산형 광고 팔레트 — 금색 그라데이션이 이 장르의 기본값이다
GOLD = ((255, 248, 186), (255, 146, 0))
FIRE = ((255, 214, 120), (226, 24, 24))
CYAN = ((214, 252, 255), (0, 156, 255))
LIME = ((238, 255, 176), (86, 214, 0))
MAGENTA = ((255, 208, 255), (214, 0, 168))
PLAIN = ((255, 255, 255), (222, 226, 236))


# ------------------------------------------------------------------- 캐시류

_img_cache = {}
_scaled_cache = {}
_font_cache = {}
_misc_cache = {}


def img(rel, crop=True):
    """`Assets/Resources/` 기준 상대경로로 RGBA 스프라이트를 읽는다.

    crop=True면 알파 bbox로 잘라내므로, 배치할 때 '그림의 실제 크기'를 기준으로
    좌표를 잡을 수 있다(원본 캔버스의 빈 여백에 위치가 휘둘리지 않는다)."""
    key = (rel, crop)
    if key in _img_cache:
        return _img_cache[key]
    im = Image.open(os.path.join(RES, rel)).convert("RGBA")
    if crop:
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
    _img_cache[key] = im
    return im


def seq(folder, pattern, count, start=0):
    """연속 프레임 폴더를 리스트로 읽는다.

    **프레임마다 따로 bbox를 자르면 안 된다.** 걷기/폭발처럼 실루엣이 변하는
    애니메이션은 프레임마다 bbox가 달라서, 각자 잘라 같은 높이로 그리면 그림이
    프레임마다 튄다. 그래서 **전체 프레임의 합집합 bbox로 한 번에** 잘라
    프레임 사이의 상대 위치·크기를 그대로 보존한다."""
    key = ("seq", folder, pattern, count, start)
    if key in _img_cache:
        return _img_cache[key]
    ims = [Image.open(os.path.join(RES, folder, pattern.format(i))).convert("RGBA")
           for i in range(start, start + count)]
    bb = None
    for im in ims:
        b = im.getbbox()
        if b is None:
            continue
        bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]),
                                   max(bb[2], b[2]), max(bb[3], b[3]))
    if bb:
        ims = [im.crop(bb) for im in ims]
    _img_cache[key] = ims
    return ims


def font(path, size):
    key = (path, size)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(path, size)
    return _font_cache[key]


def scaled(im, w, h, flip=False):
    w, h = max(1, int(round(w))), max(1, int(round(h)))
    key = (id(im), w, h, flip)
    if key in _scaled_cache:
        return _scaled_cache[key]
    out = im.resize((w, h), Image.LANCZOS)
    if flip:
        out = out.transpose(Image.FLIP_LEFT_RIGHT)
    if len(_scaled_cache) > 3000:
        _scaled_cache.clear()
    _scaled_cache[key] = out
    return out


# ------------------------------------------------------------------ 이징 함수

def clamp(v, lo=0.0, hi=1.0):
    return lo if v < lo else (hi if v > hi else v)


def ease_out(t):
    return 1.0 - (1.0 - clamp(t)) ** 3


def ease_in(t):
    return clamp(t) ** 3


def ease_out_back(t, s=2.6):
    t = clamp(t) - 1.0
    return t * t * ((s + 1) * t + s) + 1.0


def pulse(t, period, duty=0.5):
    return 1.0 if (t % period) / period < duty else 0.0


def lerp(a, b, k):
    return a + (b - a) * clamp(k)


# ---------------------------------------------------------------- 그리기 도구

def put(canvas, sprite, cx, cy, height=None, width=None, scale=None, box=None,
        size=None, flip=False, angle=0.0, alpha=255, anchor="center"):
    """스프라이트를 캔버스에 얹는다. 크기는 height/width/scale/box/size 중 하나로 지정.

    - `box=(w, h)`: 그 사각형 **안에 들어가도록** 비율을 유지한 채 맞춘다.
      가로로 납작한 그림을 세로 기준으로만 키우면 화면 밖으로 삐져나가 뭉개지므로,
      클로즈업에는 box를 쓴다.
    - `size=(w, h)`: 비율을 무시하고 **그 크기로 늘인다.** UI 패널·버튼은 9-슬라이스라
      늘어나는 게 정상이고, box로 맞추면 정사각형으로 쪼그라들어 버린다."""
    sw, sh = sprite.size
    if size is not None:
        tw, th = max(1, int(size[0])), max(1, int(size[1]))
        im = scaled(sprite, tw, th, flip)
        if angle:
            im = im.rotate(angle, resample=Image.BICUBIC, expand=True)
        if alpha < 255:
            a = im.getchannel("A").point(lambda v: v * alpha // 255)
            im = im.copy()
            im.putalpha(a)
        w, h = im.size
        canvas.alpha_composite(im, (int(cx - w / 2), int(cy - h / 2)))
        return
    if box is not None:
        k = min(box[0] / sw, box[1] / sh)
    elif height is not None:
        k = height / sh
    elif width is not None:
        k = width / sw
    elif scale is not None:
        k = scale
    else:
        k = 1.0
    tw, th = max(1, int(sw * k)), max(1, int(sh * k))
    im = scaled(sprite, tw, th, flip)
    if angle:
        im = im.rotate(angle, resample=Image.BICUBIC, expand=True)
    if alpha < 255:
        a = im.getchannel("A").point(lambda v: v * alpha // 255)
        im = im.copy()
        im.putalpha(a)
    w, h = im.size
    if anchor == "center":
        x, y = int(cx - w / 2), int(cy - h / 2)
    elif anchor == "bottom":
        x, y = int(cx - w / 2), int(cy - h)
    elif anchor == "top":
        x, y = int(cx - w / 2), int(cy)
    else:
        x, y = int(cx), int(cy)
    canvas.alpha_composite(im, (x, y))


def vgrad(size, top, bottom):
    """세로 선형 그라데이션 이미지."""
    w, h = size
    col = np.linspace(0.0, 1.0, h, dtype=np.float32).reshape(h, 1, 1)
    a = np.array(top, dtype=np.float32).reshape(1, 1, 3)
    b = np.array(bottom, dtype=np.float32).reshape(1, 1, 3)
    arr = (a + (b - a) * col).astype(np.uint8)
    return Image.fromarray(np.repeat(arr, w, axis=1), "RGB")


def fancy_text(text, fnt, grad=GOLD, stroke=16, stroke_fill=(22, 14, 6),
               rim=(255, 255, 255), rim_w=None, spacing=6, align="center"):
    """양산형 광고 자막: **검은 굵은 외곽선 → 흰 테두리 → 금색 그라데이션 속살.**

    이 3겹이 이 장르의 서명이다. 한 겹이라도 빠지면 그냥 평범한 글씨가 된다."""
    rim_w = max(2, stroke // 2) if rim_w is None else rim_w
    pad = stroke * 2 + 26
    d0 = ImageDraw.Draw(Image.new("RGBA", (8, 8)))
    bb = d0.multiline_textbbox((0, 0), text, font=fnt, spacing=spacing,
                               align=align, stroke_width=stroke)
    w = int(math.ceil(bb[2] - bb[0])) + pad * 2
    h = int(math.ceil(bb[3] - bb[1])) + pad * 2
    pos = (pad - bb[0], pad - bb[1])

    layer = Image.new("RGBA", (max(1, w), max(1, h)), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.multiline_text(pos, text, font=fnt, fill=stroke_fill, spacing=spacing,
                     align=align, stroke_width=stroke, stroke_fill=stroke_fill)
    d.multiline_text(pos, text, font=fnt, fill=rim, spacing=spacing,
                     align=align, stroke_width=rim_w, stroke_fill=rim)

    mask = Image.new("L", layer.size, 0)
    ImageDraw.Draw(mask).multiline_text(pos, text, font=fnt, fill=255,
                                        spacing=spacing, align=align)
    layer.paste(vgrad(layer.size, grad[0], grad[1]), (0, 0), mask)

    bb2 = layer.getbbox()
    return layer.crop(bb2) if bb2 else layer


def caption(canvas, text, cy, size=92, grad=GOLD, pop=1.0, angle=0.0, alpha=255,
            cx=None, stroke=None, spacing=8, kr=True, plate=0):
    fnt = font(FONT_KR if kr else FONT_EN, size)
    st = stroke if stroke is not None else max(8, size // 6)
    im = fancy_text(text, fnt, grad=grad, stroke=st, spacing=spacing)
    cx = W / 2 if cx is None else cx
    if plate:
        pw, ph = int((im.width + 66) * pop), int((im.height + 30) * pop)
        bar = Image.new("RGBA", (max(1, pw), max(1, ph)), (12, 8, 22, plate))
        canvas.alpha_composite(bar, (int(cx - pw / 2), int(cy - ph / 2)))
    put(canvas, im, cx, cy, scale=pop, angle=angle, alpha=alpha)


def flash(canvas, amount, color=(255, 255, 255)):
    if amount <= 0:
        return
    canvas.alpha_composite(Image.new("RGBA", (W, H), color + (int(255 * clamp(amount)),)))


def darken(canvas, amount, color=(0, 0, 0)):
    if amount <= 0:
        return
    canvas.alpha_composite(Image.new("RGBA", (W, H), color + (int(255 * clamp(amount)),)))


def rays(canvas, t, cx, cy, n=28, speed=0.6, color=(255, 255, 255), alpha=54):
    """회전하는 집중선. 이 장르는 뭔가 좋은 일이 생길 때마다 이걸 깐다."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    R = W * 2.2
    for i in range(n):
        a = (i / n) * math.tau + t * speed
        wdt = math.tau / n * 0.5 * 0.62
        d.polygon([(cx, cy),
                   (cx + math.cos(a - wdt) * R, cy + math.sin(a - wdt) * R),
                   (cx + math.cos(a + wdt) * R, cy + math.sin(a + wdt) * R)],
                  fill=color + (alpha,))
    canvas.alpha_composite(layer)


def stars(canvas, t, seedbase, cx, cy, rad, n=16, size=26, color=(255, 246, 190)):
    r = random.Random(seedbase)
    d = ImageDraw.Draw(canvas)
    for i in range(n):
        ph, a = r.random(), r.random() * math.tau
        rr = rad * (0.35 + 0.65 * r.random())
        k = (t * 1.9 + ph) % 1.0
        s = math.sin(k * math.pi) * size
        if s < 1.5:
            continue
        x, y = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        d.polygon([(x, y - s), (x + s * .28, y - s * .28), (x + s, y),
                   (x + s * .28, y + s * .28), (x, y + s),
                   (x - s * .28, y + s * .28), (x - s, y),
                   (x - s * .28, y - s * .28)], fill=color + (235,))


# --------------------------------------------------------------- 손가락 커서

def finger():
    """탭하는 손가락 커서. 이 장르에서 빠지면 안 되는 소품이다."""
    if "finger" in _misc_cache:
        return _misc_cache["finger"]
    im = Image.new("RGBA", (300, 420), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    LINE, SKIN = (46, 30, 22, 255), (255, 227, 199, 255)
    # 외곽선을 먼저 통짜로 깔고 그 안에 살색을 얹는다.
    # (도형마다 outline을 주면 겹치는 자리에 선이 두 번 그려져 지저분해진다)
    d.rounded_rectangle([44, 168, 250, 400], radius=76, fill=LINE)
    d.rounded_rectangle([96, 20, 186, 240], radius=45, fill=LINE)
    d.rounded_rectangle([54, 178, 240, 390], radius=68, fill=SKIN)
    d.rounded_rectangle([106, 30, 176, 232], radius=36, fill=SKIN)
    d.rounded_rectangle([118, 44, 164, 92], radius=22, fill=(255, 245, 228, 255))
    _misc_cache["finger"] = im
    return im


def tap(canvas, t, x, y, period=0.62, size=300, tilt=-16):
    """손가락이 주기적으로 화면을 누른다 + 파문."""
    k = (t % period) / period
    press = math.sin(clamp(k / 0.34) * math.pi) if k < 0.34 else 0.0
    d = ImageDraw.Draw(canvas)
    if k < 0.55:
        g = clamp(k / 0.55)
        r = 40 + 150 * g
        d.ellipse([x - r, y - r, x + r, y + r],
                  outline=(255, 255, 255, int(210 * (1 - g))), width=int(12 * (1 - g)) + 2)
    put(canvas, finger(), x + 82, y + 30 + press * 26, height=size * (1 - press * 0.06),
        angle=tilt, anchor="top")


# ------------------------------------------------------------------ 배경 도구

def battle_bg(t, scroll=90, tint=None, bright=1.18):
    """폐허 도시 배경을 세로 화면에 맞춰 잘라 쓴다."""
    key = "bgmaster"
    if key not in _misc_cache:
        src = Image.open(os.path.join(RES, "ground_ruined_city_v2_tile.png")).convert("RGB")
        k = H / src.height
        one = src.resize((int(src.width * k), H), Image.LANCZOS)
        m = Image.new("RGB", (one.width * 2, H))
        m.paste(one, (0, 0))
        m.paste(one.transpose(Image.FLIP_LEFT_RIGHT), (one.width, 0))
        _misc_cache[key] = m
    m = _misc_cache[key]
    ox = int(t * scroll) % (m.width // 2)
    out = m.crop((ox, 0, ox + W, H)).convert("RGBA")
    arr = np.asarray(out, dtype=np.float32)
    arr[..., :3] *= bright
    if tint:
        arr[..., :3] = arr[..., :3] * 0.72 + np.array(tint, dtype=np.float32) * 0.28
    out = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
    return out


def hype_bg(t, top=(92, 24, 168), bottom=(232, 60, 110), ray_alpha=48):
    """뽑기·CTA용 원색 그라데이션 배경 + 집중선."""
    c = vgrad((W, H), top, bottom).convert("RGBA")
    rays(c, t, W / 2, H * 0.46, n=30, speed=0.55, alpha=ray_alpha)
    return c


def new_canvas(rgb=(0, 0, 0)):
    return Image.new("RGBA", (W, H), rgb + (255,))


# =============================================================================
#  가짜 조작 UI (HUD)
# =============================================================================
#
#  광고에서 진짜로 중요한 건 게임이 아니라 "게임처럼 보이는 것"이다.
#  조이스틱·스킬버튼·체력바·재화 표시는 실제 게임의 UI 아트로 조립한다.

HUD_SKILLS = ["RightPlasmaCannon.png", "ChainsawSword.png",
              "RightRocketLauncher.png", "RightCombatShotgun.png"]

# (중심 오프셋 x, y, 반지름) — 오른쪽 아래 엄지 닿는 자리에 모아 둔다
SKILL_SLOTS = [(0, 0, 112), (-206, -34, 84), (-152, -216, 84), (18, -258, 84)]


def hud_static():
    """매 프레임 안 바뀌는 부분은 한 번만 그려 캐시한다."""
    if "hud" in _misc_cache:
        return _misc_cache["hud"]
    c = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(c)

    # 상단 재화 패널 두 개
    for x0, icon in ((46, "UI/Gold_icon00.png"), (556, "UI/Exp_icon.png")):
        put(c, img("UI/Black_ui00.png"), x0 + 239, 120, size=(478, 96))
        put(c, img(icon), x0 + 62, 120, height=66)

    # 조이스틱
    jx, jy = 232, H - 300
    d.ellipse([jx - 148, jy - 148, jx + 148, jy + 148], fill=(10, 10, 20, 96),
              outline=(255, 255, 255, 120), width=7)

    # 스킬 버튼 4개 — 각도 계산으로 두면 화면 밖으로 밀리기 쉬워서 좌표를 직접 박는다
    for (dx, dy, r), name in zip(SKILL_SLOTS, HUD_SKILLS):
        bx, by = W - 196 + dx, H - 300 + dy
        put(c, img("UI/Grade/Purple/Purple_button02.png"), bx, by, size=(r * 2, r * 2))
        put(c, img(name), bx, by, box=(r * 1.15, r * 1.15))

    _misc_cache["hud"] = c
    return c


def hp_bar(c, cx, cy, w, ratio):
    put(c, img("UI/Mshp_panel01.png"), cx, cy, width=w)
    fill = img("UI/Green_hp01.png") if ratio > .6 else (
        img("UI/Orange_hp02.png") if ratio > .3 else img("UI/Red_hp01.png"))
    fw = max(2, (w - 18) * ratio)
    put(c, fill, cx - (w - 18) / 2 + fw / 2, cy, width=fw)


def draw_hud(c, t, gold, gem, hp=0.72, level=None, auto=False, wave=None):
    c.alpha_composite(hud_static())
    d = ImageDraw.Draw(c)

    fnt = font(FONT_EN, 38)
    for x0, val in ((46, f"{int(gold):,}"), (556, f"{int(gem):,}")):
        im = fancy_text(val, fnt, grad=PLAIN, stroke=6)
        # 자릿수가 늘어나도 패널을 넘지 않게 폭을 잘라 준다
        w = min(im.width, 352)
        put(c, im, x0 + 112 + w / 2, 120, width=w)

    # 조이스틱 손잡이가 살짝 돈다
    jx, jy = 232, H - 300
    ox, oy = math.cos(t * 3.1) * 56, math.sin(t * 2.3) * 56
    d.ellipse([jx + ox - 66, jy + oy - 66, jx + ox + 66, jy + oy + 66],
              fill=(255, 255, 255, 232), outline=(70, 80, 110, 235), width=6)

    hp_bar(c, W / 2, H - 496, 620, hp)

    if level is not None:
        im = fancy_text(f"Lv.{int(level)}", font(FONT_EN, 58), grad=GOLD, stroke=11)
        put(c, im, W / 2, 216)
    if wave:
        im = fancy_text(wave, font(FONT_KR, 38), grad=PLAIN, stroke=8)
        put(c, im, W / 2, H - 578)
    if auto:
        blink = 0.55 + 0.45 * pulse(t, 0.4, .5)
        put(c, img("UI/Grade/Gold/Gold_button02.png"), W - 132, 236, size=(196, 112))
        im = fancy_text("AUTO", font(FONT_EN, 40), grad=GOLD, stroke=8)
        put(c, im, W - 132, 236, alpha=int(255 * blink))


# --------------------------------------------------------------- 데미지 숫자

def damage_numbers(c, t, seedbase, n=10, area=(140, 760, 940, 1330), period=0.9,
                   lo=8000, hi=999999, crit_every=4):
    """화면을 뒤덮는 데미지 숫자. 클수록, 많을수록 좋다는 게 이 장르의 문법이다."""
    r = random.Random(seedbase)
    for i in range(n):
        ph = r.random()
        x = r.randint(area[0], area[2])
        y0 = r.randint(area[1], area[3])
        val = r.randint(lo, hi)
        crit = (i % crit_every == 0)
        k = ((t / period) + ph) % 1.0
        if k > 0.72:
            continue
        g = k / 0.72
        pop = ease_out_back(clamp(g / 0.22), 3.4) * (1.0 - 0.25 * g)
        alpha = int(255 * (1.0 - max(0.0, (g - 0.6) / 0.4)))
        size = 74 if crit else 52
        txt = f"{val:,}" + ("!" if crit else "")
        im = fancy_text(txt, font(FONT_EN, size), grad=FIRE if crit else GOLD,
                        stroke=max(7, size // 7))
        put(c, im, x, y0 - g * 170, scale=pop, alpha=alpha,
            angle=-8 if crit else 0)


# =============================================================================
#  장면들 — 각 함수는 (t, dur)를 받아 캔버스(RGBA 1080x1920)를 돌려준다
# =============================================================================

_Z = {}


def zwalk():
    if "z" not in _Z:
        _Z["z"] = seq("ZombieMove", "walk_left_f{}.png", 8)
    return _Z["z"]


def horde(c, t, rows=None, speed=1.0, seedbase=3):
    """세로 화면이라 좀비는 '위에서 아래로' 밀려 내려온다."""
    fr = zwalk()
    r = random.Random(seedbase)
    rows = rows or [(700, 180, 150), (880, 230, 128), (1090, 290, 108), (1330, 360, 92)]
    for ri, (y, hgt, spd) in enumerate(rows):
        n = 5
        for i in range(n):
            ph = r.random()
            x = 90 + ((i / n) + ph * 0.2) * (W - 180) + math.sin(t * 1.6 + i * 2.1) * 40
            yy = y + ((t * spd * speed + ph * 400) % 260) - 130
            f = fr[int(t * 12 * speed + i * 3 + ri) % 8]
            put(c, f, x, yy, height=hgt, flip=(i % 2 == 0), anchor="bottom")


# --- 1. 훅 (0.0 ~ 1.5) -------------------------------------------------------

def scene_hook(t, dur):
    c = battle_bg(t, scroll=70, tint=(120, 40, 160), bright=1.1)
    horde(c, t, speed=1.3)
    put(c, img("Comstock.png"), W / 2, H - 640 + math.sin(t * 7) * 10, height=520,
        anchor="bottom")
    mz = seq("MuzzleFlash", "frame_{:02d}.png", 3, start=1)
    if int(t * 20) % 2 == 0:
        put(c, mz[int(t * 24) % 3], W / 2 + 200, H - 966, height=168)
        put(c, mz[int(t * 19) % 3], W / 2 - 192, H - 950, height=138, flip=True)
    damage_numbers(c, t, 11, n=11, period=0.75)

    draw_hud(c, t, gold=12_450 + t * 8600, gem=930, hp=0.72, level=1 + int(t * 3),
             wave="WAVE 1 / 20")
    tap(c, t, W - 176, H - 292, period=0.5)

    if t > 0.10:
        caption(c, "이게  진짜  무료라고?", 356,
                size=96, grad=GOLD, pop=ease_out_back(clamp((t - .10) / .22), 3.4),
                angle=math.sin(t * 9) * 1.6)
    if t > 0.72:
        caption(c, "설치 1분 만에  만렙", 496, size=68, grad=CYAN,
                pop=ease_out_back(clamp((t - .72) / .2), 3.2), plate=150)
    return c


# --- 2. 방치형 (1.5 ~ 3.4) ---------------------------------------------------

def scene_idle(t, dur):
    c = battle_bg(t + 3, scroll=70, tint=(30, 130, 90), bright=1.16)
    horde(c, t + 2, speed=2.4)
    put(c, img("Comstock.png"), W / 2, H - 640, height=520, anchor="bottom")
    damage_numbers(c, t, 23, n=14, period=0.5, lo=90_000, hi=99_999_999, crit_every=3)

    lv = 1 + int(ease_out(clamp(t / 1.45)) * 998)
    draw_hud(c, t, gold=120_000 + t * 480_000, gem=930 + t * 900, hp=1.0,
             level=lv, auto=True, wave="자동 전투 중")

    # 레벨업 이펙트가 쉴 새 없이 터진다
    lu = seq("LevelUpEffect", "level-up_{:03d}.png", 24, start=1)
    for i, (x, y, off) in enumerate(((300, 980, 0.0), (790, 1180, 0.33), (540, 800, 0.66))):
        k = ((t * 1.9 + off) % 1.0)
        put(c, lu[min(23, int(k * 24))], x, y, height=440, alpha=225)

    # 골드 비
    r = random.Random(77)
    for i in range(26):
        ph = r.random()
        x = r.randint(60, W - 60)
        k = ((t * 0.85 + ph) % 1.0)
        put(c, img("Gold.png"), x, -90 + k * (H + 180), height=72,
            angle=(t * 260 + i * 40) % 360)

    if t < 1.05:
        caption(c, "폰 꺼놔도\n알아서 렙업!", 372, size=96, grad=GOLD,
                pop=ease_out_back(clamp(t / .2), 3.4), spacing=14,
                angle=math.sin(t * 7) * 1.4)
    else:
        caption(c, "접속만 해도  Lv.999", 372, size=82, grad=FIRE,
                pop=ease_out_back(clamp((t - 1.05) / .2), 3.4),
                angle=math.sin(t * 22) * 2.2)
    return c


# --- 3. SSR 뽑기 (3.4 ~ 5.6) -------------------------------------------------

HEADS = ["Berserker.png", "Guardman.png", "Meteus.png", "HotPot.png",
         "SodaCan.png", "FanBot.png", "HappyPixel.png", "MiniPixie.png",
         "Pixie.png", "NeonEye_0.png", "PrivateComstock.png", "ComstockMk01.png"]


def scene_gacha(t, dur):
    # 어두워졌다가 → 무지개 광선 → 폭발 → 등장. 이 장르의 뽑기 연출 그대로다.
    if t < 0.34:
        c = new_canvas((10, 6, 26))
        rays(c, t, W / 2, H * 0.46, n=24, speed=2.6,
             color=(180, 130, 255), alpha=int(70 * ease_out(t / 0.34)))
        caption(c, "1 0 0 연 차   무 료", H * 0.46, size=76, grad=CYAN,
                pop=ease_out_back(clamp(t / .2)), alpha=235)
        return c

    lt = t - 0.34
    c = hype_bg(lt * 2.4, top=(46, 12, 96), bottom=(216, 52, 176), ray_alpha=64)
    stars(c, lt, 5, W / 2, H * 0.46, 620, n=26, size=42)

    # 머리들이 회전하며 돌다가 하나가 중앙에 착지
    ring = clamp(1.0 - lt / 0.62)
    for i, h in enumerate(HEADS):
        a = (i / len(HEADS)) * math.tau + lt * 3.4
        rr = 430 * ring
        if ring < 0.02:
            break
        put(c, img(f"Heads/{h}"), W / 2 + math.cos(a) * rr, H * 0.46 + math.sin(a) * rr,
            height=150 * ring, alpha=int(255 * ring))

    k = clamp((lt - 0.5) / 0.34)
    if k > 0:
        kk = ease_out_back(k)
        put(c, img("UI/Grade/Gold/Gold_Panel00.png"), W / 2, H * 0.46,
            size=(720 * kk, 840 * kk))
        put(c, img("Heads/Berserker.png"), W / 2, H * 0.46,
            height=430 * ease_out_back(k), angle=math.sin(lt * 6) * 3)
        expl = seq("Explosion", "frame_{:02d}.png", 10, start=1)
        if lt < 0.95:
            put(c, expl[min(9, int((lt - 0.5) / 0.045))], W / 2, H * 0.46, height=1000)

    if lt > 0.62:
        p = ease_out_back(clamp((lt - 0.62) / 0.2), 3.6)
        caption(c, "★ ★ ★ ★ ★", H * 0.46 - 430, size=86, grad=GOLD, pop=p,
                angle=math.sin(lt * 16) * 2)
        caption(c, "전 설 등 급   획 득 !", H * 0.46 + 430, size=88, grad=FIRE,
                pop=p, angle=-2)
    if lt > 1.25:
        caption(c, "지금 접속 시 즉시 지급", H - 430, size=54, grad=PLAIN,
                pop=ease_out_back(clamp((lt - 1.25) / .2)), plate=170)
    if 0.34 < t < 0.52:
        flash(c, (0.52 - t) / 0.18 * 0.9)
    return c


# --- 4. 무기 자랑 (5.6 ~ 7.3) ------------------------------------------------

WEAPONS = ["RightHMG.png", "RightPlasmaCannon.png", "RightCombatShotgun.png",
           "RightRocketLauncher.png", "ChainsawSword.png", "RightSawedOff.png",
           "RightDMR.png", "Machete.png", "RightLaserPistol.png",
           "RightGiganchong.png", "SurvivalKnife.png", "RightAMR.png"]

GRADE_COLORS = [((255, 226, 128), (208, 128, 0)),      # 레전더리
                ((214, 158, 255), (112, 24, 190)),     # 유니크
                ((150, 214, 255), (18, 96, 200)),      # 에픽
                ((255, 168, 168), (186, 26, 26))]      # 레어


def grade_card(size, gi, stars_n):
    """등급 카드를 직접 그린다. 프로젝트의 9-슬라이스 UI를 정사각 카드로 쓰면
    모서리 장식이 뭉개져서, 뽑기 카드용으로는 이쪽이 깔끔하다."""
    key = ("card", size, gi, stars_n)
    if key in _misc_cache:
        return _misc_cache[key]
    w, h = size
    top, bot = GRADE_COLORS[gi]
    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=30, fill=(16, 12, 30, 255))
    inner = Image.new("RGBA", (w - 20, h - 20), (0, 0, 0, 0))
    m = Image.new("L", inner.size, 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, inner.width - 1, inner.height - 1],
                                        radius=22, fill=255)
    inner.paste(vgrad(inner.size, top, bot), (0, 0), m)
    card.alpha_composite(inner, (10, 10))
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=30,
                        outline=(255, 255, 255, 235), width=7)
    # 등급 별
    sx, sy, s = w / 2 - (stars_n - 1) * 17, h - 30, 13
    for i in range(stars_n):
        x = sx + i * 34
        d.polygon([(x, sy - s), (x + s * .3, sy - s * .3), (x + s, sy),
                   (x + s * .3, sy + s * .3), (x, sy + s), (x - s * .3, sy + s * .3),
                   (x - s, sy), (x - s * .3, sy - s * .3)], fill=(255, 248, 190, 255))
    _misc_cache[key] = card
    return card


def scene_weapons(t, dur):
    c = hype_bg(t, top=(14, 34, 96), bottom=(0, 152, 210), ray_alpha=44)
    stars(c, t, 9, W / 2, H * 0.5, 700, n=18, size=30)

    cols, rows = 3, 4
    for i, name in enumerate(WEAPONS):
        st = 0.05 + i * 0.055
        if t < st:
            continue
        p = ease_out_back(clamp((t - st) / 0.24), 3.2)
        gx = (i % cols) - (cols - 1) / 2
        gy = (i // cols) - (rows - 1) / 2
        x = W / 2 + gx * 322
        y = H * 0.52 + gy * 286
        drop = (1 - ease_out(clamp((t - st) / 0.28))) * -420
        gi = i % 4
        put(c, grade_card((272, 272), gi, 5 - gi), x, y + drop,
            size=(272 * p, 272 * p))
        put(c, img(name), x, y + drop - 14, box=(186 * p, 186 * p),
            angle=math.sin(t * 3 + i) * 5)

    if t > 0.25:
        caption(c, "무기 65종\n전부 무료 지급", 268, size=88, grad=GOLD,
                pop=ease_out_back(clamp((t - .25) / .2), 3.4), spacing=12, plate=140)
    if t > 1.05:
        caption(c, "( 진 짜 로 )", H - 320, size=64, grad=FIRE,
                pop=ease_out_back(clamp((t - 1.05) / .2)),
                angle=math.sin(t * 18) * 2.4)
    return c


# --- 5. 가짜 선택지 (7.3 ~ 9.5) ----------------------------------------------

def scene_quiz(t, dur):
    c = hype_bg(t * 0.5, top=(24, 20, 46), bottom=(96, 24, 68), ray_alpha=30)
    d = ImageDraw.Draw(c)

    # 좌우 두 갈래
    for side, (label, sprite, col) in enumerate((
            ("1", "Machete.png", (60, 120, 255)),
            ("2", "RightPlasmaCannon.png", (255, 76, 76)))):
        x = W * (0.27 if side == 0 else 0.73)
        y = H * 0.52
        d.rounded_rectangle([x - 234, y - 300, x + 234, y + 300], radius=44,
                            fill=col + (86,), outline=(255, 255, 255, 210), width=8)
        put(c, img(sprite), x, y - 40, box=(380, 380), angle=math.sin(t * 4 + side) * 6)
        put(c, fancy_text(label, font(FONT_EN, 92), grad=PLAIN, stroke=14), x, y + 216)
    d.line([W / 2, H * 0.52 - 300, W / 2, H * 0.52 + 300], fill=(255, 255, 255, 190), width=8)

    caption(c, "당신의 선택은?", 300, size=92, grad=GOLD,
            pop=ease_out_back(clamp(t / .2), 3.4))

    # 카운트다운
    left = max(0, 3 - int(t / 0.42))
    if t < 1.26:
        caption(c, str(max(1, left)), H - 400, size=150, grad=FIRE,
                pop=ease_out_back(clamp(((t % 0.42) / 0.18)), 3.6),
                angle=math.sin(t * 30) * 3)
        tap(c, t, W * 0.27, H * 0.52, period=0.42, size=300)
    elif t < 1.80:
        # 틀렸습니다 (양산형 광고의 그 연출)
        k = clamp((t - 1.26) / 0.18)
        darken(c, 0.5 * k, (90, 0, 0))
        x, y = W * 0.27, H * 0.52
        r = 250 * ease_out_back(k)
        d.line([x - r, y - r, x + r, y + r], fill=(255, 42, 42, 245), width=int(34 * k) + 2)
        d.line([x - r, y + r, x + r, y - r], fill=(255, 42, 42, 245), width=int(34 * k) + 2)
        caption(c, "실 패 !", H - 400, size=124, grad=FIRE,
                pop=ease_out_back(k, 3.6), angle=math.sin(t * 26) * 3)
    else:
        k = clamp((t - 1.80) / 0.2)
        darken(c, 0.42 * k, (0, 60, 20))
        for x in (W * 0.27, W * 0.73):
            r = 250 * ease_out_back(k)
            d.arc([x - r, y_ := H * 0.52 - r, x + r, y_ + 2 * r], 0, 360,
                  fill=(96, 255, 120, 240), width=int(30 * k) + 2)
        caption(c, "정답 :  둘 다 장착", H - 400, size=88, grad=LIME,
                pop=ease_out_back(k, 3.4))
    return c


# --- 6. 보스 (9.5 ~ 11.4) ----------------------------------------------------

def scene_boss(t, dur):
    c = battle_bg(t + 8, scroll=40, tint=(150, 20, 20), bright=0.92)
    darken(c, 0.22)
    rays(c, t, W / 2, H * 0.42, n=22, speed=1.4, color=(255, 90, 60), alpha=42)

    roar = seq("BossRoar", "frame_{:03d}.png", 36, start=1)
    boom = seq("BossDeathExplosion", "frame_{:02d}.png", 60, start=1)

    if t < 1.20:
        fi = min(35, int(t * 26))
        put(c, roar[fi], W / 2, H * 0.70, box=(W * 0.96, 900 + 160 * ease_out(clamp(t / 1.1))),
            anchor="bottom")
        damage_numbers(c, t, 31, n=12, period=0.42, lo=1_000_000, hi=999_999_999,
                       crit_every=2, area=(150, 820, 930, 1240))

    put(c, img("Comstock.png"), W / 2, H - 560, height=380, anchor="bottom")

    # 폭발은 로봇보다 **뒤가 아니라 앞**이다. 뒤에 그리면 폭발이 안 보인다.
    if t >= 1.20:
        fi = min(59, int((t - 1.20) * 62))
        put(c, boom[fi], W / 2, H * 0.60, box=(W * 1.35, 1350))
    draw_hud(c, t, gold=9_820_000, gem=44_900, hp=0.95, level=999, auto=True,
             wave="BOSS · WAVE 20")

    caption(c, "DPS  999,999,999", 336, size=72, grad=FIRE, kr=False,
            pop=ease_out_back(clamp(t / .18), 3.4), angle=math.sin(t * 24) * 1.8)
    if t > 1.20:
        caption(c, "보스도  3초컷", 486, size=92, grad=GOLD,
                pop=ease_out_back(clamp((t - 1.20) / .2), 3.6))
        flash(c, max(0.0, 0.85 - (t - 1.20) / 0.2))
    return c


# --- 7. 가짜 사회적 증거 (11.4 ~ 13.1) ---------------------------------------

RANKS = [("1", "나", "999", True), ("2", "칼퇴요정", "981", False),
         ("3", "좀비고기맛", "944", False), ("4", "볼트조립왕", "902", False),
         ("5", "출근하기싫다", "877", False)]


def scene_proof(t, dur):
    c = hype_bg(t * 0.4, top=(12, 26, 72), bottom=(0, 132, 186), ray_alpha=36)
    caption(c, "전 서버 1위 달성!", 268, size=88, grad=GOLD,
            pop=ease_out_back(clamp(t / .2), 3.4))

    for i, (no, name, lv, me) in enumerate(RANKS):
        st = 0.16 + i * 0.09
        if t < st:
            continue
        y = 520 + i * 168
        slide = (1 - ease_out(clamp((t - st) / 0.26))) * 660
        panel = "UI/Grade/Gold/Gold_ui01.png" if me else "UI/Black_ui00.png"
        # UI 패널은 9-슬라이스라 늘여야 한다(box로 맞추면 정사각형으로 쪼그라든다)
        put(c, img(panel), W / 2 + slide, y, size=(880, 138))
        g = GOLD if me else PLAIN
        put(c, fancy_text(no, font(FONT_EN, 50), grad=g, stroke=9), 190 + slide, y)
        put(c, fancy_text(name, font(FONT_KR, 48), grad=g, stroke=9), 420 + slide, y)
        put(c, fancy_text(f"Lv.{lv}", font(FONT_EN, 42), grad=g, stroke=9),
            W - 200 + slide, y)

    if t > 0.86:
        p = ease_out_back(clamp((t - 0.86) / 0.2), 3.2)
        caption(c, "★★★★★  4.9", H - 566, size=72, grad=GOLD, pop=p)
        caption(c, "다운로드 100만 돌파", H - 448, size=62, grad=CYAN, pop=p)
    if t > 1.20:
        caption(c, "“인생겜 인정합니다 ㅠㅠ”  - 칼퇴요정", H - 320, size=42,
                grad=PLAIN, pop=ease_out_back(clamp((t - 1.20) / .2)), plate=185)
    return c


# --- 8. CTA (13.1 ~ 15.0) ----------------------------------------------------

def scene_cta(t, dur):
    c = hype_bg(t * 0.6, top=(96, 16, 140), bottom=(240, 96, 24), ray_alpha=58)
    stars(c, t, 13, W / 2, H * 0.42, 640, n=22, size=36)

    # 로고 착지
    k = clamp(t / 0.3)
    logo = fancy_text("COMSTOCK", font(FONT_EN, 116), grad=GOLD, stroke=18)
    put(c, logo, W / 2, 420 + (1 - ease_out(k)) * -420)
    if t > 0.3:
        caption(c, "컴 스 톡", 570, size=88, grad=PLAIN,
                pop=ease_out_back(clamp((t - .3) / .2), 3.2))

    put(c, img("Comstock.png"), W / 2, H * 0.60 + math.sin(t * 6) * 14, height=440)

    # 눌러 달라고 뛰는 버튼
    if t > 0.5:
        p = ease_out_back(clamp((t - .5) / .22), 3.4)
        bob = 1.0 + 0.05 * math.sin(t * 11)
        put(c, img("UI/Grade/Gold/Gold_button03.png"), W / 2, H - 470,
            size=(820 * p * bob, 236 * p * bob))
        caption(c, "지 금  플 레 이", H - 470, size=80, grad=FIRE, pop=p * bob)
        tap(c, t - 0.5, W / 2 + 210, H - 452, period=0.62, size=330)

    # "무료" 리본
    if t > 0.72:
        p = ease_out_back(clamp((t - .72) / .2), 3.6)
        caption(c, "무 료", 320, size=72, grad=FIRE, pop=p, cx=W - 210,
                angle=-16, plate=190)

    # 선착순 카운트다운 (실제로는 아무 일도 일어나지 않는다)
    left = max(0, int(9 - (t - 0.4) * 4))
    if t > 0.4:
        caption(c, f"선착순 마감까지  00:{left:02d}", H - 300, size=46, grad=PLAIN,
                pop=1.0, plate=170)

    # 초소형 면책 — 이 장르의 마지막 필수 요소
    small = fancy_text(
        "※ 실제 게임 화면과 다를 수 있습니다  ※ 위 수치·랭킹·리뷰는 전부 연출입니다"
        "  ※ 100연차·선착순 이벤트는 존재하지 않습니다",
        font(FONT_KR_R, 24), grad=PLAIN, stroke=4, rim_w=2)
    put(c, small, W / 2, H - 108, width=min(small.width, W - 60))
    return c


# =============================================================================
#  타임라인
# =============================================================================

TIMELINE = [
    (0.0,  1.5, scene_hook,    "훅"),
    (1.5,  1.9, scene_idle,    "방치형"),
    (3.4,  2.2, scene_gacha,   "SSR 뽑기"),
    (5.6,  1.7, scene_weapons, "무기 자랑"),
    (7.3,  2.2, scene_quiz,    "가짜 선택지"),
    (9.5,  1.9, scene_boss,    "보스"),
    (11.4, 1.7, scene_proof,   "가짜 랭킹"),
    (13.1, 1.9, scene_cta,     "CTA"),
]

CUTS = [s for s, _, _, _ in TIMELINE[1:]]
# 임팩트가 있어야 하는 순간들 — 줌 펀치와 화면 흔들림이 여기서 터진다
HITS = [0.10, 0.72, 1.50, 2.55, 3.40, 3.96, 4.30, 5.60, 7.30, 8.56, 9.50,
        10.78, 11.40, 13.10, 13.60]


def render_content(t):
    for st, dur, fn, _ in TIMELINE:
        if st <= t < st + dur:
            return fn(t - st, dur)
    last = TIMELINE[-1]
    return last[2](last[1] - 1e-3, last[1])


# =============================================================================
#  후처리 — 채도 · 블룸 · 줌 펀치 · 흔들림 · 플래시
# =============================================================================

def punch(t):
    """임팩트 순간마다 화면이 확 당겨졌다 풀린다."""
    z = 0.0
    for h in HITS:
        d = t - h
        if 0 <= d < 0.22:
            z = max(z, 0.055 * (1 - d / 0.22) ** 2)
    return z


def shake(t):
    for h in HITS:
        d = t - h
        if 0 <= d < 0.18:
            a = (1 - d / 0.18)
            return (math.sin(d * 150) * 26 * a, math.cos(d * 173) * 20 * a)
    return (0.0, 0.0)


def cut_flash(t):
    for cut in CUTS:
        d = t - cut
        if 0 <= d < 0.12:
            return 0.55 * (1 - d / 0.12)
    return 0.0


_vig = None


def vignette():
    global _vig
    if _vig is None:
        yy, xx = np.mgrid[0:H, 0:W]
        nx = (xx - W / 2) / (W / 2)
        ny = (yy - H / 2) / (H / 2)
        r = np.sqrt(nx * nx + ny * ny * 0.72)
        _vig = (1.0 - 0.30 * np.clip((r - 0.62) / 0.9, 0, 1) ** 1.6).astype(np.float32)
    return _vig


def post(rgba, t):
    """모바일 광고 톤: 채도를 올리고, 밝은 데를 번지게 하고, 화면을 흔든다."""
    # 줌 펀치 + 흔들림은 이미지 자체를 확대/이동해서 만든다
    z = 1.0 + punch(t)
    dx, dy = shake(t)
    if z > 1.001 or dx or dy:
        bw, bh = int(W * z * 1.03), int(H * z * 1.03)
        big = rgba.resize((bw, bh), Image.BILINEAR)
        ox = int((bw - W) / 2 + dx)
        oy = int((bh - H) / 2 + dy)
        ox = max(0, min(bw - W, ox))
        oy = max(0, min(bh - H, oy))
        rgba = big.crop((ox, oy, ox + W, oy + H))

    rgb = rgba.convert("RGB")
    arr = np.asarray(rgb, dtype=np.float32)

    # 채도 부스트 + 살짝의 대비
    luma = (arr[..., 0] * .299 + arr[..., 1] * .587 + arr[..., 2] * .114)[..., None]
    arr = luma + (arr - luma) * 1.34
    arr = (arr - 128.0) * 1.07 + 132.0
    arr = np.clip(arr, 0, 255)

    # 블룸: 밝은 부분만 뽑아 흐리게 번지게 한 뒤 스크린 합성
    bright = np.clip((arr - 176.0) * 2.2, 0, 255).astype(np.uint8)
    bl = Image.fromarray(bright, "RGB").resize((W // 4, H // 4), Image.BILINEAR)
    bl = bl.filter(ImageFilter.GaussianBlur(11)).resize((W, H), Image.BILINEAR)
    b = np.asarray(bl, dtype=np.float32) * 0.62
    arr = 255.0 - (255.0 - arr) * (255.0 - b) / 255.0

    arr *= vignette()[..., None]

    f = cut_flash(t)
    if f > 0:
        arr = arr + (255.0 - arr) * f

    return np.clip(arr, 0, 255).astype(np.uint8)


def compose_frame(t):
    return post(render_content(t), t)


# =============================================================================
#  오디오 — 게임 BGM + 효과음 폭탄
# =============================================================================

SFX_CUES = [
    (0.05, "SFX/Weapon_RapidFire.wav", 0.9),
    (0.40, "SFX/Enemy_Hit_A.wav", 0.8),
    (0.72, "SFX/Weapon_Shotgun.wav", 0.9),
    (1.05, "SFX/Enemy_Death.wav", 0.8),
    (1.50, "SFX/LevelUp.wav", 1.0),
    (1.95, "SFX/LevelUp.wav", 0.8),
    (2.40, "SFX/LevelUp.wav", 0.8),
    (2.85, "SFX/LevelUp.wav", 0.9),
    (3.40, "SFX/UI_Click.wav", 0.9),
    (3.96, "SFX/Weapon_Explosive.wav", 1.0),
    (4.35, "SFX/LevelUp.wav", 1.0),
    (5.60, "SFX/UI_Click.wav", 0.8),
    (5.95, "SFX/UI_Click.wav", 0.7),
    (6.30, "SFX/UI_Click.wav", 0.7),
    (7.30, "SFX/UI_Click.wav", 0.9),
    (8.56, "SFX/Enemy_Hit_C.ogg", 1.0),
    (9.10, "SFX/LevelUp.wav", 0.9),
    (9.50, "SFX/Boss_Hit_A.wav", 1.0),
    (9.95, "SFX/Weapon_PlasmaCannon.wav", 0.9),
    (10.40, "SFX/Boss_Hit_B.wav", 0.9),
    (10.78, "SFX/Boss_Death.wav", 1.0),
    (11.40, "SFX/UI_Click.wav", 0.8),
    (11.80, "SFX/UI_Click.wav", 0.7),
    (12.26, "SFX/LevelUp.wav", 0.9),
    (13.10, "SFX/Weapon_Explosive.wav", 0.9),
    (13.62, "SFX/UI_Click.wav", 1.0),
    (14.30, "SFX/UI_Click.wav", 0.9),
]


def build_audio(ff, out_path):
    inputs, parts, idx = [], [], 0

    inputs += ["-i", os.path.join(RES, "Musics", "Game_BGM01.mp3")]
    parts.append(f"[{idx}:a]atrim=12:{12 + DURATION},asetpts=N/SR/TB,volume=0.62,"
                 f"afade=t=in:st=0:d=0.15,afade=t=out:st={DURATION-0.7}:d=0.7[bgm]")
    idx += 1

    mix = ["[bgm]"]
    for i, (at, rel, vol) in enumerate(SFX_CUES):
        p = os.path.join(RES, rel)
        if not os.path.exists(p):
            continue
        inputs += ["-i", p]
        parts.append(f"[{idx}:a]volume={vol},adelay={int(at*1000)}|{int(at*1000)}[s{i}]")
        mix.append(f"[s{i}]")
        idx += 1

    parts.append("".join(mix) +
                 f"amix=inputs={len(mix)}:duration=first:dropout_transition=0:"
                 "normalize=0[mixed]")
    # 모바일 광고는 크고 납작하게 들린다. 저역을 살짝 올리고 세게 눌러 붙인 뒤
    # loudnorm으로 방송보다 높은 수준까지 끌어올린다.
    parts.append("[mixed]bass=g=3:f=110,acompressor=threshold=0.08:ratio=8:"
                 "attack=5:release=110,loudnorm=I=-13:TP=-1.0:LRA=9,"
                 f"atrim=0:{DURATION},asetpts=N/SR/TB,"
                 "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[out]")

    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error"] + inputs +
                   ["-filter_complex", ";".join(parts), "-map", "[out]",
                    "-c:a", "aac", "-b:a", "160k", out_path], check=True)


# =============================================================================
#  실행
# =============================================================================

def ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return "ffmpeg"


def render_stills(times):
    os.makedirs(OUT_DIR, exist_ok=True)
    for t in times:
        p = os.path.join(OUT_DIR, f"still_{t:05.2f}.png".replace(".", "_", 1))
        Image.fromarray(compose_frame(t)).save(p)
        print("saved", p)


def render_video(path):
    os.makedirs(OUT_DIR, exist_ok=True)
    ff = ffmpeg_exe()
    silent = os.path.join(OUT_DIR, "_video_only.mp4")
    audio = os.path.join(OUT_DIR, "_audio.m4a")

    proc = subprocess.Popen(
        [ff, "-y", "-hide_banner", "-loglevel", "error",
         "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
         "-r", str(FPS), "-i", "-",
         "-c:v", "libx264", "-preset", "slow", "-crf", "21",
         "-maxrate", "8M", "-bufsize", "16M",
         "-pix_fmt", "yuv420p", "-movflags", "+faststart", silent],
        stdin=subprocess.PIPE)
    for fi in range(TOTAL_FRAMES):
        proc.stdin.write(compose_frame(fi / FPS).tobytes())
        if fi % 30 == 0:
            print(f"  frame {fi}/{TOTAL_FRAMES}  ({fi/FPS:5.2f}s)", flush=True)
    proc.stdin.close()
    if proc.wait() != 0:
        raise SystemExit("ffmpeg(video) 실패")

    print("오디오 믹싱…", flush=True)
    build_audio(ff, audio)
    print("먹싱…", flush=True)
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error",
                    "-i", silent, "-i", audio, "-c:v", "copy", "-c:a", "copy",
                    "-shortest", "-movflags", "+faststart", path], check=True)
    for p in (silent, audio):
        if os.path.exists(p):
            os.remove(p)
    print("완료:", path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stills", action="store_true")
    ap.add_argument("--preview", type=float, default=None)
    ap.add_argument("-o", "--out", default=os.path.join(OUT_DIR, "comstock_pv.mp4"))
    args = ap.parse_args()

    if args.preview is not None:
        render_stills([args.preview])
    elif args.stills:
        render_stills([0.55, 1.20, 2.10, 3.00, 3.60, 4.40, 5.20,
                       6.10, 6.90, 7.70, 8.70, 9.30, 9.90, 10.90,
                       11.90, 12.60, 13.50, 14.40])
    else:
        render_video(args.out)


if __name__ == "__main__":
    main()
