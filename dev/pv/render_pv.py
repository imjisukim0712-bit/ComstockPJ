# -*- coding: utf-8 -*-
"""컴스톡 PV 렌더러 - 장면을 그린 뒤 '옛날 흑백 TV' 처리를 입혀 mp4로 굽는다.

사용법:
    python render_pv.py --lang en --out ..\\..\\dev\\pv\\Comstock_PV_EN.mp4
    python render_pv.py --lang ko --test 0.4,1.6,4.2   (미리보기 PNG만)
"""
import argparse
import math
import os
import random
import subprocess
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pv_common import W, H, OUT_W, OUT_H, FPS, NF, DUR, CUTS, ROLLS, scene_at, CACHE
import pv_scenes as S
from pv_draw import BILINEAR, LANCZOS

# ---------------------------------------------------------------- 톤 커브
# 셀셰이딩 아트를 옛날 필름처럼 대비가 센 흑백으로 만든다.
_LUT = []
for v in range(256):
    x = v / 255.0
    x = (x - 0.5) * 1.42 + 0.52          # 대비
    x = max(0.0, min(1.0, x))
    x = x ** 0.92                         # 살짝 밝게
    _LUT.append(int(max(0, min(255, x * 255))))


def _scan_masks():
    masks = []
    for phase in range(3):
        m = Image.new("L", (1, H), 255)
        d = ImageDraw.Draw(m)
        for y in range(phase, H, 3):
            d.point((0, y), fill=196)
        masks.append(m.resize((W, H)))
    return masks


def _vignette():
    p = os.path.join(CACHE, "vignette_%dx%d.png" % (W, H))
    if os.path.exists(p):
        return Image.open(p).convert("L")
    sw, sh = W // 4, H // 4
    m = Image.new("L", (sw, sh))
    px = m.load()
    for y in range(sh):
        for x in range(sw):
            dx = (x - sw / 2) / (sw / 2)
            dy = (y - sh / 2) / (sh / 2)
            r = math.sqrt(dx * dx + dy * dy) / 1.414
            v = 1.0 - 0.86 * (r ** 2.6)
            px[x, y] = int(max(0, min(255, v * 255)))
    m = m.resize((W, H), BILINEAR)
    corner = Image.new("L", (W, H), 0)
    ImageDraw.Draw(corner).rounded_rectangle([6, 6, W - 6, H - 6], radius=78, fill=255)
    corner = corner.filter(ImageFilter.GaussianBlur(14))
    m = ImageChops.multiply(m, corner)
    os.makedirs(CACHE, exist_ok=True)
    m.save(p)
    return m


SCAN = None
VIGN = None


# ---------------------------------------------------------------- 스케줄
def static_at(t):
    """지직거림 세기(0~1). 장면이 바뀌는 지점에서 확 튄다."""
    v = 0.11
    for c in CUTS:
        v += 0.92 * math.exp(-((t - c) / 0.085) ** 2)
    if 6.2 < t < 7.2:                     # 좀비가 좁혀올 때 점점 심해진다 (문제 제기 4.6~7.2)
        v += 0.315 * (t - 6.2)
    if 29.70 < t < 30.15:                 # 보스가 터질 때 (industrial 27.7 + 2.0)
        v += 0.40
    return min(1.0, v)


def shake_at(t):
    if 15.0 < t < 16.6:                   # 3단계: 총 8정 난사 (steps 11.8 + 3.2)
        return 4.0
    if 27.7 < t < 30.8:                   # 보스 시연
        return 3.0 + (3.0 if t > 29.6 else 0.0)
    if 33.7 < t < 36.4:                   # 지금 바로!
        return 3.2
    if 22.1 < t < 25.0:                   # 사용 전/후
        return 2.0
    if 4.6 < t < 7.2:                     # 좀비가 좁혀온다 (문제 제기 씬)
        return 1.7
    if 0.8 < t < 3.6:                     # 로고 영상 구간은 화면을 흔들지 않는다
        return 0.0
    return 0.8


def roll_at(t):
    for (rt, rd) in ROLLS:
        if rt <= t < rt + rd:
            p = (t - rt) / rd
            return int(H * (1 - p) * 0.9 * math.sin(p * math.pi * 3.0))
    return 0


# ---------------------------------------------------------------- 장면 렌더
def render_content(t, lang):
    name, tl, dur = scene_at(t)
    cnv = Image.new("RGB", (W, H), (10, 10, 10))
    fn = S.SCENES[name]
    if name == "tv_on":
        fn(cnv, tl, dur, lang, inner=render_content(0.84, lang))
    elif name == "tv_off":
        fn(cnv, tl, dur, lang, inner=render_content(39.94, lang))
    else:
        fn(cnv, tl, dur, lang)
    return cnv


