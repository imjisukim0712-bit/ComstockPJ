# -*- coding: utf-8 -*-
"""컴스톡 PV - 옛날 미국 흑백 TV 광고(인포머셜) 장면들.

각 함수는 (cnv, t, dur, lang) 을 받아 색이 있는 RGB 캔버스에 장면을 그린다.
흑백/지직거림 처리는 render_pv.py의 tv_process가 마지막에 한 번만 한다.
"""
import math
import random

from PIL import Image, ImageDraw, ImageFilter

from pv_common import W, H, LANG
from pv_draw import (A, ASSET, VIDEO_FRAMES, SEQ, SPR, FNT, blit, ease_out, ease_in, clamp, twos,
                     draw_stage, impact_star, speed_lines, text, text_layer,
                     wobble, badge, arrow, announcer_bar, fine_print,
                     star_rating, strike, sweat, LANCZOS, BILINEAR)

HORIZON = 470
GROUND_Y = HORIZON + 78          # 발이 닿는 선
ROBOT = "Comstock.png"

_wcache = {}
_misc = {}


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


def draw_robot(cnv, cx, gy, h, t, bounce=True, squash=1.0, rot=0.0, alpha=1.0):
    """로봇을 그리고 주요 좌표를 돌려준다. gy는 발이 닿는 y."""
    ta = twos(t)
    dy = -abs(math.sin(ta * math.pi * 2.2)) * (h * 0.055) if bounce else 0.0
    spr = SPR(ROBOT, h=int(h), rot=rot)
    if squash != 1.0:
        spr = spr.resize((max(1, int(spr.width * squash)), spr.height), BILINEAR)
    px = cx - spr.width / 2
    py = gy + dy - spr.height
    blit(cnv, spr, cx, gy + dy, anchor="cb", alpha=alpha)
    return {
        "x": cx, "y": gy + dy, "w": spr.width, "h": spr.height,
        "face": (px + spr.width * 0.487, py + spr.height * 0.382),
        "top": (px + spr.width * 0.487, py + spr.height * 0.061),
        "head_w": 340 * (h / 720.0),
    }


def zombie_frame(t, idx=0, kind="Zombie"):
    fr = SEQ({"Zombie": "ZombieMove", "Sprinter": "SprinterMove",
              "Spitter": "SpitterMove", "Disruptor": "DisruptorMove",
              "Leader": "LeaderMove"}.get(kind, "ChargerMove"))
    return fr[int(twos(t, 10) * 10 + idx) % len(fr)]


def sunburst(angle_step):
    """1950년대 광고의 방사형 배경(느리게 돈다)."""
    key = ("@sun", angle_step)
    if key in _misc:
        return _misc[key]
    big = int(math.hypot(W, H)) + 40
    im = Image.new("RGB", (big, big), (34, 34, 34))
    d = ImageDraw.Draw(im)
    c = big / 2
    n = 20
    for i in range(n):
        a0 = math.tau * i / n + math.radians(angle_step)
        a1 = a0 + math.tau / (n * 2)
        d.polygon([(c, c),
                   (c + big * math.cos(a0), c + big * math.sin(a0)),
                   (c + big * math.cos(a1), c + big * math.sin(a1))], fill=(128, 128, 128))
    im = im.crop((int(c - W / 2), int(c - H / 2), int(c - W / 2) + W, int(c - H / 2) + H))
    if len(_misc) > 60:
        _misc.clear()
    _misc[key] = im
    return im


def crowd(cnv, t, n, seed, gy=None, spread=196, base_h=178, close=0.0, speed=70):
    """좀비 무리. close(0~1)가 커질수록 화면 가운데로 좁혀온다."""
    gy = GROUND_Y if gy is None else gy
    ta = twos(t)
    items = []
    for i in range(n):
        rng = random.Random(seed + i)
        side = 1 if i % 2 == 0 else -1
        row = rng.random()
        x0 = W / 2 + side * (250 + rng.uniform(0, 440) + i * 4)
        x = x0 - side * (ta * rng.uniform(speed * 0.6, speed * 1.4)) - side * close * 190
        y = gy + 48 - row * spread
        h = (base_h + rng.uniform(-26, 34)) * (0.66 + 0.46 * (1 - row))
        items.append((y, x, h, side, i))
    items.sort(key=lambda v: v[0])
    for (y, x, h, side, i) in items:
        if -160 < x < W + 160:
            kind = ("Zombie", "Zombie", "Zombie", "Sprinter", "Spitter", "Zombie",
                    "Disruptor", "Zombie", "Leader")[i % 9]
            blit(cnv, SPR(zombie_frame(t, i, kind), h=int(h), flip=(side < 0)), x, y,
                 anchor="cb")


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
        for (inset, wdt) in ((44, 5), (60, 2)):
            d.rectangle([inset, inset, W - inset, H - inset], outline=(232, 232, 232), width=wdt)
        for (x, y) in ((52, 52), (W - 52, 52), (52, H - 52), (W - 52, H - 52)):
            d.polygon([(x, y - 13), (x + 13, y), (x, y + 13), (x - 13, y)], fill=(232, 232, 232))
        _card_bg = im
    return _card_bg


