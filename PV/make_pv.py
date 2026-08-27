#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
컴스톡(Comstock) 게임 PV 렌더러
================================

1950년대 미국 흑백 TV 광고를 흉내 낸 30초 게임 소개 영상을 만든다.
게임에 실제로 들어 있는 스프라이트(`Assets/Resources/`)만 재료로 쓰고,
프레임을 한 장씩 합성한 뒤 ffmpeg로 H.264 MP4를 뽑는다.

    python3 PV/make_pv.py            # 전체 영상 렌더 (PV/out/comstock_pv.mp4)
    python3 PV/make_pv.py --stills   # 확인용 정지 프레임만 뽑기
    python3 PV/make_pv.py --preview 12.5   # 특정 시각 한 장만 뽑기

구조
----
- 내용(콘텐츠) 화면은 4:3(960x720)으로 그린다. 옛날 TV가 4:3이기 때문이다.
- 그 위에 CRT 처리(흑백 변환·주사선·노이즈·수직 흔들림·비네팅·먼지)를 얹는다.
- 마지막에 16:9(1280x720) 검은 배경 가운데에 얹어 필러박스를 만든다.

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

FPS = 24
DURATION = 30.0
TOTAL_FRAMES = int(round(FPS * DURATION))

CW, CH = 960, 720          # 콘텐츠(4:3) 해상도
OW, OH = 1280, 720         # 최종 출력(16:9) 해상도
SCREEN_X = (OW - CW) // 2  # 필러박스 좌측 여백

FONT_KR = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Bold.ttf")
FONT_KR_R = os.path.join(FONT_DIR, "NotoSansKR", "NotoSansKR-Regular.ttf")
FONT_EN = os.path.join(FONT_DIR, "Orbitron", "Orbitron-Black.ttf")

# 재현성 (매번 같은 지직거림이 나오도록)
SEED = 19540720


# ------------------------------------------------------------------- 캐시류

_img_cache = {}
_scaled_cache = {}
_font_cache = {}


def img(rel, crop=True):
    """`Assets/Resources/` 기준 상대경로로 RGBA 스프라이트를 읽는다.

    crop=True면 알파 bbox로 잘라내므로, 배치할 때 '그림의 실제 크기'를 기준으로
    좌표를 잡을 수 있다(원본 캔버스의 빈 여백에 위치가 휘둘리지 않는다)."""
    key = (rel, crop)
    if key in _img_cache:
        return _img_cache[key]
    path = os.path.join(RES, rel)
    im = Image.open(path).convert("RGBA")
    if crop:
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
    _img_cache[key] = im
    return im


