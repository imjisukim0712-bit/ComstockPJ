# -*- coding: utf-8 -*-
"""컴스톡 숏츠 광고 - 41초 인포머셜 PV와는 완전히 다른, 새로 쓴 컬러 밈 장면 4개.

41초판(pv_scenes.py)의 장면을 하나도 가져다 쓰지 않는다. 로봇/좀비 스프라이트, 폰트,
배지 같은 저수준 그리기 도구만 공유하고, 장면 구성·대사·색은 전부 새로 짰다.
흑백 CRT 톤도 쓰지 않는다 - 컬러 그대로 빠르게 컷을 끊는 숏폼 밈 편집이다.
"""
import math
import random

from PIL import Image, ImageDraw, ImageOps

from pv_common import W, H, SHORTS_LANG
from pv_draw import (SPR, ASSET, FNT, blit, ease_out, clamp, twos, impact_star, text_layer,
                     wobble, cloud_img, skyline_img, ground_img, LANCZOS, BILINEAR)
from pv_scenes import HORIZON, GROUND_Y, crowd, SEQ

_misc = {}


# ---------------------------------------------------------------- 주인공
# ★ `Assets/Resources/Comstock.png`(41초판이 쓰는 그림)을 쓰면 안 된다.
#   그 파일은 **옛날 무기 아트가 박제된 낡은 합성본**이라 칼도 미니건도 밋밋한 회색이다.
#   현재 캐릭터는 주황 액센트가 들어간 마체테 + 미니건을 들고 있고, 사용자가 준
#   `뉴컴스톡.png`을 `assets/comstock_hero.png`로 반입해 쓴다(가로세로비 2.00 -
#   낡은 쪽 1.78보다 넓으므로 같은 높이를 주면 더 넓게 그려진다).
HERO = "comstock_hero.png"


def draw_hero(cnv, cx, gy, h, t, bounce=True, rot=0.0, alpha=1.0):
    """주인공을 그리고 좌표를 돌려준다. gy는 발이 닿는 y."""
    ta = twos(t)
    dy = -abs(math.sin(ta * math.pi * 2.2)) * (h * 0.055) if bounce else 0.0
    spr = ASSET(HERO, h=int(h))
    if rot:
        spr = spr.rotate(rot, resample=BILINEAR, expand=True)
    blit(cnv, spr, cx, gy + dy, anchor="cb", alpha=alpha)
    return {"x": cx, "y": gy + dy, "w": spr.width, "h": spr.height}


# ---------------------------------------------------------------- 배경 (컬러판)
def color_sky(top, bot, key):
    k = "@csky:" + key
    if k not in _misc:
        im = Image.new("RGB", (W, H))
        for y in range(H):
            f = y / H
            im.paste((int(top[0] + (bot[0] - top[0]) * f),
                      int(top[1] + (bot[1] - top[1]) * f),
                      int(top[2] + (bot[2] - top[2]) * f)), (0, y, W, y + 1))
        _misc[k] = im
    return _misc[k]


def tint_silhouette(im, dark, light):
    l = im.convert("L")
    colored = ImageOps.colorize(l, black=dark, white=light).convert("RGB")
    out = Image.merge("RGBA", (*colored.split(), im.getchannel("A")))
    return out


