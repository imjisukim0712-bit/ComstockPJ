# -*- coding: utf-8 -*-
"""컴스톡 병맛 PV #2 - 인스타그램 릴스 밈 몽타주. 9:16 1080x1920, 30fps, 20초.

밈 문법을 그대로 따른다: 흰 캡션 바 + 산세리프, 컷마다 바인 붐, 그리고
마지막은 반드시 "좀비보다 먼저 다운로드" + itch.io 링크. CTA의 다운로드 경쟁
게이지가 "좀비들도 와이파이가 있습니다" 개그를 받쳐 준다.
(2026-08-29 리뉴얼: 가짜 SNS 오버레이 삭제, 중간 컷을 게임 시스템 개그로 교체)

사용법:
    python3 pv2_meme.py --lang en            # dev/pv2/Comstock_Meme_IG_EN.mp4
    python3 pv2_meme.py --lang ko            # dev/pv2/Comstock_Meme_IG_KO.mp4
    python3 pv2_meme.py --test 1.0,3.4,6.0   # 미리보기 PNG만
"""
import argparse
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv2_common import (A, EMOJI, F, Mixer, ITCH_URL, SEQ, SPR, WPN, bass808,
                        blit, boom, boss_img, clamp, clap, draw_robot, ease_in,
                        ease_out, emoji_line, encode_frames, explosion_at,
                        fast_post, fit_size, game_snd, gradient_v, ground_tex,
                        hat, kick808, make_web_version, muzzle_at, note, nf,
                        otext, pop_scale, rgb_shift, riser, robot_img,
                        ruined_skyline, screen_flash, set_lang, speed_lines,
                        sub_drop, sunburst, text_w, twos, whoosh, zombie_img,
                        zoom_at, BILINEAR, LANCZOS, CACHE)

W, H = 1080, 1920
FPS = 30
DUR = 20.0
NFRAMES = int(FPS * DUR)

BPM = 150.0
BEAT = 60.0 / BPM          # 0.4초
BAR = BEAT * 4             # 1.6초

# (시작, 길이, 이름). 컷은 비트 경계에 맞춘다. 합계 20.0초.
TIMELINE = [
    (0.0, 2.8, "howit"),    # 캐릭터 성장 과정: 1일차 vs 20일차
    (2.8, 2.4, "shop"),     # 웨이브 사이 상점 싹쓸이
    (5.2, 2.4, "levelup"),  # 레벨업 3택 (전부 총)
    (7.6, 2.4, "dodge"),    # 구르기로 전부 회피
    (10.0, 2.4, "povz"),    # POV: 당신이 좀비
    (12.4, 2.8, "boss"),    # 웨이브 20 보스 🗿
    (15.2, 4.8, "cta"),     # 좀비보다 먼저 다운로드 (+ 다운로드 경쟁 게이지)
]
CUTS = [t for (t, _d, _n) in TIMELINE][1:]

INK = (28, 26, 30)
CREAM = (250, 246, 236)

# 화면에 보이는 모든 문구는 영어/한글 두 벌을 함께 관리한다(협업 규칙 9번과 같은 원칙).
LANG = {
    "en": {
        "howit_cap": "character development:",
        "howit1": "day 1",
        "howit2": "day 20",
        "shop1": "nobody:",
        "shop2": "me in the shop between waves:",
        "shop_gold": "GOLD: %d",
        "lvl_cap": "the level-up options:",
        "lvl_cards": ["gun", "more gun", "gun (shiny)"],
        "lvl_all": "yes.",
        "dodge_cap": "me dodging zombies, taxes,\nand feelings",
        "povz_cap": "POV: you're the zombie",
        "boss_cap": "wave 20 boss:",
        "boss_you": "you",
        "cta1": "DOWNLOAD IT",
        "cta2": "BEFORE THE",
        "cta3": "ZOMBIES DO",
        "free1": "FREE",
        "free2": "on itch.io",
        "race_you": "you:  %d%%",
        "race_z": "zombies:  %d%%",
        "cta_btn": "DOWNLOAD",
        "cta_hurry": "the zombies have wifi. HURRY.",
        "cta_bio": "link in bio*   (*jk. it's right here)",
    },
    "ko": {
        "howit_cap": "캐릭터 성장 과정:",
        "howit1": "1일차",
        "howit2": "20일차",
        "shop1": "아무도:",
        "shop2": "웨이브 사이 상점에서 나:",
        "shop_gold": "골드: %d",
        "lvl_cap": "레벨업 선택지:",
        "lvl_cards": ["총", "더 많은 총", "총 (반짝임)"],
        "lvl_all": "전부요.",
        "dodge_cap": "좀비도, 세금도, 감정도\n구르기로 회피하는 나",
        "povz_cap": "POV: 당신이 좀비",
        "boss_cap": "웨이브 20 보스:",
        "boss_you": "당신",
        "cta1": "좀비보다",
        "cta2": "먼저",
        "cta3": "다운로드",
        "free1": "무료",
        "free2": "itch.io에서",
        "race_you": "나:  %d%%",
        "race_z": "좀비:  %d%%",
        "cta_btn": "다운로드",
        "cta_hurry": "좀비들도 와이파이가 있습니다. 서두르세요.",
        "cta_bio": "링크는 프로필에*   (*뻥임. 바로 여기 있음)",
    },
}
T = LANG["en"]


