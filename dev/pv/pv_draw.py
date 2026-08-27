# -*- coding: utf-8 -*-
"""컴스톡 PV - 그리기 헬퍼(에셋 캐시, 무대 배경, 텍스트, 만화 효과)."""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

from pv_common import RES, CACHE, W, H, font_path

LANCZOS = Image.Resampling.LANCZOS
BILINEAR = Image.Resampling.BILINEAR

_raw = {}
_spr = {}
_fnt = {}


# ---------------------------------------------------------------- 에셋
def A(rel):
    """원본 스프라이트(RGBA)를 캐시해서 돌려준다."""
    im = _raw.get(rel)
    if im is None:
        im = Image.open(os.path.join(RES, rel.replace("/", os.sep))).convert("RGBA")
        _raw[rel] = im
    return im


ASSETS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "assets")


def ASSET(name, h=None, w=None):
    """dev/pv/assets/ 안의 마케팅 전용 이미지(게임 리소스가 아니다)를 불러온다.

    스튜디오 로고처럼 게임 에셋이 아닌 PV 전용 그림은 Assets/Resources를 오염시키지
    않도록 여기 따로 둔다.
    """
    key = ("@asset", name, h, w)
    im = _spr.get(key)
    if im is not None:
        return im
    src = Image.open(os.path.join(ASSETS_DIR, name)).convert("RGBA")
    if h:
        nw = max(1, int(round(src.width * h / src.height)))
        src = src.resize((nw, max(1, int(h))), LANCZOS)
    elif w:
        nh = max(1, int(round(src.height * w / src.width)))
        src = src.resize((max(1, int(w)), nh), LANCZOS)
    _spr[key] = src
    return src


def SEQ(folder):
    """폴더 안의 png를 이름순으로 (상대경로 리스트)로 돌려준다."""
    key = "@seq:" + folder
    if key not in _raw:
        d = os.path.join(RES, folder.replace("/", os.sep))
        names = sorted(f for f in os.listdir(d) if f.lower().endswith(".png"))
        _raw[key] = [folder + "/" + n for n in names]
    return _raw[key]


def SPR(rel, h=None, w=None, flip=False, rot=0.0):
    """높이(또는 폭) 기준으로 크기를 맞추고 좌우반전/회전까지 적용한 스프라이트."""
    rot = round(rot, 1)
    key = (rel, h, w, flip, rot)
    im = _spr.get(key)
    if im is not None:
        return im
    im = A(rel)
    if h:
        nw = max(1, int(round(im.width * h / im.height)))
        im = im.resize((nw, max(1, int(h))), LANCZOS)
    elif w:
        nh = max(1, int(round(im.height * w / im.width)))
        im = im.resize((max(1, int(w)), nh), LANCZOS)
    if flip:
        im = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if rot:
        im = im.rotate(rot, resample=BILINEAR, expand=True)
    if len(_spr) > 4000:
        _spr.clear()
    _spr[key] = im
    return im


def FNT(lang, kind, size):
    key = (lang, kind, size)
    f = _fnt.get(key)
    if f is None:
        p, idx = font_path(lang, kind)
        f = ImageFont.truetype(p, size, index=idx)
        _fnt[key] = f
    return f


# ---------------------------------------------------------------- 배치
def blit(dst, src, x, y, anchor="cb", alpha=1.0):
    """anchor: c=가운데, b=아래, t=위 (예: 'cb' = 가로 가운데 / 세로 아래)."""
    if alpha <= 0:
        return
    ax, ay = anchor[0], anchor[1]
    px = int(x - src.width / 2) if ax == "c" else (int(x) if ax == "l" else int(x - src.width))
    py = int(y - src.height) if ay == "b" else (int(y) if ay == "t" else int(y - src.height / 2))
    if alpha < 1.0:
        src = src.copy()
        a = src.getchannel("A").point(lambda v: int(v * alpha))
        src.putalpha(a)
    dst.alpha_composite(src, (px, py)) if dst.mode == "RGBA" else dst.paste(src, (px, py), src)


def ease_out(p):
    return 1 - (1 - p) ** 3


def ease_in(p):
    return p ** 3


def clamp(v, a=0.0, b=1.0):
    return a if v < a else (b if v > b else v)


def twos(t, fps=12.0):
    """옛날 카툰처럼 초당 12장으로 동작을 계단화한다."""
    return math.floor(t * fps) / fps


# ---------------------------------------------------------------- 무대 배경
def _cache_path(name):
    os.makedirs(CACHE, exist_ok=True)
    return os.path.join(CACHE, name)


