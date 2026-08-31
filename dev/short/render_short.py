# -*- coding: utf-8 -*-
"""컴스톡 쇼츠 「Dam Dididi」 렌더러.

레퍼런스(https://www.youtube.com/shorts/7Il55sXlulw)의 구성을 그대로 따르되
고양이 자리에 컴스톡 로봇을, 오른쪽 품목 자리에 게임 리소스를 넣는다.

    python render_short.py                      # 원곡을 얹은 mp4 (기본)
    python render_short.py --silent             # 무음 mp4
    python render_short.py --game-audio         # 게임 BGM/효과음을 얹은 mp4
    python render_short.py --lang en            # 상단 제목을 영문으로
    python render_short.py --frames 0,30,60     # 특정 프레임만 png로 뽑아 확인
    python render_short.py --contact            # 전체 흐름을 한 장의 콘택트시트로

애니메이션의 박자는 **원곡에서 실측한 값**(110.3 BPM)이다 - `short_common` 참고.

게임 코드·씬·에셋은 건드리지 않는다. 읽기만 한다.
"""
import argparse
import os
import subprocess
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from short_common import (HERE, W, H, BG, FPS, DUR, NF, SEGMENTS, SEG_LEN, seg_at,
                          ROBOT_CX, ROBOT_CY, ROBOT_W, ITEM_CX, ITEM_CY,
                          MARK_CX, MARK_CY, MARK_W, WATERMARK, LANG, DEFAULT_LANG, SONG)
from short_draw import draw_robot, draw_items, draw_mark, draw_title, draw_watermark

OUT_DIR = HERE


def frame(t, lang=DEFAULT_LANG):
    """절대 시각 t의 한 프레임을 그린다."""
    cnv = Image.new("RGBA", (W, H), BG + (255,))

    draw_title(cnv, lang)

    i, st = seg_at(t)
    if i >= 0:
        key, ok = SEGMENTS[i]
        draw_items(cnv, key, ITEM_CX, ITEM_CY, st)
        draw_mark(cnv, ok, MARK_CX, MARK_CY, MARK_W, st)

    # 로봇은 품목 위에 올라오도록 마지막에 그린다(골드 구간에서 커지면서 겹친다).
    draw_robot(cnv, ROBOT_CX, ROBOT_CY, ROBOT_W, t)
    draw_watermark(cnv, WATERMARK)
    return cnv.convert("RGB")


def encode(out, audio=None, crf=18, lang=DEFAULT_LANG):
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-"]
    if audio:
        cmd += ["-i", audio]
    cmd += ["-c:v", "libx264", "-preset", "medium", "-crf", str(crf),
            "-pix_fmt", "yuv420p", "-movflags", "+faststart"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
    cmd += [out]

    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for n in range(NF):
        p.stdin.write(frame(n / FPS, lang).tobytes())
        if n % 30 == 0:
            print(f"  {n:3d}/{NF}", flush=True)
    p.stdin.close()
    if p.wait() != 0:
        raise SystemExit("ffmpeg 인코딩 실패")
    print(f"완성: {out}  ({os.path.getsize(out) / 1e6:.1f} MB)")


def contact_sheet(path, cols=8, rows=5, lang=DEFAULT_LANG):
    """전체 흐름을 한 장으로 펼쳐 본다(레퍼런스 스토리보드와 나란히 비교용)."""
    n = cols * rows
    tw = 200
    th = int(round(tw * H / W))
    sheet = Image.new("RGB", (cols * tw, rows * th), (225, 225, 225))
    for k in range(n):
        t = k * DUR / n
        im = frame(t, lang).resize((tw - 2, th - 2), Image.Resampling.LANCZOS)
        r, c = divmod(k, cols)
        sheet.paste(im, (c * tw + 1, r * th + 1))
    sheet.save(path)
    print(f"콘택트시트: {path} {sheet.size}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(OUT_DIR, "Comstock_DamDidi.mp4"))
    ap.add_argument("--silent", action="store_true", help="소리 없이 뽑는다(다른 음원을 얹을 때)")
    ap.add_argument("--game-audio", action="store_true",
                    help="원곡 대신 게임 BGM/효과음으로 만든 트랙을 넣는다")
    ap.add_argument("--audio-file", default=None, help="지정한 오디오 파일을 쓴다")
    ap.add_argument("--crf", type=int, default=18)
    ap.add_argument("--frames", default=None, help="쉼표로 구분한 프레임 번호만 png로 저장")
    ap.add_argument("--contact", action="store_true")
    ap.add_argument("--lang", default=DEFAULT_LANG, choices=sorted(LANG), help="상단 제목 언어")
    a = ap.parse_args()

    if a.frames:
        for s in a.frames.split(","):
            n = int(s)
            p = os.path.join(OUT_DIR, f"frame_{n:03d}.png")
            frame(n / FPS, a.lang).save(p)
            print(p)
        return
    if a.contact:
        contact_sheet(os.path.join(OUT_DIR, "contact.png"), lang=a.lang)
        return

    # 기본은 **원곡**이다. 애니메이션의 박자(BEAT/T0)가 이 곡에서 실측한 값이라
    # 다른 소리를 넣으면 박자가 어긋난다.
    audio = a.audio_file
    if a.silent:
        audio = None
    elif not audio:
        if a.game_audio:
            import build_short_audio
            audio = build_short_audio.build(os.path.join(OUT_DIR, "short_audio.m4a"))
        elif os.path.exists(SONG):
            audio = SONG
        else:
            raise SystemExit(f"원곡이 없다: {SONG}\n"
                             "  - 파일을 그 경로에 두거나\n"
                             "  - --game-audio (게임 BGM/효과음) 또는 --silent 를 쓸 것")
    encode(a.out, audio, a.crf, a.lang)


if __name__ == "__main__":
    main()
