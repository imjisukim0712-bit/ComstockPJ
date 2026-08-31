# -*- coding: utf-8 -*-
"""컴스톡 쇼츠 검증 - 눈으로 보지 말고 픽셀로 재서 확인한다.

확인 항목
1. 화면 밖으로 나간 그림이 없는가(가장자리 픽셀이 배경색 그대로인가)
2. 로봇과 품목 뭉치가 겹치는가(왼쪽 덩어리와 오른쪽 덩어리 사이에 빈 칸이 있는가)
3. 로봇이 실제로 박자에 맞춰 통통 튀는가(프레임별 세로 위치/폭 변화량)
4. 판정 표시가 구간마다 제때 뜨고 색이 맞는가
5. 마지막 골드 구간에서 로봇이 커지는가
6. 상단 제목이 지원 언어 전부에서 두부(□) 없이 그려지는가

    python verify_short.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from PIL import Image

from short_common import (W, H, BG, FPS, DUR, NF, SEG_LEN, T0, SEG_MARK, SEGMENTS,
                          ROBOT_CX, GREEN, RED, LANG)
from render_short import frame

FAIL = []


def check(ok, msg):
    print(("  OK   " if ok else "  FAIL ") + msg)
    if not ok:
        FAIL.append(msg)


# 상단 제목과 워터마크는 "배경이 아닌 픽셀"이지만 로봇/품목이 아니다. 재는 구간에서
# 빼기 위해 무대(로봇·품목이 사는 세로 구간)를 따로 잡아 둔다.
STAGE_Y0, STAGE_Y1 = 600, 1300


def mask_of(im, tol=12, y0=0, y1=None):
    """배경(흰색)이 아닌 픽셀을 True로 하는 2차원 리스트를 만든다(y0~y1 밖은 전부 0)."""
    px = im.load()
    y1 = im.height if y1 is None else y1
    out = []
    for y in range(im.height):
        row = bytearray(im.width)
        if not (y0 <= y < y1):
            out.append(row)
            continue
        for x in range(im.width):
            r, g, b = px[x, y]
            if abs(r - BG[0]) > tol or abs(g - BG[1]) > tol or abs(b - BG[2]) > tol:
                row[x] = 1
        out.append(row)
    return out


def columns_used(m):
    cols = set()
    for row in m:
        for x, v in enumerate(row):
            if v:
                cols.add(x)
    return cols


def bbox_of(m, x0=0, x1=W):
    xs0, xs1, ys0, ys1, n = W, -1, H, -1, 0
    for y, row in enumerate(m):
        for x in range(x0, x1):
            if row[x]:
                n += 1
                if x < xs0:
                    xs0 = x
                if x > xs1:
                    xs1 = x
                if y < ys0:
                    ys0 = y
                if y > ys1:
                    ys1 = y
    return (xs0, ys0, xs1, ys1, n) if n else None


def main():
    print("1) 화면 밖으로 나간 그림 / 가장자리 침범")
    worst = None
    for n in range(0, NF, 3):
        im = frame(n / FPS)
        px = im.load()
        bad = 0
        for x in range(W):
            for y in (0, 1, H - 2, H - 1):
                if px[x, y] != BG:
                    bad += 1
        for y in range(H):
            for x in (0, 1, W - 2, W - 1):
                if px[x, y] != BG:
                    bad += 1
        if bad:
            worst = (n, bad)
            break
    check(worst is None, f"가장자리 침범 {'없음' if worst is None else worst}")

    print("2) 로봇과 품목 뭉치가 떨어져 있는가 (구간마다 가장 벌어진 시점)")
    splits = {}
    for i, (key, ok) in enumerate(SEGMENTS):
        t = T0 + i * SEG_LEN + 1.2          # 품목이 완전히 자리잡은 시각
        m = mask_of(frame(t), y0=STAGE_Y0, y1=STAGE_Y1)
        cols = columns_used(m)
        # 로봇 쪽(왼쪽)과 품목 쪽(오른쪽) 사이에 완전히 빈 세로 띠가 있어야 한다.
        best, cur, end = 0, 0, 0
        for x in range(400, 1000):
            if x not in cols:
                cur += 1
                if cur > best:
                    best, end = cur, x
            else:
                cur = 0
        check(best >= 8, f"{key:6s} 로봇-품목 사이 빈 띠 {best}px (8px 이상이어야 함)")
        split = end - best // 2 if best else 700
        splits[key] = split
        it = bbox_of(m, split, W)
        if it:
            check(it[2] <= W - 12, f"{key:6s} 품목 오른쪽 끝 x={it[2]} (화면 안)")

    print("3) 로봇이 박자에 맞춰 튀는가")
    split = splits["zombie"]
    ys, ws = [], []
    for n in range(0, 60):                   # 첫 2초 = 박자 약 3.7회
        m = mask_of(frame(n / FPS), y0=STAGE_Y0, y1=STAGE_Y1)
        b = bbox_of(m, 0, split)             # 로봇만(품목은 빈 띠 오른쪽)
        ys.append(b[1])
        ws.append(b[2] - b[0])
    check(max(ys) - min(ys) >= 4, f"로봇 윗변 변화폭 {max(ys) - min(ys)}px")
    check(max(ws) - min(ws) >= 20, f"로봇 폭 변화폭(스쿼시) {max(ws) - min(ws)}px")

    print("4) 판정 표시 등장 시각과 색")
    for i, (key, ok) in enumerate(SEGMENTS):
        t0 = T0 + i * SEG_LEN
        before = frame(t0 + SEG_MARK - 0.10)
        after = frame(t0 + SEG_MARK + 0.30)
        want = GREEN if ok else RED

        def count(im, col, tol=45):
            px = im.load()
            c = 0
            for y in range(1150, 1650, 2):
                for x in range(60, 560, 2):
                    r, g, b = px[x, y]
                    if abs(r - col[0]) < tol and abs(g - col[1]) < tol and abs(b - col[2]) < tol:
                        c += 1
            return c

        check(count(before, want) == 0, f"{key:6s} 판정 전({SEG_MARK - 0.10:.2f}s)에는 표시가 없다")
        check(count(after, want) > 200, f"{key:6s} 판정 후 {'O' if ok else 'X'} 색 픽셀 {count(after, want)}개")
        other = RED if ok else GREEN
        check(count(after, other) == 0, f"{key:6s} 반대 색은 안 나온다")

    print("5) 골드 구간에서 로봇이 커지는가")
    # 두 시각 모두 "구간 시작 +1.0초"라 박자 위상이 같다(1.0 / 0.54 = 1.852박) - 그래야
    # 스쿼시가 섞이지 않고 확대만 비교된다.
    sp = min(splits["disc"], splits["gold"])
    b1 = bbox_of(mask_of(frame(T0 + SEG_LEN * 2 + 1.0), y0=STAGE_Y0, y1=STAGE_Y1), 0, sp)
    b2 = bbox_of(mask_of(frame(T0 + SEG_LEN * 3 + 1.0), y0=STAGE_Y0, y1=STAGE_Y1), 0, sp)
    ratio = (b2[2] - b2[0]) / (b1[2] - b1[0])
    check(1.02 <= ratio <= 1.12,
          f"로봇 폭 비율 {ratio:.3f} (레퍼런스 1.28은 화면을 벗어나 1.05로 낮추고 홉/기울기로 보완)")

    print("6) 상단 제목이 언어마다 제대로 그려지는가")
    for lang in sorted(LANG):
        m = mask_of(frame(0.0, lang), y0=180, y1=340)
        b = bbox_of(m)
        check(b is not None and b[4] > 400,
              f"{lang} 제목 글자 픽셀 {0 if b is None else b[4]}개")
        if b:
            check(60 <= b[0] and b[2] <= W - 60, f"{lang} 제목 좌우 x={b[0]}~{b[2]} (여백 안)")
            # 두부(□)는 속이 빈 네모라 같은 크기의 글자보다 픽셀이 훨씬 적다. 채움 비율로 거른다.
            fill = b[4] / max(1, (b[2] - b[0] + 1) * (b[3] - b[1] + 1))
            check(fill > 0.14, f"{lang} 제목 채움 비율 {fill:.3f} (두부 글자가 아니다)")

    print()
    if FAIL:
        print(f"실패 {len(FAIL)}건:")
        for f in FAIL:
            print("  -", f)
        sys.exit(1)
    print("전부 통과")


if __name__ == "__main__":
    main()