def _card(cnv, lang, t, lines, kind="punch", sizes=(66,), pop=True, sub=None,
          sub_size=26, cy=None):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    fitted = []                             # 긴 번역문이 테두리를 넘지 않게 줄인다
    for i, s in enumerate(lines):
        sz = sizes[min(i, len(sizes) - 1)]
        f = FNT(lang, kind, sz)
        tw = d.textbbox((0, 0), s, font=f)[2]
        if tw > W - 190:
            sz = max(18, int(sz * (W - 190) / tw))
            f = FNT(lang, kind, sz)
        fitted.append((s, f, sz))
    total = sum(sz for (_s, _f, sz) in fitted) * 1.34
    y = (cy if cy is not None else H / 2) - total / 2 + fitted[0][2] * 0.67
    for (s, f, sz) in fitted:
        d.text((W / 2, y), s, font=f, fill=(244, 244, 244, 255), anchor="mm",
               stroke_width=6, stroke_fill=(14, 14, 14, 255))
        y += sz * 1.34
    if sub:
        f = FNT(lang, "serif", sub_size)
        d.text((W / 2, y + 16), sub, font=f, fill=(198, 198, 198, 255), anchor="mm")
    if pop:
        p = clamp(t / 0.22)
        s = 1.0 + 0.5 * (1 - ease_out(p)) - 0.06 * math.sin(ease_out(p) * math.pi)
        if abs(s - 1.0) > 0.01:
            nw, nh = max(2, int(W * s)), max(2, int(H * s))
            layer = layer.resize((nw, nh), BILINEAR).crop(
                ((nw - W) // 2, (nh - H) // 2, (nw - W) // 2 + W, (nh - H) // 2 + H))
    layer = wobble(layer, t, amp=1.8, ang=0.45)
    cnv.paste(layer, (0, 0), layer)


# ---------------------------------------------------------------- 0. 스튜디오 로고
LOGO_MODE = "video"   # "video" = 제작한 로고 영상 / "static" = 코드로 그린 로고 카드
LOGO_VIDEO = "pyramid_logo_intro.mp4"
LOGO_VIDEO_DUR = 2.5             # 원본 길이(초). 장면 길이에 맞춰 늘려 재생한다.


def card_presents(cnv, t, dur, lang):
    """TV를 켜면 가장 먼저 뜨는 스튜디오 로고 카드."""
    if LOGO_MODE == "video":
        _presents_video(cnv, t, dur, lang)
    else:
        _presents_static(cnv, t, dur, lang)


def _presents_video(cnv, t, dur, lang):
    """제작한 로고 영상을 그대로 재생한다(흑백/지직 처리는 tv_process가 한다).

    영상은 2.5초, 장면은 2.8초라 뒤에 죽은 시간이 생기지 않도록 재생 속도를
    dur에 맞춰 늘린다(약 0.89배 - 눈에 띄지 않는다).
    """
    frames = VIDEO_FRAMES(LOGO_VIDEO, w=W, h=H, crop="1440:1080:240:0", fps=24)
    src_t = clamp(t / max(dur, 1e-6)) * LOGO_VIDEO_DUR
    idx = min(len(frames) - 1, int(src_t * 24))
    cnv.paste(frames[idx], (0, 0))


def _presents_static(cnv, t, dur, lang):
    """코드로 그린 로고 카드(비교용 원본).

    등장 팝은 짧게 유지하고, 남은 시간은 로고가 아주 느리게 호흡하며 버틴다.
    2초 넘게 완전히 정지해 있으면 영상이 멈춘 것처럼 보인다.
    """
    L = LANG[lang]
    cnv.paste(card_bg(), (0, 0))

    q = clamp(t / 0.22)
    breath = 1.0 + 0.022 * math.sin(t * 1.5)        # 아주 느린 확대/축소
    s = (1.0 + 0.55 * (1 - ease_out(q))) * breath
    blit(cnv, ASSET("pyramid_logo.png", w=int(300 * s)), W / 2, 244, anchor="cc")
    if q < 0.5:
        ov = Image.new("RGBA", (W, H), (255, 255, 255, int(150 * (1 - q / 0.5))))
        cnv.paste(ov, (0, 0), ov)

    if t > 0.20:
        lay = wobble(text_layer((W, H), (W / 2, 396), L["presents1"], FNT(lang, "punch", 44),
                                fill=(244, 244, 244), stroke=6), t, amp=1.4, ang=0.35)
        cnv.paste(lay, (0, 0), lay)
    if t > 0.62:
        text(cnv, (W / 2, 444), L["presents2"], FNT(lang, "serif", 25),
             fill=(196, 196, 196), anchor="mm")
    if t > 1.15:                                    # 하단 장식선 (여백을 채운다)
        d = ImageDraw.Draw(cnv)
        wpx = int(clamp((t - 1.15) / 0.5) * 150)
        y = 496
        d.line([(W / 2 - wpx, y), (W / 2 + wpx, y)], fill=(200, 200, 200), width=3)
        if wpx > 20:
            d.polygon([(W / 2, y - 10), (W / 2 + 10, y), (W / 2, y + 10), (W / 2 - 10, y)],
                      fill=(200, 200, 200))


def blackout(cnv, t, dur, lang):
    """로고가 사라진 뒤 본편이 시작되기 전의 1초. 지직 한 번 튀고 가라앉는다.

    화면 자체는 거의 검다 - 튀는 지직은 tv_process가 이 장면 시작점(CUT)에서 만든다.
    완전한 검정은 죽어 보이므로 브라운관 잔광만 아주 옅게 남긴다.
    """
    cnv.paste((7, 7, 7), (0, 0, W, H))
    p = clamp(t / max(dur, 1e-6))
    if p < 0.45:                                   # 가운데 잔광이 서서히 꺼진다
        a = (1 - p / 0.45)
        glow = Image.new("L", (W // 6, H // 6), 0)
        ImageDraw.Draw(glow).ellipse(
            [W // 24, H // 12, W // 6 - W // 24, H // 6 - H // 12], fill=int(70 * a))
        glow = glow.filter(ImageFilter.GaussianBlur(6)).resize((W, H), BILINEAR)
        cnv.paste(Image.new("RGB", (W, H), (150, 150, 150)), (0, 0), glow)


# ---------------------------------------------------------------- 1. 문제 제기
def problem(cnv, t, dur, lang):
    """'좀비 때문에 고민이십니까?' - 광고 앞부분의 절망 파트."""
    L = LANG[lang]
    p = clamp(t / dur)
    draw_stage(cnv, twos(t) * 22, HORIZON, ground_dark=0.74)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 60))
    cnv.paste(dim, (0, 0), dim)

    crowd(cnv, t, 22, 8100, close=p, speed=46)
    wig = math.sin(twos(t) * 22) * 11
    r = draw_robot(cnv, W / 2, GROUND_Y, 196, t, bounce=True, rot=wig)
    for k in range(2):
        ph = (twos(t) * 2.2 + k * 0.5) % 1.0
        sweat(cnv, r["x"] + (34 if k else -34), r["top"][1] - 6 - ph * 46,
              1.0 - ph * 0.35)
    f = FNT(lang, "punch", 70)
    lay = wobble(text_layer((W, H), (r["x"] + 78, r["top"][1] - 30), "?!", f,
                            fill=(250, 250, 250), stroke=8), t, amp=3, ang=2.4)
    cnv.paste(lay, (0, 0), lay)

    announcer_bar(cnv, lang, L["prob1"], 92, size=40)
    if t > 0.95:                            # 두 번째 자막은 첫 줄을 읽을 시간을 준 뒤
        announcer_bar(cnv, lang, L["prob2"], H - 130, size=26)
    fine_print(cnv, lang, L["prob_fine"], y=H - 48)


def card_betterway(cnv, t, dur, lang):
    L = LANG[lang]
    cnv.paste(card_bg(), (0, 0))
    _card(cnv, lang, t, [L["better1"], L["better2"]], "punch", (52, 76), cy=H / 2 - 86)
    draw_robot(cnv, W / 2, H - 86, 190, t, bounce=True,
               rot=math.sin(twos(t) * 26) * 15)


# ---------------------------------------------------------------- 2. 제품 등장
def introducing(cnv, t, dur, lang):
    """'새롭게 출시' - 로고가 터져 나오고 NEW 배지가 붙는다."""
    L = LANG[lang]
    if t < 0.60:
        cnv.paste((10, 10, 10), (0, 0, W, H))
        f = FNT(lang, "punch", 44)
        a = clamp(t / 0.22)
        text(cnv, (W / 2, H / 2), L["intro_pre"], f, fill=(int(240 * a),) * 3, anchor="mm")
        return

    q = clamp((t - 0.60) / 0.30)
    cnv.paste(sunburst(int(twos(t) * 26) % 18), (0, 0))
    speed_lines(cnv, W / 2, H / 2, 9, 260, 560, random.Random(int(t * 12)), width=5,
                color=(246, 246, 246))
    draw_robot(cnv, W / 2, GROUND_Y + 118, int(340 * (0.45 + 0.55 * ease_out(q))), t)

    s = 1.0 + 1.5 * (1 - ease_out(q))
    blit(cnv, SPR("UI/title_logo.png", w=int(640 * s)), W / 2, 158, anchor="cc")
    if q < 0.5:
        ov = Image.new("RGBA", (W, H), (255, 255, 255, int(170 * (1 - q / 0.5))))
        cnv.paste(ov, (0, 0), ov)
    if t > 1.05:
        blit(cnv, badge(196, L["badge_new"], FNT(lang, "punch", 40), angle=-14),
             W - 132, 272, anchor="cc")
    if t > 1.30:
        announcer_bar(cnv, lang, L["intro_sub"], H - 96, size=30)


# ---------------------------------------------------------------- 3. 사용법 3단계
def steps(cnv, t, dur, lang):
    """'1단계 / 2단계 / 3단계는 없습니다'."""
    L = LANG[lang]
    seg = min(2, int(t / (dur / 3.0)))
    st = t - seg * (dur / 3.0)
    ta = twos(t)
    rng = random.Random(int(t * 24))
    draw_stage(cnv, ta * 26, HORIZON)

    cx, gy, rh = W / 2 - 30, GROUND_Y + 12, 264
    if seg == 2:
        crowd(cnv, t, 20, 5200, close=0.55, speed=120)

    r = draw_robot(cnv, cx, gy, rh, t)

    if seg == 0:
        zx = W + 60 - clamp(st / 1.25) * 420
        blit(cnv, SPR(zombie_frame(t), h=210), zx, GROUND_Y + 6, anchor="cb")
        if st > 0.62:
            arrow(cnv, zx + 128, GROUND_Y - 390, zx + 16, GROUND_Y - 168)
    elif seg == 1:
        if st > 0.38:
            q = clamp((st - 0.38) / 0.24)   # 등장 시점과 팝 기준점을 반드시 맞춘다
            g = WPN("HMG", 1, int(114 * (1 + 0.8 * (1 - ease_out(q)))))
            gx, gy2 = cx + 246, r["y"] - rh * 0.86
            blit(cnv, g, gx, gy2, anchor="cc")
            if q < 0.55:
                blit(cnv, impact_star(int(260 * (1 - q))), gx, gy2, anchor="cc")
            if st > 0.80:
                arrow(cnv, gx - 24, gy2 - 196, gx + 10, gy2 - 66)
    else:
        guns = [(-1, "HMG", -30, 112), (1, "HMG", -30, 112),
                (-1, "RocketLauncher", -120, 96), (1, "RocketLauncher", -120, 96),
                (-1, "PlasmaCannon", 56, 104), (1, "SawedOff", 56, 96),
                (-1, "SMG", 134, 82), (1, "LaserPistol", 134, 82)]
        mf = SEQ("MuzzleFlash")
        dd = ImageDraw.Draw(cnv)
        for (side, name, dy, gh) in guns:
            g = WPN(name, side, gh)
            gx = cx + side * (132 + gh * 0.50)
            gy2 = r["y"] - rh * 0.70 + dy
            blit(cnv, g, gx, gy2, anchor="cc")
            mx, my = gx + side * g.width / 2, gy2 + g.height * 0.06
            if rng.random() < 0.85:
                blit(cnv, SPR(mf[rng.randrange(len(mf))], h=rng.randint(84, 130),
                              flip=(side > 0)), mx, my, anchor="cc")
            for _ in range(4):
                bx = mx + side * rng.uniform(50, 480)
                by = my + rng.uniform(-10, 10)
                ln = rng.randint(30, 56)
                dd.line([(bx, by), (bx + side * ln, by)], fill=(20, 20, 20), width=9)
                dd.line([(bx, by), (bx + side * ln, by)], fill=(250, 250, 250), width=4)
        ex = SEQ("Explosion")
        for k in range(3):
            blit(cnv, SPR(ex[int((t * 16 + k * 4) % len(ex))], h=rng.randint(130, 190)),
                 W / 2 + (k - 1) * 300 + rng.uniform(-40, 40),
                 GROUND_Y - rng.uniform(0, 120), anchor="cc")

    blit(cnv, badge(188, str(seg + 1), FNT(lang, "punch", 82), angle=-9), 178, 208,
         anchor="cc")
    label = (L["step1"], L["step2"], L["step3"])[seg]
    head = ("%s %d" % (L["step_word"], seg + 1) if lang == "en"
            else "%d%s" % (seg + 1, L["step_word"]))
    announcer_bar(cnv, lang, "%s:  %s" % (head, label), H - 108, size=36)
    if seg == 2:
        announcer_bar(cnv, lang, L["steps_bar"], 86, size=30)


def card_butwait(cnv, t, dur, lang):
    L = LANG[lang]
    cnv.paste(sunburst(int(twos(t) * 30) % 18), (0, 0))
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 96))
    cnv.paste(dim, (0, 0), dim)
    _card(cnv, lang, t, [L["butwait1"], L["butwait2"]], "punch", (88, 66))


# ---------------------------------------------------------------- 4. 사은품
def more(cnv, t, dur, lang):
    """'함께 드립니다' - 파츠가 쏟아져 붙는다."""
    L = LANG[lang]
    draw_stage(cnv, 120, HORIZON, ground_dark=0.86)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 96))
    cnv.paste(dim, (0, 0), dim)

    r = draw_robot(cnv, W / 2 + 156, GROUND_Y - 20, 296, t, bounce=False)
    hw = r["head_w"]
    parts = [
        ("Accessories/8Bitsunglass-transparent.png", 0.35,
         (r["face"][0], r["face"][1] + 2), hw * 1.34, "cc"),
        ("Accessories/Crown-transparent.png", 0.78,
         (r["top"][0], r["top"][1] - 4), hw * 0.94, "cb"),
        ("Accessories/Unicon-transparent.png", 1.21,
         (r["top"][0] - hw * 0.62, r["top"][1] + 16), hw * 0.60, "cb"),
        ("Accessories/Joystick-transparent.png", 1.64,
         (r["top"][0] + hw * 0.64, r["top"][1] + 14), hw * 0.54, "cb"),
    ]
    for (rel, at, (px, py), size, anc) in parts:
        if t < at:
            continue
        q = clamp((t - at) / 0.22)
        spr = SPR(rel, w=int(size * (1 + 0.9 * (1 - ease_out(q)))))
        yy = py - (1 - ease_out(q)) * 210
        blit(cnv, spr, px, yy, anchor=anc)
        if q < 0.5:
            blit(cnv, impact_star(int(size * 1.9 * (1 - q))), px, yy, anchor="cc")
    for k, (side, name, dy, gh) in enumerate(
            ((-1, "PlasmaCannon", -66, 92), (1, "RocketLauncher", 44, 88))):
        at = 2.05 + k * 0.36
        if t < at:
            continue
        q = clamp((t - at) / 0.2)
        g = WPN(name, side, int(gh * (1 + 0.7 * (1 - ease_out(q)))))
        blit(cnv, g, r["x"] + side * 172, r["y"] - 296 * 0.66 + dy, anchor="cc")

    d = ImageDraw.Draw(cnv)                 # 사은품 목록
    d.rectangle([44, 128, 476, 462], fill=(12, 12, 12))
    d.rectangle([52, 136, 468, 454], outline=(238, 238, 238), width=3)
    text(cnv, (74, 182), L["incl_title"], FNT(lang, "punch", 29), fill=(246, 246, 246),
         anchor="lm")
    f2 = FNT(lang, "punch", 24)
    for i, key in enumerate(("incl1", "incl2", "incl3", "incl4")):
        if t < 0.62 + i * 0.44:
            continue
        y = 246 + i * 52
        d.polygon([(80, y - 9), (94, y), (80, y + 9)], fill=(246, 246, 246))
        text(cnv, (108, y), L[key], f2, fill=(240, 240, 240), anchor="lm")
    if t > 2.86:
        blit(cnv, badge(204, L["badge_free"], FNT(lang, "punch", 42), angle=13),
             168, 566, anchor="cc")


