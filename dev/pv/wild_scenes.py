# -*- coding: utf-8 -*-
"""컴스톡 PV "야생의 좀비" - 자연 다큐멘터리 패러디 장면 9개.

기존 3종 영상(41초 흑백 인포머셜 / 15초 컬러 밈 숏츠 / 세로형 미국 광고)과 겹치는 장면·
문구·편집 문법이 하나도 없다. 공유하는 것은 게임 스프라이트를 불러오는 저수준 도구뿐이다.

★ 1차본이 "영상하고 자막이 조잡하다"고 지적받아 고친 것들 - 원인이 넷이었다.
  1. **소재 해상도**: 걷기 애니메이션(`ZombieMove`)은 250x250인데 클로즈업에서 566px로
     늘려 뭉갰다. 그래서 h가 크면 고해상도 스틸(`Enemy_zombie.png` 1254px)로 갈아탄다
     (`_pick`). 애니메이션은 작게 나오는 원경/중경에만 쓴다.
  2. **접지 그림자가 없어 스프라이트가 떠 보였다**: 모든 피사체 발밑에 부드러운 타원
     그림자를 깐다(`shadow`).
  3. **배경이 평평했다**: 같은 스카이라인 띠를 두 겹 붙인 것뿐이라 '벽'처럼 보였다.
     겹을 셋으로 늘리고(가운데 겹은 좌우 반전해 반복 티를 없앤다) 지평선 헤이즈 띠,
     초점 밖 전경 잔해(가장자리), 떠다니는 먼지/보케를 넣었다. 망원 실사 느낌은 사실
     **전경 오클루더**가 만든다.
  4. **자막 뒤판이 뭉개져 지저분했다**: 글자마다 6px 블러 판을 깔던 방식을 버리고,
     화면 아래 스크림(부드러운 어두운 그라디언트) + 2px 아래로 살짝 번진 또렷한 그림자로
     바꿨다. 자막은 가운데 정렬로 아래에서 9px 올라오며 나타난다.
"""
import math
import random

from PIL import Image, ImageDraw, ImageFilter, ImageOps, ImageFont

from wild_common import W, H, LANG, font_path
from pv_draw import (A, SPR, SEQ, blit, ease_out, clamp, twos, skyline_img, ground_img,
                     LANCZOS, BILINEAR)
from pv_scenes import zombie_frame, ROBOT

HORIZON = 430
GROUND_Y = 556                    # 피사체 발이 닿는 선

_c = {}                           # 배경/텍스트 레이어 캐시
_fnt = {}
_bl = {}                          # 스프라이트 변형(스틸/블러/실루엣) 캐시


def FW(lang, kind, size):
    key = (lang, kind, size)
    f = _fnt.get(key)
    if f is None:
        f = ImageFont.truetype(font_path(lang, kind), size)
        _fnt[key] = f
    return f


def fade(t, t0, t1, fin=0.42, fout=0.36):
    """t0에 서서히 들어오고 t1에 서서히 빠지는 0~1 값(다큐 자막의 호흡)."""
    if t < t0 or t > t1:
        return 0.0
    return min(clamp((t - t0) / fin), clamp((t1 - t) / fout))


# ---------------------------------------------------------------- 배경(얕은 초점)
PAL = {
    "gold": dict(sky=((150, 163, 181), (238, 200, 142)),
                 far=((110, 102, 92), (192, 174, 144)), mid=((84, 72, 60), (162, 140, 112)),
                 near=((58, 48, 41), (128, 106, 80)), fg=(30, 24, 19),
                 ground=((74, 60, 47), (216, 184, 142)), blur=(8.0, 5.0, 3.0, 1.9),
                 haze=(255, 226, 176), dark=1.0),
    "soft": dict(sky=((150, 163, 181), (238, 200, 142)),
                 far=((110, 102, 92), (192, 174, 144)), mid=((84, 72, 60), (162, 140, 112)),
                 near=((58, 48, 41), (128, 106, 80)), fg=(30, 24, 19),
                 ground=((74, 60, 47), (216, 184, 142)), blur=(17.0, 14.0, 11.0, 7.0),
                 haze=(255, 226, 176), dark=1.0),
    "dust": dict(sky=((166, 155, 149), (244, 190, 126)),
                 far=((126, 108, 92), (208, 178, 138)), mid=((98, 78, 62), (178, 146, 110)),
                 near=((70, 53, 43), (142, 110, 80)), fg=(34, 25, 18),
                 ground=((82, 62, 46), (222, 178, 132)), blur=(9.0, 5.5, 3.4, 2.1),
                 haze=(255, 214, 152), dark=1.0),
    "ash": dict(sky=((130, 128, 130), (200, 178, 152)),
                far=((94, 92, 90), (154, 148, 142)), mid=((72, 69, 66), (126, 120, 114)),
                near=((48, 46, 44), (98, 92, 86)), fg=(24, 23, 22),
                ground=((58, 54, 48), (170, 158, 144)), blur=(9.0, 5.5, 3.4, 2.3),
                haze=(226, 220, 210), dark=0.90),
}


def _grad(top, bot, key):
    k = "@sky:" + key
    im = _c.get(k)
    if im is None:
        im = Image.new("RGB", (W, H))
        d = ImageDraw.Draw(im)
        for y in range(H):
            f = clamp(y / (HORIZON + 40.0)) ** 0.85
            d.line([(0, y), (W, y)],
                   fill=tuple(int(top[i] + (bot[i] - top[i]) * f) for i in range(3)))
        _c[k] = im
    return im


def _tint(im, dark, light):
    """회색 실루엣/텍스처에 색을 입힌다(알파는 그대로 유지)."""
    col = ImageOps.colorize(im.convert("L"), black=dark, white=light).convert("RGB")
    if im.mode == "RGBA":
        return Image.merge("RGBA", (*col.split(), im.getchannel("A")))
    return col


