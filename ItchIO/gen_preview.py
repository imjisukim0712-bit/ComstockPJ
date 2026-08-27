# -*- coding: utf-8 -*-
"""`itchio-description.html`에서 `preview.html`을 만든다.

두 파일을 손으로 따로 고치다 보면 반드시 어긋난다(실제로 `alt` 문구가 양쪽에
낡은 채로 남아 있었다). 그래서 프리뷰는 **설명 HTML에서 생성**한다.

하는 일은 두 가지뿐이다.
  1. `IMG_01_TITLE` 같은 자리표시자를 `images/01_title.png` 로 바꾼다(이름 규칙 그대로).
  2. itch.io 게임 페이지와 같은 껍데기(#wrapper > .formatted_description, 폭 553px)에 넣는다.
"""
import glob, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
# 인자로 설명 HTML 이름을 주면 그 파일의 프리뷰를 만든다(기본은 GBJAM 레이아웃판).
NAME = sys.argv[1] if len(sys.argv) > 1 else "itchio-description.html"
SRC = os.path.join(HERE, NAME)
DST = os.path.join(HERE, "preview" + NAME.replace("itchio-description", "").replace(".html", "") + ".html")

body = open(SRC, encoding="utf-8").read()

# IMG_01_TITLE -> images/01_title.png (파일이 실제로 있는지도 확인한다)
have = {os.path.basename(p) for p in glob.glob(os.path.join(HERE, "images", "*.png"))}
missing, used = [], set()


def swap(m):
    name = m.group(1).lower() + ".png"
    used.add(name)
    if name not in have:
        missing.append(name)
    return 'src="images/%s"' % name


body, n = re.subn(r'src="IMG_([0-9A-Z_]+)"', swap, body)

TOP = """<!doctype html>
<html lang="ko">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>컴스톡 itch.io 설명 미리보기</title>
  <link rel="stylesheet" href="itchio-custom.css">
  <style>
    html { background: #100e0d; }
    body { margin: 0; color: #f2ece7; background: #1a1817; font-family: Arial, sans-serif; }
    .preview-topbar { padding: 12px 20px; color: #f2ece7; background: #2f2926;
                      border-bottom: 4px solid #fa5c2f; font-weight: 700; }
    #wrapper { width: min(960px, calc(100% - 32px)); margin: 48px auto; }
    /* itch.io 게임 페이지의 본문 칸 실측 폭(553px)과 같게 맞춘다. */
    .formatted_description { width: min(553px, 100%); margin: 0 auto; }
  </style>
</head>
<body>
  <div class="preview-topbar">itch.io / COMSTOCK — DESCRIPTION PREVIEW</div>
  <div id="wrapper">
    <main class="formatted_description">
"""
BOTTOM = """    </main>
  </div>
</body>
</html>
"""

open(DST, "w", encoding="utf-8").write(TOP + body + BOTTOM)

print("자리표시자 %d개 치환" % n)
if missing:
    print("!! images/에 없는 파일:", ", ".join(sorted(set(missing))))
# "안 쓰는 그림" 검사는 기본 설명(그림 19장 전부를 쓰는 쪽)에서만 뜻이 있다.
extra = sorted(have - used) if NAME == "itchio-description.html" else []
if extra:
    print("!! HTML이 안 쓰는 그림:", ", ".join(extra))
if not missing and not extra:
    print("그림 %d장 전부 짝이 맞는다." % len(have))