# ---------------------------------------------------------------- 5. 사용 전 / 사용 후
def beforeafter(cnv, t, dur, lang):
    """광고의 상징, 좌우 분할 비교 화면."""
    L = LANG[lang]
    rng = random.Random(int(t * 24))

    before = Image.new("RGB", (W, H), (0, 0, 0))
    draw_stage(before, 60, HORIZON, ground_dark=0.7)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 74))
    before.paste(dim, (0, 0), dim)
    for i in range(7):
        zr = random.Random(600 + i)
        x = W / 4 + (i - 3) * 84 + zr.uniform(-16, 16)
        blit(before, SPR(zombie_frame(t, i), h=int(150 + zr.uniform(-16, 22)),
                         flip=(x < W / 4)), x, GROUND_Y + 20 - zr.random() * 90,
             anchor="cb")
    rb = draw_robot(before, W / 4, GROUND_Y + 6, 168, t,
                    rot=math.sin(twos(t) * 20) * 9)
    sweat(before, rb["x"] - 32, rb["top"][1] - 8, 0.9)

    after = Image.new("RGB", (W, H), (0, 0, 0))
    draw_stage(after, 300, HORIZON)
    ra = draw_robot(after, W * 0.75, GROUND_Y + 6, 214, t)
    blit(after, SPR("Accessories/Crown-transparent.png", w=int(ra["head_w"] * 0.92)),
         ra["top"][0], ra["top"][1] + 2, anchor="cb")
    mf = SEQ("MuzzleFlash")
    for (side, name, dy, gh) in ((-1, "HMG", -22, 84), (1, "HMG", -22, 84),
                                 (-1, "PlasmaCannon", 62, 76), (1, "RocketLauncher", 62, 76)):
        g = WPN(name, side, gh)
        gx = ra["x"] + side * (104 + gh * 0.46)
        gy2 = ra["y"] - 214 * 0.66 + dy
        blit(after, g, gx, gy2, anchor="cc")
        if rng.random() < 0.8:
            blit(after, SPR(mf[rng.randrange(len(mf))], h=rng.randint(64, 100),
                            flip=(side > 0)), gx + side * g.width / 2, gy2, anchor="cc")
    for i in range(3):                      # 날아가는 좀비
        zr = random.Random(910 + i)
        blit(after, SPR(zombie_frame(t, i), h=118, rot=140 * (1 if i % 2 else -1)),
             W * 0.75 + (i - 1) * 200, GROUND_Y - 140 - zr.random() * 110, anchor="cc")
    ex = SEQ("Explosion")
    for k in range(3):
        blit(after, SPR(ex[int((t * 15 + k * 3) % len(ex))], h=rng.randint(110, 170)),
             W * 0.75 + (k - 1) * 186 + rng.uniform(-20, 20),
             GROUND_Y - 40 - rng.uniform(0, 110), anchor="cc")

    split = int(W / 2)
    wipe = clamp((t - 0.60) / 0.50)
    cnv.paste(before, (0, 0))
    if wipe > 0:
        x1 = split + int((W - split) * ease_out(wipe))
        if x1 > split:
            cnv.paste(after.crop((split, 0, x1, H)), (split, 0))
    d = ImageDraw.Draw(cnv)
    d.rectangle([split - 7, 0, split + 7, H - 96], fill=(16, 16, 16))
    d.rectangle([split - 4, 0, split + 4, H - 96], fill=(248, 248, 248))

    f = FNT(lang, "punch", 40)
    for (xx, s, show) in ((W / 4, L["before"], True), (W * 0.75, L["after"], wipe > 0.25)):
        if not show:
            continue
        tw = d.textbbox((0, 0), s, font=f)[2]
        d.rectangle([xx - tw / 2 - 26, 74, xx + tw / 2 + 26, 142], fill=(12, 12, 12))
        d.rectangle([xx - tw / 2 - 20, 80, xx + tw / 2 + 20, 136], outline=(240, 240, 240),
                    width=3)
        text(cnv, (xx, 108), s, f, fill=(246, 246, 246), anchor="mm")
    fine_print(cnv, lang, L["ba_fine"], y=H - 46)


