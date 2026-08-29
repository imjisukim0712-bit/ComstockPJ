# -*- coding: utf-8 -*-
"""컴스톡 병맛 PV #2 - 인스타그램 릴스 밈 몽타주. 9:16 1080x1920, 30fps, 20초.

밈 문법을 그대로 따른다: 흰 캡션 바 + 산세리프, 컷마다 바인 붐, 딥프라이드
플렉스, 모아이(🗿) 보스, 그리고 "link in bio" 개그. 마지막은 반드시
"DOWNLOAD IT BEFORE THE ZOMBIES DO" + itch.io 링크.

사용법:
    python3 pv2_meme.py                      # dev/pv2/Comstock_Meme_IG.mp4
    python3 pv2_meme.py --test 1.0,3.4,6.0   # 미리보기 PNG만
"""
import argparse
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv2_common import (A, EMOJI, F, Mixer, ITCH_URL, SPR, WPN, add_grain,
                        apply_vignette, bass808, blit, boom, boss_img, clamp,
                        clap, deep_fry, desaturate, draw_robot, ease_in,
                        ease_out, emoji_line, encode_frames, explosion_at,
                        fast_post, fit_size, game_snd, gradient_v, ground_tex,
                        hat, kick808, make_web_version, muzzle_at, note, nf,
                        otext, pop_scale, rgb_shift, riser, robot_img,
                        ruined_skyline, screen_flash, speed_lines, sub_drop,
                        sunburst, text_w, twos, whoosh, zombie_img, zoom_at,
                        BILINEAR, LANCZOS, CACHE)

W, H = 1080, 1920
FPS = 30
DUR = 20.0
NFRAMES = int(FPS * DUR)

BPM = 150.0
BEAT = 60.0 / BPM          # 0.4초
BAR = BEAT * 4             # 1.6초

# (시작, 길이, 이름). 컷은 비트 경계에 맞춘다. 합계 20.0초.
TIMELINE = [
    (0.0, 2.4, "pov"),      # POV: 지구 최후의 로봇
    (2.4, 2.4, "am3"),      # nobody: / zombies at 3 AM:
    (4.8, 2.4, "choice"),   # 도망 ❌ / 총 6정 ✅
    (7.2, 2.8, "guns"),     # 감성지원 로봇에 7번째 총 달기
    (10.0, 2.4, "crown"),   # 레전더리 왕관 (딥프라이드)
    (12.4, 2.8, "boss"),    # 웨이브 20 보스 🗿
    (15.2, 4.8, "cta"),     # 좀비보다 먼저 다운로드
]
CUTS = [t for (t, _d, _n) in TIMELINE][1:]

INK = (28, 26, 30)
CREAM = (250, 246, 236)

T = {
    "pov": "POV: you're the last robot on Earth",
    "nobody": "nobody:",
    "am3": "zombies at 3 AM:",
    "choiceA": "option A: run",
    "choiceB": "option B: six guns",
    "guns": "me adding a 7th gun to the\nemotional support robot",
    "crown": "when the LEGENDARY crown\nfinally drops",
    "boss_cap": "wave 20 boss:",
    "cta1": "DOWNLOAD IT",
    "cta2": "BEFORE THE",
    "cta3": "ZOMBIES DO",
    "cta_free": "FREE on itch.io",
    "cta_btn": "DOWNLOAD",
    "cta_hurry": "the zombies have wifi. HURRY.",
    "cta_bio": "link in bio*   (*jk. it's right here)",
    "handle": "@pyramid.studio",
    "audio_tag": "Comstock OST - original audio",
}

_misc = {}


def scene_at(t):
    for (t0, d, name) in TIMELINE:
        if t < t0 + d:
            return name, max(0.0, t - t0), d
    t0, d, name = TIMELINE[-1]
    return name, d, d


def beat_pulse(t, amp=0.045, decay=9.0):
    """비트마다 살짝 커졌다 돌아오는 줌 배율."""
    ph = (t % BEAT) / BEAT
    return 1.0 + amp * math.exp(-ph * decay)


# ---------------------------------------------------------------- 밈 UI
def meme_bar(cnv, lines, y0=170, size=62, pad=34, bar_fill=(255, 255, 255, 244),
             bar_line=(210, 206, 200, 255)):
    """상단 흰 캡션 바(현대 밈 문법: 흰 바탕 + 검정 산세리프)."""
    line_h = int(size * 1.32)
    bar_h = pad * 2 + line_h * len(lines)
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rounded_rectangle([36, y0, W - 36, y0 + bar_h], radius=30,
                        fill=bar_fill, outline=bar_line, width=3)
    for i, (s, col) in enumerate(lines):
        sz = fit_size(s.replace("🧟", "  "), "roboto", size, W - 200)
        emoji_line(cnv, (W / 2, y0 + pad + line_h * i + line_h / 2), s, "roboto",
                   sz, fill=col, anchor="c")
    return y0 + bar_h