def _wide(im):
    """가로로 이어붙일 수 있게 두 장을 붙여 캐시한다."""
    fill = (0, 0, 0, 0) if im.mode == "RGBA" else (0, 0, 0)
    out = Image.new(im.mode, (im.width * 2, im.height), fill)
    out.paste(im, (0, 0))
    out.paste(im, (im.width, 0))
    return out


def _fg_strip(color, blur):
    """★ 초점 밖 전경 잔해 - 망원 실사 느낌은 대부분 이게 만든다.

    화면 가운데(피사체와 자막 자리)가 자주 비도록 덩어리 사이 간격을 넓게 둔다.
    """
    sw, sh = 2400, 210
    im = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    rng = random.Random(8123)
    x = 0
    while x < sw:
        x += rng.randint(240, 470)
        bw, bh = rng.randint(170, 360), rng.randint(45, 112)
        pts = [(x, sh)]
        n = rng.randint(3, 5)
        for i in range(n + 1):
            px = x + bw * i / n
            py = sh - bh * (0.45 + 0.55 * math.sin(math.pi * (i + 0.5) / (n + 1))) \
                + rng.randint(-22, 22)
            pts.append((px, py))
        pts.append((x + bw, sh))
        d.polygon(pts, fill=color + (150,))    # 불투명하면 검은 얼룩으로 읽힌다
        x += bw
    return im.filter(ImageFilter.GaussianBlur(blur))


def _haze(color, key):
    k = "@haze:" + key
    im = _c.get(k)
    if im is None:
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        for y in range(HORIZON - 160, HORIZON + 130):
            f = 1.0 - abs(y - (HORIZON - 14)) / 150.0
            if f <= 0:
                continue
            d.line([(0, y), (W, y)], fill=color + (int(44 * f ** 1.5),))
        _c[k] = im
    return im


def _sun(color, key):
    k = "@sun:" + key
    im = _c.get(k)
    if im is None:
        s = 760
        m = Image.new("L", (s, s), 0)
        d = ImageDraw.Draw(m)
        for i in range(18, 0, -1):
            r = s * 0.5 * i / 18.0
            d.ellipse([s / 2 - r, s / 2 - r, s / 2 + r, s / 2 + r],
                      fill=int(72 * (1 - i / 19.0) ** 1.7))
        m = m.filter(ImageFilter.GaussianBlur(40))
        col = Image.new("RGB", (s, s), color)
        _c[k] = Image.merge("RGBA", (*col.split(), m))
    return _c[k]


def _layers(key):
    k = "@lay:" + key
    got = _c.get(k)
    if got is not None:
        return got
    p = PAL[key]
    bf, bm, bn, bg = p["blur"]
    sl = skyline_img()
    far = _tint(sl, *p["far"]).resize((int(sl.width * 0.58), int(sl.height * 0.58)), LANCZOS)
    far = far.filter(ImageFilter.GaussianBlur(bf))
    # ★ 가운데 겹은 좌우를 반전해 붙인다 - 같은 스카이라인이 반복되는 티가 사라진다
    mid = _tint(sl, *p["mid"]).transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    mid = mid.resize((int(sl.width * 0.80), int(sl.height * 0.80)), LANCZOS)
    mid = mid.filter(ImageFilter.GaussianBlur(bm))
    near = _tint(sl, *p["near"]).filter(ImageFilter.GaussianBlur(bn))
    gh = H - HORIZON + 26
    src = ground_img()
    gnd = _tint(src.crop((0, 0, src.width, int(src.height * 0.62))), *p["ground"])
    gnd = gnd.resize((1560, gh), BILINEAR).filter(ImageFilter.GaussianBlur(bg)).convert("RGB")
    # 지면 띠 윗변에 알파 램프 - 안 주면 화면을 가로지르는 딱딱한 선이 생긴다
    ga = Image.new("L", gnd.size, 255)
    gd = ImageDraw.Draw(ga)
    for i in range(74):
        gd.line([(0, i), (gnd.width, i)], fill=int(255 * (i / 74.0) ** 1.5))
    gnd = Image.merge("RGBA", (*gnd.split(), ga))
    fg = _fg_strip(p["fg"], max(14.0, bn * 4.2))
    got = {"sky": _grad(p["sky"][0], p["sky"][1], key),
           "far": _wide(far), "farw": far.width,
           "mid": _wide(mid), "midw": mid.width,
           "near": _wide(near), "nearw": near.width,
           "gnd": _wide(gnd), "gndw": gnd.width, "gh": gh,
           "fg": _wide(fg), "fgw": fg.width,
           "haze": _haze(p["haze"], key), "sun": _sun(p["haze"], key), "dark": p["dark"]}
    _c[k] = got
    return got


def stage(cnv, camx, key="gold", dark=1.0, sun=True):
    """하늘 + 원경/중경/근경 실루엣 + 헤이즈 + 지면. camx는 픽셀 단위 카메라 위치."""
    L = _layers(key)
    cnv.paste(L["sky"], (0, 0))
    if sun:
        blit(cnv, L["sun"], W * 0.78, HORIZON - 120, anchor="cc")
    for (nm, par, dy) in (("far", 0.13, 58), ("mid", 0.26, 48), ("near", 0.44, 38)):
        o = int(camx * par) % L[nm + "w"]
        strip = L[nm].crop((o, 0, o + W, L[nm].height))
        cnv.paste(strip, (0, HORIZON - strip.height + dy), strip)
    cnv.paste(L["haze"], (0, 0), L["haze"])
    o = int(camx) % L["gndw"]
    g = L["gnd"].crop((o, 0, o + W, L["gh"]))
    cnv.paste(g, (0, HORIZON - 26), g)
    dk = dark * L["dark"]
    if dk < 1.0:
        cnv.paste(Image.blend(Image.new("RGB", (W, H), (10, 9, 12)), cnv.copy(), dk), (0, 0))


