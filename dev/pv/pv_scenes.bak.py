# -*- coding: utf-8 -*-
"""컴스톡 PV - 15개 장면 그리기.

각 함수는 (cnv, t, dur, lang) 을 받아 색이 있는 RGB 캔버스에 장면을 그린다.
흑백/지직거림 처리는 render_pv.py의 tv_process가 마지막에 한 번만 한다.
"""
import math
import random

from PIL import Image, ImageDraw, ImageFilter

from pv_common import W, H, LANG
from pv_draw import (A, SEQ, SPR, FNT, blit, ease_out, ease_in, clamp, twos,
                     draw_stage, impact_star, speed_lines, draw_note, text,
                     text_layer, wobble, caption_plate, LANCZOS, BILINEAR)

HORIZON = 470
GROUND_Y = HORIZON + 78          # 발이 닿는 선
ROBOT = "Comstock.png"

_wcache = {}


# ---------------------------------------------------------------- 소품
def WPN(name, direction, h):
    """총구가 direction(-1 왼쪽 / +1 오른쪽)을 향하도록 무기 스프라이트를 만든다.

    프로젝트 규칙: `Right*`는 총구가 왼쪽, `Left*`는 총구가 오른쪽을 본다.
    """
    key = (name, direction, h)
    im = _wcache.get(key)
    if im is None:
        rel = ("Right" if direction < 0 else "Left") + name + ".png"
        src = A(rel)
        src = src.crop(src.getbbox())
        nw = max(1, int(round(src.width * h / src.height)))
        im = src.resize((nw, int(h)), LANCZOS)
        if len(_wcache) > 300:
            _wcache.clear()
        _wcache[key] = im
    return im


def robot_pts(h):
    """로봇 스프라이트를 높이 h로 그렸을 때의 주요 좌표(스프라이트 좌상단 기준)."""
    k = h / 720.0
    w = 1242 * k
    return {
        "w": w, "h": h,
        "face": (605 * k, 275 * k),
        "top": (605 * k, 44 * k),
        "head_w": 340 * k,
        "body_c": (605 * k, 300 * k),
    }


def draw_robot(cnv, cx, gy, h, t, bounce=True, squash=1.0, extras=None, alpha=1.0):
    """로봇을 그리고 주요 좌표를 돌려준다. gy는 발이 닿는 y."""
    ta = twos(t)
    dy = 0.0
    if bounce:
        dy = -abs(math.sin(ta * math.pi * 2.2)) * (h * 0.055)
    sq = squash
    hh = int(h * (2 - sq) if sq != 1.0 else h)
    spr = SPR(ROBOT, h=hh)
    if sq != 1.0:
        spr = spr.resize((max(1, int(spr.width * sq)), spr.height), BILINEAR)
    px = cx - spr.width / 2
    py = gy + dy - spr.height
    blit(cnv, spr, cx, gy + dy, anchor="cb", alpha=alpha)
    k = spr.height / 720.0
    return {
        "x": cx, "y": gy + dy, "w": spr.width, "h": spr.height,
        "face": (px + 605 * k * (spr.width / (1242 * k)), py + 275 * k),
        "top": (px + spr.width / 2, py + 44 * k),
        "head_w": 340 * k,
    }


def zombie_frame(t, idx=0, kind="Zombie"):
    if kind == "Zombie":
        fr = SEQ("ZombieMove")
    elif kind == "Sprinter":
        fr = SEQ("SprinterMove")
    elif kind == "Spitter":
        fr = SEQ("SpitterMove")
    elif kind == "Disruptor":
        fr = SEQ("DisruptorMove")
    elif kind == "Leader":
        fr = SEQ("LeaderMove")
    else:
        fr = SEQ("ChargerMove")
    i = int(twos(t, 10) * 10 + idx) % len(fr)
    return fr[i]


# ---------------------------------------------------------------- 자막 카드
_card_bg = None


