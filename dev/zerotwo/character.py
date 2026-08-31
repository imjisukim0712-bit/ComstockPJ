"""마시멜로 로봇 캐릭터를 Pillow로 그린다.

사용자가 준 캐릭터 그림(흰 원통 몸통 + 귀 + 점눈 + 다리/부츠)을 코드로 다시 그린 것이다.
그림 파일을 그대로 쓰지 않고 코드로 그리는 이유는 **춤 포즈를 잡아야 하기 때문**이다.
원본에 없는 팔은 다리와 같은 문법(가는 회색 관절 + 볼 조인트 + 갈색 손/발)으로 새로 만들었다.

좌표계
------
캐릭터 단위(char unit)를 쓴다. **몸통(=머리) 중심이 원점**이고 아래가 +y다.
원본 그림(600x600)의 픽셀을 그대로 단위로 삼았으므로 몸통은 300x350이다.

    몸통      x -150..+150, y -175..+175
    귀        (±142, -55), r 30
    눈        (±70, +15), r 20
    다리 고관절 (±92, +178)
    발목      (±92, +286)
    바닥      y +336

`draw_character()`는 이 좌표계를 픽셀로 변환해 RGBA 타일 한 장을 돌려준다.
타일 안에서 원점이 어디인지는 `ANCHOR_PX(scale)`로 알 수 있다.
"""

import math
from PIL import Image, ImageDraw

# --- 색 ---------------------------------------------------------------------

INK = (26, 26, 26, 255)  # 외곽선(원본 그림의 굵은 검정 선)

BODY_LIGHT = (248, 249, 250, 255)
BODY_DARK = (206, 209, 216, 255)
RIM_LIGHT = (253, 253, 254, 255)

EAR_FILL = (205, 208, 214, 255)
EAR_INNER = (122, 126, 133, 255)
EAR_HOLE = (58, 61, 66, 255)

LIMB_FILL = (200, 203, 209, 255)
JOINT_FILL = (166, 170, 178, 255)

BOOT_FILL = (214, 154, 94, 255)
BOOT_DARK = (184, 120, 64, 255)

# --- 치수(캐릭터 단위) -------------------------------------------------------

BODY_W, BODY_H = 300.0, 350.0
BODY_R = 84.0  # 모서리 반지름

EAR_POS = (150.0, -62.0)
EAR_R = 34.0

EYE_POS = (70.0, 15.0)
EYE_R = 20.0

MOUTH_Y = 74.0
MOUTH_W = 58.0

HIP = (92.0, 178.0)
THIGH_LEN, SHIN_LEN = 64.0, 52.0
THIGH_W, SHIN_W = 21.0, 18.0
HIP_R, KNEE_R, ANKLE_R = 17.0, 21.0, 14.0
ANKLE_Y = 286.0
GROUND_Y = 336.0

# 팔은 원본 그림에 없다. 다리보다 길게 잡아야 "팔꿈치를 크게 벌린" 제로투 포즈가
# 실루엣으로 읽힌다(짧으면 손이 귀에 붙어 삼각형이 안 생긴다).
SHOULDER = (140.0, 5.0)
UPPER_LEN, FORE_LEN = 88.0, 84.0
UPPER_W, FORE_W = 19.0, 16.0
SHOULDER_R, ELBOW_R = 16.0, 19.0
HAND_R = 24.0

OUTLINE = 7.0  # 외곽선 두께(캐릭터 단위)

# 타일 범위 - 팔을 최대로 벌려도 잘리지 않도록 넉넉히
TILE_X0, TILE_X1 = -350.0, 350.0
TILE_Y0, TILE_Y1 = -270.0, 390.0


def tile_size_px(scale):
    return (
        int(round((TILE_X1 - TILE_X0) * scale)),
        int(round((TILE_Y1 - TILE_Y0) * scale)),
    )


