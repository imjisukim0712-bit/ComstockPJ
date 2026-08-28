# -*- coding: utf-8 -*-
"""컴스톡 숏츠 광고 렌더러 - 41초 인포머셜 PV의 흑백 CRT 톤을 쓰지 않는다.

컬러 그대로 장면을 그리고, 컷마다 화면이 살짝 하얗게 번쩍이는 "펀치 컷"과 코믹한
화면 흔들림만 얹는다. 장면 자체는 shorts_scenes.py(완전히 새로 쓴 4개)를 쓴다.

사용법:
    python render_shorts.py --lang en --out ..\\..\\dev\\pv\\Comstock_Shorts_EN.mp4
    python render_shorts.py --lang ko --test 0.6,3.4  (미리보기 PNG만)
"""
import argparse
import math
import os
import random
import subprocess
import sys

from PIL import Image, ImageChops

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pv_common import W, H, OUT_W, OUT_H, FPS, SHORTS_NF, SHORTS_DUR, SHORTS_TIMELINE, \
    SHORTS_CUTS, scene_at, CACHE
import shorts_scenes as S

NF, DUR = SHORTS_NF, SHORTS_DUR


# ---------------------------------------------------------------- 스케줄
def flash_at(t):
    """컷 지점마다 화면이 잠깐 하얗게 번쩍인다(밈 편집의 펀치 컷)."""
    v = 0.0
    for c in SHORTS_CUTS:
        dt = (t - c) / 0.045
        if -3 < dt < 3:
            v += math.exp(-dt * dt)
    return min(1.0, v)


def shake_at(t):
    if 7.5 < t < 11.0:                    # 난사 개그
        return 3.6
    if 0.0 < t < 1.5:                     # 좀비가 몰려온다
        return 1.4
    return 0.5


def _vignette():
    key = os.path.join(CACHE, "shorts_vignette_%dx%d.png" % (W, H))
    if os.path.exists(key):
        return Image.open(key).convert("L")
    from PIL import ImageDraw, ImageFilter
    sw, sh = W // 4, H // 4
    m = Image.new("L", (sw, sh))
    px = m.load()
    for y in range(sh):
        for x in range(sw):
            dx = (x - sw / 2) / (sw / 2)
            dy = (y - sh / 2) / (sh / 2)
            r = math.sqrt(dx * dx + dy * dy) / 1.414
            v = 1.0 - 0.55 * (r ** 2.6)
            px[x, y] = int(max(0, min(255, v * 255)))
    m = m.resize((W, H), Image.Resampling.BILINEAR)
    os.makedirs(CACHE, exist_ok=True)
    m.save(key)
    return m


VIGN = None


# ---------------------------------------------------------------- 장면 렌더
def render_content(t, lang):
    name, tl, dur = scene_at(t, SHORTS_TIMELINE)
    cnv = Image.new("RGB", (W, H), (10, 10, 10))
    S.SCENES[name](cnv, tl, dur, lang)
    return cnv


def post_process(rgb, f):
    """컬러를 유지한 채 비네트 + 흔들림 + 펀치 컷 플래시만 얹는다(흑백 변환 없음)."""
    global VIGN
    if VIGN is None:
        VIGN = _vignette()
    t = f / FPS
    g = rgb

    sh = shake_at(t)
    if sh > 0:
        srng = random.Random(int(t * 24) * 17)
        g = ImageChops.offset(g, int((srng.random() * 2 - 1) * sh),
                              int((srng.random() * 2 - 1) * sh * 0.6))

    r, gc, b = g.split()
    r = ImageChops.multiply(r, VIGN)
    gc = ImageChops.multiply(gc, VIGN)
    b = ImageChops.multiply(b, VIGN)
    g = Image.merge("RGB", (r, gc, b))

    fl = flash_at(t)
    if fl > 0.01:
        white = Image.new("RGB", (W, H), (255, 255, 255))
        g = Image.blend(g, white, min(1.0, fl))
    return g


# ---------------------------------------------------------------- 출력
def encode(lang, out_path, audio=None, quiet=False):
    vf = ("scale=%d:%d,pad=%d:%d:(ow-iw)/2:0:black,format=yuv420p"
          % (W, H, OUT_W, OUT_H))
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
           "-r", str(FPS), "-i", "-"]
    if audio:
        cmd += ["-i", audio]
    cmd += ["-vf", vf, "-c:v", "libx264", "-preset", "slow", "-crf", "18",
            "-pix_fmt", "yuv420p", "-r", str(FPS), "-movflags", "+faststart"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
    cmd += [out_path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(NF):
        img = post_process(render_content(f / FPS, lang), f)
        p.stdin.write(img.tobytes())
        if not quiet and f % 24 == 0:
            print("  %s  %3d/%d  (%.1fs)" % (lang, f, NF, f / FPS), flush=True)
    p.stdin.close()
    rc = p.wait()
    if rc != 0:
        raise SystemExit("ffmpeg 실패 (rc=%d)" % rc)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="en", choices=("en", "ko"))
    ap.add_argument("--out", default=None)
    ap.add_argument("--audio", default=None)
    ap.add_argument("--test", default=None, help="미리보기할 시각(초), 쉼표 구분")
    ap.add_argument("--raw", action="store_true", help="후처리 없이 원본 장면만")
    args = ap.parse_args()

    if args.test:
        outdir = os.path.join(CACHE, "preview_shorts")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            f = int(round(t * FPS))
            im = render_content(f / FPS, args.lang)
            if not args.raw:
                im = post_process(im, f)
            p = os.path.join(outdir, "t%06.2f_%s.png" % (t, args.lang))
            im.save(p)
            print(p)
        return

    out = args.out or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                   "Comstock_Shorts_%s.mp4" % args.lang.upper())
    encode(args.lang, out, args.audio)
    print("완성:", out)


if __name__ == "__main__":
    main()
