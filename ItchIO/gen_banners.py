# -*- coding: utf-8 -*-
"""컴스톡 itch.io 설명용 픽셀아트 삽화 생성기

레퍼런스: https://itch.io/jam/gbjam-14

레퍼런스에서 가져오는 것은 **두 가지**다.

1. **그림 스타일** - 게임보이식 4색 제한 팔레트 픽셀아트. 한 장에 소재 하나.
2. **레이아웃** - 그림은 페이지에 홀로 놓이지 않고 **카드 안에** 들어간다.
   그래서 크기가 자리마다 다르다(카드 위 삽화 / 옆에 붙는 238px / 96x96 정사각 아이콘 /
   가로로 흐르는 갤러리). 이 파일은 그 자리 규격에 맞춰 뽑는다.

변환 4단계:
  1. 작은 픽셀 격자로 축소(LANCZOS) - 여기서 픽셀 크기가 정해진다
  2. 밝기를 4색 팔레트로 양자화
  3. 실루엣을 1px 부풀려 **어두운 외곽선** - ★ 빼면 형태가 통째로 뭉개진다
     (양자화가 명암 대비를 눌러 배경과 실루엣 경계를 지운다)
  4. NEAREST로 2배 확대 - 픽셀 계단을 살린 채 키운다

**CSS로 늘리지 말 것.** 늘리면 계단이 뭉개진다.
"""
import glob, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

RES = "C:/Project/ComstockPJ/Assets/Resources/"
OUT = "C:/Project/ComstockPJ/ItchIO/images/"

# 게임보이가 4단계 녹색이었던 자리에 itch 테마 주황 4단계를 넣는다(어두움 → 밝음)
PAL = [(23, 16, 14, 255),      # 외곽선 / 가장 어두운 그늘
       (138, 59, 34, 255),     # 그늘
       (250, 92, 47, 255),     # 기본 #FA5C2F
       (255, 210, 155, 255)]   # 하이라이트
FB = "C:/Windows/Fonts/consolab.ttf"
SCALE = 2                      # 논리 픽셀 → 표시 픽셀


# ---------------------------------------------------------------- 변환
def pixelate(path, target_h, athr=110):
    """스프라이트를 작은 격자로 줄이고 밝기를 4단계로 양자화한다."""
    im = Image.open(RES + path).convert("RGBA")
    tw = max(1, round(im.width * target_h / im.height))
    im = im.resize((tw, target_h), Image.LANCZOS)
    src = im.load()
    out = Image.new("RGBA", (tw, target_h), (0, 0, 0, 0))
    dst = out.load()
    for y in range(target_h):
        for x in range(tw):
            r, g, b, a = src[x, y]
            if a < athr:                       # 반투명 가장자리는 잘라낸다(하드 엣지)
                continue
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            dst[x, y] = PAL[min(3, int(lum / 256 * 4))]
    return out


def outline(sp, w=1):
    """실루엣을 w픽셀 부풀려 어두운 테두리를 두른다."""
    pad = w + 1
    big = Image.new("RGBA", (sp.width + pad * 2, sp.height + pad * 2), (0, 0, 0, 0))
    big.alpha_composite(sp, (pad, pad))
    a = big.split()[3].point(lambda v: 255 if v > 0 else 0)
    for _ in range(w):
        a = a.filter(ImageFilter.MaxFilter(3))
    base = Image.new("RGBA", big.size, (0, 0, 0, 0))
    base.paste(Image.new("RGBA", big.size, PAL[0]), (0, 0), a)
    base.alpha_composite(big)
    return base


def art(path, h, ol=1):
    return outline(pixelate(path, h), ol)