# ---------------------------------------------------------------- 6. 고객 후기
def testimonial(cnv, t, dur, lang):
    """좀비 고객의 생생한 후기(연기자입니다)."""
    L = LANG[lang]
    draw_stage(cnv, 420, HORIZON, ground_dark=0.8)
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 88))
    cnv.paste(dim, (0, 0), dim)

    fr = SEQ("ZombieAttack")
    bob = math.sin(twos(t) * 9) * 9
    blit(cnv, SPR(fr[int(twos(t, 8) * 8) % len(fr)], h=540), W / 2 - 236, H + 54 + bob,
         anchor="cb")

    if t > 0.30:
        f = FNT(lang, "punch", 42)
        lay = wobble(text_layer((W, H), (W / 2 + 158, 206), L["testi"], f,
                                fill=(250, 250, 250), stroke=7), t, amp=2, ang=0.6)
        cnv.paste(lay, (0, 0), lay)
    if t > 0.85:
        star_rating(cnv, W / 2 + 158, 292, size=44)
    if t > 1.35:
        announcer_bar(cnv, lang, L["testi_name"], H - 134, size=27)
    fine_print(cnv, lang, L["testi_fine"], y=H - 50)


# ---------------------------------------------------------------- 7. 산업용 강도
def industrial(cnv, t, dur, lang):
    """보스전을 '내구성 시연'으로 판다."""
    L = LANG[lang]
    ta = twos(t)
    rng = random.Random(int(t * 24))
    draw_stage(cnv, 200, HORIZON, ground_dark=0.86)

    bh = int(200 + 500 * ease_out(clamp(t / 0.55)))
    bf = SEQ("BossMove")
    dying = t > 2.00
    if not dying:
        blit(cnv, SPR(bf[int(twos(t, 8) * 8) % len(bf)],
                      h=int(bh * (1 + 0.04 * math.sin(ta * 9)))),
             W / 2 + 120, GROUND_Y + 30, anchor="cb")
        hit = SEQ("ZombieHitEffect")
        for _ in range(3):
            blit(cnv, SPR(hit[rng.randrange(len(hit))], h=rng.randint(80, 140)),
                 W / 2 + 120 + rng.uniform(-150, 150),
                 GROUND_Y - bh * rng.uniform(0.15, 0.8), anchor="cc")
    else:
        ex = SEQ("BossDeathExplosion")
        q = clamp((t - 2.00) / 0.80)
        blit(cnv, SPR(ex[min(len(ex) - 1, int(q * (len(ex) - 1)))], h=640),
             W / 2 + 120, GROUND_Y + 60, anchor="cb")

    rr = draw_robot(cnv, 150, GROUND_Y + 10, 172, t)
    blit(cnv, SPR("Accessories/Crown-transparent.png", w=int(rr["head_w"] * 0.9)),
         rr["top"][0], rr["top"][1] + 2, anchor="cb")
    if not dying:
        mf = SEQ("MuzzleFlash")
        for k in range(4):
            my = rr["y"] - rr["h"] * (0.42 + k * 0.13)
            g = WPN("HMG" if k % 2 else "PlasmaCannon", 1, 64)
            blit(cnv, g, rr["x"] + 80, my, anchor="cc")
            if rng.random() < 0.85:
                blit(cnv, SPR(mf[rng.randrange(len(mf))], h=rng.randint(56, 88), flip=True),
                     rr["x"] + 80 + g.width / 2, my, anchor="cc")
        dd = ImageDraw.Draw(cnv)
        for _ in range(14):
            bx = rr["x"] + rng.uniform(120, 620)
            by = GROUND_Y - rng.uniform(40, 330)
            dd.line([(bx, by), (bx + 40, by)], fill=(20, 20, 20), width=7)
            dd.line([(bx, by), (bx + 40, by)], fill=(250, 250, 250), width=3)

    announcer_bar(cnv, lang, L["ind1"], 90, size=42)
    if t > 0.95:
        announcer_bar(cnv, lang, L["ind2"], H - 108, size=28)


