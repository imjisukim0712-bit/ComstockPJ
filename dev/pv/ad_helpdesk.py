# -*- coding: utf-8 -*-
"""컴스톡 15초 세로 숏츠 광고 - "고객센터 · 좀비 전용 창구".

★ 웃음 구조
  - 형식: 고객 상담 접수 화면. **불만을 제기하는 쪽이 좀비다**(관점 뒤집기).
  - 민원 3연타가 화면에 **쌓인다** - 답변은 세 번 모두 "정상입니다." 한 마디.
    (게임의 특징을 설명하지 않고 좀비의 불만으로만 전달한다)
  - 상단 모니터에서는 상담 중에도 로봇이 계속 난사하고, **하단 대기 인원이 431 -> 0**으로
    줄어든다. 답변하는 사이에 민원인이 없어진다는 게 마지막 뒤통수다.
  - 세로 9:16이라 글자를 크게 쓰고, 카드가 아래에서 위로 올라오며 쌓이게 배치했다.
"""
from PIL import Image, ImageDraw

import ad_kit as K
from pv_draw import SPR, blit
from pv_scenes import zombie_frame, ROBOT

W, H = 1080, 1920
FPS = 30
NAME = "Helpdesk"

NAVY = (24, 40, 72)
BG = (238, 241, 245)
INK = (28, 30, 36)
BLUE = (46, 108, 214)
YEL = (245, 197, 24)
RED = (206, 38, 38)

TIMELINE = [
    (0.0,  2.0, "open"),
    (2.0,  2.6, "c1"),
    (4.6,  2.6, "c2"),
    (7.2,  2.6, "c3"),
    (9.8,  2.4, "zero"),
    (12.2, 2.8, "cta"),
]
DUR = sum(d for (_t, d, _n) in TIMELINE)
CUTS = [t for (t, _d, _n) in TIMELINE][1:]
# 답변 도장이 찍히는 순간
SHAKES = [(2.0 + 1.35, 6.0), (4.6 + 1.35, 6.0), (7.2 + 1.35, 6.0), (9.8 + 0.55, 9.0)]
VIGNETTE = 0.14

QUEUE = (431, 431, 190, 42, 0)          # open / c1 / c2 / c3 / zero 시점의 대기 인원

LANG = {
    "ko": {
        "head": "컴스톡 고객센터",
        "desk": "좀비 전용 창구",
        "live": "실시간 현장",
        "queue": "대기 중인 좀비",
        "unit": "명",
        "agent": "상담원",
        "reply": "정상입니다.",
        "q1": "총이 너무 많습니다.",
        "q2": "조준을 안 하는데 다 맞습니다.",
        "q3": "재장전을 기다렸는데 안 합니다.",
        "open_fine": "*상담 시간: 무제한 (좀비 측 사정으로 단축될 수 있습니다)",
        "zero_head": "대기 중인 좀비 0명",
        "zero_stamp": "응대 완료",
        "zero_fine": "*민원인 전원 응대가 완료되었습니다.",
        "tag": "좀비보다 먼저 플레이하세요.",
        "url": "pyramid-studio.itch.io/comstock",
        "cta_fine": "*민원은 접수되지 않았습니다.",
    },
    "en": {
        "head": "COMSTOCK SUPPORT",
        "desk": "ZOMBIE DESK",
        "live": "LIVE FEED",
        "queue": "ZOMBIES IN QUEUE",
        "unit": "",
        "agent": "AGENT",
        "reply": "WORKING AS INTENDED.",
        "q1": "There are too many guns.",
        "q2": "It never aims, yet it hits.",
        "q3": "We waited for the reload.",
        "open_fine": "*HOURS: UNLIMITED (MAY BE SHORTENED BY THE ZOMBIES)",
        "zero_head": "ZOMBIES IN QUEUE: 0",
        "zero_stamp": "RESOLVED",
        "zero_fine": "*ALL COMPLAINANTS HAVE BEEN HANDLED.",
        "tag": "PLAY IT BEFORE THE ZOMBIES DO.",
        "url": "pyramid-studio.itch.io/comstock",
        "cta_fine": "*NO COMPLAINT WAS FILED.",
    },
}

MON = (40, 300, 1040, 770)              # 상단 모니터 자리
CARD_Y = (830, 1130, 1430)              # 민원 세트 3개의 y 시작점