def foreground(cnv, camx, key="gold"):
    """초점 밖 전경 잔해를 화면 아래 가장자리에 얹는다(피사체보다 빠르게 흐른다)."""
    L = _layers(key)
    o = int(camx * 2.1) % L["fgw"]
    strip = L["fg"].crop((o, 0, o + W, L["fg"].height))
    cnv.paste(strip, (0, H - strip.height + 30), strip)


def dust(cnv, t, seed=4242, n=26, big=6):
    """떠다니는 먼지와 초점 밖 보케 - '실제로 찍은 화면' 느낌을 만드는 값싼 장치.

    절반 해상도로 그려 블러하고 늘린다(전체 해상도 블러는 프레임당 20ms를 먹는다).
    """
    sw, sh = W // 2, H // 2
    lay = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    rng = random.Random(seed)
    for i in range(n):
        x0 = rng.uniform(0, sw)
        y0 = rng.uniform(sh * 0.25, sh)
        x = (x0 - t * rng.uniform(3.0, 11.0)) % (sw + 40) - 20
        y = y0 + math.sin(t * rng.uniform(0.5, 1.3) + i) * 7
        r = rng.uniform(1.0, 2.6)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 244, 224, rng.randint(70, 150)))
    for i in range(big):
        x0 = rng.uniform(0, sw)
        y0 = rng.uniform(sh * 0.45, sh)
        x = (x0 - t * rng.uniform(6.0, 15.0)) % (sw + 60) - 30
        y = y0 + math.sin(t * 0.7 + i * 1.7) * 10
        r = rng.uniform(7.0, 14.0)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 238, 210, 46))
    lay = lay.filter(ImageFilter.GaussianBlur(2.2)).resize((W, H), BILINEAR)
    cnv.paste(lay, (0, 0), lay)


def smoke(cnv, t, n=9, seed=777, color=(206, 198, 188), peak=104):
    rng = random.Random(seed)
    sw, sh = W // 2, H // 2
    lay = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    for i in range(n):
        x0 = rng.uniform(70, sw - 70)
        r0 = rng.uniform(24, 47)
        rise = (t * (5.5 + i * 1.5) + rng.uniform(0, 4) * 11) % 115
        y = GROUND_Y / 2 - 12 - rise
        r = r0 * (1 + 0.55 * rise / 115.0)
        a = int(peak * clamp(1 - rise / 120.0))
        d.ellipse([x0 - r, y - r * 0.6, x0 + r, y + r * 0.6], fill=color + (a,))
    lay = lay.filter(ImageFilter.GaussianBlur(7)).resize((W, H), BILINEAR)
    cnv.paste(lay, (0, 0), lay)


# ---------------------------------------------------------------- 피사체
STILL = {"Zombie": "Enemy_zombie.png", "Charger": "Charger.png", "Leader": "Leader.png",
         "Disruptor": "Disruptor.png", "Spitter": "Spitter.png", "Sprinter": "Sprinter.png"}


def _still(name, h, flip=False):
    """고해상도 스틸을 내용 영역만 잘라 높이 h로 맞춘다(발끝이 정확히 gy에 온다)."""
    key = ("@still", name, int(h), flip)
    im = _bl.get(key)
    if im is None:
        src = A(name)
        src = src.crop(src.getbbox())
        nw = max(1, int(round(src.width * h / src.height)))
        im = src.resize((nw, max(1, int(h))), LANCZOS)
        if flip:
            im = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if len(_bl) > 700:
            _bl.clear()
        _bl[key] = im
    return im


def _pick(kind, h, t, i, flip):
    """★ h가 크면 애니메이션 프레임(250px)이 뭉개진다 - 고해상도 스틸로 갈아탄다."""
    if h > 310 and kind in STILL:
        return _still(STILL[kind], h, flip)
    return SPR(zombie_frame(t, i, kind), h=int(h), flip=flip)


def _blurred(im, key, blur):
    b = _bl.get(key)
    if b is None:
        pad = int(blur * 3) + 2
        b = Image.new("RGBA", (im.width + pad * 2, im.height + pad * 2), (0, 0, 0, 0))
        b.alpha_composite(im, (pad, pad))
        b = b.filter(ImageFilter.GaussianBlur(blur))
        if len(_bl) > 700:
            _bl.clear()
        _bl[key] = b
    return b


def shadow(cnv, x, gy, w, a=0.30, squash=0.22):
    """★ 접지 그림자 - 이게 없으면 스프라이트가 배경 위에 붙인 종이처럼 보인다."""
    key = ("@shd", int(w), round(a, 2), round(squash, 2))
    im = _c.get(key)
    if im is None:
        s = 200
        m = Image.new("L", (s, s), 0)
        d = ImageDraw.Draw(m)
        for i in range(16, 0, -1):
            r = s * 0.5 * i / 16.0
            d.ellipse([s / 2 - r, s / 2 - r, s / 2 + r, s / 2 + r],
                      fill=int(255 * a * (1 - i / 17.0) ** 1.3))
        m = m.filter(ImageFilter.GaussianBlur(11))
        col = Image.new("RGB", (s, s), (26, 19, 13))
        im = Image.merge("RGBA", (*col.split(), m))
        im = im.resize((max(4, int(w)), max(3, int(w * squash))), BILINEAR)
        _c[key] = im
    blit(cnv, im, x, gy, anchor="cc")