# ---------------------------------------------------------------- 8. 가격
def price(cnv, t, dur, lang):
    """정가를 쫙 긋고 0원 도장을 찍는다."""
    L = LANG[lang]
    cnv.paste(sunburst(int(twos(t) * 22) % 18), (0, 0))
    dim = Image.new("RGBA", (W, H), (0, 0, 0, 142))
    cnv.paste(dim, (0, 0), dim)

    f = FNT(lang, "punch", 62)
    tw = ImageDraw.Draw(cnv).textbbox((0, 0), L["price_was"], font=f)[2]
    lay = text_layer((W, H), (W / 2, 186), L["price_was"], f, fill=(242, 242, 242), stroke=4)
    cnv.paste(lay, (0, 0), lay)
    if t > 0.62:
        q = clamp((t - 0.62) / 0.20)
        x0 = W / 2 - tw / 2 - 26
        strike(cnv, x0, x0 + (tw + 52) * ease_out(q), 186)
    if t > 1.12:
        text(cnv, (W / 2, 272), L["price_now"], FNT(lang, "punch", 33),
             fill=(240, 240, 240), anchor="mm")
    if t > 1.52:
        q = clamp((t - 1.52) / 0.22)
        f3 = FNT(lang, "punch", max(20, int(140 * (1.0 + 1.7 * (1 - ease_out(q))))))
        lay = wobble(text_layer((W, H), (W / 2, 408), L["price_free"], f3,
                                fill=(252, 252, 252), stroke=10), t, amp=2.6, ang=0.7)
        if q < 0.5:
            blit(cnv, impact_star(int(740 * (1 - q))), W / 2, 408, anchor="cc")
        cnv.paste(lay, (0, 0), lay)
    if t > 1.98:
        fb = FNT(lang, "punch", 40)
        blit(cnv, badge(180, L["badge_free"], fb, angle=-16), 186, 470, anchor="cc")
        blit(cnv, badge(164, L["badge_new"], fb, angle=15), W - 178, 452, anchor="cc")
    fine_print(cnv, lang, L["price_fine"], y=H - 64)


