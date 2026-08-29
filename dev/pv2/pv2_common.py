# -*- coding: utf-8 -*-
"""컴스톡 PV 2탄 공용 모듈 - 경로/폰트/스프라이트/그리기/합성 사운드.

dev/pv 의 40초 흑백 인포머셜과 별개로, **컬러** 병맛 영상 두 벌을 만든다:
  * pv2_tvad.py - 미국 제약(처방약) TV 광고 패러디 (16:9 1920x1080, 28초)
  * pv2_meme.py - 인스타그램 릴스 밈 몽타주   (9:16 1080x1920, 20초)

경로는 이 파일 위치에서 역산한다(dev/pv 처럼 드라이브 문자를 박지 않는다).
폰트는 저장소 안에 이미 있는 것만 쓴다 - Anton(임팩트류)/Bangers/Oswald/Roboto는
TextMesh Pro 예제에, Orbitron/NotoSansKR은 Assets/Fonts 에 있다.
이모지는 시스템 NotoColorEmoji(비트맵 109px 고정)를 그린 뒤 원하는 크기로 줄인다.
"""
import math
import os
import random
import subprocess
import tempfile
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, "Assets", "Resources")
CACHE = os.environ.get("PV2_CACHE") or os.path.join(tempfile.gettempdir(),
                                                    "comstock_pv2_cache")
LANCZOS = Image.Resampling.LANCZOS
BILINEAR = Image.Resampling.BILINEAR

ITCH_URL = "pyramid-studio.itch.io/comstock"

# ---------------------------------------------------------------- 폰트
_FONT_FILES = {
    # 밈 대문자(임팩트 대용) / 만화 헤드라인 / 광고 카피 / 본문 / 게임 로고체
    "anton": "Assets/TextMesh Pro/Examples & Extras/Fonts/Anton.ttf",
    "bangers": "Assets/TextMesh Pro/Examples & Extras/Fonts/Bangers.ttf",
    "oswald": "Assets/TextMesh Pro/Examples & Extras/Fonts/Oswald-Bold.ttf",
    "roboto": "Assets/TextMesh Pro/Examples & Extras/Fonts/Roboto-Bold.ttf",
    "liberation": "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
    "orbitron": "Assets/Fonts/Orbitron/Orbitron-Black.ttf",
    "korean": "Assets/Fonts/NotoSansKR/NotoSansKR-Bold.ttf",
    "korean_r": "Assets/Fonts/NotoSansKR/NotoSansKR-Regular.ttf",
}
_SYS_FONTS = {
    "serif": "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf",
    "serifb": "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf",
}
EMOJI_TTF = "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf"

_fnt = {}
_raw = {}
_spr = {}
_emo = {}


def font_file(name):
    if name in _FONT_FILES:
        return os.path.join(ROOT, _FONT_FILES[name].replace("/", os.sep))
    return _SYS_FONTS[name]


# 한글판에서는 라틴 전용 폰트(Anton/Oswald/Roboto/DejaVu)를 NotoSansKR로 바꿔치기
# 한다. 장면 코드는 폰트 이름을 그대로 쓰고, set_lang()만 호출하면 된다.
_KO_SUB = {"anton": "korean", "oswald": "korean", "roboto": "korean",
           "bangers": "korean", "serif": "korean_r", "serifb": "korean"}
_font_sub = {}


def set_lang(lang):
    global _font_sub
    _font_sub = dict(_KO_SUB) if lang == "ko" else {}


def F(name, size):
    name = _font_sub.get(name, name)
    key = (name, int(size))
    f = _fnt.get(key)
    if f is None:
        f = ImageFont.truetype(font_file(name), int(size))
        _fnt[key] = f
    return f


# ---------------------------------------------------------------- 에셋
def A(rel):
    """게임 리소스 원본(RGBA)을 캐시해서 돌려준다."""
    im = _raw.get(rel)
    if im is None:
        im = Image.open(os.path.join(RES, rel.replace("/", os.sep))).convert("RGBA")
        _raw[rel] = im
    return im


