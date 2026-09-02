from __future__ import annotations

import argparse
import json
import math
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from render_ankara_comstock import FFMPEG, ROOT, SOURCE, lerp_track


OUT_W, OUT_H = 1080, 1920
CREAM = (242, 238, 231)
ORANGE = (255, 111, 0)
STEM = "Ankara_Comstock_MotionTracked"

# Each range starts after a hard broadcast cut. Optical flow and camera inertia
# must be reset there so motion from two unrelated shots never gets mixed.
SEGMENTS = (
    # The source uses a dissolve here, not the 4.11 s hard cut assumed by V1.
    # Before 4.45 s Messi's head is only a few pixels wide, so forcing a large
    # synthetic face creates a floating sticker. Start the face transform once
    # the broadcast close-up actually resolves his head.
    (4.45, 7.25),
    (7.26, 16.20),
    (16.21, 18.20),
    (22.00, 24.60),
)

# The legacy V1 path followed a foreground teammate after the finish. These
# corrected anchors stay on Messi as he carries his momentum behind the goal.
GOAL_CELEBRATION_TRACK = (
    (13.00, 950.0, 205.0, 110.0),
    (13.35, 900.0, 190.0, 116.0),
    (13.70, 875.0, 175.0, 120.0),
    (14.00, 860.0, 155.0, 124.0),
    (14.40, 845.0, 120.0, 128.0),
    (14.80, 835.0, 104.0, 132.0),
    (15.20, 845.0, 105.0, 136.0),
    (15.60, 830.0, 105.0, 140.0),
    (16.20, 850.0, 115.0, 145.0),
)


def clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def segment_for(t: float) -> int | None:
    for index, (start, end) in enumerate(SEGMENTS):
        if start <= t <= end:
            return index
    return None


def manual_track(t: float) -> tuple[float, float, float] | None:
    if not (13.0 <= t <= 16.2):
        return lerp_track(t)
    for a, b in zip(GOAL_CELEBRATION_TRACK, GOAL_CELEBRATION_TRACK[1:]):
        if a[0] <= t <= b[0]:
            q = (t - a[0]) / (b[0] - a[0])
            q = q * q * (3.0 - 2.0 * q)
            return tuple(a[i] + (b[i] - a[i]) * q for i in (1, 2, 3))
    return GOAL_CELEBRATION_TRACK[-1][1:]


def face_width(nominal: float, scale: float = 1.0) -> float:
    return max(34.0, nominal * 0.52 * clamp(scale, 0.88, 1.16))


@dataclass
class HeadPose:
    x: float
    y: float
    nominal: float
    angle: float = 0.0
    scale: float = 1.0


