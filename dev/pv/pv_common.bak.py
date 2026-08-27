# -*- coding: utf-8 -*-
"""컴스톡 PV 공용 모듈 - 에셋 로딩, 폰트, 타임라인 상수.

옛날 미국 카툰 흑백 TV 톤의 30초 게임 소개 영상(병맛)을 만들기 위한 기반이다.
프레임 렌더러(render_pv.py)와 오디오 빌더(build_audio.py)가 이 값을 공유한다.
"""
import os
import sys

ROOT = r"C:\Project\ComstockPJ"
RES = os.path.join(ROOT, "Assets", "Resources")
CACHE = os.path.join(
    os.environ.get("TEMP", os.path.join(ROOT, "dev", "pv")),
    "comstock_pv_cache",
)

# 화면 규격: 내용은 4:3(옛날 TV), 최종 출력은 16:9 레터박스
W, H = 960, 720
OUT_W, OUT_H = 1280, 720
FPS = 24
DUR = 30.0
NF = int(round(FPS * DUR))

# ---------------------------------------------------------------- 타임라인
# (시작초, 길이, 씬이름). 합계 30.0초.
TIMELINE = [
    (0.0, 1.0, "tv_on"),
    (1.0, 2.0, "card_presents"),
    (3.0, 2.6, "walk"),
    (5.6, 1.4, "card_trouble"),
    (7.0, 2.4, "horde"),
    (9.4, 1.4, "card_guns"),
    (10.8, 4.2, "massacre"),
    (15.0, 1.3, "card_noreload"),
    (16.3, 2.2, "shop"),
    (18.5, 2.0, "waves"),
    (20.5, 1.2, "card_boss"),
    (21.7, 3.3, "boss"),
    (25.0, 2.6, "logo"),
    (27.6, 1.4, "card_end"),
    (29.0, 1.0, "tv_off"),
]

# 장면이 바뀌는 지점 = 지직거림(정전기)이 튀는 지점
CUTS = [t for (t, _d, _n) in TIMELINE][1:] + [12.6, 19.6, 23.4]
# 화면이 세로로 흐르는(수직 동기 이탈) 순간
ROLLS = [(9.4, 0.26), (20.5, 0.28), (23.4, 0.22)]


def scene_at(t):
    """절대 시각 t가 속한 (씬이름, 씬내부시각, 씬길이)를 돌려준다."""
    for (t0, d, name) in TIMELINE:
        if t < t0 + d:
            return name, max(0.0, t - t0), d
    t0, d, name = TIMELINE[-1]
    return name, d, d


