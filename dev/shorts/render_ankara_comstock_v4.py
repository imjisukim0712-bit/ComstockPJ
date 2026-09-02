from __future__ import annotations

import math
from pathlib import Path

import cv2
import numpy as np

import render_ankara_comstock_v3 as base


ASSET_PATH = base.ROOT / "Assets" / "Resources" / "Heads" / "ComstockMk01.png"
base.STEM = "Ankara_Comstock_AuthenticHead"
PRESERVE_FULL_HEAD = False


def load_comstock_head(path: Path) -> np.ndarray:
    sprite = cv2.imread(str(path), cv2.IMREAD_UNCHANGED)
    if sprite is None or sprite.ndim != 3 or sprite.shape[2] != 4:
        raise RuntimeError(f"Could not load RGBA Comstock head: {path}")
    alpha = sprite[:, :, 3]
    ys, xs = np.where(alpha > 4)
    if len(xs) == 0:
        raise RuntimeError(f"Comstock head has no visible pixels: {path}")
    pad = 3
    x0 = max(0, int(xs.min()) - pad)
    x1 = min(sprite.shape[1], int(xs.max()) + pad + 1)
    y0 = max(0, int(ys.min()) - pad)
    y1 = min(sprite.shape[0], int(ys.max()) + pad + 1)
    return sprite[y0:y1, x0:x1].copy()


COMSTOCK_HEAD = load_comstock_head(ASSET_PATH)


def rotated_sprite(sprite: np.ndarray, width: int, angle: float) -> np.ndarray:
    width = max(12, int(width))
    height = max(12, int(round(sprite.shape[0] * width / sprite.shape[1])))
    interpolation = cv2.INTER_AREA if width < sprite.shape[1] else cv2.INTER_LANCZOS4
    resized = cv2.resize(sprite, (width, height), interpolation=interpolation)

    margin = max(6, int(round(max(width, height) * 0.18)))
    padded = cv2.copyMakeBorder(
        resized,
        margin,
        margin,
        margin,
        margin,
        cv2.BORDER_CONSTANT,
        value=(0, 0, 0, 0),
    )
    center = (padded.shape[1] / 2.0, padded.shape[0] / 2.0)
    matrix = cv2.getRotationMatrix2D(center, angle, 1.0)
    return cv2.warpAffine(
        padded,
        matrix,
        (padded.shape[1], padded.shape[0]),
        flags=cv2.INTER_CUBIC,
        borderMode=cv2.BORDER_CONSTANT,
        borderValue=(0, 0, 0, 0),
    )


def draw_authentic_comstock_head(frame: np.ndarray, pose: base.HeadPose) -> np.ndarray:
    """Fuse the real ComstockMk01 sprite into Messi's tracked face.

    This is not a redrawn approximation. Every visible contour, eye, mouth, ear,
    top rim, and material pixel comes from the shipped game asset. Only lighting,
    edge feathering, pose, and partial hair occlusion are adapted to the footage.
    """
    face_w = base.face_width(pose.nominal, pose.scale)
    sprite_w = int(round(face_w * 1.03))
    sprite = rotated_sprite(COMSTOCK_HEAD, sprite_w, pose.angle)
    sh, sw = sprite.shape[:2]

    cx = int(round(pose.x))
    cy = int(round(pose.y + face_w * 0.10))
    x0 = cx - sw // 2
    y0 = cy - sh // 2
    x1 = x0 + sw
    y1 = y0 + sh

    frame_h, frame_w = frame.shape[:2]
    fx0, fy0 = max(0, x0), max(0, y0)
    fx1, fy1 = min(frame_w, x1), min(frame_h, y1)
    if fx0 >= fx1 or fy0 >= fy1:
        return frame
    sx0, sy0 = fx0 - x0, fy0 - y0
    sx1, sy1 = sx0 + (fx1 - fx0), sy0 + (fy1 - fy0)

    layer = sprite[sy0:sy1, sx0:sx1]
    rgb = layer[:, :, :3].astype(np.float32)
    alpha = layer[:, :, 3].astype(np.float32) / 255.0
    under = frame[fy0:fy1, fx0:fx1]
    under_gray = cv2.cvtColor(under, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255.0

    # Keep the exact asset colors but let broadcast lighting and compression
    # influence the pale metal surface so it belongs to the original footage.
    light_factor = 0.78 + under_gray[:, :, None] * 0.28
    lit_rgb = np.clip(rgb * light_factor, 0, 255)

    asset_gray = cv2.cvtColor(layer[:, :, :3], cv2.COLOR_BGR2GRAY)
    dark_asset_pixels = (asset_gray < 105) & (alpha > 0.05)

    # Turn the cylindrical sprite into a facial surface instead of pasting its
    # entire rectangular base. The identity-defining pixels still come directly
    # from ComstockMk01: top rim, ears, eyes, and mouth are never redrawn.
    visible_y, visible_x = np.where(alpha > 0.05)
    if PRESERVE_FULL_HEAD:
        # V5 route: preserve every opaque pixel and the complete cylindrical
        # silhouette from the game asset. Only pose and source lighting change.
        identity_pixels = dark_asset_pixels
    elif len(visible_x):
        left, right = float(visible_x.min()), float(visible_x.max())
        top, bottom = float(visible_y.min()), float(visible_y.max())
        grid_y, grid_x = np.indices(alpha.shape, dtype=np.float32)
        nx = (grid_x - left) / max(1.0, right - left)
        ny = (grid_y - top) / max(1.0, bottom - top)
        face_surface = ((nx - 0.50) / 0.35) ** 2 + ((ny - 0.55) / 0.44) ** 2 <= 1.0
        top_rim = dark_asset_pixels & (ny < 0.34)
        ears = dark_asset_pixels & (ny < 0.69) & ((nx < 0.23) | (nx > 0.77))
        eyes_and_mouth = dark_asset_pixels & (ny > 0.31) & (ny < 0.80) & (nx > 0.22) & (nx < 0.78)
        identity_pixels = top_rim | ears | eyes_and_mouth
        alpha[~face_surface & ~identity_pixels] = 0.0
        alpha[identity_pixels] = layer[:, :, 3][identity_pixels].astype(np.float32) / 255.0
    else:
        identity_pixels = dark_asset_pixels

    # Messi's dark hair crosses in front of the pale face material. The original
    # black Comstock contours/features remain fully visible, preserving identity.
    yy = np.arange(fy0, fy1, dtype=np.float32)[:, None]
    hair_region = yy < (pose.y + face_w * 0.01)
    source_hair = (under_gray < 0.39) & hair_region
    if not PRESERVE_FULL_HEAD:
        alpha[source_hair & ~identity_pixels] *= 0.18

    # Feather only transparency, never redraw or simplify the source artwork.
    alpha = cv2.GaussianBlur(alpha, (3, 3), 0.55)
    opacity = 0.98 if PRESERVE_FULL_HEAD else 0.94
    alpha = np.clip(alpha * opacity, 0.0, 1.0)[:, :, None]
    fused = under.astype(np.float32) * (1.0 - alpha) + lit_rgb * alpha
    result = frame.copy()
    result[fy0:fy1, fx0:fx1] = np.clip(fused, 0, 255).astype(np.uint8)
    return result


base.draw_robot_face = draw_authentic_comstock_head


if __name__ == "__main__":
    base.main()