def zom(cnv, x, gy, h, t, i=0, kind="Zombie", flip=False, blur=0.0, alpha=1.0,
        shade=True):
    """좀비 한 마리. blur를 주면 원경 피사체처럼 초점이 빠진다."""
    spr = _pick(kind, h, t, i, flip)
    if shade and blur < 4.0:
        shadow(cnv, x, gy + 2, spr.width * 0.76, a=0.30 - 0.05 * min(1.0, blur / 4.0))
    if blur > 0.05:
        frame = 0 if h > 310 else int(twos(t, 10) * 10 + i) % 8
        spr = _blurred(spr, ("@zb", kind, int(h), flip, round(blur, 1), frame), blur)
    blit(cnv, spr, x, gy, anchor="cb", alpha=alpha)


def robot(cnv, x, gy, h, t, bounce=True, reveal=1.0, shade=True):
    """로봇. reveal<1이면 역광 실루엣에서 원본 색으로 드러난다(스프라이트 변형 없음)."""
    spr = SPR(ROBOT, h=int(h))
    if reveal < 0.99:
        key = (ROBOT, int(h), "sil")
        sil = _bl.get(key)
        if sil is None:
            rgb = spr.convert("RGB").point(lambda v: int(v * 0.32))
            sil = Image.merge("RGBA", (*rgb.split(), spr.getchannel("A")))
            _bl[key] = sil
        spr = Image.blend(sil, spr, clamp(reveal))
    dy = -abs(math.sin(twos(t) * math.pi * 2.2)) * (h * 0.05) if bounce else 0.0
    if shade:
        shadow(cnv, x, gy + 4, spr.width * 0.50, a=0.34)
    blit(cnv, spr, x, gy + dy, anchor="cb")
    return {"x": x, "y": gy + dy, "w": spr.width, "h": spr.height}


def glow(cnv, cx, cy, r, color=(255, 214, 150), strength=0.55):
    key = ("@glow", int(r), color, round(strength, 2))
    im = _c.get(key)
    if im is None:
        s = int(r * 2)
        m = Image.new("L", (s, s), 0)
        d = ImageDraw.Draw(m)
        for i in range(14, 0, -1):
            rr = r * i / 14.0
            d.ellipse([r - rr, r - rr, r + rr, r + rr],
                      fill=int(255 * strength * (1 - i / 15.0) ** 1.6))
        m = m.filter(ImageFilter.GaussianBlur(r * 0.18))
        col = Image.new("RGB", (s, s), color)
        im = Image.merge("RGBA", (*col.split(), m))
        _c[key] = im
    blit(cnv, im, cx, cy, anchor="cc")


def flash(cnv, a, color=(255, 236, 206)):
    """폭발 순간 화면이 살짝 밝아진다 - 붙인 폭발 스프라이트가 '터진 것'으로 읽힌다."""
    if a <= 0.01:
        return
    cnv.paste(Image.blend(cnv.copy(), Image.new("RGB", (W, H), color), min(0.42, a)), (0, 0))


# ---------------------------------------------------------------- 자막
def _cache_layer(key, build):
    im = _c.get(key)
    if im is None:
        im = build()
        _c[key] = im
    return im


def _put(cnv, layer, alpha, dy=0):
    if alpha <= 0.01:
        return
    if alpha < 0.995:
        a = layer.getchannel("A").point(lambda v: int(v * alpha))
        layer = Image.merge("RGBA", (*layer.split()[:3], a))
    if cnv.mode == "RGBA":
        cnv.alpha_composite(layer, (0, max(0, int(dy))))
    else:
        cnv.paste(layer, (0, int(dy)), layer)


def _crisp(build_text, color=(250, 249, 245), blur=2.6, shadow_a=0.90, drop=2):
    """또렷한 글자 + 아래로 2px 번진 그림자.

    ★ 1차본은 글자마다 6px 블러 판을 깔아 '지저분한 얼룩'이 됐다. 가독성은 스크림이
      맡고, 글자는 얇고 또렷하게 둔다.
    """
    mask = Image.new("L", (W, H), 0)
    build_text(ImageDraw.Draw(mask), 255)
    sh = mask.filter(ImageFilter.GaussianBlur(blur))
    sh = sh.point(lambda v: min(255, int(v * 1.45 * shadow_a)))
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.paste(Image.new("RGB", (W, H), (10, 8, 6)), (0, drop), sh)
    out.paste(Image.new("RGB", (W, H), color), (0, 0), mask)
    return out


def _scrim():
    """화면 아래 부드러운 어두운 그라디언트 - 자막 가독성을 여기서 확보한다."""
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    top = H - 230
    for y in range(top, H):
        v = min(1.0, (y - top) / 140.0) ** 1.35
        d.line([(0, y), (W, y)], fill=(8, 7, 6, int(168 * v)))
    return im


_measure = None


def _tlen(s, f):
    global _measure
    if _measure is None:
        _measure = ImageDraw.Draw(Image.new("L", (4, 4)))
    return _measure.textlength(s, font=f)


def caption(cnv, lang, s, t, t0, t1, size=None, y=None, fout=0.36):
    """다큐 내레이션 자막 - 가운데 아래 한 줄. 아래에서 9px 올라오며 나타난다."""
    a = fade(t, t0, t1, fout=fout)
    if a <= 0.01:
        return
    size = size or (30 if lang == "en" else 28)
    y = y or (H - 88)
    f = FW(lang, "cap", size)
    while _tlen(s, f) > W - 260 and size > 16:
        size -= 1
        f = FW(lang, "cap", size)
    _put(cnv, _cache_layer("@scrim", _scrim), a * 0.95)
    key = ("@cap", lang, s, size, y)
    lay = _cache_layer(key, lambda: _crisp(
        lambda d, v: d.text((W / 2, y), s, font=f, fill=v, anchor="mm")))
    _put(cnv, lay, a, 9 * (1 - ease_out(clamp((t - t0) / 0.55))))


