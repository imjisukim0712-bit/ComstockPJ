# -*- coding: utf-8 -*-
"""컴스톡 세로 쇼츠(9:16) 렌더러 - 기존 40초 PV 장면을 그대로 재사용한다.

두 단계로 굽는다. 배럴 왜곡을 **TV 밴드에만** 걸어야 하기 때문이다
(세로 캔버스 전체에 걸면 자막과 배경까지 휘어 버린다).

    1단계  기존 장면 960x720 렌더 → TV 처리 → ffmpeg 배럴 왜곡 → 중간 mp4
    2단계  배경 + 1단계 mp4 + 세로 자막 레이어 합성 → 최종 1080x1920 mp4

사용법:
    python render_shorts.py --lang ko
    python render_shorts.py --lang en --bg plates/ruined_city.mp4 --audio shorts_en.m4a
    python render_shorts.py --lang ko --test 0.5,3.0,8.0      (미리보기 PNG만)

--bg 는 ComfyUI로 만든 배경 플레이트 영상이다. 생략하면 검은 배경으로 굽는다.
스프라이트는 절대 ComfyUI에 넣지 않는다 - 배경만 AI, 전경은 원본 픽셀 그대로다.
"""
import argparse
import math
import os
import subprocess
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pv_common import W, H, FPS, CACHE
import pv_scenes as S
import render_pv as R
from pv_draw import FNT, text_layer, wobble, ease_out, clamp
from shorts_common import (
    SW, SH, TV_X, TV_Y, CAP_TOP_Y, CAP_BOT_Y, DUR, NF,
    SHORTS_TIMELINE, remap, caption_at,
)

SIDE_MARGIN = 60          # 자막이 좌우로 넘지 않아야 하는 여백
CAP_STROKE = 6            # 자막 외곽선 두께. 그려지는 폭이 좌우로 이만큼 넓어진다.
CAP_WOBBLE = 1.4          # 필름 흔들림 진폭. 이만큼 더 밀려날 수 있다.


# ---------------------------------------------------------------- 장면 렌더
def _draw_scene(name, tl, dur, lang, inner=None):
    cnv = Image.new("RGB", (W, H), (10, 10, 10))
    fn = S.SCENES[name]
    if inner is not None:
        fn(cnv, tl, dur, lang, inner=inner)
    else:
        fn(cnv, tl, dur, lang)
    return cnv


def render_content(t, lang):
    """쇼츠 시각 t의 TV 밴드 내용(960x720, TV 처리 전)."""
    name, tl, dur, _o = remap(t)
    if name == "tv_on":
        # 화면이 켜지면서 드러나는 것은 **쇼츠의 다음 장면**이다.
        # (40초판은 card_presents가 나오지만 쇼츠에는 그 장면이 없다.)
        _s0, d2, n2, _o2 = SHORTS_TIMELINE[1]
        return _draw_scene(name, tl, dur, lang, inner=_draw_scene(n2, 0.04, d2, lang))
    if name == "tv_off":
        _s0, d2, n2, _o2 = SHORTS_TIMELINE[-2]
        return _draw_scene(name, tl, dur, lang,
                           inner=_draw_scene(n2, max(0.0, d2 - 0.06), d2, lang))
    return _draw_scene(name, tl, dur, lang)


def tv_frame(f, lang):
    """쇼츠 f번째 프레임의 TV 밴드(960x720, 흑백 TV 처리까지 끝난 상태).

    ★ TV 처리에는 **원본 PV 시각**을 넘긴다. static_at/shake_at/roll_at은 40초
      타임라인에 맞춰 손으로 맞춘 값이라, 쇼츠 시각을 넣으면 박자가 어긋난다.
      f_orig을 넘기면 tv_process 안에서 t = f_orig/FPS = 원본 시각이 된다.
    """
    t = f / FPS
    _name, _tl, _dur, orig_t = remap(t)
    f_orig = int(round(orig_t * FPS))
    return R.tv_process(render_content(t, lang), f_orig)