# ---------------------------------------------------------------- TV 처리
def tv_process(rgb, f):
    global SCAN, VIGN
    if SCAN is None:
        SCAN = _scan_masks()
        VIGN = _vignette()
    t = f / FPS
    rng = random.Random(f * 7919 + 13)
    g = rgb.convert("L").point(_LUT)

    # 브라운관 발광(밝은 곳이 번진다)
    small = g.resize((W // 3, H // 3), BILINEAR).filter(ImageFilter.GaussianBlur(2.4))
    bright = small.point(lambda v: 0 if v < 150 else min(255, int((v - 150) * 1.9)))
    g = ImageChops.screen(g, bright.resize((W, H), BILINEAR).point(lambda v: int(v * 0.42)))

    # 주사선
    g = ImageChops.multiply(g, SCAN[f % 3])

    # 수평 동기가 흐르는 띠
    by = int((t * 128) % (H + 260)) - 130
    if -120 < by < H:
        y0, y1 = max(0, by), min(H, by + 118)
        if y1 - y0 > 4:
            band = g.crop((0, y0, W, y1))
            band = ImageChops.offset(band, 7, 0).point(lambda v: min(255, int(v * 1.14) + 8))
            g.paste(band, (0, y0))
            d = ImageDraw.Draw(g)
            d.line([(0, y0), (W, y0)], fill=54)

    # 정전기
    st = static_at(t)
    noise = Image.effect_noise((W, H), 40 + 90 * st)
    g = Image.blend(g, ImageChops.overlay(g, noise), 0.16 + 0.34 * st)
    if st > 0.5:
        snow = Image.effect_noise((W, H), 160)
        g = Image.blend(g, snow, (st - 0.5) * 1.35)

    # 화면 찢김
    if st > 0.35 or rng.random() < 0.06:
        for _ in range(int(2 + 9 * st)):
            y0 = rng.randrange(0, H - 6)
            hh = rng.randint(4, 46)
            band = g.crop((0, y0, W, min(H, y0 + hh)))
            g.paste(ImageChops.offset(band, rng.randint(-52, 52), 0), (0, y0))

    # 세로 흐름(수직 동기 이탈)
    ro = roll_at(t)
    if ro:
        g = ImageChops.offset(g, 0, ro)
        d = ImageDraw.Draw(g)
        sy = ro % H
        d.rectangle([0, sy - 4, W, sy + 4], fill=28)

    # 필름 먼지/스크래치 (초당 12장으로만 바뀐다)
    drng = random.Random(int(t * 12) * 331)
    d = ImageDraw.Draw(g)
    for _ in range(drng.randint(0, 3)):
        x = drng.randrange(W)
        y0 = drng.randrange(H)
        d.line([(x, y0), (x + drng.randint(-2, 2), y0 + drng.randint(20, 190))],
               fill=drng.choice((240, 236, 30)), width=drng.randint(1, 2))
    for _ in range(drng.randint(2, 9)):
        x, y = drng.randrange(W), drng.randrange(H)
        r = drng.randint(1, 3)
        d.ellipse([x, y, x + r, y + r], fill=drng.choice((250, 20)))

    # 밝기 깜빡임 + 흔들림
    fl = 1.0 + (rng.random() - 0.5) * 0.11
    if fl != 1.0:
        g = g.point(lambda v: max(0, min(255, int(v * fl))))
    sh = shake_at(t)
    if sh > 0:
        srng = random.Random(int(t * 24) * 17)
        g = ImageChops.offset(g, int((srng.random() * 2 - 1) * sh),
                              int((srng.random() * 2 - 1) * sh * 0.6))

    g = ImageChops.multiply(g, VIGN)
    return g


# ---------------------------------------------------------------- 출력
def encode(lang, out_path, audio=None, quiet=False):
    vf = ("lenscorrection=cx=0.5:cy=0.5:k1=-0.12:k2=-0.015:i=bilinear,"
          "scale=%d:%d,pad=%d:%d:(ow-iw)/2:0:black,format=gray,format=yuv420p"
          % (W, H, OUT_W, OUT_H))
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "gray", "-s", "%dx%d" % (W, H),
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
        img = tv_process(render_content(f / FPS, lang), f)
        p.stdin.write(img.tobytes())
        if not quiet and f % 48 == 0:
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
    ap.add_argument("--raw", action="store_true", help="TV 처리 없이 원본 장면만")
    args = ap.parse_args()

    if args.test:
        outdir = os.path.join(CACHE, "preview")
        os.makedirs(outdir, exist_ok=True)
        for s in args.test.split(","):
            t = float(s)
            f = int(round(t * FPS))
            im = render_content(f / FPS, args.lang)
            if not args.raw:
                im = tv_process(im, f)
            p = os.path.join(outdir, "t%06.2f_%s.png" % (t, args.lang))
            im.save(p)
            print(p)
        return

    out = args.out or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                   "Comstock_PV_%s.mp4" % args.lang.upper())
    encode(args.lang, out, args.audio)
    print("완성:", out)


if __name__ == "__main__":
    main()
