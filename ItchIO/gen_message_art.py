# -*- coding: utf-8 -*-
"""itch.io 설명의 「HOW TO PLAY」 안내와 「FEATURES」 아이콘을 이미지로 뽑는다.

itch.io는 설명 HTML의 인라인 style을 지운다. 그래서 이 두 구역을 인라인 CSS로 칠해 두면
실제 페이지에서는 디자인이 통째로 사라진다. 이미지로 구우면 그 검열을 통과한다.

HOW TO PLAY는 읽기 쉬운 2배 해상도 산세리프 UI이고, FEATURES 아이콘만 아래의 픽셀아트
팔레트 변환을 사용한다.

`gen_banners.py`(GBJAM 레이아웃용 4색 팔레트)와 별개다. 이쪽은 팔레트가 넓다.

## gen_banners.py의 4색 대비 달라진 점

1. **알파 프리멀티플 후 축소** - Pillow는 RGBA를 그냥 섞어서 투명 픽셀(0,0,0,0)의 검정이
   가장자리로 번진다. 곱해 두고 줄인 뒤 되나눠야 테두리가 안 죽는다.
2. **팔레트가 24색**(용도별 램프 5줄) - 명도만 보고 주황 4단계로 눌러 버리면 강철·뼈·감염체가
   전부 같은 색이 된다. 램프를 나눠 재질이 남게 했다.
3. **양자화 전 채도·대비 보정** - 원본이 평평한 카툰 채색이라 그냥 줄이면 단계가 안 갈린다.
4. **림 라이트** - 실루엣 좌상단에 1px 밝은 테두리. 픽셀아트에서 입체감을 만드는 관용구다.
5. **바닥 그림자** - 아이콘이 공중에 뜨지 않게 발밑에 어두운 타원을 깐다.
"""
import os, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance

ROOT = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(os.path.dirname(ROOT), "Assets", "Resources") + os.sep
OUT = os.path.join(ROOT, "images") + os.sep

SCALE = 2  # 논리 픽셀 → 표시 픽셀

# ---------------------------------------------------------------- 팔레트
# message.txt의 테마색(#F07D2E 주황 / #232323 배경 / #4a4358 카드 / #EAE6DA 글자)에
# 맞춘 램프 5줄. 원본 색의 채도·색상으로 램프를 고르고 명도로 단계를 고른다.
OUTLINE = (15, 11, 10)

STEEL = [(43, 39, 49), (74, 67, 88), (114, 107, 128), (160, 154, 173),
         (206, 202, 214), (240, 238, 244)]          # 로봇 몸체 / 총기
ORANGE = [(92, 35, 15), (155, 64, 22), (224, 110, 38), (240, 125, 46),
          (255, 175, 110), (255, 222, 190)]          # 강조색
CREAM = [(79, 62, 48), (130, 108, 82), (181, 163, 131), (220, 208, 182),
         (234, 230, 218), (246, 244, 238)]           # 뼈 / 크림 외장
PURPLE = [(36, 26, 48), (74, 67, 88), (106, 88, 138), (148, 126, 184),
          (186, 168, 214), (214, 202, 232)]          # 감염체 / 보스
OLIVE = [(40, 40, 30), (72, 70, 48), (110, 106, 74), (150, 144, 104),
         (190, 182, 140), (222, 216, 184)]           # 좀비 피부
BLUE = [(18, 40, 60), (34, 74, 108), (60, 124, 168), (100, 176, 216),
        (156, 214, 240), (206, 236, 250)]            # 에너지 / 코어

# 테마 상수(패널을 직접 그릴 때 쓴다)
BG_PANEL = (35, 35, 35)
BG_CARD = (74, 67, 88)
ACCENT = (240, 125, 46)
ACCENT_LT = (255, 193, 153)
INK = (234, 230, 218)
KEYCAP = (233, 228, 214)