def sky_img():
    if "@sky" not in _raw:
        im = Image.new("RGB", (W, H), (225, 225, 225))
        d = ImageDraw.Draw(im)
        for y in range(H):
            v = int(238 - 66 * (y / H))
            d.line([(0, y), (W, y)], fill=(v, v, v))
        _raw["@sky"] = im
    return _raw["@sky"]


def cloud_img(idx):
    key = "@cloud%d" % idx
    if key not in _raw:
        rng = random.Random(700 + idx)
        cw, ch = 300, 130
        im = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        puffs = []
        for i in range(5):
            r = rng.randint(34, 52)
            cx = 46 + i * 52 + rng.randint(-8, 8)
            cy = 78 - rng.randint(0, 26)
            puffs.append((cx, cy, r))
        for (cx, cy, r) in puffs:
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 255, 255, 255),
                      outline=(20, 20, 20, 255), width=7)
        for (cx, cy, r) in puffs:  # 안쪽 윤곽선을 지워 한 덩어리로 만든다
            d.ellipse([cx - r + 6, cy - r + 6, cx + r - 6, cy + r - 6], fill=(255, 255, 255, 255))
        d.rectangle([0, 84, cw, ch], fill=(0, 0, 0, 0))
        d.line([(20, 84), (cw - 24, 84)], fill=(20, 20, 20, 255), width=7)
        _raw[key] = im
    return _raw[key]


def skyline_img():
    """폐허가 된 도시 실루엣 띠(가로로 이어붙일 수 있게 만든다)."""
    if "@skyline" not in _raw:
        sw, sh = 1920, 260
        im = Image.new("RGBA", (sw, sh), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        rng = random.Random(31)
        x = 0
        while x < sw:
            bw = rng.randint(60, 150)
            bh = rng.randint(70, 210)
            top = sh - bh
            d.rectangle([x, top, x + bw, sh], fill=(96, 96, 96, 255),
                        outline=(18, 18, 18, 255), width=6)
            # 무너진 윗면
            for k in range(rng.randint(1, 3)):
                nw = rng.randint(14, bw // 2)
                nx = x + rng.randint(0, max(1, bw - nw))
                d.rectangle([nx, top - 2, nx + nw, top + rng.randint(10, 34)],
                            fill=(0, 0, 0, 0))
            # 창문
            for wy in range(top + 20, sh - 14, 26):
                for wx in range(x + 14, x + bw - 16, 24):
                    if rng.random() < 0.55:
                        d.rectangle([wx, wy, wx + 11, wy + 13], fill=(28, 28, 28, 255))
            x += bw + rng.randint(4, 26)
        _raw["@skyline"] = im
    return _raw["@skyline"]


def ground_img():
    """게임에서 쓰는 폐허 지면 텍스처를 PV 크기로 줄여 캐시한다."""
    if "@ground" not in _raw:
        p = _cache_path("ground_1440.png")
        if os.path.exists(p):
            im = Image.open(p).convert("RGB")
        else:
            src = Image.open(os.path.join(RES, "ground_ruined_city_v2_tile.png")).convert("RGB")
            im = src.resize((1440, 810), LANCZOS)
            im.save(p)
        _raw["@ground"] = im
    return _raw["@ground"]


def draw_stage(cnv, camx=0.0, horizon=470, ground_dark=1.0):
    """하늘 + 구름 + 폐허 스카이라인 + 지면을 그린다. camx는 픽셀 단위 카메라 위치."""
    cnv.paste(sky_img(), (0, 0))
    for i in range(3):
        c = cloud_img(i)
        cx = int((-camx * 0.12 + i * 430 + 120) % (W + 420)) - 210
        blit(cnv, c, cx, 60 + i * 46, anchor="lt")
    sl = skyline_img()
    off = int(camx * 0.35) % sl.width
    strip = Image.new("RGBA", (W + sl.width, sl.height), (0, 0, 0, 0))
    strip.alpha_composite(sl, (0, 0))
    strip.alpha_composite(sl, (sl.width, 0))
    cnv.paste(strip.crop((off, 0, off + W, sl.height)), (0, horizon - sl.height + 34),
              strip.crop((off, 0, off + W, sl.height)))

    g = ground_img()
    gh = H - horizon
    band = g.crop((0, 0, g.width, min(g.height, gh)))
    off = int(camx) % band.width
    tile = Image.new("RGB", (band.width * 2, band.height))
    tile.paste(band, (0, 0))
    tile.paste(band, (band.width, 0))
    ground = tile.crop((off, 0, off + W, band.height)).resize((W, gh), BILINEAR)
    if ground_dark != 1.0:
        ground = ground.point(lambda v: int(v * ground_dark))
    cnv.paste(ground, (0, horizon))
    d = ImageDraw.Draw(cnv)
    d.line([(0, horizon), (W, horizon)], fill=(18, 18, 18), width=7)


# ---------------------------------------------------------------- 만화 장식
def impact_star(size, points=10, color=(255, 255, 255), outline=(15, 15, 15)):
    key = ("@star", size, points, color)
    if key in _raw:
        return _raw[key]
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = size / 2
    pts = []
    for i in range(points * 2):
        a = math.pi * i / points - math.pi / 2
        r = c * (0.98 if i % 2 == 0 else 0.46)
        pts.append((c + r * math.cos(a), c + r * math.sin(a)))
    d.polygon(pts, fill=color + (255,), outline=outline + (255,))
    for i in range(3):
        d.line(pts + [pts[0]], fill=outline + (255,), width=5)
    _raw[key] = im
    return im


def speed_lines(cnv, cx, cy, n, r0, r1, rng, width=4, color=(20, 20, 20)):
    d = ImageDraw.Draw(cnv)
    for i in range(n):
        a = rng.random() * math.tau
        d.line([(cx + r0 * math.cos(a), cy + r0 * math.sin(a)),
                (cx + r1 * math.cos(a), cy + r1 * math.sin(a))], fill=color, width=width)


def draw_note(d, x, y, s=1.0, color=(20, 20, 20)):
    """음표 하나(옛날 카툰의 휘파람)."""
    r = 9 * s
    d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8], fill=color)
    d.line([(x + r - 1, y), (x + r - 1, y - 30 * s)], fill=color, width=int(4 * s) or 1)
    d.line([(x + r - 1, y - 30 * s), (x + r + 13 * s, y - 20 * s)], fill=color, width=int(5 * s) or 1)


