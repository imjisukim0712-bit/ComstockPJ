# -*- coding: utf-8 -*-
"""컴스톡 병맛 PV #1 - 미국 처방약(제약) TV 광고 패러디. 16:9 1920x1080, 28초.

미국 제약 광고의 정석 구조를 그대로 따라간다:
  흑백의 우울(문제) → "Ask your doctor"(해결책 등장) → 초록 들판 행복 몽타주
  → 속사포 부작용 고지(블랙코미디 핵심) → CTA.
마지막은 반드시 "DOWNLOAD IT BEFORE THE ZOMBIES DO." + itch.io 링크.

사용법:
    python3 pv2_tvad.py --lang en           # dev/pv2/Comstock_Ad_TV_EN.mp4
    python3 pv2_tvad.py --lang ko           # dev/pv2/Comstock_Ad_TV_KO.mp4
    python3 pv2_tvad.py --test 1.0,5.0,9.0  # 미리보기 PNG만
"""
import argparse
import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv2_common import (A, SPR, WPN, EMOJI, F, Mixer, ITCH_URL, add_grain,
                        apply_vignette, bass808, blit, boom, clamp, desaturate,
                        draw_robot, ease_in, ease_out, encode_frames,
                        explosion_at, fast_post, fit_size, game_snd, gradient_v,
                        make_web_version, muzzle_at, note, nf, otext, pop_scale,
                        rain_noise, robot_img, ruined_skyline, screen_flash,
                        set_lang, sunburst, text_w, twos, zombie_img, zoom_at,
                        BILINEAR, LANCZOS, CACHE)

W, H = 1920, 1080
FPS = 24
DUR = 28.0
NFRAMES = int(FPS * DUR)
GROUND_Y = 940

# (시작, 길이, 이름). 합계 28.0초.
TIMELINE = [
    (0.0, 4.2, "gray"),          # 흑백의 우울 - "좀비 만성질환"
    (4.2, 3.0, "turn"),          # "Ask your doctor about COMSTOCK"
    (7.2, 6.0, "montage"),       # 들판 행복 몽타주 (총과 함께)
    (13.2, 5.0, "picnic"),       # 소중한 것(전리품)과 보내는 시간
    (18.2, 6.0, "sideeffects"),  # 속사포 부작용 고지
    (24.2, 3.8, "cta"),          # 무료 + 좀비보다 먼저 다운로드
]

CREAM = (250, 244, 228)
INK = (44, 40, 36)
PHARMA_GREEN = (86, 168, 96)
STAMP_RED = (208, 52, 44)

