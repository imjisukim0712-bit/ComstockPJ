# -*- coding: utf-8 -*-
"""컴스톡 PV "야생의 좀비" 렌더러 - 다큐멘터리 카메라/색감 후처리.

기존 두 렌더러와 후처리가 겹치지 않는다.
  - render_pv.py: 흑백 변환 + 주사선 + 정전기 + 배럴 왜곡 (옛날 TV)   -> 안 쓴다
  - render_shorts.py: 원색 유지 + 화이트 플래시 펀치 컷              -> 안 쓴다
  - 여기: 손으로 든 카메라 흔들림(부드러운 사인 합성) + 초점 놓침(전면 블러) +
          따뜻한 필름 그레이딩(채널별 커브) + 약한 비네트 + 장면 간 디졸브.

사용법:
    python render_wild.py --lang ko --test 0.8,4.5,10,15,20,26,31
    python render_wild.py --lang en --out Comstock_PV_Wild_EN.mp4 --audio a.m4a
"""
import argparse
import math
import os
import subprocess
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from wild_common import W, H, FPS, NF, DUR, TIMELINE, DISSOLVE_IN, FOCUS, SHAKE
from pv_common import CACHE
import wild_scenes as S

PAD = 24                      # 카메라 흔들림 여유분(잘라낼 테두리)


# ---------------------------------------------------------------- 카메라
def _shake_amp(t):
    for (t_end, a) in SHAKE:
        if t < t_end:
            return a
    return SHAKE[-1][1]


def cam_offset(t):
    """손으로 든 카메라 - 주파수가 다른 사인 3개를 겹쳐 부드럽게 떠돌게 한다.

    숏츠판처럼 프레임마다 난수를 뽑으면 '덜덜'거리는 만화 흔들림이 되고, 다큐의
    '사람이 들고 있는' 느낌은 저주파가 살아 있어야 나온다.
    """
    a = _shake_amp(t)
    dx = (math.sin(t * 2.13) * 0.62 + math.sin(t * 5.70 + 1.3) * 0.26
          + math.sin(t * 11.1 + 0.7) * 0.12) * a
    dy = (math.sin(t * 1.77 + 2.0) * 0.54 + math.sin(t * 4.30 + 0.4) * 0.30
          + math.sin(t * 9.30 + 1.9) * 0.16) * a
    return dx, dy


def focus_blur(t):
    """초점 이동 - (시작, 끝, 시작반경, 끝반경) 구간을 선형 보간해 최대값을 쓴다.

    콜드 오픈은 흐릿하게 시작해 초점이 맞고, 사냥 중에는 두 번 놓친다.
    """
    r = 0.0
    for (t0, t1, r0, r1) in FOCUS:
        if t0 <= t < t1 and t1 > t0:
            p = (t - t0) / (t1 - t0)
            r = max(r, r0 + (r1 - r0) * p)
    return r


# ---------------------------------------------------------------- 색감
def _luts():
    def curve(gamma, lift, gain):
        out = []
        for v in range(256):
            x = (v / 255.0) ** gamma
            x = x * x * (3 - 2 * x) * 0.46 + x * 0.54        # S커브(헤이즈를 줄인 만큼 조금 세게)
            out.append(max(0, min(255, int((lift + x * gain) * 255))))
        return out
    return curve(0.955, 0.014, 1.02) + curve(1.0, 0.008, 0.995) + curve(1.07, 0.004, 0.94)


LUT = None
VIGN = None


def _vignette():
    p = os.path.join(CACHE, "wild_vign_%dx%d.png" % (W, H))
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
            px[x, y] = int(max(0, min(255, (1.0 - 0.40 * r ** 2.4) * 255)))
    m = m.resize((W, H), Image.Resampling.BILINEAR)
    os.makedirs(CACHE, exist_ok=True)
    m.save(p)
    return m


# ---------------------------------------------------------------- 장면
def _idx_at(t):
    for i, (t0, d, _n) in enumerate(TIMELINE):
        if t < t0 + d:
            return i
    return len(TIMELINE) - 1


def render_scene(i, tl, lang):
    t0, dur, name = TIMELINE[i]
    cnv = Image.new("RGB", (W + PAD * 2, H + PAD * 2), (0, 0, 0))
    inner = Image.new("RGB", (W, H), (0, 0, 0))
    S.SCENES[name](inner, tl, dur, lang)
    cnv.paste(inner, (PAD, PAD))
    # 흔들림으로 드러나는 테두리는 화면 끝 줄을 늘려 메운다(검은 띠가 보이면 안 된다)
    cnv.paste(inner.crop((0, 0, W, 1)).resize((W, PAD)), (PAD, 0))
    cnv.paste(inner.crop((0, H - 1, W, H)).resize((W, PAD)), (PAD, H + PAD))
    left = cnv.crop((PAD, 0, PAD + 1, H + PAD * 2)).resize((PAD, H + PAD * 2))
    right = cnv.crop((W + PAD - 1, 0, W + PAD, H + PAD * 2)).resize((PAD, H + PAD * 2))
    cnv.paste(left, (0, 0))
    cnv.paste(right, (W + PAD, 0))
    return cnv


