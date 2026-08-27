#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
컴스톡(Comstock) 게임 PV 렌더러 — 15초 스팀 트레일러 (캐주얼 뱀서라이크)
=========================================================================

사용자 콘티(2026-08-27)를 그대로 옮긴 6컷 구성이다.

    1. 컴스톡이 등장
    2. 좀비가 뒤돌아보며 컴스톡을 보고, 대군단이 온다
    3. 컴스톡이 잡으면서 진행
    4. 게임으로 전환하면서 게임플레이를 보여준다
    5. 장비 전환 + 각 컴스톡들을 빠르게 → "합격" 도장
    6. 게임 로고

톤은 **스팀에 올리는 캐주얼 뱀서라이크 트레일러**다. 세 가지가 이 톤을 만든다.

    · 가로 16:9 (1920x1080) — PC 게임이라는 가장 빠른 신호
    · 모바일 조작 UI를 걷어내고 PC HUD(상단 경험치 바·웨이브 타이머·재화)로 교체
    · 자막은 광고 톤이 아니라 하단 스크림 위의 담백한 흰 글씨

게임에 실제로 들어 있는 스프라이트(`Assets/Resources/`)만 재료로 쓰고,
프레임을 한 장씩 합성한 뒤 ffmpeg로 H.264 MP4를 뽑는다.

    python3 PV/make_pv.py            # 전체 영상 렌더 (PV/out/comstock_pv.mp4)
    python3 PV/make_pv.py --stills   # 확인용 정지 프레임만 뽑기
    python3 PV/make_pv.py --preview 7.5   # 특정 시각 한 장만

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

W, H = 1920, 1080          # 가로 16:9 — 스팀 트레일러 규격

FONT_KR = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Bold.ttf")
FONT_KR_R = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Regular.ttf")
FONT_EN = os.path.join(FONT_DIR, "Orbitron", "Orbitron-Black.ttf")

SEED = 20260827

# 게임 아트의 색을 그대로 쓴다 — 보라 UI, 주황 포인트, 청록 경험치
INK = (18, 14, 30)
WHITE = ((255, 255, 255), (232, 236, 248))
AMBER = ((255, 226, 150), (255, 148, 32))
CYAN = ((206, 250, 255), (46, 178, 255))
LIME = ((236, 255, 186), (108, 214, 44))
GOLD = ((255, 248, 186), (255, 146, 0))
FIRE = ((255, 214, 120), (226, 24, 24))


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


def ease_in_out(t):
    t = clamp(t)
    return 3 * t * t - 2 * t * t * t


def ease_out_back(t, s=2.2):
    t = clamp(t) - 1.0
    return t * t * ((s + 1) * t + s) + 1.0


def lerp(a, b, k):
    return a + (b - a) * clamp(k)


# ---------------------------------------------------------------- 그리기 도구

