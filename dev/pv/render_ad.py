# -*- coding: utf-8 -*-
"""컴스톡 광고 영상 공용 렌더러 - 스펙 모듈(ad_safety / ad_helpdesk)을 받아 렌더한다.

스펙 모듈이 W, H, FPS, TIMELINE, DUR, SCENES, CUTS, SHAKES, VIGNETTE를 노출하면 여기서
후처리(컷 플래시 / 도장 충격 / 비네트 / 암전)와 인코딩만 맡는다. 가로 30초판과 세로 15초판이
같은 코드를 쓴다.

사용법:
    python render_ad.py --spec safety   --lang ko --sheet 1,4,10,20
    python render_ad.py --spec helpdesk --lang ko --out Comstock_Ad_Helpdesk_KO.mp4 --audio a.m4a
"""
import argparse
import importlib
import math
import os
import subprocess
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv_common import CACHE

PAD = 22


def load(spec):
    return importlib.import_module("ad_" + spec)


def flash_at(S, t):
    """컷 지점 = 슬라이드가 넘어가듯 화면이 한 번 하얗게 튄다."""
    v = 0.0
    for c in getattr(S, "CUTS", ()):
        if 0 <= t - c < 0.13:
            v = max(v, 0.55 * (1 - (t - c) / 0.13) ** 1.4)
    return v


def shake_at(S, t):
    """도장이 찍히는 순간의 짧은 충격."""
    dx = dy = 0.0
    for (c, amp) in getattr(S, "SHAKES", ()):
        d = t - c
        if 0 <= d < 0.24:
            k = amp * (1 - d / 0.24) ** 1.6
            dx += math.sin(d * 92.0) * k
            dy += math.sin(d * 71.0 + 1.1) * k * 0.7
    return dx, dy


_vig = {}


def vignette(W, H, strength):
    key = (W, H, round(strength, 2))
    m = _vig.get(key)
    if m is None:
        p = os.path.join(CACHE, "ad_vig_%dx%d_%d.png" % (W, H, int(strength * 100)))
        if os.path.exists(p):
            m = Image.open(p).convert("L")
        else:
            sw, sh = W // 4, H // 4
            m = Image.new("L", (sw, sh))
            px = m.load()
            for y in range(sh):
                for x in range(sw):
                    ddx = (x - sw / 2) / (sw / 2)
                    ddy = (y - sh / 2) / (sh / 2)
                    r = math.sqrt(ddx * ddx + ddy * ddy) / 1.414
                    px[x, y] = int(max(0, min(255, (1.0 - strength * r ** 2.3) * 255)))
            m = m.resize((W, H), Image.Resampling.BILINEAR)
            os.makedirs(CACHE, exist_ok=True)
            m.save(p)
        _vig[key] = m
    return m


def scene_at(S, t):
    for (t0, d, n) in S.TIMELINE:
        if t < t0 + d:
            return n, t - t0, d
    t0, d, n = S.TIMELINE[-1]
    return n, d, d


def render_content(S, t, lang):
    name, tl, dur = scene_at(S, t)
    cnv = Image.new("RGB", (S.W, S.H), (0, 0, 0))
    S.SCENES[name](cnv, tl, dur, lang)
    return cnv


def post_process(S, im, t):
    W, H = S.W, S.H
    dx, dy = shake_at(S, t)
    if abs(dx) > 0.4 or abs(dy) > 0.4:
        big = im.resize((W + PAD * 2, H + PAD * 2), Image.Resampling.BILINEAR)
        x = int(round(PAD + dx))
        y = int(round(PAD + dy))
        im = big.crop((x, y, x + W, y + H))

    fl = flash_at(S, t)
    if fl > 0.01:
        im = Image.blend(im, Image.new("RGB", (W, H), (255, 255, 255)), min(0.85, fl))

    st = getattr(S, "VIGNETTE", 0.0)
    if st > 0.01:
        from PIL import ImageChops
        v = vignette(W, H, st)
        r, g, b = im.split()
        im = Image.merge("RGB", (ImageChops.multiply(r, v), ImageChops.multiply(g, v),
                                 ImageChops.multiply(b, v)))

    k = 1.0
    if t < 0.22:
        k = t / 0.22
    if t > S.DUR - 0.35:
        k = min(k, max(0.0, (S.DUR - t) / 0.35))
    if k < 0.999:
        im = Image.blend(Image.new("RGB", (W, H), (0, 0, 0)), im, k)
    return im


def frame(S, f, lang):
    t = f / S.FPS
    return post_process(S, render_content(S, t, lang), t)


def encode(S, lang, out_path, audio=None, quiet=False):
    nf = int(round(S.FPS * S.DUR))
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (S.W, S.H),
           "-r", str(S.FPS), "-i", "-"]
    if audio:
        cmd += ["-i", audio]
    cmd += ["-vf", "format=yuv420p", "-c:v", "libx264", "-preset", "slow", "-crf", "19",
            "-pix_fmt", "yuv420p", "-r", str(S.FPS), "-movflags", "+faststart"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
    cmd += [out_path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(nf):
        p.stdin.write(frame(S, f, lang).tobytes())
        if not quiet and f % 30 == 0:
            print("  %s %s  %4d/%d  (%.1fs)" % (S.NAME, lang, f, nf, f / S.FPS), flush=True)
    p.stdin.close()
    rc = p.wait()
    if rc != 0:
        raise SystemExit("ffmpeg 실패 (rc=%d)" % rc)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", required=True, choices=("safety", "helpdesk"))
    ap.add_argument("--lang", default="ko", choices=("en", "ko"))
    ap.add_argument("--out", default=None)
    ap.add_argument("--audio", default=None)
    ap.add_argument("--test", default=None)
    ap.add_argument("--sheet", default=None)
    ap.add_argument("--cols", type=int, default=0)
    args = ap.parse_args()
    S = load(args.spec)

    if args.test or args.sheet:
        ts = [float(x) for x in (args.test or args.sheet).split(",")]
        outdir = os.path.join(CACHE, "preview_ad_" + args.spec)
        os.makedirs(outdir, exist_ok=True)
        ims = []
        for t in ts:
            im = frame(S, int(round(t * S.FPS)), args.lang)
            ims.append((t, im))
            if args.test:
                p = os.path.join(outdir, "t%06.2f_%s.png" % (t, args.lang))
                im.save(p)
                print(p)
        if args.sheet:
            cols = args.cols or (2 if S.W >= S.H else 4)
            rows = (len(ims) + cols - 1) // cols
            tw = max(220, 1240 // cols)
            th = int(tw * S.H / S.W)
            sheet = Image.new("RGB", (tw * cols, th * rows), (0, 0, 0))
            d = ImageDraw.Draw(sheet)
            for i, (t, im) in enumerate(ims):
                sheet.paste(im.resize((tw, th), Image.Resampling.LANCZOS),
                            ((i % cols) * tw, (i // cols) * th))
                d.text(((i % cols) * tw + 6, (i // cols) * th + 4), "%.2fs" % t,
                       fill=(255, 235, 110))
            p = os.path.join(outdir, "sheet_%s.jpg" % args.lang)
            sheet.save(p, quality=90)
            print(p)
        return

    out = args.out or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                   "Comstock_Ad_%s_%s.mp4" % (S.NAME, args.lang.upper()))
    encode(S, args.lang, out, args.audio)
    print("완성:", out)


if __name__ == "__main__":
    main()
