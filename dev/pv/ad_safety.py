# -*- coding: utf-8 -*-
"""컴스톡 30초 가로 광고 - "좀비 산업안전 교육 · 제 7 과: 로봇 조우 시 행동 요령".

★ 웃음 구조(41초 인포머셜에서 배운 대로)
  - 누구나 아는 형식(산업안전 교육 비디오)을 세우고 **그 형식을 항목마다 배신한다**.
    항목 ① ② ③ 모두 "실패" 도장 -> "정답: 없음" -> "수료 좀비 0명".
  - 화면에 항상 텍스트가 3~4층 겹친다: 과목 머리글 + 항목 제목 + 결과 자막 + 깨알 고지.
  - 항목 하나당 개그 이벤트가 4개(제목 / 자료 화면의 배신 / 실패 도장 / 깨알 고지)라
    1.3초마다 뒤통수가 온다.
  - 배색은 산업 안전색(노랑/검정)이라 흑백 인포머셜·컬러 밈·다큐와 겹치지 않는다.

게임 정보를 "설명"하지 않는다 - 좀비 쪽 관점의 실패담으로만 보여준다.
"""
from PIL import Image, ImageDraw

import ad_kit as K

W, H = 1280, 720
FPS = 30
NAME = "Safety"

CREAM = (246, 242, 232)
INK = (26, 26, 28)
YEL = (245, 197, 24)
RED = (206, 38, 38)
TAPE_H = 38

TIMELINE = [
    (0.0,  2.6, "open"),
    (2.6,  5.4, "item1"),
    (8.0,  5.4, "item2"),
    (13.4, 5.4, "item3"),
    (18.8, 4.6, "answer"),
    (23.4, 3.2, "stats"),
    (26.6, 3.4, "cta"),
]
DUR = sum(d for (_t, d, _n) in TIMELINE)

# 컷 지점 = 슬라이드가 넘어가는 흰 플래시
CUTS = [t for (t, _d, _n) in TIMELINE][1:]
# 도장이 찍히는 순간의 충격(전역시각, 진폭)
SHAKES = [(2.6 + 2.60, 7.0), (8.0 + 2.60, 7.0), (13.4 + 2.60, 7.0), (18.8 + 2.00, 9.0)]
VIGNETTE = 0.20

LANG = {
    "ko": {
        "course": "좀비 산업안전 교육",
        "lesson": "제 7 과 — 로봇 조우 시 행동 요령",
        "open_stamp": "교육용",
        "open_fine": "*본 교육은 좀비를 위한 것입니다.",
        "i1": "발견하면 즉시 멀어진다",
        "i1r": "결과: 전원 접근",
        "i1f": "*멀어진 개체는 없습니다.",
        "i2": "무리와 함께 행동한다",
        "i2r": "결과: 전원 소실",
        "i2f": "*무리는 함께 사라졌습니다.",
        "i3": "엄폐물을 활용한다",
        "i3r": "결과: 엄폐물 소실",
        "i3f": "*엄폐물이 먼저 사라집니다.",
        "fail": "실패",
        "ans": "정답: 없음",
        "ans_sub": "제 7 과 종료",
        "ans_fine": "*정답이 있었다면 이 과목은 없었을 것입니다.",
        "stats_head": "교육 결과",
        "row1": "본 교육 수료 좀비",
        "row2": "재수강 대상",
        "unit": "명",
        "stats_fine": "*수료증은 발급되지 않습니다.",
        "tag": "좀비보다 먼저 플레이하세요.",
        "url": "pyramid-studio.itch.io/comstock",
        "cta_fine": "*좀비는 기다려주지 않습니다.",
        "i1e": "예상: 안전 확보",
        "i2e": "예상: 생존율 상승",
        "i3e": "예상: 피격 감소",
    },
    "en": {
        "course": "ZOMBIE WORKPLACE SAFETY",
        "lesson": "LESSON 7 — WHAT TO DO WHEN YOU MEET THE ROBOT",
        "open_stamp": "TRAINING",
        "open_fine": "*THIS TRAINING IS FOR ZOMBIES.",
        "i1": "MOVE AWAY IMMEDIATELY",
        "i1r": "RESULT: ALL APPROACHED",
        "i1f": "*NO SUBJECT MOVED AWAY.",
        "i2": "STAY WITH THE HERD",
        "i2r": "RESULT: HERD REMOVED",
        "i2f": "*THE HERD LEFT TOGETHER.",
        "i3": "USE AVAILABLE COVER",
        "i3r": "RESULT: COVER REMOVED",
        "i3f": "*THE COVER LEAVES FIRST.",
        "fail": "FAILED",
        "ans": "CORRECT ANSWER: NONE",
        "ans_sub": "END OF LESSON 7",
        "ans_fine": "*IF THERE WERE ONE, THIS LESSON WOULD NOT EXIST.",
        "stats_head": "TRAINING RESULTS",
        "row1": "ZOMBIES WHO COMPLETED",
        "row2": "SCHEDULED TO RETAKE",
        "unit": "",
        "stats_fine": "*NO CERTIFICATES WILL BE ISSUED.",
        "tag": "PLAY IT BEFORE THE ZOMBIES DO.",
        "url": "pyramid-studio.itch.io/comstock",
        "cta_fine": "*ZOMBIES DO NOT WAIT.",
        "i1e": "EXPECTED: SAFE",
        "i2e": "EXPECTED: BETTER ODDS",
        "i3e": "EXPECTED: FEWER HITS",
    },
}