def SEQ(folder):
    """폴더 안의 png 상대경로를 이름순 리스트로."""
    key = "@seq:" + folder
    if key not in _raw:
        d = os.path.join(RES, folder.replace("/", os.sep))
        names = sorted(f for f in os.listdir(d) if f.lower().endswith(".png"))
        _raw[key] = [folder + "/" + n for n in names]
    return _raw[key]


def SPR(rel, h=None, w=None, flip=False, rot=0.0, crop=False):
    """크기/반전/회전을 적용한 스프라이트(캐시). crop=True면 여백을 먼저 잘라낸다."""
    rot = round(rot, 1)
    key = (rel, h, w, flip, rot, crop)
    im = _spr.get(key)
    if im is not None:
        return im
    im = A(rel)
    if crop:
        bb = im.getbbox()
        if bb:
            im = im.crop(bb)
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


def WPN(name, direction, h):
    """총구가 direction(-1 왼쪽 / +1 오른쪽)을 향한 무기(여백 제거).

    프로젝트 규칙: Right*는 총구가 왼쪽, Left*는 총구가 오른쪽을 본다.
    """
    rel = ("Right" if direction < 0 else "Left") + name + ".png"
    return SPR(rel, h=int(h), crop=True)


def EMOJI(ch, h):
    """컬러 이모지 한 덩어리를 높이 h로 - NotoColorEmoji는 109px 고정 비트맵이라
    한 번 크게 그린 뒤 리샘플한다(밈에서는 이 특유의 뭉개짐도 맛이다)."""
    key = (ch, int(h))
    im = _emo.get(key)
    if im is not None:
        return im
    f = ImageFont.truetype(EMOJI_TTF, 109)
    tmp = Image.new("RGBA", (160 * max(1, len(ch)), 160), (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    d.text((8, 8), ch, font=f, embedded_color=True)
    bb = tmp.getbbox()
    if bb:
        tmp = tmp.crop(bb)
    nw = max(1, int(round(tmp.width * h / tmp.height)))
    im = tmp.resize((nw, int(h)), LANCZOS)
    _emo[key] = im
    return im


def is_emoji(ch):
    o = ord(ch[0])
    return o >= 0x1F000 or 0x2600 <= o <= 0x27BF or o in (0x2B50, 0x203C, 0x2049)


def split_emoji(s):
    """문자열을 [('t', 텍스트) | ('e', 이모지)] 조각으로 나눈다.
    VS16(0xFE0F)은 앞 이모지에 붙인다. ZWJ 조합은 안 쓰므로 처리하지 않는다."""
    out = []
    buf = ""
    for ch in s:
        if ord(ch) == 0xFE0F:
            continue
        if is_emoji(ch):
            if buf:
                out.append(("t", buf))
                buf = ""
            out.append(("e", ch))
        else:
            buf += ch
    if buf:
        out.append(("t", buf))
    return out


# ---------------------------------------------------------------- 배치/이징
def blit(dst, src, x, y, anchor="cc", alpha=1.0):
    """anchor 두 글자: 가로 l/c/r + 세로 t/c/b."""
    if alpha <= 0:
        return
    ax, ay = anchor[0], anchor[1]
    px = int(x - src.width / 2) if ax == "c" else (int(x) if ax == "l" else int(x - src.width))
    py = int(y - src.height / 2) if ay == "c" else (int(y) if ay == "t" else int(y - src.height))
    if alpha < 1.0:
        src = src.copy()
        src.putalpha(src.getchannel("A").point(lambda v: int(v * alpha)))
    if dst.mode == "RGBA":
        dst.alpha_composite(src, (px, py))
    else:
        dst.paste(src, (px, py), src)


def ease_out(p):
    return 1 - (1 - p) ** 3


def ease_in(p):
    return p ** 3


def clamp(v, a=0.0, b=1.0):
    return a if v < a else (b if v > b else v)


def twos(t, fps=12.0):
    """카툰처럼 동작을 초당 fps 장으로 계단화."""
    return math.floor(t * fps) / fps


def pop_scale(t, dur=0.22, over=0.45):
    """등장 팝: 크게 나타나서 살짝 튕기며 1.0으로."""
    p = clamp(t / dur)
    return 1.0 + over * (1 - ease_out(p)) - 0.05 * math.sin(ease_out(p) * math.pi)


# ---------------------------------------------------------------- 글자
def text_w(s, fname, size):
    d = ImageDraw.Draw(Image.new("RGB", (8, 8)))
    bb = d.textbbox((0, 0), s, font=F(fname, size))
    return bb[2] - bb[0]


def fit_size(s, fname, size, max_w):
    tw = text_w(s, fname, size)
    if tw > max_w:
        size = max(12, int(size * max_w / tw))
    return size


def otext(cnv, xy, s, fname, size, fill=(255, 255, 255), anchor="mm", stroke=0,
          stroke_fill=(20, 20, 22), max_w=None):
    """외곽선 글자. max_w를 주면 넘칠 때 크기를 줄인다. 실제 크기를 돌려준다."""
    if max_w:
        size = fit_size(s, fname, size, max_w)
    d = ImageDraw.Draw(cnv)
    d.text(xy, s, font=F(fname, size), fill=fill, anchor=anchor,
           stroke_width=stroke, stroke_fill=stroke_fill)
    return size


def emoji_line(cnv, xy, s, fname, size, fill=(255, 255, 255), anchor="mm",
               stroke=0, stroke_fill=(20, 20, 22), emoji_scale=1.06, gap=6):
    """텍스트+이모지 혼합 한 줄을 anchor 기준으로 그린다(가로 l/c/r + 세로 m만)."""
    parts = split_emoji(s)
    d = ImageDraw.Draw(cnv)
    widths = []
    for (k, v) in parts:
        if k == "t":
            bb = d.textbbox((0, 0), v, font=F(fname, size))
            widths.append(bb[2] - bb[0])
        else:
            widths.append(EMOJI(v, int(size * emoji_scale)).width + gap)
    total = sum(widths)
    x, y = xy
    ax = anchor[0]
    px = x - total / 2 if ax in ("c", "m") else (x if ax == "l" else x - total)
    for (k, v), w in zip(parts, widths):
        if k == "t":
            d.text((px, y), v, font=F(fname, size), fill=fill, anchor="lm",
                   stroke_width=stroke, stroke_fill=stroke_fill)
        else:
            e = EMOJI(v, int(size * emoji_scale))
            blit(cnv, e, px + gap / 2, y, anchor="lc")
        px += w
    return total


# ---------------------------------------------------------------- 화면 효과
def gradient_v(w, h, top, bottom):
    key = ("@grad", w, h, top, bottom)
    if key in _raw:
        return _raw[key]
    col = np.linspace(np.array(top, float), np.array(bottom, float), h)
    im = Image.fromarray(np.repeat(col[:, None, :], w, axis=1).astype(np.uint8), "RGB")
    _raw[key] = im
    return im


def vignette_mask(w, h, strength=0.5, power=2.4):
    key = ("@vig", w, h, round(strength, 2), power)
    if key in _raw:
        return _raw[key]
    yy, xx = np.mgrid[0:h, 0:w]
    dx = (xx - w / 2) / (w / 2)
    dy = (yy - h / 2) / (h / 2)
    r = np.sqrt(dx * dx + dy * dy) / 1.41421
    v = 1.0 - strength * (r ** power)
    im = Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8), "L")
    _raw[key] = im
    return im


