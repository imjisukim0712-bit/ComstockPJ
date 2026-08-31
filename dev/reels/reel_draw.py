# -*- coding: utf-8 -*-
"""컴스톡 릴스 - 얼굴 스탬프 제작, 튐(스쿼시) 곡선, 격자 배치, 글자."""
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

from reel_common import (HEADS, W, H, SAFE_TOP, SAFE_BOTTOM, INK, HITS, BEAT)

LANCZOS = Image.Resampling.LANCZOS
BICUBIC = Image.Resampling.BICUBIC

# 스탬프를 미리 만들어 두는 해상도. 프레임마다는 여기서 줄이기만 한다.
STAMP = 900

# 원본 밈의 노란 테두리를 옮긴 것. 흰 배경에서 얼굴이 떠 보이게 만드는 게 이 링의 역할이다.
# ★ 링은 스탬프와 함께 축소되므로 두께가 "얼굴 크기 대비 비율"로 고정된다.
# 16px(1.8%)은 12개 격자에서 서로 닿아 지저분했다. 10px(1.1%)이 한 얼굴에서도, 격자에서도 버틴다.
RIM_COLOR = (255, 214, 64)
RIM_PX = 10          # STAMP 기준 두께
GLOW_COLOR = (255, 196, 40)

_stamps = {}
_sized = {}
_fonts = {}


def font(path, size):
    key = (path, size)
    f = _fonts.get(key)
    if f is None:
        f = ImageFont.truetype(path, size)
        _fonts[key] = f
    return f


# ---------------------------------------------------------------- 얼굴 스탬프
def _frames_for(name):
    """머리 이름 하나에 해당하는 PNG 경로들. NeonEye처럼 여러 장이면 전부 돌려준다."""
    single = os.path.join(HEADS, name + ".png")
    if os.path.exists(single):
        return [single]
    seq = sorted(f for f in os.listdir(HEADS)
                 if f.startswith(name + "_") and f.endswith(".png"))
    if not seq:
        raise FileNotFoundError("머리 스프라이트를 못 찾았다: " + name)
    return [os.path.join(HEADS, f) for f in seq]