def card_bg():
    global _card_bg
    if _card_bg is None:
        im = Image.new("RGB", (W, H), (12, 12, 12))
        glow = Image.new("L", (W // 4, H // 4), 0)
        d = ImageDraw.Draw(glow)
        d.ellipse([W // 16, H // 16, W // 4 - W // 16, H // 4 - H // 16], fill=90)
        glow = glow.filter(ImageFilter.GaussianBlur(18)).resize((W, H), BILINEAR)
        im = Image.composite(Image.new("RGB", (W, H), (46, 46, 46)), im, glow)
        d = ImageDraw.Draw(im)
        for i, (inset, wdt) in enumerate(((44, 5), (60, 2))):
            d.rectangle([inset, inset, W - inset, H - inset], outline=(232, 232, 232), width=wdt)
        for (x, y) in ((52, 52), (W - 52, 52), (52, H - 52), (W - 52, H - 52)):
            d.polygon([(x, y - 13), (x + 13, y), (x, y + 13), (x - 13, y)], fill=(232, 232, 232))
        _card_bg = im
    return _card_bg


def _card(cnv, lang, t, dur, lines, kind="serif", sizes=(66,), pop=False, sub=None,
          sub_size=26):
    cnv.paste(card_bg(), (0, 0))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # 긴 번역문이 카드 테두리를 넘지 않도록 줄 단위로 크기를 줄인다
    fitted = []
    for i, s in enumerate(lines):
        sz = sizes[min(i, len(sizes) - 1)]
        f = FNT(lang, kind, sz)
        bb = d.textbbox((0, 0), s, font=f)
        tw = bb[2] - bb[0]
        if tw > W - 190:
            sz = max(18, int(sz * (W - 190) / tw))
            f = FNT(lang, kind, sz)
        fitted.append((s, f, sz))

    total = sum(sz for (_s, _f, sz) in fitted) * 1.34
    y = H / 2 - total / 2 + fitted[0][2] * 0.67
    if sub:
        y -= 26
    for (s, f, sz) in fitted:
        d.text((W / 2, y), s, font=f, fill=(242, 242, 242, 255), anchor="mm")
        y += sz * 1.34
    if sub:
        f = FNT(lang, "serif", sub_size)
        d.text((W / 2, y + 18), sub, font=f, fill=(196, 196, 196, 255), anchor="mm")
    if pop:
        p = clamp(t / 0.22)
        s = 1.0 + 0.5 * (1 - ease_out(p)) - 0.06 * math.sin(ease_out(p) * math.pi)
        if abs(s - 1.0) > 0.01:
            nw, nh = max(2, int(W * s)), max(2, int(H * s))
            layer = layer.resize((nw, nh), BILINEAR).crop(
                ((nw - W) // 2, (nh - H) // 2, (nw - W) // 2 + W, (nh - H) // 2 + H))
    layer = wobble(layer, t, amp=1.8, ang=0.45)
    cnv.paste(layer, (0, 0), layer)


def card_presents(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["presents1"], L["presents2"]], "serif", (58, 40),
          sub=L["presents3"])
    d = ImageDraw.Draw(cnv)
    y = H / 2 + 128
    d.line([(W / 2 - 150, y), (W / 2 + 150, y)], fill=(210, 210, 210), width=3)
    d.polygon([(W / 2, y - 11), (W / 2 + 11, y), (W / 2, y + 11), (W / 2 - 11, y)],
              fill=(210, 210, 210))


def card_trouble(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["trouble1"], L["trouble2"]], "punch", (62, 76), pop=True)


def card_guns(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["guns1"], L["guns2"]], "punch", (48, 74), pop=True)


def card_noreload(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["noreload1"], L["noreload2"]], "punch", (52, 62), pop=True)


def card_boss(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["boss1"], L["boss2"]], "punch", (64, 58), pop=True)


def card_end(cnv, t, dur, lang):
    L = LANG[lang]
    _card(cnv, lang, t, dur, [L["end1"]], "serif", (104,), sub=L["end2"], sub_size=24)


# ---------------------------------------------------------------- 장면
def walk(cnv, t, dur, lang):
    """평화로운 산책. 좀비 한 마리가 뒤에서 다가온다."""
    L = LANG[lang]
    ta = twos(t)
    camx = ta * 46
    draw_stage(cnv, camx, HORIZON)
    d = ImageDraw.Draw(cnv)

    startled = t > 1.95
    jump = 0.0
    if startled:
        p = clamp((t - 1.95) / 0.45)
        jump = -math.sin(p * math.pi) * 90

    r = draw_robot(cnv, 372, GROUND_Y + jump, 250, t, bounce=not startled,
                   squash=1.0 if not startled else 0.92)

    if not startled:  # 휘파람 음표
        for i in range(3):
            ph = (ta * 0.9 + i * 0.33) % 1.0
            nx = r["face"][0] + 96 + ph * 120
            ny = r["face"][1] - 24 - ph * 96
            if ph > 0.08:
                draw_note(d, nx, ny, 1.0 + ph * 0.4)

    # 좀비가 오른쪽에서 걸어온다
    if t > 0.85:
        p = clamp((t - 0.85) / 1.5)
        zx = W + 90 - p * 330
        blit(cnv, SPR(zombie_frame(t), h=196), zx, GROUND_Y + 6, anchor="cb")

    if startled:
        p = clamp((t - 1.95) / 0.45)
        f = FNT(lang, "punch", int(104 + 26 * math.sin(p * math.pi)))
        lay = text_layer((W, H), (r["top"][0] + 96, r["top"][1] - 34), "!", f,
                         fill=(250, 250, 250), stroke=8)
        lay = wobble(lay, t, amp=4, ang=3.0)
        cnv.paste(lay, (0, 0), lay)
        speed_lines(cnv, r["top"][0] + 96, r["top"][1] - 34, 9, 60, 108,
                    random.Random(int(t * 12)), width=5)

    caption_plate(cnv, lang, L["walk_cap"], y=H - 66, size=27)
    f = FNT(lang, "serif", 15)
    text(cnv, (W - 62, H - 36), L["footnote"], f, fill=(126, 126, 126), anchor="rs")


def horde(cnv, t, dur, lang):
    """좀비가 기하급수적으로 늘어난다. 카메라가 뒤로 빠진다."""
    L = LANG[lang]
    p = clamp(t / dur)
    ta = twos(t)
    zoom = 1.0 - 0.24 * ease_out(p)
    horizon = int(HORIZON - 40 * ease_out(p))
    draw_stage(cnv, ta * 30, horizon)
    gy = GROUND_Y - 30 * ease_out(p)

    n = int(2 + 76 * (p ** 1.35))
    kinds = ["Zombie", "Zombie", "Zombie", "Sprinter", "Spitter", "Zombie", "Disruptor",
             "Zombie", "Leader", "Zombie", "Charger"]
    items = []
    for i in range(n):
        rng = random.Random(4100 + i)
        side = 1 if i % 2 == 0 else -1
        row = rng.random()
        base = rng.uniform(0.55, 1.35)
        kind = kinds[i % len(kinds)]
        sp = rng.uniform(48, 108)
        x0 = (W / 2 + side * (240 + rng.uniform(0, 430) + i * 4))
        x = x0 - side * (ta * sp)
        y = gy + 52 - row * 232 * zoom
        h = (188 + rng.uniform(-30, 40)) * zoom * (0.66 + 0.48 * (1 - row))
        items.append((y, x, h, kind, side, i))
    items.sort(key=lambda v: v[0])
    for (y, x, h, kind, side, i) in items:
        if -120 < x < W + 120:
            blit(cnv, SPR(zombie_frame(t, i, kind), h=int(h), flip=(side < 0)), x, y,
                 anchor="cb")

    r = draw_robot(cnv, W / 2, gy, int(250 * zoom), t, bounce=True)
    # 당황해서 부들부들
    if p > 0.35:
        rng = random.Random(int(t * 12))
        speed_lines(cnv, r["x"], r["y"] - r["h"] * 0.5, 6, 70 * zoom, 120 * zoom, rng, width=3)

    shown = int(1 + (n - 1) * 1.0)
    f = FNT(lang, "hud", 44)
    s = "%s  %04d" % (L["zombies"], shown if p < 0.85 else 9999)
    lay = text_layer((W, H), (78, H - 74), s, f, fill=(248, 248, 248), anchor="ls", stroke=6)
    cnv.paste(lay, (0, 0), lay)


def massacre(cnv, t, dur, lang):
    """총을 잔뜩 달고 전부 쏜다."""
    L = LANG[lang]
    ta = twos(t)
    rng = random.Random(int(t * 24))
    attach_end = 1.55
    draw_stage(cnv, ta * 24, HORIZON)

    guns = [
        (-1, "HMG", -34, 118), (1, "HMG", -34, 118),
        (-1, "RocketLauncher", -132, 100), (1, "RocketLauncher", -132, 100),
        (-1, "PlasmaCannon", 62, 110), (1, "SawedOff", 62, 100),
        (-1, "SMG", 146, 86), (1, "LaserPistol", 146, 86),
    ]
    cx, gy = W / 2, GROUND_Y + 24
    rh = 300

    # 뒤에서 몰려오는 좀비 (총이 다 붙은 뒤부터)
    if t > attach_end - 0.3:
        tt = t - (attach_end - 0.3)
        for i in range(26):
            zr = random.Random(5200 + i)
            side = 1 if i % 2 == 0 else -1
            sp = zr.uniform(70, 150)
            row = zr.random()
            x = W / 2 + side * (520 + zr.uniform(0, 460)) - side * tt * sp
            y = GROUND_Y + 44 - row * 196
            h = 178 + zr.uniform(-26, 34) - row * 54
            dead = (abs(x - W / 2) < 250 + zr.uniform(0, 60))
            if dead:
                # 만화처럼 날아간다
                k = clamp((250 + 60 - abs(x - W / 2)) / 120)
                y -= k * 190 * abs(math.sin(tt * 3 + i))
                rot = k * 220 * (1 if side > 0 else -1)
                if k > 0.55:
                    ex = SEQ("Explosion")
                    fi = int((tt * 16 + i * 3) % len(ex))
                    blit(cnv, SPR(ex[fi], h=int(140 + 60 * k)), x, y, anchor="cc")
                    continue
                spr = SPR(zombie_frame(t, i), h=int(h), flip=(side < 0), rot=rot)
            else:
                spr = SPR(zombie_frame(t, i), h=int(h), flip=(side < 0))
            if -160 < x < W + 160:
                blit(cnv, spr, x, y, anchor="cb")

    r = draw_robot(cnv, cx, gy, rh, t, bounce=True)
    fx, fy = r["face"]

    # 무기 장착 (하나씩 뻥뻥 붙는다)
    muzzles = []
    for i, (side, name, dy, gh) in enumerate(guns):
        at = 0.12 + i * 0.16
        if t < at:
            continue
        p = clamp((t - at) / 0.2)
        s = 1.0 + 0.85 * (1 - ease_out(p))
        g = WPN(name, side, int(gh * s))
        gx = cx + side * (132 + gh * 0.50)
        gy2 = r["y"] - rh * 0.70 + dy
        blit(cnv, g, gx, gy2, anchor="cc")
        if p < 0.55:
            st = impact_star(int(gh * 2.2 * (1 - p)))
            blit(cnv, st, gx, gy2, anchor="cc")
        muzzles.append((gx + side * g.width / 2, gy2 + g.height * 0.06, side))

    # 총구 화염 + 탄환
    if t > attach_end:
        mf = SEQ("MuzzleFlash")
        for (mx, my, side) in muzzles:
            if rng.random() < 0.82:
                fl = SPR(mf[rng.randrange(len(mf))], h=rng.randint(84, 132),
                         flip=(side > 0))
                blit(cnv, fl, mx, my, anchor="cc")
            dd = ImageDraw.Draw(cnv)
            for _ in range(4):
                bx = mx + side * rng.uniform(50, 520)
                by = my + rng.uniform(-10, 10)
                ln = rng.randint(30, 56)
                dd.line([(bx, by), (bx + side * ln, by)], fill=(20, 20, 20), width=9)
                dd.line([(bx, by), (bx + side * ln, by)], fill=(250, 250, 250), width=4)

    # 처치 수 카운터
    if t > attach_end:
        tt = t - attach_end
        k = int(12 + tt * tt * 640)
        if tt > 2.0:
            k = int(4213 + (tt - 2.0) * 178000)
        f = FNT(lang, "hud", 46)
        lay = text_layer((W, H), (78, H - 68), "%s  %s" % (L["kills"], "{:,}".format(k)),
                         f, fill=(250, 250, 250), anchor="ls", stroke=6)
        cnv.paste(lay, (0, 0), lay)

    # NO RELOADING 도장
    if t > 3.05:
        p = clamp((t - 3.05) / 0.18)
        sz = int(74 * (1 + 1.4 * (1 - ease_out(p))))
        f = FNT(lang, "punch", sz)
        lay = text_layer((W, H), (W / 2, 176), L["stamp"], f, fill=(252, 252, 252), stroke=9)
        lay = lay.rotate(-11, resample=BILINEAR, center=(W / 2, 176))
        lay = wobble(lay, t, amp=2.4, ang=0.6)
        cnv.paste(lay, (0, 0), lay)


def shop(cnv, t, dur, lang):
    """웨이브 사이의 상점/정비. 로봇이 점점 이상해진다."""
    L = LANG[lang]
    p = clamp(t / dur)
    draw_stage(cnv, 120, HORIZON, ground_dark=0.82)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 120))
    cnv.paste(dim, (0, 0), dim)

    r = draw_robot(cnv, W / 2 - 6, GROUND_Y - 66, 300, t, bounce=False)
    fx, fy = r["face"]
    hw = r["head_w"]

    # 파츠가 하나씩 날아와 붙는다
    parts = [
        ("Accessories/8Bitsunglass-transparent.png", 0.25, (fx, fy + 2), hw * 1.34, "w"),
        ("Accessories/Crown-transparent.png", 0.55, (r["top"][0], r["top"][1] - 4), hw * 0.94, "wb"),
        ("Accessories/Unicon-transparent.png", 0.85, (r["top"][0] - hw * 0.62, r["top"][1] + 16), hw * 0.60, "wb"),
        ("Accessories/Joystick-transparent.png", 1.15, (r["top"][0] + hw * 0.64, r["top"][1] + 14), hw * 0.54, "wb"),
    ]
    for (rel, at, (px, py), size, mode) in parts:
        if t < at:
            continue
        q = clamp((t - at) / 0.22)
        spr = SPR(rel, w=int(size * (1 + 0.9 * (1 - ease_out(q)))))
        yy = py - (1 - ease_out(q)) * 190
        blit(cnv, spr, px, yy, anchor="cb" if mode == "wb" else "cc")
        if q < 0.5:
            blit(cnv, impact_star(int(size * 1.9 * (1 - q))), px, yy, anchor="cc")

    # 상점 패널 (아래에서 올라온다)
    sp = clamp(t / 0.35)
    # 완전히 화면 밖으로 내려가면 사각형 좌표가 뒤집히므로 아래쪽을 묶어둔다
    panel_y = min(H - 60, H - 196 + (1 - ease_out(sp)) * 240)
    d = ImageDraw.Draw(cnv)
    d.rectangle([40, panel_y, W - 40, H - 24], fill=(13, 13, 13))
    d.rectangle([40, panel_y, W - 40, H - 24], outline=(238, 238, 238), width=4)
    d.rectangle([50, panel_y + 10, W - 50, H - 34], outline=(238, 238, 238), width=2)
    f = FNT(lang, "punch", 32)
    text(cnv, (W / 2, panel_y + 36), L["shop_title"], f, fill=(245, 245, 245), anchor="mm")

    icons = ["RightHMG.png", "RightPlasmaCannon.png", "Machete.png",
             "Accessories/Crown-transparent.png"]
    for i, rel in enumerate(icons):
        cx = 156 + i * 216
        cy = panel_y + 122
        d.rectangle([cx - 84, cy - 52, cx + 84, cy + 52], fill=(34, 34, 34),
                    outline=(226, 226, 226), width=3)
        src = A(rel)
        src = src.crop(src.getbbox())
        sc = min(146 / src.width, 80 / src.height)
        src = src.resize((max(1, int(src.width * sc)), max(1, int(src.height * sc))), LANCZOS)
        blit(cnv, src, cx, cy, anchor="cc")
        if t > 0.5 + i * 0.18:
            f2 = FNT(lang, "hud", 24)
            d.rectangle([cx + 16, cy + 26, cx + 82, cy + 50], fill=(240, 240, 240))
            text(cnv, (cx + 49, cy + 38), "SOLD", f2, fill=(16, 16, 16), anchor="mm")

    f3 = FNT(lang, "hud", 32)
    blink = (int(t * 8) % 2 == 0)
    text(cnv, (W - 72, panel_y + 36), L["shop_price"], f3,
         fill=(250, 250, 250) if blink else (150, 150, 150), anchor="rm")
    caption_plate(cnv, lang, L["shop_cap"], y=76, size=24)