def render_content(t, lang):
    """장면 전환은 디졸브다 - 전환 구간에서는 앞 장면을 계속 돌리며 섞는다."""
    i = _idx_at(t)
    t0, dur, _n = TIMELINE[i]
    tl = t - t0
    name = TIMELINE[i][2]
    dis = DISSOLVE_IN.get(name, 0.0)
    cur = render_scene(i, tl, lang)
    if i > 0 and dis > 0 and tl < dis:
        p = tl / dis
        p = p * p * (3 - 2 * p)                     # 부드럽게
        _pt0, pdur, _pn = TIMELINE[i - 1]
        prev = render_scene(i - 1, pdur + tl, lang)  # 앞 장면을 계속 돌리며 섞는다
        cur = Image.blend(prev, cur, p)
    return cur


def post_process(big, t):
    global LUT, VIGN
    if LUT is None:
        LUT = _luts()
        VIGN = _vignette()

    dx, dy = cam_offset(t)
    x = int(round(PAD + dx))
    y = int(round(PAD + dy))
    im = big.crop((x, y, x + W, y + H))

    fb = focus_blur(t)
    if fb > 0.05:
        im = im.filter(ImageFilter.GaussianBlur(fb))

    im = im.point(LUT)

    r, g, b = im.split()
    im = Image.merge("RGB", (ImageChops.multiply(r, VIGN), ImageChops.multiply(g, VIGN),
                             ImageChops.multiply(b, VIGN)))

    # 다큐 오프닝/엔딩의 암전
    k = 1.0
    if t < 0.28:
        k = t / 0.28
    if t > DUR - 0.60:
        k = min(k, max(0.0, (DUR - t) / 0.60))
    if k < 0.999:
        im = Image.blend(Image.new("RGB", (W, H), (0, 0, 0)), im, k)
    return im


def frame(f, lang):
    t = f / FPS
    return post_process(render_content(t, lang), t)


# ---------------------------------------------------------------- 출력
def encode(lang, out_path, audio=None, quiet=False):
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
           "-r", str(FPS), "-i", "-"]
    if audio:
        cmd += ["-i", audio]
    cmd += ["-vf", "format=yuv420p", "-c:v", "libx264", "-preset", "slow", "-crf", "19",
            "-pix_fmt", "yuv420p", "-r", str(FPS), "-movflags", "+faststart"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
    cmd += [out_path]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for f in range(NF):
        p.stdin.write(frame(f, lang).tobytes())
        if not quiet and f % 30 == 0:
            print("  %s  %4d/%d  (%.1fs)" % (lang, f, NF, f / FPS), flush=True)
    p.stdin.close()
    rc = p.wait()
    if rc != 0:
        raise SystemExit("ffmpeg 실패 (rc=%d)" % rc)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lang", default="ko", choices=("en", "ko"))
    ap.add_argument("--out", default=None)
    ap.add_argument("--audio", default=None)
    ap.add_argument("--test", default=None, help="미리보기할 시각(초), 쉼표 구분")
    ap.add_argument("--sheet", default=None, help="여러 시각을 한 장에 모아 붙인다")
    args = ap.parse_args()

    if args.test or args.sheet:
        spec = args.test or args.sheet
        ts = [float(s) for s in spec.split(",")]
        outdir = os.path.join(CACHE, "preview_wild")
        os.makedirs(outdir, exist_ok=True)
        ims = []
        for t in ts:
            f = int(round(t * FPS))
            im = frame(f, args.lang)
            ims.append((t, im))
            if args.test:
                p = os.path.join(outdir, "t%06.2f_%s.png" % (t, args.lang))
                im.save(p)
                print(p)
        if args.sheet:
            cols = 2
            rows = (len(ims) + cols - 1) // cols
            tw, th = W // 2, H // 2
            sheet = Image.new("RGB", (tw * cols, th * rows), (0, 0, 0))
            d = ImageDraw.Draw(sheet)
            for i, (t, im) in enumerate(ims):
                sheet.paste(im.resize((tw, th), Image.Resampling.LANCZOS),
                            ((i % cols) * tw, (i // cols) * th))
                d.text(((i % cols) * tw + 8, (i // cols) * th + 6), "%.2fs" % t,
                       fill=(255, 240, 120))
            p = os.path.join(outdir, "sheet_%s.jpg" % args.lang)
            sheet.save(p, quality=88)
            print(p)
        return

    out = args.out or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                   "Comstock_PV_Wild_%s.mp4" % args.lang.upper())
    encode(args.lang, out, args.audio)
    print("완성:", out)


if __name__ == "__main__":
    main()
