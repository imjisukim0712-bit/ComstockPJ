# -*- coding: utf-8 -*-
"""컴스톡 광고 패러디 세로 숏츠 V3 - 한/영 3종.

패러디 대상: 초강력 테이프 인포머셜 / 과장된 남성 바디워시 원테이크 / 미니멀 테크 키노트.
실제 상표·인물·로고·원문 카피는 쓰지 않고 광고 문법과 박자만 패러디한다.
"""
from __future__ import annotations

import argparse
import array
import hashlib
import importlib.util
import io
import json
import math
import random
import re
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
BASE_SCRIPT = ROOT / "output" / "comstock-shorts-3x2-v2" / "render_shorts_v2.py"
spec = importlib.util.spec_from_file_location("shorts_base", BASE_SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("V2 공용 렌더 모듈을 읽을 수 없습니다")
B = importlib.util.module_from_spec(spec)
sys.modules["shorts_base"] = B
spec.loader.exec_module(B)

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

W, H = B.W, B.H
OUT_W, OUT_H = B.OUT_W, B.OUT_H
FPS, DUR, FRAMES = B.FPS, B.DUR, B.FRAMES
END_AT = 12.6
FFMPEG = B.FFMPEG
RES = B.RES
HERO = B.HERO
ENDCARD = HERE / "endcard_source.png"
B.ENDCARD = ENDCARD
LANCZOS, BILINEAR = B.LANCZOS, B.BILINEAR

LANG = {
    "en": {"cta": "PLAY IT BEFORE THE ZOMBIES DO."},
    "ko": {"cta": "좀비보다 먼저 플레이하세요."},
}

COPY = {
    "tape": {
        "en": {
            "hook": "I SAWED THIS ZOMBIE\nIN HALF!",
            "prove": "TO PROVE THE POWER OF\nCOMSTOCK TAPE!",
            "lot": "THAT'S A LOT OF UNDEAD.",
            "oops": "IT MULTIPLIED.",
            "script": "THAT'S NOT IN THE SCRIPT.",
            "fixed": "FIXED.",
            "product": "COMSTOCK TAPE",
            "tag": "FIXES THE PROBLEM.\nNOT THE ZOMBIE.",
        },
        "ko": {
            "hook": "이 좀비를\n반으로 잘랐습니다!",
            "prove": "컴스톡 테이프의 성능을\n증명하기 위해서죠!",
            "lot": "언데드가 아주 많군요.",
            "oops": "두 마리가 됐습니다.",
            "script": "이건 대본에 없는데.",
            "fixed": "수리 완료.",
            "product": "컴스톡 테이프",
            "tag": "좀비 말고\n문제를 고칩니다.",
        },
    },
    "bodywash": {
        "en": {
            "look": "LOOK AT YOUR ZOMBIE.",
            "back": "NOW BACK TO ME.",
            "gone": "THE ZOMBIE IS GONE.",
            "returned": "HE'S BACK.",
            "missile": "I'M ON A MISSILE.",
            "realgone": "NOW HE'S GONE.",
            "product": "COMSTOCK",
            "scent": "EAU DE GUNPOWDER",
            "tag": "SMELLS LIKE SURVIVAL.",
        },
        "ko": {
            "look": "당신의 좀비를 보세요.",
            "back": "다시 저를 보세요.",
            "gone": "좀비가 사라졌습니다.",
            "returned": "다시 왔네요.",
            "missile": "저는 미사일 위에 있습니다.",
            "realgone": "이젠 진짜 사라졌습니다.",
            "product": "컴스톡",
            "scent": "오 드 화약",
            "tag": "생존의 향기.",
        },
    },
    "keynote": {
        "en": {
            "thing": "ONE MORE THING.",
            "gun": "ONE MORE GUN.",
            "courage": "COURAGE.",
            "price": "STARTING AT\n$0",
            "demo": "LIVE DEMO",
            "ovation": "STANDING OVATION.",
            "sold": "SOLD OUT",
            "included": "AUDIENCE INCLUDED.",
        },
        "ko": {
            "thing": "한 가지 더.",
            "gun": "총 한 개 더.",
            "courage": "용기입니다.",
            "price": "시작 가격\n0원",
            "demo": "라이브 시연",
            "ovation": "기립박수.",
            "sold": "전석 매진",
            "included": "관객 포함.",
        },
    },
}


def high_zombie(cnv: Image.Image, x: float, y: float, h: int, flip=False,
                rotate=0.0, alpha=1.0) -> Image.Image:
    im = B.sprite("Enemy_zombie.png", height=h, flip=flip, rotate=rotate)
    B.paste(cnv, im, x, y, "cb", alpha=alpha)
    return im


def shadow(cnv: Image.Image, x: float, y: float, w: float, h: float = 34,
           alpha=90) -> None:
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.ellipse((x - w / 2, y - h / 2, x + w / 2, y + h / 2), fill=(0, 0, 0, alpha))
    cnv.alpha_composite(lay.filter(ImageFilter.GaussianBlur(12)))


def glossy_caption(cnv: Image.Image, s: str, lang: str, y: int, color=(255, 222, 48),
                   max_size=70) -> None:
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rounded_rectangle((28, y - 64, W - 28, y + 64), 20,
                        fill=(7, 12, 22, 225), outline=(255, 255, 255, 210), width=4)
    d.line((48, y - 48, W - 48, y - 48), fill=(255, 255, 255, 55), width=3)
    B.text(cnv, s, lang, (W / 2, y), W - 90, 112, max_size, fill=color, stroke=6)


def saw_line(cnv: Image.Image, x: float, y: float, w: float, t: float) -> None:
    d = ImageDraw.Draw(cnv)
    pts = []
    for i in range(17):
        xx = x - w / 2 + i * w / 16
        yy = y + (12 if i % 2 else -12) + math.sin(t * 16 + i) * 4
        pts.append((xx, yy))
    d.line(pts, fill=(42, 42, 46), width=13)
    d.line(pts, fill=(218, 224, 230), width=7)


def tape_roll(cnv: Image.Image, x: float, y: float, r: int, t: float,
              label: str, lang: str) -> None:
    d = ImageDraw.Draw(cnv)
    d.ellipse((x - r, y - r, x + r, y + r), fill=(36, 177, 214),
              outline=(8, 45, 63), width=9)
    d.ellipse((x - r * .47, y - r * .47, x + r * .47, y + r * .47),
              fill=(238, 235, 217), outline=(8, 45, 63), width=7)
    d.arc((x - r + 16, y - r + 16, x + r - 16, y + r - 16),
          start=int(t * 150) % 360, end=(int(t * 150) + 120) % 360,
          fill=(255, 255, 255), width=8)
    B.text(cnv, label, lang, (x, y), int(r * 1.4), int(r * .7), max(18, int(r * .28)),
           fill=(8, 45, 63), stroke=0, kind="bold", shadow=False)


def split_zombie(cnv: Image.Image, x: float, y: float, h: int, gap: float, t: float) -> None:
    im = B.sprite("Enemy_zombie.png", height=h)
    cut = im.width // 2
    left = im.crop((0, 0, cut, im.height))
    right = im.crop((cut, 0, im.width, im.height))
    B.paste(cnv, left, x - gap / 2, y, "rb")
    B.paste(cnv, right, x + gap / 2, y, "lb")
    saw_line(cnv, x, y - h * .45, min(420, im.width + gap), t)


def tape_frame(t: float, lang: str) -> Image.Image:
    C = COPY["tape"][lang]
    cnv = Image.new("RGBA", (W, H), (15, 55, 96, 255))
    d = ImageDraw.Draw(cnv, "RGBA")
    # 값싼 인포머셜 세트: 파랑 그라디언트, 흰 격자, 노란 가격띠.
    for y in range(H):
        q = y / H
        d.line((0, y, W, y), fill=(15, int(55 + 55 * q), int(96 + 70 * q), 255))
    for x in range(0, W, 54):
        d.line((x, 0, x, H), fill=(255, 255, 255, 18), width=2)
    for y in range(0, H, 54):
        d.line((0, y, W, y), fill=(255, 255, 255, 18), width=2)

    if t < 1.2:
        gap = B.ease_out(t / 0.65) * 115
        shadow(cnv, 270, 850, 360)
        split_zombie(cnv, 270, 875, 610, gap, t)
        glossy_caption(cnv, C["hook"], lang, 145, (255, 226, 53), 66)
        d.rectangle((0, 790, W, 960), fill=(214, 27, 45, 210))
        B.text(cnv, "REAL ZOMBIE*" if lang == "en" else "실제 좀비*", lang,
               (270, 918), 430, 45, 31, fill=(255, 255, 255), stroke=3, kind="bold")
    elif t < 2.35:
        lt = t - 1.2
        B.burst(cnv, (270, 520), ((20, 97, 157), (26, 177, 215), (246, 207, 42)), 30, lt * .2)
        tape_roll(cnv, 270, 520, int(180 * (.45 + .55 * B.ease_out(lt / .22))), lt,
                  "COMSTOCK\nTAPE" if lang == "en" else "컴스톡\n테이프", lang)
        glossy_caption(cnv, C["prove"], lang, 165, (255, 255, 255), 55)
        d.polygon([(0, 800), (540, 750), (540, 960), (0, 960)], fill=(241, 202, 34, 235))
        B.text(cnv, "NOW WITH MORE TAPE" if lang == "en" else "테이프 함량 증가", lang,
               (270, 875), 470, 55, 38, fill=(20, 56, 85), stroke=2, kind="bold")
    elif t < 3.75:
        lt = t - 2.35
        gap = 110 * (1 - B.ease_out(lt / .4))
        split_zombie(cnv, 270, 875, 610, gap, lt)
        # 파란 테이프가 세로로 쾅 붙는다.
        if lt > .25:
            d.rounded_rectangle((235, 260, 305, 900), 12, fill=(35, 183, 220),
                                outline=(7, 55, 75), width=7)
            for yy in range(280, 890, 55):
                d.line((245, yy, 295, yy + 22), fill=(130, 228, 245, 100), width=4)
        glossy_caption(cnv, C["lot"], lang, 145, (255, 229, 54), 59)
        B.speed_lines(cnv, (270, 560), lt, (255, 255, 255, 100), 36)
    elif t < 5.15:
        lt = t - 3.75
        B.burst(cnv, (270, 570), ((16, 79, 133), (39, 177, 210), (236, 57, 58)), 34, lt * .18)
        # 붙인 결과가 복구가 아니라 완전한 좀비 두 마리로 증식.
        spread = 150 + B.ease_out(lt / .28) * 80
        high_zombie(cnv, 270 - spread / 2, 900, 460, flip=False, rotate=-5)
        high_zombie(cnv, 270 + spread / 2, 900, 460, flip=True, rotate=5)
        d.rounded_rectangle((215, 340, 325, 815), 12, fill=(35, 183, 220, 230),
                            outline=(7, 55, 75), width=6)
        glossy_caption(cnv, C["oops"], lang, 150, (255, 225, 54), 66)
        B.text(cnv, "×2", "en", (270, 760), 180, 80, 65, fill=(255, 255, 255), stroke=7)
    elif t < 6.15:
        lt = t - 5.15
        # 진행자 좀비가 카메라를 보는 죽은 정적.
        d.rectangle((0, 0, W, H), fill=(25, 73, 112))
        high_zombie(cnv, 270, 900, 650, rotate=math.sin(lt * 5) * 1)
        glossy_caption(cnv, C["script"], lang, 140, (255, 255, 255), 54)
        B.text(cnv, "...", "en", (270, 740), 260, 100, 86, fill=(255, 226, 54), stroke=5)
    elif t < 9.75:
        lt = t - 6.15
        B.burst(cnv, (270, 620), ((40, 20, 73), (147, 37, 173), (244, 82, 42)), 32, lt * .12)
        high_zombie(cnv, 95, 910, 360, rotate=-7)
        high_zombie(cnv, 445, 910, 360, flip=True, rotate=7)
        B.draw_hero(cnv, 270, 925, 470, lt, rotate=math.sin(lt * 19) * 4)
        B.muzzle(cnv, 510, 590 + math.sin(lt * 19) * 45, lt, 190)
        B.speed_lines(cnv, (270, 620), lt, (255, 225, 70, 165), 56)
        for i, (x, y, at) in enumerate(((85, 650, .05), (450, 700, .45), (120, 820, .85),
                                        (420, 570, 1.3), (180, 735, 1.8), (385, 830, 2.25))):
            B.explosion(cnv, x, y, lt, at, 240 + i % 2 * 40)
        if lt > 2.45:
            glossy_caption(cnv, C["fixed"], lang, 150, (255, 225, 54), 82)
    else:
        lt = t - 9.75
        B.burst(cnv, (270, 500), ((17, 78, 132), (28, 176, 214), (246, 204, 42)), 32, lt * .08)
        tape_roll(cnv, 270, 425, 180, lt, C["product"], lang)
        d.rounded_rectangle((35, 655, 505, 855), 26, fill=(7, 22, 36, 230),
                            outline=(255, 255, 255), width=5)
        B.text(cnv, C["tag"], lang, (270, 750), 420, 160, 57,
               fill=(255, 228, 54), stroke=5)
        B.text(cnv, "*DO NOT APPLY TO ZOMBIES" if lang == "en" else "*좀비에게 붙이지 마십시오", lang,
               (270, 915), 470, 35, 22, fill=(255, 255, 255), stroke=2, kind="bold")
    return cnv.convert("RGB")


def bubbles(cnv: Image.Image, seed: int, count=26) -> None:
    rng = random.Random(seed)
    d = ImageDraw.Draw(cnv, "RGBA")
    for _ in range(count):
        x, y = rng.randrange(W), rng.randrange(H)
        r = rng.randrange(6, 28)
        d.ellipse((x-r, y-r, x+r, y+r), fill=(255,255,255,35), outline=(255,255,255,95), width=2)


def towel(cnv: Image.Image, x: float, y: float, w=180, h=145) -> None:
    d = ImageDraw.Draw(cnv)
    d.rounded_rectangle((x-w/2, y-h, x+w/2, y), 16, fill=(245, 246, 240),
                        outline=(173, 180, 181), width=5)
    for yy in range(int(y-h+18), int(y-10), 22):
        d.line((x-w/2+12, yy, x+w/2-12, yy), fill=(205, 211, 210), width=2)


def missile(cnv: Image.Image, x: float, y: float, scale: float, angle=-12) -> None:
    im = Image.new("RGBA", (230, 720), (0,0,0,0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((55, 90, 175, 600), 58, fill=(225, 229, 232), outline=(37,43,49), width=8)
    d.polygon([(55, 155),(115, 18),(175,155)], fill=(241,87,43), outline=(37,43,49))
    d.polygon([(55, 500),(10, 650),(80, 610)], fill=(241,87,43), outline=(37,43,49))
    d.polygon([(175, 500),(220, 650),(150,610)], fill=(241,87,43), outline=(37,43,49))
    d.ellipse((78, 250, 152, 324), fill=(68,155,210), outline=(37,43,49), width=6)
    d.polygon([(72,590),(115,710),(158,590)], fill=(255,206,42))
    im = im.resize((int(im.width*scale), int(im.height*scale)), LANCZOS).rotate(angle, BILINEAR, expand=True)
    B.paste(cnv, im, x, y)


def bodywash_frame(t: float, lang: str) -> Image.Image:
    C = COPY["bodywash"][lang]
    cnv = Image.new("RGBA", (W,H), (18,75,88,255))
    d = ImageDraw.Draw(cnv, "RGBA")
    if t < 1.45:
        # 푸른 타일 욕실 + 수건 두른 로봇.
        for x in range(0,W,90): d.line((x,0,x,H), fill=(180,230,232,65), width=3)
        for y in range(0,H,90): d.line((0,y,W,y), fill=(180,230,232,65), width=3)
        bubbles(cnv, 103, 34)
        high_zombie(cnv, 390, 900, 540, flip=True, rotate=4)
        B.draw_hero(cnv, 175, 905, 430, t)
        towel(cnv, 175, 905, 180, 150)
        glossy_caption(cnv, C["look"], lang, 135, (255,255,255), 60)
        B.text(cnv, "BODY WASH AD" if lang=="en" else "바디워시 광고", lang,
               (270,925), 440, 35, 23, fill=(190,241,238), stroke=2, kind="bold")
    elif t < 2.65:
        lt=t-1.45
        # 매치 줌: 좀비가 화면을 채웠다가 로봇으로 스냅.
        if lt < .48:
            high_zombie(cnv,270,960,850,rotate=math.sin(lt*10)*2)
        else:
            bubbles(cnv,104,28)
            B.draw_hero(cnv,270,920,650,lt,bob=False)
            towel(cnv,270,920,240,175)
        glossy_caption(cnv,C["back"],lang,135,(255,229,55),62)
    elif t < 3.75:
        lt=t-2.65
        # 말도 안 되는 해변 순간이동.
        d.rectangle((0,0,W,610),fill=(80,188,218))
        d.ellipse((60,60,170,170),fill=(255,222,82))
        for y in range(610,850,34):
            d.line((0,y,W,y+math.sin(y)*5),fill=(232,247,247,160),width=12)
        d.rectangle((0,820,W,H),fill=(236,198,121))
        B.draw_hero(cnv,270,920,470,lt)
        towel(cnv,270,920,190,150)
        B.text(cnv,C["gone"],lang,(270,145),470,80,59,fill=(255,255,255),stroke=6)
    elif t < 4.75:
        lt=t-3.75
        # 해변 배경 그대로 뒤에서 좀비가 다시 올라온다.
        d.rectangle((0,0,W,610),fill=(80,188,218)); d.rectangle((0,820,W,H),fill=(236,198,121))
        high_zombie(cnv,405,900,480,flip=True,rotate=8)
        B.draw_hero(cnv,190,920,430,lt)
        towel(cnv,190,920,175,145)
        glossy_caption(cnv,C["returned"],lang,140,(255,225,54),68)
    elif t < 6.15:
        lt=t-4.75
        # 구름 위 미사일, 여전히 수건 차림.
        d.rectangle((0,0,W,H),fill=(63,128,188))
        for i in range(8):
            x=(i*95+int(lt*40))%700-80; y=660+(i%3)*55
            d.ellipse((x-90,y-60,x+120,y+85),fill=(240,246,248,210))
        missile(cnv,270,610,0.78,-10)
        B.draw_hero(cnv,235,490,310,lt,rotate=-7)
        towel(cnv,235,490,130,100)
        B.text(cnv,C["missile"],lang,(270,135),480,110,60,fill=(255,255,255),stroke=7)
        B.speed_lines(cnv,(270,650),lt,(255,255,255,100),34)
    elif t < 7.35:
        lt=t-6.15
        # 미사일이 좀비 무리로 낙하.
        d.rectangle((0,0,W,H),fill=(42,45,61))
        for i in range(9): B.draw_zombie(cnv,30+i*62,930,220,lt+i*.1,flip=i%2==0)
        y=50+B.ease_in(lt/1.2)*850
        missile(cnv,270,y,0.56,5+lt*40)
        B.speed_lines(cnv,(270,y),lt,(255,210,60,150),50)
    elif t < 9.65:
        lt=t-7.35
        B.burst(cnv,(270,620),((35,25,65),(196,48,77),(245,122,38)),32,lt*.1)
        B.explosion(cnv,270,610,lt,0,650)
        if lt>.35:
            B.draw_hero(cnv,270,930,460,lt)
            towel(cnv,270,930,190,150)
        B.text(cnv,C["realgone"],lang,(270,145),480,120,64,fill=(255,255,255),stroke=7)
    else:
        lt=t-9.65
        # 고급 향수 제품 컷처럼 과하게 진지하게 마무리.
        for y in range(H):
            q=y/H; d.line((0,y,W,y),fill=(int(7+15*q),int(25+55*q),int(32+60*q),255))
        d.ellipse((70,170,470,620),fill=(70,190,180,25),outline=(111,230,214,70),width=5)
        # 제품 병
        d.rounded_rectangle((165,245,375,675),28,fill=(16,33,39),outline=(130,225,211),width=8)
        d.rectangle((215,170,325,275),fill=(25,44,50),outline=(130,225,211),width=7)
        d.rectangle((232,125,308,185),fill=(205,211,207),outline=(40,55,58),width=5)
        B.text(cnv,C["product"],lang,(270,395),170,70,48,fill=(236,240,235),stroke=0,shadow=False)
        B.text(cnv,C["scent"],lang,(270,485),170,100,32,fill=(130,225,211),stroke=0,kind="bold",shadow=False)
        B.text(cnv,C["tag"],lang,(270,770),470,70,50,fill=(255,255,255),stroke=4)
        bubbles(cnv,105,18)
    return cnv.convert("RGB")


def audience(cnv: Image.Image, t: float, rows=4, empty=False, shoes=False) -> None:
    d=ImageDraw.Draw(cnv,"RGBA")
    for row in range(rows):
        y=940-row*115
        scale=170-row*22
        for col in range(7):
            x=35+col*78+(row%2)*18
            if empty:
                if shoes and (col+row)%2==0:
                    d.ellipse((x-18,y-10,x+18,y+10),fill=(107,71,38))
                continue
            B.draw_zombie(cnv,x,y,scale,t+row*.1+col*.04,flip=(col+row)%2==0)


def keynote_slide(cnv: Image.Image, s: str, lang: str, subtitle: str|None=None,
                  dark=False, accent=(40,115,240)) -> None:
    d=ImageDraw.Draw(cnv)
    bg=(9,10,14) if dark else (248,248,246)
    fg=(255,255,255) if dark else (20,20,24)
    d.rectangle((0,0,W,H),fill=bg)
    B.text(cnv,s,lang,(270,360),470,250,94,fill=fg,stroke=0,shadow=False)
    if subtitle:
        B.text(cnv,subtitle,lang,(270,560),440,100,42,fill=accent,stroke=0,kind="bold",shadow=False)


def keynote_frame(t: float, lang: str) -> Image.Image:
    C=COPY["keynote"][lang]
    cnv=Image.new("RGBA",(W,H),(248,248,246,255)); d=ImageDraw.Draw(cnv,"RGBA")
    if t<1.35:
        keynote_slide(cnv,C["thing"],lang,dark=False)
        # 검은 터틀넥 발표자: 하단에 작게 서서 무대를 걷는다.
        x=110+B.ease_out(t/1.35)*110
        B.draw_hero(cnv,x,900,255,t)
        d.rounded_rectangle((x-60,690,x+60,825),25,fill=(15,15,18),outline=(15,15,18))
        d.ellipse((x-4,820,x+4,828),fill=(75,75,80))
    elif t<2.75:
        lt=t-1.35
        keynote_slide(cnv,C["gun"],lang,dark=True,accent=(255,118,35))
        weapon=B.sprite("RightPlasmaCannon.png",width=int(430*(.55+.45*B.ease_out(lt/.25))),rotate=math.sin(lt*2)*3)
        B.paste(cnv,weapon,270,690)
        d.ellipse((90,800,450,880),fill=(255,255,255,18))
    elif t<4.05:
        lt=t-2.75
        d.rectangle((0,0,W,H),fill=(21,22,27))
        audience(cnv,lt,rows=4)
        # 박수처럼 작은 흰 손 모양 대신 수직 박자선.
        for i in range(18):
            x=20+i*31; h=25+int(20*abs(math.sin(lt*12+i)))
            d.line((x,160,x,160-h),fill=(255,255,255,120),width=4)
        B.text(cnv,C["courage"],lang,(270,120),450,90,70,fill=(255,255,255),stroke=6)
    elif t<5.25:
        keynote_slide(cnv,C["price"],lang,dark=False,accent=(40,115,240))
        d.rounded_rectangle((120,675,420,770),22,fill=(40,115,240))
        B.text(cnv,"PRE-ORDER" if lang=="en" else "사전 주문",lang,(270,723),260,60,40,
               fill=(255,255,255),stroke=0,kind="bold",shadow=False)
    elif t<6.25:
        lt=t-5.25
        d.rectangle((0,0,W,H),fill=(248,248,246))
        B.text(cnv,C["demo"],lang,(270,170),450,100,75,fill=(20,20,24),stroke=0,shadow=False)
        d.rounded_rectangle((170,380,370,580),100,fill=(40,115,240),outline=(20,60,130),width=8)
        B.text(cnv,"▶","en",(270,480),120,110,80,fill=(255,255,255),stroke=0,shadow=False)
        if lt>.55:
            d.ellipse((205,415,335,545),fill=(255,255,255,70))
    elif t<9.15:
        lt=t-6.25
        d.rectangle((0,0,W,H),fill=(12,13,18))
        audience(cnv,lt,rows=4)
        B.draw_hero(cnv,270,900,410,lt)
        B.muzzle(cnv,505,600+math.sin(lt*20)*45,lt,190)
        B.speed_lines(cnv,(270,640),lt,(255,205,55,160),58)
        for i,(x,y,at) in enumerate(((70,650,.05),(465,720,.4),(130,820,.8),(410,570,1.2),(230,760,1.65),(470,850,2.1))):
            B.explosion(cnv,x,y,lt,at,230+i%2*50)
        d.rounded_rectangle((145,55,395,125),16,fill=(206,28,47,230))
        B.text(cnv,C["demo"],lang,(270,90),220,50,38,fill=(255,255,255),stroke=2,kind="bold")
    elif t<10.45:
        lt=t-9.15
        d.rectangle((0,0,W,H),fill=(22,23,28))
        audience(cnv,lt,rows=4,empty=True,shoes=True)
        B.draw_hero(cnv,270,900,360,lt)
        B.text(cnv,C["ovation"],lang,(270,170),470,100,65,fill=(255,255,255),stroke=6)
        B.text(cnv,"...", "en",(270,340),280,100,80,fill=(255,208,50),stroke=5)
    else:
        lt=t-10.45
        keynote_slide(cnv,C["sold"],lang,C["included"],dark=False,accent=(206,28,47))
        # 발표자 로봇과 텅 빈 좌석 표식.
        B.draw_hero(cnv,270,900,320,lt)
        for i in range(5):
            d.rounded_rectangle((55+i*90,705,120+i*90,780),12,outline=(150,150,155),width=4)
    return cnv.convert("RGB")


CUTS={
    "tape":(1.2,2.35,3.75,5.15,6.15,9.75,END_AT),
    "bodywash":(1.45,2.65,3.75,4.75,6.15,7.35,9.65,END_AT),
    "keynote":(1.35,2.75,4.05,5.25,6.25,9.15,10.45,END_AT),
}


def raw_frame(concept:str,lang:str,t:float)->Image.Image:
    if t>=END_AT:
        return B.endcard_frame(t-END_AT,lang)
    if concept=="tape": return tape_frame(t,lang)
    if concept=="bodywash": return bodywash_frame(t,lang)
    return keynote_frame(t,lang)


def post(im:Image.Image,concept:str,t:float,frame:int)->Image.Image:
    fl=0.0
    for cut in CUTS[concept]: fl+=math.exp(-((t-cut)/.032)**2)
    if fl>.02: im=Image.blend(im,Image.new("RGB",im.size,(255,255,255)),min(.78,fl*.7))
    action=((concept=="tape" and 6.15<t<9.75) or
            (concept=="bodywash" and 6.15<t<9.65) or
            (concept=="keynote" and 6.25<t<9.15))
    if action:
        rng=random.Random(frame*7907+71); im=ImageChops.offset(im,rng.randint(-7,7),rng.randint(-5,5))
    # 광고 원본처럼 보이게 콘셉트별 후처리 차별화.
    if concept=="tape":
        im=ImageEnhance.Contrast(im).enhance(1.08)
    elif concept=="bodywash":
        im=ImageEnhance.Color(im).enhance(1.14)
    else:
        im=ImageEnhance.Contrast(im).enhance(1.04)
    return im


def frame_at(concept:str,lang:str,t:float)->Image.Image:
    return post(raw_frame(concept,lang,t),concept,t,int(t*FPS))


def render_silent(concept:str,lang:str,out:Path)->None:
    cmd=[FFMPEG,"-y","-hide_banner","-loglevel","error","-f","rawvideo","-pix_fmt","rgb24",
         "-s",f"{W}x{H}","-r",str(FPS),"-i","-","-an","-vf",f"scale={OUT_W}:{OUT_H}:flags=lanczos,format=yuv420p",
         "-c:v","libx264","-preset","medium","-crf","17","-pix_fmt","yuv420p","-r",str(FPS),
         "-t",str(DUR),"-movflags","+faststart",str(out)]
    p=subprocess.Popen(cmd,stdin=subprocess.PIPE); assert p.stdin is not None
    for f in range(FRAMES):
        p.stdin.write(frame_at(concept,lang,f/FPS).tobytes())
        if f%120==0: print(f"  {concept}/{lang} {f:03d}/{FRAMES}",flush=True)
    p.stdin.close()
    if p.wait(): raise RuntimeError(f"비디오 인코딩 실패: {concept}/{lang}")


def build_bed(concept:str,path:Path)->None:
    sr=48000; n=int(sr*DUR); out=array.array("h",[0])*n
    rng=random.Random({"tape":701,"bodywash":702,"keynote":703}[concept])
    bpm={"tape":150,"bodywash":132,"keynote":126}[concept]; beat=60/bpm
    for i in range(n):
        t=i/sr; ph=(t/beat)%1
        if concept=="tape":
            kick=math.exp(-ph*15)*math.sin(math.tau*(62-24*ph)*t)
            brass=.05*sum(math.sin(math.tau*f*t) for f in (130.8,164.8,196.0))
            v=.16*kick+brass*(.45+.55*math.sin(math.tau*2*t)**2)+rng.uniform(-.008,.008)
        elif concept=="bodywash":
            notes=(110,146.8,164.8,220); f=notes[int(t/beat)%4]
            pluck=math.exp(-ph*5)*math.sin(math.tau*f*t)
            shimmer=.025*math.sin(math.tau*660*t)*math.sin(math.tau*.35*t)
            v=.11*pluck+shimmer+rng.uniform(-.004,.004)
        else:
            notes=(261.6,329.6,392,523.3); f=notes[int(t/beat)%4]
            pluck=math.exp(-ph*7)*math.sin(math.tau*f*t)
            v=.10*pluck+.035*math.sin(math.tau*98*t)+rng.uniform(-.003,.003)
        out[i]=max(-32768,min(32767,int(v*32767)))
    with wave.open(str(path),"wb") as wf:
        wf.setnchannels(1); wf.setsampwidth(2); wf.setframerate(sr); wf.writeframes(out.tobytes())


SFX={
"tape":[("UI_Click.wav",1.2,.8),("UI_Click.wav",2.35,.9),("Weapon_Melee.wav",3.0,.8),
        ("LevelUp.wav",3.75,.7),("UI_Click.wav",5.15,.7),("Weapon_Explosive.wav",6.15,1.0),
        ("Weapon_RapidFire.wav",6.35,.5),("Enemy_Death.wav",8.1,.65),("LevelUp.wav",9.75,.8),("LevelUp.wav",12.6,.8)],
"bodywash":[("UI_Click.wav",1.45,.7),("LevelUp.wav",2.65,.65),("UI_Click.wav",3.75,.7),
            ("Weapon_Explosive.wav",4.75,.8),("Weapon_RapidFire.wav",6.15,.25),("Weapon_Explosive.wav",7.35,1.0),
            ("Enemy_Death.wav",8.1,.6),("LevelUp.wav",9.65,.75),("LevelUp.wav",12.6,.8)],
"keynote":[("UI_Click.wav",1.35,.7),("LevelUp.wav",2.75,.65),("UI_Click.wav",4.05,.8),
           ("UI_Click.wav",5.25,.9),("Weapon_Explosive.wav",6.25,.9),("Weapon_RapidFire.wav",6.4,.5),
           ("Enemy_Death.wav",8.0,.65),("UI_Click.wav",9.15,.7),("LevelUp.wav",10.45,.8),("LevelUp.wav",12.6,.8)]}


def mix_audio(silent:Path,bed:Path,concept:str,out:Path)->None:
    schedule=SFX[concept]; cmd=[FFMPEG,"-y","-hide_banner","-loglevel","error","-i",str(silent),"-i",str(bed)]
    for name,_,_ in schedule: cmd += ["-i",str(RES/"SFX"/name)]
    filters=["[1:a]volume=1.12[bed]"]; labels=["[bed]"]
    for idx,(_,at,gain) in enumerate(schedule,start=2):
        lab=f"s{idx}"; delay=int(at*1000)
        filters.append(f"[{idx}:a]volume={gain},adelay={delay}|{delay}[{lab}]"); labels.append(f"[{lab}]")
    filters.append("".join(labels)+f"amix=inputs={len(labels)}:normalize=0:duration=longest,"
                   "acompressor=threshold=0.13:ratio=4:attack=5:release=120:makeup=1.55,"
                   "alimiter=limit=0.94,atrim=0:15,afade=t=out:st=14.82:d=0.18[aout]")
    cmd += ["-filter_complex",";".join(filters),"-map","0:v:0","-map","[aout]","-c:v","copy",
            "-c:a","aac","-b:a","192k","-ar","48000","-ac","2","-t","15","-movflags","+faststart",str(out)]
    subprocess.run(cmd,check=True)


def sha256(path:Path)->str:
    h=hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024),b""): h.update(chunk)
    return h.hexdigest()


def probe(path:Path)->dict:
    p=subprocess.run([FFMPEG,"-hide_banner","-i",str(path)],text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    dm=re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)",p.stderr)
    vm=re.search(r"Video: h264.*?, yuv420p.*?, (\d+)x(\d+).*?, (\d+(?:\.\d+)?) fps",p.stderr)
    if not dm or not vm: raise RuntimeError(f"검증 실패: {path}")
    dur=int(dm.group(1))*3600+int(dm.group(2))*60+float(dm.group(3))
    return {"duration":dur,"width":int(vm.group(1)),"height":int(vm.group(2)),"fps":float(vm.group(3)),"aac_audio":"Audio: aac" in p.stderr}


SHEET_TIMES=[.35,1.35,2.55,3.75,4.85,5.95,7.0,8.2,9.4,10.5,11.7,13.5]


def decoded_frame(path:Path,t:float)->Image.Image:
    p=subprocess.run([FFMPEG,"-hide_banner","-loglevel","error","-ss",str(t),"-i",str(path),"-frames:v","1",
                      "-f","image2pipe","-vcodec","png","-"],stdout=subprocess.PIPE,check=True)
    return Image.open(io.BytesIO(p.stdout)).convert("RGB")


def make_sheet(concept:str)->Path:
    tw,th=270,480; sheet=Image.new("RGB",(1620,1950),(12,12,16)); d=ImageDraw.Draw(sheet)
    for li,lang in enumerate(("en","ko")):
        video=HERE/f"Comstock_ParodyV3_{concept.title()}_{lang.upper()}_15s.mp4"
        for i,t in enumerate(SHEET_TIMES):
            im=decoded_frame(video,t) if video.exists() else frame_at(concept,lang,t)
            im=im.resize((tw,th),LANCZOS); row=li*2+i//6; col=i%6
            sheet.paste(im,(col*tw,30+row*th))
        d.text((8,5+li*960),f"{concept.upper()} — {lang.upper()}",font=B.font("en","bold",18),fill=(255,255,255))
    out=HERE/f"contact-sheet-{concept}.jpg"; sheet.save(out,quality=91); return out


def render_one(concept:str,lang:str)->dict:
    stem=f"Comstock_ParodyV3_{concept.title()}_{lang.upper()}_15s"
    silent=HERE/f"_{stem}_silent.mp4"; bed=HERE/f"_bed_{concept}.wav"; out=HERE/f"{stem}.mp4"
    if not bed.exists(): build_bed(concept,bed)
    render_silent(concept,lang,silent); mix_audio(silent,bed,concept,out); silent.unlink(missing_ok=True)
    info=probe(out)
    if abs(info["duration"]-DUR)>.05 or (info["width"],info["height"])!=(OUT_W,OUT_H) or not info["aac_audio"]:
        raise RuntimeError(f"출력 규격 오류: {out}: {info}")
    return {"concept":concept,"language":lang,"output":out.name,"sha256":sha256(out),"probe":info}


def main()->None:
    ap=argparse.ArgumentParser(); ap.add_argument("--concept",choices=("tape","bodywash","keynote","all"),default="all")
    ap.add_argument("--lang",choices=("en","ko","all"),default="all"); ap.add_argument("--preview",action="store_true"); args=ap.parse_args()
    concepts=("tape","bodywash","keynote") if args.concept=="all" else (args.concept,)
    langs=("en","ko") if args.lang=="all" else (args.lang,)
    if args.preview:
        for c in concepts: print(make_sheet(c))
        return
    exports=[]
    for c in concepts:
        for lang in langs:
            print(f"render: {c}/{lang}"); exports.append(render_one(c,lang))
        make_sheet(c)
    manifest={
      "title":"컴스톡 인지 가능한 광고 패러디 숏츠 V3 3종 × 한/영",
      "format":{"size":[OUT_W,OUT_H],"aspect":"9:16","fps":FPS,"duration_seconds":DUR,"video":"H.264/yuv420p","audio":"AAC 48kHz stereo"},
      "parodies":{"tape":"초강력 테이프 인포머셜 문법","bodywash":"과장된 남성 향수/바디워시 원테이크 문법","keynote":"미니멀 테크 키노트 문법"},
      "trademark_policy":"실제 상표·인물·로고·원문 카피 미사용; 형식과 박자만 패러디",
      "required_copy":{"ko":LANG["ko"]["cta"],"en":LANG["en"]["cta"]},
      "endcard":{"start_seconds":END_AT,"source":ENDCARD.name,"sha256":sha256(ENDCARD)},
      "sources":["dev/pv/assets/comstock_hero.png","Assets/Resources/Enemy_zombie.png","Assets/Resources/ZombieMove/*.png","Assets/Resources/Explosion/*.png","Assets/Resources/MuzzleFlash/*.png","Assets/Resources/Right*.png","Assets/Resources/SFX/*.wav"],
      "exports":exports,"contact_sheets":[f"contact-sheet-{c}.jpg" for c in concepts],"generator":Path(__file__).name}
    (HERE/"manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding="utf-8")
    print("complete")


if __name__=="__main__": main()
