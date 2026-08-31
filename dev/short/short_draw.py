# -*- coding: utf-8 -*-
"""컴스톡 쇼츠 「Dam Dididi」 - 그리기 헬퍼(에셋 캐시, 로봇 모션, 품목 뭉치, O/X 판정)."""
import math
import os

from PIL import Image, ImageDraw, ImageFont

from short_common import (RES, FONTS, W, H, BG, BEAT, T0, SEG_LEN, SEG_IN, SEG_IN_STAGGER,
                          SEG_MARK, SEG_MARK_POP, SEG_OUT, SEG_OUT_LEN, SEGMENTS, ITEMS,
                          CLUSTER, CLUSTERS, ITEM_BOX, ITEM_BOXES, ITEM_CXS, GREEN, RED)

LANCZOS = Image.Resampling.LANCZOS
BICUBIC = Image.Resampling.BICUBIC

ROBOT = "Comstock.png"

_raw = {}
_spr = {}
_fnt = {}


# ---------------------------------------------------------------- 에셋
def A(rel):
    """원본 스프라이트를 여백을 잘라낸 RGBA로 캐시해서 돌려준다.

    게임 스프라이트는 캔버스 가운데에 그림이 놓인 형태라 여백이 제각각이다
    (예: Comstock.png 1242x720 캔버스에 그림은 1197x672). 여백을 그대로 두면
    "크기를 맞췄는데 그림만 작게 보이는" 문제가 생기므로 여기서 한 번에 잘라낸다.
    """
    im = _raw.get(rel)
    if im is None:
        im = Image.open(os.path.join(RES, rel.replace("/", os.sep))).convert("RGBA")
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
        _raw[rel] = im
    return im


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
        im = im.rotate(rot, resample=BICUBIC, expand=True)
    if len(_spr) > 3000:
        _spr.clear()
    _spr[key] = im
    return im


def FNT(size):
    f = _fnt.get(size)
    if f is None:
        p = os.path.join(FONTS, "Orbitron", "Orbitron-Bold.ttf")
        f = ImageFont.truetype(p, size)
        _fnt[size] = f
    return f


def blit(dst, src, x, y, anchor="mm", alpha=1.0):
    """anchor 기준으로 스프라이트를 얹는다. mm=가운데, cb=아래가운데."""
    if alpha <= 0.003:
        return
    ax = x - src.width / 2
    ay = y - src.height / 2 if anchor == "mm" else y - src.height
    if alpha < 0.997:
        a = src.getchannel("A").point(lambda v: int(v * alpha))
        src = src.copy()
        src.putalpha(a)
    dst.alpha_composite(src, (int(round(ax)), int(round(ay))))


# ---------------------------------------------------------------- 이징
def clamp(v, a=0.0, b=1.0):
    return a if v < a else (b if v > b else v)


def ease_out(p):
    return 1.0 - (1.0 - p) ** 3


def ease_in(p):
    return p * p * p


def ease_out_back(p, s=1.70158):
    """튀어나오듯 살짝 넘겼다 돌아오는 이징. 팝 애니메이션용."""
    p = p - 1.0
    return p * p * ((s + 1.0) * p + s) + 1.0


# ---------------------------------------------------------------- 로봇 모션
# 레퍼런스의 고양이는 컷아웃 한 장이 박자에 맞춰 통통 튀는 것이 전부다
# (스토리보드 실측: 세로 위치가 거의 고정, 면적만 미세하게 오르내린다).
# 그래서 "박자 홉 + 스쿼시&스트레치 + 아주 얕은 좌우 기울기"만 준다.
HOP = 0.035          # 튀는 높이(로봇 높이 대비)
SQUASH = 0.060       # 착지에서 납작해지는 비율
TILT = 2.4           # 좌우 기울기 최대 각도