def waves(cnv, t, dur, lang):
    """웨이브 1 → 20이 빠르게 넘어간다."""
    L = LANG[lang]
    p = clamp(t / dur)
    # 넘어가는 속도가 점점 빨라진다
    wave = int(1 + 19 * (p ** 1.45))
    wave = min(20, wave)
    seg = (p ** 1.45) * 19
    flash = 1.0 - clamp((seg - math.floor(seg)) * 6.0)

    ta = twos(t)
    draw_stage(cnv, ta * 90, HORIZON)
    dens = 4 + wave * 2
    for i in range(dens):
        zr = random.Random(9000 + wave * 31 + i)
        x = zr.uniform(-60, W + 60)
        row = zr.random()
        h = (162 + zr.uniform(-24, 30)) * (0.66 + 0.46 * (1 - row))
        blit(cnv, SPR(zombie_frame(t, i), h=int(h), flip=(zr.random() < 0.5)),
             x, GROUND_Y + 34 - row * 186, anchor="cb")
    draw_robot(cnv, W / 2, GROUND_Y, 210, t, bounce=True)

    f = FNT(lang, "punch", 86 if wave < 20 else 108)
    s = "%s %02d" % (L["wave"], wave)
    lay = text_layer((W, H), (W / 2, H / 2 - 60), s, f, fill=(250, 250, 250), stroke=10)
    lay = wobble(lay, t, amp=3, ang=0.8)
    cnv.paste(lay, (0, 0), lay)
    if flash > 0.05:
        ov = Image.new("RGBA", (W, H), (255, 255, 255, int(90 * flash)))
        cnv.paste(ov, (0, 0), ov)