def apply_vignette(im, strength=0.5, power=2.4):
    m = vignette_mask(im.width, im.height, strength, power)
    arr = np.asarray(im, np.float32) * (np.asarray(m, np.float32)[:, :, None] / 255.0)
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def add_grain(im, amount=8, seed=0):
    rng = np.random.default_rng(seed)
    noise = rng.normal(0, amount, (im.height, im.width, 1)).astype(np.float32)
    arr = np.asarray(im, np.float32) + noise
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")


_post_cache = {}


def fast_post(im, strength=0.30, power=2.6, grain=4, f=0):
    """비네트 + 그레인을 PIL C 경로로만 처리한다(전 프레임에 도는 코드라 numpy
    float 연산을 쓰면 렌더가 프레임당 수백 ms씩 느려진다). 그레인은 미리 만든
    타일 6장을 돌려 쓴다 - 매 프레임 난수를 뽑는 것과 화면상 차이가 없다."""
    from PIL import ImageChops
    key = ("vrgb", im.size, round(strength, 2), power)
    v = _post_cache.get(key)
    if v is None:
        v = vignette_mask(im.width, im.height, strength, power).convert("RGB")
        _post_cache[key] = v
    out = ImageChops.multiply(im, v)
    if grain > 0:
        gkey = ("grain", im.size, grain)
        tiles = _post_cache.get(gkey)
        if tiles is None:
            tiles = []
            for s in range(6):
                rng = np.random.default_rng(1000 + s)
                n = rng.normal(128, grain, (im.height, im.width))
                tiles.append(Image.fromarray(
                    np.clip(n, 0, 255).astype(np.uint8), "L").convert("RGB"))
            _post_cache[gkey] = tiles
        out = ImageChops.add(out, tiles[f % 6], 1.0, -128)
    return out