# ---------------------------------------------------------------- 공통 틀
def frame_bg(cnv, dark=False):
    """크림색 교재 바탕 + 위아래 안전 테이프."""
    cnv.paste(Image.new("RGB", (W, H), INK if dark else CREAM), (0, 0))
    tape = K.warn_tape(W, TAPE_H)
    cnv.paste(tape, (0, 0))
    cnv.paste(tape, (0, H - TAPE_H))


def page_no(cnv, lang, s="7 / 7", alpha=1.0):
    if alpha <= 0.01:
        return
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(lay).text((W - 44, 656), s, font=K.F(lang, "num", 20),
                             fill=(120, 116, 110, 255), anchor="rm")
    K.put(cnv, lay, alpha)


def course_head(cnv, lang, alpha=1.0):
    L = LANG[lang]
    f = K.fit(lang, "body", 21, L["course"], 520)
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.text((44, 66), L["course"], font=f, fill=INK + (255,), anchor="lm")
    d.line([(44, 84), (44 + K.tlen(L["course"], f), 84)], fill=INK + (150,), width=2)
    f2 = K.fit(lang, "fine", 18, L["lesson"], 620)
    d.text((W - 44, 66), L["lesson"], font=f2, fill=(96, 92, 88, 255), anchor="rm")
    K.put(cnv, lay, alpha)


def row(cnv, lang, label, value, unit, cy, alpha=1.0):
    """양식지의 한 줄 - 점선 리더 + 오른쪽에 숫자."""
    if alpha <= 0.01:
        return
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    fl = K.fit(lang, "body", 34, label, 620)
    d.text((150, cy), label, font=fl, fill=INK + (255,), anchor="lm")
    x0 = 150 + K.tlen(label, fl) + 18
    fv = K.F(lang, "num", 54)
    val = str(value) + ((" " + unit) if unit else "")
    fu = K.F(lang, "head", 26)
    uw = (K.tlen(unit, fu) + 12) if unit else 0
    vx = W - 170 - uw
    d.text((vx, cy), str(value), font=fv, fill=RED + (255,), anchor="rm")
    if unit:
        d.text((W - 158, cy + 6), unit, font=fu, fill=INK + (255,), anchor="rm")
    x1 = vx - K.tlen(str(value), fv) - 18
    x = x0
    while x < x1:
        d.line([(x, cy + 8), (x + 5, cy + 8)], fill=(150, 146, 140, 255), width=3)
        x += 14
    K.put(cnv, lay, alpha)


# ---------------------------------------------------------------- 1. 표지
def sc_open(cnv, t, dur, lang):
    L = LANG[lang]
    frame_bg(cnv)
    course_head(cnv, lang, K.fade(t, 0.05, dur + 1, 0.2, 0.3))
    s = K.pop(t, 0.12)
    if t > 0.12:
        K.headline(cnv, lang, L["course"], 246, int(74 / s), bg=YEL,
                   alpha=K.fade(t, 0.12, dur + 1, 0.14, 0.3))
    K.headline(cnv, lang, L["lesson"], 356, 36, fg=(250, 250, 250), bg=INK,
               alpha=K.fade(t, 0.72, dur + 1, 0.18, 0.3), outline=None)
    K.stamp(cnv, lang, L["open_stamp"], 1080, 168, 44, angle=-11, color=(58, 92, 168),
            alpha=K.fade(t, 1.35, dur + 1, 0.1, 0.3), scale=K.pop(t, 1.35, 0.14))
    K.fine(cnv, lang, L["open_fine"], 470, 19, alpha=K.fade(t, 1.85, dur + 1, 0.2, 0.3))
    K.caption(cnv, lang, "PYRAMID STUDIO", 552, 22, fg=INK, bg=(226, 220, 206),
              alpha=K.fade(t, 2.05, dur + 1, 0.2, 0.3))