def boss(cnv, t, dur, lang):
    """20웨이브 보스. 아주 크다."""
    L = LANG[lang]
    ta = twos(t)
    rng = random.Random(int(t * 24))
    draw_stage(cnv, 200, HORIZON, ground_dark=0.86)

    rise = clamp(t / 0.8)
    bh = int(180 + 524 * ease_out(rise))
    pulse = 1.0 + 0.04 * math.sin(ta * 9)
    bf = SEQ("BossMove")
    fi = int(twos(t, 8) * 8) % len(bf)
    dying = t > 2.25
    if not dying:
        blit(cnv, SPR(bf[fi], h=int(bh * pulse)), W / 2 + 90, GROUND_Y + 30, anchor="cb")
        if t > 0.9:  # 피격 이펙트
            hit = SEQ("ZombieHitEffect")
            for _ in range(3):
                hx = W / 2 + 90 + rng.uniform(-150, 150)
                hy = GROUND_Y - bh * rng.uniform(0.15, 0.8)
                blit(cnv, SPR(hit[rng.randrange(len(hit))], h=rng.randint(70, 130)),
                     hx, hy, anchor="cc")
    else:
        ex = SEQ("BossDeathExplosion")
        q = clamp((t - 2.25) / 0.9)
        fi = min(len(ex) - 1, int(q * (len(ex) - 1)))
        blit(cnv, SPR(ex[fi], h=int(620)), W / 2 + 90, GROUND_Y + 60, anchor="cb")

    # 아주 작은 로봇
    rr = draw_robot(cnv, 168, GROUND_Y + 10, 168, t, bounce=True)
    cr = SPR("Accessories/Crown-transparent.png", w=int(rr["head_w"] * 0.85))
    blit(cnv, cr, rr["top"][0], rr["top"][1] + 4, anchor="cb")

    if not dying:
        mf = SEQ("MuzzleFlash")
        for k in range(4):
            my = rr["y"] - rr["h"] * (0.4 + k * 0.13)
            g = WPN("HMG" if k % 2 else "PlasmaCannon", 1, 62)
            blit(cnv, g, rr["x"] + 78, my, anchor="cc")
            if rng.random() < 0.85:
                blit(cnv, SPR(mf[rng.randrange(len(mf))], h=rng.randint(52, 84), flip=True),
                     rr["x"] + 78 + g.width / 2, my, anchor="cc")
        dd = ImageDraw.Draw(cnv)
        for _ in range(14):
            bx = rr["x"] + rng.uniform(120, 620)
            by = GROUND_Y - rng.uniform(40, 330)
            dd.line([(bx, by), (bx + 40, by)], fill=(20, 20, 20), width=7)
            dd.line([(bx, by), (bx + 40, by)], fill=(250, 250, 250), width=3)