F_TITLE = os.path.join(os.path.dirname(ROOT), "Assets", "Fonts", "Orbitron",
                       "Orbitron-Black.ttf")
F_MONO = "C:/Windows/Fonts/consolab.ttf"
F_UI = "C:/Windows/Fonts/segoeui.ttf"
F_UI_BOLD = "C:/Windows/Fonts/segoeuib.ttf"


def _ramp(r, g, b):
    """원본 색의 색상·채도로 어느 램프를 쓸지 고른다."""
    mx, mn = max(r, g, b), min(r, g, b)
    v = mx / 255.0
    s = 0 if mx == 0 else (mx - mn) / mx
    # ★ 임계값 0.07은 낮아 보이지만 근거가 있다. 좀비 피부가 채도 0.10이라
    # 0.13으로 두면 무채색으로 분류돼 강철(차가운 회색)이 되고 시체 느낌이 통째로 사라진다.
    if s < 0.07:
        return STEEL if v < 0.86 else CREAM      # 무채색: 어두우면 강철, 밝으면 크림
    d = mx - mn
    if mx == r:
        h = (60 * ((g - b) / d)) % 360
    elif mx == g:
        h = 60 * ((b - r) / d) + 120
    else:
        h = 60 * ((r - g) / d) + 240
    if 250 <= h <= 320:
        return PURPLE
    if 185 <= h < 250:
        return BLUE
    if 45 <= h < 100:
        return OLIVE if s < 0.45 else ORANGE     # 좀비 카키는 OLIVE, 쨍한 노랑은 ORANGE
    if 15 <= h < 45:
        return CREAM if s < 0.45 else ORANGE     # 뼈·가죽 같은 탁한 갈색은 크림 램프로
    return ORANGE                                # 나머지 난색(빨강 계열)은 테마 주황으로


def downscale(im, h):
    """알파를 곱해 둔 채로 줄인다(투명 픽셀의 검정이 가장자리로 번지는 것을 막는다)."""
    im = im.convert("RGBA")
    a = im.split()[3]
    rgb = Image.merge("RGB", [Image.composite(c, Image.new("L", im.size, 0), a)
                              for c in im.split()[:3]])
    w = max(1, round(im.width * h / im.height))
    rgb = rgb.resize((w, h), Image.LANCZOS)
    a = a.resize((w, h), Image.LANCZOS)
    rp, ap = rgb.load(), a.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    for y in range(h):
        for x in range(w):
            av = ap[x, y]
            if av < 96:                     # 반투명 가장자리는 잘라 하드 엣지로
                continue
            r, g, b = rp[x, y]
            k = 255 / av
            op[x, y] = (min(255, int(r * k)), min(255, int(g * k)), min(255, int(b * k)), 255)
    return out


def pixelate(path, h, sat=1.35, contrast=1.12, force=None):
    src = Image.open(RES + path).convert("RGBA")
    bb = src.split()[3].getbbox()        # ★ 원본 여백을 먼저 잘라야 h가 "그림 높이"가 된다
    if bb:
        src = src.crop(bb)
    sm = downscale(src, h)
    # ★ 램프 선택은 원본 색으로, 단계 선택은 보정한 명도로 한다.
    # 채도를 올린 색으로 램프까지 고르면 보스의 카키 뼈가 임계값을 넘어 통째로 주황이 된다.
    raw = sm.convert("RGB")
    boost = ImageEnhance.Contrast(ImageEnhance.Color(raw).enhance(sat)).enhance(contrast)
    rp, bp, ap = raw.load(), boost.load(), sm.load()
    out = Image.new("RGBA", sm.size, (0, 0, 0, 0))
    op = out.load()
    for y in range(sm.height):
        for x in range(sm.width):
            if ap[x, y][3] == 0:
                continue
            br, bg, bb_ = bp[x, y]
            lum = min(1.0, max(0.0, ((0.299 * br + 0.587 * bg + 0.114 * bb_) / 255 - 0.04) / 0.92))
            ramp = force or _ramp(*rp[x, y])
            op[x, y] = ramp[min(len(ramp) - 1, int(lum * len(ramp)))] + (255,)
    return out