# ---------------------------------------------------------------- 캔버스
def canvas(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def put(img, sp, cx, cy):
    img.alpha_composite(sp, (int(cx - sp.width / 2), int(cy - sp.height / 2)))


def fit(img, sp, cx, cy, box):
    """정사각 아이콘 칸(box)에 넘치지 않게 줄여 넣는다."""
    m = max(sp.width, sp.height)
    if m > box:
        s = box / m
        sp = sp.resize((max(1, int(sp.width * s)), max(1, int(sp.height * s))), Image.NEAREST)
    put(img, sp, cx, cy)


def pixtext(img, xy, s, size, color, thr=110):
    """안티에일리어싱 없이(임계값 처리) 글자를 찍는다 - 픽셀아트와 결이 맞게."""
    f = ImageFont.truetype(FB, size)
    m = Image.new("L", img.size, 0)
    ImageDraw.Draw(m).text(xy, s, font=f, fill=255, anchor="mm")
    m = m.point(lambda v: 255 if v >= thr else 0)
    img.paste(Image.new("RGBA", img.size, color), (0, 0), m)


def pixbox(img, x, y, w, h, fill=PAL[1], edge=PAL[0]):
    """1픽셀 테두리를 가진 납작한 사각형(키캡용)."""
    ImageDraw.Draw(img).rectangle([x, y, x + w - 1, y + h - 1], fill=fill, outline=edge, width=1)


def save(img, name, trim=True, pad=3):
    """`trim=True`면 세로 여백만 잘라 낸다.

    가로는 캔버스 폭을 그대로 둔다(가운데 정렬이라 좌우 여백은 안 보이고, 폭이 같아야
    카드 안에서 정렬이 흔들리지 않는다). 정사각 아이콘(96x96)은 자리 규격이 고정이라
    `trim=False`로 그대로 둔다.
    """
    if trim:
        bb = img.split()[3].getbbox()
        if bb:
            img = img.crop((0, max(0, bb[1] - pad), img.width, min(img.height, bb[3] + pad)))
    img.resize((img.width * SCALE, img.height * SCALE), Image.NEAREST).save(OUT + name)
    print("  %-18s %dx%d" % (name, img.width * SCALE, img.height * SCALE))


ICON = 48          # 정사각 아이콘 논리 크기 → 표시 96x96


def icon(name, path, box=42):
    """96x96 정사각 아이콘 한 장(빌드 목록 / 적 갤러리용)."""
    img = canvas(ICON, ICON)
    fit(img, art(path, box), ICON // 2, ICON // 2, box)
    save(img, name, trim=False)


# ================================================================ 그림들
# -- 인트로: 로봇 + 이름 (카드 왼쪽에 붙는다) ------------------------------
def title():
    img = canvas(100, 118)
    put(img, art("Comstock.png", 70), 50, 42)
    pixtext(img, (50, 96), "COMSTOCK", 14, PAL[3])
    save(img, "01_title.png")


# -- 조작 카드 2장 ---------------------------------------------------------
def move():
    img = canvas(70, 62)
    ks, kg, cx, top = 20, 3, 35, 8
    pixbox(img, cx - ks // 2, top, ks, ks)
    for i in range(3):
        pixbox(img, cx - ks // 2 + (i - 1) * (ks + kg), top + ks + kg, ks, ks)
    for c, x, y in [("W", cx, top + 10), ("A", cx - ks - kg, top + ks + kg + 10),
                    ("S", cx, top + ks + kg + 10), ("D", cx + ks + kg, top + ks + kg + 10)]:
        pixtext(img, (x, y), c, 13, PAL[3])
    save(img, "10_move.png")


def roll():
    img = canvas(70, 62)
    put(img, art("UI/Dash_skill.png", 30), 35, 20)
    pixbox(img, 35 - 30, 42, 60, 14)
    pixtext(img, (35, 49), "SPACE", 9, PAL[3])
    save(img, "11_roll.png")


# -- 웨이브: 보스 (카드 옆에 붙는 180px) -----------------------------------
def boss():
    img = canvas(90, 88)
    put(img, art("BossMove/boss_idle_0.png", 80), 45, 44)
    save(img, "20_boss.png")


# -- 런 루프 카드 4장 ------------------------------------------------------
def loop_battle():
    img = canvas(56, 52)
    put(img, art("Zombie.png", 38), 38, 28)
    put(img, art("RightHMG.png", 26), 16, 30)
    save(img, "30_battle.png")


def loop_core():
    img = canvas(56, 52)
    put(img, art("Exp.png", 38), 28, 26)
    save(img, "31_core.png")


def loop_bay():
    img = canvas(56, 52)
    put(img, art("Parts/Body.png", 32), 22, 24)
    put(img, art("PartIcons/Leg_Spider.png", 26), 40, 34)
    save(img, "32_bay.png")


def loop_shop():
    img = canvas(56, 52)
    put(img, art("ItemBox.png", 34), 24, 30)
    put(img, art("Gold.png", 22), 43, 38)
    save(img, "33_shop.png")


# -- 빌드 목록 96x96 아이콘 5개 --------------------------------------------
def build_icons():
    icon("40_sockets.png", "PartIcons/Socket_Universal.png")
    icon("41_modding.png", "PartIcons/Helmet_IronHelmet.png")

    # 등급 5단계는 스프라이트가 없어 직접 그린다 - 낮은 칸부터 높아지는 막대.
    img = canvas(ICON, ICON)
    bw, gap, base = 6, 3, 42
    for i in range(5):
        h = 8 + i * 6
        x = 5 + i * (bw + gap)
        col = PAL[2] if i == 4 else PAL[1]
        pixbox(img, x, base - h, bw, h, fill=col)
    save(img, "42_grades.png", trim=False)

    # 위험한 조합 = 근접 무기 한 자루. 48px 칸에 두 자루를 넣으면 둘 다 뭉갠다
    # (소켓 두 개 → "삼각형 두 개"로만 보였고, 회전은 픽셀아트를 통째로 뭉갰다).
    # ★ 레퍼런스의 아이콘도 전부 소재 하나짜리다. 뜻은 옆 글이 지고 아이콘은 표식만 한다.
    icon("43_risky.png", "Machete.png", box=40)

    disc = sorted(glob.glob(RES + "Discs/*.png"))[2].replace(RES, "").replace("\\", "/")
    icon("44_disc.png", disc)


# -- 적 갤러리 96x96 아이콘 6개 --------------------------------------------
def enemy_icons():
    for name, path in [("50_zombie.png", "Zombie.png"),
                       ("51_charger.png", "Charger.png"),
                       ("52_sprinter.png", "Sprinter.png"),
                       ("53_spitter.png", "Spitter.png"),
                       ("54_disruptor.png", "Disruptor.png"),
                       ("55_leader.png", "Leader.png")]:
        icon(name, path)


os.makedirs(OUT, exist_ok=True)
# ★ 이 폴더에는 gen_message_art.py가 만드는 60~73번 그림도 함께 산다.
#   `*.png`를 전부 지우면 그쪽 결과물까지 날아가므로 이 스크립트의 번호대(01~55)만 지운다.
for f in glob.glob(OUT + "*.png"):
    b = os.path.basename(f)
    if b[:2].isdigit() and int(b[:2]) < 60:
        os.remove(f)
print("인트로");   title()
print("조작");     move(); roll()
print("웨이브");   boss()
print("런 루프");  loop_battle(); loop_core(); loop_bay(); loop_shop()
print("빌드");     build_icons()
print("적");       enemy_icons()