def set_video_lang(lang):
    """문구 사전과 폰트 대체(한글=NotoSansKR)를 함께 전환한다."""
    global T
    T = LANG[lang]
    set_lang(lang)


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


def panel_frame(cnv, x0, y0, x1, y1):
    d = ImageDraw.Draw(cnv)
    d.rounded_rectangle([x0 - 8, y0 - 8, x1 + 8, y1 + 8], radius=26,
                        fill=(255, 255, 255), outline=(60, 56, 60), width=5)


# ---------------------------------------------------------------- 1. 성장 과정
def sc_howit(cnv, t, dur):
    cnv.paste((236, 232, 226), (0, 0, W, H))
    top = meme_bar(cnv, [(T["howit_cap"], INK)], y0=140, size=58)
    pw, ph = W - 120, 560
    y1 = top + 80
    y2 = y1 + ph + 96

    # ---- 1일차: 칼 한 자루, 덜덜, 좀비들이 다가온다
    p1 = Image.new("RGB", (pw, ph), (52, 54, 76))
    p1.paste(gradient_v(pw, ph, (46, 48, 72), (76, 72, 96)), (0, 0))
    d1 = ImageDraw.Draw(p1, "RGBA")
    gy = ph - 56
    d1.rectangle([0, gy, pw, ph], fill=(64, 58, 70))
    d1.line([(0, gy), (pw, gy)], fill=(28, 24, 32), width=6)
    rob = draw_robot(p1, pw * 0.26, gy + 16, 300, t, bounce=False,
                     rot=math.sin(t * 28) * 3.5)
    blit(p1, SPR("SurvivalKnife.png", h=110, rot=24), rob["x"] + 120,
         rob["y"] - rob["h"] * 0.42, anchor="cc")
    for k in range(2):                     # 식은땀
        phs = (t * 1.8 + k * 0.5) % 1.0
        d1.ellipse([rob["top"][0] + 60 + k * 26, rob["top"][1] + 20 + phs * 40,
                    rob["top"][0] + 74 + k * 26, rob["top"][1] + 40 + phs * 40],
                   fill=(180, 220, 255, int(220 * (1 - phs))))
    for i in range(3):
        blit(p1, zombie_img(t * 0.8, i, "Zombie", h=230, face=-1),
             pw * 0.68 + i * 130 - 26 * t, gy + 10, anchor="cb")

    # ---- 20일차: 왕관 + 총기 전신 무장, 좀비들이 도망간다
    p2 = Image.new("RGB", (pw, ph), (40, 40, 40))
    p2.paste(sunburst(pw, ph, twos(t) * 40, c1=(255, 214, 60), c2=(255, 172, 40)),
             (0, 0))
    d2 = ImageDraw.Draw(p2, "RGBA")
    gy2 = ph - 56
    d2.rectangle([0, gy2, pw, ph], fill=(150, 120, 60))
    d2.line([(0, gy2), (pw, gy2)], fill=(70, 52, 26), width=6)
    rob2 = draw_robot(p2, pw * 0.30, gy2 + 16, 330, t, bounce=True)
    for (g, ox, oyr, rot) in (("SMG", -40, -0.62, -14), ("SawedOff", -110, -0.28, -36),
                              ("LaserPistol", 96, -0.5, 26),
                              ("RocketLauncher", 66, -0.12, -8)):
        blit(p2, WPN(g, +1, 96).rotate(rot, resample=BILINEAR, expand=True),
             rob2["x"] + ox, rob2["y"] + oyr * rob2["h"], anchor="cc")
    blit(p2, SPR("Accessories/Crown-transparent.png", w=int(rob2["head_w"] * 1.0)),
         rob2["top"][0] + 4, rob2["top"][1] + 6, anchor="cb")
    muzzle_at(p2, rob2["x"] + 210, rob2["y"] - rob2["h"] * 0.30, t, size=110, seed=1)
    for i in range(3):                     # 도망가는 좀비(오른쪽으로, 뒤돌아서)
        zx = pw * 0.62 + i * 120 + 90 * t
        blit(p2, zombie_img(t, 3 + i, "Sprinter" if i == 1 else "Zombie",
                            h=220, face=1), zx, gy2 + 10, anchor="cb")
        for k in range(3):
            ly = gy2 - 120 - (i % 2) * 40 + k * 26
            d2.line([(zx - 200 - k * 16, ly), (zx - 90, ly)],
                    fill=(255, 255, 255, 170), width=7)

    panel_frame(cnv, 60, y1, 60 + pw, y1 + ph)
    cnv.paste(p1, (60, y1))
    otext(cnv, (60 + 36, y1 + 44), T["howit1"], "anton", 54, fill=(255, 255, 255),
          anchor="lm", stroke=7, stroke_fill=(20, 18, 24))
    if t > 0.9:                            # 20일차 패널이 쾅 등장
        q = pop_scale(t - 0.9, 0.22, 0.6)
        panel_frame(cnv, 60, y2, 60 + pw, y2 + ph)
        cnv.paste(p2, (60, y2))
        otext(cnv, (60 + 36, y2 + 44), T["howit2"], "anton", 54, fill=(255, 255, 255),
              anchor="lm", stroke=7, stroke_fill=(120, 70, 10))
        if q < 1.0:
            screen_flash(cnv, (1 - q) * 0.35)


