# -*- coding: utf-8 -*-
"""컴스톡 광고 영상 공용 그리기 도구 - 41초 인포머셜의 "웃음 엔진"을 도구로 만든 것.

★ 레퍼런스(`Comstock_by_pyramidstudio_ko.mp4` = 41초 흑백 인포머셜)를 프레임 단위로 뜯어
  얻은 결론이 이 모듈의 설계 근거다.
    1. **텍스트를 3층으로 동시에 쌓는다** - 큰 헤드라인 + 하단 자막 + 맨 아래 깨알 고지.
       화면에 항상 개그가 2~3개 겹쳐 있어야 밀도가 산다(`headline`/`caption`/`fine`).
    2. **누구나 아는 형식을 패러디하고 그 형식을 배신한다** - "1단계/2단계/3단계는 없습니다".
       그래서 번호·도장·표 같은 "양식" 부품을 도구로 뽑았다(`numbered`/`stamp`/`row`).
    3. **짧고 단정한 문장 + 뒤통수** - 문장이 길면 웃음이 죽는다. 자동 축소로 한 줄 유지.

두 편(가로 30초 안전교육 / 세로 15초 고객센터)이 이 도구를 공유하고, 장면·문구·배색은
각 스펙 모듈(ad_safety.py / ad_helpdesk.py)이 따로 가진다. 다큐판(wild_*)과는 무관하다.
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

from pv_draw import (SPR, SEQ, blit, clamp, ease_out, twos, skyline_img, ground_img,
                     LANCZOS, BILINEAR)
from pv_scenes import zombie_frame, ROBOT

FONTDIR = r"C:\Windows\Fonts"
FONTS = {
    ("ko", "head"): "malgunbd.ttf",     # 굵은 고딕 - 헤드라인/항목 제목
    ("ko", "body"): "malgun.ttf",
    ("ko", "fine"): "malgun.ttf",
    ("en", "head"): "ariblk.ttf",       # Arial Black
    ("en", "body"): "arial.ttf",
    ("en", "fine"): "arial.ttf",
    ("ko", "num"): "consolab.ttf",      # 숫자/카운터
    ("en", "num"): "consolab.ttf",
}
FALLBACK = {"head": "arialbd.ttf", "body": "arial.ttf", "fine": "arial.ttf",
            "num": "courbd.ttf"}
_fnt = {}
_c = {}


def F(lang, kind, size):
    key = (lang, kind, size)
    f = _fnt.get(key)
    if f is None:
        name = FONTS.get((lang, kind), FALLBACK[kind])
        p = os.path.join(FONTDIR, name)
        if not os.path.exists(p):
            p = os.path.join(FONTDIR, FALLBACK[kind])
        f = ImageFont.truetype(p, size)
        _fnt[key] = f
    return f


_measure = None


def tlen(s, f):
    global _measure
    if _measure is None:
        _measure = ImageDraw.Draw(Image.new("L", (4, 4)))
    return _measure.textlength(s, font=f)


def fit(lang, kind, size, s, maxw):
    """한 줄을 유지하도록 글자 크기를 줄여서 폰트를 돌려준다(번역문이 길어도 안 넘친다)."""
    f = F(lang, kind, size)
    while tlen(s, f) > maxw and size > 11:
        size -= 1
        f = F(lang, kind, size)
    return f


def fade(t, t0, t1, fin=0.22, fout=0.20):
    if t < t0 or t > t1:
        return 0.0
    return min(clamp((t - t0) / fin), clamp((t1 - t) / fout))


def pop(t, t0, dur=0.18):
    """도장·배지가 쾅 찍히는 느낌 - 크게 들어와 제 크기로 줄어든다."""
    if t < t0:
        return 0.0
    p = clamp((t - t0) / dur)
    return 1.0 + 0.9 * (1 - ease_out(p))


# ---------------------------------------------------------------- 판/띠
def cache(key, build):
    im = _c.get(key)
    if im is None:
        im = build()
        if len(_c) > 260:
            for k in list(_c)[:80]:
                _c.pop(k, None)
        _c[key] = im
    return im


def warn_tape(w, h, c1=(245, 197, 24), c2=(24, 24, 26), band=34):
    """산업 안전 표지의 노랑/검정 대각선 띠."""
    def build():
        im = Image.new("RGB", (w, h), c1)
        d = ImageDraw.Draw(im)
        step = band * 2
        for x in range(-h, w + h, step):
            d.polygon([(x, h), (x + band, h), (x + band + h, 0), (x + h, 0)], fill=c2)
        return im
    return cache(("@tape", w, h, c1, c2, band), build)


def plate(cnv, box, fill, outline=None, width=3, radius=0):
    d = ImageDraw.Draw(cnv)
    if radius > 0:
        d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)
    else:
        d.rectangle(box, fill=fill, outline=outline, width=width)


def shadow_plate(cnv, box, fill, outline=None, width=3, radius=0, off=7,
                 shade=(24, 24, 26)):
    """살짝 어긋난 그림자를 깐 판 - 인쇄물 같은 두께감이 생긴다."""
    x0, y0, x1, y1 = box
    plate(cnv, (x0 + off, y0 + off, x1 + off, y1 + off), shade, radius=radius)
    plate(cnv, box, fill, outline, width, radius)


def headline(cnv, lang, s, cy, size, fg=(24, 24, 26), bg=(245, 197, 24), pad=(26, 14),
             alpha=1.0, cx=None, maxw=None, radius=0, outline=(24, 24, 26)):
    """큰 헤드라인 - 색 판 위에 굵은 글자. 광고 자막의 기본 부품."""
    if alpha <= 0.01:
        return None
    W, H = cnv.size
    cx = W / 2 if cx is None else cx
    maxw = maxw or (W - 150)
    f = fit(lang, "head", size, s, maxw)
    tw = tlen(s, f)
    th = size
    box = (cx - tw / 2 - pad[0], cy - th / 2 - pad[1], cx + tw / 2 + pad[0],
           cy + th / 2 + pad[1])
    lay = Image.new("RGBA", cnv.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    if radius > 0:
        d.rounded_rectangle(box, radius=radius, fill=bg + (255,),
                            outline=(outline + (255,)) if outline else None, width=3)
    else:
        d.rectangle(box, fill=bg + (255,), outline=(outline + (255,)) if outline else None,
                    width=3)
    d.text((cx, cy), s, font=f, fill=fg + (255,), anchor="mm")
    put(cnv, lay, alpha)
    return box


def caption(cnv, lang, s, cy, size, fg=(250, 250, 250), bg=(24, 24, 26), alpha=1.0,
            cx=None, maxw=None):
    """하단 자막 - 헤드라인보다 작고, 부연이나 결과를 담는다."""
    return headline(cnv, lang, s, cy, size, fg=fg, bg=bg, pad=(20, 11), alpha=alpha,
                    cx=cx, maxw=maxw, outline=None)


def fine(cnv, lang, s, cy, size=16, color=(60, 58, 56), alpha=1.0, cx=None):
    """맨 아래 깨알 고지 - 레퍼런스에서 가장 잘 먹던 장치다(*좀비는 별매입니다)."""
    if alpha <= 0.01:
        return
    W, H = cnv.size
    cx = W / 2 if cx is None else cx
    f = fit(lang, "fine", size, s, W - 90)
    lay = Image.new("RGBA", cnv.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).text((cx, cy), s, font=f, fill=color + (255,), anchor="mm")
    put(cnv, lay, alpha)


def stamp(cnv, lang, s, cx, cy, size, angle=-13, color=(214, 40, 40), alpha=1.0,
          scale=1.0):
    """비스듬히 쾅 찍히는 도장(실패 / 무료 / 정상)."""
    if alpha <= 0.01:
        return
    f = F(lang, "head", size)
    tw = tlen(s, f)
    pad = size * 0.42
    w = int(tw + pad * 2)
    h = int(size + pad * 1.5)
    lay = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rectangle([2, 2, w - 3, h - 3], outline=color + (255,), width=max(4, size // 7))
    d.text((w / 2, h / 2), s, font=f, fill=color + (255,), anchor="mm")
    lay = lay.rotate(angle, resample=BILINEAR, expand=True)
    if scale != 1.0:
        lay = lay.resize((max(1, int(lay.width * scale)), max(1, int(lay.height * scale))),
                         BILINEAR)
    blit(cnv, lay, cx, cy, anchor="cc", alpha=alpha)


def numbered(cnv, lang, n, cx, cy, r, fill=(24, 24, 26), fg=(245, 197, 24), alpha=1.0):
    """항목 번호 동그라미 - "양식"을 만들면 그 양식을 배신할 수 있다."""
    if alpha <= 0.01:
        return
    lay = Image.new("RGBA", cnv.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill + (255,))
    d.text((cx, cy + 1), str(n), font=F(lang, "head", int(r * 1.25)), fill=fg + (255,),
           anchor="mm")
    put(cnv, lay, alpha)


def bubble(cnv, lang, s, box, size, fg=(28, 30, 36), bg=(255, 255, 255), tail="l",
           alpha=1.0, radius=22, outline=(210, 214, 222)):
    """말풍선 - 고객센터 편의 민원/답변 카드."""
    if alpha <= 0.01:
        return
    x0, y0, x1, y1 = box
    lay = Image.new("RGBA", cnv.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rounded_rectangle(box, radius=radius, fill=bg + (255,), outline=outline + (255,),
                        width=2)
    ty = y0 + (y1 - y0) * 0.5
    if tail == "l":
        d.polygon([(x0 - 16, ty - 10), (x0 + 2, ty - 24), (x0 + 2, ty + 8)], fill=bg + (255,))
    elif tail == "r":
        d.polygon([(x1 + 16, ty - 10), (x1 - 2, ty - 24), (x1 - 2, ty + 8)], fill=bg + (255,))
    f = fit(lang, "body", size, s, (x1 - x0) - 44)
    d.text(((x0 + x1) / 2, (y0 + y1) / 2), s, font=f, fill=fg + (255,), anchor="mm")
    put(cnv, lay, alpha)


def put(cnv, layer, alpha=1.0, dy=0):
    if alpha <= 0.01:
        return
    if alpha < 0.995:
        a = layer.getchannel("A").point(lambda v: int(v * alpha))
        layer = Image.merge("RGBA", (*layer.split()[:3], a))
    if cnv.mode == "RGBA":
        cnv.alpha_composite(layer, (0, max(0, int(dy))))
    else:
        cnv.paste(layer, (0, int(dy)), layer)


# ---------------------------------------------------------------- 자료 화면(게임 장면)
def _mini_bg(w, h, key, sky=((146, 180, 208), (236, 212, 168)),
             sil=(92, 88, 84), gnd=0.96):
    """자료 화면용 배경(하늘 + 스카이라인 + 지면)을 크기별로 캐시한다."""
    def build():
        im = Image.new("RGB", (w, h))
        d = ImageDraw.Draw(im)
        hz = int(h * 0.62)
        for y in range(h):
            fq = clamp(y / float(hz + 10))
            d.line([(0, y), (w, y)],
                   fill=tuple(int(sky[0][i] + (sky[1][i] - sky[0][i]) * fq) for i in range(3)))
        sl = skyline_img()
        sh = max(24, int(h * 0.34))
        sw = max(2, int(sl.width * sh / sl.height))
        s2 = sl.resize((sw, sh), LANCZOS)
        from PIL import ImageOps
        col = ImageOps.colorize(s2.convert("L"), black=sil,
                                white=tuple(min(255, c + 74) for c in sil)).convert("RGB")
        s2 = Image.merge("RGBA", (*col.split(), s2.getchannel("A")))
        x = 0
        while x < w:
            im.paste(s2, (x, hz - sh + int(h * 0.06)), s2)
            x += sw
        g = ground_img()
        gh = h - hz
        band = g.resize((w, max(2, gh)), BILINEAR)
        if gnd != 1.0:
            band = Image.blend(Image.new("RGB", band.size, (40, 34, 28)), band, gnd)
        im.paste(band, (0, hz))
        return im
    return cache(("@mbg", w, h, key, sky, sil, round(gnd, 2)), build)


def footage(w, h, t, kind, seed=7, camx=0.0, sky=None, sil=(122, 116, 108)):
    """교육 자료/모니터 안에 들어가는 게임 장면 조각.

    kind: approach(다가온다) / wipe(한 번에 폭발) / cover(엄폐물이 사라진다) /
          spray(난사 중) / empty(아무도 없다)
    """
    kw = {"sky": sky} if sky else {}
    im = _mini_bg(w, h, kind + str(seed), sil=sil, **kw).copy()
    gy = int(h * 0.90)
    rng = random.Random(seed)
    ex = SEQ("Explosion")
    mf = SEQ("MuzzleFlash")
    kinds = ("Zombie", "Sprinter", "Charger", "Zombie", "Spitter", "Leader")

    if kind == "empty":
        return im

    bh = int(h * 0.30)
    rh = int(h * 0.40)

    if kind == "approach":
        # 멀어지라고 배웠는데 전원 다가온다
        for i in range(7):
            x0 = w * (0.06 + 0.14 * i) + rng.uniform(-10, 10)
            x = x0 + t * (16 + i * 5)
            zx = min(x, w * 0.60)
            blit(im, SPR(zombie_frame(t, i, kinds[i % 6]), h=int(bh * rng.uniform(0.86, 1.1))),
                 zx, gy, anchor="cb")
        blit(im, SPR(ROBOT, h=rh), w * 0.80, gy, anchor="cb")
        if t > 0.9:
            blit(im, SPR(mf[int(t * 14) % len(mf)], h=int(rh * 0.40)), w * 0.66,
                 gy - rh * 0.52, anchor="cc")
    elif kind == "wipe":
        # 무리와 함께 행동한 결과
        boom_at = 1.15
        for i in range(8):
            x = w * (0.08 + 0.11 * i)
            if t < boom_at:
                blit(im, SPR(zombie_frame(t, i, kinds[i % 6]), h=int(bh * rng.uniform(0.86, 1.1))),
                     x, gy, anchor="cb")
            elif t < boom_at + 0.7:
                p = (t - boom_at) / 0.7
                blit(im, SPR(ex[min(len(ex) - 1, int(p * len(ex)))], h=int(bh * 1.9)),
                     x, gy - bh * 0.45, anchor="cc")
            else:
                # 폭발이 끝난 뒤에도 연기가 남아야 화면이 텅 비어 보이지 않는다
                p = clamp((t - boom_at - 0.7) / 2.2)
                lay = Image.new("RGBA", im.size, (0, 0, 0, 0))
                dd = ImageDraw.Draw(lay)
                r = bh * (0.34 + 0.5 * p)
                yy = gy - bh * (0.30 + 0.5 * p)
                dd.ellipse([x - r, yy - r * 0.6, x + r, yy + r * 0.6],
                           fill=(206, 200, 192, int(150 * (1 - p))))
                im.paste(lay.filter(ImageFilter.GaussianBlur(9)), (0, 0),
                         lay.filter(ImageFilter.GaussianBlur(9)))
        blit(im, SPR(ROBOT, h=rh), w * 0.86, gy, anchor="cb")
        if boom_at - 0.25 < t < boom_at + 0.25:
            im.paste(Image.blend(im.copy(), Image.new("RGB", im.size, (255, 252, 240)), 0.55),
                     (0, 0))
    elif kind == "cover":
        # 엄폐물을 활용한 결과 - 엄폐물이 먼저 사라진다
        d = ImageDraw.Draw(im)
        cw, ch = int(w * 0.17), int(h * 0.34)
        cx0 = int(w * 0.30)
        gone = t > 1.35
        if not gone:
            d.rectangle([cx0, gy - ch, cx0 + cw, gy], fill=(96, 92, 88),
                        outline=(30, 30, 32), width=4)
            for yy in range(gy - ch + 12, gy - 14, 22):
                for xx in range(cx0 + 10, cx0 + cw - 14, 20):
                    d.rectangle([xx, yy, xx + 9, yy + 11], fill=(40, 40, 44))
        blit(im, SPR(zombie_frame(t, 2, "Zombie"), h=int(bh)), cx0 + cw * 0.5,
             gy, anchor="cb")
        if 1.35 < t < 2.1:
            p = (t - 1.35) / 0.75
            blit(im, SPR(ex[min(len(ex) - 1, int(p * len(ex)))], h=int(ch * 2.0)),
                 cx0 + cw * 0.5, gy - ch * 0.5, anchor="cc")
        blit(im, SPR(ROBOT, h=rh), w * 0.84, gy, anchor="cb")
        if t > 0.5:
            blit(im, SPR(mf[int(t * 16) % len(mf)], h=int(rh * 0.42)), w * 0.70,
                 gy - rh * 0.52, anchor="cc")
    elif kind == "spray":
        # 난사 중 - 고객센터 편 상단 모니터용
        blit(im, SPR(ROBOT, h=rh), w * 0.5, gy, anchor="cb")
        d = ImageDraw.Draw(im)
        for k in range(5):
            side = 1 if k % 2 == 0 else -1
            gx = w * 0.5 + side * rh * 0.34
            gyy = gy - rh * 0.52 + rng.uniform(-10, 10)
            ln = rng.uniform(w * 0.16, w * 0.42)
            d.line([(gx, gyy), (gx + side * ln, gyy + rng.uniform(-16, 16))],
                   fill=(232, 152, 60), width=6)
            d.line([(gx, gyy), (gx + side * ln, gyy + rng.uniform(-16, 16))],
                   fill=(255, 244, 206), width=2)
            blit(im, SPR(mf[rng.randrange(len(mf))], h=int(rh * 0.36)), gx, gyy,
                 anchor="cc")
        for i in range(4):
            x = w * (0.08 + 0.26 * i) + math.sin(t * 1.4 + i) * 8
            if abs(x - w * 0.5) < w * 0.12:
                continue
            blit(im, SPR(zombie_frame(t, i, kinds[i % 6]), h=int(bh * 0.9)), x, gy,
                 anchor="cb")
        for i in range(2):
            p = (t * 1.3 + i * 0.5) % 1.0
            blit(im, SPR(ex[min(len(ex) - 1, int(p * len(ex)))], h=int(bh * 1.5)),
                 w * (0.18 + 0.62 * i), gy - bh * 0.4, anchor="cc")
    return im


def screen(cnv, box, img, border=(255, 255, 255), width=6, shade=(24, 24, 26), off=8,
           radius=0):
    """자료 화면을 흰 테두리 + 그림자와 함께 얹는다(슬라이드 안의 영상처럼)."""
    x0, y0, x1, y1 = [int(v) for v in box]
    d = ImageDraw.Draw(cnv)
    d.rectangle([x0 + off, y0 + off, x1 + off, y1 + off], fill=shade)
    cnv.paste(img.resize((x1 - x0, y1 - y0), BILINEAR), (x0, y0))
    d.rectangle([x0, y0, x1, y1], outline=border, width=width)