def text(cnv, xy, s, font, fill=(255, 255, 255), anchor="mm", stroke=0,
         stroke_fill=(15, 15, 15)):
    d = ImageDraw.Draw(cnv)
    d.text(xy, s, font=font, fill=fill, anchor=anchor, stroke_width=stroke,
           stroke_fill=stroke_fill)


def text_layer(size, xy, s, font, fill=(255, 255, 255), anchor="mm", stroke=0,
               stroke_fill=(15, 15, 15)):
    im = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.text(xy, s, font=font, fill=fill + (255,), anchor=anchor, stroke_width=stroke,
           stroke_fill=stroke_fill + (255,))
    return im


def wobble(layer, t, amp=1.6, ang=0.5, seed=0):
    """옛날 필름처럼 글자가 미세하게 흔들리게 한다(초당 12장 단위)."""
    k = int(twos(t, 12) * 12) + seed
    rng = random.Random(k * 977 + seed)
    a = (rng.random() * 2 - 1) * ang
    im = layer.rotate(a, resample=BILINEAR, center=(layer.width / 2, layer.height / 2))
    dx = int((rng.random() * 2 - 1) * amp)
    dy = int((rng.random() * 2 - 1) * amp)
    out = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    out.alpha_composite(im, (dx, dy))
    return out


def caption_plate(cnv, lang, s, y=H - 74, size=30, pad=18):
    """무성영화식 하단 자막 띠."""
    f = FNT(lang, "serif", size)
    d = ImageDraw.Draw(cnv)
    bb = d.textbbox((0, 0), s, font=f)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    x0, x1 = W / 2 - tw / 2 - pad * 2, W / 2 + tw / 2 + pad * 2
    d.rectangle([x0, y - th / 2 - pad, x1, y + th / 2 + pad], fill=(12, 12, 12))
    d.rectangle([x0 + 6, y - th / 2 - pad + 6, x1 - 6, y + th / 2 + pad - 6],
                outline=(235, 235, 235), width=3)
    d.text((W / 2, y), s, font=f, fill=(240, 240, 240), anchor="mm")