# 마지막(돈=골드) 구간에서 고양이가 눈에 띄게 커진다(스토리보드 면적 44 → 72픽셀 = 선형 1.28배).
#
# ★ 그 1.28배를 우리 로봇에 그대로 주면 화면 왼쪽으로 삐져나간다. 레퍼런스의 고양이는
#   폭이 화면의 45%인 정사각형 덩어리라 커져도 여유가 있지만, 우리 로봇은 이미 폭
#   520px(스쿼시·기울기 최대에서 10~626)을 쓰는 가로로 긴 그림이라 1.10배가 한계다.
#   대신 **튀는 높이와 기울기를 키워서** 신난 느낌을 배율 대신 움직임으로 낸다
#   (레퍼런스도 이 구간에서 품목 뭉치가 가장 작아지므로 크기 대비는 함께 살아난다).
GOLD_ZOOM = 1.10
GOLD_HOP = 1.9        # 이 구간에서 튀는 높이 배수
GOLD_TILT = 1.6       # 이 구간에서 기울기 배수


def gold_mix(t):
    """골드 구간에서 0 → 1 로 올라갔다가 마지막에 0으로 돌아오는 값."""
    g0 = T0 + 3 * SEG_LEN
    if t < g0:
        return 0.0
    ramp = clamp((t - g0) / 0.55)
    back = clamp((t - (g0 + SEG_LEN)) / 0.24)     # 구간이 끝나면 원래대로
    return ease_out(ramp) * (1.0 - ease_out(back))


def robot_scale_at(t):
    return 1.0 + (GOLD_ZOOM - 1.0) * gold_mix(t)


def draw_robot(cnv, cx, cy, base_w, t):
    """박자에 맞춰 튀는 로봇을 그린다. cx/cy는 가운데 기준."""
    p = ((t - T0) % BEAT) / BEAT if t >= T0 else 0.0
    hop = math.sin(math.pi * p) ** 0.75                    # 0(착지) → 1(정점) → 0
    c = math.cos(2 * math.pi * p)                          # +1 착지 / -1 정점

    g = gold_mix(t)
    zoom = robot_scale_at(t)
    src = A(ROBOT)
    w = base_w * zoom * (1.0 + SQUASH * c)
    h = base_w * zoom * src.height / src.width * (1.0 - SQUASH * c)
    rot = TILT * (1.0 + (GOLD_TILT - 1.0) * g) * math.sin(math.pi * (t - T0) / (2 * BEAT))

    spr = src.resize((max(1, int(round(w))), max(1, int(round(h)))), LANCZOS)
    if abs(rot) > 0.05:
        spr = spr.rotate(rot, resample=BICUBIC, expand=True)
    # 스쿼시는 발밑을 기준으로 해야 눌리는 느낌이 난다 - 아래를 고정하고 위로 자란다.
    bottom = cy + base_w * src.height / src.width * 0.5
    lift = base_w * zoom * HOP * (1.0 + (GOLD_HOP - 1.0) * g) * hop
    blit(cnv, spr, cx, bottom - lift, anchor="cb")


# ---------------------------------------------------------------- 품목 뭉치
def fit(rel, cw, ch, flip=False, rot=0.0):
    """스프라이트를 (cw x ch) 칸에 맞춰 "눈에 보이는 덩치"가 같아지도록 줄인다.

    ★ 칸에 통째로 넣기(contain)로 맞추면 가로로 긴 무기(1.5:1)가 폭에 걸려 세로가
      칸의 2/3밖에 안 차서 디스크(1:1) 옆에서 유난히 작아 보인다. 반대로 높이로만
      맞추면 무기가 칸 밖으로 튀어나가 로봇을 덮는다. 그래서 **넓이(기하평균)** 를
      칸에 맞추고, 한쪽으로 지나치게 길어지는 것만 1.25배로 잘라 준다.
      회전은 잘림을 막기 위해 조금 여유를 두고(0.94) 건다.
    """
    src = A(rel)
    k = math.sqrt(cw * ch) / math.sqrt(src.width * src.height)
    k = min(k, cw * 1.25 / src.width, ch * 1.25 / src.height)
    if rot:
        k *= 0.94
    return SPR(rel, w=max(2, int(round(src.width * k))), flip=flip, rot=rot)