def stage_bg(cnv, camx, top, bot, tint_dark, tint_light, key, horizon=HORIZON, ground_dark=1.0):
    """하늘 그라디언트 + 색 입힌 스카이라인 실루엣 + 폐허 지면(원본 컬러 텍스처 그대로)."""
    cnv.paste(color_sky(top, bot, key), (0, 0))
    for i in range(3):
        c = cloud_img(i)
        cx = int((-camx * 0.12 + i * 430 + 120) % (W + 420)) - 210
        blit(cnv, c, cx, 60 + i * 46, anchor="lt")
    sk = "@tsl:%s:%s" % (tint_dark, tint_light)
    sl = _misc.get(sk)
    if sl is None:
        sl = tint_silhouette(skyline_img(), tint_dark, tint_light)
        _misc[sk] = sl
    off = int(camx * 0.35) % sl.width
    strip = Image.new("RGBA", (W + sl.width, sl.height), (0, 0, 0, 0))
    strip.alpha_composite(sl, (0, 0))
    strip.alpha_composite(sl, (sl.width, 0))
    crop = strip.crop((off, 0, off + W, sl.height))
    cnv.paste(crop, (0, horizon - sl.height + 34), crop)

    g = ground_img()
    gh = H - horizon
    band = g.crop((0, 0, g.width, min(g.height, gh)))
    off2 = int(camx) % band.width
    tile = Image.new("RGB", (band.width * 2, band.height))
    tile.paste(band, (0, 0))
    tile.paste(band, (band.width, 0))
    ground = tile.crop((off2, 0, off2 + W, band.height)).resize((W, gh), BILINEAR)
    if ground_dark != 1.0:
        ground = Image.blend(Image.new("RGB", ground.size, (0, 0, 0)), ground, ground_dark)
    cnv.paste(ground, (0, horizon))
    d = ImageDraw.Draw(cnv)
    d.line([(0, horizon), (W, horizon)], fill=(40, 26, 20), width=7)


def color_sunburst(c1, c2, bg, angle_step, key):
    k = "@csun:%s:%d" % (key, angle_step)
    if k in _misc:
        return _misc[k]
    big = int(math.hypot(W, H)) + 40
    im = Image.new("RGB", (big, big), bg)
    d = ImageDraw.Draw(im)
    c = big / 2
    n = 20
    for i in range(n):
        a0 = math.tau * i / n + math.radians(angle_step)
        a1 = a0 + math.tau / (n * 2)
        d.polygon([(c, c), (c + big * math.cos(a0), c + big * math.sin(a0)),
                   (c + big * math.cos(a1), c + big * math.sin(a1))],
                  fill=(c1 if i % 2 == 0 else c2))
    im = im.crop((int(c - W / 2), int(c - H / 2), int(c - W / 2) + W, int(c - H / 2) + H))
    if len(_misc) > 80:
        _misc.clear()
    _misc[k] = im
    return im


# ---------------------------------------------------------------- 자막/장식 (컬러판)
def caption(cnv, s, y, lang, size=42, fill=(255, 255, 255), bg=(196, 40, 40), font="punch",
            pad=16):
    f = FNT(lang, font, size)
    d = ImageDraw.Draw(cnv)
    bb = d.textbbox((0, 0), s, font=f)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    if tw > W - 140:
        size = max(16, int(size * (W - 140) / tw))
        f = FNT(lang, font, size)
        bb = d.textbbox((0, 0), s, font=f)
        tw, th = bb[2] - bb[0], bb[3] - bb[1]
    x0, x1 = W / 2 - tw / 2 - pad * 2, W / 2 + tw / 2 + pad * 2
    d.rectangle([x0, y - th / 2 - pad, x1, y + th / 2 + pad], fill=bg)
    d.rectangle([x0 + 4, y - th / 2 - pad + 4, x1 - 4, y + th / 2 + pad - 4], outline=fill,
                width=3)
    d.text((W / 2, y), s, font=f, fill=fill, anchor="mm")


def colored_strike(cnv, x0, x1, y, color=(235, 60, 50), width=9):
    d = ImageDraw.Draw(cnv)
    d.line([(x0, y + 11), (x1, y - 11)], fill=(20, 20, 20), width=width + 5)
    d.line([(x0, y + 11), (x1, y - 11)], fill=color, width=width)