# ---------------------------------------------------------------- 2~4. 항목
def _item(cnv, t, dur, lang, n, title, expect, result, fine, kind, seed):
    L = LANG[lang]
    frame_bg(cnv)
    course_head(cnv, lang)
    K.numbered(cnv, lang, n, 96, 122, 38, alpha=K.fade(t, 0.02, dur + 1, 0.12, 0.25))
    K.headline(cnv, lang, title, 122, 42, bg=YEL, cx=708, maxw=W - 320,
               alpha=K.fade(t, 0.10, dur + 1, 0.14, 0.25))
    # ★ 레퍼런스의 "정가 취소선"을 "예상 결과 취소"로 옮겨 왔다 - 개그 한 층이 더 쌓인다
    box = K.caption(cnv, lang, expect, 176, 22, fg=INK, bg=(226, 221, 210),
                    alpha=K.fade(t, 0.80, dur + 1, 0.14, 0.25))
    if box and t > 2.60:
        q = K.clamp((t - 2.60) / 0.20)
        lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        cy = (box[1] + box[3]) / 2
        d.line([(box[0] + 5, cy + 3), (box[0] + 5 + (box[2] - box[0] - 10) * q, cy - 3)],
               fill=RED + (255,), width=5)
        K.put(cnv, lay, 1.0)
    ft = max(0.0, t - 0.30)
    K.screen(cnv, (150, 208, 1130, 562),
             K.footage(980, 354, ft, kind if t > 0.30 else "empty", seed=seed))
    K.stamp(cnv, lang, L["fail"], 396, 470, 64, angle=-13, color=RED,
            alpha=K.fade(t, 2.60, dur + 1, 0.08, 0.25), scale=K.pop(t, 2.60, 0.16))
    K.caption(cnv, lang, result, 608, 32, fg=(255, 244, 224), bg=INK,
              alpha=K.fade(t, 3.10, dur + 1, 0.16, 0.25))
    K.fine(cnv, lang, fine, 658, 19, alpha=K.fade(t, 4.30, dur + 1, 0.2, 0.25))
    page_no(cnv, lang, "%d / 3" % n, 0.9)


def sc_item1(cnv, t, dur, lang):
    L = LANG[lang]
    _item(cnv, t, dur, lang, 1, L["i1"], L["i1e"], L["i1r"], L["i1f"], "approach", 11)


def sc_item2(cnv, t, dur, lang):
    L = LANG[lang]
    _item(cnv, t, dur, lang, 2, L["i2"], L["i2e"], L["i2r"], L["i2f"], "wipe", 23)


def sc_item3(cnv, t, dur, lang):
    L = LANG[lang]
    _item(cnv, t, dur, lang, 3, L["i3"], L["i3e"], L["i3r"], L["i3f"], "cover", 37)


# ---------------------------------------------------------------- 5. 정답
def sc_answer(cnv, t, dur, lang):
    """★ 형식의 배신이 여기서 완성된다 - 항목 셋 다 실패했으니 정답이 없다."""
    L = LANG[lang]
    cnv.paste(Image.new("RGB", (W, H), YEL), (0, 0))
    tape = K.warn_tape(W, TAPE_H)
    cnv.paste(tape, (0, 0))
    cnv.paste(tape, (0, H - TAPE_H))
    course_head(cnv, lang, 0.85)
    s = K.pop(t, 0.14, 0.2)
    K.headline(cnv, lang, L["ans"], 300, int(92 / s), fg=YEL, bg=INK, pad=(40, 22),
               alpha=K.fade(t, 0.14, dur + 1, 0.12, 0.3), outline=None)
    K.caption(cnv, lang, L["ans_sub"], 412, 32, fg=INK, bg=(255, 255, 255),
              alpha=K.fade(t, 1.20, dur + 1, 0.16, 0.3))
    K.stamp(cnv, lang, L["fail"], 1046, 236, 74, angle=-15, color=RED,
            alpha=K.fade(t, 2.00, dur + 1, 0.08, 0.3), scale=K.pop(t, 2.00, 0.16))
    K.fine(cnv, lang, L["ans_fine"], 520, 20, color=(74, 62, 30),
           alpha=K.fade(t, 2.70, dur + 1, 0.2, 0.3))