def ANCHOR_PX(scale):
    """타일 안에서 캐릭터 원점(몸통 중심)의 픽셀 좌표."""
    return (-TILE_X0 * scale, -TILE_Y0 * scale)


# --- 그리기 도우미 ------------------------------------------------------------


def _rot(p, deg, about=(0.0, 0.0)):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    dx, dy = p[0] - about[0], p[1] - about[1]
    return (about[0] + dx * c - dy * s, about[1] + dx * s + dy * c)


def _polar(p, deg, length):
    a = math.radians(deg)
    return (p[0] + math.cos(a) * length, p[1] + math.sin(a) * length)


def _dot(d, p, r, fill, outline_w=OUTLINE, outline=INK):
    """외곽선을 두른 원. outline_w=0이면 외곽선 없음."""
    if outline_w > 0:
        rr = r + outline_w * 0.5
        d.ellipse([p[0] - rr, p[1] - rr, p[0] + rr, p[1] + rr], fill=outline)
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill)


def _capsule(d, p0, p1, w, fill, outline_w=OUTLINE, outline=INK):
    """양 끝이 둥근 막대. 외곽선을 먼저 굵게 깔고 그 위에 채움을 덮는다."""
    if outline_w > 0:
        ow = w + outline_w
        d.line([p0, p1], fill=outline, width=int(round(ow)))
        for p in (p0, p1):
            d.ellipse(
                [p[0] - ow / 2, p[1] - ow / 2, p[0] + ow / 2, p[1] + ow / 2],
                fill=outline,
            )
    d.line([p0, p1], fill=fill, width=int(round(w)))
    for p in (p0, p1):
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=fill)


def _outlined_polygon(d, pts, fill, outline_w=OUTLINE, outline=INK):
    """둥근 이음매로 외곽선을 두른 다각형.

    **선을 먼저 긋고 채움으로 안쪽 절반을 덮는다** - 그래야 밖으로 보이는 두께가
    `outline_w / 2`가 되어 `_dot`/`_capsule`/몸통과 정확히 같아진다.
    (선을 2배로 긋고 다시 덧그리면 부츠만 외곽선이 두 배로 두꺼워진다.)
    """
    if outline_w > 0:
        d.line(list(pts) + [pts[0]], fill=outline, width=int(round(outline_w)), joint="curve")
    d.polygon(pts, fill=fill)


def _vertical_gradient(size, top, bottom):
    """세로 그라데이션 한 장(RGBA)."""
    w, h = size
    img = Image.new("RGBA", (1, h))
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = tuple(int(round(top[i] + (bottom[i] - top[i]) * t)) for i in range(4))
    return img.resize((w, h), Image.BILINEAR)


# --- 몸통(=머리) 레이어 ------------------------------------------------------

_BODY_CACHE = {}