# ---------------------------------------------------------------- 공통 틀
def shell(cnv, lang, t, mon_t, alpha_head=1.0):
    """헤더 + 상단 모니터 + 상담 영역 배경."""
    L = LANG[lang]
    cnv.paste(Image.new("RGB", (W, H), BG), (0, 0))
    d = ImageDraw.Draw(cnv)
    d.rectangle([0, 0, W, 258], fill=NAVY)
    d.rectangle([0, 258, W, 266], fill=YEL)
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dl = ImageDraw.Draw(lay)
    fh = K.fit(lang, "head", 64, L["head"], W - 120)
    dl.text((W / 2, 116), L["head"], font=fh, fill=(255, 255, 255, 255), anchor="mm")
    fd = K.fit(lang, "body", 38, L["desk"], W - 200)
    dl.text((W / 2, 190), L["desk"], font=fd, fill=YEL + (255,), anchor="mm")
    K.put(cnv, lay, alpha_head)
    # 상단 모니터 - 상담 중에도 현장은 돌아간다
    K.screen(cnv, MON, K.footage(1000, 470, mon_t, "spray", seed=5), border=(255, 255, 255),
             width=8, off=10)
    lay2 = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d2 = ImageDraw.Draw(lay2)
    d2.rectangle([MON[0] + 16, MON[1] + 16, MON[0] + 196, MON[1] + 62],
                 fill=(20, 20, 24, 210))
    blink = 1.0 if int(t * 2) % 2 == 0 else 0.35
    d2.ellipse([MON[0] + 30, MON[1] + 30, MON[0] + 48, MON[1] + 48],
               fill=(232, 48, 48, int(255 * blink)))
    d2.text((MON[0] + 58, MON[1] + 39), L["live"], font=K.F(lang, "body", 24),
            fill=(255, 255, 255, 240), anchor="lm")
    K.put(cnv, lay2, 1.0)


def queue_bar(cnv, lang, value, alpha=1.0, red=False):
    """맨 아래 대기 인원 표시 - 상담이 진행될수록 줄어든다."""
    if alpha <= 0.01:
        return
    L = LANG[lang]
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rectangle([0, 1706, W, 1780], fill=NAVY + (255,))
    d.text((52, 1743), L["queue"], font=K.F(lang, "body", 34),
           fill=(226, 232, 244, 255), anchor="lm")
    fv = K.F(lang, "num", 62)
    col = (RED if red else YEL) + (255,)
    ux = W - 52
    if L["unit"]:
        fu = K.F(lang, "head", 30)
        d.text((ux, 1752), L["unit"], font=fu, fill=(226, 232, 244, 255), anchor="rm")
        ux -= K.tlen(L["unit"], fu) + 14
    d.text((ux, 1743), str(value), font=fv, fill=col, anchor="rm")
    K.put(cnv, lay, alpha)


def complaint(cnv, lang, idx, text, y, t, t_in, t_reply, dur):
    """민원 카드(왼쪽) + 답변 카드(오른쪽). 세트가 쌓여서 밀도가 생긴다."""
    L = LANG[lang]
    a = K.fade(t, t_in, dur + 2, 0.16, 0.2)
    if a <= 0.01:
        return
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.text((64, y - 22), "#%d" % idx, font=K.F(lang, "num", 30),
           fill=(120, 128, 142, 255), anchor="lm")
    K.put(cnv, lay, a)
    blit(cnv, SPR(zombie_frame(t, idx, "Zombie"), h=118), 108, y + 128, anchor="cb",
         alpha=a)
    K.bubble(cnv, lang, text, (176, y, W - 60, y + 132), 42, alpha=a)
    ar = K.fade(t, t_reply, dur + 2, 0.10, 0.2)
    if ar > 0.01:
        lay2 = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(lay2).text((W - 190, y + 168), L["agent"],
                                  font=K.F(lang, "body", 26),
                                  fill=(120, 128, 142, 255), anchor="rm")
        K.put(cnv, lay2, ar)
        blit(cnv, SPR(ROBOT, h=104), W - 96, y + 236, anchor="cb", alpha=ar)
        K.bubble(cnv, lang, L["reply"], (150, y + 148, W - 172, y + 244), 44,
                 fg=(255, 255, 255), bg=BLUE, tail="r", alpha=ar,
                 outline=(30, 74, 160))


# ---------------------------------------------------------------- 1. 창구 개설
def sc_open(cnv, t, dur, lang):
    L = LANG[lang]
    shell(cnv, lang, t, t, K.fade(t, 0.05, dur + 1, 0.18, 0.3))
    K.headline(cnv, lang, L["desk"], 900, 56, fg=INK, bg=YEL,
               alpha=K.fade(t, 0.55, dur + 1, 0.14, 0.3))
    K.fine(cnv, lang, L["open_fine"], 1010, 26, color=(96, 104, 118),
           alpha=K.fade(t, 1.15, dur + 1, 0.2, 0.3))
    queue_bar(cnv, lang, QUEUE[0], K.fade(t, 0.85, dur + 1, 0.16, 0.3))
    K.fine(cnv, lang, "PYRAMID STUDIO", 1830, 26, color=(120, 128, 142),
           alpha=K.fade(t, 1.45, dur + 1, 0.2, 0.3))


# ---------------------------------------------------------------- 2~4. 민원 3연타
def _talk(cnv, t, dur, lang, n):
    """민원 n번 컷 - 앞선 민원은 위에 그대로 쌓여 있다."""
    L = LANG[lang]
    shell(cnv, lang, t, t + 2.0 * n, 1.0)
    for i in range(1, n + 1):
        prev = i < n
        complaint(cnv, lang, i, L["q%d" % i], CARD_Y[i - 1], t,
                  -9.0 if prev else 0.10, -9.0 if prev else 1.35, dur)
    queue_bar(cnv, lang, QUEUE[n], 1.0)
    if t > 1.35:
        K.stamp(cnv, lang, L["reply"], W * 0.52, CARD_Y[n - 1] + 196, 40, angle=-9,
                color=BLUE, alpha=K.fade(t, 1.35, 1.95, 0.08, 0.3) * 0.0)


