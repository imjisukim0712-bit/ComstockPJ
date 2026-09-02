from __future__ import annotations

import argparse
import math
import os
import subprocess
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from render_ankara_comstock import FFMPEG, ROOT, SOURCE, lerp_track


OUT_W, OUT_H = 1080, 1920
CREAM = (242, 238, 231)
ORANGE = (255, 111, 0)


def clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def face_width(nominal: float) -> float:
    # TRACK의 nominal은 V1 스티커 폭이었다. V2에서는 실제 얼굴 영역으로 환산한다.
    # 원거리에서도 합성 요소가 3~4픽셀로 무너지지 않게 최소 폭을 둔다.
    return max(34.0, nominal * 0.52)


def draw_robot_face(frame: np.ndarray, x: float, y: float, nominal: float,
                    angle_hint: float = 0.0) -> np.ndarray:
    """메시 머리 위에 그림을 붙이지 않고, 원본 얼굴 픽셀 자체를 로봇 재질로 바꾼다."""
    h_img, w_img = frame.shape[:2]
    fw = face_width(nominal)
    fh = fw * 1.12
    cx = int(round(x))
    cy = int(round(y + fh * 0.12))
    # 얼굴 피부 영역만 바꾼다. 머리 전체를 감싸는 실루엣은 스티커처럼 보이므로 만들지 않는다.
    axes = (max(5, int(fw * 0.36)), max(6, int(fh * 0.39)))

    mask = np.zeros((h_img, w_img), np.uint8)
    cv2.ellipse(mask, (cx, cy), axes, angle_hint, 0, 360, 255, -1, cv2.LINE_AA)

    # 머리카락은 남겨서 '메시가 컴스톡 가면을 쓴 것'이 아니라
    # '메시 얼굴 표면이 로봇으로 변한 것'처럼 보이게 한다.
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    yy = np.arange(h_img, dtype=np.float32)[:, None]
    hair_zone = yy < (y + fh * 0.01)
    dark_hair = (gray < 100) & hair_zone
    mask[dark_hair] = (mask[dark_hair].astype(np.float32) * 0.04).astype(np.uint8)
    blur = max(3, int(round(fw * 0.035)))
    if blur % 2 == 0:
        blur += 1
    mask = cv2.GaussianBlur(mask, (blur, blur), 0)

    # 원본 얼굴의 명암·압축 노이즈를 금속색 안에 보존한다.
    lum = gray.astype(np.float32)
    metal = np.empty_like(frame, dtype=np.float32)
    metal[:, :, 0] = 154 + lum * 0.34
    metal[:, :, 1] = 158 + lum * 0.35
    metal[:, :, 2] = 162 + lum * 0.36
    metal = np.clip(metal, 0, 244).astype(np.uint8)
    alpha = (mask.astype(np.float32) / 255.0 * 0.80)[:, :, None]
    fused = (frame.astype(np.float32) * (1 - alpha) + metal.astype(np.float32) * alpha).astype(np.uint8)

    # 특징은 원본 해상도에서 그린 뒤 확대한다. 그래서 방송 영상과 같은 블러/픽셀감을 가진다.
    ink = (24, 25, 27)
    light = (220, 222, 222)
    shadow = (92, 94, 98)
    outline_thick = max(1, int(round(fw * 0.026)))
    feature_thick = max(1, int(round(fw * 0.026)))

    # 실제 귀 위치 안쪽에 작은 링만 넣는다. 큰 외부 디스크는 스티커처럼 보인다.
    ear_y = int(round(y + fh * 0.11))
    ear_r = max(2, int(round(fw * 0.068)))
    for side in (-1, 1):
        ex = int(round(x + side * fw * 0.34))
        cv2.circle(fused, (ex, ear_y), ear_r, shadow, outline_thick, cv2.LINE_AA)
        cv2.circle(fused, (ex, ear_y), max(1, ear_r // 2), light, feature_thick, cv2.LINE_AA)

    # 윗테두리는 이마 안쪽의 짧은 곡선만 남긴다.
    rim_y = int(round(y - fh * 0.10))
    rim_axes = (max(4, int(fw * 0.27)), max(2, int(fh * 0.055)))
    cv2.ellipse(fused, (cx, rim_y), rim_axes, angle_hint, 200, 340, ink,
                outline_thick, cv2.LINE_AA)
    cv2.ellipse(fused, (cx, rim_y - 1), (max(3, rim_axes[0] - 1), max(1, rim_axes[1] - 1)),
                angle_hint, 210, 330, (232, 232, 230), feature_thick, cv2.LINE_AA)

    eye_y = int(round(y + fh * 0.09))
    eye_dx = fw * 0.13
    eye_r = max(2, int(round(fw * 0.042)))
    for side in (-1, 1):
        ex = int(round(x + side * eye_dx))
        cv2.circle(fused, (ex, eye_y), eye_r, ink, -1, cv2.LINE_AA)
        if eye_r >= 4:
            cv2.circle(fused, (ex - max(1, eye_r // 3), eye_y - max(1, eye_r // 3)),
                       max(1, eye_r // 4), (236, 236, 236), -1, cv2.LINE_AA)

    smile_center = (cx, int(round(y + fh * 0.20)))
    smile_axes = (max(3, int(fw * 0.11)), max(2, int(fh * 0.065)))
    cv2.ellipse(fused, smile_center, smile_axes, angle_hint, 18, 162, ink,
                feature_thick, cv2.LINE_AA)

    # 얼굴 아래쪽에 약한 원통 음영을 넣어 목과 자연스럽게 맞닿게 한다.
    return fused


def crop_center_for_no_messi(t: float) -> tuple[float, float, float]:
    # 감독 리액션과 마지막 세리머니도 원본 오디오/흐름대로 유지한다.
    if 18.2 <= t < 22.0:
        return 660.0, 360.0, 720.0
    return 640.0, 360.0, 720.0


def zoom_crop(frame: np.ndarray, t: float, track: tuple[float, float, float] | None) -> np.ndarray:
    src_h, src_w = frame.shape[:2]
    if track is None:
        center_x, center_y, crop_h = crop_center_for_no_messi(t)
    else:
        x, y, nominal = track
        fw = face_width(nominal)
        # 얼굴이 최종 화면에서 약 270px로 읽히게 해 상반신과 드리블 동작도 함께 남긴다.
        crop_h = clamp(OUT_H * fw / 270.0, 300.0, 720.0)
        center_x = x
        center_y = y + crop_h * 0.20

    crop_w = crop_h * OUT_W / OUT_H
    x0 = clamp(center_x - crop_w / 2, 0, src_w - crop_w)
    y0 = clamp(center_y - crop_h / 2, 0, src_h - crop_h)
    x1 = x0 + crop_w
    y1 = y0 + crop_h
    crop = frame[int(y0):int(math.ceil(y1)), int(x0):int(math.ceil(x1))]
    if crop.size == 0:
        raise RuntimeError(f"empty crop at t={t:.3f}")
    enlarged = cv2.resize(crop, (OUT_W, OUT_H), interpolation=cv2.INTER_LANCZOS4)

    # 과도한 확대에서 경계가 뭉개지지 않도록 아주 약한 언샵만 적용한다.
    blurred = cv2.GaussianBlur(enlarged, (0, 0), 1.1)
    return cv2.addWeighted(enlarged, 1.16, blurred, -0.16, 0)


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / name
    return ImageFont.truetype(str(path), size)


def add_minimal_titles(frame_bgr: np.ndarray, t: float) -> np.ndarray:
    im = Image.fromarray(cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)).convert("RGBA")
    draw = ImageDraw.Draw(im, "RGBA")
    draw.rounded_rectangle((42, 34, 1038, 154), radius=28, fill=(8, 9, 11, 178),
                           outline=ORANGE + (230,), width=5)
    draw.text((540, 92), "ANKARA COMSTOCK", font=font("impact.ttf", 76),
              fill=CREAM + (255,), anchor="mm", stroke_width=4, stroke_fill=(0, 0, 0, 255))
    if 13.1 <= t < 18.2:
        draw.rounded_rectangle((338, 1740, 742, 1848), radius=24, fill=(8, 9, 11, 175),
                               outline=ORANGE + (230,), width=4)
        draw.text((540, 1794), "GOAL +999", font=font("ariblk.ttf", 54),
                  fill=CREAM + (255,), anchor="mm", stroke_width=3, stroke_fill=(0, 0, 0, 255))
    return cv2.cvtColor(np.asarray(im.convert("RGB")), cv2.COLOR_RGB2BGR)


def render_one(frame: np.ndarray, t: float) -> np.ndarray:
    track = lerp_track(t)
    if track is not None:
        x, y, nominal = track
        angle = 3.0 * math.sin(t * 8.0)
        frame = draw_robot_face(frame, x, y, nominal, angle)
    frame = zoom_crop(frame, t, track)
    return add_minimal_titles(frame, t)


def save_previews(cap: cv2.VideoCapture, out_dir: Path) -> None:
    previews = {
        "Ankara_Comstock_FaceFusion_Close_1080x1920.png": 6.0,
        "Ankara_Comstock_FaceFusion_Dribble_1080x1920.png": 10.5,
        "Ankara_Comstock_FaceFusion_Goal_1080x1920.png": 15.2,
    }
    for name, t in previews.items():
        cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
        ok, frame = cap.read()
        if not ok:
            raise RuntimeError(f"preview read failed at {t}")
        rendered = render_one(frame, t)
        cv2.imwrite(str(out_dir / name), rendered)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=str(ROOT / "dev" / "shorts" / "output"))
    parser.add_argument("--preview-only", action="store_true")
    args = parser.parse_args()
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    cap = cv2.VideoCapture(str(SOURCE))
    if not cap.isOpened():
        raise RuntimeError(f"Could not open {SOURCE}")
    fps = cap.get(cv2.CAP_PROP_FPS)
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    save_previews(cap, out_dir)
    if args.preview_only:
        cap.release()
        return

    cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
    silent = out_dir / "Ankara_Comstock_FaceFusion_1080x1920_silent.mp4"
    encoder = subprocess.Popen([
        str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
        "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{OUT_W}x{OUT_H}",
        "-r", f"{fps:.8f}", "-i", "-", "-an", "-c:v", "libx264",
        "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p",
        "-movflags", "+faststart", str(silent),
    ], stdin=subprocess.PIPE)

    for index in range(frame_count):
        ok, frame = cap.read()
        if not ok:
            break
        rendered = render_one(frame, index / fps)
        assert encoder.stdin is not None
        encoder.stdin.write(rendered.tobytes())
        if index % 120 == 0:
            print(f"rendered {index}/{frame_count}", flush=True)
    cap.release()
    assert encoder.stdin is not None
    encoder.stdin.close()
    if encoder.wait():
        raise RuntimeError("video encoder failed")

    final = out_dir / "Ankara_Comstock_FaceFusion_1080x1920.mp4"
    subprocess.run([
        str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
        "-i", str(silent), "-i", str(SOURCE), "-map", "0:v:0", "-map", "1:a:0",
        "-c:v", "copy", "-c:a", "copy", "-movflags", "+faststart", str(final),
    ], check=True)
    print(final)


if __name__ == "__main__":
    main()