# ---------------------------------------------------------------- 1. 밈 셋업
def meme_setup(cnv, t, dur, lang):
    """"좀비: 존재함 / 우리: 이 녀석을 만듦" - 숏폼 밈 구조(문제 컷 -> 리액션 컷)."""
    L = SHORTS_LANG[lang]
    beat = 1.5
    if t < beat:
        stage_bg(cnv, twos(t) * 20, (255, 138, 66), (255, 214, 140), (70, 20, 60), (150, 90, 130),
                 "dusk", ground_dark=0.85)
        crowd(cnv, t, 22, 8300, close=clamp(t / beat) * 0.55, speed=48)
        caption(cnv, L["meme1"], 100, lang, size=42, bg=(196, 40, 40))
        if t > 0.55:
            caption(cnv, L["meme1b"], H - 110, lang, size=30, bg=(30, 30, 30))
    else:
        t2 = t - beat
        stage_bg(cnv, twos(t) * 20, (255, 210, 90), (255, 240, 180), (60, 60, 20), (150, 150, 70),
                 "day", ground_dark=1.0)
        q = clamp(t2 / 0.28)
        rh = int(210 * (0.55 + 0.45 * ease_out(q)))
        if q < 0.6:
            blit(cnv, impact_star(int(560 * (1 - q / 0.6)), color=(255, 214, 60),
                                  outline=(190, 60, 20)), W / 2, GROUND_Y - rh * 0.55,
                 anchor="cc")
        draw_hero(cnv, W / 2, GROUND_Y, rh, t, bounce=True)
        caption(cnv, L["meme2"], H - 100, lang, size=38, bg=(30, 140, 90))
        if t2 > 0.6:
            caption(cnv, L["meme2b"], 90, lang, size=24, bg=(30, 30, 30))


# ---------------------------------------------------------------- 2. 스펙시트
def spec_sheet(cnv, t, dur, lang):
    """가짜 '스펙시트'가 툭툭 채워진다 - 41초판의 "1/2/3단계" 대신 완전히 새 장치."""
    L = SHORTS_LANG[lang]
    cnv.paste(color_sunburst((255, 196, 30), (255, 150, 10), (60, 26, 6),
                             int(twos(t) * 22) % 18, "gold"), (0, 0))
    # 새 캐릭터는 가로가 넓어(2.00) 같은 높이를 줘도 더 퍼진다 - 칼끝은 패널 뒤로 가려지고
    # 미니건 총열이 오른쪽 화면 밖으로 살짝 나가는 구도가 된다(의도).
    draw_hero(cnv, W * 0.78, GROUND_Y + 44, 225, t, bounce=True)
    d = ImageDraw.Draw(cnv)
    d.rectangle([40, 108, 566, 566], fill=(18, 26, 46))
    d.rectangle([48, 116, 558, 558], outline=(255, 210, 60), width=4)
    text_f = FNT(lang, "punch", 30)
    d.text((70, 156), L["spec_title"], font=text_f, fill=(255, 255, 255), anchor="lm")
    f2 = FNT(lang, "punch", 25)
    for i, key in enumerate(("spec1", "spec2", "spec3", "spec4")):
        at = 0.30 + i * 0.55
        if t < at:
            continue
        y = 232 + i * 78
        q = clamp((t - at) / 0.16)
        s = 1.0 + 0.7 * (1 - ease_out(q))
        d.polygon([(76, y - 12 * s), (76 + 18 * s, y), (76, y + 12 * s)], fill=(90, 220, 210))
        d.text((110, y), L[key], font=f2, fill=(255, 255, 255), anchor="lm")
        if q < 0.5:
            blit(cnv, impact_star(int(90 * (1 - q)), color=(255, 214, 60),
                                  outline=(190, 60, 20)), 76, y, anchor="cc")
    caption(cnv, L["spec_bar"], H - 74, lang, size=26, bg=(196, 30, 110))