def rgb_shift(im, dx):
    """싸구려 색수차 - R을 오른쪽, B를 왼쪽으로 민다."""
    if dx <= 0:
        return im
    r, g, b = im.split()
    r = ImageChops_offset(r, dx)
    b = ImageChops_offset(b, -dx)
    return Image.merge("RGB", (r, g, b))


def ImageChops_offset(ch, dx):
    from PIL import ImageChops
    return ImageChops.offset(ch, int(dx), 0)


def zoom_at(im, scale, cx=None, cy=None):
    """같은 크기를 유지하면서 (cx,cy) 중심으로 확대한다. scale>=1."""
    if abs(scale - 1.0) < 1e-3:
        return im
    w, h = im.size
    cx = w / 2 if cx is None else cx
    cy = h / 2 if cy is None else cy
    cw, ch = w / scale, h / scale
    x0 = clamp(cx - cw / 2, 0, w - cw)
    y0 = clamp(cy - ch / 2, 0, h - ch)
    return im.crop((int(x0), int(y0), int(x0 + cw), int(y0 + ch))).resize((w, h), BILINEAR)


def deep_fry(im, p):
    """딥프라이드 밈 필터(p 0~1): 채도/대비 폭발 + 노이즈 + 과한 샤픈."""
    if p <= 0:
        return im
    im = ImageEnhance.Color(im).enhance(1 + 1.9 * p)
    im = ImageEnhance.Contrast(im).enhance(1 + 0.85 * p)
    im = ImageEnhance.Brightness(im).enhance(1 + 0.10 * p)
    if p > 0.25:
        im = im.filter(ImageFilter.UnsharpMask(radius=3, percent=int(190 * p), threshold=2))
    im = add_grain(im, amount=16 * p, seed=int(p * 997))
    return im


def desaturate(im, amount):
    """amount 1.0 = 완전 흑백."""
    return ImageEnhance.Color(im).enhance(1.0 - amount)


def screen_flash(cnv, alpha, color=(255, 255, 255)):
    if alpha <= 0:
        return
    ov = Image.new("RGBA", cnv.size, color + (int(255 * clamp(alpha)),))
    cnv.paste(ov, (0, 0), ov)


def speed_lines(cnv, cx, cy, n, r0, r1, seed, width=6, color=(255, 255, 255, 110)):
    d = ImageDraw.Draw(cnv, "RGBA")
    rng = random.Random(seed)
    for _ in range(n):
        a = rng.random() * math.tau
        d.line([(cx + r0 * math.cos(a), cy + r0 * math.sin(a)),
                (cx + r1 * math.cos(a), cy + r1 * math.sin(a))], fill=color, width=width)