# 화면에 보이는 모든 문구는 영어/한글 두 벌을 함께 관리한다(협업 규칙 9번과 같은 원칙).
LANG = {
    "en": {
        "gray1": "Feeling... surrounded lately?",
        "gray2": "Chronic Zombies affect 10 out of 10 survivors.*",
        "gray_fine": "*the other 0 are already zombies.",
        "ask": "ASK YOUR DOCTOR ABOUT",
        "ask_sub": "COMSTOCK is a robot, not a medication.",
        "ask_badge": "NOW\nWITH\nGUNS",
        "mon1": "Get back out there.",
        "mon2": "Enjoy the little things. Then shoot them.",
        "mon3": "Up to 6 guns. Zero thoughts.",
        "mon_docs": "9 out of 10 doctors agree*",
        "mon_fine": "*the 10th doctor turned. he agrees much louder now.",
        "pic1": "Spend more time with what matters.*",
        "pic_fine": "*loot. what matters is loot.",
        "se_head": "COMSTOCK may cause side effects.",
        "se_incl": "side effects may include:",
        "se_list": ["excessive winning", "chronic modding",
                    "spontaneous gun acquisition", "crown-related confidence",
                    "compulsive shopping between waves", "mild robot smugness",
                    "zombie unemployment", "sudden onset of Wave 20",
                    "loss of loss", "fun"],
        "se_not1": "COMSTOCK is not for zombies.",
        "se_not2": "Zombies should not take COMSTOCK.",
        "cta_free": "FREE.  No prescription.  No ammo.  No refills.",
        "cta_stamp1": "DOWNLOAD IT BEFORE",
        "cta_stamp2": "THE ZOMBIES DO.",
        "cta_play": "PLAY FREE ON ITCH.IO",
        "cta_url": ITCH_URL,
        "cta_fine": "COMSTOCK is a video game. zombies cannot download it. probably.",
        "ticker": ("DO NOT OPERATE HEAVY MACHINERY UNLESS THE MACHINERY IS COMSTOCK   ·   "
                   "IF WINNING PERSISTS FOR MORE THAN 20 WAVES, THAT IS THE WHOLE GAME   ·   "
                   "ROBOT MAY CONTAIN ROBOT   ·   TALK TO YOUR DOCTOR. IF YOUR DOCTOR "
                   "GROANS, RUN   ·   SIDE EFFECTS ARE THE POINT   ·   "),
    },
    "ko": {
        "gray1": "요즘… 사방에서 조여오십니까?",
        "gray2": "만성 좀비는 생존자 10명 중 10명이 겪는 질환입니다.*",
        "gray_fine": "※ 나머지 0명은 이미 좀비입니다.",
        "ask": "주치의에게 문의하세요:",
        "ask_y": 92,       # 한글은 글리프가 세로로 꽉 차서 로고와 겹치지 않게 올린다
        "ask_sub": "컴스톡은 약이 아니라 로봇입니다.",
        "ask_badge": "이젠\n총도\n포함",
        "mon1": "다시 바깥으로 나가세요.",
        "mon2": "소소한 행복을 즐기세요. 그리고 쏘세요.",
        "mon3": "총 6정. 생각 0개.",
        "mon_docs": "의사 10명 중 9명이 동의*",
        "mon_fine": "※ 10번째 의사는 좀비가 됐습니다. 지금은 더 크게 동의합니다.",
        "pic1": "소중한 것과 더 많은 시간을 보내세요.*",
        "pic_fine": "※ 소중한 것 = 전리품.",
        "se_head": "컴스톡은 부작용을 유발할 수 있습니다.",
        "se_incl": "부작용 예시:",
        "se_list": ["과도한 승리", "만성 모딩", "돌발적 총기 획득", "왕관발 자신감",
                    "웨이브 사이 충동구매", "경미한 로봇 거만증", "좀비 실업",
                    "갑작스러운 웨이브 20", "패배 상실증", "재미"],
        "se_not1": "컴스톡은 좀비용이 아닙니다.",
        "se_not2": "좀비는 컴스톡을 복용하지 마십시오.",
        "cta_free": "무료.  처방전 없음.  탄약 없음.  리필 없음.",
        "cta_stamp1": "좀비보다 먼저",
        "cta_stamp2": "다운로드하세요.",
        "cta_play": "ITCH.IO에서 무료 플레이",
        "cta_url": ITCH_URL,
        "cta_fine": "컴스톡은 비디오 게임입니다. 좀비는 다운로드할 수 없습니다. 아마도요.",
        "ticker": ("컴스톡이 아닌 중장비는 조작하지 마십시오   ·   승리가 20웨이브 넘게 "
                   "지속되면 그게 정상입니다   ·   로봇에는 로봇이 함유되어 있습니다   ·   "
                   "의사와 상담하세요. 의사가 으르렁거리면 도망치세요   ·   "
                   "부작용이 곧 콘텐츠입니다   ·   "),
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


# ---------------------------------------------------------------- 무대(들판)
def cloud(idx):
    key = "@cl%d" % idx
    if key not in _misc:
        rng = random.Random(40 + idx)
        im = Image.new("RGBA", (360, 150), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        puffs = [(60 + i * 60 + rng.randint(-10, 10), 96 - rng.randint(0, 34),
                  rng.randint(36, 54)) for i in range(5)]
        for (cx, cy, r) in puffs:
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 255, 255, 255),
                      outline=(70, 90, 110, 255), width=6)
        for (cx, cy, r) in puffs:
            d.ellipse([cx - r + 5, cy - r + 5, cx + r - 5, cy + r - 5],
                      fill=(255, 255, 255, 255))
        d.rectangle([0, 100, 360, 150], fill=(0, 0, 0, 0))
        d.line([(26, 100), (334, 100)], fill=(70, 90, 110, 255), width=6)
        _misc[key] = im
    return _misc[key]


def flower(seed, sway):
    key = ("@fl", seed, round(sway, 1))
    if key not in _misc:
        rng = random.Random(seed)
        im = Image.new("RGBA", (64, 96), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        top = (32 + sway, 26)
        d.line([(32, 92), top], fill=(46, 110, 60, 255), width=5)
        petal_col = rng.choice([(255, 120, 150), (255, 200, 80), (170, 150, 255),
                                (255, 255, 255)])
        for k in range(6):
            a = math.tau * k / 6
            d.ellipse([top[0] - 8 + 14 * math.cos(a) - 8, top[1] + 14 * math.sin(a) - 8,
                       top[0] - 8 + 14 * math.cos(a) + 8, top[1] + 14 * math.sin(a) + 8],
                      fill=petal_col + (255,), outline=(60, 50, 40, 255), width=2)
        d.ellipse([top[0] - 16, top[1] - 8, top[0], top[1] + 8],
                  fill=(255, 214, 64, 255), outline=(60, 50, 40, 255), width=2)
        if len(_misc) > 700:
            _misc.clear()
        _misc[key] = im
    return _misc[key]


def butterfly(t, seed):
    im = Image.new("RGBA", (72, 56), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    flap = abs(math.sin(t * 14 + seed))
    wing_w = 12 + 16 * flap
    col = (255, 170, 60) if seed % 2 else (120, 190, 255)
    for sx in (-1, 1):
        d.ellipse([36 + sx * wing_w - 14, 12, 36 + sx * wing_w + 14, 40],
                  fill=col + (255,), outline=(60, 50, 40, 255), width=3)
    d.ellipse([32, 14, 40, 44], fill=(70, 55, 45, 255))
    return im


def meadow(cnv, t, camx=0.0, sunny=1.0):
    """파란 하늘 + 해 + 구름 + 초록 언덕 + 꽃밭."""
    cnv.paste(gradient_v(W, H, (128, 202, 255), (222, 244, 255)), (0, 0))
    # 해 (오른쪽 위, 빙글 도는 광선)
    d = ImageDraw.Draw(cnv, "RGBA")
    sx, sy = W - 300, 190
    for i in range(12):
        a = math.tau * i / 12 + t * 0.25
        d.line([(sx + 120 * math.cos(a), sy + 120 * math.sin(a)),
                (sx + (168 + 12 * math.sin(t * 3 + i)) * math.cos(a),
                 sy + (168 + 12 * math.sin(t * 3 + i)) * math.sin(a))],
               fill=(255, 226, 110, 230), width=14)
    d.ellipse([sx - 95, sy - 95, sx + 95, sy + 95], fill=(255, 236, 120, 255),
              outline=(235, 180, 60, 255), width=8)
    for i in range(3):
        c = cloud(i)
        cx = int((-camx * 0.2 + i * 700 + t * 26) % (W + 500)) - 250
        blit(cnv, c, cx, 120 + i * 90, anchor="lt")
    # 언덕 두 겹
    d.ellipse([-500, GROUND_Y - 210, W * 0.72, GROUND_Y + 500],
              fill=(118, 190, 96), outline=(52, 110, 62), width=8)
    d.ellipse([W * 0.35, GROUND_Y - 160, W + 560, GROUND_Y + 560],
              fill=(132, 202, 104), outline=(52, 110, 62), width=8)
    d.rectangle([0, GROUND_Y + 60, W, H], fill=(126, 196, 100))
    # 꽃
    rng = random.Random(7)
    for i in range(26):
        fx = (rng.random() * (W + 240) - 120 - camx * 0.6) % (W + 240) - 120
        fy = GROUND_Y - 40 + rng.random() * 150
        sc = 0.55 + 0.5 * (fy - (GROUND_Y - 40)) / 150
        sway = math.sin(t * 2.2 + i) * 3
        fl = flower(i, sway)
        fl = fl.resize((int(64 * sc), int(96 * sc)), BILINEAR)
        blit(cnv, fl, fx, fy, anchor="cb")


# ---------------------------------------------------------------- 장식
def plate(w, h, radius=26, fill=CREAM, outline=INK, width=5):
    key = ("@plate", w, h, radius, fill, outline, width)
    if key not in _misc:
        im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([3, 3, w - 3, h - 3], radius=radius, fill=fill + (246,),
                            outline=outline + (255,), width=width)
        _misc[key] = im
    return _misc[key]


def caption(cnv, s, y, size=52, appear=1.0, fname="oswald", pad=30):
    """광고 카피 - 크림색 둥근 판 + 잉크색 글자."""
    if appear <= 0:
        return
    size = fit_size(s, fname, size, W - 420)
    tw = text_w(s, fname, size)
    pl = plate(int(tw + pad * 2.6), int(size * 1.9), radius=int(size * 0.6))
    sc = pop_scale(appear * 0.24, 0.24, 0.35) if appear < 1 else 1.0
    if sc != 1.0:
        pl = pl.resize((max(2, int(pl.width * sc)), max(2, int(pl.height * sc))), BILINEAR)
    blit(cnv, pl, W / 2, y, anchor="cc")
    otext(cnv, (W / 2, y - size * 0.06), s, fname, int(size * sc), fill=INK, anchor="mm")


def fine_print(cnv, s, y=H - 44, size=24, light=False):
    col = (238, 234, 224) if light else (74, 68, 60)
    bg = (20, 18, 16, 150) if light else (250, 244, 228, 190)
    d = ImageDraw.Draw(cnv, "RGBA")
    tw = text_w(s, "serif", size)
    d.rounded_rectangle([W / 2 - tw / 2 - 16, y - size * 0.85,
                         W / 2 + tw / 2 + 16, y + size * 0.85], radius=10, fill=bg)
    otext(cnv, (W / 2, y), s, "serif", size, fill=col, anchor="mm")


def petals_burst(cnv, x, y, p, seed, n=10):
    """좀비가 '꽃잎으로 승화'하는 연출(제약 광고니까 폭력은 파스텔로)."""
    if not 0.0 <= p < 1.0:
        return
    rng = random.Random(seed)
    d = ImageDraw.Draw(cnv, "RGBA")
    for i in range(n):
        a = rng.random() * math.tau
        v = 150 + rng.random() * 210
        px = x + math.cos(a) * v * p
        py = y + math.sin(a) * v * p * 0.7 - 190 * p + 260 * p * p
        r = (10 + rng.random() * 9) * (1 - p * 0.5)
        col = rng.choice([(255, 140, 160), (255, 210, 90), (180, 160, 255),
                          (255, 255, 255)])
        rot = a + p * 6
        d.ellipse([px - r, py - r * 0.6, px + r, py + r * 0.6],
                  fill=col + (int(255 * (1 - ease_in(p)) ),),
                  outline=(60, 50, 40, int(220 * (1 - p))), width=2)
        _ = rot


def heart(cnv, x, y, s, alpha=255, col=(255, 110, 130)):
    d = ImageDraw.Draw(cnv, "RGBA")
    r = s * 0.32
    d.ellipse([x - s * 0.5, y - s * 0.35, x - s * 0.5 + 2 * r, y - s * 0.35 + 2 * r],
              fill=col + (alpha,), outline=(60, 50, 40, alpha), width=3)
    d.ellipse([x + s * 0.5 - 2 * r, y - s * 0.35, x + s * 0.5, y - s * 0.35 + 2 * r],
              fill=col + (alpha,), outline=(60, 50, 40, alpha), width=3)
    d.polygon([(x - s * 0.48, y - s * 0.02), (x + s * 0.48, y - s * 0.02),
               (x, y + s * 0.55)], fill=col + (alpha,))
    d.line([(x - s * 0.48, y - s * 0.02), (x, y + s * 0.55)], fill=(60, 50, 40, alpha), width=3)
    d.line([(x + s * 0.48, y - s * 0.02), (x, y + s * 0.55)], fill=(60, 50, 40, alpha), width=3)


# ---------------------------------------------------------------- 1. 흑백의 우울
def sc_gray(cnv, t, dur):
    p = clamp(t / dur)
    cnv.paste(gradient_v(W, H, (96, 104, 120), (150, 152, 158)), (0, 0))
    sk = ruined_skyline(W + 400, 300, col=(84, 84, 96), win=(50, 50, 60))
    blit(cnv, sk, -100, GROUND_Y - 268, anchor="lt", alpha=0.9)
    d = ImageDraw.Draw(cnv)
    d.rectangle([0, GROUND_Y, W, H], fill=(108, 108, 112))
    d.line([(0, GROUND_Y), (W, GROUND_Y)], fill=(60, 60, 66), width=8)

    # 좀비들이 양옆에서 느릿느릿 좁혀온다
    for i in range(8):
        rng = random.Random(300 + i)
        side = 1 if i % 2 == 0 else -1
        x0 = W / 2 + side * (620 + rng.uniform(0, 480))
        x = x0 - side * (60 * t + 150 * p)
        hh = 240 + rng.uniform(-30, 50)
        kind = ("Zombie", "Zombie", "Spitter", "Zombie", "Leader")[i % 5]
        blit(cnv, zombie_img(t * 0.6, i, kind, h=hh, face=-side),
             x, GROUND_Y + 26 - (i % 3) * 44, anchor="cb")

    # 축 처진 로봇 (한숨 김이 모락모락)
    r = draw_robot(cnv, W / 2, GROUND_Y + 6, 330, t * 0.5, bounce=False, sad=1.0)
    for k in range(2):
        ph = (t * 0.6 + k * 0.5) % 1.0
        d2 = ImageDraw.Draw(cnv, "RGBA")
        d2.ellipse([r["top"][0] + 40 - 16 * ph, r["top"][1] - 30 - 70 * ph,
                    r["top"][0] + 74 + 10 * ph, r["top"][1] + 4 - 70 * ph],
                   outline=(230, 230, 235, int(190 * (1 - ph))), width=6)

    # 비
    rng = random.Random(int(t * 24))
    d2 = ImageDraw.Draw(cnv, "RGBA")
    for _ in range(150):
        rx, ry = rng.randrange(-40, W), rng.randrange(0, H)
        d2.line([(rx, ry), (rx + 7, ry + 34)], fill=(210, 216, 228, 120), width=3)

    # 자막(제약 광고의 나긋한 문제 제기)
    if t > 0.5:
        otext(cnv, (W / 2, 150), T["gray1"], "serifb", 66, fill=(245, 245, 248),
              anchor="mm", stroke=6, stroke_fill=(30, 32, 40), max_w=W - 300)
    if t > 2.1:
        otext(cnv, (W / 2, H - 150), T["gray2"], "serif", 44, fill=(235, 235, 240),
              anchor="mm", stroke=5, stroke_fill=(30, 32, 40), max_w=W - 300)
    if t > 2.9:
        fine_print(cnv, T["gray_fine"], y=H - 66, light=True)

    out = desaturate(cnv, 0.88)
    out = Image.blend(out, Image.new("RGB", (W, H), (66, 80, 110)), 0.12)
    out = zoom_at(out, 1.0 + 0.05 * p)
    cnv.paste(out, (0, 0))


# ---------------------------------------------------------------- 2. 처방전
def sc_turn(cnv, t, dur):
    cnv.paste(sunburst(W, H, twos(t) * 30, c1=(255, 222, 120), c2=(255, 186, 70)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle([0, GROUND_Y + 40, W, H], fill=(126, 196, 100))
    d.line([(0, GROUND_Y + 40), (W, GROUND_Y + 40)], fill=(52, 110, 62), width=8)

    q = clamp((t - 0.18) / 0.4)
    r = draw_robot(cnv, W / 2, GROUND_Y + 36, int(300 + 60 * ease_out(q)), t,
                   bounce=True)
    # 반짝이
    rng = random.Random(int(t * 12))
    for _ in range(6):
        sx, sy = rng.randrange(W), rng.randrange(0, GROUND_Y)
        s = rng.randint(8, 22)
        d.line([(sx - s, sy), (sx + s, sy)], fill=(255, 255, 255, 200), width=4)
        d.line([(sx, sy - s), (sx, sy + s)], fill=(255, 255, 255, 200), width=4)

    if t > 0.30:
        sz = otext(cnv, (W / 2, T.get("ask_y", 130)), T["ask"], "oswald", 64,
                   fill=INK, anchor="mm", stroke=8, stroke_fill=(255, 252, 240),
                   max_w=W - 400)
    if t > 0.55:
        s = pop_scale(t - 0.55, 0.3, 0.8)
        logo = SPR("UI/title_logo.png", w=int(1000 * s))
        blit(cnv, logo, W / 2, 300, anchor="cc")
        otext(cnv, (W / 2 + 520 * s, 240), "®", "roboto", 44, fill=INK, anchor="mm",
              stroke=6, stroke_fill=(255, 252, 240))
    if t > 1.5:
        caption(cnv, T["ask_sub"], 452, size=40, appear=clamp((t - 1.5) / 0.24))
    if t > 1.95:                      # 별 모양 배지
        s = pop_scale(t - 1.95, 0.26, 0.9)
        bs = int(300 * s)
        star = Image.new("RGBA", (bs * 2, bs * 2), (0, 0, 0, 0))
        sd = ImageDraw.Draw(star)
        c = bs
        pts = []
        for i in range(28):
            a = math.pi * i / 14 - math.pi / 2
            rr = bs * (0.98 if i % 2 == 0 else 0.72)
            pts.append((c + rr * math.cos(a), c + rr * math.sin(a)))
        sd.polygon(pts, fill=(255, 240, 90, 255), outline=(60, 50, 40, 255))
        sd.line(pts + [pts[0]], fill=(60, 50, 40, 255), width=6)
        star = star.rotate(-10, resample=BILINEAR, expand=False)
        blit(cnv, star, W - 330, 690, anchor="cc")
        for li, ln in enumerate(T["ask_badge"].split("\n")):
            otext(cnv, (W - 330, 632 + li * 62), ln, "anton", int(56 * s), fill=INK,
                  anchor="mm")
    screen_flash(cnv, (1 - t / 0.22) * 0.9 if t < 0.22 else 0.0)


# ---------------------------------------------------------------- 3. 몽타주
KILLS = (1.35, 2.55, 3.75, 4.95)          # 좀비가 꽃잎이 되는 시각


def sc_montage(cnv, t, dur):
    meadow(cnv, t, camx=t * 60)
    rob = draw_robot(cnv, 560, GROUND_Y + 30, 400, t, bounce=True,
                     rot=math.sin(twos(t) * 5.2) * 4)
    mzx, mzy = rob["x"] + rob["w"] * 0.455, rob["y"] - rob["h"] * 0.30

    d = ImageDraw.Draw(cnv, "RGBA")
    for i, tk in enumerate(KILLS):
        zx_kill = 1280 + (i % 2) * 240
        zy = GROUND_Y + 24 - (i % 2) * 90
        # 등장(오른쪽에서 걸어 들어온다) → 명중 → 꽃잎
        if tk - 1.15 <= t < tk:
            zx = zx_kill + 340 * (tk - t) / 1.15
            blit(cnv, zombie_img(t, i, "Zombie", h=250 - (i % 2) * 40, face=-1),
                 zx, zy, anchor="cb")
        # 발사(명중 직전 0.5초 동안 연사)
        if tk - 0.5 <= t < tk + 0.04:
            muzzle_at(cnv, mzx, mzy, t, size=150, seed=i)
            if int(t * 30) % 2 == 0:
                d.line([(mzx, mzy), (zx_kill, zy - 90)], fill=(255, 238, 120, 220),
                       width=10)
        if tk <= t:
            explosion_at(cnv, zx_kill, zy - 100, (t - tk) / 0.45, size=300)
            petals_burst(cnv, zx_kill, zy - 110, (t - tk) / 0.9, seed=40 + i)
            gp = (t - tk) / 0.8
            if gp < 1:
                blit(cnv, SPR("Gold.png", h=54), zx_kill, zy - 170 - 110 * ease_out(gp),
                     anchor="cc", alpha=1 - ease_in(gp))
                otext(cnv, (zx_kill + 56, zy - 170 - 110 * ease_out(gp)), "+3",
                      "bangers", 44, fill=(255, 214, 64), anchor="lm", stroke=5)

    for k in range(2):                      # 나비
        bx = (W * 0.25 + 300 * math.sin(t * 0.9 + k * 2.4)) + k * 700
        by = 420 + 90 * math.sin(t * 1.7 + k)
        blit(cnv, butterfly(t, k), bx, by, anchor="cc")

    if t < 2.0:
        caption(cnv, T["mon1"], 150, appear=clamp((t - 0.25) / 0.24))
    elif t < 4.0:
        caption(cnv, T["mon2"], 150, appear=clamp((t - 2.2) / 0.24))
    else:
        caption(cnv, T["mon3"], 150, appear=clamp((t - 4.2) / 0.24))
    if t > 3.0:
        pl = plate(560, 108, radius=30)
        blit(cnv, pl, 330, H - 190, anchor="cc")
        otext(cnv, (330, H - 212), T["mon_docs"], "oswald", 38, fill=INK, anchor="mm",
              max_w=500)
        stars = "★" * 5
        otext(cnv, (330, H - 168), stars, "serifb", 30, fill=(230, 160, 30), anchor="mm")
    if t > 3.6:
        fine_print(cnv, T["mon_fine"])


# ---------------------------------------------------------------- 4. 소풍
def sc_picnic(cnv, t, dur):
    meadow(cnv, t, camx=30)
    d = ImageDraw.Draw(cnv, "RGBA")
    # 체크무늬 돗자리
    bx0, by0, bx1, by1 = 760, GROUND_Y - 46, 1360, GROUND_Y + 96
    d.polygon([(bx0 + 70, by0), (bx1 - 70, by0), (bx1, by1), (bx0, by1)],
              fill=(236, 84, 84, 255), outline=(60, 50, 40, 255), width=6)
    for k in range(1, 6):
        x = bx0 + 70 + (bx1 - bx0 - 140) * k / 6
        d.line([(x, by0), (x - 46, by1)], fill=(255, 240, 240, 200), width=12)
    for k in range(1, 3):
        y = by0 + (by1 - by0) * k / 3
        d.line([(bx0 + 40, y), (bx1 - 40, y)], fill=(255, 240, 240, 200), width=12)
    # 전리품: 골드 무더기 + 아이템 상자 + 경험치
    for i, (gx, gy, gh) in enumerate(((980, GROUND_Y + 10, 70), (1040, GROUND_Y + 30, 78),
                                      (920, GROUND_Y + 34, 64), (1000, GROUND_Y + 52, 84))):
        blit(cnv, SPR("Gold.png", h=gh), gx, gy, anchor="cb")
    blit(cnv, SPR("ItemBox.png", h=150), 1220, GROUND_Y + 60, anchor="cb")
    blit(cnv, SPR("Exp.png", h=56), 1130, GROUND_Y - 6, anchor="cb")

    rob = draw_robot(cnv, 620, GROUND_Y + 30, 380, t * 0.7, bounce=True)
    # 로봇 주변 하트
    for k in range(3):
        ph = (t * 0.55 + k * 0.33) % 1.0
        heart(cnv, rob["x"] - 150 + k * 150, rob["top"][1] - 40 - 120 * ph,
              44 * (1 - ph * 0.4), alpha=int(235 * (1 - ph)))

    mzx, mzy = rob["x"] + rob["w"] * 0.455, rob["y"] - rob["h"] * 0.30
    for i, tk in enumerate((2.45, 3.75)):
        zx_kill, zy = 1560, GROUND_Y + 40
        if tk - 1.3 <= t < tk:
            zx = zx_kill + 300 * (tk - t) / 1.3
            blit(cnv, zombie_img(t, 5 + i, "Sprinter" if i else "Zombie",
                                 h=250, face=-1), zx, zy, anchor="cb")
        if tk - 0.28 <= t < tk + 0.04:
            muzzle_at(cnv, mzx, mzy, t, size=140, seed=3 + i)
            if int(t * 30) % 2 == 0:
                d.line([(mzx, mzy), (zx_kill, zy - 100)], fill=(255, 238, 120, 220), width=10)
        if tk <= t:
            explosion_at(cnv, zx_kill, zy - 110, (t - tk) / 0.45, size=280)
            petals_burst(cnv, zx_kill, zy - 110, (t - tk) / 0.9, seed=60 + i)

    caption(cnv, T["pic1"], 150, appear=clamp((t - 0.3) / 0.24))
    if t > 1.2:
        fine_print(cnv, T["pic_fine"])


# ---------------------------------------------------------------- 5. 부작용
def sc_sideeffects(cnv, t, dur):
    meadow(cnv, t * 0.5, camx=10)
    # 느린 왈츠를 추는 로봇(슬로모션 흉내)
    rot = math.sin(t * 1.4) * 10
    rob = draw_robot(cnv, W / 2 + math.sin(t * 0.7) * 120, GROUND_Y + 26, 400,
                     t * 0.4, bounce=True, rot=rot)
    # 흩날리는 꽃잎
    rng = random.Random(77)
    d = ImageDraw.Draw(cnv, "RGBA")
    for i in range(16):
        ph = (t * 0.14 + rng.random()) % 1.0
        px = (rng.random() * (W + 200) - 100 + 130 * math.sin(t * 0.8 + i)) % (W + 200) - 100
        py = -60 + (H + 120) * ph
        r = 9 + rng.random() * 8
        col = rng.choice([(255, 140, 160), (255, 210, 90), (255, 255, 255)])
        d.ellipse([px - r, py - r * 0.6, px + r, py + r * 0.6], fill=col + (210,),
                  outline=(60, 50, 40, 190), width=2)

    caption(cnv, T["se_head"], 130, size=48, appear=clamp((t - 0.15) / 0.24))

    # 속사포 부작용 목록 - 두 줄 밴드에 0.42초 간격으로 쏟아진다
    band_y0 = H - 330
    n_show = int(clamp((t - 0.65) / 0.42, 0, len(T["se_list"])))
    if t > 0.55 and t < 4.9:
        d.rounded_rectangle([120, band_y0, W - 120, band_y0 + 220], radius=26,
                            fill=CREAM + (232,), outline=INK + (255,), width=5)
        otext(cnv, (150, band_y0 + 44), T["se_incl"], "serifb", 30,
              fill=(120, 60, 50), anchor="lm")
        for i in range(n_show):
            if n_show - i > 6:             # 밴드에는 6칸뿐 - 오래된 줄은 밀려난다
                continue
            appear = clamp((t - 0.65 - i * 0.42) / 0.16)
            if appear <= 0:
                continue
            slot = i % 6
            x = 170 + (slot % 2) * 860
            y = band_y0 + 92 + (slot // 2) * 42
            otext(cnv, (x, y), "• " + T["se_list"][i], "oswald",
                  int(34 * (1 + 0.6 * (1 - appear))), fill=INK, anchor="lm")
    # 맨 아래 초고속 스크롤 깨알 고지
    if t > 0.55:
        tick = T["ticker"]
        tw = text_w(tick, "serif", 26)
        off = int((t * 620) % tw)
        d.rectangle([0, H - 58, W, H], fill=(24, 22, 20, 235))
        otext(cnv, (-off, H - 29), tick + tick, "serif", 26, fill=(230, 226, 214),
              anchor="lm")

    if t > 4.9:
        q = pop_scale(t - 4.9, 0.22, 0.5)
        pl = plate(int(1150 * q), int(210 * q), radius=34)
        blit(cnv, pl, W / 2, H - 240, anchor="cc")
        otext(cnv, (W / 2, H - 285), T["se_not1"], "serifb", 46, fill=INK, anchor="mm",
              max_w=1000)
        if t > 5.35:
            otext(cnv, (W / 2, H - 205), T["se_not2"], "serifb", 46,
                  fill=(150, 50, 44), anchor="mm", max_w=1000)


# ---------------------------------------------------------------- 6. CTA
def sc_cta(cnv, t, dur):
    cnv.paste(gradient_v(W, H, (252, 247, 234), (244, 234, 210)), (0, 0))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle([26, 26, W - 26, H - 26], outline=INK + (255,), width=6)
    d.rectangle([44, 44, W - 44, H - 44], outline=(180, 168, 148, 255), width=2)

    s = pop_scale(t, 0.26, 0.7)
    blit(cnv, SPR("UI/title_logo.png", w=int(1000 * s)), W / 2, 225, anchor="cc")
    rob = draw_robot(cnv, 250, 950, 300, t, bounce=True,
                     rot=math.sin(twos(t) * 5.2) * 5)

    # 도장 쾅 (FREE 문구보다 먼저 그려서 문구가 도장 위에 얹힌다)
    if t > 1.15:
        q = clamp((t - 1.15) / 0.2)
        sc = 2.1 - 1.1 * ease_out(q)
        stamp = Image.new("RGBA", (1500, 430), (0, 0, 0, 0))
        sd = ImageDraw.Draw(stamp)
        sd.rounded_rectangle([16, 16, 1484, 414], radius=40, outline=STAMP_RED + (255,),
                             width=16)
        sd.rounded_rectangle([44, 44, 1456, 386], radius=28, outline=STAMP_RED + (255,),
                             width=6)
        sd.text((750, 130), T["cta_stamp1"], font=F("anton", 116), fill=STAMP_RED + (255,),
                anchor="mm")
        sd.text((750, 288), T["cta_stamp2"], font=F("anton", 116), fill=STAMP_RED + (255,),
                anchor="mm")
        stamp = stamp.rotate(-7, resample=BILINEAR, expand=True)
        stamp = stamp.resize((int(stamp.width * sc * 0.94), int(stamp.height * sc * 0.94)),
                             BILINEAR)
        blit(cnv, stamp, W / 2 + 60, 685, anchor="cc", alpha=min(1.0, 0.25 + q))
    if t > 0.5:
        otext(cnv, (W / 2, 408), T["cta_free"], "oswald", 54, fill=INK, anchor="mm",
              stroke=8, stroke_fill=(250, 244, 228), max_w=W - 480)
    # URL 판
    if t > 1.9:
        q = pop_scale(t - 1.9, 0.22, 0.5)
        pl = plate(int(1010 * q), int(120 * q), radius=28, fill=(34, 32, 30),
                   outline=(255, 214, 64), width=6)
        blit(cnv, pl, W / 2, 922, anchor="cc")
        otext(cnv, (W / 2, 922), T["cta_url"], "roboto", 58, fill=(255, 238, 130),
              anchor="mm", max_w=940)
        otext(cnv, (W / 2, 840), T["cta_play"], "oswald", 40, fill=INK,
              anchor="mm", stroke=7, stroke_fill=(250, 244, 228))
    # 좀비 손이 URL을 노린다
    if t > 2.3:
        zp = clamp((t - 2.3) / 0.7)
        knock = clamp((t - 3.15) / 0.3)
        rise = 240 * ease_out(zp) - 190 * ease_out(knock)
        zx = W - 300 + 130 * ease_out(knock)
        zr = 34 * ease_out(knock)
        blit(cnv, SPR("ZombieMove/walk_left_f2.png", h=330, rot=18 + zr), zx,
             H + 210 - rise, anchor="cb")
        if 3.15 <= t < 3.5:
            star_p = (t - 3.15) / 0.35
            for i in range(5):
                a = math.tau * i / 5 + star_p * 2
                d.line([(W - 390 + 90 * math.cos(a), 900 + 60 * math.sin(a)),
                        (W - 390 + (130 + 60 * star_p) * math.cos(a),
                         900 + (90 + 40 * star_p) * math.sin(a))],
                       fill=(255, 90, 60, int(255 * (1 - star_p))), width=10)
    if t > 2.7:
        fine_print(cnv, T["cta_fine"], y=1014, size=22)
    if t > dur - 0.4:                      # 페이드아웃
        screen_flash(cnv, ease_in((t - (dur - 0.4)) / 0.4), color=(10, 10, 12))


SCENES = {"gray": sc_gray, "turn": sc_turn, "montage": sc_montage,
          "picnic": sc_picnic, "sideeffects": sc_sideeffects, "cta": sc_cta}


# ---------------------------------------------------------------- 렌더/후처리
def shake_at(t):
    name, tl, _d = scene_at(t)
    if name == "turn" and tl < 0.4:
        return 7.0
    if name == "montage":
        for tk in KILLS:
            if 0 <= t - (7.2 + tk) < 0.22:
                return 6.0
    if name == "cta" and 1.15 <= tl < 1.5:
        return 9.0
    return 0.0


def render_frame(f):
    t = f / FPS
    name, tl, d = scene_at(t)
    cnv = Image.new("RGB", (W, H), (12, 12, 12))
    SCENES[name](cnv, tl, d)
    # 장면마다 아주 느리게 줌 인(광고 특유의 드리프트)
    if name not in ("gray",):
        cnv = zoom_at(cnv, 1.0 + 0.035 * clamp(tl / d))
    sh = shake_at(t)
    if sh > 0:
        rng = random.Random(f * 31)
        cnv = cnv.transform((W, H), Image.Transform.AFFINE,
                            (1, 0, (rng.random() * 2 - 1) * sh,
                             0, 1, (rng.random() * 2 - 1) * sh * 0.7),
                            resample=BILINEAR, fillcolor=(12, 12, 12))
    cnv = fast_post(cnv, strength=0.30, power=2.6, grain=3, f=f)
    return cnv


# ---------------------------------------------------------------- 오디오
BPM = 100.0
BEAT = 60.0 / BPM


def build_audio(path):
    mx = Mixer(DUR)

    # ---- 1) 우울 파트: 빗소리 + 단조 아르페지오 + 늘어진 좀비 신음
    mx.put(rain_noise(4.6, gain=0.16, seed=5), 0.0, 1.0)
    sad = [("A2", 0), ("C3", 1), ("E3", 2), ("C3", 3),
           ("F2", 4), ("A2", 5), ("C3", 6), ("E3", 7)]
    for (nm, i) in sad:
        mx.put(note(nf(nm), 0.9, 0.20, "ep"), 0.25 + i * 0.52)
    mx.put(game_snd("Enemy_Hit_A.wav", rate=0.5), 1.3, 0.5, pan=-0.4)
    mx.put(game_snd("Enemy_Hit_B.wav", rate=0.45), 2.9, 0.5, pan=0.45)
    mx.put(game_snd("Player_Hit.wav", rate=0.8), 3.85, 0.5)

    # ---- 2) 전환: 반짝 + 상승 글리산도
    mx.put(game_snd("LevelUp.wav"), 4.18, 0.9)
    for i, nm in enumerate(("C5", "E5", "G5", "C6")):
        mx.put(note(nf(nm), 0.5, 0.16, "glock"), 4.22 + i * 0.07)
    mx.put(boom(0.5, 120, 44, 0.5), 4.75)              # 로고 슬램
    mx.put(game_snd("UI_Click.wav"), 6.18, 0.6)        # 배지

    # ---- 3) 명랑한 광고 음악 (4.2 ~ 27.2): C - G - Am - F
    chords = [("C3", "E3", "G3", "C4", "E4"), ("G2", "B2", "D3", "G3", "B3"),
              ("A2", "C3", "E3", "A3", "C4"), ("F2", "A2", "C3", "F3", "A3")]
    melody = ["E5", "D5", "C5", "F5", "G5", "E5", "A5", "G5"]
    t0 = 4.2
    bar = 4 * BEAT
    nbars = int((27.2 - t0) / bar) + 1
    for b in range(nbars):
        ch = chords[b % 4]
        tb = t0 + b * bar
        if tb > 26.8:
            break
        # 베이스(1·3박) + 아르페지오(8분음표) + 멜로디(마디당 2음)
        mx.put(note(nf(ch[0]), 1.0, 0.24, "tri"), tb)
        mx.put(note(nf(ch[0]), 1.0, 0.22, "tri"), tb + 2 * BEAT)
        arp = (ch[1], ch[2], ch[3], ch[4], ch[3], ch[2], ch[3], ch[2])
        for i, nm in enumerate(arp):
            mx.put(note(nf(nm), 0.42, 0.105, "ep"), tb + i * BEAT / 2)
        mx.put(note(nf(melody[(b * 2) % 8]), 1.1, 0.12, "glock"), tb, pan=0.2)
        mx.put(note(nf(melody[(b * 2 + 1) % 8]), 1.1, 0.10, "glock"), tb + 2 * BEAT,
               pan=-0.2)

    # ---- 몽타주 효과음 (7.2 + KILLS)
    for i, tk in enumerate(KILLS):
        tt = 7.2 + tk
        for k in range(4):
            mx.put(game_snd("Weapon_RapidFire.wav"), tt - 0.46 + k * 0.115, 0.30,
                   pan=0.25)
        mx.put(game_snd("Weapon_Explosive.wav"), tt, 0.5, pan=0.3)
        mx.put(game_snd("Enemy_Death.wav"), tt + 0.03, 0.45, pan=0.3)
        mx.put(note(nf("A5"), 0.3, 0.14, "glock"), tt + 0.25, pan=0.3)  # 동전 딩

    # ---- 소풍 (13.2)
    for i, tk in enumerate((2.45, 3.75)):
        tt = 13.2 + tk
        for k in range(3):
            mx.put(game_snd("Weapon_RapidFire.wav"), tt - 0.26 + k * 0.1, 0.28, pan=0.3)
        mx.put(game_snd("Weapon_Explosive.wav"), tt, 0.45, pan=0.35)
        mx.put(game_snd("Enemy_Death.wav"), tt + 0.03, 0.4, pan=0.35)
    mx.put(game_snd("Enemy_Hit_C.ogg", rate=0.6), 14.6, 0.4, pan=0.5)

    # ---- 부작용 속사포 (18.85 + 0.42*i) - 종이 넘어가는 틱틱
    for i in range(len(T["se_list"])):
        mx.put(game_snd("UI_Click.wav"), 18.85 + i * 0.42, 0.33,
               pan=-0.2 if i % 2 == 0 else 0.2)
    mx.put(game_snd("Player_Hit.wav", rate=0.9), 23.1, 0.5)   # "좀비는 복용 금지"
    mx.put(game_snd("Enemy_Hit_A.wav", rate=0.55), 23.55, 0.5)

    # ---- CTA (24.2)
    mx.put(game_snd("LevelUp.wav"), 24.25, 0.8)
    mx.put(boom(0.7, 150, 40, 0.95), 25.35)                   # 도장 쾅
    mx.put(game_snd("UI_Click.wav"), 26.12, 0.7)
    mx.put(game_snd("Enemy_Hit_B.wav", rate=0.5), 26.6, 0.55, pan=0.5)  # 좀비 손
    mx.put(game_snd("Enemy_Hit_A.wav", rate=1.4), 27.38, 0.7, pan=0.5)  # 퍽!
    for i, nm in enumerate(("C4", "E4", "G4", "C5", "E5")):   # 마무리 화음
        mx.put(note(nf(nm), 1.6, 0.14, "ep"), 26.4 + i * 0.03)
    return mx.write(path, master=0.92)


# ---------------------------------------------------------------- 메인
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="en", choices=("en", "ko"))
    ap.add_argument("--test", default=None, help="미리보기 시각(초, 쉼표 구분)")
    ap.add_argument("--out", default=None)
    ap.add_argument("--fast", action="store_true", help="빠른 인코딩 프리셋")
    args = ap.parse_args()
    set_video_lang(args.lang)

    here = os.path.dirname(os.path.abspath(__file__))
    if args.test:
        outdir = os.path.join(CACHE, "preview_tvad")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            im = render_frame(int(round(t * FPS)))
            p = os.path.join(outdir, "tv_%s_%05.2f.png" % (args.lang, t))
            im.save(p)
            print(p)
        return

    os.makedirs(CACHE, exist_ok=True)
    audio = build_audio(os.path.join(CACHE, "tvad_audio.wav"))
    out = args.out or os.path.join(here, "Comstock_Ad_TV_%s.mp4" % args.lang.upper())
    encode_frames(render_frame, NFRAMES, FPS, (W, H), out, audio=audio,
                  crf=22, label="tvad-" + args.lang,
                  preset="veryfast" if args.fast else "medium")
    print("완성:", out)
    web = os.path.splitext(out)[0] + "_web.mp4"
    make_web_version(out, web, height=720, crf=25)
    print("웹용:", web)


if __name__ == "__main__":
    main()