def tracked(cnv, cx, y, lang, s, kind, size, tracking=7.0, alpha=1.0,
            color=(250, 249, 245)):
    """자간을 벌려 그리는 제목/표기용 글자(다큐 타이틀 카드의 관용 표현)."""
    if alpha <= 0.01:
        return
    f = FW(lang, kind, size)
    key = ("@trk", lang, s, kind, size, round(tracking, 1), int(cx), int(y), color)

    def build():
        ws = [_tlen(ch, f) for ch in s]
        total = sum(ws) + tracking * (len(s) - 1)

        def draw(d, v):
            xx = cx - total / 2.0
            for ch, w in zip(s, ws):
                d.text((xx, y), ch, font=f, fill=v, anchor="lm")
                xx += w + tracking
        return _crisp(draw, color)
    _put(cnv, _cache_layer(key, build), alpha)


def place_tag(cnv, lang, s, alpha):
    """왼쪽 위 위치/시각 표기 - 얇은 세로선 + 작은 글자(블러 얼룩을 쓰지 않는다)."""
    if alpha <= 0.01:
        return

    def build():
        f = FW(lang, "small", 18)
        im = _crisp(lambda d, v: d.text((106, 88), s, font=f, fill=v, anchor="lm"),
                    color=(246, 244, 238))
        ImageDraw.Draw(im).rectangle([90, 77, 92, 100], fill=(246, 244, 238, 235))
        return im
    _put(cnv, _cache_layer(("@place", lang, s), build), alpha)


def rule(cnv, cx, y, half, alpha, color=(238, 232, 218)):
    if alpha <= 0.01:
        return

    def build():
        im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(im).line([(cx - half, y), (cx + half, y)], fill=color + (200,))
        return im
    _put(cnv, _cache_layer(("@rule", int(cx), int(y), int(half)), build), alpha)


# ---------------------------------------------------------------- 무리
ROWS = ((-104, 86, 3.6), (-46, 132, 1.4), (12, 200, 0.0))   # (y오프셋, 키, 블러)


def _herd_list(n=14, seed=900):
    rng = random.Random(seed)
    kinds = ("Zombie", "Zombie", "Sprinter", "Zombie", "Spitter", "Zombie", "Leader",
             "Zombie", "Disruptor", "Zombie", "Charger")
    return [dict(row=i % 3, x0=rng.uniform(-100, W + 300), sp=rng.uniform(11, 26),
                 kind=kinds[i % len(kinds)], idx=i, dir=-1,
                 dyj=rng.uniform(-12, 16), hj=rng.uniform(0.88, 1.14)) for i in range(n)]


HERD = _herd_list()


def draw_herd(cnv, t, camx, zoom=1.0, freeze=None):
    """무리를 원경/중경/근경 3열로 그린다. freeze를 주면 그 시각에 무리가 멈춘다."""
    ta = t if freeze is None else min(t, freeze)
    items = []
    for z in HERD:
        dy, h, bl = ROWS[z["row"]]
        x = z["x0"] + z["dir"] * ta * z["sp"] - camx * (0.10 + 0.06 * z["row"])
        items.append((z["row"], x, GROUND_Y + dy + z["dyj"], h * zoom * z["hj"], bl, z))
    items.sort(key=lambda v: v[0])
    for (_row, x, gy, h, bl, z) in items:
        if -260 < x < W + 260:
            zom(cnv, x, gy, h, ta, z["idx"], z["kind"], flip=(z["dir"] > 0), blur=bl)




# ================================================================ 데이터 오버레이
POP0 = 431                        # 셋업에서 보여주는 좀비 개체 수(개그용 가짜 조사 수치)


def pop_overlay(cnv, lang, value, alpha, est=False, label=None):
    """★ 이번 판의 핵심 장치 - 다큐의 데이터 표기.

    숫자가 무너지는 것만으로 학살을 보여준다(자막으로 설명하지 않는다). 값마다 레이어를
    캐시하므로 숫자는 폭발 횟수에 맞춰 **띄엄띄엄** 떨어져야 한다 - 매 프레임 다른 값이면
    캐시가 터진다.
    """
    if alpha <= 0.01:
        return
    L = LANG[lang]

    def build():
        fl = FW(lang, "small", 17)
        fn = FW(lang, "num", 48)
        fe = FW(lang, "small", 15)

        def draw(d, v):
            d.text((W - 98, 84), label or L["pop"], font=fl, fill=v, anchor="rm")
            d.line([(W - 250, 103), (W - 98, 103)], fill=v, width=1)
            d.text((W - 98, 132), "%d" % value, font=fn, fill=v, anchor="rm")
            if est:
                d.text((W - 98, 168), L["pop_est"], font=fe, fill=v, anchor="rm")
        return _crisp(draw, color=(248, 246, 240))
    _put(cnv, _cache_layer(("@pop", lang, value, est, label), build), alpha)


# ---------------------------------------------------------------- 사냥 계획
def _kill_plan():
    """개체별 (등장, 폭발) 계획. cut은 어느 서브컷에서 보이는지다(오디오도 이걸 읽는다).

    ★ 개체 수 카운터가 폭발 한 번에 한 칸씩 떨어지므로, 마지막 개체가 터지는 시각이
      곧 카운터가 0이 되는 시각이다.
    """
    rng = random.Random(90210)
    kinds = ("Zombie", "Sprinter", "Charger", "Zombie", "Spitter", "Disruptor")
    out = []
    for t0 in (0.00, 0.24, 0.48, 0.72, 0.96):               # 서브컷 A (0.0~2.2)
        i = len(out)
        out.append(dict(cut="a", t0=t0, death=t0 + 0.55, side=1 if i % 2 == 0 else -1,
                        row=i % 3, idx=i, kind=kinds[i % 6],
                        sp=rng.uniform(170, 250), hs=rng.uniform(0.9, 1.15)))
    for t0 in (2.30, 2.85):                                 # 서브컷 B (클로즈업)
        i = len(out)
        out.append(dict(cut="b", t0=t0, death=t0 + 0.42, side=1, row=2, idx=i,
                        kind="Zombie", sp=0.0, hs=1.0))
    for t0 in (4.10, 4.32, 4.54, 4.76, 4.98, 5.16, 5.34):   # 서브컷 C
        i = len(out)
        out.append(dict(cut="c", t0=t0, death=t0 + 0.46, side=1 if i % 2 == 0 else -1,
                        row=i % 3, idx=i, kind=kinds[(i + 1) % 6],
                        sp=rng.uniform(180, 260), hs=rng.uniform(0.9, 1.15)))
    return out