# ---------------------------------------------------------------- 문구
# 화면에 보이는 모든 글자는 영어/한글 두 벌을 함께 관리한다(협업 규칙 9번과 같은 원칙).
LANG = {
    "en": {
        "presents1": "PYRAMID STUDIO",
        "presents2": "presents",
        "presents3": "A CARTOON IN ONE REEL",
        "walk_cap": "OUR HERO GOES FOR A WALK.",
        "footnote": "*SPRITES REENACTED. NO ROBOTS WERE HARMED.",
        "trouble1": "A MILD",
        "trouble2": "INCONVENIENCE.",
        "zombies": "ZOMBIES",
        "guns1": "SOLUTION:",
        "guns2": "ATTACH MORE GUNS.",
        "kills": "KILLS",
        "stamp": "NO RELOADING",
        "noreload1": "NO AMMO. NO RELOADING.",
        "noreload2": "NO THOUGHTS.",
        "shop_title": "SHOPPING TIME",
        "shop_price": "-9,999 G",
        "shop_cap": "SWAP EVERY PART. REGRET NOTHING.",
        "wave": "WAVE",
        "boss1": "WAVE 20.",
        "boss2": "HE IS VERY LARGE.",
        "logo_sub": "WAVE SURVIVAL  \u00b7  ROBOT MODDING  \u00b7  20 WAVES",
        "logo_cta": "PLAY FREE ON ITCH.IO",
        "logo_url": "pyramid-studio.itch.io/comstock",
        "end1": "THE END",
        "end2": "(NOT REALLY. STILL IN DEVELOPMENT.)",
    },
    "ko": {
        "presents1": "\ud53c\ub77c\ubbf8\ub4dc \uc2a4\ud29c\ub514\uc624",
        "presents2": "\uc81c\uacf5",
        "presents3": "\ub2e8\ud3b8 \ub9cc\ud654 \ud55c \ud3b8",
        "walk_cap": "\uc6b0\ub9ac\uc758 \uc8fc\uc778\uacf5, \uc0b0\ucc45 \uc911.",
        "footnote": "*\uc2a4\ud504\ub77c\uc774\ud2b8 \uc7ac\uc5f0\ucd9c. \ub85c\ubd07\uc740 \ub2e4\uce58\uc9c0 \uc54a\uc558\uc2b5\ub2c8\ub2e4.",
        "trouble1": "\uc0ac\uc18c\ud55c",
        "trouble2": "\ubb38\uc81c \ubc1c\uc0dd.",
        "zombies": "\uc880\ube44",
        "guns1": "\ud574\uacb0\ucc45:",
        "guns2": "\ucd1d\uc744 \ub354 \ub2e8\ub2e4.",
        "kills": "\ucc98\uce58",
        "stamp": "\uc7ac\uc7a5\uc804 \uc5c6\uc74c",
        "noreload1": "\ud0c4\uc57d \uc5c6\uc74c. \uc7ac\uc7a5\uc804 \uc5c6\uc74c.",
        "noreload2": "\uc0dd\uac01\ub3c4 \uc5c6\uc74c.",
        "shop_title": "\uc1fc\ud551 \ud0c0\uc784",
        "shop_price": "-9,999 G",
        "shop_cap": "\ubaa8\ub4e0 \ud30c\uce20\ub97c \uac08\uc544\ub048\ub294\ub2e4. \ud6c4\ud68c\ub294 \uc5c6\ub2e4.",
        "wave": "\uc6e8\uc774\ube0c",
        "boss1": "\uc6e8\uc774\ube0c 20.",
        "boss2": "\ub9e4\uc6b0 \ud07d\ub2c8\ub2e4.",
        "logo_sub": "\uc6e8\uc774\ube0c \uc11c\ubc14\uc774\ubc8c  \u00b7  \ub85c\ubd07 \ubaa8\ub529  \u00b7  20\uc6e8\uc774\ube0c",
        "logo_cta": "itch.io\uc5d0\uc11c \ubb34\ub8cc \ud50c\ub808\uc774",
        "logo_url": "pyramid-studio.itch.io/comstock",
        "end1": "\ub05d",
        "end2": "(\uc0ac\uc2e4 \uc548 \ub05d\ub0a8. \uac1c\ubc1c \uc911.)",
    },
}

# ---------------------------------------------------------------- 폰트
FONTDIR = r"C:\Windows\Fonts"
FONTS = {
    # 무성영화 자막 카드용 세리프
    ("en", "serif"): (os.path.join(FONTDIR, "georgiab.ttf"), 0),
    ("ko", "serif"): (os.path.join(FONTDIR, "HANBatangB.ttf"), 0),
    # 병맛 강조용 굵은 산세리프
    ("en", "punch"): (os.path.join(FONTDIR, "ariblk.ttf"), 0),
    ("ko", "punch"): (os.path.join(FONTDIR, "malgunbd.ttf"), 0),
    # HUD/수치용
    ("en", "hud"): (os.path.join(FONTDIR, "impact.ttf"), 0),
    ("ko", "hud"): (os.path.join(FONTDIR, "malgunbd.ttf"), 0),
}
FALLBACK = {
    "serif": (os.path.join(FONTDIR, "times.ttf"), 0),
    "punch": (os.path.join(FONTDIR, "arialbd.ttf"), 0),
    "hud": (os.path.join(FONTDIR, "arialbd.ttf"), 0),
}


def font_path(lang, kind):
    p, idx = FONTS.get((lang, kind), FALLBACK[kind])
    if not os.path.exists(p):
        p, idx = FALLBACK[kind]
    return p, idx