def body_layer(scale, eyes="open"):
    """몸통 + 귀 + 얼굴을 그린 RGBA 타일. 회전만 바뀌므로 캐시한다.

    `eyes`: "open" | "half" | "closed" (깜빡임)
    """
    key = (round(scale, 4), eyes)
    if key in _BODY_CACHE:
        return _BODY_CACHE[key]

    W, H = tile_size_px(scale)
    ax, ay = ANCHOR_PX(scale)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def P(x, y):
        return (ax + x * scale, ay + y * scale)

    def S(v):
        return v * scale

    ow = S(OUTLINE)

    # 귀는 몸통 뒤에 깔린다
    for side in (-1, 1):
        c = P(side * EAR_POS[0], EAR_POS[1])
        _dot(d, c, S(EAR_R), EAR_FILL, ow)
    # 오른쪽 귀는 원근 때문에 안쪽 구멍이 보인다.
    # 구멍은 **몸통 밖으로 나온 쪽에** 놓아야 한다 - 귀 한가운데에 두면 몸통에 가려 안 보인다.
    ec = P(EAR_POS[0] + 11, EAR_POS[1])
    d.ellipse(
        [ec[0] - S(11), ec[1] - S(18), ec[0] + S(11), ec[1] + S(18)],
        fill=EAR_INNER,
        outline=INK,
        width=max(1, int(round(ow * 0.5))),
    )
    d.ellipse([ec[0] - S(5), ec[1] - S(11), ec[0] + S(5), ec[1] + S(11)], fill=EAR_HOLE)
    # 왼쪽 귀는 살짝만
    lc = P(-EAR_POS[0] - 10, EAR_POS[1])
    d.ellipse(
        [lc[0] - S(13), lc[1] - S(13), lc[0] + S(13), lc[1] + S(13)],
        fill=EAR_INNER,
        outline=INK,
        width=max(1, int(round(ow * 0.5))),
    )

    # 몸통 - 마스크에 세로 그라데이션을 씌워 원통 음영을 만든다
    x0, y0 = P(-BODY_W / 2, -BODY_H / 2)
    x1, y1 = P(BODY_W / 2, BODY_H / 2)
    rr = S(BODY_R)

    outline_img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(outline_img).rounded_rectangle(
        [x0 - ow / 2, y0 - ow / 2, x1 + ow / 2, y1 + ow / 2], radius=rr + ow / 2, fill=INK
    )
    img.alpha_composite(outline_img)

    mask = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask).rounded_rectangle([x0, y0, x1, y1], radius=rr, fill=255)
    grad = _vertical_gradient((W, H), BODY_LIGHT, BODY_DARK)
    img.paste(grad, (0, 0), mask)

    # 원통 윗면 테두리(살짝 위에서 본 타원)와 하이라이트
    top_c = P(0, -BODY_H / 2 + 46)
    d.arc(
        [top_c[0] - S(140), top_c[1] - S(52), top_c[0] + S(140), top_c[1] + S(52)],
        start=0,
        end=180,
        fill=INK,
        width=max(1, int(round(ow * 0.85))),
    )
    hl = P(-96, -BODY_H / 2 + 40)
    d.arc(
        [hl[0] - S(46), hl[1] - S(30), hl[0] + S(46), hl[1] + S(30)],
        start=185,
        end=290,
        fill=INK,
        width=max(1, int(round(ow * 0.75))),
    )

    # 눈
    for side in (-1, 1):
        c = P(side * EYE_POS[0], EYE_POS[1])
        if eyes == "closed":
            d.arc(
                [c[0] - S(EYE_R), c[1] - S(EYE_R * 0.8), c[0] + S(EYE_R), c[1] + S(EYE_R * 0.8)],
                start=200,
                end=340,
                fill=INK,
                width=max(1, int(round(S(7)))),
            )
            continue
        r = S(EYE_R) * (0.45 if eyes == "half" else 1.0)
        d.ellipse([c[0] - S(EYE_R), c[1] - r, c[0] + S(EYE_R), c[1] + r], fill=INK)
        if eyes == "open":
            h = (c[0] - S(7), c[1] - S(8))
            d.ellipse(
                [h[0] - S(6), h[1] - S(6), h[0] + S(6), h[1] + S(6)], fill=(255, 255, 255, 255)
            )

    # 입 - 아래로 볼록한 웃음
    m = P(-6, MOUTH_Y)
    d.arc(
        [m[0] - S(MOUTH_W / 2), m[1] - S(30), m[0] + S(MOUTH_W / 2), m[1] + S(22)],
        start=20,
        end=160,
        fill=INK,
        width=max(1, int(round(S(6.5)))),
    )

    _BODY_CACHE[key] = img
    return img


# --- 다리 / 팔 ---------------------------------------------------------------


def _two_bone(root, target, l1, l2, bend):
    """2관절 IK. `bend`가 +1이면 관절이 오른쪽, -1이면 왼쪽으로 꺾인다."""
    dx, dy = target[0] - root[0], target[1] - root[1]
    d = math.hypot(dx, dy)
    d = max(1e-3, min(d, l1 + l2 - 1e-3))
    # 코사인 법칙
    a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    h = math.sqrt(max(0.0, l1 * l1 - a * a))
    ux, uy = dx / d, dy / d
    mx, my = root[0] + ux * a, root[1] + uy * a
    return (mx - uy * h * bend, my + ux * h * bend)