def sc_c1(cnv, t, dur, lang):
    _talk(cnv, t, dur, lang, 1)


def sc_c2(cnv, t, dur, lang):
    _talk(cnv, t, dur, lang, 2)


def sc_c3(cnv, t, dur, lang):
    _talk(cnv, t, dur, lang, 3)


# ---------------------------------------------------------------- 5. 대기 0명
def sc_zero(cnv, t, dur, lang):
    """★ 마지막 뒤통수 - 답변하는 사이에 민원인이 없어졌다."""
    L = LANG[lang]
    shell(cnv, lang, t, t + 8.0, 1.0)
    for i in range(1, 4):
        complaint(cnv, lang, i, L["q%d" % i], CARD_Y[i - 1], t, -9.0, -9.0, dur)
    queue_bar(cnv, lang, 0, 1.0, red=True)
    K.stamp(cnv, lang, L["zero_stamp"], W * 0.5, 1180, 92, angle=-13, color=RED,
            alpha=K.fade(t, 0.55, dur + 1, 0.08, 0.3), scale=K.pop(t, 0.55, 0.16))
    K.fine(cnv, lang, L["zero_fine"], 1660, 28, color=(96, 104, 118),
           alpha=K.fade(t, 1.35, dur + 1, 0.2, 0.3))


# ---------------------------------------------------------------- 6. 마무리
def sc_cta(cnv, t, dur, lang):
    """★ 영상 전체에서 유일한 광고 문구가 여기 한 줄."""
    L = LANG[lang]
    cnv.paste(Image.new("RGB", (W, H), NAVY), (0, 0))
    d = ImageDraw.Draw(cnv)
    d.rectangle([0, 0, W, 10], fill=YEL)
    d.rectangle([0, H - 10, W, H], fill=YEL)
    a1 = K.fade(t, 0.15, dur + 1, 0.2, 0.35)
    if a1 > 0.01:
        blit(cnv, SPR("UI/title_logo.png", w=760), W / 2, 780, anchor="cc", alpha=a1)
    K.headline(cnv, lang, L["tag"], 1010, 52, fg=INK, bg=YEL,
               alpha=K.fade(t, 0.80, dur + 1, 0.16, 0.35))
    K.caption(cnv, lang, L["url"], 1120, 34, fg=YEL, bg=(38, 54, 88),
              alpha=K.fade(t, 1.55, dur + 1, 0.18, 0.35))
    K.fine(cnv, lang, L["cta_fine"], 1220, 26, color=(150, 162, 184),
           alpha=K.fade(t, 2.05, dur + 1, 0.2, 0.35))


SCENES = {
    "open": sc_open,
    "c1": sc_c1,
    "c2": sc_c2,
    "c3": sc_c3,
    "zero": sc_zero,
    "cta": sc_cta,
}


# ---------------------------------------------------------------- 오디오
def audio(A):
    T = {n: t0 for (t0, _d, n) in TIMELINE}
    # 상단 모니터의 현장 소리는 상담 내내 계속 들린다
    A.rapid("Weapon_RapidFire.wav", 0.25, T["cta"] - 0.15, 0.16, 0.20, seed=13)
    A.hum(0.0, T["cta"], 0.020)
    A.blip(0.10, 880.0, 0.10, 0.18)                  # 창구 개설
    A.blip(0.60, 660.0, 0.10, 0.15)
    for (i, nm) in enumerate(("c1", "c2", "c3")):
        t0 = T[nm]
        A.sfx("UI_Click.wav", t0 + 0.12, 0.55)       # 민원 접수
        A.blip(t0 + 0.16, 560.0 + i * 60, 0.09, 0.14)
        A.sfx("UI_Click.wav", t0 + 1.35, 0.70)       # 답변
        A.thud(t0 + 1.35, 0.32, 92.0, 46.0, 0.30)
        A.sfx("Enemy_Death.wav", t0 + 1.90, 0.26)    # 대기 인원이 줄어드는 소리
        A.sfx("Weapon_Explosive.wav", t0 + 1.95, 0.28)
    A.sfx("Weapon_Explosive.wav", T["zero"] + 0.55, 0.75)   # 응대 완료 도장
    A.thud(T["zero"] + 0.55, 0.50, 120.0, 40.0, 0.48)
    A.blip(T["zero"] + 1.30, 420.0, 0.18, 0.16)
    A.sfx("LevelUp.wav", T["cta"] + 0.12, 0.50)
    A.bell(T["cta"] + 0.80, 784.0, 1.8, 0.15)
    A.music("Game_BGM02.mp3", 14.0, 0.0, DUR, 0.18)