def draw_items(cnv, key, cx, cy, st):
    """구간 내부시각 st에 맞춰 품목 4개를 아래에서 솟아오르게 그린다.

    레퍼런스 실측: 뭉치가 화면 아래에서 올라오면서 커지고, 품목이 하나씩 늦게 뜬다.
    구간 끝에서는 반대로 작아지며 아래로 빠진다.
    """
    items = ITEMS[key]
    bw, bh = ITEM_BOXES.get(key, ITEM_BOX)
    cluster = CLUSTERS.get(key, CLUSTER)
    cx = ITEM_CXS.get(key, cx)
    rise = bh * 0.95

    for i, (rel, size, flip, rot) in enumerate(items):
        gx, gy, gs = cluster[i]
        q = clamp((st - i * SEG_IN_STAGGER) / SEG_IN)
        # ★ 튀어나오는 정도(back)를 기본값 1.70(약 10% 초과)으로 두면 오른쪽 품목이
        #   솟아오르는 순간에만 화면 밖으로 5px 삐져나간다. 0.9(약 5%)로 낮췄다.
        e = ease_out_back(q, 0.9) if q < 1.0 else 1.0
        out = clamp((st - SEG_OUT) / SEG_OUT_LEN)
        oe = ease_in(out)

        scale = (0.30 + 0.70 * e) * (1.0 - 0.85 * oe)
        alpha = clamp(q * 3.0) * (1.0 - clamp(out * 1.25))
        if scale <= 0.02 or alpha <= 0.01:
            continue

        k = size * gs * scale
        spr = fit(rel, bw * 0.5 * k, bh * 0.5 * k, flip=flip, rot=rot)
        x = cx + gx * bw
        y = cy + gy * bh + rise * (1.0 - e) + bh * 0.30 * oe
        blit(cnv, spr, x, y, alpha=alpha)


# ---------------------------------------------------------------- O / X 판정
_mark_cache = {}


def mark_img(ok, size):
    """초록 체크 / 빨간 가위표를 4배 슈퍼샘플링으로 그려 캐시한다.

    Pillow의 선 그리기는 안티에일리어싱이 없어서 그대로 그리면 계단이 심하게 진다.
    4배로 그린 뒤 LANCZOS로 줄이면 스프라이트와 같은 매끈함이 나온다.
    """
    key = (ok, size)
    im = _mark_cache.get(key)
    if im is not None:
        return im

    S = 4
    s = size * S
    col = GREEN if ok else RED
    lay = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    wdt = int(s * 0.155)
    r = wdt // 2

    def stroke(p0, p1):
        d.line([p0, p1], fill=col + (255,), width=wdt)
        for p in (p0, p1):                       # 둥근 끝(Pillow는 캡을 안 그려준다)
            d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=col + (255,))

    if ok:
        stroke((s * 0.10, s * 0.56), (s * 0.38, s * 0.84))
        stroke((s * 0.38, s * 0.84), (s * 0.92, s * 0.16))
    else:
        stroke((s * 0.14, s * 0.14), (s * 0.86, s * 0.86))
        stroke((s * 0.86, s * 0.14), (s * 0.14, s * 0.86))

    im = lay.resize((size, size), LANCZOS)
    if len(_mark_cache) > 400:
        _mark_cache.clear()
    _mark_cache[key] = im
    return im


def draw_mark(cnv, ok, cx, cy, base, st):
    """판정 표시를 팝으로 띄우고 구간 끝에서 다시 거둬들인다."""
    if st < SEG_MARK:
        return
    q = clamp((st - SEG_MARK) / SEG_MARK_POP)
    e = ease_out_back(q, 2.2) if q < 1.0 else 1.0
    out = clamp((st - (SEG_OUT + 0.07)) / SEG_OUT_LEN)
    scale = e * (1.0 - 0.8 * ease_in(out))
    alpha = clamp(q * 4.0) * (1.0 - clamp(out * 1.3))
    if scale <= 0.02 or alpha <= 0.01:
        return
    size = max(2, int(round(base * scale)))
    blit(cnv, mark_img(ok, size), cx, cy, alpha=alpha)


# ---------------------------------------------------------------- 워터마크
def draw_watermark(cnv, s):
    d = ImageDraw.Draw(cnv)
    f = FNT(30)
    # 레퍼런스의 채널 워터마크와 같은 자리(내용 상자 오른쪽 아래).
    d.text((W - 54, H / 2 + 1440 * 0.47), s, font=f, fill=(180, 180, 180, 255), anchor="rs")