# 부츠 로컬 범위(발목이 원점, 발끝은 +x 방향).
# **좌우 대칭으로 잡아야 한다** - 발끝 쪽만 넉넉하게 잡으면 미러링한 왼쪽 부츠의
# 발끝이 조각 밖으로 나가 잘린다.
_BOOT_BOX = (-78.0, -48.0, 78.0, 66.0)
_BOOT_CACHE = {}


def _boot_layer(scale, side):
    """부츠 한 짝을 그린 RGBA 조각. 발목이 원점이고 캐시한다.

    **발목 통과 발을 둥근 사각형 두 개로 합친다** - 꼭짓점을 손으로 찍은 다각형은
    안쪽에 계단 같은 홈이 생겨 실루엣이 깨졌다. 두 조각의 **외곽선을 먼저 둘 다
    깔고 채움을 둘 다 덮어야** 겹치는 자리에 검은 이음매가 남지 않는다.
    """
    key = (round(scale, 4), side)
    if key in _BOOT_CACHE:
        return _BOOT_CACHE[key]

    lx0, ly0, lx1, ly1 = _BOOT_BOX
    W = int(math.ceil((lx1 - lx0) * scale))
    H = int(math.ceil((ly1 - ly0) * scale))
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ox, oy = -lx0 * scale, -ly0 * scale

    def R(x0, y0, x1, y1):
        """부츠 로컬 사각형 -> 조각 픽셀. side=-1이면 좌우를 뒤집는다."""
        a, b = x0 * side, x1 * side
        return [ox + min(a, b) * scale, oy + y0 * scale, ox + max(a, b) * scale, oy + y1 * scale]

    ow = OUTLINE * scale
    cuff = R(-26, -38, 26, 20)
    foot = R(-26, 4, 66, 54)

    for box, rad in ((cuff, 15), (foot, 22)):
        d.rounded_rectangle(
            [box[0] - ow / 2, box[1] - ow / 2, box[2] + ow / 2, box[3] + ow / 2],
            radius=rad * scale + ow / 2,
            fill=INK,
        )
    for box, rad in ((cuff, 15), (foot, 22)):
        d.rounded_rectangle(box, radius=rad * scale, fill=BOOT_FILL)

    # 밑창과 발등 주름
    sole = R(-20, 40, 60, 51)
    d.rounded_rectangle(sole, radius=6 * scale, fill=BOOT_DARK)
    crease = R(24, 12, 60, 34)
    d.arc(crease, start=180 if side > 0 else 0, end=270 if side > 0 else 90,
          fill=BOOT_DARK, width=max(1, int(round(ow * 0.7))))

    _BOOT_CACHE[key] = img
    return img


def _paste_boot(img, ankle, side, scale, tilt=0.0):
    """부츠 조각을 발목 위치에 (필요하면 회전해서) 얹는다."""
    piece = _boot_layer(scale, side)
    ox, oy = -_BOOT_BOX[0] * scale, -_BOOT_BOX[1] * scale
    if abs(tilt) > 0.01:
        piece = piece.rotate(-tilt, resample=Image.BICUBIC, center=(ox, oy))
    img.alpha_composite(piece, (int(round(ankle[0] - ox)), int(round(ankle[1] - oy))))