# ---------------------------------------------------------------- 광고 장식
def badge(size, s, font, angle=-12, points=14):
    """홈쇼핑 광고의 뾰족뾰족한 별 배지(NEW! / 무료!)."""
    key = ("@badge", size, s, id(font), angle, points)
    if key in _raw:
        return _raw[key]
    pad = int(size * 0.25)
    im = Image.new("RGBA", (size + pad * 2, size + pad * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c = (size + pad * 2) / 2
    pts = []
    for i in range(points * 2):
        a = math.pi * i / points - math.pi / 2
        r = (size / 2) * (1.0 if i % 2 == 0 else 0.74)
        pts.append((c + r * math.cos(a), c + r * math.sin(a)))
    d.polygon(pts, fill=(250, 250, 250, 255), outline=(16, 16, 16, 255))
    d.line(pts + [pts[0]], fill=(16, 16, 16, 255), width=6)
    d.text((c, c), s, font=font, fill=(16, 16, 16, 255), anchor="mm")
    im = im.rotate(angle, resample=BILINEAR, expand=True)
    _raw[key] = im
    return im


def arrow(cnv, x0, y0, x1, y1, width=11, head=30, color=(250, 250, 250),
          outline=(16, 16, 16)):
    """화살표(광고에서 '여기를 보세요' 할 때 쓰는 것)."""
    d = ImageDraw.Draw(cnv)
    a = math.atan2(y1 - y0, x1 - x0)
    bx, by = x1 - head * math.cos(a) * 0.9, y1 - head * math.sin(a) * 0.9
    for (col, wd, hd) in ((outline, width + 6, head + 7), (color, width, head)):
        d.line([(x0, y0), (bx, by)], fill=col, width=wd)
        d.polygon([
            (x1, y1),
            (x1 - hd * math.cos(a - 0.42), y1 - hd * math.sin(a - 0.42)),
            (x1 - hd * math.cos(a + 0.42), y1 - hd * math.sin(a + 0.42)),
        ], fill=col)


def announcer_bar(cnv, lang, s, y, size=34, kind="punch", pad=16, fill=(245, 245, 245)):
    """광고 아나운서 자막 띠(검은 바 + 흰 테두리 + 굵은 대문자)."""
    f = FNT(lang, kind, size)
    d = ImageDraw.Draw(cnv)
    bb = d.textbbox((0, 0), s, font=f)
    tw = bb[2] - bb[0]
    if tw > W - 200:                      # 긴 번역문이 넘치면 줄인다
        size = max(14, int(size * (W - 200) / tw))
        f = FNT(lang, kind, size)
        bb = d.textbbox((0, 0), s, font=f)
        tw = bb[2] - bb[0]
    th = bb[3] - bb[1]
    x0, x1 = W / 2 - tw / 2 - pad * 2, W / 2 + tw / 2 + pad * 2
    d.rectangle([x0, y - th / 2 - pad, x1, y + th / 2 + pad], fill=(12, 12, 12))
    d.rectangle([x0 + 5, y - th / 2 - pad + 5, x1 - 5, y + th / 2 + pad - 5],
                outline=(238, 238, 238), width=3)
    d.text((W / 2, y), s, font=f, fill=fill, anchor="mm")
    return (x0, x1)


def fine_print(cnv, lang, s, y=None, size=15, color=(190, 190, 190)):
    """화면 아래 깨알 고지. 밝은 배경에서도 읽히게 어두운 판을 깔고, CRT 왜곡을
    감안해 가장자리에서 충분히 띄운다."""
    f = FNT(lang, "serif", size)
    y = y if y is not None else H - 40
    d = ImageDraw.Draw(cnv)
    bb = d.textbbox((0, 0), s, font=f)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    plate = Image.new("RGBA", (int(tw) + 28, int(th) + 18), (8, 8, 8, 188))
    cnv.paste(plate, (int(W / 2 - plate.width / 2), int(y - plate.height / 2)), plate)
    d.text((W / 2, y), s, font=f, fill=color, anchor="mm")


def star_rating(cnv, cx, cy, size=30, n=5):
    for i in range(n):
        blit(cnv, impact_star(size, points=5), cx + (i - (n - 1) / 2) * (size * 1.12), cy,
             anchor="cc")


def strike(cnv, x0, x1, y, width=7):
    """가격에 쫙 긋는 줄. 너무 굵으면 글자를 통째로 덮어 글자가 검게 보인다."""
    d = ImageDraw.Draw(cnv)
    d.line([(x0, y + 11), (x1, y - 11)], fill=(16, 16, 16), width=width + 5)
    d.line([(x0, y + 11), (x1, y - 11)], fill=(250, 250, 250), width=width)


def sweat(cnv, x, y, s=1.0):
    """당황했을 때 튀는 땀방울(카툰 관용 표현)."""
    d = ImageDraw.Draw(cnv)
    r = 11 * s
    d.ellipse([x - r, y - r, x + r, y + r * 1.25], fill=(250, 250, 250),
              outline=(18, 18, 18), width=int(4 * s) or 1)
    d.polygon([(x - r * 0.55, y - r * 0.5), (x, y - r * 2.0), (x + r * 0.55, y - r * 0.5)],
              fill=(250, 250, 250), outline=(18, 18, 18))