KILLS = _kill_plan()
CUT_B, CUT_C = 2.20, 4.00


def pop_now(t):
    """폭발 한 번에 한 칸씩 떨어진다 - 마지막 개체가 터질 때 정확히 0이 된다."""
    n = sum(1 for z in KILLS if t >= z["death"])
    return max(0, int(round(POP0 * (1 - n / float(len(KILLS))))))


def _tracers(cnv, r, t, side_bias=0, n=5, ln=(250, 520), w=(7, 3)):
    rng = random.Random(int(t * 30) * 31 + 7)
    mf = SEQ("MuzzleFlash")
    d = ImageDraw.Draw(cnv)
    for k in range(n):
        side = side_bias or (1 if k % 2 == 0 else -1)
        gx = r["x"] + side * r["w"] * 0.30
        gy = r["y"] - r["h"] * 0.52 + rng.uniform(-22, 22)
        ex, ey = gx + side * rng.uniform(*ln), gy + rng.uniform(-64, 64)
        d.line([(gx, gy), (ex, ey)], fill=(228, 148, 66), width=w[0])
        d.line([(gx, gy), (ex, ey)], fill=(255, 242, 200), width=w[1])
        blit(cnv, SPR(mf[rng.randrange(len(mf))],
                      h=rng.randint(int(r["h"] * 0.30), int(r["h"] * 0.46))), gx, gy,
             anchor="cc")


def _kills(cnv, t, cut, wide=True):
    ex = SEQ("Explosion")
    fl = 0.0
    for z in KILLS:
        if z["cut"] != cut or t < z["t0"]:
            continue
        dy, hh, bl = ROWS[z["row"]]
        gy = GROUND_Y + dy
        x = W / 2 + z["side"] * (620 - (t - z["t0"]) * z["sp"])
        if not wide:
            x, gy, hh, bl = W * 0.82, H + 30, 330, 0.0
        if t < z["death"]:
            if -260 < x < W + 260:
                zom(cnv, x, gy, hh * z["hs"], t, z["idx"], z["kind"],
                    flip=(z["side"] < 0), blur=bl)
        elif t < z["death"] + 0.42:
            p = (t - z["death"]) / 0.42
            glow(cnv, x, gy - hh * 0.45, int(210 * (1 - p) + 60), (255, 198, 124), 0.7)
            blit(cnv, SPR(ex[min(len(ex) - 1, int(p * len(ex)))], h=int(hh * 2.6)),
                 x, gy - hh * 0.45, anchor="cc")
            fl = max(fl, (1 - p) ** 2.2 * (0.45 if wide else 0.75))
    flash(cnv, fl)


def _dirt_layer():
    """폭발에 튄 흙이 렌즈에 묻는다 - 촬영진이 현장에 있다는 증거."""
    rng = random.Random(51)
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    for _ in range(22):
        x, y = rng.uniform(0, W), rng.uniform(H * 0.42, H)
        r = rng.uniform(4, 24)
        d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8],
                  fill=(94, 70, 46, rng.randint(40, 110)))
    for _ in range(5):
        x, y = rng.uniform(60, W - 60), rng.uniform(H * 0.62, H - 20)
        r = rng.uniform(26, 58)
        d.ellipse([x - r, y - r * 0.55, x + r, y + r * 0.55], fill=(80, 58, 38, 62))
    return lay.filter(ImageFilter.GaussianBlur(4.0))


def lens_dirt(cnv, alpha):
    _put(cnv, _cache_layer("@dirt", _dirt_layer), alpha)


# ================================================================ 장면
# ---------------------------------------------------------------- 1. 셋업
def sc_coldopen(cnv, t, dur, lang):
    """훅 - 좀비 얼굴 클로즈업으로 시작하고, 개체 수 표기를 깔아둔다(뒤에서 무너진다)."""
    L = LANG[lang]
    camx = 690 + t * 5
    stage(cnv, camx, "soft", sun=False)
    zom(cnv, W * 0.46 + t * 7, H + 128, 628, t, 1, "Zombie")
    dust(cnv, t, seed=51, n=22, big=5)
    foreground(cnv, camx, "soft")
    pop_overlay(cnv, lang, POP0, fade(t, 1.15, dur + 1, 0.5, 0.4))
    caption(cnv, lang, L["cold1"], t, 0.50, 2.20)
    caption(cnv, lang, L["cold2"], t, 2.30, dur + 1)


# ---------------------------------------------------------------- 2. 타이틀 카드
def sc_title(cnv, t, dur, lang):
    """★ 부제의 "제 1 화"가 마지막 컷에서 "최종화"로 회수된다."""
    L = LANG[lang]
    cnv.paste(Image.new("RGB", (W, H), (8, 8, 10)), (0, 0))
    a1 = fade(t, 0.08, dur - 0.05, 0.30, 0.28)
    tracked(cnv, W / 2, 322, lang, L["title"], "title",
            64 if lang == "en" else 56, tracking=14 if lang == "en" else 11, alpha=a1)
    a2 = fade(t, 0.52, dur - 0.05, 0.30, 0.28)
    rule(cnv, W / 2, 372, 120, a2 * 0.8)
    tracked(cnv, W / 2, 406, lang, L["sub"], "small", 19, tracking=5.2, alpha=a2,
            color=(228, 221, 206))