def sunburst(w, h, angle, c1=(255, 214, 92), c2=(255, 170, 40)):
    """방사형 배경(홈쇼핑/플렉스 연출용). angle도 단위로 돌린다."""
    key = ("@burst", w, h, int(angle) % 20, c1, c2)
    if key in _raw:
        return _raw[key]
    big = int(math.hypot(w, h)) + 40
    im = Image.new("RGB", (big, big), c2)
    d = ImageDraw.Draw(im)
    c = big / 2
    n = 18
    for i in range(n):
        a0 = math.tau * i / n + math.radians(angle)
        a1 = a0 + math.tau / (n * 2)
        d.polygon([(c, c), (c + big * math.cos(a0), c + big * math.sin(a0)),
                   (c + big * math.cos(a1), c + big * math.sin(a1))], fill=c1)
    im = im.crop((int(c - w / 2), int(c - h / 2), int(c - w / 2) + w, int(c - h / 2) + h))
    if len(_raw) > 900:
        _raw.clear()
    _raw[key] = im
    return im


# ---------------------------------------------------------------- 게임 소품
ROBOT = "Comstock.png"


def robot_img(h, rot=0.0, flip=False):
    return SPR(ROBOT, h=int(h), rot=rot, flip=flip)


def draw_robot(cnv, cx, gy, h, t, bounce=True, rot=0.0, flip=False, alpha=1.0,
               sad=0.0):
    """로봇을 발 기준(gy)으로 그린다. sad>0이면 축 처진 모습(기울이고 낮춘다)."""
    ta = twos(t)
    dy = -abs(math.sin(ta * math.pi * 2.2)) * (h * 0.055) if bounce else 0.0
    if sad > 0:
        rot = rot + 13 * sad
        dy += h * 0.05 * sad
    spr = robot_img(h, rot=rot, flip=flip)
    if sad > 0:
        spr = desaturate(spr.convert("RGBA"), 0.0)  # 채도는 장면 전체에서 조절한다
    px = cx - spr.width / 2
    py = gy + dy - spr.height
    blit(cnv, spr, cx, gy + dy, anchor="cb", alpha=alpha)
    return {"x": cx, "y": gy + dy, "w": spr.width, "h": spr.height,
            "face": (px + spr.width * 0.487, py + spr.height * 0.382),
            "top": (px + spr.width * 0.487, py + spr.height * 0.061),
            "head_w": spr.height * 0.47}


ZOMBIE_KINDS = {"Zombie": "ZombieMove", "Sprinter": "SprinterMove",
                "Spitter": "SpitterMove", "Disruptor": "DisruptorMove",
                "Leader": "LeaderMove", "Charger": "ChargerMove"}


def zombie_img(t, idx=0, kind="Zombie", h=200, face=1):
    """face +1 = 오른쪽을 본다. 원본 프레임은 왼쪽을 보므로 +1이면 뒤집는다."""
    fr = SEQ(ZOMBIE_KINDS[kind])
    rel = fr[int(twos(t, 10) * 10 + idx) % len(fr)]
    return SPR(rel, h=int(h), flip=(face > 0))


def boss_img(t, h=600, roar=False, fps=14):
    fr = SEQ("BossRoar" if roar else "BossMove")
    rel = fr[int(t * fps) % len(fr)]
    return SPR(rel, h=int(h))


def explosion_at(cnv, x, y, p, size=260):
    """p 0~1로 폭발 10프레임을 넘긴다."""
    if not 0.0 <= p < 1.0:
        return
    fr = SEQ("Explosion")
    blit(cnv, SPR(fr[min(len(fr) - 1, int(p * len(fr)))], h=size), x, y, anchor="cc")


def muzzle_at(cnv, x, y, t, size=120, seed=0, rot=0.0, flip=False):
    fr = SEQ("MuzzleFlash")
    rel = fr[int(t * 30 + seed) % len(fr)]
    blit(cnv, SPR(rel, h=size, rot=rot, flip=flip), x, y, anchor="cc")