def _draw_hand(d, p, scale, side):
    """장갑(벙어리 손). 부츠와 같은 갈색이라 발과 짝이 맞는다.

    손바닥과 엄지의 **외곽선을 먼저 둘 다 깔고 그 다음에 채움을 둘 다 덮는다** -
    하나씩 완성하면 두 원이 겹치는 자리에 검은 이음매가 남는다.
    """

    def S(v):
        return v * scale

    t = (p[0] - side * S(HAND_R * 0.66), p[1] + S(HAND_R * 0.46))
    tr = S(HAND_R * 0.44)
    for c, r in ((p, S(HAND_R)), (t, tr)):
        rr = r + S(OUTLINE) * 0.5
        d.ellipse([c[0] - rr, c[1] - rr, c[0] + rr, c[1] + rr], fill=INK)
    for c, r in ((p, S(HAND_R)), (t, tr)):
        d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=BOOT_FILL)


# --- 포즈 --------------------------------------------------------------------


class Pose:
    """한 프레임의 포즈. 각도는 도(degree), 길이는 캐릭터 단위."""

    def __init__(self):
        self.body_x = 0.0  # 몸통 중심 이동
        self.body_y = 0.0
        self.lean = 0.0  # 몸통 회전(+가 시계방향)
        self.eyes = "open"
        # 발목 위치(월드) - None이면 기본 자리
        self.foot_dx = [0.0, 0.0]  # [왼, 오]
        self.foot_lift = [0.0, 0.0]
        self.foot_tilt = [0.0, 0.0]
        # 팔: (위팔 각도, 아래팔 각도) - 왼쪽/오른쪽
        self.arm = [(120.0, 100.0), (60.0, 80.0)]


def draw_character(scale, pose):
    """포즈 하나를 RGBA 타일에 그려 돌려준다."""
    W, H = tile_size_px(scale)
    ax, ay = ANCHOR_PX(scale)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def S(v):
        return v * scale

    ow = S(OUTLINE)
    bx, by = pose.body_x, pose.body_y

    def P(x, y):
        """캐릭터 로컬 좌표 -> 타일 픽셀. 몸통 회전과 이동을 함께 적용."""
        rx, ry = _rot((x, y), pose.lean)
        return (ax + (rx + bx) * scale, ay + (ry + by) * scale)

    # --- 다리(몸통 뒤) ---
    for i, side in enumerate((-1, 1)):
        hip = P(side * HIP[0], HIP[1])
        ankle = (
            ax + (side * HIP[0] + pose.foot_dx[i]) * scale,
            ay + (ANKLE_Y - pose.foot_lift[i]) * scale,
        )
        knee = _two_bone(hip, ankle, S(THIGH_LEN), S(SHIN_LEN), bend=side)
        _capsule(d, hip, knee, S(THIGH_W), LIMB_FILL, ow)
        _capsule(d, knee, ankle, S(SHIN_W), LIMB_FILL, ow)
        _dot(d, hip, S(HIP_R), JOINT_FILL, ow)
        _dot(d, knee, S(KNEE_R), JOINT_FILL, ow)
        _dot(d, ankle, S(ANKLE_R), JOINT_FILL, ow)
        _paste_boot(img, ankle, side, scale, pose.foot_tilt[i])

    # --- 몸통 ---
    body = body_layer(scale, pose.eyes)
    if abs(pose.lean) > 0.01:
        body = body.rotate(-pose.lean, resample=Image.BICUBIC, center=(ax, ay))
    img.alpha_composite(body, (int(round(bx * scale)), int(round(by * scale))))

    # --- 팔(몸통 위) ---
    for i, side in enumerate((-1, 1)):
        sh = P(side * SHOULDER[0], SHOULDER[1])
        a1, a2 = pose.arm[i]
        elbow = _polar(sh, a1 + pose.lean, S(UPPER_LEN))
        hand = _polar(elbow, a2 + pose.lean, S(FORE_LEN))
        _capsule(d, sh, elbow, S(UPPER_W), LIMB_FILL, ow)
        _capsule(d, elbow, hand, S(FORE_W), LIMB_FILL, ow)
        _dot(d, sh, S(SHOULDER_R), JOINT_FILL, ow)
        _dot(d, elbow, S(ELBOW_R), JOINT_FILL, ow)
        _draw_hand(d, hand, scale, side)

    return img