# ---------------------------------------------------------------- 9. 지금 바로!
def actnow(cnv, t, dur, lang):
    """웨이브 카운터가 폭주하는 뒤로 '지금 바로!'가 깜빡인다."""
    L = LANG[lang]
    p = clamp(t / dur)
    ta = twos(t)
    draw_stage(cnv, ta * 90, HORIZON)
    wave = min(20, int(1 + 19 * (p ** 1.25)))
    crowd(cnv, t, 6 + wave, 9000 + wave * 31, close=0.4, speed=110)
    draw_robot(cnv, W / 2, GROUND_Y, 196, t)

    lay = text_layer((W, H), (W - 98, 126), "%s %02d" % (L["wave"], wave),
                     FNT(lang, "hud", 50), fill=(250, 250, 250), anchor="rm", stroke=7)
    cnv.paste(lay, (0, 0), lay)

    blink = (int(t * 7) % 2 == 0)
    lay = wobble(text_layer((W, H), (W / 2, 296), L["act1"], FNT(lang, "punch", 100),
                            fill=(252, 252, 252) if blink else (168, 168, 168), stroke=11),
                 t, amp=3.4, ang=1.0)
    cnv.paste(lay, (0, 0), lay)
    announcer_bar(cnv, lang, L["act2"], H - 120, size=32)
    fine_print(cnv, lang, L["act_fine"], y=H - 52)