def ruined_skyline(w, h, seed=31, col=(70, 64, 84), win=(36, 32, 46)):
    """폐허 스카이라인 실루엣(컬러판). 가로로 타일링 가능."""
    key = ("@sky2", w, h, seed, col)
    if key in _raw:
        return _raw[key]
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    rng = random.Random(seed)
    x = 0
    while x < w:
        bw = rng.randint(60, 150)
        bh = rng.randint(int(h * 0.3), int(h * 0.85))
        top = h - bh
        d.rectangle([x, top, x + bw, h], fill=col + (255,))
        for k in range(rng.randint(1, 3)):
            nw = rng.randint(14, bw // 2)
            nx = x + rng.randint(0, max(1, bw - nw))
            d.rectangle([nx, top - 2, nx + nw, top + rng.randint(10, 34)],
                        fill=(0, 0, 0, 0))
        for wy in range(top + 20, h - 14, 26):
            for wx in range(x + 12, x + bw - 14, 24):
                if rng.random() < 0.5:
                    d.rectangle([wx, wy, wx + 10, wy + 12], fill=win + (255,))
        x += bw + rng.randint(4, 26)
    _raw[key] = im
    return im


def ground_tex(w, h, dark=1.0):
    """게임 지면 텍스처 띠."""
    key = ("@ground2", w, h, round(dark, 2))
    if key in _raw:
        return _raw[key]
    src = A("ground_ruined_city_v2_tile.png").convert("RGB")
    ratio = w / src.width
    band = src.resize((w, max(1, int(src.height * ratio))), LANCZOS)
    band = band.crop((0, 0, w, min(band.height, h)))
    if band.height < h:
        band = band.resize((w, h), BILINEAR)
    else:
        band = band.crop((0, 0, w, h))
    if dark != 1.0:
        band = band.point(lambda v: int(v * dark))
    _raw[key] = band
    return band


# ---------------------------------------------------------------- 인코딩
def encode_frames(render, nf, fps, size, out_path, audio=None, crf=19,
                  label="", preset="medium"):
    """render(f) -> RGB Image 를 파이프로 ffmpeg에 밀어 넣는다."""
    w, h = size
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (w, h),
           "-r", str(fps), "-i", "-"]
    if audio:
        cmd += ["-i", audio]
    cmd += ["-c:v", "libx264", "-preset", preset, "-crf", str(crf),
            "-pix_fmt", "yuv420p", "-r", str(fps), "-movflags", "+faststart"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
    cmd += [out_path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(nf):
        img = render(f)
        p.stdin.write(img.tobytes())
        if f % (fps * 2) == 0:
            print("  %s %4d/%d (%.1fs)" % (label, f, nf, f / fps), flush=True)
    p.stdin.close()
    if p.wait() != 0:
        raise SystemExit("ffmpeg 실패")
    return out_path


def make_web_version(src, dst, height=720, crf=25):
    subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", src,
                    "-vf", "scale=-2:%d" % height, "-c:v", "libx264",
                    "-preset", "medium", "-crf", str(crf), "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", dst],
                   check=True)
    return dst


# ================================================================ 사운드
SR = 48000


def _ff_decode(path, rate=1.0):
    """어떤 포맷이든 48kHz 모노 float로. rate<1 이면 느리고 낮게(좀비 신음 용)."""
    os.makedirs(CACHE, exist_ok=True)
    tag = "%s_r%03d.wav" % (os.path.basename(path), int(rate * 100))
    out = os.path.join(CACHE, "a_" + tag)
    if not os.path.exists(out):
        af = "aresample=%d" % SR
        if rate != 1.0:
            af = "asetrate=%d,aresample=%d" % (int(SR * rate), SR)
        subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                        "-i", path, "-ac", "1", "-ar", str(SR), "-af", af,
                        "-c:a", "pcm_s16le", out], check=True)
    with wave.open(out, "rb") as wv:
        raw = wv.readframes(wv.getnframes())
    return np.frombuffer(raw, np.int16).astype(np.float32) / 32768.0


_snd = {}


