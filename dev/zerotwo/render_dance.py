"""제로투 댄스(2 Phut Hon) 0:50~1:05 구간을 마시멜로 로봇으로 다시 만든다.

레퍼런스: https://www.youtube.com/watch?v=XZ7FcT5UvOI (Phao - 2 Phut Hon, KAIZ remix)
그 구간의 화면 문법은 이렇다.

  - 어두운 와인색 -> 마젠타 방사형 배경, 공중에 먼지 입자가 천천히 떠다닌다
  - 캐릭터는 **양팔을 머리 옆으로 올리고 팔꿈치를 벌린 채** 박마다 좌우로 스웨이
  - 2박마다 **마젠타로 물든 커다란 잔상(고스트)이 좌우 번갈아** 나타났다 사라진다
  - 8박(2마디)마다 한쪽 팔이 볼 옆으로 내려오는 변형이 섞인다

유니티를 쓰지 않고 Pillow로 프레임을 그려 ffmpeg으로 인코딩한다(`dev/pv/`와 같은 방식).

사용법
------
    python3 render_dance.py                    # 기본 1280x720 / 30fps / 15초
    python3 render_dance.py --size 1920x1080 --out ../../Comstock_ZeroTwo.mp4
"""

import argparse
import math
import os
import random
import shutil
import subprocess
import sys
from collections import deque

from PIL import Image, ImageChops, ImageDraw, ImageFilter

import character as C

# --- 기본값 ------------------------------------------------------------------

DUR = 15.0  # 레퍼런스 0:50 ~ 1:05
FPS = 30
BPM = 128.0  # 이 값이면 15초가 정확히 32박(8마디)으로 딱 떨어진다
SS = 3  # 캐릭터 슈퍼샘플링 배수

# 배경색
BG_INNER = (126, 24, 62)
BG_OUTER = (26, 7, 17)
GLOW_RGB = (232, 40, 128)

GHOST_ALPHA = 0.56
GHOST_SCALE = 1.30
GHOST_TRAIL_FRAMES = 5  # 몇 프레임 전 모습을 잔상으로 쓸지
GHOST_OFFSET_X = 0.135  # 화면 폭 대비. 더 벌리면 잔상이 아니라 "옆에 선 두 번째 캐릭터"가 된다

FADE = 0.4  # 앞뒤 페이드(초)


# --- 안무 --------------------------------------------------------------------

# 팔 포즈는 (위팔 각도, 아래팔 각도)이고 **오른팔 기준**이다.
# 왼팔은 `180 - a`로 좌우 대칭시킨다(화면 절대 각도이므로 이 식이 곧 미러다).
ARM_UP = (-42.0, -128.0)  # 머리 옆으로 올리고 팔꿈치를 벌린 시그니처 포즈
ARM_CHEEK = (-60.0, -172.0)  # 손을 볼 옆에 대는 변형
ARM_HIP = (58.0, 172.0)  # 손을 허리에(현재 안무에서는 안 쓰지만 변형용으로 남겨 둔다)

# 8박(2마디) 블록마다 어떤 포즈를 쓰는지. [왼팔, 오른팔]
BLOCKS = [
    (ARM_UP, ARM_UP),
    (ARM_CHEEK, ARM_UP),
    (ARM_UP, ARM_UP),
    (ARM_UP, ARM_CHEEK),
]
BLOCK_BEATS = 8
BLEND_BEATS = 0.6  # 블록이 바뀔 때 섞는 구간


def _smoothstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


def _mirror(a):
    """오른팔 각도를 왼팔 각도로."""
    return (180.0 - a[0], 180.0 - a[1])


def _lerp_arm(a, b, w):
    return (a[0] + (b[0] - a[0]) * w, a[1] + (b[1] - a[1]) * w)