class MotionTracker:
    """Refine the hand-keyed head path with real frame-to-frame optical flow."""

    def __init__(self) -> None:
        self.segment: int | None = None
        self.prev_gray: np.ndarray | None = None
        self.points: np.ndarray | None = None
        self.pose: HeadPose | None = None
        self.frame_in_segment = 0

    def _seed_points(self, gray: np.ndarray, pose: HeadPose) -> np.ndarray | None:
        h, w = gray.shape
        roi_w = max(44, int(pose.nominal * 0.90))
        roi_h = max(56, int(pose.nominal * 1.18))
        x0 = max(0, int(pose.x - roi_w / 2))
        x1 = min(w, int(pose.x + roi_w / 2))
        y0 = max(0, int(pose.y - roi_h * 0.50))
        y1 = min(h, int(pose.y + roi_h * 0.68))
        mask = np.zeros_like(gray)
        mask[y0:y1, x0:x1] = 255
        return cv2.goodFeaturesToTrack(
            gray,
            maxCorners=70,
            qualityLevel=0.008,
            minDistance=3,
            mask=mask,
            blockSize=5,
        )

    def update(self, frame: np.ndarray, t: float) -> HeadPose | None:
        manual = manual_track(t)
        seg = segment_for(t)
        if manual is None or seg is None:
            self.segment = None
            self.prev_gray = None
            self.points = None
            self.pose = None
            self.frame_in_segment = 0
            return None

        mx, my, nominal = manual
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)

        if seg != self.segment or self.prev_gray is None or self.pose is None:
            self.segment = seg
            self.pose = HeadPose(mx, my, nominal)
            self.points = self._seed_points(gray, self.pose)
            self.prev_gray = gray
            self.frame_in_segment = 0
            return self.pose

        dx = dy = 0.0
        step_scale = 1.0
        step_angle = 0.0
        good_next: np.ndarray | None = None
        good_prev: np.ndarray | None = None
        if self.points is not None and len(self.points) >= 4:
            nxt, status, _ = cv2.calcOpticalFlowPyrLK(
                self.prev_gray,
                gray,
                self.points,
                None,
                winSize=(25, 25),
                maxLevel=3,
                criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 25, 0.01),
            )
            if nxt is not None and status is not None:
                valid = status.reshape(-1).astype(bool)
                good_prev = self.points.reshape(-1, 2)[valid]
                good_next = nxt.reshape(-1, 2)[valid]
                if len(good_next) >= 4:
                    delta = good_next - good_prev
                    med = np.median(delta, axis=0)
                    distance = np.linalg.norm(delta - med, axis=1)
                    keep = distance < max(4.0, nominal * 0.10)
                    if np.count_nonzero(keep) >= 4:
                        good_prev = good_prev[keep]
                        good_next = good_next[keep]
                        med = np.median(good_next - good_prev, axis=0)
                    dx = float(clamp(float(med[0]), -24.0, 24.0))
                    dy = float(clamp(float(med[1]), -20.0, 20.0))

                    affine, _ = cv2.estimateAffinePartial2D(
                        good_prev,
                        good_next,
                        method=cv2.RANSAC,
                        ransacReprojThreshold=2.5,
                    )
                    if affine is not None:
                        a, b = float(affine[0, 0]), float(affine[1, 0])
                        step_scale = clamp(math.hypot(a, b), 0.94, 1.06)
                        step_angle = clamp(math.degrees(math.atan2(b, a)), -5.0, 5.0)

        # Optical motion drives every frame; the sparse manual path only supplies
        # a weak spring that prevents long-shot drift.
        predicted_x = self.pose.x + dx
        predicted_y = self.pose.y + dy
        # A stronger anchor is necessary in the wide goal sequence where other
        # players repeatedly cross the tiny head ROI. Optical flow still adds
        # per-frame bob/tilt, but it can no longer migrate to a defender.
        spring = 0.46 if t >= 13.0 else 0.22
        x = predicted_x + (mx - predicted_x) * spring
        y = predicted_y + (my - predicted_y) * spring
        angle = clamp((self.pose.angle + step_angle) * 0.86, -15.0, 15.0)
        scale = clamp((self.pose.scale * step_scale) * 0.90 + 0.10, 0.88, 1.16)
        self.pose = HeadPose(x, y, nominal, angle, scale)
        self.frame_in_segment += 1

        # Refresh features inside the moving head/shoulder box frequently. This
        # also recovers after motion blur destroys a few Lucas-Kanade points.
        need_refresh = (
            good_next is None
            or len(good_next) < 12
            or self.frame_in_segment % 8 == 0
        )
        if need_refresh:
            self.points = self._seed_points(gray, self.pose)
        else:
            self.points = good_next.reshape(-1, 1, 2).astype(np.float32)
        self.prev_gray = gray
        return self.pose


def rotate_point(cx: float, cy: float, dx: float, dy: float, angle: float) -> tuple[int, int]:
    r = math.radians(angle)
    c, s = math.cos(r), math.sin(r)
    return int(round(cx + dx * c - dy * s)), int(round(cy + dx * s + dy * c))


