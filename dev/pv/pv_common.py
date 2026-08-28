# -*- coding: utf-8 -*-
"""컴스톡 PV 공용 모듈 - 에셋 로딩, 폰트, 타임라인 상수.

옛날 미국 흑백 TV **광고(인포머셜)** 톤의 30초 게임 소개 영상을 만들기 위한 기반이다.
"좀비 때문에 고민이십니까?" → "잠깐! 이게 끝이 아닙니다!" → "지금 바로!" 구조.
프레임 렌더러(render_pv.py)와 오디오 빌더(build_audio.py)가 이 값을 공유한다.
"""
import os

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
DUR = 41.0
NF = int(round(FPS * DUR))

# ---------------------------------------------------------------- 타임라인
# (시작초, 길이, 씬이름). 합계 41.0초. 광고의 정석 순서를 그대로 따른다.
#
# ★ 장면을 늘릴 때는 그 장면 함수 안쪽의 박자(자막이 뜨는 시각 등)도 같이 벌려야 한다.
#   안 그러면 앞부분만 빨리 끝나고 남은 시간이 정지 화면으로 남는다.
TIMELINE = [
    (0.0, 0.8, "tv_on"),          # TV 켜짐
    (0.8, 2.8, "card_presents"),  # 스튜디오 로고 영상 (무음)
    (3.6, 1.0, "blackout"),       # 로고가 사라지며 지직 한 번 -> 1초 정적
    (4.6, 2.6, "problem"),        # 여기부터 본편 - "좀비 때문에 고민이십니까?"
    (7.2, 1.7, "card_betterway"), # "분명 더 좋은 방법이 있습니다!"
    (8.9, 2.9, "introducing"),    # 제품 등장 - 로고 + NEW 배지
    (11.8, 4.8, "steps"),         # 사용법 3단계 (3단계는 없다)
    (16.6, 1.5, "card_butwait"),  # "잠깐! 이게 끝이 아닙니다!"
    (18.1, 4.0, "more"),          # 사은품 목록
    (22.1, 2.9, "beforeafter"),   # 사용 전 / 사용 후
    (25.0, 2.7, "testimonial"),   # 좀비 고객 후기
    (27.7, 3.1, "industrial"),    # 보스 = 산업용 강도 시연
    (30.8, 2.9, "price"),         # 가격 공개
    (33.7, 2.7, "actnow"),        # "지금 바로!" + 웨이브 카운터
    (36.4, 3.6, "cta"),           # 로고 + itch.io + 깨알 고지 스크롤
    (40.0, 1.0, "tv_off"),        # TV 꺼짐
]

# 장면이 바뀌는 지점 = 지직거림(정전기)이 튀는 지점
# 뒤에 덧붙인 값은 장면 중간의 강조 지점: 3단계 경계 2곳(steps는 dur/3로 나뉜다),
# 사용 전/후 와이프 직후, 가격의 "0원" 도장.
CUTS = [t for (t, _d, _n) in TIMELINE][1:] + [13.4, 15.0, 23.6, 32.35]
# 화면이 세로로 흐르는(수직 동기 이탈) 순간
ROLLS = [(16.6, 0.26), (30.8, 0.24), (33.7, 0.22)]


def scene_at(t, timeline=None):
    """절대 시각 t가 속한 (씬이름, 씬내부시각, 씬길이)를 돌려준다."""
    tl = timeline if timeline is not None else TIMELINE
    for (t0, d, name) in tl:
        if t < t0 + d:
            return name, max(0.0, t - t0), d
    t0, d, name = tl[-1]
    return name, d, d


# ---------------------------------------------------------------- 숏츠(15초) 타임라인
# ★ 41초 인포머셜 PV의 장면을 잘라 쓰지 않는다 - 완전히 새로 쓴 장면 4개다
# (shorts_scenes.py). 흑백 CRT 톤도 쓰지 않는다 - 컬러로 빠르게 컷을 끊는 밈 광고 편집이다.
# "좀비는 계속 나온다 / 그래서 이 녀석을 만들었다"(밈 구조) -> 스펙시트(무기/파츠 수) ->
# 난사 개그 -> 가격 0원 + CTA.
SHORTS_TIMELINE = [
    (0.0, 3.5, "meme_setup"),
    (3.5, 4.0, "spec_sheet"),
    (7.5, 3.5, "chaos_gag"),
    (11.0, 4.0, "price_cta"),
]
SHORTS_DUR = sum(d for (_t, d, _n) in SHORTS_TIMELINE)
SHORTS_NF = int(round(FPS * SHORTS_DUR))
# 컷이 바뀌는 지점 = 화면이 잠깐 하얗게 번쩍하는 지점(밈 편집의 "펀치 컷")
SHORTS_CUTS = [t for (t, _d, _n) in SHORTS_TIMELINE][1:] + [1.5, 12.15]