# ---------------------------------------------------------------- 3. 무리
def sc_herd(cnv, t, dur, lang):
    L = LANG[lang]
    camx = 60 + t * 11
    stage(cnv, camx, "gold")
    draw_herd(cnv, t, camx, zoom=1.0 + 0.04 * (t / dur))
    dust(cnv, t + 3, seed=88)
    foreground(cnv, camx, "gold")
    place_tag(cnv, lang, L["place"], fade(t, 0.12, 1.95, 0.45, 0.4))
    pop_overlay(cnv, lang, POP0, fade(t, 0.02, dur + 1, 0.25, 0.4))
    caption(cnv, lang, L["herd1"], t, 0.22, dur + 1)


# ---------------------------------------------------------------- 4. 예외가 나타난다
def sc_arrival(cnv, t, dur, lang):
    L = LANG[lang]
    camx = 150 + t * 8 - clamp((t - 0.75) / 1.2) * 92        # 급하게 되돌리는 팬
    stage(cnv, camx, "dust")
    draw_herd(cnv, t, camx, freeze=1.30)
    if t > 0.70:
        p = clamp((t - 0.70) / 1.70)
        h = 46 + 208 * ease_out(p)
        gy = HORIZON + 20 + (GROUND_Y - HORIZON - 20) * ease_out(p)
        glow(cnv, W * 0.72, gy - h * 0.52, int(80 + 330 * p), (255, 212, 148), 0.75)
        robot(cnv, W * 0.72, gy, h, t, reveal=clamp((t - 1.60) / 0.80))
    dust(cnv, t + 15, seed=606)
    foreground(cnv, camx, "dust")
    pop_overlay(cnv, lang, POP0, fade(t, 0.02, dur + 1, 0.25, 0.4))
    caption(cnv, lang, L["arr1"], t, 0.12, dur + 1)


# ---------------------------------------------------------------- 5. 천적입니다
def sc_predator(cnv, t, dur, lang):
    """★ 반전 - 귀엽게 웃는 얼굴을 화면에 가득 채우고 자막은 딱 한 줄만 준다.

    얼굴은 스프라이트 위에서 세로 38.2% / 가로 48.7% 지점이다(pv_scenes.draw_robot과 같은
    비율). 그래서 gy = 얼굴목표y + 0.618*h, cx = 화면중앙 + 0.013*w 로 놓으면 얼굴이 정확히
    가운데 온다. ★ h를 매 프레임 바꾸면 2500px짜리 스프라이트가 프레임마다 새로 캐시돼
    메모리가 터진다 - h는 고정하고 cx만 미세하게 흔든다.
    """
    L = LANG[lang]
    stage(cnv, 1240 + t * 3, "soft", sun=False)
    h = 1520
    w = h * 1627.0 / 967.0
    cx = W * 0.5 + 0.013 * w + math.sin(t * 1.6) * 7
    robot(cnv, cx, 302 + 0.618 * h, h, t, bounce=False, shade=False)
    dust(cnv, t + 30, seed=777, n=18, big=7)
    caption(cnv, lang, L["pred1"], t, 0.16, dur + 1)


# ---------------------------------------------------------------- 6. 학살
def sc_slaughter(cnv, t, dur, lang):
    """와이드 / 총구 클로즈업 / 놓친 와이드 세 컷 + 개체 수 카운터가 431 -> 0."""
    L = LANG[lang]
    if t < CUT_B:
        camx = 300 + t * 22
        stage(cnv, camx, "dust")
        _kills(cnv, t, "a")
        r = robot(cnv, W * 0.5 + math.sin(t * 1.6) * 150, GROUND_Y + 10, 316, t)
        _tracers(cnv, r, t)
        smoke(cnv, t * 1.7, n=6, seed=311, color=(206, 168, 124), peak=80)
    elif t < CUT_C:
        tb = t - CUT_B
        camx = 980 + tb * 30
        stage(cnv, camx, "dust", sun=False)
        _kills(cnv, t, "b", wide=False)
        r = robot(cnv, W * 0.34 + math.sin(tb * 2.1) * 42, H + 250, 690, t, shade=False)
        _tracers(cnv, r, t, side_bias=1, n=4, ln=(420, 780), w=(11, 4))
        smoke(cnv, t * 2.2, n=7, seed=77, color=(214, 176, 130), peak=104)
    else:
        tc = t - CUT_C
        camx = 520 + tc * 26
        stage(cnv, camx, "dust")
        _kills(cnv, t, "c")
        r = robot(cnv, W * 0.5 + math.sin(tc * 1.25 + 0.4) * 470, GROUND_Y + 10, 316, t)
        _tracers(cnv, r, t)
        smoke(cnv, t * 1.7, n=6, seed=311, color=(206, 168, 124), peak=80)
    dust(cnv, t + 19, seed=909, n=30, big=7)
    foreground(cnv, 300 + t * 40, "dust")
    if t > 0.8:
        lens_dirt(cnv, clamp((t - 0.8) / 0.3) * 0.85)
    v = pop_now(t)
    pop_overlay(cnv, lang, v, 1.0, est=(v == 0))
    caption(cnv, lang, L["sl1"], t, 0.26, 1.95)
    caption(cnv, lang, L["sl2"], t, 2.40, 3.85)
    caption(cnv, lang, L["sl3"], t, 4.35, dur + 1)


# ---------------------------------------------------------------- 7. 촬영진 차례
FLASH_AT = 1.62