def _mask(sp):
    return sp.split()[3].point(lambda v: 255 if v > 0 else 0)


def outline(sp, w=1, color=OUTLINE):
    pad = w + 1
    big = Image.new("RGBA", (sp.width + pad * 2, sp.height + pad * 2), (0, 0, 0, 0))
    big.alpha_composite(sp, (pad, pad))
    a = _mask(big)
    for _ in range(w):
        a = a.filter(ImageFilter.MaxFilter(3))
    base = Image.new("RGBA", big.size, (0, 0, 0, 0))
    base.paste(Image.new("RGBA", big.size, color + (255,)), (0, 0), a)
    base.alpha_composite(big)
    return base


def rimlight(sp, amount=0.55):
    """실루엣 좌상단 1px을 밝게 - 픽셀아트에서 입체감을 만드는 관용구."""
    a = _mask(sp)
    shifted = Image.new("L", sp.size, 0)
    shifted.paste(a, (1, 1))                       # 우하로 민 마스크
    rim = Image.eval(a, lambda v: v)               # 원본에서 민 것을 빼면 좌상단 테두리만 남는다
    rim = Image.composite(Image.new("L", sp.size, 0), rim, shifted)
    px, rp = sp.load(), rim.load()
    for y in range(sp.height):
        for x in range(sp.width):
            if rp[x, y] < 128:
                continue
            r, g, b, al = px[x, y]
            if al == 0:
                continue
            px[x, y] = (min(255, int(r + (255 - r) * amount)),
                        min(255, int(g + (255 - g) * amount)),
                        min(255, int(b + (255 - b) * amount)), al)
    return sp


def art(path, h, ol=1, rim=True, sat=1.35, force=None):
    sp = pixelate(path, h, sat=sat, force=force)
    if rim:
        sp = rimlight(sp)
    return outline(sp, ol)


# ---------------------------------------------------------------- 그리기 도구
def canvas(w, h, fill=(0, 0, 0, 0)):
    return Image.new("RGBA", (w, h), fill)


def put(img, sp, cx, cy):
    img.alpha_composite(sp, (int(round(cx - sp.width / 2)), int(round(cy - sp.height / 2))))


def put_bottom(img, sp, cx, by):
    img.alpha_composite(sp, (int(round(cx - sp.width / 2)), int(round(by - sp.height))))


def fit(sp, box_w, box_h):
    s = min(box_w / sp.width, box_h / sp.height, 1.0)
    if s < 1.0:
        sp = sp.resize((max(1, int(sp.width * s)), max(1, int(sp.height * s))), Image.NEAREST)
    return sp


def rrect(img, x, y, w, h, r, fill, edge=None, ew=1):
    """모서리를 계단식으로 깎은 사각형(픽셀아트용 둥근 모서리)."""
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([x, y, x + w - 1, y + h - 1], radius=r, fill=fill,
                        outline=edge, width=ew)