def game_snd(name, rate=1.0, folder="SFX"):
    key = (name, rate, folder)
    if key not in _snd:
        _snd[key] = _ff_decode(os.path.join(RES, folder, name), rate)
    return _snd[key]


class Mixer:
    def __init__(self, dur):
        self.n = int(SR * dur)
        self.L = np.zeros(self.n, np.float32)
        self.R = np.zeros(self.n, np.float32)

    def put(self, arr, t, gain=1.0, pan=0.0):
        """pan -1(왼쪽)~+1(오른쪽)."""
        off = int(t * SR)
        if off >= self.n:
            return
        if off < 0:
            arr = arr[-off:]
            off = 0
        end = min(self.n, off + len(arr))
        seg = arr[: end - off]
        gl = gain * math.sqrt((1 - pan) / 2 + 0.25)
        gr = gain * math.sqrt((1 + pan) / 2 + 0.25)
        self.L[off:end] += seg * gl
        self.R[off:end] += seg * gr

    def write(self, path, master=0.9):
        st = np.stack([self.L, self.R], axis=1)
        st = np.tanh(st * 1.15) * master          # 소프트 리미터
        data = (np.clip(st, -1, 1) * 32767).astype(np.int16)
        with wave.open(path, "wb") as wv:
            wv.setnchannels(2)
            wv.setsampwidth(2)
            wv.setframerate(SR)
            wv.writeframes(data.tobytes())
        return path


def env_ad(n, a, d, curve=4.0):
    """어택-감쇠 포락선."""
    na = max(1, int(a * SR))
    nd = max(1, n - na)
    return np.concatenate([np.linspace(0, 1, na, dtype=np.float32),
                           np.exp(-np.linspace(0, curve, nd)).astype(np.float32)])[:n]


def boom(dur=0.62, f0=140.0, f1=36.0, gain=1.0):
    """바인 붐 - 낮게 떨어지는 사인 + 살짝 왜곡."""
    n = int(dur * SR)
    tt = np.arange(n) / SR
    freq = f0 * (f1 / f0) ** (tt / dur)
    ph = 2 * np.pi * np.cumsum(freq) / SR
    x = np.sin(ph) * env_ad(n, 0.004, 0.996, curve=5.0)
    return np.tanh(x * 2.6) * gain


def sub_drop(dur=1.6, f0=90.0, f1=30.0, gain=0.9):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    freq = f0 * (f1 / f0) ** (tt / dur)
    ph = 2 * np.pi * np.cumsum(freq) / SR
    x = np.sin(ph) * env_ad(n, 0.02, 0.98, curve=2.2)
    return np.tanh(x * 1.8) * gain


def whoosh(dur=0.35, up=True, gain=0.7, seed=7):
    n = int(dur * SR)
    rng = np.random.default_rng(seed)
    x = rng.normal(0, 1, n).astype(np.float32)
    # 이동평균 폭을 시간에 따라 바꿔 대역이 쓸려 올라가는/내려가는 느낌을 낸다
    k0, k1 = (28, 3) if up else (3, 28)
    ks = np.linspace(k0, k1, 8).astype(int)
    seg = n // len(ks)
    parts = []
    for i, k in enumerate(ks):
        s = x[i * seg:(i + 1) * seg + 1]
        kernel = np.ones(max(1, k), np.float32) / max(1, k)
        parts.append(np.convolve(s, kernel, "same"))
    y = np.concatenate(parts)[:n]
    return y * env_ad(n, 0.35 * dur, 0.65 * dur, 2.0) * gain


def riser(dur=1.2, f0=160.0, f1=980.0, gain=0.5):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    freq = f0 * (f1 / f0) ** (tt / dur)
    ph = 2 * np.pi * np.cumsum(freq) / SR
    x = (np.sin(ph) + 0.4 * np.sin(2.02 * ph)) * np.linspace(0.15, 1.0, n)
    tr = 0.5 + 0.5 * np.sin(2 * np.pi * 13 * tt)
    return (x * tr * gain).astype(np.float32)