def _make_stamp(path):
    """알파 bbox로 잘라 정사각 캔버스 가운데 놓고, 노란 링 + 글로우를 입혀 둔다.

    ★ 링을 프레임마다 만들면 MaxFilter가 450프레임 x 12얼굴만큼 돌아 렌더가 몇 배로
    느려진다. 얼굴당 딱 한 번만 만들고 이후에는 축소/회전만 한다.
    """
    src = Image.open(path).convert("RGBA")
    box = src.getchannel("A").getbbox()
    src = src.crop(box)

    # 링이 잘리지 않게 여백을 두고 정사각형으로 맞춘다.
    pad = RIM_PX * 3
    side = max(src.size)
    inner = STAMP - pad * 2
    sc = inner / side
    src = src.resize((max(1, round(src.width * sc)), max(1, round(src.height * sc))), LANCZOS)

    canvas = Image.new("RGBA", (STAMP, STAMP), (0, 0, 0, 0))
    canvas.alpha_composite(src, ((STAMP - src.width) // 2, (STAMP - src.height) // 2))

    a = canvas.getchannel("A")
    # 링: 알파를 부풀린 것에서 원본 알파를 빼면 바깥쪽 테두리만 남는다.
    grown = a.filter(ImageFilter.MaxFilter(RIM_PX * 2 + 1))
    ring = Image.new("RGBA", canvas.size, RIM_COLOR + (0,))
    ring.putalpha(grown)
    # 글로우: 링을 한 번 더 부풀리고 흐린다.
    glow = Image.new("RGBA", canvas.size, GLOW_COLOR + (0,))
    glow.putalpha(grown.filter(ImageFilter.GaussianBlur(RIM_PX * 2.2)).point(lambda v: v * 0.40))

    out = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    out.alpha_composite(glow)
    out.alpha_composite(ring)
    out.alpha_composite(canvas)
    return out


def stamp(name, frame=0):
    """머리 이름(+애니메이션 프레임)의 완성된 스탬프."""
    key = (name, frame)
    im = _stamps.get(key)
    if im is None:
        paths = _frames_for(name)
        im = _make_stamp(paths[frame % len(paths)])
        _stamps[key] = im
    return im


def frame_count(name):
    return len(_frames_for(name))


# ---------------------------------------------------------------- 튐 곡선
def hit_state(t, slot, nslots):
    """시각 t에서 이 칸이 얼마나 찌그러져 있는지와, 몇 번째 히트에 맞았는지.

    돌려주는 `s`는 1이 최대 압축, 0이 평상시이고 부호가 바뀌며 잦아든다(감쇠 스프링).
    ★ 히트 간격으로 정규화하지 않는다 - 그래야 8분음표("디디")가 앞 여운을 잘라먹으면서
    실제 드럼처럼 촘촘해진다.
    """
    cur = None
    for ht, n, b, k in HITS:
        if ht <= t + 1e-6:
            cur = (ht, n)
        else:
            break
    if cur is None:
        return 0.0, -1

    ht, n = cur
    # 칸이 4개 이하면 "주고받기"(히트마다 한 칸씩), 그보다 많으면 다 같이 튀되 물결로 늦춘다.
    if nslots <= 4:
        if n % nslots != slot:
            return 0.0, n
        u = t - ht
    else:
        u = t - ht - slot * 0.012
        if u < 0:
            return 0.0, n

    s = math.exp(-u * 9.0) * math.cos(u * 22.0)
    return s, n


def squash(size, s):
    """압축량 s를 (가로, 세로, 아래로 내려앉는 픽셀)로 바꾼다."""
    sx = size * (1.0 + 0.17 * s)
    sy = size * (1.0 - 0.23 * s)
    dy = size * 0.055 * s
    return max(4, int(round(sx))), max(4, int(round(sy))), dy


def camera_pulse(t):
    """히트마다 화면 전체가 살짝 커졌다 돌아온다."""
    s, _ = hit_state(t, 0, 1)
    return 1.0 + 0.028 * max(0.0, s)


# ---------------------------------------------------------------- 배치
def layout(n):
    """얼굴 n개의 (중심x, 중심y, 한 변) 목록. 안전 띠 안에서만 잡는다."""
    cy = (SAFE_TOP + SAFE_BOTTOM) / 2.0
    if n == 1:
        return [(W / 2, cy, 740)]
    if n == 2:
        return [(W * 0.28, cy, 470), (W * 0.72, cy, 470)]
    if n == 4:
        return [(W * 0.28, cy - 235, 430), (W * 0.72, cy - 235, 430),
                (W * 0.28, cy + 235, 430), (W * 0.72, cy + 235, 430)]
    if n == 12:
        xs = (W * 0.185, W * 0.5, W * 0.815)
        band = SAFE_BOTTOM - SAFE_TOP
        ys = [SAFE_TOP + band * (i + 0.5) / 4.0 for i in range(4)]
        return [(x, y, 296) for y in ys for x in xs]
    raise ValueError("배치가 정의되지 않은 얼굴 수: %d" % n)


def _sized_stamp(name, anim, size):
    """900px 스탬프를 슬롯 크기 근처(1.3배)로 한 번만 줄여 캐시해 둔다.

    ★ 프레임마다 900px에서 300px로 LANCZOS를 태우면 렌더 시간의 대부분이 여기서 나간다.
    크기는 카메라 펄스 때문에 매 프레임 미세하게 달라지므로 16px 단위로 뭉쳐 캐시한다.
    """
    q = max(64, int(size * 1.3) // 16 * 16)
    key = (name, anim, q)
    im = _sized.get(key)
    if im is None:
        src = stamp(name, anim)
        im = src if q >= src.width else src.resize((q, q), LANCZOS)
        _sized[key] = im
    return im


def paste_face(dst, name, anim, cx, cy, size, s, rot):
    """스탬프 하나를 찌그러뜨리고 돌려서 붙인다."""
    sx, sy, dy = squash(size, s)
    im = _sized_stamp(name, anim, size).resize((sx, sy), LANCZOS)
    if abs(rot) > 0.3:
        im = im.rotate(rot, resample=BICUBIC, expand=True)
    dst.alpha_composite(im, (int(round(cx - im.width / 2)),
                             int(round(cy + dy - im.height / 2))))


# ---------------------------------------------------------------- 연출 소품
def speed_lines(dst, s, inner):
    """"담"에서만 얼굴 무리 바깥으로 짧게 터지는 방사선.

    `inner`는 선이 시작하는 반지름 - 얼굴을 덮지 않게 무대 크기에 맞춰 넘겨준다.
    캡션이 있는 위쪽(각도 -120~-60도)은 비워 둔다.
    """
    if s <= 0.2:
        return
    d = ImageDraw.Draw(dst, "RGBA")
    cx, cy = W / 2, (SAFE_TOP + SAFE_BOTTOM) / 2
    a = int(70 * min(1.0, s))
    for i in range(16):
        deg = i * 22.5 + 11
        if 240 <= deg <= 300:          # 캡션 자리
            continue
        ang = math.radians(deg)
        r0 = inner + 40 * s
        r1 = r0 + 120 + 70 * s
        d.line([(cx + math.cos(ang) * r0, cy + math.sin(ang) * r0),
                (cx + math.cos(ang) * r1, cy + math.sin(ang) * r1)],
               fill=(130, 130, 142, a), width=12)


def text_center(dst, txt, fnt, cy, fill=INK, max_w=None, path=None):
    """가운데 정렬 한 줄. `max_w`를 주면 넘칠 때 폰트를 줄여서 맞춘다."""
    d = ImageDraw.Draw(dst)
    if max_w and path:
        size = fnt.size
        while size > 12:
            box = d.textbbox((0, 0), txt, font=fnt)
            if box[2] - box[0] <= max_w:
                break
            size -= 2
            fnt = font(path, size)
    box = d.textbbox((0, 0), txt, font=fnt)
    d.text((W / 2 - (box[2] - box[0]) / 2 - box[0], cy - (box[3] - box[1]) / 2 - box[1]),
           txt, font=fnt, fill=fill)
    return box[2] - box[0], box[3] - box[1]