# ---------------------------------------------------------------- 10. 마무리
def _crawl_strip(lang):
    key = ("@crawl", lang)
    if key in _misc:
        return _misc[key]
    s = LANG[lang]["crawl"] * 2
    f = FNT(lang, "serif", 17)
    tw = int(ImageDraw.Draw(Image.new("RGB", (8, 8))).textbbox((0, 0), s, font=f)[2]) + 40
    im = Image.new("RGB", (tw, 30), (10, 10, 10))
    ImageDraw.Draw(im).text((0, 15), s, font=f, fill=(178, 178, 178), anchor="lm")
    _misc[key] = im
    return im


def cta(cnv, t, dur, lang):
    """로고 + itch.io 주소 + 아래로 흐르는 깨알 고지."""
    L = LANG[lang]
    p = clamp(t / dur)
    src = A("UI/titleimage.png")
    nh = int(H * (1.06 + 0.10 * p))
    nw = max(W, int(src.width * nh / src.height))
    cnv.paste(src.resize((nw, nh), LANCZOS).convert("RGB"),
              (int((W - nw) / 2), int((H - nh) / 2 - 10 * p)))

    if t > 0.40:
        q = clamp((t - 0.40) / 0.24)
        blit(cnv, SPR("UI/title_logo.png", w=int(720 * (1 + 1.6 * (1 - ease_out(q))))),
             W / 2, 222, anchor="cc")
        if q < 0.45:
            ov = Image.new("RGBA", (W, H), (255, 255, 255, int(150 * (1 - q / 0.45))))
            cnv.paste(ov, (0, 0), ov)
    if t > 0.82:
        lay = text_layer((W, H), (W / 2, 322), L["cta_sub"], FNT(lang, "punch", 27),
                         fill=(246, 246, 246), stroke=6)
        cnv.paste(lay, (0, 0), lay)

    grad = Image.new("L", (1, H), 0)        # 아래쪽을 어둡게 깔아 글자를 살린다
    gd = ImageDraw.Draw(grad)
    for y in range(H):
        gd.point((0, y), fill=int(205 * max(0.0, (y - H * 0.58) / (H * 0.42)) ** 1.3))
    cnv.paste(Image.new("RGB", (W, H), (0, 0, 0)), (0, 0), grad.resize((W, H), BILINEAR))

    if t > 1.22:
        blink = (int(t * 5) % 2 == 0)
        lay = wobble(text_layer((W, H), (W / 2, H - 222), L["cta_main"],
                                FNT(lang, "punch", 46),
                                fill=(252, 252, 252) if blink else (176, 176, 176),
                                stroke=7), t, amp=1.4, ang=0.3)
        cnv.paste(lay, (0, 0), lay)
        lay2 = text_layer((W, H), (W / 2, H - 168), L["cta_url"], FNT("en", "hud", 30),
                          fill=(236, 236, 236), stroke=5)
        cnv.paste(lay2, (0, 0), lay2)
    if t > 1.72:
        text(cnv, (W / 2, H - 122), L["cta_ops"], FNT(lang, "punch", 23),
             fill=(214, 214, 214), anchor="mm")

    strip = _crawl_strip(lang)              # 깨알 법적 고지가 옆으로 흐른다
    off = int(t * 330) % (strip.width // 2)
    d = ImageDraw.Draw(cnv)
    d.rectangle([0, H - 86, W, H - 48], fill=(10, 10, 10))
    cnv.paste(strip.crop((off, 0, off + W, 30)), (0, H - 82))
    d.line([(0, H - 86), (W, H - 86)], fill=(150, 150, 150), width=2)
    d.line([(0, H - 48), (W, H - 48)], fill=(150, 150, 150), width=2)


# ---------------------------------------------------------------- TV 켜기/끄기
def _squeeze(inner, p, ph):
    out = Image.new("RGB", (W, H), (0, 0, 0))
    hh = max(2, int(H * ph))
    ww = max(2, int(W * p))
    out.paste(inner.resize((ww, hh), BILINEAR), ((W - ww) // 2, (H - hh) // 2))
    return out


def tv_on(cnv, t, dur, lang, inner=None):
    p = clamp(t / dur)
    if p < 0.2 or inner is None:
        cnv.paste((0, 0, 0), (0, 0, W, H))
        if p > 0.12:
            k = (p - 0.12) / 0.08
            ImageDraw.Draw(cnv).rectangle(
                [W * 0.5 - W * 0.5 * k, H / 2 - 2, W * 0.5 + W * 0.5 * k, H / 2 + 2],
                fill=(255, 255, 255))
        return
    q = clamp((p - 0.2) / 0.45)
    cnv.paste(_squeeze(inner, 1.0, max(0.006, ease_out(q))), (0, 0))
    if q < 0.9:
        ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(ov).rectangle([0, H / 2 - 3, W, H / 2 + 3],
                                     fill=(255, 255, 255, int(255 * (1 - q))))
        cnv.paste(ov, (0, 0), ov)


def tv_off(cnv, t, dur, lang, inner=None):
    p = clamp(t / dur)
    if inner is None:
        inner = Image.new("RGB", (W, H), (0, 0, 0))
    if p < 0.28:
        cnv.paste(_squeeze(inner, 1.0, 1.0 - ease_in(p / 0.28) * 0.985), (0, 0))
        ov = Image.new("RGBA", (W, H), (255, 255, 255, int(120 * (p / 0.28))))
        cnv.paste(ov, (0, 0), ov)
    elif p < 0.52:
        q = (p - 0.28) / 0.24
        wpx = max(2, int(W * (1 - ease_in(q) * 0.985)))
        cnv.paste((0, 0, 0), (0, 0, W, H))
        ImageDraw.Draw(cnv).rectangle(
            [W / 2 - wpx / 2, H / 2 - 2.5, W / 2 + wpx / 2, H / 2 + 2.5],
            fill=(255, 255, 255))
    else:
        q = clamp((p - 0.52) / 0.22)
        cnv.paste((0, 0, 0), (0, 0, W, H))
        r = max(0.0, 7 * (1 - q))
        if r > 0.4:
            v = int(255 * (1 - q * 0.6))
            ImageDraw.Draw(cnv).ellipse([W / 2 - r, H / 2 - r, W / 2 + r, H / 2 + r],
                                        fill=(v, v, v))


SCENES = {
    "tv_on": tv_on,
    "card_presents": card_presents,
    "blackout": blackout,
    "problem": problem,
    "card_betterway": card_betterway,
    "introducing": introducing,
    "steps": steps,
    "card_butwait": card_butwait,
    "more": more,
    "beforeafter": beforeafter,
    "testimonial": testimonial,
    "industrial": industrial,
    "price": price,
    "actnow": actnow,
    "cta": cta,
    "tv_off": tv_off,
}