def put(canvas, sprite, cx, cy, height=None, width=None, scale=None, box=None,
        size=None, flip=False, angle=0.0, alpha=255, anchor="center"):
    """스프라이트를 캔버스에 얹는다. 크기는 height/width/scale/box/size 중 하나로.

    - `box=(w, h)`: 그 사각형 **안에 들어가도록** 비율을 유지한 채 맞춘다.
    - `size=(w, h)`: 비율을 무시하고 **그 크기로 늘인다.** UI 패널은 9-슬라이스라
      늘어나는 게 정상이고, box로 맞추면 정사각형으로 쪼그라든다."""
    sw, sh = sprite.size
    if size is not None:
        im = scaled(sprite, max(1, int(size[0])), max(1, int(size[1])), flip)
    else:
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
        im = scaled(sprite, max(1, sw * k), max(1, sh * k), flip)
    if angle:
        im = im.rotate(angle, resample=Image.BICUBIC, expand=True)
    if alpha < 255:
        a = im.getchannel("A").point(lambda v: v * alpha // 255)
        im = im.copy()
        im.putalpha(a)
    w, h = im.size
    if anchor == "bottom":
        x, y = int(cx - w / 2), int(cy - h)
    elif anchor == "top":
        x, y = int(cx - w / 2), int(cy)
    else:
        x, y = int(cx - w / 2), int(cy - h / 2)
    canvas.alpha_composite(im, (x, y))


def vgrad(size, top, bottom):
    w, h = size
    col = np.linspace(0.0, 1.0, h, dtype=np.float32).reshape(h, 1, 1)
    a = np.array(top, dtype=np.float32).reshape(1, 1, 3)
    b = np.array(bottom, dtype=np.float32).reshape(1, 1, 3)
    return Image.fromarray(np.repeat((a + (b - a) * col).astype(np.uint8), w, axis=1), "RGB")


def text_img(text, fnt, grad=WHITE, stroke=11, stroke_fill=(16, 12, 26),
             spacing=8, align="center"):
    """트레일러 자막: **어두운 외곽선 + 밝은 속살** 두 겹.

    앞서 만들던 광고 톤(외곽선 → 흰 테두리 → 금색)은 3겹이라 시끄럽다.
    스팀 트레일러는 게임 화면을 보여 주는 게 목적이라 글씨가 조용해야 한다."""
    pad = stroke * 2 + 20
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
    mask = Image.new("L", layer.size, 0)
    ImageDraw.Draw(mask).multiline_text(pos, text, font=fnt, fill=255,
                                        spacing=spacing, align=align)
    layer.paste(vgrad(layer.size, grad[0], grad[1]), (0, 0), mask)
    bb2 = layer.getbbox()
    return layer.crop(bb2) if bb2 else layer


_scrim = None


def scrim(canvas, amount=1.0):
    """하단 그라데이션 스크림. 자막이 게임 화면 위에서 읽히게 해 준다.
    상자형 자막판보다 화면을 덜 가려서 트레일러에 맞다."""
    global _scrim
    if _scrim is None:
        band = 380
        a = (np.linspace(0, 1, band, dtype=np.float32) ** 2.1 * 205).astype(np.uint8)
        arr = np.zeros((H, W, 4), dtype=np.uint8)
        arr[H - band:, :, 3] = a.reshape(band, 1)
        _scrim = Image.fromarray(arr, "RGBA")
    if amount >= 0.999:
        canvas.alpha_composite(_scrim)
    else:
        s = _scrim.copy()
        s.putalpha(s.getchannel("A").point(lambda v: int(v * clamp(amount))))
        canvas.alpha_composite(s)


def caption(canvas, text, cy=None, size=72, grad=WHITE, pop=1.0, angle=0.0,
            alpha=255, cx=None, stroke=None, spacing=10, kr=True):
    fnt = font(FONT_KR if kr else FONT_EN, size)
    im = text_img(text, fnt, grad=grad,
                  stroke=stroke if stroke is not None else max(7, size // 8),
                  spacing=spacing)
    put(canvas, im, W / 2 if cx is None else cx, H - 150 if cy is None else cy,
        scale=pop, angle=angle, alpha=alpha)


def flash(canvas, amount, color=(255, 255, 255)):
    if amount > 0:
        canvas.alpha_composite(Image.new("RGBA", (W, H), color + (int(255 * clamp(amount)),)))


def darken(canvas, amount, color=(0, 0, 0)):
    if amount > 0:
        canvas.alpha_composite(Image.new("RGBA", (W, H), color + (int(255 * clamp(amount)),)))


def rays(canvas, t, cx, cy, n=26, speed=0.5, color=(255, 255, 255), alpha=44):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    R = W * 1.6
    for i in range(n):
        a = (i / n) * math.tau + t * speed
        wdt = math.tau / n * 0.31
        d.polygon([(cx, cy),
                   (cx + math.cos(a - wdt) * R, cy + math.sin(a - wdt) * R),
                   (cx + math.cos(a + wdt) * R, cy + math.sin(a + wdt) * R)],
                  fill=color + (alpha,))
    canvas.alpha_composite(layer)


def sparks(canvas, t, seedbase, cx, cy, rad, n=14, size=22, color=(255, 246, 200)):
    r = random.Random(seedbase)
    d = ImageDraw.Draw(canvas)
    for i in range(n):
        ph, a = r.random(), r.random() * math.tau
        rr = rad * (0.35 + 0.65 * r.random())
        k = (t * 1.8 + ph) % 1.0
        s = math.sin(k * math.pi) * size
        if s < 1.5:
            continue
        x, y = cx + math.cos(a) * rr, cy + math.sin(a) * rr
        d.polygon([(x, y - s), (x + s * .28, y - s * .28), (x + s, y),
                   (x + s * .28, y + s * .28), (x, y + s), (x - s * .28, y + s * .28),
                   (x - s, y), (x - s * .28, y - s * .28)], fill=color + (230,))


def camera(c, zoom, fx=W / 2, fy=H / 2):
    """장면 안에서의 카메라 줌. 화면 밖으로 밀려난 것은 잘려 나가므로,
    '줌아웃하면 화면 밖에 있던 좀비 떼가 드러난다' 같은 연출을 좌표대로 그려 두면 된다."""
    if abs(zoom - 1.0) < 0.003:
        return c
    bw, bh = max(W, int(W * zoom)), max(H, int(H * zoom))
    big = c.resize((bw, bh), Image.BILINEAR)
    ox = max(0, min(bw - W, int(fx * zoom - W / 2)))
    oy = max(0, min(bh - H, int(fy * zoom - H / 2)))
    return big.crop((ox, oy, ox + W, oy + H))


# ------------------------------------------------------------------ 배경 도구

def battle_bg(t, scroll=90, tint=None, bright=1.0, mix=0.24):
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
    arr = np.asarray(m.crop((ox, 0, ox + W, H)), dtype=np.float32) * bright
    if tint:
        arr = arr * (1 - mix) + np.array(tint, dtype=np.float32) * mix
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def stage_bg(t, top=(38, 30, 78), bottom=(16, 74, 126), ray_alpha=34):
    c = vgrad((W, H), top, bottom).convert("RGBA")
    rays(c, t, W / 2, H * 0.46, n=28, speed=0.45, alpha=ray_alpha)
    return c


def new_canvas(rgb=(0, 0, 0)):
    return Image.new("RGBA", (W, H), rgb + (255,))


# =============================================================================
#  컴스톡 조립 — 머리와 무기를 갈아 끼운 "각 컴스톡"
# =============================================================================
#
#  `Comstock.png`는 무기까지 통째로 그려진 한 장이라 장비를 바꿀 수 없다.
#  그래서 5번 컷용으로는 머리(=몸통) + 다리 파츠 + 좌우 무기를 코드로 조립한다.
#  게임의 `ProceduralCharacterRig`가 하는 일과 같은 발상이다.

ROBOT_PX = 620


def build_robot(head, wl, wr):
    key = ("robot", head, wl, wr)
    if key in _misc_cache:
        return _misc_cache[key]
    S = ROBOT_PX
    c = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(c)
    for sx in (-0.076, 0.076):
        put(c, img("Parts/LegUpper.png"), S * (0.5 + sx), S * 0.80, height=S * 0.115)
        put(c, img("Parts/LegLower.png"), S * (0.5 + sx), S * 0.888, height=S * 0.095)
        put(c, img("Parts/Foot.png"), S * (0.5 + sx), S * 0.958, height=S * 0.078)
    # 팔 연결부 — 무기만 띄워 두면 몸에서 떨어진 것처럼 보인다
    for x0, x1 in ((S * 0.44, S * 0.30), (S * 0.56, S * 0.70)):
        d.line([x0, S * 0.575, x1, S * 0.565], fill=(58, 62, 78, 255), width=int(S * 0.030))
        d.line([x0, S * 0.575, x1, S * 0.565], fill=(150, 158, 178, 255), width=int(S * 0.016))
    put(c, img(wl), S * 0.235, S * 0.555, width=S * 0.375, flip=True)
    put(c, img(wr), S * 0.765, S * 0.555, width=S * 0.375)
    put(c, img(head), S * 0.5, S * 0.545, height=S * 0.44)
    # **bbox로 자르지 않는다.** 무기마다 실루엣 폭이 달라서, 잘라 놓고 같은 높이로
    # 그리면 무기가 클수록 로봇이 작아 보인다(장비를 넘길 때마다 크기가 튄다).
    _misc_cache[key] = c
    return c


VARIANTS = [
    ("ComstockMk01.png", "컴스톡 Mk.01"), ("PrivateComstock.png", "이등병 컴스톡"),
    ("Berserker.png", "버서커"), ("Guardman.png", "가드맨"),
    ("Meteus.png", "메테우스"), ("HotPot.png", "핫팟"),
    ("SodaCan.png", "소다캔"), ("FanBot.png", "팬봇"),
    ("HappyPixel.png", "해피픽셀"), ("MiniPixie.png", "미니픽시"),
    ("Pixie.png", "픽시"), ("NeonEye_0.png", "네온아이"),
]

LOADOUTS = [
    ("LeftHMG.png", "RightHMG.png", "중기관총"),
    ("LeftPlasmaCannon.png", "RightPlasmaCannon.png", "플라즈마 캐논"),
    ("LeftCombatShotgun.png", "RightCombatShotgun.png", "전투 산탄총"),
    ("LeftRocketLauncher.png", "RightRocketLauncher.png", "로켓 런처"),
    ("LeftSMG.png", "RightSMG.png", "기관단총"),
    ("LeftDMR.png", "RightDMR.png", "지정사수 소총"),
]


# =============================================================================
#  PC HUD — 뱀서라이크 관용구 (상단 경험치 바 · 웨이브 타이머 · 재화)
# =============================================================================
#
#  모바일 조작 UI(조이스틱·터치 스킬 버튼·손가락 커서)는 전부 걷어냈다.
#  키보드로 움직이고 공격은 자동인 PC 게임이라는 걸 HUD가 말해 준다.

def hud_frame():
    if "hudf" in _misc_cache:
        return _misc_cache["hudf"]
    c = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(c)
    d.rectangle([0, 0, W, 18], fill=(12, 10, 24, 225))          # 경험치 바 홈
    d.rounded_rectangle([38, 44, 470, 100], radius=12,
                        fill=(12, 10, 24, 175), outline=(226, 220, 255, 90), width=3)
    _misc_cache["hudf"] = c
    return c


def draw_hud(c, t, hp=0.78, level=7, wave=7, secs=47, gold=1240, alpha=1.0):
    if alpha <= 0.01:
        return
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    layer.alpha_composite(hud_frame())
    d = ImageDraw.Draw(layer)

    # 화면 맨 위를 가로지르는 경험치 바 — 뱀서라이크의 서명
    xp = (t * 0.34 + 0.18) % 1.0
    d.rectangle([0, 0, W * xp, 18], fill=(64, 196, 255, 255))
    d.rectangle([0, 15, W * xp, 18], fill=(180, 240, 255, 255))

    # 좌상단 체력
    put(layer, img("UI/Heart_icon.png"), 76, 72, height=46)
    d.rounded_rectangle([112, 56, 452, 88], radius=8, fill=(40, 18, 26, 235))
    col = (108, 214, 44) if hp > .6 else ((255, 156, 32) if hp > .3 else (226, 40, 40))
    d.rounded_rectangle([114, 58, 114 + (336) * hp, 86], radius=7, fill=col + (255,))
    put(layer, text_img(f"{int(160*hp)} / 160", font(FONT_EN, 26), stroke=6), 282, 72)

    # 중앙 상단 웨이브 / 남은 시간
    put(layer, text_img(f"WAVE {wave} / 20", font(FONT_EN, 30), stroke=7), W / 2, 62)
    put(layer, text_img(f"0:{int(secs):02d}", font(FONT_EN, 62), grad=AMBER, stroke=10),
        W / 2, 126)

    # 우상단 재화 / 레벨
    put(layer, img("UI/Gold_icon00.png"), W - 300, 70, height=48)
    put(layer, text_img(f"{int(gold):,}", font(FONT_EN, 34), grad=AMBER, stroke=7),
        W - 232, 70, anchor="center")
    put(layer, img("UI/Exp_icon.png"), W - 108, 70, height=48)
    put(layer, text_img(f"Lv.{int(level)}", font(FONT_EN, 32), grad=CYAN, stroke=7),
        W - 56, 70)

    # 좌하단 장착 무기 두 칸
    for i, wname in enumerate(("RightHMG.png", "ChainsawSword.png")):
        bx = 88 + i * 118
        d.rounded_rectangle([bx - 50, H - 122, bx + 50, H - 22], radius=14,
                            fill=(28, 20, 52, 215), outline=(206, 190, 255, 150), width=3)
        put(layer, img(wname), bx, H - 72, box=(78, 78))

    if alpha < 0.999:
        layer.putalpha(layer.getchannel("A").point(lambda v: int(v * alpha)))
    c.alpha_composite(layer)


# --------------------------------------------------------------- 데미지 숫자

def damage_numbers(c, t, seedbase, n=10, area=(220, 300, 1700, 900), period=0.8,
                   lo=400, hi=6400, crit_every=4):
    r = random.Random(seedbase)
    for i in range(n):
        ph = r.random()
        x, y0 = r.randint(area[0], area[2]), r.randint(area[1], area[3])
        val = r.randint(lo, hi)
        crit = (i % crit_every == 0)
        k = ((t / period) + ph) % 1.0
        if k > 0.7:
            continue
        g = k / 0.7
        pop = ease_out_back(clamp(g / 0.2), 3.0) * (1.0 - 0.22 * g)
        a = int(240 * (1.0 - max(0.0, (g - 0.55) / 0.45)))
        size = 52 if crit else 38
        put(c, text_img(f"{val:,}" + ("!" if crit else ""), font(FONT_EN, size),
                        grad=FIRE if crit else AMBER, stroke=max(6, size // 7)),
            x, y0 - g * 120, scale=pop, alpha=a)


def xp_gems(c, t, seedbase, px, py, n=18):
    """처치한 자리에서 경험치 보석이 플레이어에게 빨려 들어간다.
    이 장르에서 화면이 가장 기분 좋아지는 순간이라 빠뜨리면 안 된다."""
    r = random.Random(seedbase)
    for i in range(n):
        ph = r.random()
        a = r.random() * math.tau
        rr = 260 + r.random() * 520
        k = ((t * 0.9 + ph) % 1.0)
        x = lerp(px + math.cos(a) * rr, px, ease_in(k))
        y = lerp(py + math.sin(a) * rr * 0.62, py, ease_in(k))
        put(c, img("Exp.png"), x, y, height=34 + 10 * math.sin(t * 9 + i),
            alpha=int(255 * (1 - k * 0.35)))


# =============================================================================
#  좀비 떼 — 가로 화면에서는 좌우(그리고 위아래)에서 밀려든다
# =============================================================================

def zwalk():
    if "zw" not in _misc_cache:
        _misc_cache["zw"] = seq("ZombieMove", "walk_left_f{}.png", 8)
    return _misc_cache["zw"]


def swarm(c, t, cx, cy, count=42, seedbase=7, spread=1.0, speed=1.0, hgt=(90, 190),
          ring=(560, 1500)):
    """중심을 향해 모여드는 무리. 뱀서라이크는 '사방에서 조여 온다'가 핵심이라
    줄 세워 걷게 하지 않고 원형으로 배치해 안쪽으로 당긴다."""
    fr = zwalk()
    r = random.Random(seedbase)
    items = []
    for i in range(count):
        a = r.random() * math.tau
        ph = r.random()
        base = lerp(ring[1], ring[0], ((t * 0.16 * speed + ph) % 1.0))
        rr = base * spread
        x = cx + math.cos(a) * rr * 1.35
        y = cy + math.sin(a) * rr * 0.66
        z = (y + 1000) / 2000.0
        items.append((y, x, lerp(hgt[0], hgt[1], clamp(z)), i, a))
    for y, x, hh, i, a in sorted(items):          # 아래쪽이 앞에 오도록
        f = fr[int(t * 11 * speed + i * 3) % 8]
        put(c, f, x, y + math.sin(t * 7 + i) * 4, height=hh,
            flip=math.cos(a) < 0, anchor="bottom")


def rank_horde(c, t, rows, speed=1.0, seedbase=3):
    """가로로 늘어선 떼. 2번 컷의 '대군단'처럼 벽으로 보여야 할 때 쓴다."""
    fr = zwalk()
    r = random.Random(seedbase)
    for ri, (y, hgt, spd, n) in enumerate(rows):
        for i in range(n):
            ph = r.random()
            x = (40 + ((i + 0.5) / n) * (W - 80)
                 + math.sin(t * 1.5 + i * 2.1) * 34
                 + (ph - 0.5) * (W / n) * 0.9)          # 칸 안에서 좌우로 흩뜨린다
            yy = y + ((t * spd * speed + ph * 300) % 200) - 100 + (ph - 0.5) * 46
            put(c, fr[int(t * 11 * speed + i * 3 + ri) % 8], x, yy,
                height=hgt, flip=(i % 2 == 0), anchor="bottom")


# =============================================================================
#  컷 1. 컴스톡이 등장 (0.0 ~ 2.4)
# =============================================================================

LAND = 0.52


def scene_arrive(t, dur):
    c = battle_bg(0.0, scroll=0, tint=(26, 22, 62), bright=0.66, mix=0.46)
    d = ImageDraw.Draw(c)
    gx, gy = W / 2, H * 0.80

    if t < LAND:
        k = t / LAND
        rw = 90 + 220 * k
        d.ellipse([gx - rw, gy - rw * 0.2, gx + rw, gy + rw * 0.2],
                  fill=(0, 0, 0, int(120 + 90 * k)))
        # 낙하 궤적은 화면 안에서 보여야 한다(ease_in=k**3은 착지 직전에야 들어온다)
        put(c, img("Comstock.png"), gx, -180 + (gy + 180) * (k ** 1.2),
            height=430, anchor="bottom", alpha=235)
        darken(c, 0.28)
    else:
        lt = t - LAND
        sq = 1.0 + 0.22 * math.exp(-lt * 13) * math.cos(lt * 26)
        dust = seq("RollDust", "굵은{:03d}.png", 3, start=1)
        if lt < 0.55:
            g = lt / 0.55
            for s in (-1, 1):
                put(c, dust[min(2, int(g * 3))], gx + s * (70 + 340 * g), gy - 24,
                    height=170 + 130 * g, flip=(s < 0), alpha=int(230 * (1 - g)))
            rr = 70 + 700 * ease_out(g)
            d.ellipse([gx - rr, gy - rr * 0.24, gx + rr, gy + rr * 0.24],
                      outline=(255, 250, 230, int(210 * (1 - g))),
                      width=int(18 * (1 - g)) + 2)
        reveal = clamp(lt / 0.7)
        rays(c, lt, gx, gy - 320, n=24, speed=0.42,
             color=(255, 238, 196), alpha=int(46 * reveal))
        put(c, img("Comstock.png"), gx, gy + 6,
            size=(430 * 1.725 / sq, 430 * sq), anchor="bottom")
        sparks(c, lt, 4, gx, gy - 220, 300, n=14, size=26)
        darken(c, 0.28 * (1 - reveal))
        if lt < 0.16:
            flash(c, (0.16 - lt) / 0.16 * 0.85)

    # 낙하 중에는 거의 당기지 않는다(당기면 위에서 내려오는 몸이 잘린다).
    # 착지 순간 확 당겼다가 끝까지 천천히 풀어 주는 게 곧 충격이다.
    if t < LAND:
        return camera(c, 1.04, gx, H * 0.5)
    c = camera(c, lerp(1.22, 1.01, ease_in_out((t - LAND) / (dur - LAND))), gx, gy - 210)

    # **자막은 카메라를 적용한 뒤에 그린다.** 카메라 안에서 그리면 줌을 따라
    # 같이 커졌다 작아지고, 하단 자막은 화면 밖으로 밀려 잘린다.
    lt = t - LAND
    if lt > 0.42:
        scrim(c, clamp((lt - 0.42) / 0.3))
        p = ease_out_back(clamp((lt - 0.42) / 0.28), 2.0)
        put(c, text_img("COMSTOCK", font(FONT_EN, 86), stroke=13),
            W / 2, H - 176, scale=p)
        if lt > 0.72:
            caption(c, "캐주얼 뱀서라이크 · 로봇 조립", H - 92, size=40,
                    grad=CYAN, pop=ease_out_back(clamp((lt - 0.72) / 0.24), 2.0))
    return c


# =============================================================================
#  컷 2. 좀비가 뒤돌아보고, 대군단이 온다 (2.4 ~ 5.0)
# =============================================================================

TURN = 0.62


def scene_spotted(t, dur):
    c = battle_bg(t * 8, scroll=1, tint=(64, 26, 34), bright=0.90, mix=0.30)
    d = ImageDraw.Draw(c)
    gy = H * 0.80

    # 뒤에서 밀려오는 대군단 — 화면 밖 좌표로 깔아 두고 카메라로 드러낸다
    if t > TURN:
        g = clamp((t - TURN) / 1.3)
        rank_horde(c, t, [(-180 + 240 * g, 120, 100, 11), (-30 + 230 * g, 148, 92, 10),
                          (170 + 210 * g, 182, 84, 9), (400 + 180 * g, 222, 76, 8),
                          (660 + 140 * g, 268, 68, 7)], speed=1.0)

    # 주인공 좀비 — flip 이 뒤집히는 순간이 '뒤돌아봤다'로 읽힌다
    zx, zy = W * 0.5 + 30, gy - 20
    turned = t >= TURN
    wob = 0.0 if not turned else math.exp(-(t - TURN) * 9) * math.sin((t - TURN) * 40) * 8
    zf = zwalk()[int(t * 9) % 8] if not turned else img("Zombie.png")
    put(c, zf, zx, zy, height=430, flip=not turned, anchor="bottom", angle=wob)

    if turned and t < TURN + 0.5:
        g = clamp((t - TURN) / 0.16)
        bx, by = zx + 190, zy - 420
        rr = 80 * ease_out_back(g, 3.4)
        d.ellipse([bx - rr, by - rr, bx + rr, by + rr], fill=(255, 255, 255, 245),
                  outline=(24, 18, 30, 255), width=9)
        d.polygon([(bx - 34, by + rr - 14), (bx - 4, by + rr + 54), (bx + 30, by + rr - 20)],
                  fill=(255, 255, 255, 245))
        put(c, text_img("!", font(FONT_EN, 80), grad=FIRE, stroke=9), bx, by,
            scale=ease_out_back(g, 3.4))
        for i in range(10):
            a = (i / 10) * math.tau + 0.3
            r0, r1 = 250 + 50 * g, 350 + 120 * g
            d.line([zx + math.cos(a) * r0, zy - 210 + math.sin(a) * r0,
                    zx + math.cos(a) * r1, zy - 210 + math.sin(a) * r1],
                   fill=(255, 240, 190, int(210 * (1 - g))), width=10)

    # 카메라가 좀비 얼굴에 바짝 붙어 있어서, 컴스톡을 화면 중앙에 두면 좀비를 가린다.
    # 좌하단 전경으로 비켜 세워 "좀비가 보고 있는 대상"으로만 읽히게 한다.
    put(c, img("Comstock.png"), W * 0.24, H + 120, height=380, anchor="bottom")

    if t < TURN:
        z = lerp(1.50, 1.68, ease_in_out(t / TURN))
    else:
        z = lerp(1.88, 1.0, ease_in_out(clamp((t - TURN) / 1.5)))
    c = camera(c, z, zx, zy - 220)

    # 자막은 카메라 뒤에 — 화면 좌표에 고정된다
    if t > 1.45:
        scrim(c, clamp((t - 1.45) / 0.25))
        caption(c, "화면을 가득 채우는  좀비 떼", H - 132, size=62,
                pop=ease_out_back(clamp((t - 1.45) / 0.22), 2.0))
        if t > 1.9:
            caption(c, "웨이브 20 · 회당 60초", H - 62, size=36, grad=CYAN,
                    pop=ease_out_back(clamp((t - 1.9) / 0.22), 2.0))
    return c


# =============================================================================
#  컷 3. 컴스톡이 잡으면서 진행 (5.0 ~ 7.6)
# =============================================================================

BOOMS = [(0.16, 470, 560), (0.44, 1380, 700), (0.78, 330, 800), (1.10, 1560, 470),
         (1.42, 760, 640), (1.74, 1210, 830), (2.04, 420, 520), (2.32, 1450, 760)]


def scene_push(t, dur):
    c = battle_bg(t, scroll=380, tint=(44, 32, 78), bright=0.86, mix=0.28)
    px, py = W / 2 + math.sin(t * 2.2) * 30, H * 0.72

    swarm(c, t, px, py - 60, count=46, seedbase=11, speed=1.5, hgt=(84, 176))

    expl = seq("Explosion", "frame_{:02d}.png", 10, start=1)
    for st, ex, ey in BOOMS:
        if st <= t < st + 0.4:
            put(c, expl[min(9, int((t - st) / 0.04))], ex, ey, height=300)

    put(c, img("Comstock.png"), px, py + abs(math.sin(t * 9)) * -10,
        height=380, anchor="bottom")

    mz = seq("MuzzleFlash", "frame_{:02d}.png", 3, start=1)
    if int(t * 22) % 2 == 0:
        put(c, mz[int(t * 26) % 3], px + 156, py - 214, height=130)
        put(c, mz[int(t * 21) % 3], px - 150, py - 206, height=108, flip=True)

    # 자동 공격 — 사방으로 뿌린다
    for i in range(26):
        a = (i / 26) * math.tau + t * 1.1
        bt = (t * 2.4 + i * 0.09) % 1.0
        rr = 120 + bt * 980
        put(c, img("BasicBullet.png"), px + math.cos(a) * rr,
            py - 190 + math.sin(a) * rr * 0.6, height=22,
            angle=-math.degrees(a), alpha=int(255 * (1 - bt * 0.5)))

    damage_numbers(c, t, 41, n=13, period=0.5, lo=600, hi=18_000)
    xp_gems(c, t, 61, px, py - 150, n=16)

    scrim(c, 1.0)
    if t > 0.25:
        caption(c, "가만히 서 있어도  알아서 쏜다", H - 132, size=62,
                pop=ease_out_back(clamp((t - 0.25) / 0.22), 2.0))
    if t > 1.35:
        caption(c, "자동 조준 · 자동 공격 · 재장전 없음", H - 62, size=36, grad=CYAN,
                pop=ease_out_back(clamp((t - 1.35) / 0.22), 2.0))
    return c


# =============================================================================
#  컷 4. 게임으로 전환 → 게임플레이 (7.6 ~ 10.4)
# =============================================================================

def scene_gameplay(t, dur):
    c = battle_bg(t + 3, scroll=70, tint=(26, 52, 108), bright=0.94, mix=0.24)
    px, py = W / 2 + math.sin(t * 1.8) * 90, H * 0.70

    swarm(c, t + 4, px, py - 60, count=52, seedbase=17, speed=1.2, hgt=(78, 168))

    put(c, img("Comstock.png"), px, py, height=340, anchor="bottom")
    mz = seq("MuzzleFlash", "frame_{:02d}.png", 3, start=1)
    if int(t * 20) % 2 == 0:
        put(c, mz[int(t * 24) % 3], px + 140, py - 192, height=118)
        put(c, mz[int(t * 18) % 3], px - 136, py - 186, height=98, flip=True)

    for i in range(20):
        a = (i / 20) * math.tau - t * 0.9
        bt = (t * 2.1 + i * 0.11) % 1.0
        rr = 110 + bt * 900
        put(c, img("BasicBullet.png"), px + math.cos(a) * rr,
            py - 170 + math.sin(a) * rr * 0.6, height=20, angle=-math.degrees(a))

    expl = seq("Explosion", "frame_{:02d}.png", 10, start=1)
    for st, ex, ey in ((0.5, 520, 620), (1.1, 1420, 760), (2.0, 700, 500)):
        if st <= t < st + 0.4:
            put(c, expl[min(9, int((t - st) / 0.04))], ex, ey, height=260)

    damage_numbers(c, t, 53, n=10, period=0.55, lo=400, hi=9_000)
    xp_gems(c, t, 71, px, py - 140, n=18)

    # ── 게임으로 '전환' — HUD가 페이드로 얹히며 게임 화면이 된다
    draw_hud(c, t, hp=0.78, level=6 + int(t), wave=7, secs=47 - int(t * 4),
             gold=1240 + int(t * 260), alpha=clamp(t / 0.4))

    # 레벨업 → AI 코어 3택
    if 1.45 < t < 2.35:
        g = clamp((t - 1.45) / 0.18)
        darken(c, 0.55 * g)
        lu = seq("LevelUpEffect", "level-up_{:03d}.png", 24, start=1)
        put(c, lu[min(23, int((t - 1.45) * 26))], px, py - 130, height=520, alpha=210)
        put(c, text_img("LEVEL UP", font(FONT_EN, 74), grad=AMBER, stroke=12),
            W / 2, 250, scale=ease_out_back(g, 2.4))
        for i, (label, panel) in enumerate((
                ("공격력\n+12%", "UI/Grade/Gold/Gold_Panel00.png"),
                ("공격속도\n+9%", "UI/Grade/Purple/Purple_Panel00.png"),
                ("이동속도\n+7%", "UI/Grade/Blue/Blue_Panel00.png"))):
            st2 = 1.52 + i * 0.08
            if t < st2:
                continue
            p = ease_out_back(clamp((t - st2) / 0.2), 2.4)
            x = W / 2 + (i - 1) * 420
            put(c, img(panel), x, H * 0.56, size=(360 * p, 470 * p))
            put(c, text_img(label, font(FONT_KR, 50), stroke=10, spacing=14),
                x, H * 0.56, scale=p)
        scrim(c, 1.0)
        caption(c, "레벨업마다  3장 중 1장", H - 92, size=48, grad=CYAN,
                pop=ease_out_back(clamp((t - 1.72) / 0.2), 2.0))
    elif t > 0.45:
        scrim(c, clamp((t - 0.45) / 0.25))
        caption(c, "쓰러뜨릴수록  강해진다", H - 132, size=62,
                pop=ease_out_back(clamp((t - 0.45) / 0.22), 2.0))
        if t > 0.85:
            caption(c, "경험치 · 골드 · 부품 상자", H - 62, size=36, grad=CYAN,
                    pop=ease_out_back(clamp((t - 0.85) / 0.22), 2.0))
    return c


# =============================================================================
#  컷 5. 장비 전환 + 각 컴스톡들 → 합격 (10.4 ~ 13.4)
# =============================================================================

GEAR_END = 1.30
HEAD_END = 2.42


def roster_strip(c, t, current):
    """하단에 12칸 로스터를 깔고 현재 칸을 밝힌다. 캐릭터 선택 화면의 관용구라
    '고를 수 있다'가 한눈에 읽힌다."""
    d = ImageDraw.Draw(c)
    n = len(VARIANTS)
    bw, gap = 106, 14
    total = n * bw + (n - 1) * gap
    x0 = W / 2 - total / 2
    for i, (head, _) in enumerate(VARIANTS):
        x = x0 + i * (bw + gap) + bw / 2
        on = (i == current)
        d.rounded_rectangle([x - bw / 2, H - 176, x + bw / 2, H - 70], radius=14,
                            fill=(48, 38, 82, 232) if not on else (104, 76, 176, 246),
                            outline=(255, 208, 96, 255) if on else (150, 140, 190, 130),
                            width=5 if on else 3)
        put(c, img(f"Heads/{head}"), x, H - 123, box=(74, 74),
            alpha=255 if on else 196)


def stamp(c, t, text="합격"):
    """도장이 쿵 찍힌다. 위에서 크게 내려와 순식간에 제 크기가 된다."""
    k = clamp(t / 0.17)
    s = lerp(3.2, 1.0, ease_in(k))
    ang = lerp(-32, -13, ease_out(clamp(t / 0.3)))
    R = 208
    layer = Image.new("RGBA", (R * 2 + 40, R * 2 + 40), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx = cy = R + 20
    col = (214, 34, 44, 238)
    d.ellipse([cx - R, cy - R, cx + R, cy + R], outline=col, width=22)
    d.ellipse([cx - R + 40, cy - R + 40, cx + R - 40, cy + R - 40], outline=col, width=9)
    f = font(FONT_KR, 118)
    bb = d.textbbox((0, 0), text, font=f)
    d.text((cx - (bb[2] - bb[0]) / 2 - bb[0], cy - (bb[3] - bb[1]) / 2 - bb[1]),
           text, font=f, fill=col)
    put(c, layer, W * 0.735, H * 0.40, scale=s, angle=ang,
        alpha=int(255 * clamp(t / 0.06)))
    if t < 0.2:
        flash(c, (0.2 - t) / 0.2 * 0.5)


def scene_loadout(t, dur):
    c = stage_bg(t * 0.5, top=(30, 26, 72), bottom=(12, 86, 138), ray_alpha=32)
    sparks(c, t, 21, W / 2, H * 0.44, 620, n=16, size=24)

    if t < GEAR_END:
        step = int(t / 0.21) % len(LOADOUTS)
        wl, wr, wname = LOADOUTS[step]
        swap = (t / 0.21) % 1.0
        r = build_robot("Heads/ComstockMk01.png", wl, wr)
        put(c, r, W / 2, H * 0.45, height=900 * (1.0 + 0.03 * math.sin(swap * math.pi)))
        if swap < 0.22:
            flash(c, (0.22 - swap) / 0.22 * 0.4)
        put(c, text_img("장비 전환", font(FONT_KR, 66), stroke=11), W / 2, 130,
            scale=ease_out_back(clamp(t / 0.2), 2.0))
        scrim(c, 1.0)
        caption(c, wname, H - 132, size=58, grad=AMBER)
        caption(c, "무기 65종 · 파츠 134개 · 등급 5단계", H - 62, size=36, grad=CYAN)

    elif t < HEAD_END:
        lt = t - GEAR_END
        step = min(len(VARIANTS) - 1, int(lt / 0.093))
        head, hname = VARIANTS[step]
        wl, wr, _ = LOADOUTS[step % len(LOADOUTS)]
        swap = (lt / 0.093) % 1.0
        dx = (1 - ease_out(clamp(swap / 0.45))) * 150
        put(c, build_robot(f"Heads/{head}", wl, wr), W / 2 + dx, H * 0.41,
            height=820, alpha=int(255 * clamp(swap / 0.2 + .35)))
        put(c, text_img("각 컴스톡", font(FONT_KR, 66), stroke=11), W / 2, 130)
        roster_strip(c, lt, step)
        scrim(c, 0.75)
        caption(c, hname, H - 42, size=46, grad=AMBER)

    else:
        lt = t - HEAD_END
        put(c, build_robot("Heads/Berserker.png", "LeftPlasmaCannon.png",
                           "RightPlasmaCannon.png"), W / 2, H * 0.41, height=820)
        put(c, text_img("각 컴스톡", font(FONT_KR, 66), stroke=11), W / 2, 130)
        roster_strip(c, lt, 2)
        stamp(c, lt)
        scrim(c, 0.75)
        caption(c, "골라서 조립한다 · 로봇 12종", H - 42, size=46,
                pop=ease_out_back(clamp((lt - 0.18) / 0.2), 2.0))
    return c


# =============================================================================
#  컷 6. 게임 로고 (13.4 ~ 15.0)
# =============================================================================

def scene_logo(t, dur):
    c = stage_bg(t * 0.45, top=(42, 22, 86), bottom=(178, 88, 30), ray_alpha=44)
    sparks(c, t, 31, W / 2, H * 0.42, 560, n=18, size=28)

    k = clamp(t / 0.28)
    logo = text_img("COMSTOCK", font(FONT_EN, 124),
                    grad=GOLD, stroke=17, stroke_fill=(26, 16, 10))
    if t <= 0.28:
        put(c, logo, W / 2, H * 0.36 + (1 - ease_out(k)) * -420)
    else:
        sq = 1.0 + 0.08 * math.exp(-(t - 0.28) * 9) * math.cos((t - 0.28) * 24)
        put(c, logo, W / 2, H * 0.36, size=(logo.width * sq, logo.height / sq))
    if 0.26 < t < 0.5:
        flash(c, (0.5 - t) / 0.24 * 0.75)

    if t > 0.32:
        put(c, text_img("컴 스 톡", font(FONT_KR, 64), stroke=11), W / 2, H * 0.50,
            scale=ease_out_back(clamp((t - 0.32) / 0.2), 2.2))
    if t > 0.5:
        put(c, text_img("웨이브 서바이벌 · 로봇 조립 · 캐주얼 뱀서라이크",
                        font(FONT_KR, 38), grad=CYAN, stroke=8),
            W / 2, H * 0.585, scale=ease_out_back(clamp((t - 0.5) / 0.2), 2.0))

    put(c, img("Comstock.png"), W / 2, H - 60, height=300, anchor="bottom")

    if t > 0.78:
        p = ease_out_back(clamp((t - .78) / .22), 2.4)
        d = ImageDraw.Draw(c)
        bw, bh = 620 * p, 108 * p
        d.rounded_rectangle([W / 2 - bw / 2, H * 0.755 - bh / 2,
                             W / 2 + bw / 2, H * 0.755 + bh / 2], radius=16,
                            fill=(24, 18, 40, 230), outline=(255, 208, 96, 240), width=5)
        put(c, text_img("STEAM  위시리스트에 추가", font(FONT_KR, 42),
                        grad=AMBER, stroke=9), W / 2, H * 0.755, scale=p)
    return c


# =============================================================================
#  타임라인 — 사용자 콘티 6컷
# =============================================================================

TIMELINE = [
    (0.0,  2.4, scene_arrive,   "1. 컴스톡 등장"),
    (2.4,  2.6, scene_spotted,  "2. 좀비가 뒤돌아본다 → 대군단"),
    (5.0,  2.6, scene_push,     "3. 잡으면서 진행"),
    (7.6,  2.8, scene_gameplay, "4. 게임으로 전환 → 게임플레이"),
    (10.4, 3.0, scene_loadout,  "5. 장비전환 · 각 컴스톡 · 합격"),
    (13.4, 1.6, scene_logo,     "6. 게임 로고"),
]

CUTS = [s for s, _, _, _ in TIMELINE[1:]]
# 임팩트 — 광고 톤보다 훨씬 아껴서 쓴다(트레일러는 화면이 흔들릴수록 싸 보인다)
HITS = [0.52, 3.02, 5.00, 9.05, 12.82, 13.40]


def render_content(t):
    for st, dur, fn, _ in TIMELINE:
        if st <= t < st + dur:
            return fn(t - st, dur)
    last = TIMELINE[-1]
    return last[2](last[1] - 1e-3, last[1])


# =============================================================================
#  후처리
# =============================================================================

def punch(t):
    z = 0.0
    for h in HITS:
        d = t - h
        if 0 <= d < 0.22:
            z = max(z, 0.035 * (1 - d / 0.22) ** 2)
    return z


def shake(t):
    for h in HITS:
        d = t - h
        if 0 <= d < 0.16:
            a = 1 - d / 0.16
            return (math.sin(d * 150) * 16 * a, math.cos(d * 173) * 12 * a)
    return (0.0, 0.0)


def cut_flash(t):
    for cut in CUTS:
        d = t - cut
        if 0 <= d < 0.10:
            return 0.38 * (1 - d / 0.10)
    return 0.0


_vig = None


def vignette():
    global _vig
    if _vig is None:
        yy, xx = np.mgrid[0:H, 0:W]
        nx, ny = (xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)
        r = np.sqrt(nx * nx * 0.78 + ny * ny)
        _vig = (1.0 - 0.28 * np.clip((r - 0.58) / 0.9, 0, 1) ** 1.6).astype(np.float32)
    return _vig


def post(rgba, t):
    z = 1.0 + punch(t)
    dx, dy = shake(t)
    if z > 1.001 or dx or dy:
        bw, bh = int(W * z * 1.025), int(H * z * 1.025)
        big = rgba.resize((bw, bh), Image.BILINEAR)
        ox = max(0, min(bw - W, int((bw - W) / 2 + dx)))
        oy = max(0, min(bh - H, int((bh - H) / 2 + dy)))
        rgba = big.crop((ox, oy, ox + W, oy + H))

    arr = np.asarray(rgba.convert("RGB"), dtype=np.float32)
    luma = (arr[..., 0] * .299 + arr[..., 1] * .587 + arr[..., 2] * .114)[..., None]
    arr = np.clip(luma + (arr - luma) * 1.18, 0, 255)      # 광고 톤보다 절제한 채도
    arr = np.clip((arr - 128.0) * 1.05 + 131.0, 0, 255)

    bright = np.clip((arr - 186.0) * 2.0, 0, 255).astype(np.uint8)
    bl = Image.fromarray(bright, "RGB").resize((W // 4, H // 4), Image.BILINEAR)
    bl = bl.filter(ImageFilter.GaussianBlur(10)).resize((W, H), Image.BILINEAR)
    b = np.asarray(bl, dtype=np.float32) * 0.44
    arr = 255.0 - (255.0 - arr) * (255.0 - b) / 255.0
    arr *= vignette()[..., None]

    f = cut_flash(t)
    if f > 0:
        arr = arr + (255.0 - arr) * f
    return np.clip(arr, 0, 255).astype(np.uint8)


def compose_frame(t):
    return post(render_content(t), t)


# =============================================================================
#  오디오
# =============================================================================

SFX_CUES = [
    (0.52, "SFX/Weapon_Explosive.wav", 0.95),
    (2.98, "SFX/Enemy_Hit_A.wav", 0.85),
    (3.30, "SFX/Enemy_Death.wav", 0.65),
    (5.02, "SFX/Weapon_RapidFire.wav", 0.9),
    (5.24, "SFX/Enemy_Hit_B.wav", 0.75),
    (5.52, "SFX/Weapon_Explosive.wav", 0.8),
    (5.94, "SFX/Enemy_Hit_C.ogg", 0.75),
    (6.28, "SFX/Weapon_Shotgun.wav", 0.8),
    (6.60, "SFX/Enemy_Death.wav", 0.75),
    (7.02, "SFX/Weapon_PlasmaCannon.wav", 0.8),
    (7.62, "SFX/UI_Click.wav", 0.8),
    (8.20, "SFX/Weapon_RapidFire.wav", 0.75),
    (8.72, "SFX/Weapon_Explosive.wav", 0.75),
    (9.05, "SFX/LevelUp.wav", 1.0),
    (9.70, "SFX/UI_Click.wav", 0.6),
    (10.42, "SFX/UI_Click.wav", 0.8),
    (10.63, "SFX/UI_Click.wav", 0.7),
    (10.84, "SFX/UI_Click.wav", 0.7),
    (11.05, "SFX/UI_Click.wav", 0.7),
    (11.70, "SFX/UI_Click.wav", 0.65),
    (12.10, "SFX/UI_Click.wav", 0.65),
    (12.82, "SFX/Boss_Hit_A.wav", 0.95),
    (13.42, "SFX/Weapon_Explosive.wav", 0.9),
    (14.20, "SFX/LevelUp.wav", 0.7),
]


def build_audio(ff, out_path):
    inputs, parts, idx = [], [], 0
    inputs += ["-i", os.path.join(RES, "Musics", "Game_BGM01.mp3")]
    parts.append(f"[{idx}:a]atrim=12:{12 + DURATION},asetpts=N/SR/TB,volume=0.66,"
                 f"afade=t=in:st=0:d=0.5,afade=t=out:st={DURATION-0.8}:d=0.8[bgm]")
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
    parts.append("".join(mix) + f"amix=inputs={len(mix)}:duration=first:"
                                "dropout_transition=0:normalize=0[mixed]")
    # 트레일러는 광고처럼 납작하게 밀지 않는다. 살짝만 눌러 -16 LUFS로.
    parts.append("[mixed]bass=g=2:f=110,acompressor=threshold=0.12:ratio=5:"
                 "attack=8:release=140,loudnorm=I=-16:TP=-1.5:LRA=11,"
                 f"atrim=0:{DURATION},asetpts=N/SR/TB,"
                 "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[out]")
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error"] + inputs +
                   ["-filter_complex", ";".join(parts), "-map", "[out]",
                    "-c:a", "aac", "-b:a", "192k", out_path], check=True)


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
         "-c:v", "libx264", "-preset", "slow", "-crf", "20",
         "-maxrate", "9M", "-bufsize", "18M",
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
        render_stills([0.30, 0.75, 1.60, 2.30, 2.85, 3.20, 3.90, 4.70,
                       5.40, 6.30, 7.30, 7.95, 8.60, 9.40, 10.10,
                       10.80, 11.50, 12.20, 12.70, 13.20, 13.90, 14.60])
    else:
        render_video(args.out)


if __name__ == "__main__":
    main()
