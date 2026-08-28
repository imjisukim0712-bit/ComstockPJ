# -*- coding: utf-8 -*-
"""컴스톡 단일 상황 패러디 숏츠 V4 - 요리/응급처치/반려동물 훈련, 한영 6종."""
from __future__ import annotations

import argparse
import array
import base64
import hashlib
import importlib.util
import io
import json
import math
import random
import re
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
V3_SCRIPT = ROOT / "output" / "comstock-parody-shorts-v3" / "render_parody_v3.py"
spec = importlib.util.spec_from_file_location("parody_v3", V3_SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError("V3 공용 렌더 모듈을 읽을 수 없습니다")
V3 = importlib.util.module_from_spec(spec)
sys.modules["parody_v3"] = V3
spec.loader.exec_module(V3)
B = V3.B

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

W, H, OUT_W, OUT_H = B.W, B.H, B.OUT_W, B.OUT_H
FPS, DUR, FRAMES, END_AT = B.FPS, B.DUR, B.FRAMES, 12.6
FFMPEG, RES, LANCZOS = B.FFMPEG, B.RES, B.LANCZOS
ENDCARD = HERE / "endcard_source.png"
B.ENDCARD = ENDCARD

COPY = {
    "cooking": {
        "en": ["15-SECOND RECIPE", "INGREDIENT: 1 ZOMBIE", "ADD 1 GUN", "MAKE THAT 14", "HIGH HEAT · 3 SEC", "DONE."],
        "ko": ["15초 요리", "재료: 좀비 1마리", "총 1정을 넣습니다", "아니, 14정", "강불 · 3초", "완성."],
    },
    "firstaid": {
        "en": ["FIRST AID", "A ZOMBIE IS DOWN", "CHECK FOR A PULSE", "CHOMP", "VERY HEALTHY", "NOW IT IS DOWN"],
        "ko": ["응급처치", "좀비가 쓰러졌습니다", "맥박을 확인하세요", "콱", "아주 건강합니다", "이제 쓰러졌습니다"],
    },
    "sit": {
        "en": ["ZOMBIE TRAINING", "LESSON 1: SIT", "SIT.", "I SAID SIT.", "GOOD BOY."],
        "ko": ["좀비 훈련", "1교시: 앉아", "앉아.", "앉으라고.", "옳지."],
    },
}

NARRATION = {
    "cooking": {
        "en": [(0.1, "Today, one zombie."), (2.4, "Add one gun."), (4.4, "Actually, fourteen."), (6.1, "High heat. Three seconds."), (10.0, "Done.")],
        "ko": [(0.1, "오늘의 재료, 좀비 한 마리."), (2.4, "총 한 정을 넣습니다."), (4.4, "아니, 열네 정."), (6.1, "강불로 삼 초."), (10.0, "완성.")],
    },
    "firstaid": {
        "en": [(0.1, "A zombie is down."), (2.2, "Check for a pulse."), (4.6, "Pulse confirmed."), (6.2, "Very healthy."), (10.0, "Now it is down.")],
        "ko": [(0.1, "좀비가 쓰러졌습니다."), (2.2, "맥박을 확인하세요."), (4.6, "맥박 확인."), (6.2, "아주 건강합니다."), (10.0, "이제 쓰러졌습니다.")],
    },
    "sit": {
        "en": [(0.1, "Zombie training."), (2.0, "Lesson one. Sit."), (4.3, "Sit."), (6.0, "I said sit."), (10.1, "Good boy.")],
        "ko": [(0.1, "좀비 훈련."), (2.0, "일 교시. 앉아."), (4.3, "앉아."), (6.0, "앉으라고."), (10.1, "옳지.")],
    },
}

CUTS = {
    "cooking": (2.25, 4.25, 6.0, 9.75, END_AT),
    "firstaid": (2.1, 4.25, 6.0, 7.55, 9.75, END_AT),
    "sit": (1.9, 4.0, 5.65, 7.15, 9.75, END_AT),
}


def bg_gradient(top, bottom):
    im = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(im)
    for y in range(H):
        q = y / (H - 1)
        c = tuple(int(top[i] * (1 - q) + bottom[i] * q) for i in range(3))
        d.line((0, y, W, y), fill=(*c, 255))
    return im


def label(cnv, s, lang, y=76, color=(255, 236, 69), size=58):
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rounded_rectangle((22, y - 48, W - 22, y + 48), 18, fill=(6, 8, 14, 232), outline=(255, 255, 255, 220), width=4)
    B.text(cnv, s, lang, (W / 2, y), W - 64, 82, size, fill=color, stroke=5)


def zombie(cnv, x, y, h, flip=False, rotate=0, alpha=1.0):
    im = B.sprite("Enemy_zombie.png", height=h, flip=flip, rotate=rotate)
    B.paste(cnv, im, x, y, "cb", alpha=alpha)


def zombie_row(cnv, y, count=5, h=245, t=0.0):
    for i in range(count):
        x = 42 + i * (W - 84) / max(1, count - 1)
        zombie(cnv, x, y + (i % 2) * 18, h, flip=i % 2 == 0, rotate=math.sin(t * 7 + i) * 2)


def weapon_fan(cnv, x, y, t):
    names = ["RightHMG.png", "RightRocketLauncher.png", "RightPlasmaCannon.png", "RightCombatShotgun.png", "RightSMG.png"]
    for i, name in enumerate(names):
        ang = -58 + i * 29 + math.sin(t * 11 + i) * 2
        im = B.sprite(name, height=138, rotate=ang)
        B.paste(cnv, im, x + (i - 2) * 24, y - abs(i - 2) * 12, "cc")


def plate(cnv, x, y, w=330):
    d = ImageDraw.Draw(cnv, "RGBA")
    d.ellipse((x - w / 2, y - 52, x + w / 2, y + 52), fill=(224, 229, 231), outline=(58, 67, 72), width=7)
    d.ellipse((x - w * .34, y - 28, x + w * .34, y + 28), fill=(250, 251, 249), outline=(174, 182, 184), width=3)


def cooking_frame(t, lang):
    C = COPY["cooking"][lang]
    cnv = bg_gradient((255, 196, 77), (214, 69, 43))
    d = ImageDraw.Draw(cnv, "RGBA")
    # 한 공간: 타일 벽 + 조리대.
    for x in range(0, W, 90): d.line((x, 0, x, 560), fill=(255, 255, 255, 65), width=3)
    for y in range(0, 560, 90): d.line((0, y, W, y), fill=(255, 255, 255, 65), width=3)
    d.rectangle((0, 610, W, H), fill=(87, 48, 34), outline=(37, 25, 20), width=8)
    d.rectangle((0, 595, W, 660), fill=(237, 212, 168), outline=(70, 46, 28), width=7)
    if t < 2.25:
        label(cnv, C[0], lang)
        d.rounded_rectangle((75, 485, 465, 625), 26, fill=(232, 194, 137), outline=(95, 50, 26), width=8)
        zombie(cnv, 275, 635, 390, rotate=88)
        B.text(cnv, C[1], lang, (270, 795), 470, 100, 54, fill=(255, 255, 255), stroke=7)
    elif t < 4.25:
        label(cnv, C[2], lang)
        zombie(cnv, 350, 640, 380, rotate=88)
        B.draw_hero(cnv, 145, 810, 350, t)
        gun = B.sprite("RightHMG.png", height=170, rotate=-7)
        B.paste(cnv, gun, 275, 520)
        d.ellipse((235, 450, 315, 530), outline=(255, 245, 80), width=8)
        B.text(cnv, "×1", "en", (405, 790), 120, 80, 62, fill=(255, 255, 255), stroke=7)
    elif t < 6.0:
        zombie_row(cnv, 590, 5, 310, t)
        d.rectangle((0, 595, W, 660), fill=(237, 212, 168), outline=(70, 46, 28), width=7)
        B.draw_hero(cnv, 270, 850, 370, t)
        weapon_fan(cnv, 270, 480, t)
        label(cnv, C[3], lang, color=(255, 87, 58), size=66)
        B.text(cnv, "×14", "en", (270, 660), 240, 110, 92, fill=(255, 237, 62), stroke=10)
    elif t < 9.75:
        zombie_row(cnv, 625, 6, 330, t)
        B.draw_hero(cnv, 270, 875, 420, t)
        weapon_fan(cnv, 270, 470, t)
        label(cnv, C[4], lang, color=(255, 89, 44), size=56)
        for i, at in enumerate((6.1, 6.65, 7.2, 7.75, 8.3, 8.85)):
            B.explosion(cnv, 65 + (i % 3) * 205, 545 + (i // 3) * 145, t, at, h=250)
        B.muzzle(cnv, 405, 625, t, 190)
    else:
        plate(cnv, 270, 650)
        d.ellipse((45, 430, 495, 830), fill=(210, 210, 210, 45))
        boot = B.sprite("Parts/Foot.png", height=220, rotate=-10)
        B.paste(cnv, boot, 270, 640)
        label(cnv, C[5], lang, color=(112, 255, 126), size=82)
        B.text(cnv, "★", "en", (270, 840), 150, 120, 88, fill=(255, 220, 44), stroke=8)
    return cnv.convert("RGB")


def firstaid_frame(t, lang):
    C = COPY["firstaid"][lang]
    cnv = bg_gradient((218, 247, 250), (110, 186, 201))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle((0, 690, W, H), fill=(211, 222, 220))
    for x in range(0, W, 68): d.line((x, 690, x + 80, H), fill=(150, 165, 164), width=2)
    d.rectangle((22, 20, W - 22, 125), fill=(247, 250, 249), outline=(26, 101, 112), width=5)
    B.text(cnv, C[0], lang, (270, 72), 470, 75, 57, fill=(19, 91, 103), stroke=0, kind="bold", shadow=False)
    if t < 2.1:
        zombie(cnv, 285, 735, 420, rotate=88)
        d.polygon(((70, 280), (140, 280), (140, 210), (200, 210), (200, 280), (270, 280), (270, 340), (200, 340), (200, 410), (140, 410), (140, 340), (70, 340)), fill=(231, 64, 64))
        B.text(cnv, C[1], lang, (270, 560), 480, 115, 60, fill=(255, 255, 255), stroke=7)
    elif t < 4.25:
        zombie(cnv, 330, 760, 430, rotate=88)
        B.draw_hero(cnv, 120, 830, 320, t, rotate=8)
        d.line((170, 610, 285, 665), fill=(180, 190, 194), width=24)
        d.ellipse((270, 642, 310, 682), fill=(205, 215, 218), outline=(20, 30, 34), width=4)
        label(cnv, C[2], lang, y=185, color=(255, 232, 62), size=55)
    elif t < 6.0:
        zombie(cnv, 330, 760, 430, rotate=88)
        B.draw_hero(cnv, 125, 830, 320, t, rotate=-8)
        # 물린 손을 크게 보여준다.
        d.rounded_rectangle((145, 400, 395, 640), 38, fill=(221, 227, 228), outline=(23, 29, 32), width=8)
        for x, y in ((205, 470), (255, 450), (305, 470), (235, 540), (290, 545)):
            d.ellipse((x - 13, y - 13, x + 13, y + 13), fill=(181, 35, 41))
        B.text(cnv, C[3], lang, (270, 330), 320, 100, 78, fill=(227, 42, 47), stroke=8)
        B.text(cnv, "PULSE CONFIRMED" if lang == "en" else "맥박 있음", lang, (270, 705), 350, 75, 48, fill=(35, 188, 90), stroke=5)
    elif t < 7.55:
        zombie(cnv, 270, 815, 590, rotate=-2)
        label(cnv, C[4], lang, y=185, color=(70, 220, 105), size=64)
        d.rounded_rectangle((155, 690, 385, 770), 18, fill=(35, 188, 90), outline=(255, 255, 255), width=4)
        B.text(cnv, "100%", "en", (270, 730), 190, 65, 54, fill=(255, 255, 255), stroke=3)
    elif t < 9.75:
        zombie(cnv, 390, 825, 560, flip=True)
        B.draw_hero(cnv, 130, 850, 360, t)
        B.muzzle(cnv, 330, 620, t, 220)
        B.explosion(cnv, 390, 610, t, 7.6, h=290)
        B.explosion(cnv, 390, 660, t, 8.35, h=270)
    else:
        zombie(cnv, 285, 760, 420, rotate=88, alpha=.82)
        B.draw_hero(cnv, 110, 860, 300, t)
        label(cnv, C[5], lang, y=190, color=(255, 232, 62), size=60)
        d.rounded_rectangle((90, 355, 450, 455), 18, fill=(33, 122, 137), outline=(255, 255, 255), width=4)
        B.text(cnv, "LESSON COMPLETE" if lang == "en" else "교육 완료", lang, (270, 405), 330, 74, 48, fill=(255, 255, 255), stroke=3)
    return cnv.convert("RGB")


def leash(cnv, a, b, slack=18):
    d = ImageDraw.Draw(cnv)
    x1, y1 = a; x2, y2 = b
    pts = []
    for i in range(15):
        q = i / 14
        pts.append((x1 * (1 - q) + x2 * q, y1 * (1 - q) + y2 * q + math.sin(q * math.pi) * slack))
    d.line(pts, fill=(222, 47, 54), width=9)


def sit_frame(t, lang):
    C = COPY["sit"][lang]
    cnv = bg_gradient((129, 210, 255), (83, 176, 103))
    d = ImageDraw.Draw(cnv, "RGBA")
    d.rectangle((0, 650, W, H), fill=(82, 161, 83))
    for x in range(20, W, 90): d.ellipse((x, 700 + (x % 3) * 38, x + 5, 706 + (x % 3) * 38), fill=(211, 242, 150))
    d.ellipse((405, 80, 490, 165), fill=(255, 240, 89))
    if t < 1.9:
        label(cnv, C[0], lang)
        B.draw_hero(cnv, 120, 850, 330, t)
        zombie(cnv, 390, 850, 470, flip=True)
        leash(cnv, (170, 700), (330, 675))
        B.text(cnv, C[1], lang, (270, 330), 470, 120, 66, fill=(255, 255, 255), stroke=8)
    elif t < 4.0:
        B.draw_hero(cnv, 120, 850, 330, t)
        zombie(cnv, 390, 850, 470, flip=True)
        leash(cnv, (170, 700), (330, 675))
        label(cnv, C[2], lang, y=190, color=(255, 232, 62), size=92)
        # 손 위 간식.
        d.line((165, 590, 235, 540), fill=(205, 214, 216), width=20)
        d.ellipse((226, 525, 252, 548), fill=(111, 65, 28), outline=(45, 25, 12), width=3)
    elif t < 5.65:
        # 좀비가 앉지 않고 로봇에게 달려든다.
        p = (t - 4.0) / 1.65
        zx = 390 - 150 * p
        zombie(cnv, zx, 850, 510, flip=True, rotate=-5 * p)
        B.draw_hero(cnv, 120, 850, 330, t)
        leash(cnv, (170, 700), (zx - 55, 675), slack=5)
        B.text(cnv, "GRRR" if lang == "en" else "크르릉", lang, (340, 350), 300, 95, 66, fill=(222, 40, 45), stroke=8)
    elif t < 7.15:
        zombie(cnv, 250, 850, 520, flip=True, rotate=-7)
        B.draw_hero(cnv, 105, 850, 340, t)
        label(cnv, C[3], lang, y=190, color=(255, 76, 56), size=75)
    elif t < 9.75:
        zombie(cnv, 350, 850, 510, flip=True)
        B.draw_hero(cnv, 115, 850, 360, t)
        B.muzzle(cnv, 315, 620, t, 210)
        B.explosion(cnv, 355, 650, t, 7.2, h=260)
        B.explosion(cnv, 355, 670, t, 8.15, h=260)
    else:
        zombie(cnv, 320, 780, 420, rotate=88, alpha=.82)
        B.draw_hero(cnv, 105, 850, 300, t)
        leash(cnv, (160, 700), (290, 735), slack=8)
        label(cnv, C[4], lang, y=190, color=(118, 255, 120), size=86)
        d.ellipse((280, 585, 320, 620), fill=(111, 65, 28), outline=(45, 25, 12), width=4)
        d.polygon(((400, 410), (420, 450), (468, 456), (434, 487), (444, 535), (400, 510), (356, 535), (366, 487), (332, 456), (380, 450)), fill=(255, 221, 47), outline=(65, 45, 15))
    return cnv.convert("RGB")


def raw_frame(concept, lang, t):
    if t >= END_AT:
        return B.endcard_frame(t - END_AT, lang)
    if concept == "cooking": return cooking_frame(t, lang)
    if concept == "firstaid": return firstaid_frame(t, lang)
    return sit_frame(t, lang)


def frame_at(concept, lang, t):
    im = raw_frame(concept, lang, t)
    flash = sum(math.exp(-((t - c) / .03) ** 2) for c in CUTS[concept])
    if flash > .03:
        im = Image.blend(im, Image.new("RGB", im.size, (255, 255, 255)), min(.72, flash * .68))
    action = ((concept == "cooking" and 6 < t < 9.75) or (concept == "firstaid" and 7.55 < t < 9.75) or (concept == "sit" and 7.15 < t < 9.75))
    if action:
        rng = random.Random(int(t * FPS) * 7907 + 91)
        im = ImageChops.offset(im, rng.randint(-6, 6), rng.randint(-4, 4))
    return ImageEnhance.Color(im).enhance(1.08)


def render_silent(concept, lang, out):
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-", "-an", "-vf", f"scale={OUT_W}:{OUT_H}:flags=lanczos,format=yuv420p", "-c:v", "libx264", "-preset", "medium", "-crf", "17", "-pix_fmt", "yuv420p", "-r", str(FPS), "-t", str(DUR), "-movflags", "+faststart", str(out)]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    assert p.stdin is not None
    for f in range(FRAMES):
        p.stdin.write(frame_at(concept, lang, f / FPS).tobytes())
        if f % 120 == 0: print(f"  {concept}/{lang} {f:03d}/{FRAMES}", flush=True)
    p.stdin.close()
    if p.wait(): raise RuntimeError(f"비디오 인코딩 실패: {concept}/{lang}")


def build_bed(concept, path):
    sr, n = 48000, int(48000 * DUR)
    out = array.array("h", [0]) * n
    rng = random.Random({"cooking": 801, "firstaid": 802, "sit": 803}[concept])
    bpm = {"cooking": 148, "firstaid": 118, "sit": 142}[concept]
    beat = 60 / bpm
    for i in range(n):
        t = i / sr; ph = (t / beat) % 1
        kick = math.exp(-ph * 14) * math.sin(math.tau * (58 - 18 * ph) * t)
        tone = .032 * math.sin(math.tau * ({"cooking": 196, "firstaid": 146.8, "sit": 220}[concept]) * t)
        v = .10 * kick + tone + rng.uniform(-.004, .004)
        out[i] = max(-32768, min(32767, int(v * 32767)))
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1); wf.setsampwidth(2); wf.setframerate(sr); wf.writeframes(out.tobytes())


SFX = {
    "cooking": [("UI_Click.wav", 2.25, .7), ("LevelUp.wav", 4.25, .65), ("Weapon_RapidFire.wav", 6.05, .5), ("Weapon_Explosive.wav", 7.15, .85), ("Enemy_Death.wav", 8.4, .6), ("LevelUp.wav", 9.75, .7), ("LevelUp.wav", 12.6, .7)],
    "firstaid": [("UI_Click.wav", 2.1, .6), ("Player_Hit.wav", 4.25, .8), ("LevelUp.wav", 6.0, .55), ("Weapon_RapidFire.wav", 7.55, .45), ("Weapon_Explosive.wav", 8.25, .8), ("Enemy_Death.wav", 9.0, .6), ("LevelUp.wav", 9.75, .65), ("LevelUp.wav", 12.6, .7)],
    "sit": [("UI_Click.wav", 1.9, .6), ("UI_Click.wav", 4.0, .6), ("Player_Hit.wav", 5.65, .65), ("Weapon_RapidFire.wav", 7.15, .45), ("Weapon_Explosive.wav", 8.0, .8), ("Enemy_Death.wav", 8.8, .55), ("LevelUp.wav", 9.75, .7), ("LevelUp.wav", 12.6, .7)],
}


def tts_clip(text, lang, path):
    voice = "Microsoft Zira Desktop" if lang == "en" else "Microsoft Heami Desktop"
    rate = 2 if lang == "en" else 1
    esc_text = text.replace("'", "''"); esc_path = str(path).replace("'", "''")
    ps = f"Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.SelectVoice('{voice}'); $s.Rate={rate}; $s.Volume=100; $s.SetOutputToWaveFile('{esc_path}'); $s.Speak('{esc_text}'); $s.Dispose()"
    encoded = base64.b64encode(ps.encode("utf-16le")).decode("ascii")
    subprocess.run(["powershell.exe", "-NoProfile", "-EncodedCommand", encoded], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    ok = path.exists() and path.stat().st_size > 100
    if not ok: path.unlink(missing_ok=True)
    return ok


def mix_audio(silent, bed, concept, lang, out):
    phrases = NARRATION[concept][lang]
    voiced = []
    for idx, (at, words) in enumerate(phrases):
        p = HERE / f"_tts_{concept}_{lang}_{idx}.wav"
        if tts_clip(words, lang, p): voiced.append((at, p))
    schedule = SFX[concept]
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "error", "-i", str(silent), "-i", str(bed)]
    for name, _, _ in schedule: cmd += ["-i", str(RES / "SFX" / name)]
    for _, p in voiced: cmd += ["-i", str(p)]
    filters = ["[1:a]volume=.72[bed]"]; labels = ["[bed]"]
    next_idx = 2
    for name, at, gain in schedule:
        delay = int(at * 1000); lab = f"s{next_idx}"
        filters.append(f"[{next_idx}:a]volume={gain},adelay={delay}|{delay}[{lab}]"); labels.append(f"[{lab}]"); next_idx += 1
    for at, p in voiced:
        delay = int(at * 1000); lab = f"v{next_idx}"
        filters.append(f"[{next_idx}:a]volume=1.65,adelay={delay}|{delay}[{lab}]"); labels.append(f"[{lab}]"); next_idx += 1
    filters.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0:duration=longest,acompressor=threshold=0.12:ratio=4:attack=5:release=120:makeup=1.35,alimiter=limit=0.92,atrim=0:15,afade=t=out:st=14.82:d=0.18[aout]")
    cmd += ["-filter_complex", ";".join(filters), "-map", "0:v:0", "-map", "[aout]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-t", "15", "-movflags", "+faststart", str(out)]
    subprocess.run(cmd, check=True)
    for _, p in voiced: p.unlink(missing_ok=True)


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""): h.update(chunk)
    return h.hexdigest()


def probe(path):
    p = subprocess.run([FFMPEG, "-hide_banner", "-i", str(path)], text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    dm = re.search(r"Duration: (\d+):(\d+):(\d+\.\d+)", p.stderr)
    vm = re.search(r"Video: h264.*?, yuv420p.*?, (\d+)x(\d+).*?, (\d+(?:\.\d+)?) fps", p.stderr)
    if not dm or not vm: raise RuntimeError(f"검증 실패: {path}")
    return {"duration": int(dm.group(1)) * 3600 + int(dm.group(2)) * 60 + float(dm.group(3)), "width": int(vm.group(1)), "height": int(vm.group(2)), "fps": float(vm.group(3)), "aac_audio": "Audio: aac" in p.stderr}


SHEET_TIMES = [.45, 2.55, 4.55, 6.55, 8.55, 10.35, 13.5]


def decoded_frame(path, t):
    p = subprocess.run([FFMPEG, "-hide_banner", "-loglevel", "error", "-ss", str(t), "-i", str(path), "-frames:v", "1", "-f", "image2pipe", "-vcodec", "png", "-"], stdout=subprocess.PIPE, check=True)
    return Image.open(io.BytesIO(p.stdout)).convert("RGB")


def make_sheet(concept):
    tw, th = 270, 480
    sheet = Image.new("RGB", (1890, 990), (12, 12, 16)); d = ImageDraw.Draw(sheet)
    for li, lang in enumerate(("en", "ko")):
        video = HERE / f"Comstock_ClearV4_{concept.title()}_{lang.upper()}_15s.mp4"
        for i, t in enumerate(SHEET_TIMES):
            im = decoded_frame(video, t) if video.exists() else frame_at(concept, lang, t)
            sheet.paste(im.resize((tw, th), LANCZOS), (i * tw, 25 + li * th))
        d.text((8, 3 + li * th), f"{concept.upper()} — {lang.upper()}", font=B.font("en", "bold", 17), fill=(255, 255, 255))
    out = HERE / f"storyboard-{concept}.jpg"; sheet.save(out, quality=92); return out


def render_one(concept, lang):
    stem = f"Comstock_ClearV4_{concept.title()}_{lang.upper()}_15s"
    silent = HERE / f"_{stem}_silent.mp4"; bed = HERE / f"_bed_{concept}.wav"; out = HERE / f"{stem}.mp4"
    if not bed.exists(): build_bed(concept, bed)
    render_silent(concept, lang, silent); mix_audio(silent, bed, concept, lang, out); silent.unlink(missing_ok=True)
    info = probe(out)
    if abs(info["duration"] - DUR) > .05 or (info["width"], info["height"]) != (OUT_W, OUT_H) or not info["aac_audio"]: raise RuntimeError(f"출력 규격 오류: {out}: {info}")
    return {"concept": concept, "language": lang, "output": out.name, "sha256": sha256(out), "probe": info}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--concept", choices=("cooking", "firstaid", "sit", "all"), default="all"); ap.add_argument("--lang", choices=("en", "ko", "all"), default="all"); ap.add_argument("--preview", action="store_true"); args = ap.parse_args()
    concepts = ("cooking", "firstaid", "sit") if args.concept == "all" else (args.concept,)
    langs = ("en", "ko") if args.lang == "all" else (args.lang,)
    if args.preview:
        for c in concepts: print(make_sheet(c))
        return
    exports = []
    for c in concepts:
        for lang in langs:
            print(f"render: {c}/{lang}"); exports.append(render_one(c, lang))
        make_sheet(c)
    manifest = {
        "title": "컴스톡 단일 상황 패러디 숏츠 V4 3종 × 한/영", "format": {"size": [OUT_W, OUT_H], "aspect": "9:16", "fps": FPS, "duration_seconds": DUR, "video": "H.264/yuv420p", "audio": "AAC 48kHz stereo"},
        "narration": "Windows TTS API가 보안 설정으로 음성 합성을 거부하여 미사용. 무음 자동재생 기준의 행동·짧은 자막으로 설계.",
        "story_rule": "상황 1개 → 잘못된 해결 1개 → 결과 1개", "parodies": {"cooking": "15초 요리 숏츠", "firstaid": "응급처치 교육 영상", "sit": "반려동물 훈련 영상"},
        "required_copy": {"ko": "좀비보다 먼저 플레이하세요.", "en": "PLAY IT BEFORE THE ZOMBIES DO."}, "endcard": {"start_seconds": END_AT, "source": ENDCARD.name, "sha256": sha256(ENDCARD)},
        "sources": ["dev/pv/assets/comstock_hero.png", "Assets/Resources/Enemy_zombie.png", "Assets/Resources/Parts/Foot.png", "Assets/Resources/Right*.png", "Assets/Resources/Explosion/*.png", "Assets/Resources/MuzzleFlash/*.png", "Assets/Resources/SFX/*.wav"],
        "exports": exports, "storyboards": [f"storyboard-{c}.jpg" for c in concepts], "generator": Path(__file__).name,
    }
    (HERE / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    for c in concepts: (HERE / f"_bed_{c}.wav").unlink(missing_ok=True)
    print("complete")


if __name__ == "__main__": main()