def logo(cnv, t, dur, lang):
    """키 아트 + 타이틀 로고."""
    L = LANG[lang]
    p = clamp(t / dur)
    src = A("UI/titleimage.png")
    z = 1.06 + 0.10 * p
    nh = int(H * z)
    nw = max(W, int(src.width * nh / src.height))
    bgim = src.resize((nw, nh), LANCZOS).convert("RGB")
    cnv.paste(bgim, (int((W - nw) / 2), int((H - nh) / 2 - 10 * p)))

    slam = 0.3
    if t > slam:
        q = clamp((t - slam) / 0.22)
        s = 1.0 + 1.6 * (1 - ease_out(q))
        lg = SPR("UI/title_logo.png", w=int(720 * s))
        blit(cnv, lg, W / 2, 236, anchor="cc")
        if q < 0.45:
            ov = Image.new("RGBA", (W, H), (255, 255, 255, int(150 * (1 - q / 0.45))))
            cnv.paste(ov, (0, 0), ov)
    if t > 0.62:
        f = FNT(lang, "punch", 27)
        lay = text_layer((W, H), (W / 2, 336), L["logo_sub"], f, fill=(246, 246, 246), stroke=6)
        cnv.paste(lay, (0, 0), lay)
    if t > 0.95:
        grad = Image.new("L", (1, H), 0)
        gd = ImageDraw.Draw(grad)
        for y in range(H):
            gd.point((0, y), fill=int(190 * max(0.0, (y - H * 0.66) / (H * 0.34)) ** 1.4))
        grad = grad.resize((W, H), BILINEAR)
        dark = Image.new("RGB", (W, H), (0, 0, 0))
        cnv.paste(dark, (0, 0), grad)
        blink = (int(t * 5) % 2 == 0)
        f = FNT(lang, "punch", 44)
        lay = text_layer((W, H), (W / 2, H - 132), L["logo_cta"], f,
                         fill=(252, 252, 252) if blink else (176, 176, 176), stroke=7)
        lay = wobble(lay, t, amp=1.4, ang=0.3)
        cnv.paste(lay, (0, 0), lay)
        f2 = FNT("en", "hud", 27)
        lay2 = text_layer((W, H), (W / 2, H - 82), L["logo_url"], f2,
                          fill=(232, 232, 232), stroke=5)
        cnv.paste(lay2, (0, 0), lay2)