def kick808(dur=0.5, f0=120.0, f1=44.0, gain=1.0):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    freq = f0 * (f1 / f0) ** (np.clip(tt / 0.09, 0, 1))
    ph = 2 * np.pi * np.cumsum(freq) / SR
    x = np.sin(ph) * env_ad(n, 0.002, 0.998, curve=6.5)
    click = np.zeros(n, np.float32)
    click[: int(0.004 * SR)] = np.random.default_rng(3).normal(0, 0.5, int(0.004 * SR))
    return (np.tanh((x + click) * 2.2)) * gain


def bass808(dur, freq, gain=0.8):
    n = int(dur * SR)
    tt = np.arange(n) / SR
    x = np.sin(2 * np.pi * freq * tt) + 0.18 * np.sin(2 * np.pi * freq * 2 * tt)
    e = np.ones(n, np.float32)
    rel = int(0.06 * SR)
    e[:int(0.008 * SR)] = np.linspace(0, 1, int(0.008 * SR))
    e[-rel:] = np.linspace(1, 0, rel)
    return np.tanh(x * 1.6).astype(np.float32) * e * gain


def hat(dur=0.05, gain=0.35, seed=11):
    n = int(dur * SR)
    x = np.random.default_rng(seed).normal(0, 1, n).astype(np.float32)
    x -= np.convolve(x, np.ones(9, np.float32) / 9, "same")   # 고역만 남긴다
    return x * env_ad(n, 0.001, 0.999, 7.0) * gain


def clap(gain=0.8, seed=5):
    rng = np.random.default_rng(seed)
    n = int(0.32 * SR)
    y = np.zeros(n, np.float32)
    for i, off in enumerate((0.0, 0.012, 0.026)):
        m = int(0.10 * SR)
        burst = rng.normal(0, 1, m).astype(np.float32) * env_ad(m, 0.001, 0.999, 6.0)
        o = int(off * SR)
        y[o:o + m] += burst * (0.7 + 0.15 * i)
    y -= np.convolve(y, np.ones(15, np.float32) / 15, "same")
    return y * gain


def note(freq, dur, gain=0.5, timbre="ep", a=0.012):
    """싸구려 전자피아노/차임/삼각파 음 하나."""
    n = int(dur * SR)
    tt = np.arange(n) / SR
    if timbre == "ep":
        x = (np.sin(2 * np.pi * freq * tt) * 1.0 +
             np.sin(2 * np.pi * freq * 2 * tt) * 0.28 * np.exp(-tt * 5) +
             np.sin(2 * np.pi * freq * 3 * tt) * 0.10 * np.exp(-tt * 7))
        x *= env_ad(n, a, dur - a, 3.2)
    elif timbre == "glock":
        x = (np.sin(2 * np.pi * freq * tt) +
             0.35 * np.sin(2 * np.pi * freq * 3.98 * tt) * np.exp(-tt * 9))
        x *= env_ad(n, 0.002, dur, 5.5)
    elif timbre == "tri":
        x = 2 / np.pi * np.arcsin(np.sin(2 * np.pi * freq * tt))
        x *= env_ad(n, 0.02, dur, 2.2)
    else:  # organ/saw-ish for airhorn류
        x = sum(np.sign(np.sin(2 * np.pi * freq * k * tt)) / k for k in (1, 2, 3))
        x *= env_ad(n, 0.01, dur, 1.2)
    return (x * gain).astype(np.float32)


NOTE_FREQ = {}


def nf(name):
    """'C4' 같은 음이름을 주파수로."""
    if name in NOTE_FREQ:
        return NOTE_FREQ[name]
    names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
    key = names.index(name[:-1])
    octave = int(name[-1])
    freq = 440.0 * 2 ** ((key - 9) / 12 + (octave - 4))
    NOTE_FREQ[name] = freq
    return freq


def rain_noise(dur, gain=0.30, seed=99):
    n = int(dur * SR)
    x = np.random.default_rng(seed).normal(0, 1, n).astype(np.float32)
    x = np.convolve(x, np.ones(6, np.float32) / 6, "same")
    return x * gain