def draw_robot_face(frame: np.ndarray, pose: HeadPose) -> np.ndarray:
    """Transform the actual face surface while preserving hair, blur, and lighting."""
    h_img, w_img = frame.shape[:2]
    fw = face_width(pose.nominal, pose.scale)
    fh = fw * 1.12
    cx = float(pose.x)
    cy = float(pose.y + fh * 0.12)
    axes = (max(5, int(fw * 0.36)), max(6, int(fh * 0.39)))

    mask = np.zeros((h_img, w_img), np.uint8)
    cv2.ellipse(
        mask,
        (int(round(cx)), int(round(cy))),
        axes,
        pose.angle,
        0,
        360,
        255,
        -1,
        cv2.LINE_AA,
    )

    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    yy = np.arange(h_img, dtype=np.float32)[:, None]
    hair_zone = yy < (pose.y + fh * 0.01)
    dark_hair = (gray < 100) & hair_zone
    mask[dark_hair] = (mask[dark_hair].astype(np.float32) * 0.04).astype(np.uint8)
    blur = max(3, int(round(fw * 0.035)))
    if blur % 2 == 0:
        blur += 1
    mask = cv2.GaussianBlur(mask, (blur, blur), 0)

    lum = gray.astype(np.float32)
    metal = np.empty_like(frame, dtype=np.float32)
    metal[:, :, 0] = 154 + lum * 0.34
    metal[:, :, 1] = 158 + lum * 0.35
    metal[:, :, 2] = 162 + lum * 0.36
    metal = np.clip(metal, 0, 244).astype(np.uint8)
    alpha = (mask.astype(np.float32) / 255.0 * 0.80)[:, :, None]
    fused = (frame.astype(np.float32) * (1 - alpha) + metal.astype(np.float32) * alpha).astype(np.uint8)

    ink = (24, 25, 27)
    light = (220, 222, 222)
    shadow = (92, 94, 98)
    line = max(1, int(round(fw * 0.026)))

    ear_y = fh * -0.01
    ear_r = max(2, int(round(fw * 0.068)))
    for side in (-1, 1):
        ex, ey = rotate_point(cx, cy, side * fw * 0.34, ear_y, pose.angle)
        cv2.circle(fused, (ex, ey), ear_r, shadow, line, cv2.LINE_AA)
        cv2.circle(fused, (ex, ey), max(1, ear_r // 2), light, line, cv2.LINE_AA)

    rim_center = rotate_point(cx, cy, 0, -fh * 0.22, pose.angle)
    rim_axes = (max(4, int(fw * 0.27)), max(2, int(fh * 0.055)))
    cv2.ellipse(fused, rim_center, rim_axes, pose.angle, 200, 340, ink, line, cv2.LINE_AA)
    cv2.ellipse(
        fused,
        rim_center,
        (max(3, rim_axes[0] - 1), max(1, rim_axes[1] - 1)),
        pose.angle,
        210,
        330,
        (232, 232, 230),
        line,
        cv2.LINE_AA,
    )

    eye_r = max(2, int(round(fw * 0.042)))
    for side in (-1, 1):
        ex, ey = rotate_point(cx, cy, side * fw * 0.13, -fh * 0.03, pose.angle)
        cv2.circle(fused, (ex, ey), eye_r, ink, -1, cv2.LINE_AA)
        if eye_r >= 4:
            cv2.circle(fused, (ex - 1, ey - 1), max(1, eye_r // 4), (236, 236, 236), -1, cv2.LINE_AA)

    smile_center = rotate_point(cx, cy, 0, fh * 0.08, pose.angle)
    smile_axes = (max(3, int(fw * 0.11)), max(2, int(fh * 0.065)))
    cv2.ellipse(fused, smile_center, smile_axes, pose.angle, 18, 162, ink, line, cv2.LINE_AA)
    return fused


class LagCamera:
    """A dead-zone camera that follows the player, never pins the face to center."""

    def __init__(self) -> None:
        self.segment: int | None = None
        self.x = 640.0
        self.y = 360.0
        self.crop_h = 720.0

    def update(self, t: float, pose: HeadPose | None) -> tuple[float, float, float]:
        seg = segment_for(t)
        if pose is None or seg is None:
            self.segment = None
            target_x = 660.0 if 18.2 <= t < 22.0 else 640.0
            self.x += (target_x - self.x) * 0.08
            self.y += (360.0 - self.y) * 0.08
            self.crop_h += (720.0 - self.crop_h) * 0.08
            return self.x, self.y, self.crop_h

        target_h = clamp(OUT_H * face_width(pose.nominal, pose.scale) / 270.0, 310.0, 720.0)
        if seg != self.segment:
            self.segment = seg
            self.x = pose.x
            self.y = pose.y + target_h * 0.20
            self.crop_h = target_h
            return self.x, self.y, self.crop_h

        self.crop_h += (target_h - self.crop_h) * 0.045
        crop_w = self.crop_h * OUT_W / OUT_H
        desired_y = pose.y + self.crop_h * 0.20
        dead_x = crop_w * 0.12
        dead_y = self.crop_h * 0.055
        if pose.x < self.x - dead_x:
            self.x += ((pose.x + dead_x) - self.x) * 0.18
        elif pose.x > self.x + dead_x:
            self.x += ((pose.x - dead_x) - self.x) * 0.18
        if desired_y < self.y - dead_y:
            self.y += ((desired_y + dead_y) - self.y) * 0.16
        elif desired_y > self.y + dead_y:
            self.y += ((desired_y - dead_y) - self.y) * 0.16
        return self.x, self.y, self.crop_h


def zoom_crop(frame: np.ndarray, camera: tuple[float, float, float]) -> tuple[np.ndarray, tuple[float, float, float, float]]:
    src_h, src_w = frame.shape[:2]
    center_x, center_y, crop_h = camera
    crop_h = min(float(src_h), crop_h)
    crop_w = crop_h * OUT_W / OUT_H
    x0 = clamp(center_x - crop_w / 2, 0, src_w - crop_w)
    y0 = clamp(center_y - crop_h / 2, 0, src_h - crop_h)
    x1 = x0 + crop_w
    y1 = y0 + crop_h
    crop = frame[int(y0):int(math.ceil(y1)), int(x0):int(math.ceil(x1))]
    if crop.size == 0:
        raise RuntimeError("empty crop")
    enlarged = cv2.resize(crop, (OUT_W, OUT_H), interpolation=cv2.INTER_LANCZOS4)
    blurred = cv2.GaussianBlur(enlarged, (0, 0), 1.1)
    return cv2.addWeighted(enlarged, 1.16, blurred, -0.16, 0), (x0, y0, crop_w, crop_h)


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    path = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / name
    return ImageFont.truetype(str(path), size)


def add_minimal_titles(frame_bgr: np.ndarray, t: float) -> np.ndarray:
    im = Image.fromarray(cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)).convert("RGBA")
    draw = ImageDraw.Draw(im, "RGBA")
    draw.rounded_rectangle((56, 38, 1024, 142), radius=25, fill=(8, 9, 11, 168), outline=ORANGE + (225,), width=4)
    draw.text((540, 90), "ANKARA COMSTOCK", font=font("impact.ttf", 66), fill=CREAM + (255,), anchor="mm", stroke_width=4, stroke_fill=(0, 0, 0, 255))
    if 13.1 <= t < 18.2:
        draw.rounded_rectangle((338, 1740, 742, 1848), radius=24, fill=(8, 9, 11, 175), outline=ORANGE + (230,), width=4)
        draw.text((540, 1794), "GOAL +999", font=font("ariblk.ttf", 54), fill=CREAM + (255,), anchor="mm", stroke_width=3, stroke_fill=(0, 0, 0, 255))
    return cv2.cvtColor(np.asarray(im.convert("RGB")), cv2.COLOR_RGB2BGR)


def screen_face_position(pose: HeadPose, crop_rect: tuple[float, float, float, float]) -> tuple[float, float]:
    x0, y0, crop_w, crop_h = crop_rect
    return (pose.x - x0) * OUT_W / crop_w, (pose.y - y0) * OUT_H / crop_h


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=str(ROOT / "dev" / "shorts" / "output"))
    parser.add_argument("--preview-only", action="store_true")
    parser.add_argument("--preview-end", type=float, default=15.3)
    parser.add_argument("--clip-start", type=float)
    parser.add_argument("--clip-end", type=float)
    args = parser.parse_args()
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    cap = cv2.VideoCapture(str(SOURCE))
    if not cap.isOpened():
        raise RuntimeError(f"Could not open {SOURCE}")
    fps = cap.get(cv2.CAP_PROP_FPS)
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    tracker = MotionTracker()
    camera = LagCamera()
    silent = out_dir / f"{STEM}_1080x1920_silent.mp4"
    clip_silent = out_dir / f"{STEM}_MovementPreview_1080x1920_silent.mp4"
    preview_targets = {
        f"{STEM}_Close_1080x1920.png": 6.0,
        f"{STEM}_Dribble_1080x1920.png": 10.5,
        f"{STEM}_Goal_1080x1920.png": 13.0,
    }
    saved: set[str] = set()
    positions: dict[int, list[tuple[float, float]]] = {i: [] for i in range(len(SEGMENTS))}

    render_video = not args.preview_only
    make_clip = args.clip_start is not None and args.clip_end is not None
    encoder = None
    if render_video or make_clip:
        destination = clip_silent if make_clip else silent
        encoder = subprocess.Popen([
            str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{OUT_W}x{OUT_H}",
            "-r", f"{fps:.8f}", "-i", "-", "-an", "-c:v", "libx264",
            "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p",
            "-movflags", "+faststart", str(destination),
        ], stdin=subprocess.PIPE)

    for index in range(frame_count):
        ok, frame = cap.read()
        if not ok:
            break
        t = index / fps
        pose = tracker.update(frame, t)
        camera_state = camera.update(t, pose)
        if pose is not None:
            frame = draw_robot_face(frame, pose)
        enlarged, crop_rect = zoom_crop(frame, camera_state)
        rendered = add_minimal_titles(enlarged, t)
        if pose is not None:
            seg = segment_for(t)
            assert seg is not None
            positions[seg].append(screen_face_position(pose, crop_rect))

        for name, target in preview_targets.items():
            if name not in saved and t >= target:
                cv2.imwrite(str(out_dir / name), rendered)
                saved.add(name)

        should_encode = render_video or (make_clip and args.clip_start <= t <= args.clip_end)
        if encoder is not None and should_encode:
            assert encoder.stdin is not None
            encoder.stdin.write(rendered.tobytes())
        if index % 120 == 0:
            print(f"rendered {index}/{frame_count}", flush=True)
        if args.preview_only and not make_clip and t >= args.preview_end:
            break
        if make_clip and t > args.clip_end:
            break

    cap.release()
    if encoder is not None:
        assert encoder.stdin is not None
        encoder.stdin.close()
        if encoder.wait():
            raise RuntimeError("video encoder failed")

    metric_output: dict[str, dict[str, float]] = {}
    for seg, values in positions.items():
        if not values:
            continue
        arr = np.asarray(values, dtype=np.float32)
        metric_output[str(seg)] = {
            "x_span_px": round(float(np.ptp(arr[:, 0])), 1),
            "y_span_px": round(float(np.ptp(arr[:, 1])), 1),
            "x_std_px": round(float(np.std(arr[:, 0])), 1),
            "y_std_px": round(float(np.std(arr[:, 1])), 1),
        }
    print("MOTION_METRICS=" + json.dumps(metric_output, ensure_ascii=False), flush=True)

    if make_clip:
        clip = out_dir / f"{STEM}_MovementPreview_1080x1920.mp4"
        subprocess.run([
            str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
            "-i", str(clip_silent), "-ss", f"{args.clip_start:.3f}", "-i", str(SOURCE),
            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "copy",
            "-t", f"{args.clip_end - args.clip_start:.3f}", "-movflags", "+faststart", str(clip),
        ], check=True)
        print(clip)
    elif render_video:
        final = out_dir / f"{STEM}_1080x1920.mp4"
        subprocess.run([
            str(FFMPEG), "-y", "-hide_banner", "-loglevel", "error",
            "-i", str(silent), "-i", str(SOURCE), "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy", "-c:a", "copy", "-movflags", "+faststart", str(final),
        ], check=True)
        print(final)


if __name__ == "__main__":
    main()