# ---------------------------------------------------------------- TV 켜기/끄기
def _squeeze(inner, p, ph):
    """CRT가 켜지고 꺼질 때의 세로 찌그러짐."""
    out = Image.new("RGB", (W, H), (0, 0, 0))
    hh = max(2, int(H * ph))
    ww = max(2, int(W * p))
    sq = inner.resize((ww, hh), BILINEAR)
    out.paste(sq, ((W - ww) // 2, (H - hh) // 2))
    return out


def tv_on(cnv, t, dur, lang, inner=None):
    p = clamp(t / dur)
    if p < 0.2 or inner is None:
        cnv.paste((0, 0, 0), (0, 0, W, H))
        if p > 0.12:
            d = ImageDraw.Draw(cnv)
            k = (p - 0.12) / 0.08
            d.rectangle([W * 0.5 - W * 0.5 * k, H / 2 - 2, W * 0.5 + W * 0.5 * k, H / 2 + 2],
                        fill=(255, 255, 255))
        return
    q = clamp((p - 0.2) / 0.45)
    im = _squeeze(inner, 1.0, max(0.006, ease_out(q)))
    cnv.paste(im, (0, 0))
    if q < 0.9:
        d = ImageDraw.Draw(cnv)
        a = int(255 * (1 - q))
        ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        od = ImageDraw.Draw(ov)
        od.rectangle([0, H / 2 - 3, W, H / 2 + 3], fill=(255, 255, 255, a))
        cnv.paste(ov, (0, 0), ov)


def tv_off(cnv, t, dur, lang, inner=None):
    p = clamp(t / dur)
    if inner is None:
        inner = Image.new("RGB", (W, H), (0, 0, 0))
    if p < 0.28:
        ph = 1.0 - ease_in(p / 0.28) * 0.985
        cnv.paste(_squeeze(inner, 1.0, ph), (0, 0))
        ov = Image.new("RGBA", (W, H), (255, 255, 255, int(120 * (p / 0.28))))
        cnv.paste(ov, (0, 0), ov)
    elif p < 0.52:
        q = (p - 0.28) / 0.24
        wpx = max(2, int(W * (1 - ease_in(q) * 0.985)))
        d = ImageDraw.Draw(cnv)
        cnv.paste((0, 0, 0), (0, 0, W, H))
        d.rectangle([W / 2 - wpx / 2, H / 2 - 2.5, W / 2 + wpx / 2, H / 2 + 2.5],
                    fill=(255, 255, 255))
    else:
        q = clamp((p - 0.52) / 0.22)
        cnv.paste((0, 0, 0), (0, 0, W, H))
        r = max(0.0, 7 * (1 - q))
        if r > 0.4:
            d = ImageDraw.Draw(cnv)
            v = int(255 * (1 - q * 0.6))
            d.ellipse([W / 2 - r, H / 2 - r, W / 2 + r, H / 2 + r], fill=(v, v, v))


SCENES = {
    "tv_on": tv_on,
    "card_presents": card_presents,
    "walk": walk,
    "card_trouble": card_trouble,
    "horde": horde,
    "card_guns": card_guns,
    "massacre": massacre,
    "card_noreload": card_noreload,
    "shop": shop,
    "waves": waves,
    "card_boss": card_boss,
    "boss": boss,
    "logo": logo,
    "card_end": card_end,
    "tv_off": tv_off,
}