# ---------------------------------------------------------------- 숏츠 전용 문구
# 41초판 LANG과 별개로 관리한다 - 완전히 새로 쓴 개그이기 때문이다.
SHORTS_LANG = {
    "en": {
        "meme1": "ZOMBIES KEEP SHOWING UP.",
        "meme1b": "AGAIN.",
        "meme2": "SO WE BUILT THIS GUY.",
        "meme2b": "PROBLEM SOLVED. MOSTLY.",
        "spec_title": "WHAT YOU GET:",
        "spec1": "14 WEAPONS",
        "spec2": "134 PARTS",
        "spec3": "0 CHILL",
        "spec4": "1 KING (OPTIONAL)",
        "spec_bar": "NO SUBSCRIPTION.  NO DLC.  NO MERCY.",
        "chaos1": "AIM: CREATIVE.",
        "chaos2": "RESULTS: SURPRISINGLY EFFECTIVE.",
        "price_was2": "WORTH $59.99, PROBABLY.",
        "price_free": "FREE",
        "cta_main2": "PLAY IT BEFORE THE ZOMBIES DO.",
        "cta_url": "pyramid-studio.itch.io/comstock",
    },
    "ko": {
        "meme1": "좀비가 계속 나타납니다.",
        "meme1b": "또요.",
        "meme2": "그래서 이 녀석을 만들었습니다.",
        "meme2b": "문제 해결. 대충.",
        "spec_title": "포함 내역:",
        "spec1": "무기 14종",
        "spec2": "파츠 134종",
        "spec3": "여유 0",
        "spec4": "왕관 1개 (선택)",
        "spec_bar": "구독 없음.  DLC 없음.  자비도 없음.",
        "chaos1": "조준: 자유분방.",
        "chaos2": "결과: 의외로 효과적.",
        "price_was2": "정가 59,900원, 아마도.",
        "price_free": "무료",
        "cta_main2": "좀비보다 먼저 플레이하세요.",
        "cta_url": "pyramid-studio.itch.io/comstock",
    },
}