# ---------------------------------------------------------------- 3. 난사 개그
def chaos_gag(cnv, t, dur, lang):
    """조준을 안 하고 아무 데나 쏜다 - 그래도 어쩌다 다 맞는다는 물리 개그."""
    L = SHORTS_LANG[lang]
    rng = random.Random(int(t * 24))
    stage_bg(cnv, twos(t) * 60, (255, 150, 90), (255, 210, 130), (70, 24, 50), (150, 80, 110),
             "chaos", ground_dark=0.9)
    crowd(cnv, t, 14, 6600, close=0.5, speed=90)
    r = draw_hero(cnv, W / 2, GROUND_Y, 214, t, rot=math.sin(twos(t) * 30) * 20)
    mf = SEQ("MuzzleFlash")
    dd = ImageDraw.Draw(cnv)
    for k in range(6):
        ang = rng.uniform(0, math.tau)
        gx = r["x"] + math.cos(ang) * 40
        gy2 = r["y"] - r["h"] * 0.6 + math.sin(ang) * 30
        if rng.random() < 0.7:
            blit(cnv, SPR(mf[rng.randrange(len(mf))], h=rng.randint(60, 110)), gx, gy2,
                 anchor="cc")
        ln = rng.uniform(200, 480)
        ex, ey = gx + math.cos(ang) * ln, gy2 + math.sin(ang) * ln
        dd.line([(gx, gy2), (ex, ey)], fill=(150, 40, 10), width=8)
        dd.line([(gx, gy2), (ex, ey)], fill=(255, 214, 70), width=4)
    ex = SEQ("Explosion")
    for k in range(3):
        blit(cnv, SPR(ex[int((t * 18 + k * 5) % len(ex))], h=rng.randint(120, 190)),
             rng.uniform(80, W - 80), rng.uniform(GROUND_Y - 260, GROUND_Y), anchor="cc")
    caption(cnv, L["chaos1"], 96, lang, size=38, bg=(196, 40, 40))
    if t > 0.9:
        caption(cnv, L["chaos2"], H - 100, lang, size=27, bg=(30, 140, 90))


# ---------------------------------------------------------------- 4. 가격 + CTA
def price_cta(cnv, t, dur, lang):
    """정가 취소선 -> 0원 -> 로고 + itch.io 링크. 41초판과 다른 문구·색으로 새로 짰다."""
    L = SHORTS_LANG[lang]
    cnv.paste(color_sunburst((150, 40, 190), (90, 20, 140), (24, 8, 40),
                             int(twos(t) * 20) % 18, "purple"), (0, 0))

    f = FNT(lang, "punch", 46)
    lay = text_layer((W, H), (W / 2, 150), L["price_was2"], f, fill=(238, 238, 238), stroke=4)
    cnv.paste(lay, (0, 0), lay)
    if t > 0.35:
        q = clamp((t - 0.35) / 0.16)
        d = ImageDraw.Draw(cnv)
        tw = d.textbbox((0, 0), L["price_was2"], font=f)[2]
        x0 = W / 2 - tw / 2 - 20
        colored_strike(cnv, x0, x0 + (tw + 40) * ease_out(q), 150)
    if t > 0.65:
        q = clamp((t - 0.65) / 0.22)
        f3 = FNT(lang, "punch", max(20, int(120 * (1.0 + 1.5 * (1 - ease_out(q))))))
        lay = wobble(text_layer((W, H), (W / 2, 330), L["price_free"], f3,
                                fill=(110, 235, 120), stroke=10), t, amp=2.2, ang=0.6)
        if q < 0.5:
            blit(cnv, impact_star(int(640 * (1 - q)), color=(255, 214, 60),
                                  outline=(190, 60, 20)), W / 2, 330, anchor="cc")
        cnv.paste(lay, (0, 0), lay)
    if t > 1.15:
        blit(cnv, SPR("UI/title_logo.png", w=420), W / 2, 468, anchor="cc")
    if t > 1.55:
        blink = (int(t * 6) % 2 == 0)
        lay = wobble(text_layer((W, H), (W / 2, 590), L["cta_main2"], FNT(lang, "punch", 32),
                                fill=(255, 255, 255) if blink else (255, 214, 60),
                                stroke=6), t, amp=1.2, ang=0.3)
        cnv.paste(lay, (0, 0), lay)
        lay2 = text_layer((W, H), (W / 2, 632), L["cta_url"], FNT("en", "hud", 26),
                          fill=(255, 214, 60), stroke=4)
        cnv.paste(lay2, (0, 0), lay2)


SCENES = {
    "meme_setup": meme_setup,
    "spec_sheet": spec_sheet,
    "chaos_gag": chaos_gag,
    "price_cta": price_cta,
}