# ---------------------------------------------------------------- 2. 상점
# 로봇이 x = -220 + 1520·(t/2.3) 로 등속 이동하므로, 각 아이템의 흡수 시각은
# 로봇이 그 선반 칸 아래를 지나는 순간과 일치시킨다.
SHOP_ITEMS = (("SMG", 0.64), ("CombatShotgun", 0.90), ("LaserPistol", 1.15),
              ("GrenadeLauncher", 1.40), ("HMG", 1.66))


def sc_shop(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (86, 62, 46), (60, 42, 32)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    top = meme_bar(cnv, [(T["shop1"], (120, 116, 124)), (T["shop2"], INK)],
                   y0=150, size=56)

    # 선반 두 단
    shelf_y = (980, 1330)
    for sy in shelf_y:
        d.rectangle([90, sy, W - 90, sy + 34], fill=(122, 84, 52))
        d.rectangle([90, sy, W - 90, sy + 12], fill=(150, 106, 66))
        d.line([(90, sy + 34), (W - 90, sy + 34)], fill=(50, 34, 22), width=6)

    # 로봇이 카트처럼 밀고 지나가며 전부 쓸어담는다(등속)
    rx = -220 + (W + 440) * clamp(t / 2.3)
    rob = draw_robot(cnv, rx, 1700, 340, t, bounce=True,
                     rot=math.sin(twos(t) * 6) * 4)

    for i, (g, tg) in enumerate(SHOP_ITEMS):
        sy = shelf_y[i % 2]
        ix = 210 + i * 170
        if t < tg:                         # 아직 선반 위
            blit(cnv, WPN(g, +1, 120), ix, sy - 4, anchor="cb")
        elif t < tg + 0.30:                # 로봇 쪽으로 빨려 들어간다
            q = ease_in((t - tg) / 0.30)
            fx = ix + (rob["x"] - ix) * q
            fy = (sy - 60) + (rob["y"] - rob["h"] * 0.4 - (sy - 60)) * q
            blit(cnv, WPN(g, +1, int(120 * (1 - 0.4 * q))).rotate(
                q * 200, resample=BILINEAR, expand=True), fx, fy, anchor="cc")
        # 흡수 후에는 사라진다(로봇이 이미 총투성이다)
        if tg <= t < tg + 0.5:             # 금화가 튄다
            gp = (t - tg) / 0.5
            blit(cnv, SPR("Gold.png", h=48), ix, sy - 90 - 130 * ease_out(gp),
                 anchor="cc", alpha=1 - ease_in(gp))
    if t >= 1.76 and t < 2.06:             # 마지막엔 아이템 상자까지
        q = ease_in((t - 1.76) / 0.30)
        blit(cnv, SPR("ItemBox.png", h=140), 940 + (rob["x"] - 940) * q,
             1300 - 200 * q * (1 - q) * 4 + (rob["y"] - rob["h"] * 0.4 - 1300) * q,
             anchor="cc")
    elif t < 1.76:
        blit(cnv, SPR("ItemBox.png", h=140), 940, shelf_y[1] - 4, anchor="cb")

    # 골드 카운터(신나게 깎인다)
    gold = max(0, int(347 * (1 - clamp((t - 0.25) / 1.6))))
    d.rounded_rectangle([W - 360, 560, W - 70, 668], radius=20,
                        fill=(16, 14, 18, 225), outline=(255, 214, 64, 255), width=5)
    otext(cnv, (W - 215, 614), T["shop_gold"] % gold, "roboto", 46,
          fill=(255, 224, 90), anchor="mm", max_w=260)
    if t > 2.1:                            # 탈탈 털고 퇴장
        speed_lines(cnv, rob["x"] - 160, rob["y"] - 200, 8, 60, 200,
                    seed=int(t * 20), width=6, color=(255, 255, 255, 180))
    out = zoom_at(cnv, beat_pulse(t, 0.03), W / 2, 1200)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 3. 레벨업
GRADE = ((168, 168, 172), (86, 140, 255), (255, 196, 44))   # 일반/레어/레전더리


def sc_levelup(cnv, t, dur):
    night_bg(cnv, t, moon=False, dark=0.8)
    dim = Image.new("RGBA", (W, H), (12, 10, 24, 120))
    cnv.paste(dim, (0, 0), dim)
    d = ImageDraw.Draw(cnv, "RGBA")
    top = meme_bar(cnv, [(T["lvl_cap"], INK)], y0=150, size=58)

    # 레벨업 이펙트를 뒤집어쓴 로봇(아래쪽에 작게)
    fr = SEQ("LevelUpEffect")
    rob = draw_robot(cnv, W / 2, 1800, 280, t, bounce=True)
    blit(cnv, SPR(fr[int(t * 20) % len(fr)], h=430), rob["x"],
         rob["y"] - rob["h"] * 0.45, anchor="cc", alpha=0.9)

    cards = ((W / 2 - 330, 0.30, 0), (W / 2, 0.55, 1), (W / 2 + 330, 0.80, 2))
    cw, ch = 300, 430
    cy = 900
    icons = (("🔫",), ("🔫", "🔫"), ("👑", "🔫"))
    for (cx, t0, gi) in cards:
        if t < t0:
            continue
        q = pop_scale(t - t0, 0.2, 0.8)
        w2, h2 = cw * q / 2, ch * q / 2
        col = GRADE[gi]
        d.rounded_rectangle([cx - w2, cy - h2, cx + w2, cy + h2], radius=24,
                            fill=(30, 28, 40, 245), outline=col + (255,), width=8)
        if gi == 2:                        # 레전더리는 반짝인다
            rng = random.Random(int(t * 10))
            for _ in range(3):
                sx = cx + rng.uniform(-w2, w2)
                sy = cy + rng.uniform(-h2, h2)
                ln = rng.randint(8, 20)
                d.line([(sx - ln, sy), (sx + ln, sy)], fill=(255, 240, 170, 220),
                       width=5)
                d.line([(sx, sy - ln), (sx, sy + ln)], fill=(255, 240, 170, 220),
                       width=5)
        ix0 = cx - (len(icons[gi]) - 1) * 55
        for k, ic in enumerate(icons[gi]):
            blit(cnv, EMOJI(ic, int(120 * q)), ix0 + k * 110, cy - 60 * q,
                 anchor="cc")
        otext(cnv, (cx, cy + 110 * q), T["lvl_cards"][gi], "roboto", int(42 * q),
              fill=(240, 238, 246), anchor="mm", max_w=int(cw * q - 40))
    # 셋 다 고른다
    if t > 1.5:
        for i, (cx, _t0, _gi) in enumerate(cards):
            qq = pop_scale(t - 1.5 - i * 0.12, 0.18, 1.0)
            if t > 1.5 + i * 0.12:
                blit(cnv, EMOJI("✅", int(150 * qq)), cx + 90, cy - 160, anchor="cc")
    if t > 1.95:
        q = pop_scale(t - 1.95, 0.2, 0.9)
        otext(cnv, (W / 2, 1420), T["lvl_all"], "anton", int(130 * q),
              fill=(255, 255, 255), anchor="mm", stroke=10, stroke_fill=(20, 18, 26),
              max_w=W - 200)


# ---------------------------------------------------------------- 4. 구르기
DODGE_ZOMBIES = (0.35, 0.95, 1.55)         # 각 좀비 앞을 지나는 시각


def sc_dodge(cnv, t, dur):
    night_bg(cnv, t, camx=t * 120, moon=True, dark=1.0)
    d = ImageDraw.Draw(cnv, "RGBA")
    cap_lines = [(s, INK) for s in T["dodge_cap"].split("\n")]
    meme_bar(cnv, cap_lines, y0=150, size=56)

    gy = 1560
    rx = -160 + (W + 320) * (t / dur)
    # 좀비들이 헛스윙한다
    att = SEQ("ZombieAttack")
    for i, tz in enumerate(DODGE_ZOMBIES):
        zx = -160 + (W + 320) * (tz / dur) + 10
        swing = clamp((t - (tz - 0.25)) / 0.4)
        if swing <= 0 or swing >= 1:
            blit(cnv, zombie_img(t, i, "Zombie", h=300, face=(-1 if zx > rx else 1)),
                 zx, gy + 10, anchor="cb")
        else:
            fr = att[int(swing * (len(att) - 1))]
            blit(cnv, SPR(fr, h=300, flip=(zx < rx)), zx, gy + 10, anchor="cb")
        if 0.1 < swing < 0.6:              # 헛스윙 이펙트
            d.arc([zx - 130, gy - 300, zx + 130, gy - 40], 300, 60,
                  fill=(255, 255, 255, 200), width=9)

    # 데굴데굴 구르는 로봇 + 먼지
    dust = SEQ("RollDust")
    for k in range(3):
        dxp = rx - 130 - k * 120
        if dxp > -100:
            blit(cnv, SPR(dust[(int(t * 18) + k) % len(dust)], h=170 + k * 20),
                 dxp, gy - 30, anchor="cc", alpha=0.75 - k * 0.2)
    hop = -abs(math.sin(t * 9)) * 40
    spr = robot_img(300, rot=(-t * 640) % 360)
    blit(cnv, spr, rx, gy + hop - 110, anchor="cc")
    for k in range(4):                     # 속도선
        ly = gy - 220 + k * 60
        d.line([(rx - 420 - k * 20, ly), (rx - 190, ly)],
               fill=(255, 255, 255, 150), width=7)


# ---------------------------------------------------------------- 5. POV 좀비
def sc_povz(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (34, 20, 26), (66, 30, 34)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    g = ground_tex(W, 320, dark=0.5)
    cnv.paste(g, (0, H - 320))
    d.line([(0, H - 320), (W, H - 320)], fill=(14, 10, 12), width=7)

    q = clamp(t / 0.5)
    rob = draw_robot(cnv, W / 2, 1560, int(430 + 60 * ease_out(q)), t, bounce=True)
    guns = (("SMG", -170, -0.60, -30), ("SawedOff", 170, -0.58, 30),
            ("LaserPistol", -230, -0.30, -12), ("GrenadeLauncher", 230, -0.30, 12),
            ("RocketLauncher", 0, -0.80, 0))
    for (gname, ox, oyr, rot) in guns:
        spr = WPN(gname, +1 if ox >= 0 else -1, 130)
        blit(cnv, spr.rotate(rot + (14 if ox >= 0 else -14) * math.sin(t * 3),
                             resample=BILINEAR, expand=True),
             rob["x"] + ox, rob["y"] + oyr * rob["h"], anchor="cc")
    # 레이저 조준점이 화면(=시청자)으로 모인다
    if t > 0.5:
        for i, (ox, oyr) in enumerate(((-170, -0.6), (170, -0.58), (0, -0.8))):
            lx, ly = rob["x"] + ox, rob["y"] + oyr * rob["h"]
            tx = W / 2 + math.sin(t * 5 + i * 2) * 60
            ty = H - 160 + math.cos(t * 4 + i) * 30
            d.line([(lx, ly), (tx, ty)], fill=(255, 40, 40, 140), width=6)
            d.ellipse([tx - 16, ty - 16, tx + 16, ty + 16], fill=(255, 50, 50, 220))
    # 일제 사격 + 화면 금 가기
    if t > 1.35:
        for k, (ox, oyr) in enumerate(((-170, -0.6), (170, -0.58), (0, -0.8),
                                       (-230, -0.3), (230, -0.3))):
            muzzle_at(cnv, rob["x"] + ox, rob["y"] + oyr * rob["h"] - 40, t,
                      size=130, seed=k)
        if int(t * 30) % 3 == 0:
            screen_flash(cnv, 0.18)
    if t > 1.6:                            # 렌즈에 금이 간다
        rng = random.Random(7)
        cq = clamp((t - 1.6) / 0.5)
        ccx, ccy = W / 2, H * 0.62
        for i in range(int(10 * cq) + 3):
            a = rng.random() * math.tau
            r1 = 40 + rng.random() * 90
            r2 = r1 + (140 + rng.random() * 420) * cq
            mx = ccx + (r1 + r2) / 2 * math.cos(a + 0.2)
            my = ccy + (r1 + r2) / 2 * math.sin(a + 0.2)
            d.line([(ccx + r1 * math.cos(a), ccy + r1 * math.sin(a)), (mx, my),
                    (ccx + r2 * math.cos(a + 0.12), ccy + r2 * math.sin(a + 0.12))],
                   fill=(255, 255, 255, 210), width=5)
    if t > 1.95:
        blit(cnv, EMOJI("💀", int(300 * pop_scale(t - 1.95, 0.2, 0.9))),
             W / 2, H * 0.62, anchor="cc")
    meme_bar(cnv, [(T["povz_cap"], (245, 240, 238))], y0=170, size=62,
             bar_fill=(20, 14, 16, 235), bar_line=(150, 40, 40, 255))


# ---------------------------------------------------------------- 6. 보스
def sc_boss(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (30, 12, 16), (74, 22, 26)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    al = 0.5 + 0.5 * math.sin(t * 9)
    d.rectangle([0, 0, W, H], fill=(120, 10, 14, int(44 * al)))
    g = ground_tex(W, 240, dark=0.42)
    cnv.paste(g, (0, H - 240))
    d.line([(0, H - 240), (W, H - 240)], fill=(12, 8, 10), width=8)

    p = clamp(t / dur)
    bh = 1150 + 420 * ease_out(clamp(t / 2.2))
    bs = boss_img(t, h=int(bh), roar=(t > 1.1))
    blit(cnv, bs, W / 2, 320 + bh / 2, anchor="cc")
    # 스케일용 꼬마 로봇 - 떨면서도 일단 쏜다
    rob = draw_robot(cnv, 230, H - 300, 210, t, bounce=False,
                     rot=math.sin(t * 30) * 4)
    if t > 0.8 and int(t * 15) % 2 == 0:
        mx, my = rob["x"] + 90, rob["y"] - rob["h"] * 0.55
        muzzle_at(cnv, mx, my, t, size=90, seed=3, rot=50)
        d.line([(mx, my), (W / 2 + math.sin(t * 7) * 120, 760)],
               fill=(255, 238, 120, 190), width=7)
    otext(cnv, (230, H - 540), T["boss_you"], "roboto", 44, fill=(255, 255, 255),
          anchor="mm", stroke=6, stroke_fill=(20, 10, 12))
    if t > 1.35:
        s = pop_scale(t - 1.35, 0.24, 0.9)
        blit(cnv, EMOJI("🗿", int(430 * s)), W - 260, 1500, anchor="cc")
    if t > 0.2:
        meme_bar(cnv, [(T["boss_cap"], (245, 240, 238))], y0=170, size=62,
                 bar_fill=(20, 14, 16, 235), bar_line=(150, 40, 40, 255))
    out = zoom_at(cnv, 1.0 + 0.07 * ease_in(p), W / 2, 800)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 7. CTA
def race_bar(d, x0, x1, y, hgt, frac, label, fill_col):
    d.rounded_rectangle([x0, y, x1, y + hgt], radius=hgt // 2,
                        fill=(228, 222, 208, 255), outline=(60, 56, 60, 255),
                        width=5)
    wpx = int((x1 - x0 - 12) * clamp(frac))
    if wpx > hgt:
        d.rounded_rectangle([x0 + 6, y + 6, x0 + 6 + wpx, y + hgt - 6],
                            radius=(hgt - 12) // 2, fill=fill_col + (255,))
    return y + hgt / 2


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
    if t > 1.15:
        q = pop_scale(t - 1.15, 0.22, 0.9)
        bs = int(225 * q)
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
        blit(cnv, star, 175, 1170, anchor="cc")
        otext(cnv, (175, 1132), T["free1"], "anton", int(64 * q), fill=INK,
              anchor="mm", max_w=280)
        otext(cnv, (175, 1196), T["free2"], "roboto", int(29 * q), fill=INK,
              anchor="mm", max_w=280)

    # 다운로드 경쟁 게이지 - "좀비들도 와이파이가 있습니다"의 근거
    if t > 1.45:
        you = min(7, int(2 + (t - 1.45) * 1.8))
        zom = min(99, int(34 + (t - 1.45) * 26))
        ly = race_bar(d, 400, W - 90, 1090, 76, you / 100, "", (208, 52, 44))
        otext(cnv, (416, ly), T["race_you"] % you, "roboto", 38, fill=INK,
              anchor="lm", max_w=520)
        ly2 = race_bar(d, 400, W - 90, 1196, 76, zom / 100, "", (86, 176, 92))
        otext(cnv, (416, ly2), T["race_z"] % zom, "roboto", 38, fill=INK,
              anchor="lm", max_w=520)
        blit(cnv, EMOJI("🤖", 66), 352, ly, anchor="cc")
        blit(cnv, EMOJI("🧟", 66), 352, ly2, anchor="cc")

    # 거대 다운로드 버튼 + 좀비 두 마리가 양옆에서 연타
    if t > 1.55:
        press = math.exp(-((t * 2.5) % 1) * 7)          # 0.4초마다 꾹
        bw, bh = 640, 190
        bx, by = W / 2, 1430
        sq = 1.0 - 0.07 * press
        d.rounded_rectangle([bx - bw / 2 * sq, by - bh / 2 * sq + 14,
                             bx + bw / 2 * sq, by + bh / 2 * sq + 14],
                            radius=40, fill=(44, 120, 52, 255))
        d.rounded_rectangle([bx - bw / 2 * sq, by - bh / 2 * sq - 10 * (1 - press),
                             bx + bw / 2 * sq, by + bh / 2 * sq - 10 * (1 - press)],
                            radius=40, fill=(86, 190, 96, 255),
                            outline=(30, 70, 36, 255), width=7)
        aw = 26 * sq
        ax = bx - text_w(T["cta_btn"], "oswald", int(64 * sq)) / 2 - aw - 26
        ay = by - 12 * (1 - press)
        d.polygon([(ax - aw * 0.45, ay - aw), (ax + aw * 0.45, ay - aw),
                   (ax + aw * 0.45, ay), (ax + aw, ay), (ax, ay + aw),
                   (ax - aw, ay), (ax - aw * 0.45, ay)], fill=(255, 255, 255, 255))
        otext(cnv, (bx + aw, by - 12 * (1 - press)), T["cta_btn"], "oswald",
              int(64 * sq), fill=(255, 255, 255), anchor="mm")
        zp = clamp((t - 1.55) / 0.5)
        blit(cnv, zombie_img(t, 3, "Zombie", h=420, face=-1),
             W - 130 + 40 * (1 - ease_out(zp)), by + 250, anchor="cb")
        blit(cnv, zombie_img(t + 0.2, 5, "Zombie", h=400, face=1),
             130 - 40 * (1 - ease_out(zp)), by + 250, anchor="cb")
    if t > 2.5:
        otext(cnv, (W / 2, 1600), T["cta_hurry"], "roboto", 44, fill=INK,
              anchor="mm", max_w=760)
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


SCENES = {"howit": sc_howit, "shop": sc_shop, "levelup": sc_levelup,
          "dodge": sc_dodge, "povz": sc_povz, "boss": sc_boss, "cta": sc_cta}


# ---------------------------------------------------------------- 렌더
def shake_at(t):
    name, tl, _d = scene_at(t)
    v = 0.0
    for c in CUTS:                          # 컷마다 화면이 울린다
        if 0 <= t - c < 0.22:
            v = max(v, 14.0 * (1 - (t - c) / 0.22))
    if name == "howit" and 0.9 <= tl < 1.2:
        v = max(v, 11.0)
    if name == "levelup" and 1.95 <= tl < 2.2:
        v = max(v, 8.0)
    if name == "dodge":
        v = max(v, 3.0)
    if name == "povz" and tl > 1.35:
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

    # ---- 1. 성장 과정: 1일차는 처량하게, 20일차는 신나게
    mx.put(note(nf("E3"), 0.7, 0.28, "ep"), 0.25)
    mx.put(note(nf("C3"), 0.9, 0.26, "ep"), 0.65)
    mx.put(game_snd("Enemy_Hit_A.wav", rate=0.55), 0.5, 0.4, pan=0.4)
    mx.put(boom(0.5, 140, 42, 0.8), 0.92)              # 20일차 쾅
    mx.put(game_snd("LevelUp.wav"), 0.96, 0.85)
    for k in range(6):
        mx.put(game_snd("Weapon_RapidFire.wav"), 1.25 + k * 0.22, 0.30, pan=0.3)
    mx.put(game_snd("Enemy_Hit_C.ogg", rate=1.25), 1.9, 0.35, pan=0.5)

    # ---- 2. 상점: 착착 쓸어담는 소리 + 금전 등록기
    for (_g, tg) in SHOP_ITEMS:
        mx.put(game_snd("UI_Click.wav"), 2.8 + tg, 0.7)
        mx.put(note(nf("A5"), 0.25, 0.15, "glock"), 2.8 + tg + 0.06, pan=0.2)
    mx.put(game_snd("UI_Click.wav"), 4.58, 0.7)        # 아이템 상자
    mx.put(game_snd("LevelUp.wav"), 4.82, 0.7)         # 결제 완료(?)
    mx.put(whoosh(0.4, up=True, gain=0.8, seed=3), 4.98)

    # ---- 3. 레벨업: 카드 3장 + 전부 선택
    for i, tc in enumerate((0.30, 0.55, 0.80)):
        mx.put(whoosh(0.25, up=True, gain=0.6, seed=10 + i), 5.2 + tc - 0.08)
        mx.put(game_snd("UI_Click.wav"), 5.2 + tc, 0.6, pan=(i - 1) * 0.4)
    for i in range(3):                                  # 체크 3연타
        mx.put(game_snd("UI_Click.wav"), 6.7 + i * 0.12, 0.75, pan=(i - 1) * 0.4)
        mx.put(note(nf(("C6", "E6", "G6")[i]), 0.4, 0.22, "glock"), 6.7 + i * 0.12)
    mx.put(boom(0.5, 130, 40, 0.75), 7.17)              # "전부요."

    # ---- 4. 구르기: 슝슝 + 헛스윙
    for i, tz in enumerate(DODGE_ZOMBIES):
        mx.put(whoosh(0.35, up=False, gain=0.75, seed=20 + i), 7.6 + tz - 0.2,
               pan=-0.3 + i * 0.3)
        mx.put(game_snd("Weapon_Melee.wav", rate=0.9), 7.6 + tz + 0.05, 0.45)
        mx.put(game_snd("Enemy_Hit_B.wav", rate=0.7), 7.6 + tz + 0.28, 0.3)
    mx.put(game_snd("Enemy_Hit_C.ogg", rate=0.6), 9.7, 0.4, pan=0.4)  # 분한 신음

    # ---- 5. POV 좀비: 조준 → 일제 사격 → 화면 파손
    mx.put(riser(0.8, 180, 900, 0.45), 10.5)
    for k in range(8):
        mx.put(game_snd("Weapon_RapidFire.wav"), 11.35 + k * 0.07, 0.42,
               pan=(k % 3 - 1) * 0.4)
    mx.put(game_snd("Weapon_Explosive.wav"), 11.9, 0.6)
    mx.put(clap(0.9, seed=42), 12.0)                    # 렌즈 깨지는 소리
    mx.put(clap(0.7, seed=43), 12.1)
    mx.put(boom(0.5, 140, 40, 0.8), 12.32)              # 💀

    # ---- 6. 보스: 드론 + 경보 + 포효 + 꼬마 로봇의 발악
    mx.put(bass808(2.7, nf("E1"), 0.65), 12.45)
    for i in range(5):
        mx.put(note(nf("A4"), 0.16, 0.30, "organ"), 12.6 + i * 0.5, pan=0.3)
    mx.put(game_snd("Boss_Death.wav", rate=0.65), 13.5, 0.85)   # 포효로 재활용
    mx.put(game_snd("Boss_Hit_A.wav", rate=0.7), 14.5, 0.6)
    for k in range(9):
        mx.put(game_snd("Weapon_RapidFire.wav"), 13.3 + k * 0.17, 0.16, pan=-0.5)

    # ---- 7. CTA
    mx.put(game_snd("LevelUp.wav"), 15.25, 0.9)
    for i, nm in enumerate(("C5", "E5", "G5", "C6")):
        mx.put(note(nf(nm), 0.5, 0.18, "glock"), 15.3 + i * 0.07)
    mx.put(boom(0.5, 150, 42, 0.8), 16.0)                # 3번째 줄 슬램
    # 좀비 게이지가 차오르는 틱틱(점점 빠르게) + 양옆 좀비 연타
    tt, step = 16.7, 0.30
    while tt < 18.9:
        mx.put(note(nf("E6"), 0.08, 0.12, "glock"), tt, pan=0.3)
        step = max(0.10, step * 0.90)
        tt += step
    for i in range(6):
        mx.put(game_snd("UI_Click.wav"), 16.8 + i * 0.4, 0.5,
               pan=0.35 if i % 2 == 0 else -0.35)
    mx.put(game_snd("Enemy_Hit_A.wav", rate=0.6), 17.7, 0.4, pan=0.4)
    mx.put(boom(0.7, 130, 34, 0.9), 19.35)               # 마지막 붐
    return mx.write(path, master=0.93)


# ---------------------------------------------------------------- 메인
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="en", choices=("en", "ko"))
    ap.add_argument("--test", default=None, help="미리보기 시각(초, 쉼표 구분)")
    ap.add_argument("--out", default=None)
    ap.add_argument("--fast", action="store_true")
    args = ap.parse_args()
    set_video_lang(args.lang)

    here = os.path.dirname(os.path.abspath(__file__))
    if args.test:
        outdir = os.path.join(CACHE, "preview_meme")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            im = render_frame(int(round(t * FPS)))
            p = os.path.join(outdir, "ig_%s_%05.2f.png" % (args.lang, t))
            im.save(p)
            print(p)
        return

    os.makedirs(CACHE, exist_ok=True)
    audio = build_audio(os.path.join(CACHE, "meme_audio.wav"))
    out = args.out or os.path.join(here, "Comstock_Meme_IG_%s.mp4" % args.lang.upper())
    encode_frames(render_frame, NFRAMES, FPS, (W, H), out, audio=audio,
                  crf=22, label="meme-" + args.lang,
                  preset="veryfast" if args.fast else "medium")
    print("완성:", out)
    web = os.path.splitext(out)[0] + "_web.mp4"
    make_web_version(out, web, height=1280, crf=26)
    print("웹용:", web)


if __name__ == "__main__":
    main()