def reels_overlay(cnv, t, show_handle=True):
    """오른쪽 액션 아이콘 + 왼쪽 아래 핸들(어느 플랫폼이라고는 안 했다)."""
    d = ImageDraw.Draw(cnv, "RGBA")
    x = W - 92
    wh = (255, 255, 255, 235)
    sh = (0, 0, 0, 110)
    # 하트
    y = 1210
    like_pop = 1.0 + 0.25 * math.exp(-((t % 2.3) / 0.14) ** 2)
    r = 32 * like_pop
    for (ox, oy, col) in ((3, 4, sh), (0, 0, (255, 68, 92, 240))):
        d.ellipse([x - r + ox, y - r * 0.9 + oy, x + ox, y + r * 0.1 + oy], fill=col)
        d.ellipse([x + ox, y - r * 0.9 + oy, x + r + ox, y + r * 0.1 + oy], fill=col)
        d.polygon([(x - r * 0.96 + ox, y - r * 0.22 + oy),
                   (x + r * 0.96 + ox, y - r * 0.22 + oy),
                   (x + ox, y + r * 0.95 + oy)], fill=col)
    otext(cnv, (x, y + 76), "42.0K", "roboto", 30, fill=wh, anchor="mm", stroke=3,
          stroke_fill=(0, 0, 0, 130))
    # 말풍선
    y = 1395
    for (ox, oy, col) in ((3, 4, sh), (0, 0, wh)):
        d.rounded_rectangle([x - 34 + ox, y - 30 + oy, x + 34 + ox, y + 22 + oy],
                            radius=22, fill=col)
        d.polygon([(x - 14 + ox, y + 18 + oy), (x + 8 + ox, y + 18 + oy),
                   (x - 20 + ox, y + 42 + oy)], fill=col)
    otext(cnv, (x, y + 92), "1.3K", "roboto", 30, fill=wh, anchor="mm", stroke=3,
          stroke_fill=(0, 0, 0, 130))
    # 종이비행기
    y = 1580
    for (ox, oy, col) in ((3, 4, sh), (0, 0, wh)):
        d.polygon([(x - 34 + ox, y + 6 + oy), (x + 36 + ox, y - 26 + oy),
                   (x + 6 + ox, y + 34 + oy), (x - 4 + ox, y + 12 + oy)], fill=col)
    otext(cnv, (x, y + 92), "9.9K", "roboto", 30, fill=wh, anchor="mm", stroke=3,
          stroke_fill=(0, 0, 0, 130))
    # 핸들 + 오디오 (음표는 DejaVu에만 있어서 serif 폰트로 그린다)
    if show_handle:
        otext(cnv, (56, 1712), T["handle"], "roboto", 36, fill=wh, anchor="lm",
              stroke=4, stroke_fill=(0, 0, 0, 130))
        otext(cnv, (56, 1770), "♪", "serifb", 30, fill=(240, 240, 240, 220),
              anchor="lm", stroke=4, stroke_fill=(0, 0, 0, 120))
        otext(cnv, (98, 1768), T["audio_tag"], "roboto", 30,
              fill=(240, 240, 240, 220), anchor="lm", stroke=4,
              stroke_fill=(0, 0, 0, 120))