# ---------------------------------------------------------------- 세로 자막
def _fit_font(lang, kind, s, size, max_w, stroke=0, slack=0):
    """글자가 좌우 여백을 넘지 않을 때까지 크기를 내린다.

    안 줄이면 긴 한국어/영어 문구가 화면 밖으로 나간다 - 폰 화면이라 잘리면
    바로 티가 난다(정비/상점 이름표에서 겪은 것과 같은 문제).

    ★ 글자 폭만 재면 안 된다. 실제로 그려지는 폭은 **좌우로 stroke만큼 더 넓고**,
      wobble이 최대 amp만큼 더 밀어낸다. 그 둘을 빼지 않아 영어 문구가 3px
      넘쳤었다(측정으로 발견). 재는 대상과 그리는 대상을 같은 재료로 만든다.
    """
    budget = max_w - 2 * (stroke + slack)
    while size > 18:
        f = FNT(lang, kind, size)
        bb = f.getbbox(s)
        if bb is None or (bb[2] - bb[0]) <= budget:
            return f
        size -= 2
    return FNT(lang, kind, size)


def _caption_layer(t, lang):
    """1080x1920 RGBA 자막 레이어. TV 밴드 자리는 비워 둔다."""
    lay = Image.new("RGBA", (SW, SH), (0, 0, 0, 0))
    top, bot, tl, _dur = caption_at(t, lang)
    max_w = SW - SIDE_MARGIN * 2

    # 장면이 시작하고 0.22초에 걸쳐 튀어나온다(40초판 카드 팝과 같은 박자).
    pop = ease_out(clamp(tl / 0.22))
    if pop <= 0.0:
        return lay

    for s, y, size in ((top, CAP_TOP_Y, 78), (bot, CAP_BOT_Y, 60)):
        if not s:
            continue
        f = _fit_font(lang, "punch", s, size, max_w,
                      stroke=CAP_STROKE, slack=CAP_WOBBLE)
        im = text_layer((SW, 200), (SW // 2, 100), s, f,
                        fill=(246, 246, 246), anchor="mm", stroke=CAP_STROKE)
        im = wobble(im, t, amp=CAP_WOBBLE, ang=0.4, seed=int(y))
        if pop < 1.0:
            # 세로로 눌렸다 펴지는 팝. 가로 폭은 건드리지 않는다.
            hh = max(2, int(im.height * (0.35 + 0.65 * pop)))
            im = im.resize((im.width, hh), Image.Resampling.BILINEAR)
        lay.alpha_composite(im, (0, int(y - im.height / 2)))

    return lay


def _tv_border(lay):
    """TV 밴드 둘레에 얇은 테두리. 배경 위에 얹혔을 때 '브라운관'으로 읽히게 한다."""
    d = ImageDraw.Draw(lay)
    d.rounded_rectangle([TV_X - 5, TV_Y - 5, TV_X + W + 4, TV_Y + H + 4],
                        radius=26, outline=(150, 150, 150, 210), width=5)
    d.rounded_rectangle([TV_X - 12, TV_Y - 12, TV_X + W + 11, TV_Y + H + 11],
                        radius=32, outline=(38, 38, 38, 170), width=8)


def caption_frame(f, lang):
    lay = _caption_layer(f / FPS, lang)
    _tv_border(lay)
    return lay


# ---------------------------------------------------------------- 1단계
def encode_tv(lang, out_path, quiet=False):
    """TV 밴드만 960x720으로 굽는다(배럴 왜곡까지 적용)."""
    vf = ("lenscorrection=cx=0.5:cy=0.5:k1=-0.12:k2=-0.015:i=bilinear,"
          "format=gray,format=yuv420p")
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "gray", "-s", "%dx%d" % (W, H),
           "-r", str(FPS), "-i", "-",
           "-vf", vf, "-c:v", "libx264", "-preset", "medium", "-crf", "14",
           "-pix_fmt", "yuv420p", "-r", str(FPS), out_path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(NF):
        p.stdin.write(tv_frame(f, lang).tobytes())
        if not quiet and f % 48 == 0:
            print("  [1/2] TV  %s  %3d/%d  (%.1fs)" % (lang, f, NF, f / FPS), flush=True)
    p.stdin.close()
    if p.wait() != 0:
        raise SystemExit("1단계 ffmpeg 실패")
    return out_path


# ---------------------------------------------------------------- 2단계
def encode_final(lang, tv_path, out_path, bg=None, audio=None, bg_color=False,
                 quiet=False):
    """배경 + TV 밴드 + 세로 자막을 합성해 1080x1920으로 굽는다.

    bg_color=False(기본)면 배경을 흑백으로 깎고 어둡게 눌러 TV 밴드가 튀어나오게
    한다. 40초판이 통째로 흑백이라 컬러 배경을 그대로 쓰면 톤이 깨진다.
    """
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error"]

    # 0: 배경 (AI 플레이트 영상이 없으면 검은 화면)
    if bg:
        cmd += ["-stream_loop", "-1", "-i", bg]
    else:
        cmd += ["-f", "lavfi", "-i", "color=c=black:s=%dx%d:r=%d" % (SW, SH, FPS)]
    # 1: 1단계 결과
    cmd += ["-i", tv_path]
    # 2: 자막 레이어 (stdin)
    cmd += ["-f", "rawvideo", "-pix_fmt", "rgba", "-s", "%dx%d" % (SW, SH),
            "-r", str(FPS), "-i", "-"]
    if audio:
        cmd += ["-i", audio]

    grade = "" if bg_color else ",eq=saturation=0:brightness=-0.14:contrast=1.06"
    fc = ("[0:v]scale=%d:%d:force_original_aspect_ratio=increase,"
          "crop=%d:%d,setsar=1%s,format=yuv420p[bg];"
          "[bg][1:v]overlay=%d:%d[t1];"
          "[t1][2:v]overlay=0:0,format=yuv420p[v]"
          % (SW, SH, SW, SH, grade, TV_X, TV_Y))
    cmd += ["-filter_complex", fc, "-map", "[v]"]
    if audio:
        cmd += ["-map", "3:a", "-c:a", "aac", "-b:a", "192k"]
    cmd += ["-t", "%.3f" % DUR,
            "-c:v", "libx264", "-preset", "slow", "-crf", "19",
            "-pix_fmt", "yuv420p", "-r", str(FPS), "-movflags", "+faststart",
            out_path]

    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(NF):
        p.stdin.write(caption_frame(f, lang).tobytes())
        if not quiet and f % 48 == 0:
            print("  [2/2] 합성  %s  %3d/%d" % (lang, f, NF), flush=True)
    p.stdin.close()
    if p.wait() != 0:
        raise SystemExit("2단계 ffmpeg 실패")
    return out_path


# ---------------------------------------------------------------- 미리보기
def preview(t, lang):
    """세로 한 프레임을 PNG로 (배럴 왜곡은 ffmpeg 담당이라 빠져 있다)."""
    f = int(round(t * FPS))
    cnv = Image.new("RGB", (SW, SH), (0, 0, 0))
    cnv.paste(tv_frame(f, lang).convert("RGB"), (TV_X, TV_Y))
    cnv = cnv.convert("RGBA")
    cnv.alpha_composite(caption_frame(f, lang))
    return cnv.convert("RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="ko", choices=("en", "ko"))
    ap.add_argument("--out", default=None)
    ap.add_argument("--bg", default=None, help="ComfyUI 배경 플레이트 영상")
    ap.add_argument("--audio", default=None)
    ap.add_argument("--test", default=None, help="미리보기할 시각(초), 쉼표 구분")
    ap.add_argument("--keep-tv", action="store_true", help="1단계 중간 mp4를 남긴다")
    ap.add_argument("--bg-color", action="store_true",
                    help="배경을 컬러 그대로 쓴다(기본은 흑백으로 깎아 톤을 맞춘다)")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    print("쇼츠 길이 %.2f초 / %d프레임 / %dx%d" % (DUR, NF, SW, SH))

    if args.test:
        outdir = os.path.join(CACHE, "shorts_preview")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            p = os.path.join(outdir, "s%06.2f_%s.png" % (t, args.lang))
            preview(t, args.lang).save(p)
            print(p)
        return

    os.makedirs(CACHE, exist_ok=True)
    tv_path = os.path.join(CACHE, "shorts_tv_%s.mp4" % args.lang)
    out = args.out or os.path.join(here, "Comstock_Shorts_%s.mp4" % args.lang.upper())

    encode_tv(args.lang, tv_path)
    encode_final(args.lang, tv_path, out, bg=args.bg, audio=args.audio,
                 bg_color=args.bg_color)
    if not args.keep_tv and os.path.exists(tv_path):
        os.remove(tv_path)
    print("완성:", out)


if __name__ == "__main__":
    main()