# ---------------------------------------------------------------- 6. 교육 결과
def sc_stats(cnv, t, dur, lang):
    L = LANG[lang]
    frame_bg(cnv)
    course_head(cnv, lang)
    K.headline(cnv, lang, L["stats_head"], 176, 46, bg=YEL,
               alpha=K.fade(t, 0.05, dur + 1, 0.14, 0.25))
    K.shadow_plate(cnv, (120, 250, W - 120, 470), (255, 255, 255), INK, 3)
    row(cnv, lang, L["row1"], 0, L["unit"], 316, K.fade(t, 0.60, dur + 1, 0.14, 0.25))
    row(cnv, lang, L["row2"], 0, L["unit"], 408, K.fade(t, 1.35, dur + 1, 0.14, 0.25))
    K.fine(cnv, lang, L["stats_fine"], 540, 20, alpha=K.fade(t, 2.15, dur + 1, 0.2, 0.25))


# ---------------------------------------------------------------- 7. 마무리
def sc_cta(cnv, t, dur, lang):
    """★ 영상 전체에서 유일한 광고 문구가 여기 한 줄."""
    L = LANG[lang]
    frame_bg(cnv, dark=True)
    a1 = K.fade(t, 0.12, dur + 1, 0.2, 0.35)
    if a1 > 0.01:
        from pv_draw import SPR, blit
        blit(cnv, SPR("UI/title_logo.png", w=470), W / 2, 258, anchor="cc", alpha=a1)
    K.headline(cnv, lang, L["tag"], 404, 40, fg=INK, bg=YEL,
               alpha=K.fade(t, 0.85, dur + 1, 0.16, 0.35))
    K.caption(cnv, lang, L["url"], 486, 24, fg=YEL, bg=(44, 44, 48),
              alpha=K.fade(t, 1.70, dur + 1, 0.18, 0.35))
    K.fine(cnv, lang, L["cta_fine"], 556, 19, color=(168, 162, 150),
           alpha=K.fade(t, 2.30, dur + 1, 0.2, 0.35))


SCENES = {
    "open": sc_open,
    "item1": sc_item1,
    "item2": sc_item2,
    "item3": sc_item3,
    "answer": sc_answer,
    "stats": sc_stats,
    "cta": sc_cta,
}


# ---------------------------------------------------------------- 오디오
def audio(A):
    """A는 build_ad_audio.py가 넘겨주는 합성/효과음 도구."""
    T = {n: t0 for (t0, _d, n) in TIMELINE}
    A.hum(0.0, DUR - 0.2, 0.030)                     # 영사기 잡음(교육 비디오 느낌)
    # 표지
    A.blip(0.14, 880.0, 0.09, 0.16)
    A.blip(0.76, 660.0, 0.09, 0.13)
    A.sfx("UI_Click.wav", 1.36, 0.55)                # 도장
    # 항목 3개 - 제목 삐 / 자료화면 사격 / 실패 도장 쾅 / 결과 자막 딸깍
    for (nm, kind) in (("item1", "approach"), ("item2", "wipe"), ("item3", "cover")):
        t0 = T[nm]
        A.blip(t0 + 0.12, 990.0, 0.10, 0.16)
        A.rapid("Weapon_RapidFire.wav", t0 + 0.9, t0 + 2.5, 0.13, 0.24, seed=int(t0 * 7))
        if kind == "wipe":
            A.sfx("Weapon_Explosive.wav", t0 + 1.45, 0.60)
            A.sfx("Enemy_Death.wav", t0 + 1.50, 0.40)
        if kind == "cover":
            A.sfx("Weapon_Explosive.wav", t0 + 1.65, 0.55)
        A.sfx("Weapon_Explosive.wav", t0 + 2.60, 0.72)     # 실패 도장
        A.thud(t0 + 2.60, 0.45, 96.0, 40.0, 0.42)
        A.sfx("UI_Click.wav", t0 + 3.12, 0.45)
    # 정답: 없음
    A.thud(T["answer"] + 0.14, 0.70, 128.0, 44.0, 0.52)
    A.sfx("Weapon_Explosive.wav", T["answer"] + 2.00, 0.70)
    A.thud(T["answer"] + 2.00, 0.45, 96.0, 38.0, 0.40)
    # 교육 결과
    A.blip(T["stats"] + 0.08, 760.0, 0.10, 0.15)
    A.blip(T["stats"] + 0.62, 520.0, 0.14, 0.17)
    A.blip(T["stats"] + 1.37, 440.0, 0.16, 0.17)
    # 마무리
    A.sfx("LevelUp.wav", T["cta"] + 0.14, 0.50)
    A.bell(T["cta"] + 0.88, 784.0, 2.0, 0.15)
    A.music("Title_BGM.mp3", 6.0, 0.0, DUR, 0.20)    # 아주 낮게 깔리는 배경음