def sc_attack(cnv, t, dur, lang):
    """★ 4벽 붕괴 - 로봇이 카메라로 다가오고, 자막이 문장 중간에서 뚝 끊긴다."""
    L = LANG[lang]
    camx = 700 + t * 12
    stage(cnv, camx, "dust")
    p = clamp(t / FLASH_AT)
    # h를 40px 단위로 양자화한다(프레임마다 다른 크기를 캐시하면 메모리가 터진다)
    hq = int((330 + 540 * p * p) / 40) * 40
    r = robot(cnv, W * 0.5 + math.sin(t * 2.3) * 26, GROUND_Y + 12 + 300 * p * p, hq, t,
              shade=(p < 0.45))
    _tracers(cnv, r, t, n=3, ln=(140, 320), w=(9, 4))
    smoke(cnv, t * 2.0, n=6, seed=311, color=(206, 168, 124), peak=90)
    if t >= FLASH_AT:                       # 총구 섬광이 화면을 덮는다
        q = clamp((t - FLASH_AT) / 0.26)
        mf = SEQ("MuzzleFlash")
        blit(cnv, SPR(mf[int(t * 24) % len(mf)], h=int(1250 + 500 * q)),
             W * 0.5, H * 0.44, anchor="cc")
        flash(cnv, 1.0 - 0.55 * q)
    dust(cnv, t + 45, seed=333, n=26, big=6)
    foreground(cnv, camx, "dust")
    lens_dirt(cnv, 0.85)
    # ★ 페이드아웃 없이 끊는다 - 문장이 끝나지 않은 채로 녹화가 중단된 것처럼
    caption(cnv, lang, L["atk1"], t, 0.14, FLASH_AT, fout=0.02)


# ---------------------------------------------------------------- 8. 넘어진 카메라
def sc_fallen(cnv, t, dur, lang):
    """★ 콜백 펀치라인 - 화면이 기울어진 채로 "최종화입니다".

    카메라가 넘어졌으니 자막도 같이 기울어져야 한다(자막까지 tmp에 그린 뒤 회전한다).
    확대한 뒤 회전해서 잘라내므로 검은 여백이 생기지 않는다.
    """
    L = LANG[lang]
    tmp = Image.new("RGB", (W, H), (0, 0, 0))
    camx = 1500 + t * 4
    stage(tmp, camx, "ash", sun=False)
    smoke(tmp, t, n=6, peak=52)
    x = W * 0.56 - t * 84
    if -180 < x < W + 180:
        robot(tmp, x, GROUND_Y + 4, 148, t)      # 배경이 1.78배로 확대되므로 작게 그린다
    dust(tmp, t + 40, seed=1414, n=18, big=4)
    foreground(tmp, camx, "ash")
    lens_dirt(tmp, 0.55)
    # ★ 회전 후 잘라내려면 배율이 충분해야 한다 - 1280x720을 20도 돌리면 세로로 1124px가
    #   필요하다(1.5배로는 모자라 아래 모서리에 검은 삼각형이 남았다). 1.78배 + 오프셋 50.
    ang = -19.0 - 1.6 * math.sin(t * 1.1)
    big = tmp.resize((int(W * 1.78), int(H * 1.78)), BILINEAR).rotate(ang, resample=BILINEAR)
    l, tp = (big.width - W) // 2, (big.height - H) // 2 + 50
    cnv.paste(big.crop((l, tp, l + W, tp + H)), (0, 0))
    # ★ 자막은 배경과 함께 확대하면 안 된다(1.5배로 커져 화면 밖으로 잘린다).
    #   원래 크기로 그린 뒤 같은 각도만 회전해 얹는다 - 카메라가 넘어졌으니 자막도 기울어야 한다.
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    caption(ov, lang, L["fal1"], t, 0.55, dur + 1, size=27, y=H - 118)
    ov = ov.rotate(ang, resample=BILINEAR)
    cnv.paste(ov, (0, 0), ov)
    # ★ 두 번째 펀치라인 - 데이터 표기가 "촬영진 0 (추정)"으로 갱신된다(장치의 회수).
    #   자막은 카메라와 함께 기울지만 이 표기는 후반 작업에서 얹는 방송 그래픽이라
    #   수평을 유지한다 - 기울이면 읽기도 어려웠다.
    pop_overlay(cnv, lang, 0, fade(t, 1.70, dur + 1, 0.28, 0.4), est=True, label=L["crew"])


# ---------------------------------------------------------------- 9. 엔딩 카드
def sc_outro(cnv, t, dur, lang):
    """★ 영상 전체에서 유일한 광고 문구가 여기 한 줄 나온다."""
    L = LANG[lang]
    cnv.paste(Image.new("RGB", (W, H), (7, 7, 9)), (0, 0))
    a1 = fade(t, 0.22, dur + 1, 0.7, 0.4)
    if a1 > 0.01:
        blit(cnv, SPR("UI/title_logo.png", w=430), W / 2, 288, anchor="cc", alpha=a1)
    tracked(cnv, W / 2, 424, lang, L["tag"], "cap", 36 if lang == "en" else 33,
            tracking=3.0, alpha=fade(t, 1.30, dur + 1, 0.7, 0.4))
    tracked(cnv, W / 2, 488, "en", L["url"], "small", 18, tracking=2.2,
            alpha=fade(t, 2.45, dur + 1, 0.65, 0.4) * 0.82, color=(190, 185, 174))


SCENES = {
    "coldopen": sc_coldopen,
    "title": sc_title,
    "herd": sc_herd,
    "arrival": sc_arrival,
    "predator": sc_predator,
    "slaughter": sc_slaughter,
    "attack": sc_attack,
    "fallen": sc_fallen,
    "outro": sc_outro,
}
