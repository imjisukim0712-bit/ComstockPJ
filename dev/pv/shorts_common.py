# -*- coding: utf-8 -*-
"""컴스톡 세로 쇼츠(9:16) 공용 모듈 - 규격, 타임라인, 시간 재사상, 문구.

기존 40초 PV(pv_common / pv_scenes / render_pv)를 **그대로 재사용**해 쇼츠를 만든다.
새로 그리는 장면 함수는 하나도 없다 - 검증된 장면을 골라 순서만 다시 엮는다.

★ 원리 1: 장면 길이를 절대 바꾸지 않는다.
   pv_scenes의 장면 함수는 `if t > 0.72:` 처럼 **장면 내부의 절대 시각**으로 자막
   등장을 제어한다(작업.md 2026-08-27 "장면 길이만 늘리면 뒤가 정지 화면이 된다").
   길이를 줄이면 뒷부분 연출이 잘리고, 늘리면 앞만 빨리 끝나고 정지 화면이 남는다.
   그래서 쇼츠는 장면을 **고르기만** 하고 길이는 40초판 값을 그대로 쓴다.

★ 원리 2: 정전기·흔들림·세로흐름 스케줄은 "원본 PV 시각"으로 조회한다.
   render_pv의 static_at/shake_at/roll_at은 40초 타임라인의 절대 시각에 맞춰
   손으로 맞춘 값이다. 쇼츠 시각을 그대로 넣으면 박자가 전부 어긋난다.
   remap()이 쇼츠 시각 → 원본 PV 시각으로 되돌려 주므로, 그 값으로 조회하면
   장면 전환 지직거림·보스 폭발 흔들림 같은 박자가 자동으로 따라온다.

★ 원리 3: TV 밴드는 960x720을 **1배율 그대로** 붙인다(확대 금지).
   프로젝트 안내.md "픽셀아트는 회전·비정수 배율 금지" - 1080/960 = 1.125배는
   픽셀을 들쭉날쭉하게 만든다. 좌우 60px 여백을 두고 원본 크기로 얹는다.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pv_common import W, H, FPS, TIMELINE

# ---------------------------------------------------------------- 화면 규격
# 쇼츠 표준 9:16. TV 밴드(960x720)는 확대 없이 가운데에 얹는다.
SW, SH = 1080, 1920
TV_X = (SW - W) // 2          # 60 - 좌우 여백
TV_Y = 480                    # TV 밴드 상단. 위 480px / 아래 720px 이 자막 자리다.

# 플랫폼 UI가 덮는 영역(대략). 글자는 이 안쪽에만 놓는다.
SAFE_TOP = 190
SAFE_BOT = SH - 330

# 위/아래 자막의 기준선
CAP_TOP_Y = 330               # TV 위 큰 자막
CAP_BOT_Y = TV_Y + H + 190    # TV 아래 큰 자막 (= 1390)


# ---------------------------------------------------------------- 타임라인
# 40초판에서 고른 장면들. 길이는 원본 그대로이므로 여기에 길이를 적지 않는다.
#
# 고른 기준: 훅이 빨라야 하고(1.5초 안에 승부), 개그 한 방이 있어야 하며,
# 시연과 CTA로 닫아야 한다. `steps`(3단계는 없습니다)는 이미 GIF 훅으로 뽑아
# 검증된 구간이라 중심에 둔다.
SHORTS_SCENES = [
    "tv_on",        # 0.8  TV 켜짐
    "problem",      # 2.6  "좀비 때문에 고민이십니까?"
    "steps",        # 4.8  사용법 3단계 - 3단계는 없다 (핵심 개그)
    "industrial",   # 3.1  보스 = 산업용 강도 시연
    "price",        # 2.9  가격 공개 (0원)
    "cta",          # 3.6  로고 + itch.io
    "tv_off",       # 1.0  TV 꺼짐
]

# 원본 TIMELINE에서 (시작초, 길이)를 이름으로 찾을 수 있게 정리
_ORIG = {name: (t0, d) for (t0, d, name) in TIMELINE}

# 쇼츠 타임라인: (쇼츠시작초, 길이, 장면이름, 원본시작초)
SHORTS_TIMELINE = []
_acc = 0.0
for _name in SHORTS_SCENES:
    _t0, _d = _ORIG[_name]
    # 누적을 반올림해 둔다. 안 그러면 0.8 + 2.6 = 3.4000000000000004 가 되어
    # "장면 시작 시각"을 손으로 계산했을 때 직전 장면 끝으로 떨어진다.
    SHORTS_TIMELINE.append((round(_acc, 6), _d, _name, _t0))
    _acc = round(_acc + _d, 6)

DUR = round(_acc, 3)
NF = int(round(FPS * DUR))


def remap(t):
    """쇼츠 시각 t → (장면이름, 장면내부시각, 장면길이, 원본PV절대시각).

    원본PV절대시각은 render_pv의 static_at/shake_at/roll_at에 넣을 값이다.
    이 값으로 조회해야 40초판에서 손으로 맞춘 박자가 그대로 따라온다.
    """
    for (s0, d, name, o0) in SHORTS_TIMELINE:
        if t < s0 + d:
            tl = max(0.0, t - s0)
            return name, tl, d, o0 + tl
    s0, d, name, o0 = SHORTS_TIMELINE[-1]
    return name, d, d, o0 + d


# ---------------------------------------------------------------- 문구
# 협업 규칙 9번: 화면에 보이는 글은 영어/한글을 같은 작업에서 함께 넣는다.
# 세로 자막은 TV 안쪽 자막과 별개다 - 폰이라 짧고 굵어야 한다.
CAP = {
    "en": {
        "hook_top":   "ZOMBIE PROBLEM?",
        "hook_bot":   "WE HAVE A ROBOT FOR THAT",
        "steps_top":  "3 EASY STEPS",
        "steps_bot":  "(THERE IS NO STEP 3)",
        "boss_top":   "WAVE 20 BOSS",
        "boss_bot":   "INDUSTRIAL STRENGTH",
        "price_top":  "HOW MUCH?",
        "price_bot":  "COMPLETELY FREE",
        "cta_top":    "COMSTOCK",
        "cta_bot":    "PLAY FREE ON ITCH.IO",
    },
    "ko": {
        "hook_top":   "좀비 때문에 고민?",
        "hook_bot":   "로봇 하나면 됩니다",
        "steps_top":  "사용법 3단계",
        "steps_bot":  "(3단계는 없습니다)",
        "boss_top":   "20웨이브 보스",
        "boss_bot":   "산업용 강도",
        "price_top":  "얼마일까요?",
        "price_bot":  "완전 무료",
        "cta_top":    "컴스톡",
        "cta_bot":    "itch.io에서 무료 플레이",
    },
}

# 장면별 (위 자막 키, 아래 자막 키). None이면 그 자리는 비운다.
# tv_on/tv_off는 화면이 켜지고 꺼지는 연출이라 자막을 얹지 않는다.
SCENE_CAPTION = {
    "tv_on":      (None, None),
    "problem":    ("hook_top", "hook_bot"),
    "steps":      ("steps_top", "steps_bot"),
    "industrial": ("boss_top", "boss_bot"),
    "price":      ("price_top", "price_bot"),
    "cta":        ("cta_top", "cta_bot"),
    "tv_off":     (None, None),
}


def caption_at(t, lang):
    """쇼츠 시각 t의 (위 문구, 아래 문구, 장면내부시각, 장면길이). 없으면 None."""
    name, tl, d, _o = remap(t)
    top_key, bot_key = SCENE_CAPTION.get(name, (None, None))
    tbl = CAP[lang]
    top = tbl.get(top_key) if top_key else None
    bot = tbl.get(bot_key) if bot_key else None
    return top, bot, tl, d