def shadow(img, cx, by, w):
    """발밑 그림자 - 폭이 다른 타원 3장을 겹쳐 가장자리를 흐린 것처럼 보이게 한다.
    한 장짜리 3px 타원은 이 크기에서 그냥 회색 막대로 보인다."""
    ov = canvas(img.width, img.height)
    d = ImageDraw.Draw(ov)
    for k, a in ((1.00, 34), (0.72, 40), (0.44, 46)):
        ww = int(w * k)
        d.ellipse([cx - ww // 2, by - 2, cx + ww // 2, by], fill=(10, 8, 8, a))
    img.alpha_composite(ov)


def text(img, xy, s, font_path, size, color, anchor="mm", thr=115, track=0):
    """안티에일리어싱을 임계값으로 날린 글자(픽셀아트와 결이 맞게).

    `track`은 글자 사이 추가 간격(논리 픽셀). Orbitron은 자간이 좁아 작은 크기에서 붙는다.
    """
    f = ImageFont.truetype(font_path, size)
    if track == 0:
        m = Image.new("L", img.size, 0)
        ImageDraw.Draw(m).text(xy, s, font=f, fill=255, anchor=anchor)
    else:
        widths = [f.getbbox(c)[2] - f.getbbox(c)[0] if c != " " else size // 2 for c in s]
        adv = [f.getlength(c) + track for c in s]
        total = sum(adv) - track
        x0 = xy[0] - total / 2 if anchor[0] == "m" else xy[0]
        m = Image.new("L", img.size, 0)
        dm = ImageDraw.Draw(m)
        cx = x0
        for c, a in zip(s, adv):
            dm.text((cx, xy[1]), c, font=f, fill=255, anchor="l" + anchor[1])
            cx += a
    m = m.point(lambda v: 255 if v >= thr else 0)
    img.paste(Image.new("RGBA", img.size, color + (255,)), (0, 0), m)
    return m


def text_w(s, font_path, size, track=0):
    f = ImageFont.truetype(font_path, size)
    if track == 0:
        return f.getlength(s)
    return sum(f.getlength(c) + track for c in s) - track


def wrap(s, font_path, size, max_w):
    f = ImageFont.truetype(font_path, size)
    out, line = [], ""
    for w in s.split(" "):
        t = (line + " " + w).strip()
        if f.getlength(t) <= max_w or not line:
            line = t
        else:
            out.append(line)
            line = w
    if line:
        out.append(line)
    return out


def keycap(img, cx, cy, w, h, label, size=10, track=1):
    """크림색 키캡 + 주황 테두리 + 아래쪽 1px 그림자(눌리는 키의 관용 표현).

    ★ 글자는 반드시 Consolas(모노)로 찍는다. Orbitron은 자소가 기하학적이라 10px 이하에서
    임계값을 먹이면 A가 R로, S가 9로 뭉개진다(실제로 그렇게 나왔다). 큰 제목에만 쓸 것.
    """
    x, y = int(cx - w / 2), int(cy - h / 2)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([x, y + 1, x + w - 1, y + h], radius=2, fill=(120, 62, 26, 255))
    d.rounded_rectangle([x, y, x + w - 1, y + h - 2], radius=2,
                        fill=KEYCAP + (255,), outline=ACCENT + (255,), width=1)
    text(img, (cx, y + (h - 2) / 2), label, F_MONO, size, (35, 30, 30), track=track)


def badge(img, cx, cy, w, h, label, size=10, track=1):
    """주황 배지(AUTO처럼 키가 아닌 것)."""
    x, y = int(cx - w / 2), int(cy - h / 2)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([x, y + 1, x + w - 1, y + h], radius=2, fill=(92, 35, 15, 255))
    d.rounded_rectangle([x, y, x + w - 1, y + h - 2], radius=2,
                        fill=ACCENT + (255,), outline=ACCENT_LT + (255,), width=1)
    text(img, (cx, y + (h - 2) / 2), label, F_MONO, size, (35, 30, 30), track=track)


def save(img, name):
    img.resize((img.width * SCALE, img.height * SCALE), Image.NEAREST).save(OUT + name)
    print("  %-22s %dx%d" % (name, img.width * SCALE, img.height * SCALE))


# ================================================================ HOW TO PLAY
W = 276          # FEATURES용 논리 폭 기준. HOW TO PLAY는 별도의 2배 해상도로 직접 그린다.


def reticle(img, cx, cy, r):
    """자동 조준 표식 - 네 귀퉁이 괄호 두께 2px + 가운데 십자."""
    d = ImageDraw.Draw(img)
    c = ACCENT + (255,)
    arm = max(3, r // 2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = cx + sx * r, cy + sy * r
            d.rectangle([min(x, x - sx * arm), y - 1, max(x, x - sx * arm), y], fill=c)
            d.rectangle([x - 1, min(y, y - sy * arm), x, max(y, y - sy * arm)], fill=c)


def controls():
    # HTML 키캡은 itch.io 저장 과정에서 크기/배경 스타일이 지워진다. 이 안내만큼은
    # 2배 해상도의 매끈한 산세리프 이미지로 굽고 552px 폭으로 축소 표시한다.
    w, h = 1104, 400
    img = canvas(w, h, BG_PANEL + (255,))
    d = ImageDraw.Draw(img)
    orange = ACCENT + (255,)
    cream = KEYCAP + (255,)
    ink = (32, 32, 32, 255)
    white = (244, 242, 236, 255)
    muted = (166, 162, 156, 255)

    d.rounded_rectangle([3, 3, w - 4, h - 4], radius=28,
                        fill=BG_PANEL + (255,), outline=orange, width=6)

    title_font = ImageFont.truetype(F_UI_BOLD, 46)
    label_font = ImageFont.truetype(F_UI_BOLD, 34)
    sub_font = ImageFont.truetype(F_UI, 25)
    key_font = ImageFont.truetype(F_UI_BOLD, 32)
    badge_font = ImageFont.truetype(F_UI_BOLD, 31)

    def centered(x, y, value, font, fill):
        d.text((x, y), value, font=font, fill=fill, anchor="mm")

    def smooth_key(x, y, width, height, value):
        box = [x - width // 2, y - height // 2,
               x + width // 2, y + height // 2]
        d.rounded_rectangle(box, radius=14, fill=cream, outline=orange, width=5)
        centered(x, y - 1, value, key_font, ink)

    centered(w // 2, 50, "HOW TO PLAY", title_font, orange)
    d.rounded_rectangle([w // 2 - 48, 84, w // 2 + 48, 90], radius=3, fill=orange)

    cols = [184, 552, 920]

    # W / A S D
    smooth_key(cols[0], 140, 72, 64, "W")
    for i, value in enumerate("ASD"):
        smooth_key(cols[0] + (i - 1) * 82, 212, 72, 64, value)

    # 자동 사격 / 회피
    d.rounded_rectangle([cols[1] - 76, 150, cols[1] + 76, 214],
                        radius=14, fill=orange)
    centered(cols[1], 181, "AUTO", badge_font, ink)
    smooth_key(cols[2], 182, 188, 64, "SPACE")

    centered(cols[0], 292, "Move", label_font, white)
    centered(cols[1], 292, "Aim & Fire", label_font, white)
    centered(cols[1], 329, "closest enemy, always", sub_font, muted)
    centered(cols[2], 292, "Dodge Roll", label_font, white)

    img.save(OUT + "60_controls.png")
    print("  %-22s %dx%d" % ("60_controls.png", w, h))


# ================================================================ FEATURES
def feature(name, path, h, box=(58, 58), sat=1.35, ground=True):
    """128x128 정사각 아이콘 - 바닥 그림자 + 림 라이트."""
    S = 64
    img = canvas(S, S)
    sp = fit(art(path, h, sat=sat), *box)
    by = S - 5
    if ground:
        shadow(img, S // 2, by + 2, int(sp.width * 0.80))
        put_bottom(img, sp, S // 2, by)
    else:
        put(img, sp, S // 2, S // 2)
    save(img, name)


def features():
    feature("70_guns.png", "RightHMG.png", 48, box=(62, 52), ground=False)
    feature("71_bot.png", "Comstock.png", 54, box=(62, 56))
    feature("72_boss.png", "BossMove/boss_idle_0.png", 56, box=(60, 56), sat=1.6)
    feature("73_cute.png", "Zombie.png", 56, box=(58, 57))


os.makedirs(OUT, exist_ok=True)
print("HOW TO PLAY"); controls()
print("FEATURES"); features()
