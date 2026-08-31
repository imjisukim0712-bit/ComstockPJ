# -*- coding: utf-8 -*-
"""컴스톡 세로 릴스(9:16) - 공통 설정·비트 격자·문구.

원본 밈("Damdam Didi didi cat")은 **남의 유튜브 업로드**라 받아서 다시 올리지 않는다.
대신 그 밈의 **형식**(흰 배경 + 오려낸 얼굴 하나 + 비트마다 찌그러졌다 튀어오르기)만
가져와 우리 로봇 머리 12종으로 새로 그린다. 그래서 이 파이프라인은 입력 영상이 없고
`Assets/Resources/Heads`의 PNG만 읽는다.

★ 타이밍은 전부 "초"가 아니라 `BPM`/`BAR`에서 파생시킨다. 곡을 바꿔 붙일 때
`BPM` 한 곳만 고치면 장면 길이·히트 좌표·엔드카드 시각이 함께 따라온다.
"""
import os
import shutil
import subprocess

# ---------------------------------------------------------------- 경로
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, "Assets", "Resources")
HEADS = os.path.join(RES, "Heads")
FONTS = os.path.join(ROOT, "Assets", "Fonts")
OUT = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(OUT, "_cache")

# ---------------------------------------------------------------- 화면
# 릴스/틱톡 표준. 세로 9:16, 30fps.
W, H = 1080, 1920
FPS = 30

# 인스타 릴스는 위/아래에 자기 UI를 겹쳐 그린다. 글자와 얼굴은 이 띠 안에만 놓는다.
SAFE_TOP = 260
SAFE_BOTTOM = 1560

BG = (255, 255, 255)
INK = (26, 26, 30)

# ---------------------------------------------------------------- 비트
# "담담 디디디디" = 한 마디에 4분음표 2번 + 8분음표 4번.
BPM = 128.0
BEAT = 60.0 / BPM          # 0.46875초
BAR = BEAT * 4             # 1.875초
BARS = 8
DUR = BAR * BARS           # 15.000초
NFRAMES = int(round(DUR * FPS))   # 450

# 마디 안에서 얼굴이 튀는 시각(4분음표 단위). 이게 "담-담-디디-디디"다.
HITS_IN_BAR = (0.0, 1.0, 2.0, 2.5, 3.0, 3.5)


def hit_times():
    """전체 히트 시각을 (절대초, 통짜 인덱스, 마디, 마디 안 순번)으로 돌려준다."""
    out = []
    n = 0
    for b in range(BARS):
        for k, off in enumerate(HITS_IN_BAR):
            out.append((b * BAR + off * BEAT, n, b, k))
            n += 1
    return out


HITS = hit_times()

# ---------------------------------------------------------------- 구성
# 마디마다 화면에 놓을 얼굴 개수. 1 → 2 → 4 → 12로 불어나는 게 이 영상의 개그다.
# 마지막 마디에서 제목 카드가 내려꽂힌다.
FACES_PER_BAR = (1, 1, 2, 2, 4, 4, 12, 12)
ENDCARD_BAR = 7

# 얼굴 순서(NeonEye는 8장짜리 애니메이션이라 대표로 한 칸만 차지한다).
HEAD_ORDER = (
    "NeonEye", "HappyPixel", "SodaCan", "FanBot",
    "Guardman", "MiniPixie", "ComstockMk01", "Meteus",
    "Pixie", "HotPot", "PrivateComstock", "Berserker",
)

# ---------------------------------------------------------------- 문구
# PV(`dev/pv/pv_common.py`)와 같은 방식 - 언어 분기를 코드에 만들지 않고 여기 한 곳에 둔다.
LANG = {
    "en": {
        "caption": "our robots after clearing wave 10",
        "title": "COMSTOCK",
        "sub": "FREE ON ITCH.IO",
        "url": "PYRAMID-STUDIO.ITCH.IO/COMSTOCK",
        "font_caption": os.path.join(FONTS, "NotoSansKR", "NotoSansKR-Bold.ttf"),
        "font_title": os.path.join(FONTS, "Orbitron", "Orbitron-Black.ttf"),
        "font_sub": os.path.join(FONTS, "Orbitron", "Orbitron-Bold.ttf"),
        "font_url": os.path.join(FONTS, "Orbitron", "Orbitron-Bold.ttf"),
    },
    "ko": {
        "caption": "웨이브 10 깬 우리 로봇들",
        "title": "COMSTOCK",
        "sub": "itch.io에서 무료 플레이",
        "url": "PYRAMID-STUDIO.ITCH.IO/COMSTOCK",
        # ★ Orbitron은 라틴 전용(207자)이라 한글을 그리면 두부가 된다. 한글 줄은 NotoSansKR.
        "font_caption": os.path.join(FONTS, "NotoSansKR", "NotoSansKR-Bold.ttf"),
        "font_title": os.path.join(FONTS, "Orbitron", "Orbitron-Black.ttf"),
        "font_sub": os.path.join(FONTS, "NotoSansKR", "NotoSansKR-Bold.ttf"),
        "font_url": os.path.join(FONTS, "Orbitron", "Orbitron-Bold.ttf"),
    },
}


# ---------------------------------------------------------------- ffmpeg
def ffmpeg_exe():
    """ffmpeg 실행 파일을 찾는다.

    사용자 PC에는 PATH에 있고, 원격 컨테이너에는 없어서 `imageio-ffmpeg`가 들고 있는
    번들 바이너리로 떨어진다. 둘 다 없으면 그때 알려 준다.
    """
    env = os.environ.get("FFMPEG")
    if env and os.path.exists(env):
        return env
    found = shutil.which("ffmpeg")
    if found:
        return found
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        pass
    raise RuntimeError(
        "ffmpeg를 찾지 못했다. PATH에 두거나 `pip install imageio-ffmpeg`를 하거나 "
        "환경변수 FFMPEG에 경로를 지정할 것."
    )


def run(cmd):
    subprocess.run(cmd, check=True)