# ---------------------------------------------------------------- 문구
# 화면에 보이는 모든 글자는 영어/한글 두 벌을 함께 관리한다(협업 규칙 9번과 같은 원칙).
LANG = {
    "en": {
        # 스튜디오 로고 카드
        "presents1": "PYRAMID STUDIO",
        "presents2": "presents",
        # 문제 제기
        "prob1": "ARE YOU TIRED OF ZOMBIES?",
        "prob2": "EATEN AGAIN? THAT'S THE THIRD TIME THIS WEEK.",
        "prob_fine": "*ZOMBIES SOLD SEPARATELY.",
        "better1": "THERE HAS TO BE",
        "better2": "A BETTER WAY!",
        # 제품 등장
        "intro_pre": "INTRODUCING",
        "intro_sub": "THE ROBOT THAT SOLVES EVERYTHING",
        "badge_new": "NEW!",
        # 사용법
        "step_word": "STEP",
        "step1": "SPOT A ZOMBIE.",
        "step2": "BOLT ON A GUN.",
        "step3": "THERE IS NO STEP 3.",
        "steps_bar": "NO AMMO!  NO RELOADING!  NO EFFORT!",
        # 사은품
        "butwait1": "BUT WAIT!",
        "butwait2": "THERE'S MORE!",
        "incl_title": "ALSO INCLUDED:",
        "incl1": "134 ROBOT PARTS",
        "incl2": "14 WEAPONS",
        "incl3": "1 CROWN (LEGENDARY)",
        "incl4": "0 RELOAD BUTTONS",
        "badge_free": "FREE!",
        # 사용 전/후
        "before": "BEFORE",
        "after": "AFTER",
        "ba_fine": "RESULTS NOT TYPICAL.",
        # 후기
        "testi": "\u201cIT REALLY WORKS!\u201d",
        "testi_name": "\u2014 GARY,  RECENTLY DECEASED",
        "testi_fine": "*PAID ACTOR.",
        # 보스
        "ind1": "INDUSTRIAL STRENGTH!",
        "ind2": "TESTED ON WAVE 20",
        # 가격
        "price_was": "A $59.99 VALUE!",
        "price_now": "NOW ONLY",
        "price_free": "FREE",
        "price_fine": "*IT IS ACTUALLY FREE. WE CHECKED.",
        # 행동 유도
        "act1": "ACT NOW!",
        "act2": "20 WAVES  \u00b7  LIMITED SUPPLY*",
        "act_fine": "*SUPPLY IS NOT LIMITED.",
        "wave": "WAVE",
        # 마무리
        "cta_sub": "WAVE SURVIVAL  \u00b7  ROBOT MODDING  \u00b7  20 WAVES",
        "cta_main": "PLAY FREE ON ITCH.IO",
        "cta_url": "pyramid-studio.itch.io/comstock",
        "cta_ops": "OPERATORS ARE STANDING BY",
        "crawl": ("COMSTOCK IS A VIDEO GAME.  ROBOT NOT INCLUDED.  ZOMBIES SOLD SEPARATELY.  "
                  "ALL SPRITES REENACTED BY PROFESSIONAL SPRITES.  NO ROBOTS WERE HARMED.  "
                  "STEP 3 DOES NOT EXIST AND NEVER DID.  GARY IS DOING FINE.  "
                  "SUPPLY IS NOT LIMITED.  OPERATORS ARE NOT STANDING BY.  "
                  "WAVE 20 IS VERY LARGE.  THIS ADVERTISEMENT WAS FILMED IN BLACK AND WHITE "
                  "ON PURPOSE.  "),
    },
    "ko": {
        "presents1": "\ud53c\ub77c\ubbf8\ub4dc \uc2a4\ud29c\ub514\uc624",
        "presents2": "\uc81c\uacf5",
        "prob1": "\uc880\ube44 \ub54c\ubb38\uc5d0 \uace0\ubbfc\uc774\uc2ed\ub2c8\uae4c?",
        "prob2": "\ub610 \uc7a1\uc544\uba39\ud614\ub2e4\uad6c\uc694? \uc774\ubc88 \uc8fc\uc5d0\ub9cc \uc138 \ubc88\uc9f8\uc785\ub2c8\ub2e4.",
        "prob_fine": "\u203b \uc880\ube44\ub294 \ubcc4\ub9e4\uc785\ub2c8\ub2e4.",
        "better1": "\ubd84\uba85",
        "better2": "\ub354 \uc88b\uc740 \ubc29\ubc95\uc774 \uc788\uc2b5\ub2c8\ub2e4!",
        "intro_pre": "\uc0c8\ub86d\uac8c \ucd9c\uc2dc",
        "intro_sub": "\ubaa8\ub4e0 \uac83\uc744 \ud574\uacb0\ud558\ub294 \ub85c\ubd07",
        "badge_new": "\uc2e0\uc81c\ud488!",
        "step_word": "\ub2e8\uacc4",
        "step1": "\uc880\ube44\ub97c \ubc1c\uacac\ud55c\ub2e4.",
        "step2": "\ucd1d\uc744 \ud558\ub098 \ub2ec\uc544\ub454\ub2e4.",
        "step3": "3\ub2e8\uacc4\ub294 \uc5c6\uc2b5\ub2c8\ub2e4.",
        "steps_bar": "\ud0c4\uc57d \uc5c6\uc74c!  \uc7ac\uc7a5\uc804 \uc5c6\uc74c!  \ub178\ub825\ub3c4 \uc5c6\uc74c!",
        "butwait1": "\uc7a0\uae50!",
        "butwait2": "\uc774\uac8c \ub05d\uc774 \uc544\ub2d9\ub2c8\ub2e4!",
        "incl_title": "\ud568\uaed8 \ub4dc\ub9bd\ub2c8\ub2e4:",
        "incl1": "\ub85c\ubd07 \ud30c\uce20 134\uc885",
        "incl2": "\ubb34\uae30 14\uc885",
        "incl3": "\uc655\uad00 1\uac1c (\ub808\uc804\ub354\ub9ac)",
        "incl4": "\uc7ac\uc7a5\uc804 \ubc84\ud2bc 0\uac1c",
        "badge_free": "\ubb34\ub8cc!",
        "before": "\uc0ac\uc6a9 \uc804",
        "after": "\uc0ac\uc6a9 \ud6c4",
        "ba_fine": "\ud6a8\uacfc\ub294 \uac1c\uc778\ucc28\uac00 \uc788\uc2b5\ub2c8\ub2e4.",
        "testi": "\u201c\uc815\ub9d0 \ud6a8\uacfc\uac00 \uc788\uc5b4\uc694!\u201d",
        "testi_name": "\u2014 \uac8c\ub9ac \uc528,  \ucd5c\uadfc \uc0ac\ub9dd",
        "testi_fine": "\u203b \uc5f0\uae30\uc790\uc785\ub2c8\ub2e4.",
        "ind1": "\uc0b0\uc5c5\uc6a9 \uac15\ub3c4!",
        "ind2": "20\uc6e8\uc774\ube0c\uc5d0\uc11c \uac80\uc99d \uc644\ub8cc",
        "price_was": "\uc815\uac00 59,900\uc6d0!",
        "price_now": "\uc9c0\uae08\uc740 \ub2e8\ub3c8",
        "price_free": "0\uc6d0",
        "price_fine": "\u203b \uc9c4\uc9dc 0\uc6d0\uc785\ub2c8\ub2e4. \ud655\uc778\ud588\uc2b5\ub2c8\ub2e4.",
        "act1": "\uc9c0\uae08 \ubc14\ub85c!",
        "act2": "20\uc6e8\uc774\ube0c  \u00b7  \uc218\ub7c9 \ud55c\uc815 \u203b",
        "act_fine": "\u203b \uc218\ub7c9 \ud55c\uc815 \uc544\ub2d9\ub2c8\ub2e4.",
        "wave": "\uc6e8\uc774\ube0c",
        "cta_sub": "\uc6e8\uc774\ube0c \uc11c\ubc14\uc774\ubc8c  \u00b7  \ub85c\ubd07 \ubaa8\ub529  \u00b7  20\uc6e8\uc774\ube0c",
        "cta_main": "itch.io\uc5d0\uc11c \ubb34\ub8cc \ud50c\ub808\uc774",
        "cta_url": "pyramid-studio.itch.io/comstock",
        "cta_ops": "\uc0c1\ub2f4\uc6d0\uc774 \ub300\uae30 \uc911\uc785\ub2c8\ub2e4",
        "crawl": ("\ucef4\uc2a4\ud1a1\uc740 \ube44\ub514\uc624 \uac8c\uc784\uc785\ub2c8\ub2e4.  \ub85c\ubd07\uc740 \ud3ec\ud568\ub418\uc5b4 \uc788\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4.  "
                  "\uc880\ube44\ub294 \ubcc4\ub9e4\uc785\ub2c8\ub2e4.  \ubaa8\ub4e0 \uc2a4\ud504\ub77c\uc774\ud2b8\ub294 \uc804\ubb38 \uc2a4\ud504\ub77c\uc774\ud2b8\uac00 \uc7ac\uc5f0\ud588\uc2b5\ub2c8\ub2e4.  "
                  "\ub85c\ubd07\uc740 \ub2e4\uce58\uc9c0 \uc54a\uc558\uc2b5\ub2c8\ub2e4.  3\ub2e8\uacc4\ub294 \uc874\uc7ac\ud55c \uc801\uc774 \uc5c6\uc2b5\ub2c8\ub2e4.  "
                  "\uac8c\ub9ac \uc528\ub294 \uc798 \uc9c0\ub0c5\ub2c8\ub2e4.  \uc218\ub7c9\uc740 \ud55c\uc815\ub418\uc5b4 \uc788\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4.  "
                  "\uc0c1\ub2f4\uc6d0\uc740 \ub300\uae30\ud558\uace0 \uc788\uc9c0 \uc54a\uc2b5\ub2c8\ub2e4.  20\uc6e8\uc774\ube0c\ub294 \ub9e4\uc6b0 \ud07d\ub2c8\ub2e4.  "
                  "\uc774 \uad11\uace0\ub294 \uc77c\ubd80\ub7ec \ud751\ubc31\uc73c\ub85c \ucd2c\uc601\ud588\uc2b5\ub2c8\ub2e4.  "),
    },
}

# ---------------------------------------------------------------- 폰트
FONTDIR = r"C:\Windows\Fonts"
FONTS = {
    # 무성영화 자막 카드용 세리프
    ("en", "serif"): (os.path.join(FONTDIR, "georgiab.ttf"), 0),
    ("ko", "serif"): (os.path.join(FONTDIR, "HANBatangB.ttf"), 0),
    # 광고 아나운서용 굵은 산세리프
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