def seq(folder, pattern, count, start=0):
    """연속 프레임 폴더를 리스트로 읽는다.

    **프레임마다 따로 bbox를 자르면 안 된다.** 걷기/포효처럼 실루엣이 변하는
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
    """크기 변경 결과를 캐시한다(프레임마다 리사이즈하면 느리다)."""
    w = max(1, int(round(w)))
    h = max(1, int(round(h)))
    key = (id(im), w, h, flip)
    if key in _scaled_cache:
        return _scaled_cache[key]
    out = im.resize((w, h), Image.LANCZOS)
    if flip:
        out = out.transpose(Image.FLIP_LEFT_RIGHT)
    if len(_scaled_cache) > 4000:
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


def ease_out_back(t, s=2.4):
    t = clamp(t) - 1.0
    return t * t * ((s + 1) * t + s) + 1.0


def pulse(t, period, duty=0.5):
    """0~1 구간에서 duty 비율만큼 1이 되는 사각파(광고식 깜빡임용)."""
    return 1.0 if (t % period) / period < duty else 0.0


# ---------------------------------------------------------------- 그리기 도구

def put(canvas, sprite, cx, cy, height=None, width=None, scale=None, box=None,
        flip=False, angle=0.0, alpha=255, anchor="center"):
    """스프라이트를 캔버스에 얹는다. 크기는 height/width/scale/box 중 하나로 지정.

    `box=(w, h)`는 그 사각형 **안에 들어가도록** 비율을 유지한 채 맞춘다.
    가로로 납작한 그림(예: 달리는 스프린터)을 세로 기준으로만 키우면
    화면 밖으로 삐져나가 뭉개지므로, 몬스터 클로즈업에는 box를 쓴다."""
    sw, sh = sprite.size
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


def text_img(text, fnt, fill=(255, 255, 255, 255), stroke=0,
             stroke_fill=(0, 0, 0, 255), spacing=8, align="center"):
    """글자를 자체 RGBA 이미지로 그려서 돌려준다(확대/회전/팝인용)."""
    pad = stroke * 2 + 24
    probe = Image.new("RGBA", (8, 8))
    d = ImageDraw.Draw(probe)
    bb = d.multiline_textbbox((0, 0), text, font=fnt, spacing=spacing,
                              align=align, stroke_width=stroke)
    w = int(math.ceil(bb[2] - bb[0])) + pad * 2
    h = int(math.ceil(bb[3] - bb[1])) + pad * 2
    out = Image.new("RGBA", (max(1, w), max(1, h)), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    d.multiline_text((pad - bb[0], pad - bb[1]), text, font=fnt, fill=fill,
                     spacing=spacing, align=align, stroke_width=stroke,
                     stroke_fill=stroke_fill)
    bb2 = out.getbbox()
    return out.crop(bb2) if bb2 else out


def caption(canvas, text, cy, size=62, pop=1.0, angle=0.0, kr=True,
            fill=(255, 255, 255, 255), stroke=None, alpha=255, cx=None,
            spacing=10, banner=False):
    """미국 광고식 굵은 자막 한 덩어리."""
    fnt = font(FONT_KR if kr else FONT_EN, size)
    st = stroke if stroke is not None else max(4, size // 9)
    im = text_img(text, fnt, fill=fill, stroke=st, spacing=spacing)
    cx = CW / 2 if cx is None else cx
    if banner:
        bw = im.width + 46
        bh = im.height + 26
        bar = Image.new("RGBA", (int(bw * pop), int(bh * pop)), (0, 0, 0, 205))
        canvas.alpha_composite(bar, (int(cx - bar.width / 2), int(cy - bar.height / 2)))
    put(canvas, im, cx, cy, scale=pop, angle=angle, alpha=alpha)


def flash(canvas, amount):
    """화면 전체 흰색 플래시."""
    if amount <= 0:
        return
    ov = Image.new("RGBA", (CW, CH), (255, 255, 255, int(255 * clamp(amount))))
    canvas.alpha_composite(ov)


def darken(canvas, amount):
    if amount <= 0:
        return
    ov = Image.new("RGBA", (CW, CH), (0, 0, 0, int(255 * clamp(amount))))
    canvas.alpha_composite(ov)


# -------------------------------------------------------------- 배경 만들기

_bg_master = None


def bg_tile(offset_x, brightness=1.0):
    """폐허 도시 배경을 가로로 스크롤시켜 한 장 만든다."""
    global _bg_master
    if _bg_master is None:
        src = Image.open(os.path.join(RES, "ground_ruined_city_v2_tile.png")).convert("RGB")
        # 4:3 화면 높이에 맞춰 줄이고, 가로로 이어붙일 수 있게 두 장 붙여 둔다
        k = CH / src.height
        one = src.resize((int(src.width * k), CH), Image.LANCZOS)
        m = Image.new("RGB", (one.width * 2, CH))
        m.paste(one, (0, 0))
        m.paste(one.transpose(Image.FLIP_LEFT_RIGHT), (one.width, 0))
        _bg_master = m
    m = _bg_master
    ox = int(offset_x) % (m.width // 2)
    out = m.crop((ox, 0, ox + CW, CH)).convert("RGBA")
    if brightness != 1.0:
        out = Image.eval(out, lambda v: min(255, int(v * brightness)))
        out.putalpha(255)
    return out


def new_canvas(fillv=0):
    return Image.new("RGBA", (CW, CH), (fillv, fillv, fillv, 255))


# =============================================================================
#  장면(Scene)들
# =============================================================================
#
#  각 함수는 (t, dur)를 받아 콘텐츠 캔버스(RGBA 960x720)를 돌려준다.
#  t는 그 장면 안에서 흐른 초, dur은 장면 길이.

RNG = random.Random(SEED)


# --- 1. TV 켜짐 + 방송 대기 (0.0 ~ 1.8) -------------------------------------

def scene_signon(t, dur):
    c = new_canvas(0)
    d = ImageDraw.Draw(c)

    if t < 0.30:
        # CRT 전원: 가운데 가로선이 위아래로 펼쳐진다
        k = ease_out(t / 0.30)
        h = max(2, int(CH * k))
        y0 = int(CH / 2 - h / 2)
        d.rectangle([0, y0, CW, y0 + h], fill=(255, 255, 255, 255))
        if t < 0.08:
            d.rectangle([0, int(CH / 2 - 3), CW, int(CH / 2 + 3)], fill=(255, 255, 255, 255))
        return c

    # 테스트 패턴 카드
    c = new_canvas(74)
    d = ImageDraw.Draw(c)
    cx, cy = CW / 2, CH / 2 - 96
    r = 178
    for i, rr in enumerate((r, r * 0.74, r * 0.48, r * 0.22)):
        v = 250 if i % 2 == 0 else 24
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], outline=(v, v, v, 255), width=9)
    d.line([cx - r - 66, cy, cx + r + 66, cy], fill=(240, 240, 240, 255), width=5)
    d.line([cx, cy - r - 66, cx, cy + r + 66], fill=(240, 240, 240, 255), width=5)
    # 그레이 스케일 계단
    for i in range(8):
        v = int(255 * i / 7)
        d.rectangle([cx - 168 + i * 42, cy + r + 30, cx - 168 + i * 42 + 40, cy + r + 76],
                    fill=(v, v, v, 255))

    blink = pulse(t, 0.62, 0.62)
    caption(c, "잠 시 후  방 송 을  시 작 합 니 다", CH - 122, size=42,
            alpha=int(150 + 105 * blink), banner=True)
    caption(c, "COMSTOCK BROADCASTING · CH 20", CH - 56, size=24, kr=False, alpha=210)
    return c


# --- 2. 문제 제기: 좀비 (1.8 ~ 4.6) -----------------------------------------

_ZWALK = None
_ZATK = None


def zwalk():
    global _ZWALK
    if _ZWALK is None:
        _ZWALK = seq("ZombieMove", "walk_left_f{}.png", 8)
    return _ZWALK


def zatk():
    global _ZATK
    if _ZATK is None:
        _ZATK = seq("ZombieAttack", "ZombieAttack_{:02d}.png", 12, start=1)
    return _ZATK


ZOMBIE_ROWS = [
    # (y, 화면높이, 속도, 시작위상, 마리수)
    (330, 120, 118, 0.00, 5),
    (430, 165, 152, 0.35, 5),
    (545, 215, 196, 0.70, 4),
    (682, 285, 246, 0.15, 4),
]


def draw_zombie_horde(c, t, speed=1.0, start_off=0.0):
    """좀비 떼가 오른쪽에서 왼쪽으로 몰려온다."""
    fr = zwalk()
    for ri, (y, hgt, spd, ph, n) in enumerate(ZOMBIE_ROWS):
        gap = CW / n * 1.15
        for i in range(n + 2):
            x = CW + 190 - ((t * spd * speed + start_off * spd) + i * gap + ph * 260) % (gap * (n + 2))
            if x < -220 or x > CW + 260:
                continue
            f = fr[int((t * 11 * speed + i * 3 + ri * 2)) % 8]
            bob = math.sin(t * 9 + i * 1.7 + ri) * hgt * 0.02
            put(c, f, x, y + bob, height=hgt, anchor="bottom")


def scene_problem(t, dur):
    c = bg_tile(t * 26, brightness=0.62)
    darken(c, 0.18)
    draw_zombie_horde(c, t)

    # 화면 앞까지 온 한 마리가 카메라를 물어뜯는다
    if t > 1.55:
        k = clamp((t - 1.55) / 0.55)
        a = zatk()[min(11, int((t - 1.55) * 20))]
        put(c, a, CW / 2 + 40, CH + 40, height=520 + 700 * ease_in(k), anchor="bottom")

    if t < 1.75:
        pop = ease_out_back(clamp(t / 0.35))
        caption(c, "혹시…  좀비 때문에\n고민이십니까?", 148, size=68, pop=pop,
                angle=math.sin(t * 3.0) * 1.1)
        if t > 0.9:
            caption(c, "( 네 . )", 300, size=34,
                    alpha=int(255 * pulse(t - 0.9, 0.34, 0.55)))
    else:
        sh = ease_out_back(clamp((t - 1.75) / 0.25))
        caption(c, "으 아 악 !", CH / 2 - 190, size=104, pop=sh,
                angle=math.sin(t * 40) * 4.5)
    return c


# --- 3. 절망 3연타 (4.6 ~ 6.8) ----------------------------------------------

DESPAIR = [
    ("출근길에도  좀비!", "Sprinter.png", "SprinterMove/sprinter_run_{}.png", 8),
    ("퇴근길에도  좀비!", "Charger.png", "ChargerMove/f{:02d}.png", 16),
    ("주말에도  좀비!!", "Leader.png", "LeaderMove/leader_walk_{}.png", 8),
]

_desp_cache = {}


def scene_despair(t, dur):
    step = min(2, int(t / (dur / 3)))
    lt = t - step * (dur / 3)
    label, still, pat, n = DESPAIR[step]

    if step not in _desp_cache:
        start = 1 if "{:02d}" in pat else 0
        folder, fname = pat.split("/")
        _desp_cache[step] = seq(folder, fname, n, start=start)
    frames = _desp_cache[step]

    c = bg_tile(600 + step * 900 + t * 40, brightness=0.55)
    darken(c, 0.22)

    # 배경에 좀비 떼는 계속 흐른다 (주인공 몬스터가 묻히지 않게 한 겹 눌러 둔다)
    draw_zombie_horde(c, t + step * 2.0, speed=1.35)
    darken(c, 0.3)

    f = frames[int(t * 16) % len(frames)]
    zoom = 300 + 210 * ease_out(clamp(lt / 0.45))
    put(c, f, CW / 2 + math.sin(lt * 26) * 12, CH - 56,
        box=(CW * 0.72, zoom), anchor="bottom")

    pop = ease_out_back(clamp(lt / 0.2))
    caption(c, label, 150, size=76, pop=pop, angle=(-2.5 if step % 2 == 0 else 2.5),
            banner=True)
    return c


# --- 4. "그래서!" 전환 (6.8 ~ 8.0) -------------------------------------------

def scene_so(t, dur):
    c = new_canvas(0)
    # 방사형 집중선(옛 만화 광고 관용구)
    d = ImageDraw.Draw(c)
    cx, cy = CW / 2, CH / 2
    rays = 46
    for i in range(rays):
        a = (i / rays) * math.tau + t * 1.1
        wdt = 0.030 + 0.020 * ((i * 7919) % 13) / 13.0
        p1 = (cx + math.cos(a - wdt) * 1400, cy + math.sin(a - wdt) * 1400)
        p2 = (cx + math.cos(a + wdt) * 1400, cy + math.sin(a + wdt) * 1400)
        v = 255 if i % 2 == 0 else 20
        d.polygon([(cx, cy), p1, p2], fill=(v, v, v, 255))

    # 글자가 집중선에 묻히지 않도록 뒤에 검은 판을 깐다
    plate = Image.new("RGBA", (CW, 216), (0, 0, 0, 216))
    c.alpha_composite(plate, (0, int(CH / 2 - 108)))

    # 세 박자로 쿵 쿵 쿵
    words = ["그", "래", "서", "!"]
    beat = 0.24
    for i, w in enumerate(words):
        st = i * beat
        if t < st:
            continue
        k = clamp((t - st) / 0.16)
        xs = CW / 2 + (i - 1.5) * 190
        caption(c, w, CH / 2, size=150, pop=ease_out_back(k, 3.2), cx=xs,
                angle=(-6 if i % 2 == 0 else 6))
    if t > 0.96:
        flash(c, max(0.0, 1.0 - (t - 0.96) / 0.22))
    return c


# --- 5. 제품 등장: 컴스톡 (8.0 ~ 11.2) ---------------------------------------

BADGES = [
    ("자동 조준!", -300, -170, -9),
    ("탄약 무제한!", 305, -215, 8),
    ("재장전 없음!", -320, 130, 7),
    ("무게 제한 있음", 300, 150, -6),
]


def sparkle(c, t, seedbase, cx, cy, rad, n=16):
    d = ImageDraw.Draw(c)
    r = random.Random(seedbase)
    for i in range(n):
        ph = r.random()
        a = r.random() * math.tau
        rr = rad * (0.55 + 0.45 * r.random())
        k = ((t * 1.6 + ph) % 1.0)
        s = math.sin(k * math.pi) * 16
        if s < 1:
            continue
        x = cx + math.cos(a) * rr
        y = cy + math.sin(a) * rr
        d.line([x - s, y, x + s, y], fill=(255, 255, 255, 255), width=3)
        d.line([x, y - s, x, y + s], fill=(255, 255, 255, 255), width=3)


def scene_hero(t, dur):
    c = bg_tile(1800 + t * 18, brightness=1.05)
    # 위쪽을 밝게: 광고의 "after" 톤
    ov = Image.new("RGBA", (CW, CH), (255, 255, 255, 46))
    c.alpha_composite(ov)

    hero = img("Comstock.png")
    k = ease_out_back(clamp(t / 0.5))
    bob = math.sin(t * 3.4) * 12
    hgt = 470 * k
    put(c, hero, CW / 2, CH - 92 + bob, height=hgt, anchor="bottom")
    sparkle(c, t, 7, CW / 2, CH - 300, 260, 18)

    caption(c, "신형 전투 로봇", 108, size=46, pop=ease_out(clamp((t - 0.25) / 0.3)))
    if t > 0.45:
        p = ease_out_back(clamp((t - 0.45) / 0.35))
        caption(c, "컴 스 톡", 196, size=110, pop=p, kr=True,
                angle=math.sin(t * 2.2) * 1.4)

    for i, (label, dx, dy, ang) in enumerate(BADGES):
        st = 1.05 + i * 0.30
        if t < st:
            continue
        p = ease_out_back(clamp((t - st) / 0.24), 3.0)
        wob = math.sin((t - st) * 8) * 2.2
        caption(c, label, CH / 2 + dy, size=44, pop=p, cx=CW / 2 + dx,
                angle=ang + wob, banner=True)
    return c


# --- 6. 무기 카탈로그 (11.2 ~ 15.0) -----------------------------------------

WEAPONS = [
    "RightHMG.png", "RightPlasmaCannon.png", "RightCombatShotgun.png",
    "RightRocketLauncher.png", "ChainsawSword.png", "RightSawedOff.png",
    "RightDMR.png", "Machete.png", "RightLaserPistol.png",
    "RightGiganchong.png", "SurvivalKnife.png", "RightAMR.png",
]

COUNTER_BEATS = [
    # (시작, 라벨, 최종숫자, 접미사)
    (0.00, "전 투 무 기", 65, "종"),
    (1.05, "로 봇 파 츠", 134, "개"),
    (2.05, "디 스 크 · A I 코 어", 5, "등급"),
]


def scene_catalog(t, dur):
    c = new_canvas(18)
    # 회전하는 방사 배경(홈쇼핑 톤)
    d = ImageDraw.Draw(c)
    cx, cy = CW / 2, CH / 2
    for i in range(24):
        a = (i / 24) * math.tau + t * 0.5
        wdt = 0.052
        p1 = (cx + math.cos(a - wdt) * 1400, cy + math.sin(a - wdt) * 1400)
        p2 = (cx + math.cos(a + wdt) * 1400, cy + math.sin(a + wdt) * 1400)
        d.polygon([(cx, cy), p1, p2], fill=(64, 64, 64, 255))

    # 무기들이 순서대로 팡팡 튀어나온다
    slots = [(-1.5, -1), (-0.5, -1), (0.5, -1), (1.5, -1),
             (-1.5, 0), (-0.5, 0), (0.5, 0), (1.5, 0),
             (-1.5, 1), (-0.5, 1), (0.5, 1), (1.5, 1)]
    for i, name in enumerate(WEAPONS):
        st = 0.12 + i * 0.115
        if t < st:
            continue
        p = ease_out_back(clamp((t - st) / 0.22), 3.4)
        gx, gy = slots[i]
        x = CW / 2 + gx * 218
        y = 268 + gy * 156
        spin = math.sin((t - st) * 3.4 + i) * 5
        put(c, img(name), x, y, height=128 * p, angle=spin)

    # 아래쪽은 자막 전용 띠로 비워 둔다(무기가 글자를 덮지 않게)
    band = Image.new("RGBA", (CW, 176), (0, 0, 0, 232))
    c.alpha_composite(band, (0, CH - 176))

    # 숫자 카운터
    for st, label, target, suf in COUNTER_BEATS:
        if t < st:
            continue
        k = clamp((t - st) / 0.55)
        if k < 1.0:
            # 미친 듯이 돌다가 정확한 숫자에 착지
            n = int(target * (0.25 + 0.75 * ease_out(k)) + RNG.randint(0, 40) * (1 - k))
        else:
            n = target
        big = f"{n}{suf}"
        pop = ease_out_back(clamp((t - st) / 0.2), 3.0)
        alpha = 255 if t < st + 1.02 else int(255 * clamp(1 - (t - st - 1.02) / 0.2))
        if alpha <= 4:
            continue
        caption(c, label, CH - 132, size=38, pop=pop, alpha=alpha)
        caption(c, big, CH - 62, size=76, pop=pop, alpha=alpha,
                angle=math.sin(t * 24) * 1.6)

    if t > 3.15:
        # 병맛 한 방: 숫자를 실컷 자랑한 뒤 스스로 김을 뺀다
        k = clamp((t - 3.15) / 0.3)
        caption(c, "전부 임시 수치입니다", CH - 84, size=52,
                pop=ease_out_back(k), angle=-3)
    return c


# --- 7. 머리 12종 (15.0 ~ 17.8) ---------------------------------------------

HEADS = ["ComstockMk01.png", "PrivateComstock.png", "Berserker.png", "Guardman.png",
         "Meteus.png", "HotPot.png", "SodaCan.png", "FanBot.png",
         "HappyPixel.png", "MiniPixie.png", "Pixie.png", "NeonEye_0.png"]


def scene_heads(t, dur):
    c = new_canvas(22)
    d = ImageDraw.Draw(c)
    for y in range(0, CH, 56):
        d.rectangle([0, y, CW, y + 28], fill=(31, 31, 31, 255))

    caption(c, "머리도 골라 쓰십시오", 82, size=52,
            pop=ease_out_back(clamp(t / 0.28)), banner=True)

    cols, rows = 4, 3
    for i, h in enumerate(HEADS):
        st = 0.20 + i * 0.055
        if t < st:
            continue
        p = ease_out_back(clamp((t - st) / 0.2), 3.6)
        gx = (i % cols) - (cols - 1) / 2
        gy = (i // cols) - (rows - 1) / 2
        x = CW / 2 + gx * 214
        y = CH / 2 + 46 + gy * 178
        # 순회 하이라이트
        hi = 1.0 + 0.16 * max(0.0, math.sin(t * 5.5 - i * 0.55))
        put(c, img(f"Heads/{h}"), x, y, height=126 * p * hi)

    if t > 1.85:
        # 마지막에 '음료수 캔' 클로즈업
        k = ease_out(clamp((t - 1.85) / 0.32))
        darken(c, 0.72 * k)
        put(c, img("Heads/SodaCan.png"), CW / 2, CH / 2 - 20,
            height=170 + 300 * k, angle=math.sin(t * 7) * 3)
        if t > 2.15:
            caption(c, "…이것도 로봇입니다", CH - 132, size=58,
                    pop=ease_out_back(clamp((t - 2.15) / 0.22)), banner=True)
    return c


# --- 8. 전투 몽타주 (17.8 ~ 21.4) -------------------------------------------

_EXPL = None
_MUZZ = None


def expl():
    global _EXPL
    if _EXPL is None:
        _EXPL = seq("Explosion", "frame_{:02d}.png", 10, start=1)
    return _EXPL


def muzz():
    global _MUZZ
    if _MUZZ is None:
        _MUZZ = seq("MuzzleFlash", "frame_{:02d}.png", 3, start=1)
    return _MUZZ


BOOMS = [(0.35, 300, 300), (0.72, 690, 380), (1.15, 175, 470), (1.62, 780, 250),
         (2.05, 430, 350), (2.48, 640, 520), (2.90, 250, 250), (3.25, 560, 420)]


def scene_combat(t, dur):
    c = bg_tile(2600 + t * 150, brightness=0.9)
    darken(c, 0.08)

    draw_zombie_horde(c, t + 4.0, speed=1.55)

    # 폭발
    for st, ex, ey in BOOMS:
        if st <= t < st + 0.42:
            fi = min(9, int((t - st) / 0.042))
            put(c, expl()[fi], ex, ey, height=250)

    # 총알
    hx = 268 + math.sin(t * 2.2) * 26
    for i in range(14):
        bt = (t * 1.9 + i * 0.13) % 1.0
        bx = hx + 170 + bt * (CW - 200)
        by = CH - 330 + math.sin(i * 2.1) * 140
        put(c, img("BasicBullet.png"), bx, by, height=15)

    # 주인공은 좀비 떼보다 **앞에** 그린다(광고의 주인공이 묻히면 안 된다)
    put(c, img("Comstock.png"), hx, CH - 74, height=420, anchor="bottom")
    if int(t * 18) % 2 == 0:
        f = muzz()[int(t * 22) % 3]
        put(c, f, hx + 152, CH - 336, height=124)
        put(c, f, hx - 138, CH - 326, height=96, flip=True)

    # 광고식 수치 배지
    if t > 0.55:
        p = ease_out_back(clamp((t - 0.55) / 0.24))
        caption(c, "웨 이 브  2 0 회 !", 116, size=62, pop=p, banner=True, angle=-2)
    if t > 1.5:
        p = ease_out_back(clamp((t - 1.5) / 0.24))
        caption(c, "1 회 당  단 돈  6 0 초 !", 208, size=54, pop=p, banner=True, angle=2)
    if t > 2.4:
        p = ease_out_back(clamp((t - 2.4) / 0.24))
        caption(c, "탄약 없음 · 재장전 없음 · 조준도 안 하셔도 됩니다",
                CH - 150, size=38, pop=p, banner=True)

    caption(c, "* 실제 플레이 화면입니다 (흑백입니다)", CH - 44, size=22,
            alpha=190, kr=True)
    return c


# --- 9. 보스 (21.4 ~ 24.6) ---------------------------------------------------

_ROAR = None


def roar():
    global _ROAR
    if _ROAR is None:
        _ROAR = seq("BossRoar", "frame_{:03d}.png", 36, start=1)
    return _ROAR


def scene_boss(t, dur):
    c = bg_tile(3400, brightness=0.42)
    darken(c, 0.3)

    fr = roar()
    fi = min(len(fr) - 1, int(t * 15))
    zoom = 470 + 200 * ease_out(clamp(t / 1.8))
    shake = 0 if t < 0.55 else math.sin(t * 44) * 9
    put(c, fr[fi], CW / 2 + shake, CH - 24 + abs(math.sin(t * 3)) * 6,
        box=(CW * 0.94, zoom), anchor="bottom")

    if t < 1.25:
        p = ease_out_back(clamp(t / 0.24), 3.4)
        caption(c, "2 0 웨 이 브 ,  보 스 등 장 !", 132, size=62, pop=p,
                banner=True, angle=math.sin(t * 30) * 1.6)
    elif t < 2.35:
        p = ease_out_back(clamp((t - 1.25) / 0.24))
        caption(c, "이길 수 있겠습니까?", 132, size=68, pop=p, banner=True)
    else:
        p = ease_out_back(clamp((t - 2.35) / 0.2))
        caption(c, "저희도  모릅니다", 132, size=68, pop=p, banner=True,
                angle=math.sin(t * 18) * 2)

    if t > 2.95:
        flash(c, (t - 2.95) / 0.25)
    return c


# --- 10. 로고 + CTA + 면책 + TV 꺼짐 (24.6 ~ 30.0) --------------------------

FINE_PRINT = (
    "※ 본 영상은 흑백입니다. 실제 게임은 컬러입니다.   "
    "※ 무기 65종·파츠 134개·로봇 12종은 사실이나 수치는 전부 임시값입니다.   "
    "※ 탄약과 재장전은 존재하지 않습니다. 찾지 마십시오.   "
    "※ 디스크는 능력치를 올려 주는 대신 다른 능력치를 내립니다. 이는 정상입니다.   "
    "※ 무기를 너무 많이 장착하면 무거워서 느려집니다. 그래도 장착하십시오.   "
    "※ 20웨이브 보스에게 패배할 경우 당사는 책임지지 않습니다.   "
    "※ 좀비는 실제로 제거되지 않습니다.   "
)


def scene_logo(t, dur):
    c = new_canvas(14)
    d = ImageDraw.Draw(c)
    # 뒤에서 도는 집중선
    cx, cy = CW / 2, CH / 2 - 40
    for i in range(30):
        a = (i / 30) * math.tau - t * 0.35
        wdt = 0.042
        p1 = (cx + math.cos(a - wdt) * 1400, cy + math.sin(a - wdt) * 1400)
        p2 = (cx + math.cos(a + wdt) * 1400, cy + math.sin(a + wdt) * 1400)
        d.polygon([(cx, cy), p1, p2], fill=(52, 52, 52, 255))

    # 로고 착지
    k = clamp(t / 0.38)
    drop = (1 - ease_out(k)) * -420
    squash = 1.0 + 0.16 * math.sin(clamp((t - 0.38) / 0.3) * math.pi) if t > 0.38 else 1.0
    logo = text_img("COMSTOCK", font(FONT_EN, 116), stroke=13)
    if t <= 0.38:
        put(c, logo, CW / 2, cy - 66 + drop)
    else:
        put(c, logo, CW / 2, cy - 66, width=logo.width * squash)
    if 0.36 < t < 0.62:
        flash(c, (0.62 - t) / 0.26 * 0.85)

    if t > 0.55:
        p = ease_out_back(clamp((t - 0.55) / 0.24))
        caption(c, "웨이브 서바이벌 · 로봇 모딩 · 뱀서라이크", cy + 22, size=36, pop=p)

    if t > 0.95:
        blink = pulse(t - 0.95, 0.42, 0.62)
        p = ease_out_back(clamp((t - 0.95) / 0.22), 3.4)
        caption(c, "지 금  바 로  플 레 이 !", cy + 122, size=70, pop=p,
                alpha=int(90 + 165 * blink), angle=math.sin(t * 8) * 1.5,
                banner=True)

    # 주인공이 아래에서 뛰어올라와 자막 밑에 선다(자막을 가리지 않는 자리)
    if t > 1.25:
        k2 = ease_out_back(clamp((t - 1.25) / 0.4), 2.0)
        put(c, img("Comstock.png"), CW / 2, CH - 52 + (1 - k2) * 320,
            height=192, anchor="bottom", angle=math.sin(t * 9) * 4)

    # 초고속 면책 조항
    if t > 1.1:
        fnt = font(FONT_KR_R, 21)
        strip = text_img(FINE_PRINT, fnt, stroke=0, fill=(232, 232, 232, 255))
        sx = CW - (t - 1.1) * 430
        band = Image.new("RGBA", (CW, 34), (0, 0, 0, 190))
        c.alpha_composite(band, (0, CH - 40))
        c.alpha_composite(strip, (int(sx), CH - 37))
        c.alpha_composite(strip, (int(sx + strip.width + 60), CH - 37))

    # TV 꺼짐 (마지막 0.75초)
    off = dur - 0.75
    if t > off:
        k3 = clamp((t - off) / 0.75)
        black = new_canvas(0)
        # 세로로 찌부러들고 마지막에 점 하나
        hh = max(1, int(CH * (1 - ease_in(k3 * 1.15)) ))
        shrunk = c.resize((CW, hh), Image.LANCZOS) if hh > 1 else None
        if shrunk is not None:
            black.alpha_composite(shrunk, (0, int(CH / 2 - hh / 2)))
        dd = ImageDraw.Draw(black)
        if k3 > 0.55:
            g = clamp((k3 - 0.55) / 0.3)
            rad = max(0.0, 90 * (1 - g))
            v = int(255 * (1 - g))
            if rad > 0.6:
                dd.ellipse([CW / 2 - rad, CH / 2 - 3.5, CW / 2 + rad, CH / 2 + 3.5],
                           fill=(v, v, v, 255))
        c = black
    return c


# =============================================================================
#  타임라인
# =============================================================================

TIMELINE = [
    (0.0,  1.8, scene_signon,   "사인온"),
    (1.8,  2.8, scene_problem,  "문제제기"),
    (4.6,  2.2, scene_despair,  "절망3연타"),
    (6.8,  1.2, scene_so,       "그래서"),
    (8.0,  3.2, scene_hero,     "제품등장"),
    (11.2, 3.8, scene_catalog,  "무기카탈로그"),
    (15.0, 2.8, scene_heads,    "머리12종"),
    (17.8, 3.6, scene_combat,   "전투몽타주"),
    (21.4, 3.2, scene_boss,     "보스"),
    (24.6, 5.4, scene_logo,     "로고/CTA"),
]

# 장면이 바뀌는 순간마다 채널이 튀는 느낌으로 정전기를 터뜨린다
CUTS = [s for s, _, _, _ in TIMELINE[1:]]


def render_content(t):
    for st, dur, fn, _ in TIMELINE:
        if st <= t < st + dur:
            return fn(t - st, dur)
    return TIMELINE[-1][2](TIMELINE[-1][1], TIMELINE[-1][1])


# =============================================================================
#  CRT / 필름 처리
# =============================================================================

_vig = None
_scan = None


def vignette_mask():
    global _vig
    if _vig is None:
        yy, xx = np.mgrid[0:CH, 0:CW]
        nx = (xx - CW / 2) / (CW / 2)
        ny = (yy - CH / 2) / (CH / 2)
        r = np.sqrt(nx * nx * 0.94 + ny * ny)
        m = 1.0 - 0.44 * np.clip((r - 0.50) / 0.85, 0, 1) ** 1.7
        _vig = m.astype(np.float32)
    return _vig


def scanline_mask():
    global _scan
    if _scan is None:
        rows = np.arange(CH, dtype=np.float32)
        s = 0.90 + 0.10 * np.cos(rows * math.pi)
        _scan = s.reshape(CH, 1).astype(np.float32)
    return _scan


def burst_amount(t):
    """장면 전환 지점에서 확 튀는 정전기 세기."""
    a = 0.0
    for cut in CUTS:
        d = t - (cut - 0.10)
        if 0 <= d < 0.24:
            a = max(a, 1.0 - d / 0.24)
    # 중간중간 아주 짧게 신호가 튄다
    if 0.30 < t < 0.72:
        a = max(a, 0.95)
    for tt in (3.42, 9.71, 13.66, 19.55, 23.31):
        if 0 <= t - tt < 0.09:
            a = max(a, 0.72)
    return a


def crt(content, t, fi, rng):
    """콘텐츠(RGBA)를 흑백 CRT 화면으로 바꿔 numpy uint8 (CH,CW) 로 돌려준다."""
    g = np.asarray(content.convert("L"), dtype=np.float32)

    # 1) 톤 커브 — 옛날 흑백 카툰처럼 대비를 세게 준다
    g = (g - 128.0) * 1.40 + 138.0
    g = np.clip(g, 0, 255)
    g = 255.0 * np.power(g / 255.0, 0.82)   # 중간톤을 들어 올려 카툰 선이 살게 한다

    # 2) 컴포지트 신호 번짐 (가로 방향으로만 살짝)
    g = (np.roll(g, 1, axis=1) * 0.24 + g * 0.56 + np.roll(g, -1, axis=1) * 0.20)

    # 3) 밝기 깜빡임 + 수직 롤바
    g *= 0.94 + 0.10 * rng.random()
    roll_y = (t * 118.0 + 40.0) % (CH * 1.7)
    rows = np.arange(CH, dtype=np.float32)
    band = np.exp(-((rows - roll_y) ** 2) / (2 * 44.0 ** 2))
    g += (band * 17.0).reshape(CH, 1)

    # 4) 주사선 + 비네팅
    g *= scanline_mask()
    g *= vignette_mask()

    # 5) 필름 그레인
    g += rng.normal(0.0, 7.5, size=(CH, CW)).astype(np.float32)

    # 6) 수평 찢김(트래킹 불량)
    if rng.random() < 0.16:
        for _ in range(rng.integers(1, 4)):
            y0 = int(rng.integers(0, CH - 24))
            hgt = int(rng.integers(5, 40))
            sh = int(rng.integers(-16, 17))
            g[y0:y0 + hgt] = np.roll(g[y0:y0 + hgt], sh, axis=1)

    # 7) 정전기 폭발 (장면 전환)
    b = burst_amount(t)
    if b > 0.01:
        noise = rng.integers(0, 256, size=(CH, CW)).astype(np.float32)
        # 가로로 늘어난 신호 노이즈처럼 보이게 흐린다
        noise = (np.roll(noise, 1, 1) + noise + np.roll(noise, -1, 1)) / 3.0
        g = g * (1 - b * 0.93) + noise * (b * 0.93)
        vsh = int(rng.integers(-70, 71) * b)
        g = np.roll(g, vsh, axis=0)

    # 8) 필름 먼지 / 세로 스크래치
    if rng.random() < 0.30:
        x = int(rng.integers(0, CW))
        wdt = int(rng.integers(1, 3))
        y0 = int(rng.integers(0, CH // 2))
        y1 = int(rng.integers(y0 + 40, CH))
        g[y0:y1, x:x + wdt] = np.clip(g[y0:y1, x:x + wdt] + rng.integers(60, 150), 0, 255)
    for _ in range(int(rng.integers(0, 7))):
        y = int(rng.integers(0, CH)); x = int(rng.integers(0, CW))
        s = int(rng.integers(1, 4))
        g[y:y + s, x:x + s] = 255 if rng.random() < 0.6 else 0

    # 9) 프레임 지터 (필름이 게이트에서 흔들리는 느낌)
    dx = int(rng.integers(-2, 3))
    dy = int(rng.integers(-2, 3))
    g = np.roll(np.roll(g, dx, axis=1), dy, axis=0)

    return np.clip(g, 0, 255).astype(np.uint8)


_corner_mask = None


def corner_mask():
    """CRT 둥근 모서리 마스크."""
    global _corner_mask
    if _corner_mask is None:
        m = Image.new("L", (CW, CH), 0)
        d = ImageDraw.Draw(m)
        d.rounded_rectangle([0, 0, CW - 1, CH - 1], radius=46, fill=255)
        m = m.filter(ImageFilter.GaussianBlur(2.2))
        _corner_mask = np.asarray(m, dtype=np.float32) / 255.0
    return _corner_mask


def compose_frame(t, fi, rng):
    content = render_content(t)

    # 오버스캔: 살짝 확대해 가장자리를 화면 밖으로 흘린다
    k = 1.035
    big = content.resize((int(CW * k), int(CH * k)), Image.LANCZOS)
    ox = (big.width - CW) // 2
    oy = (big.height - CH) // 2
    content = big.crop((ox, oy, ox + CW, oy + CH))

    g = crt(content, t, fi, rng).astype(np.float32)
    g *= corner_mask()

    out = np.zeros((OH, OW), dtype=np.float32)
    out[:, SCREEN_X:SCREEN_X + CW] = g

    # 브라운관 주변부 미광(화면 빛이 베젤에 번진다)
    glow = np.zeros((OH, OW), dtype=np.float32)
    glow[:, SCREEN_X:SCREEN_X + CW] = g
    glow_im = Image.fromarray(np.clip(glow, 0, 255).astype(np.uint8)).filter(
        ImageFilter.GaussianBlur(26))
    out = np.maximum(out, np.asarray(glow_im, dtype=np.float32) * 0.20)

    # 필러박스에도 아주 옅은 그레인
    out[:, :SCREEN_X] += rng.normal(0, 2.0, size=(OH, SCREEN_X))
    out[:, SCREEN_X + CW:] += rng.normal(0, 2.0, size=(OH, OW - SCREEN_X - CW))

    rgb = np.repeat(np.clip(out, 0, 255).astype(np.uint8)[:, :, None], 3, axis=2)
    return rgb


# =============================================================================
#  오디오 (게임의 실제 BGM/효과음 + 브라운관 잡음)
# =============================================================================

SFX_CUES = [
    # (초, 파일, 볼륨)
    (0.32, "SFX/UI_Click.wav", 0.9),
    (1.82, "SFX/Enemy_Hit_A.wav", 0.8),
    (3.40, "SFX/Enemy_Death.wav", 0.9),
    (4.62, "SFX/Enemy_Hit_B.wav", 0.8),
    (5.35, "SFX/Enemy_Hit_C.ogg", 0.8),
    (6.10, "SFX/Enemy_Hit_A.wav", 0.8),
    (6.86, "SFX/Weapon_Melee.wav", 0.9),
    (7.10, "SFX/Weapon_Melee.wav", 0.9),
    (7.34, "SFX/Weapon_Explosive.wav", 1.0),
    (8.05, "SFX/LevelUp.wav", 0.85),
    (11.30, "SFX/UI_Click.wav", 0.7),
    (12.35, "SFX/UI_Click.wav", 0.7),
    (13.35, "SFX/UI_Click.wav", 0.7),
    (15.10, "SFX/UI_Click.wav", 0.7),
    (18.15, "SFX/Weapon_RapidFire.wav", 0.9),
    (18.55, "SFX/Weapon_Explosive.wav", 0.85),
    (19.45, "SFX/Weapon_Shotgun.wav", 0.85),
    (20.30, "SFX/Weapon_PlasmaCannon.wav", 0.8),
    (21.45, "SFX/Boss_Hit_A.wav", 1.0),
    (23.75, "SFX/Boss_Death.wav", 1.0),
    (24.62, "SFX/Weapon_Explosive.wav", 0.9),
    (25.60, "SFX/LevelUp.wav", 0.8),
    (29.30, "SFX/UI_Click.wav", 0.8),
]


def build_audio(ffmpeg, out_path):
    """BGM + 효과음 + 브라운관 히스를 섞고, 낡은 TV 스피커처럼 대역을 좁힌다."""
    inputs = []
    parts = []
    idx = 0

    bgm = os.path.join(RES, "Musics", "Game_BGM01.mp3")
    inputs += ["-i", bgm]
    parts.append(f"[{idx}:a]atrim=0:{DURATION},asetpts=N/SR/TB,volume=0.42,"
                 f"afade=t=in:st=0:d=0.6,afade=t=out:st={DURATION-1.2}:d=1.2[bgm]")
    idx += 1

    mix_labels = ["[bgm]"]
    for i, (at, rel, vol) in enumerate(SFX_CUES):
        p = os.path.join(RES, rel)
        if not os.path.exists(p):
            continue
        inputs += ["-i", p]
        lbl = f"s{i}"
        parts.append(f"[{idx}:a]volume={vol},adelay={int(at*1000)}|{int(at*1000)}[{lbl}]")
        mix_labels.append(f"[{lbl}]")
        idx += 1

    # 브라운관 히스 + 60Hz 험
    inputs += ["-f", "lavfi", "-t", str(DURATION), "-i", "anoisesrc=c=white:a=0.06:r=48000"]
    parts.append(f"[{idx}:a]aformat=channel_layouts=stereo,volume=0.30[hiss]")
    mix_labels.append("[hiss]")
    idx += 1

    inputs += ["-f", "lavfi", "-t", str(DURATION), "-i", "sine=frequency=60:sample_rate=48000"]
    parts.append(f"[{idx}:a]aformat=channel_layouts=stereo,volume=0.035[hum]")
    mix_labels.append("[hum]")
    idx += 1

    n = len(mix_labels)
    parts.append("".join(mix_labels) +
                 f"amix=inputs={n}:duration=first:dropout_transition=0:normalize=0[mixed]")
    # 낡은 TV 스피커: 대역 제한(320~3600Hz) + 세게 눌러 붙인 다음 방송 수준으로 정규화.
    # loudnorm 없이 두면 평균 -28dB라 광고답지 않게 소심하게 들린다.
    parts.append("[mixed]highpass=f=320,lowpass=f=3600,acompressor=threshold=0.10:"
                 "ratio=6:attack=8:release=140,"
                 "loudnorm=I=-15:TP=-1.5:LRA=11,"
                 f"atrim=0:{DURATION},asetpts=N/SR/TB,"
                 "aformat=sample_fmts=fltp:sample_rates=48000:"
                 "channel_layouts=stereo[out]")

    cmd = [ffmpeg, "-y", "-hide_banner", "-loglevel", "error"] + inputs + [
        "-filter_complex", ";".join(parts), "-map", "[out]",
        "-c:a", "aac", "-b:a", "160k", out_path]
    subprocess.run(cmd, check=True)


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
        rng = np.random.default_rng(SEED + int(t * FPS))
        rgb = compose_frame(t, int(t * FPS), rng)
        p = os.path.join(OUT_DIR, f"still_{t:06.2f}.png".replace(".", "_", 1))
        Image.fromarray(rgb).save(p)
        print("saved", p)


def render_video(path):
    os.makedirs(OUT_DIR, exist_ok=True)
    ff = ffmpeg_exe()
    silent = os.path.join(OUT_DIR, "_video_only.mp4")
    audio = os.path.join(OUT_DIR, "_audio.m4a")

    cmd = [ff, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{OW}x{OH}",
           "-r", str(FPS), "-i", "-",
           # 필름 그레인은 압축이 거의 안 먹으므로 CRF를 낮게 잡으면 파일이 80MB를
           # 넘긴다. 화면이 어차피 지직거리는 흑백이라 CRF 28에서도 눈에 띄는 손실이
           # 없고 10MB대로 떨어진다.
           "-c:v", "libx264", "-preset", "slow", "-crf", "28",
           "-maxrate", "6M", "-bufsize", "12M",
           "-pix_fmt", "yuv420p", "-movflags", "+faststart", silent]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for fi in range(TOTAL_FRAMES):
        t = fi / FPS
        rng = np.random.default_rng(SEED + fi)
        rgb = compose_frame(t, fi, rng)
        proc.stdin.write(rgb.tobytes())
        if fi % 48 == 0:
            print(f"  frame {fi}/{TOTAL_FRAMES}  ({t:5.2f}s)", flush=True)
    proc.stdin.close()
    if proc.wait() != 0:
        raise SystemExit("ffmpeg(video) 실패")

    print("오디오 믹싱…", flush=True)
    build_audio(ff, audio)

    print("먹싱…", flush=True)
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error",
                    "-i", silent, "-i", audio,
                    "-c:v", "copy", "-c:a", "copy", "-shortest",
                    "-movflags", "+faststart", path], check=True)
    for p in (silent, audio):
        if os.path.exists(p):
            os.remove(p)
    print("완료:", path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stills", action="store_true", help="확인용 정지 프레임만 뽑는다")
    ap.add_argument("--preview", type=float, default=None, help="특정 시각 한 장")
    ap.add_argument("-o", "--out", default=os.path.join(OUT_DIR, "comstock_pv.mp4"))
    args = ap.parse_args()

    if args.preview is not None:
        render_stills([args.preview])
        return
    if args.stills:
        render_stills([0.15, 0.55, 1.20, 2.30, 3.60, 5.00, 6.00, 7.00, 7.60,
                       8.60, 10.20, 11.60, 13.00, 14.60, 15.60, 17.20,
                       18.40, 20.00, 21.90, 23.60, 25.20, 26.60, 28.60, 29.70])
        return
    render_video(args.out)


if __name__ == "__main__":
    main()