def night_bg(cnv, t, camx=0.0, moon=True, dark=1.0):
    cnv.paste(gradient_v(W, H, (26, 24, 44), (58, 52, 78)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    rng = random.Random(9)
    for _ in range(40):                    # 별
        sx, sy = rng.randrange(W), rng.randrange(0, 900)
        a = 120 + int(100 * abs(math.sin(t * 2 + sx)))
        d.ellipse([sx, sy, sx + 4, sy + 4], fill=(255, 255, 240, a))
    if moon:
        d.ellipse([W - 300, 170, W - 120, 350], fill=(250, 244, 210, 255),
                  outline=(200, 190, 150, 255), width=6)
        d.ellipse([W - 250, 210, W - 200, 260], fill=(232, 224, 186, 255))
        d.ellipse([W - 190, 280, W - 155, 315], fill=(232, 224, 186, 255))
    sk = ruined_skyline(W + 500, 420, seed=77, col=(44, 40, 62), win=(255, 216, 120))
    blit(cnv, sk, -int(camx * 0.3) % 500 - 500, 1180 - 420, anchor="lt", alpha=0.95)
    g = ground_tex(W, H - 1180, dark=0.5 * dark)
    cnv.paste(g, (0, 1180))
    d.line([(0, 1180), (W, 1180)], fill=(16, 14, 20), width=7)


# ---------------------------------------------------------------- 1. POV
def sc_pov(cnv, t, dur):
    night_bg(cnv, t, camx=t * 30)
    # 무리가 사방에서 좁혀온다
    p = t / dur
    for i in range(12):
        rng = random.Random(500 + i)
        side = 1 if i % 2 == 0 else -1
        row = i % 4
        gy = 1215 + row * 165
        x0 = W / 2 + side * (430 + rng.uniform(0, 420))
        x = x0 - side * (120 * t + 260 * p * rng.uniform(0.7, 1.2))
        hh = 250 + row * 66
        kind = ("Zombie", "Zombie", "Spitter", "Zombie", "Leader", "Zombie")[i % 6]
        blit(cnv, zombie_img(t, i, kind, h=hh, face=-side), x, gy, anchor="cb")
    # 최후의 로봇(전등 아래서 떨고 있다)
    rob = draw_robot(cnv, W / 2, 1855, 300, t, bounce=False,
                     rot=math.sin(t * 26) * 2.5)
    e = EMOJI("😰", 74)
    blit(cnv, e, rob["top"][0] + 130, rob["top"][1] - 30, anchor="cc")

    meme_bar(cnv, [(T["pov"], INK)])
    out = zoom_at(cnv, 1.0 + 0.09 * p, W / 2, 1500)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 2. 3AM
def sc_am3(cnv, t, dur):
    night_bg(cnv, t, moon=True, dark=0.7)
    dim = Image.new("RGBA", (W, H), (10, 8, 20, 90))
    cnv.paste(dim, (0, 0), dim)
    d = ImageDraw.Draw(cnv, "RGBA")
    # 디지털 시계
    d.rounded_rectangle([W / 2 - 200, 640, W / 2 + 200, 780], radius=22,
                        fill=(12, 10, 14, 235), outline=(80, 20, 24, 255), width=5)
    blink = ":" if int(t * 2) % 2 == 0 else " "
    otext(cnv, (W / 2, 710), "3%s00 AM" % blink, "orbitron", 64,
          fill=(255, 64, 58), anchor="mm")
    # 스프린터가 굉음을 내며 가로지른다 (3번, 점점 크게)
    passes = ((0.55, 1330, 380), (1.15, 1520, 540), (1.75, 1720, 700))
    for i, (t0, gy, hh) in enumerate(passes):
        if t0 - 0.5 <= t < t0 + 0.5:
            p = (t - (t0 - 0.5)) / 1.0
            x = W + 400 - (W + 900) * p
            z = zombie_img(t, i, "Sprinter", h=hh, face=-1)
            blit(cnv, z, x, gy, anchor="cb")
            for k in range(7):             # 모션 라인
                ly = gy - hh * 0.5 + (k - 3) * hh * 0.11
                d.line([(x + hh * 0.4, ly), (x + hh * 0.4 + 240 + k * 14, ly)],
                       fill=(255, 255, 255, 150), width=8)
    meme_bar(cnv, [(T["nobody"], (120, 116, 124)), (T["am3"], INK)])


# ---------------------------------------------------------------- 3. 선택
def _panel(w, h, which, t):
    im = Image.new("RGB", (w, h), (120, 170, 220))
    im.paste(gradient_v(w, h, (150, 200, 240), (200, 235, 250)), (0, 0))
    d = ImageDraw.Draw(im)
    gy = h - 60
    d.rectangle([0, gy, w, h], fill=(126, 196, 100))
    d.line([(0, gy), (w, gy)], fill=(52, 110, 62), width=6)
    if which == "A":                       # 도망 - 로봇이 왼쪽으로 내뺀다
        wig = math.sin(twos(t) * 24) * 10
        rob = draw_robot(im, w * 0.34 - 40 * math.sin(t * 2), gy + 20, 300, t,
                         bounce=True, rot=wig, flip=True)
        for k in range(3):
            d.line([(rob["x"] + 170 + k * 40, gy - 150 - k * 40),
                    (rob["x"] + 320 + k * 40, gy - 150 - k * 40)],
                   fill=(255, 255, 255), width=10)
        for i in range(2):
            blit(im, zombie_img(t, i, "Zombie", h=260, face=-1),
                 w * 0.74 + i * 150 - 30 * t, gy + 14, anchor="cb")
    else:                                  # 총 6정 - 생각이란 것을 그만둔다
        rob = draw_robot(im, w * 0.5, gy + 20, 320, t, bounce=True)
        guns = (("SMG", -60, -230, -18), ("RocketLauncher", 60, -260, 14),
                ("SawedOff", -150, -120, -40), ("LaserPistol", 150, -140, 32),
                ("GrenadeLauncher", -110, -320, 20), ("PlasmaCannon", 120, -60, -12))
        for (g, ox, oy, rot) in guns:
            spr = WPN(g, +1, 110)
            blit(im, spr.rotate(rot, resample=BILINEAR, expand=True),
                 rob["x"] + ox, rob["y"] + oy, anchor="cc")
        for k in range(3):
            muzzle_at(im, rob["x"] + 240 + (k % 2) * 60, rob["y"] - 140 - k * 90,
                      t, size=100, seed=k)
    return im


def sc_choice(cnv, t, dur):
    cnv.paste((236, 232, 226), (0, 0, W, H))
    top = meme_bar(cnv, [("how to survive the apocalypse:", INK)], y0=140, size=56)
    ph = H // 2 - 140
    y1 = top + 40
    y2 = y1 + ph - 340 + 60
    pw = W - 120
    p1 = _panel(pw, ph - 380, "A", t)
    p2 = _panel(pw, ph - 380, "B", t)
    d = ImageDraw.Draw(cnv)
    for (pimg, py, label, ok, t_pop) in ((p1, y1, T["choiceA"], False, 0.35),
                                         (p2, y2, T["choiceB"], True, 1.15)):
        d.rounded_rectangle([54, py - 6, 54 + pw + 12, py + pimg.height + 90],
                            radius=26, fill=(255, 255, 255), outline=(60, 56, 60),
                            width=5)
        cnv.paste(pimg, (60, py))
        otext(cnv, (W / 2, py + pimg.height + 40), label, "roboto", 46, fill=INK,
              anchor="mm")
        if t > t_pop:
            s = pop_scale(t - t_pop, 0.22, 1.1)
            mark = EMOJI("✅" if ok else "❌", int(190 * s))
            blit(cnv, mark, W - 200, py + 90, anchor="cc")


# ---------------------------------------------------------------- 4. 총 더 달기
GUN_SCHED = (("SMG", 0.30, -30, -0.63, -16), ("SawedOff", 0.65, -160, -0.30, -38),
             ("LaserPistol", 1.00, 150, -0.52, 30), ("GrenadeLauncher", 1.35, -140, -0.05, 14),
             ("RocketLauncher", 1.70, 120, -0.13, -10), ("PlasmaCannon", 2.05, 40, -0.78, 8))


def sc_guns(cnv, t, dur):
    cnv.paste(sunburst(W, H, twos(t) * 24, c1=(84, 104, 150), c2=(60, 76, 116)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle([0, 1560, W, H], fill=(70, 60, 80))
    d.line([(0, 1560), (W, 1560)], fill=(30, 26, 36), width=7)

    rob = draw_robot(cnv, W / 2, 1550, 560, t, bounce=True,
                     rot=math.sin(twos(t) * 5.2) * 3)
    n_on = 0
    for (g, t0, ox, oyr, rot) in GUN_SCHED:
        if t < t0:
            continue
        n_on += 1
        q = clamp((t - t0) / 0.16)
        # 화면 밖에서 날아와 척 붙는다
        fx = rob["x"] + ox * (1 + 3.5 * (1 - ease_out(q)))
        fy = rob["y"] + oyr * rob["h"] - 500 * (1 - ease_out(q)) * (1 if ox < 0 else -0.4)
        spr = WPN(g, +1, 150 + 24 * math.sin(t0 * 99))
        spr = spr.rotate(rot + 360 * (1 - q) * (1 if ox > 0 else -1),
                         resample=BILINEAR, expand=True)
        blit(cnv, spr, fx, fy, anchor="cc")
        if q >= 1.0 and t - t0 < 0.35:
            speed_lines(cnv, rob["x"] + ox, rob["y"] + oyr * rob["h"], 7, 60, 130,
                        seed=int(t0 * 100), width=5, color=(255, 255, 255, 200))
    # 카운터
    d.rounded_rectangle([W - 320, 560, W - 60, 680], radius=20,
                        fill=(16, 14, 18, 220), outline=(255, 214, 64, 255), width=5)
    otext(cnv, (W - 190, 620), "GUNS: %d" % (n_on + 1), "orbitron", 52,
          fill=(255, 224, 90), anchor="mm")
    # 다 붙으면 일제 사격
    if t > 2.35:
        for k in range(5):
            muzzle_at(cnv, rob["x"] - 260 + k * 130, rob["y"] - rob["h"] * (0.3 + 0.14 * (k % 3)),
                      t, size=130, seed=k)
        screen_flash(cnv, 0.10 + 0.06 * math.sin(t * 60))
    meme_bar(cnv, [(s, INK) for s in T["guns"].split("\n")], size=56)
    out = zoom_at(cnv, beat_pulse(t, 0.03), W / 2, 1100)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 5. 왕관
def sc_crown(cnv, t, dur):
    fry = clamp((t - 0.5) / 1.2)
    cnv.paste(sunburst(W, H, twos(t) * 40, c1=(255, 214, 60), c2=(255, 168, 36)), (0, 0))
    speed_lines(cnv, W / 2, 1150, 10, 420, 800, seed=int(t * 10), width=7,
                color=(255, 255, 255, 120))
    rob = draw_robot(cnv, W / 2, 1660, 560, t, bounce=True)
    # 왕관 강림
    cp = clamp((t - 0.12) / 0.4)
    cy = rob["top"][1] - 40 - (1 - ease_out(cp)) * 700
    crown = SPR("Accessories/Crown-transparent.png", w=int(rob["head_w"] * 1.02))
    blit(cnv, crown, rob["top"][0] + 6, cy, anchor="cb")
    # 선글라스 강림 (deal with it)
    gp = clamp((t - 0.95) / 0.35)
    if t > 0.95:
        gl = SPR("Accessories/8Bitsunglass-transparent.png", w=int(rob["head_w"] * 0.94))
        gy = rob["face"][1] - 24 - (1 - ease_out(gp)) * 520
        blit(cnv, gl, rob["face"][0], gy, anchor="cc")
    # 스티커
    for (ch, sx, sy, t0) in (("👑", 170, 620, 0.55), ("💯", W - 190, 800, 0.8),
                             ("🔥", 170, 1050, 1.05), ("🔥", W - 170, 1290, 1.2),
                             ("😂", 210, 1420, 1.35)):
        if t > t0:
            s = pop_scale(t - t0, 0.2, 1.2)
            blit(cnv, EMOJI(ch, int(150 * s)), sx, sy, anchor="cc")
    # 작은 반짝이들
    if t > 0.6:
        d = ImageDraw.Draw(cnv, "RGBA")
        rng = random.Random(int(t * 9))
        for _ in range(5):
            fx, fy = rng.randrange(80, W - 80), rng.randrange(520, 1500)
            ln = rng.randint(16, 42)
            d.line([(fx - ln, fy), (fx + ln, fy)], fill=(255, 255, 255, 220), width=7)
            d.line([(fx, fy - ln), (fx, fy + ln)], fill=(255, 255, 255, 220), width=7)
    meme_bar(cnv, [(s, INK) for s in T["crown"].split("\n")], size=56)
    out = zoom_at(cnv, beat_pulse(t, 0.06, 7.0), W / 2, 1150)
    out = deep_fry(out, fry * 0.85)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 6. 보스
def sc_boss(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (30, 12, 16), (74, 22, 26)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    # 경광등
    al = 0.5 + 0.5 * math.sin(t * 9)
    d.rectangle([0, 0, W, H], fill=(120, 10, 14, int(44 * al)))
    g = ground_tex(W, 240, dark=0.42)
    cnv.paste(g, (0, H - 240))
    d.line([(0, H - 240), (W, H - 240)], fill=(12, 8, 10), width=8)

    p = clamp(t / dur)
    bh = 1150 + 420 * ease_out(clamp(t / 2.2))
    bs = boss_img(t, h=int(bh), roar=(t > 1.1))
    blit(cnv, bs, W / 2, 320 + bh / 2, anchor="cc")
    # 스케일용 꼬마 로봇 (덜덜)
    rob = draw_robot(cnv, 230, H - 300, 210, t, bounce=False,
                     rot=math.sin(t * 30) * 4)
    otext(cnv, (230, H - 540), "you", "roboto", 44, fill=(255, 255, 255), anchor="mm",
          stroke=6, stroke_fill=(20, 10, 12))
    # 🗿 도장
    if t > 1.35:
        s = pop_scale(t - 1.35, 0.24, 0.9)
        blit(cnv, EMOJI("🗿", int(430 * s)), W - 260, 1500, anchor="cc")
    if t > 0.2:
        meme_bar(cnv, [(T["boss_cap"], (245, 240, 238))], y0=170, size=62,
                 bar_fill=(20, 14, 16, 235), bar_line=(150, 40, 40, 255))
    out = zoom_at(cnv, 1.0 + 0.07 * ease_in(p), W / 2, 800)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 7. CTA
def sc_cta(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (250, 246, 236), (240, 230, 206)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle([22, 22, W - 22, H - 22], outline=INK + (255,), width=6)

    s = pop_scale(t, 0.24, 0.7)
    blit(cnv, SPR("UI/title_logo.png", w=int(880 * s)), W / 2, 300, anchor="cc")

    lines = ((T["cta1"], 0.45), (T["cta2"], 0.62), (T["cta3"], 0.79))
    for i, (s_, t0) in enumerate(lines):
        if t > t0:
            q = pop_scale(t - t0, 0.2, 0.75)
            otext(cnv, (W / 2, 560 + i * 170), s_, "anton", int(150 * q),
                  fill=(208, 52, 44) if i == 2 else INK, anchor="mm", max_w=W - 160)
    if t > 1.0:
        blit(cnv, EMOJI("🧟", int(120 * pop_scale(t - 1.0, 0.2, 1.0))), W - 170, 480,
             anchor="cc")
        blit(cnv, EMOJI("📱", int(110 * pop_scale(t - 1.1, 0.2, 1.0))), 160, 980,
             anchor="cc")
    # FREE 배지
    if t > 1.25:
        q = pop_scale(t - 1.25, 0.22, 0.9)
        bs = int(235 * q)
        star = Image.new("RGBA", (bs * 2, bs * 2), (0, 0, 0, 0))
        sd = ImageDraw.Draw(star)
        c = bs
        pts = []
        for i in range(24):
            a = math.pi * i / 12 - math.pi / 2
            rr = bs * (0.98 if i % 2 == 0 else 0.72)
            pts.append((c + rr * math.cos(a), c + rr * math.sin(a)))
        sd.polygon(pts, fill=(255, 214, 64, 255), outline=(60, 50, 40, 255))
        sd.line(pts + [pts[0]], fill=(60, 50, 40, 255), width=6)
        star = star.rotate(12, resample=BILINEAR, expand=False)
        blit(cnv, star, 180, 1265, anchor="cc")
        otext(cnv, (180, 1225), "FREE", "anton", int(66 * q), fill=INK, anchor="mm")
        otext(cnv, (180, 1292), "on itch.io", "roboto", int(30 * q), fill=INK,
              anchor="mm")
    # 거대 다운로드 버튼 + 좀비 손가락 연타
    if t > 1.55:
        press = math.exp(-((t * 2.5) % 1) * 7)          # 0.4초마다 꾹
        bw, bh = 660, 190
        bx, by = W / 2 + 60, 1430
        sq = 1.0 - 0.07 * press
        d.rounded_rectangle([bx - bw / 2 * sq, by - bh / 2 * sq + 14,
                             bx + bw / 2 * sq, by + bh / 2 * sq + 14],
                            radius=40, fill=(44, 120, 52, 255))
        d.rounded_rectangle([bx - bw / 2 * sq, by - bh / 2 * sq - 10 * (1 - press),
                             bx + bw / 2 * sq, by + bh / 2 * sq - 10 * (1 - press)],
                            radius=40, fill=(86, 190, 96, 255),
                            outline=(30, 70, 36, 255), width=7)
        # 아래 화살표는 폰트에 없어서 직접 그린다
        aw = 26 * sq
        ax = bx - text_w(T["cta_btn"], "oswald", int(64 * sq)) / 2 - aw - 26
        ay = by - 12 * (1 - press)
        d.polygon([(ax - aw * 0.45, ay - aw), (ax + aw * 0.45, ay - aw),
                   (ax + aw * 0.45, ay), (ax + aw, ay), (ax, ay + aw),
                   (ax - aw, ay), (ax - aw * 0.45, ay)], fill=(255, 255, 255, 255))
        otext(cnv, (bx + aw, by - 12 * (1 - press)), T["cta_btn"], "oswald",
              int(64 * sq), fill=(255, 255, 255), anchor="mm")
        # 좀비가 옆에서 손을 뻗는다
        zp = clamp((t - 1.55) / 0.5)
        blit(cnv, zombie_img(t, 3, "Zombie", h=430, face=-1),
             W - 140 + 40 * (1 - ease_out(zp)), by + 260, anchor="cb")
    if t > 2.5:
        otext(cnv, (W / 2 - 60, 1600), T["cta_hurry"], "roboto", 44, fill=INK,
              anchor="mm", max_w=700)
    # URL 판
    if t > 1.9:
        q = pop_scale(t - 1.9, 0.22, 0.5)
        pw2, ph2 = int(920 * q), int(104 * q)
        d.rounded_rectangle([W / 2 - pw2 / 2, 1706 - ph2 / 2, W / 2 + pw2 / 2,
                             1706 + ph2 / 2], radius=26, fill=(34, 32, 30, 255),
                            outline=(255, 214, 64, 255), width=6)
        otext(cnv, (W / 2, 1706), ITCH_URL, "roboto", 46, fill=(255, 238, 130),
              anchor="mm", max_w=860)
    if t > 3.1:
        otext(cnv, (W / 2, 1812), T["cta_bio"], "serif", 34, fill=(96, 88, 78),
              anchor="mm")
    if t > dur - 0.35:
        screen_flash(cnv, ease_in((t - (dur - 0.35)) / 0.35), color=(8, 8, 10))


SCENES = {"pov": sc_pov, "am3": sc_am3, "choice": sc_choice, "guns": sc_guns,
          "crown": sc_crown, "boss": sc_boss, "cta": sc_cta}


# ---------------------------------------------------------------- 렌더
def shake_at(t):
    name, tl, _d = scene_at(t)
    v = 0.0
    for c in CUTS:                          # 컷마다 화면이 울린다
        if 0 <= t - c < 0.22:
            v = max(v, 14.0 * (1 - (t - c) / 0.22))
    if name == "am3":
        for (t0, _gy, hh) in ((0.55, 0, 300), (1.15, 0, 420), (1.75, 0, 560)):
            if 0 <= tl - t0 < 0.3:
                v = max(v, hh * 0.02)
    if name == "guns" and tl > 2.35:
        v = max(v, 9.0)
    if name == "boss":
        v = max(v, 3.0 + 7.0 * clamp(tl / 2.6))
    if name == "cta" and 0.75 <= tl < 1.0:
        v = max(v, 10.0)
    return v


def render_frame(f):
    t = f / FPS
    name, tl, d = scene_at(t)
    cnv = Image.new("RGB", (W, H), (10, 10, 12))
    SCENES[name](cnv, tl, d)
    sh = shake_at(t)
    if sh > 0:
        rng = random.Random(f * 77)
        cnv = cnv.transform((W, H), Image.Transform.AFFINE,
                            (1, 0, (rng.random() * 2 - 1) * sh,
                             0, 1, (rng.random() * 2 - 1) * sh * 0.8),
                            resample=BILINEAR, fillcolor=(10, 10, 12))
    # 컷 직후 색수차 펀치
    for c in CUTS:
        if 0 <= t - c < 0.14:
            cnv = rgb_shift(cnv, int(10 * (1 - (t - c) / 0.14)))
            break
    reels_overlay(cnv, t, show_handle=(name != "cta"))
    cnv = fast_post(cnv, strength=0.34, power=2.8, grain=4, f=f)
    return cnv


# ---------------------------------------------------------------- 오디오
def build_audio(path):
    mx = Mixer(DUR)

    def beat_section(t0, t1, roots):
        """150BPM 하프타임 트랩: 킥/스네어/햇 + 808 베이스."""
        b = t0
        bar_i = 0
        while b < t1 - 0.01:
            root = roots[bar_i % len(roots)]
            # 킥: 1박, 1.75박, 3.5박(비트 단위 0, 1.5, 2.5)
            for ko in (0.0, 1.5 * BEAT, 2.5 * BEAT):
                if b + ko < t1:
                    mx.put(kick808(0.42, 130, 46, 0.9), b + ko)
                    mx.put(bass808(0.5, nf(root) / 4, 0.5), b + ko)
            # 스네어(3박)
            if b + 2 * BEAT < t1:
                mx.put(clap(0.5, seed=int(b * 10)), b + 2 * BEAT)
            # 햇 8분음표 + 마지막 박 롤
            for i in range(8):
                tt = b + i * BEAT / 2
                if tt < t1:
                    mx.put(hat(0.05, 0.16, seed=i + int(b)), tt)
            for i in range(4):
                tt = b + 3 * BEAT + i * BEAT / 4
                if tt < t1:
                    mx.put(hat(0.04, 0.11, seed=50 + i), tt)
            b += BAR
            bar_i += 1

    beat_section(0.0, 12.4, ("E2", "E2", "G2", "A2"))
    beat_section(15.2, 19.7, ("E2", "G2", "A2"))

    # ---- 컷 붐
    for c in [0.02] + CUTS:
        mx.put(boom(0.6, 150, 38, 0.9), c)
    mx.put(sub_drop(2.0, 100, 26, 0.8), 12.4)          # 보스 등장은 더 깊게

    # ---- 1. POV: 좀비 신음 웅성웅성
    for i, tt in enumerate((0.3, 0.9, 1.5, 2.0)):
        mx.put(game_snd(("Enemy_Hit_A.wav", "Enemy_Hit_B.wav")[i % 2], rate=0.5),
               tt, 0.4, pan=(-0.5, 0.5, -0.3, 0.4)[i])
    # ---- 2. 3AM: 스프린터 슝슝
    for i, tt in enumerate((2.95, 3.55, 4.15)):
        mx.put(whoosh(0.4, up=False, gain=0.85, seed=i), tt - 0.08, pan=0.4 - 0.4 * i)
        mx.put(game_snd("Enemy_Hit_C.ogg", rate=1.3), tt + 0.12, 0.35)
    # ---- 3. 선택: 땡(X) 딩(V)
    mx.put(note(110, 0.4, 0.5, "organ"), 5.15)          # 오답 부저
    mx.put(note(nf("C6"), 0.5, 0.4, "glock"), 5.95)
    mx.put(note(nf("E6"), 0.5, 0.3, "glock"), 6.05)
    mx.put(game_snd("Weapon_RapidFire.wav"), 6.3, 0.3, pan=0.2)
    mx.put(game_snd("Weapon_RapidFire.wav"), 6.5, 0.3, pan=0.2)
    # ---- 4. 총 붙이기: 착착착
    for (g, t0, _ox, _oyr, _rot) in GUN_SCHED:
        mx.put(game_snd("UI_Click.wav"), 7.2 + t0, 0.8)
        mx.put(game_snd("Weapon_Melee.wav"), 7.2 + t0 + 0.03, 0.5)
    for k in range(9):                                   # 일제 사격
        mx.put(game_snd("Weapon_RapidFire.wav"), 9.55 + k * 0.05, 0.4,
               pan=(k % 3 - 1) * 0.4)
    mx.put(game_snd("Weapon_Explosive.wav"), 9.8, 0.6)
    # ---- 5. 왕관: 라이저 + 성가 + 짝퉁 에어혼
    mx.put(riser(0.9, 200, 1200, 0.5), 9.6)
    mx.put(game_snd("LevelUp.wav"), 10.12, 0.9)
    for i, nm in enumerate(("C5", "E5", "G5")):          # 천사 화음
        mx.put(note(nf(nm), 1.6, 0.22, "ep"), 10.15 + i * 0.02)
    for i in range(3):                                   # 에어혼 삼연타
        mx.put(note(nf("E5") * (1 - 0.06 * i), 0.30, 0.5, "organ"), 11.05 + i * 0.17)
    mx.put(boom(0.5, 140, 40, 0.7), 11.6)
    # ---- 6. 보스: 드론 + 경보 + 포효
    mx.put(bass808(2.7, nf("E1"), 0.65), 12.45)
    for i in range(5):
        mx.put(note(nf("A4"), 0.16, 0.30, "organ"), 12.6 + i * 0.5, pan=0.3)
    mx.put(game_snd("Boss_Death.wav", rate=0.65), 13.5, 0.85)   # 포효로 재활용
    mx.put(game_snd("Boss_Hit_A.wav", rate=0.7), 14.5, 0.6)
    # ---- 7. CTA
    mx.put(game_snd("LevelUp.wav"), 15.25, 0.9)
    for i, nm in enumerate(("C5", "E5", "G5", "C6")):
        mx.put(note(nf(nm), 0.5, 0.18, "glock"), 15.3 + i * 0.07)
    mx.put(boom(0.5, 150, 42, 0.8), 16.0)                # ZOMBIES DO 슬램
    for i in range(6):                                   # 좀비 연타
        mx.put(game_snd("UI_Click.wav"), 16.8 + i * 0.4, 0.55, pan=0.25)
    mx.put(game_snd("Enemy_Hit_A.wav", rate=0.6), 17.7, 0.4, pan=0.4)
    mx.put(boom(0.7, 130, 34, 0.9), 19.35)               # 마지막 붐
    return mx.write(path, master=0.93)


# ---------------------------------------------------------------- 메인
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--test", default=None, help="미리보기 시각(초, 쉼표 구분)")
    ap.add_argument("--out", default=None)
    ap.add_argument("--fast", action="store_true")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    if args.test:
        outdir = os.path.join(CACHE, "preview_meme")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            im = render_frame(int(round(t * FPS)))
            p = os.path.join(outdir, "ig_%05.2f.png" % t)
            im.save(p)
            print(p)
        return

    os.makedirs(CACHE, exist_ok=True)
    audio = build_audio(os.path.join(CACHE, "meme_audio.wav"))
    out = args.out or os.path.join(here, "Comstock_Meme_IG.mp4")
    encode_frames(render_frame, NFRAMES, FPS, (W, H), out, audio=audio,
                  crf=22, label="meme", preset="veryfast" if args.fast else "medium")
    print("완성:", out)
    web = os.path.splitext(out)[0] + "_web.mp4"
    make_web_version(out, web, height=1280, crf=26)
    print("웹용:", web)


if __name__ == "__main__":
    main()