def _arm_block(beat):
    """이 박에서 쓸 [왼팔, 오른팔] 포즈. 블록 경계는 부드럽게 섞는다."""
    idx = int(beat // BLOCK_BEATS)
    cur = BLOCKS[idx % len(BLOCKS)]
    into = beat - idx * BLOCK_BEATS
    if into >= BLEND_BEATS:
        return cur
    prev = BLOCKS[(idx - 1) % len(BLOCKS)]
    w = _smoothstep(into / BLEND_BEATS)
    return (_lerp_arm(prev[0], cur[0], w), _lerp_arm(prev[1], cur[1], w))


def dance_pose(t):
    """시각 t(초)의 포즈."""
    beat = t * BPM / 60.0
    # 2박에 한 번 좌우를 왕복한다
    sway = math.sin(beat * math.pi)
    # 박마다 몸이 아래로 내려찍혔다 튀어오른다(정박에서 가장 낮다)
    dip = (0.5 * (1.0 + math.cos(beat * 2.0 * math.pi))) ** 0.7

    p = C.Pose()
    p.body_x = 46.0 * sway
    p.body_y = 22.0 * dip
    p.lean = 8.5 * sway

    # 무게가 실린 쪽 발은 붙어 있고 반대쪽 발뒤꿈치가 살짝 뜬다
    lift_l = max(0.0, sway) * 11.0
    lift_r = max(0.0, -sway) * 11.0
    p.foot_dx = [7.0 * sway, 7.0 * sway]
    p.foot_lift = [lift_l, lift_r]
    p.foot_tilt = [-lift_l * 0.9, lift_r * 0.9]

    # 팔은 몸을 따라 한 박 늦게 흔들린다
    rock = -9.0 * sway
    left_base, right_base = _arm_block(beat)
    p.arm = [
        (_mirror(left_base)[0] + rock, _mirror(left_base)[1] + rock * 0.6),
        (right_base[0] + rock, right_base[1] + rock * 0.6),
    ]

    # 깜빡임
    bt = (t + 0.7) % 3.1
    p.eyes = "half" if bt < 0.05 else "closed" if bt < 0.15 else "half" if bt < 0.2 else "open"
    return p


def ghost_at(beat):
    """이 시점에 띄울 잔상. (좌우, 0..1 세기) 또는 None.

    2박마다 좌우를 번갈아 한 발씩 터뜨리고 1박에 걸쳐 사라진다.
    """
    slot = int(beat // 2)
    age = beat - slot * 2  # 0..2 박
    if age > 1.05:
        return None
    side = 1 if slot % 2 == 0 else -1
    return side, (1.0 - age / 1.05) ** 1.5


# --- 배경 --------------------------------------------------------------------


def make_background(size):
    """방사형 그라데이션 배경. 작게 그려 확대하면 부드럽고 빠르다."""
    w, h = size
    sw, sh = 200, max(1, int(200 * h / w))
    small = Image.new("RGB", (sw, sh))
    px = small.load()
    cx, cy = sw * 0.5, sh * 0.42
    maxd = math.hypot(max(cx, sw - cx), max(cy, sh - cy))
    for y in range(sh):
        for x in range(sw):
            k = min(1.0, math.hypot(x - cx, y - cy) / maxd) ** 0.85
            px[x, y] = tuple(
                int(round(BG_INNER[i] + (BG_OUTER[i] - BG_INNER[i]) * k)) for i in range(3)
            )
    return small.resize(size, Image.BICUBIC)


def make_glow(size):
    """캐릭터 뒤에서 박에 맞춰 뛰는 마젠타 후광(screen으로 얹는다)."""
    w, h = size
    img = Image.new("RGB", (w, h), (0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([w * 0.29, h * 0.14, w * 0.71, h * 0.94], fill=GLOW_RGB)
    return img.filter(ImageFilter.GaussianBlur(radius=w * 0.075))


def make_vignette(size):
    w, h = size
    sw, sh = 160, max(1, int(160 * h / w))
    small = Image.new("L", (sw, sh))
    px = small.load()
    cx, cy = sw * 0.5, sh * 0.5
    maxd = math.hypot(cx, cy)
    for y in range(sh):
        for x in range(sw):
            k = min(1.0, math.hypot(x - cx, y - cy) / maxd)
            px[x, y] = int(round(255 * (1.0 - 0.62 * k**2.1)))
    return small.resize(size, Image.BICUBIC).convert("RGB")


def make_dot_sprite(r):
    """가장자리가 부드러운 흰 점 하나(입자용)."""
    s = r * 6
    img = Image.new("L", (s, s), 0)
    ImageDraw.Draw(img).ellipse([s * 0.5 - r, s * 0.5 - r, s * 0.5 + r, s * 0.5 + r], fill=255)
    return img.filter(ImageFilter.GaussianBlur(radius=r * 0.7))


def make_particles(size, n=110, seed=20260831):
    w, h = size
    rng = random.Random(seed)
    out = []
    for _ in range(n):
        out.append(
            {
                "x": rng.uniform(-0.05, 1.05),
                "y": rng.uniform(-0.05, 1.05),
                "r": rng.uniform(1.4, 5.2),
                "vy": rng.uniform(0.010, 0.045),  # 화면 높이 대비 초당
                "vx": rng.uniform(-0.012, 0.006),
                "a": rng.uniform(0.25, 0.95),
                "tw": rng.uniform(0.0, 6.28),
            }
        )
    return out


def draw_particles(frame, parts, t, sprites, size):
    w, h = size
    for p in parts:
        y = (p["y"] - p["vy"] * t) % 1.12 - 0.06
        x = (p["x"] + p["vx"] * t) % 1.12 - 0.06
        a = p["a"] * (0.55 + 0.45 * math.sin(t * 2.1 + p["tw"]))
        if a <= 0.02:
            continue
        r = max(1, int(round(p["r"])))
        spr = sprites[min(r, len(sprites) - 1)]
        tint = Image.new("RGB", spr.size, (255, 214, 236))
        mask = spr.point(lambda v, a=a: int(v * a))
        frame.paste(tint, (int(x * w - spr.width / 2), int(y * h - spr.height / 2)), mask)


# --- 잔상 --------------------------------------------------------------------


def tint_ghost(char_img, strength):
    """캐릭터 타일을 마젠타 잔상으로 물들인다. 선화는 어두운 분홍으로 남는다."""
    # 밝기를 조금만 반영해 **납작하게** 만든다. 원본 명암을 그대로 살리면
    # 얼굴이 또렷해져 잔상이 아니라 두 번째 캐릭터로 보인다.
    lum = char_img.convert("L")
    tinted = Image.merge(
        "RGB",
        (
            lum.point(lambda v: min(255, int(v * 0.26 + 164))),
            lum.point(lambda v: min(255, int(v * 0.16 + 22))),
            lum.point(lambda v: min(255, int(v * 0.28 + 96))),
        ),
    )
    alpha = char_img.getchannel("A").point(lambda v: int(v * strength))
    tinted.putalpha(alpha)
    return tinted


# --- 메인 --------------------------------------------------------------------


def ffmpeg_exe():
    exe = shutil.which("ffmpeg")
    if exe:
        return exe
    try:
        import imageio_ffmpeg

        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        sys.exit("ffmpeg을 찾지 못했다. `pip install imageio-ffmpeg` 하거나 ffmpeg을 설치할 것.")


class Scene:
    """한 번만 만들면 되는 배경 재료들을 들고 프레임을 한 장씩 찍어낸다.

    잔상이 **몇 프레임 전 모습**이어야 하므로 캐릭터 이미지 히스토리를 씬이 들고 있다.
    """

    def __init__(self, size, dur):
        self.size = size
        self.dur = dur
        w, h = size
        # 캐릭터가 화면 높이의 약 78%를 차지하도록 배율을 정한다
        fig_h = C.GROUND_Y - (-C.BODY_H / 2 - 25)
        self.scale = (h * 0.78) / fig_h
        self.anchor = C.ANCHOR_PX(self.scale)
        self.cx, self.cy = w * 0.5, h * 0.42

        self.bg = make_background(size)
        self.glow = make_glow(size)
        self.vignette = make_vignette(size)
        self.parts = make_particles(size)
        self.sprites = [make_dot_sprite(max(1, r)) for r in range(7)]
        self.trail = deque(maxlen=GHOST_TRAIL_FRAMES + 1)

    def frame(self, t):
        size = self.size
        w, h = size
        scale, anchor = self.scale, self.anchor
        cx, cy = self.cx, self.cy
        beat = t * BPM / 60.0
        pose = dance_pose(t)

        # 캐릭터를 슈퍼샘플로 그린 뒤 출력 배율로 줄인다
        big = C.draw_character(scale * SS, pose)
        char = big.resize(C.tile_size_px(scale), Image.LANCZOS)
        self.trail.append(char)

        frame = self.bg.copy()

        # 박에 맞춰 뛰는 후광
        pulse = 0.22 + 0.30 * (0.5 * (1.0 + math.cos(beat * 2.0 * math.pi))) ** 1.6
        frame = ImageChops.screen(frame, self.glow.point(lambda v, k=pulse: int(v * k)))

        draw_particles(frame, self.parts, t, self.sprites, size)

        # 잔상 - 몇 프레임 전 모습을 크게 키워 좌우로 흘린다
        g = ghost_at(beat)
        if g is not None:
            side, strength = g
            gh = tint_ghost(self.trail[0], GHOST_ALPHA * strength)
            gh = gh.resize(
                (int(gh.width * GHOST_SCALE), int(gh.height * GHOST_SCALE)), Image.BILINEAR
            )
            gx = cx + side * w * GHOST_OFFSET_X - anchor[0] * GHOST_SCALE
            gy = cy - h * 0.028 - anchor[1] * GHOST_SCALE
            frame.paste(gh, (int(gx), int(gy)), gh)

        # 발밑 그림자 - 몸이 내려앉으면 짙고 넓어진다
        dip = (0.5 * (1.0 + math.cos(beat * 2.0 * math.pi))) ** 0.7
        sh_w = scale * 250 * (0.92 + 0.16 * dip)
        sh_h = scale * 40
        sx = cx + pose.body_x * scale * 0.35
        sy = cy + C.GROUND_Y * scale - sh_h * 0.35
        shadow = Image.new("L", (int(sh_w * 1.6), int(sh_h * 2.4)), 0)
        ImageDraw.Draw(shadow).ellipse(
            [
                shadow.width / 2 - sh_w / 2,
                shadow.height / 2 - sh_h / 2,
                shadow.width / 2 + sh_w / 2,
                shadow.height / 2 + sh_h / 2,
            ],
            fill=int(120 * (0.7 + 0.3 * dip)),
        )
        shadow = shadow.filter(ImageFilter.GaussianBlur(radius=sh_h * 0.5))
        frame.paste(
            Image.new("RGB", shadow.size, (18, 4, 12)),
            (int(sx - shadow.width / 2), int(sy - shadow.height / 2)),
            shadow,
        )

        frame.paste(char, (int(cx - anchor[0]), int(cy - anchor[1])), char)

        frame = ImageChops.multiply(frame, self.vignette)

        # 가벼운 블룸
        bright = frame.point(lambda v: max(0, v - 158) * 2)
        frame = ImageChops.screen(
            frame, bright.filter(ImageFilter.GaussianBlur(radius=w * 0.011))
        )

        # 박에 맞춘 줌 펀치 - 화면 전체가 정박마다 아주 살짝 다가온다
        z = 1.0 + 0.024 * dip
        if z > 1.001:
            iw, ih = w / z, h / z
            frame = frame.resize(
                size,
                Image.BICUBIC,
                box=((w - iw) / 2, (h - ih) / 2, (w + iw) / 2, (h + ih) / 2),
            )

        # 앞뒤 페이드
        k = 1.0
        if t < FADE:
            k = t / FADE
        elif t > self.dur - FADE:
            k = max(0.0, (self.dur - t) / FADE)
        if k < 0.999:
            frame = Image.blend(Image.new("RGB", size, (0, 0, 0)), frame, k)
        return frame


def mux_audio(video_path, audio_path, start, dur):
    """렌더한 영상에 로컬 음원의 start~start+dur 구간을 붙인다.

    노래 파일은 저작권 때문에 리포에 넣지 않는다. 사용자가 가진 파일을 지정해 쓴다.
    """
    exe = ffmpeg_exe()
    tmp = video_path + ".mux.mp4"
    cmd = [
        exe, "-y", "-loglevel", "error",
        "-i", video_path,
        "-ss", str(start), "-t", str(dur), "-i", audio_path,
        "-map", "0:v:0", "-map", "1:a:0",
        "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
        "-shortest", tmp,
    ]
    if subprocess.run(cmd).returncode != 0:
        sys.exit("오디오 합치기 실패")
    os.replace(tmp, video_path)
    print(f"오디오 합침: {audio_path} ({start}s ~ {start + dur}s)")


def render(out_path, size, fps, dur, bpm, gif_path=None):
    global BPM
    BPM = bpm
    w, h = size

    scene = Scene(size, dur)
    n_frames = int(round(dur * fps))

    exe = ffmpeg_exe()
    cmd = [
        exe, "-y", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "rgb24",
        "-s", f"{w}x{h}", "-r", str(fps), "-i", "-",
        "-c:v", "libx264", "-preset", "slow", "-crf", "18",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        out_path,
    ]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    gif_frames = []
    gif_every = max(1, round(fps / 12.5)) if gif_path else 0

    for i in range(n_frames):
        frame = scene.frame(i / fps)
        proc.stdin.write(frame.tobytes())

        if gif_every and i % gif_every == 0:
            gif_frames.append(frame.resize((480, int(480 * h / w)), Image.LANCZOS))

        if i % 30 == 0:
            print(f"  {i}/{n_frames}", flush=True)

    proc.stdin.close()
    if proc.wait() != 0:
        sys.exit("ffmpeg 인코딩 실패")
    print(f"완료: {out_path}")

    if gif_path and gif_frames:
        gif_frames[0].save(
            gif_path,
            save_all=True,
            append_images=gif_frames[1:],
            duration=int(1000 * gif_every / fps),
            loop=0,
            optimize=True,
        )
        print(f"완료: {gif_path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="Comstock_ZeroTwo_15s.mp4")
    ap.add_argument("--gif", default="Comstock_ZeroTwo_15s.gif")
    ap.add_argument("--size", default="1280x720")
    ap.add_argument("--fps", type=int, default=FPS)
    ap.add_argument("--seconds", type=float, default=DUR)
    ap.add_argument("--bpm", type=float, default=BPM)
    ap.add_argument("--audio", default=None, help="붙일 음원 파일(리포에는 넣지 않는다)")
    ap.add_argument("--audio-start", type=float, default=50.0, help="음원에서 잘라올 시작 초")
    a = ap.parse_args()
    w, h = (int(v) for v in a.size.lower().split("x"))
    here = os.path.dirname(os.path.abspath(__file__))
    out = a.out if os.path.isabs(a.out) else os.path.join(here, a.out)
    gif = None
    if a.gif:
        gif = a.gif if os.path.isabs(a.gif) else os.path.join(here, a.gif)
    render(out, (w, h), a.fps, a.seconds, a.bpm, gif)
    if a.audio:
        mux_audio(out, a.audio, a.audio_start, a.seconds)


if __name__ == "__main__":
    main()
