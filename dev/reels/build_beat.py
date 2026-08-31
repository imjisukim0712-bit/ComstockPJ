# -*- coding: utf-8 -*-
"""컴스톡 릴스 - 15초 비트 트랙.

원본 밈의 음원은 남의 것이라 쓰지 않는다. 대신 **영상의 히트 격자와 같은 재료**
(`reel_common.HITS`)로 칩튠 루프를 직접 합성하고, 게임 효과음 두 발만 강조로 얹는다.
그래서 비트와 얼굴 튐이 어긋날 수가 없다 - 좌표를 두 벌 관리하지 않는다.

릴스에 "유행하는 사운드"를 붙일 거면 이 트랙 대신 무음판(`Comstock_Reel_*.mp4`)을 올린다.
"""
import array
import math
import os
import random
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reel_common import (RES, CACHE, OUT, DUR, BAR, BARS, BEAT, HITS,
                         ENDCARD_BAR, ffmpeg_exe)

SR = 48000
N = int(SR * DUR)
FF = ffmpeg_exe()


# ---------------------------------------------------------------- 합성 악기
def kick(buf, t, gain=1.0):
    """110Hz에서 45Hz로 떨어지는 사인 + 딸깍. "담"에 쓴다."""
    i0 = int(t * SR)
    dur = 0.20
    for k in range(int(dur * SR)):
        i = i0 + k
        if i >= N:
            break
        u = k / SR
        f = 45 + 65 * math.exp(-u * 32)
        env = math.exp(-u * 16)
        buf[i] += gain * 0.95 * env * math.sin(2 * math.pi * f * u)
        if u < 0.006:
            buf[i] += gain * 0.30 * (1 - u / 0.006) * random.uniform(-1, 1)


def hat(buf, t, gain=1.0, dur=0.045):
    """짧은 노이즈. "디디"에 쓴다."""
    i0 = int(t * SR)
    prev = 0.0
    for k in range(int(dur * SR)):
        i = i0 + k
        if i >= N:
            break
        u = k / SR
        env = math.exp(-u * 90)
        n = random.uniform(-1, 1)
        hp = n - prev          # 1차 하이패스라 "치" 소리가 난다
        prev = n
        buf[i] += gain * 0.55 * env * hp


def clap(buf, t, gain=1.0):
    i0 = int(t * SR)
    prev = 0.0
    for k in range(int(0.16 * SR)):
        i = i0 + k
        if i >= N:
            break
        u = k / SR
        env = math.exp(-u * 26) * (1.0 if u > 0.008 else u / 0.008)
        n = random.uniform(-1, 1)
        bp = n - prev * 0.6
        prev = n
        buf[i] += gain * 0.30 * env * bp


def blip(buf, t, midi, dur, gain=1.0, duty=0.5):
    """사각파 한 음(칩튠). 로봇 게임이니 이 음색이 맞다."""
    f = 440.0 * (2 ** ((midi - 69) / 12.0))
    i0 = int(t * SR)
    ns = int(dur * SR)
    for k in range(ns):
        i = i0 + k
        if i >= N:
            break
        u = k / SR
        env = min(1.0, u / 0.004) * math.exp(-u * 6.0)
        ph = (f * u) % 1.0
        buf[i] += gain * 0.11 * env * (1.0 if ph < duty else -1.0)


# ---------------------------------------------------------------- 게임 효과음
def _decode(path):
    os.makedirs(CACHE, exist_ok=True)
    out = os.path.join(CACHE, "a_" + os.path.basename(path) + ".wav")
    if not os.path.exists(out):
        subprocess.run([FF, "-y", "-hide_banner", "-loglevel", "error", "-i", path,
                        "-ac", "1", "-ar", str(SR), "-c:a", "pcm_s16le", out], check=True)
    with wave.open(out, "rb") as w:
        a = array.array("h")
        a.frombytes(w.readframes(w.getnframes()))
    return a


def sfx(buf, name, t, gain=1.0):
    src = _decode(os.path.join(RES, "SFX", name))
    i0 = int(t * SR)
    for k in range(len(src)):
        i = i0 + k
        if i >= N:
            break
        buf[i] += gain * src[k] / 32768.0


# ---------------------------------------------------------------- 곡
# 8마디 코드 진행. 각 원소는 (베이스 midi, 아르페지오 4음).
CHORDS = [
    (45, (57, 60, 64, 69)),   # Am
    (45, (57, 60, 64, 69)),
    (41, (53, 57, 60, 65)),   # F
    (41, (53, 57, 60, 65)),
    (48, (60, 64, 67, 72)),   # C
    (48, (60, 64, 67, 72)),
    (43, (55, 59, 62, 67)),   # G
    (43, (55, 59, 62, 67)),
]
ARP = (0, 1, 2, 3, 2, 1, 2, 3)


def build():
    random.seed(7)
    buf = array.array("d", bytes(8 * N))

    # 드럼 - 영상과 같은 히트 격자를 그대로 쓴다.
    for t, n, b, k in HITS:
        if k in (0, 1):
            kick(buf, t, 1.0)
        else:
            hat(buf, t, 1.25 if k in (2, 4) else 0.95)
    # 클랩은 매 마디 3번째 박(스네어 자리).
    for b in range(BARS):
        clap(buf, b * BAR + 2 * BEAT, 1.0)

    # 베이스 + 아르페지오
    eighth = BEAT / 2.0
    for b in range(BARS):
        root, notes = CHORDS[b]
        for j in range(8):
            t = b * BAR + j * eighth
            blip(buf, t, notes[ARP[j]], eighth * 0.9, gain=0.85, duty=0.25)
            if j % 2 == 0:
                blip(buf, t, root, eighth * 1.6, gain=1.15, duty=0.5)

    # 강조: 얼굴이 불어나는 마디마다 클릭, 제목 카드가 꽂힐 때 레벨업.
    for b in (2, 4, 6):
        sfx(buf, "UI_Click.wav", b * BAR, 0.7)
    sfx(buf, "LevelUp.wav", ENDCARD_BAR * BAR, 0.85)

    # 마지막 0.25초 페이드아웃(루프될 때 뚝 끊기지 않게).
    fade = int(0.25 * SR)
    for k in range(fade):
        buf[N - fade + k] *= 1.0 - k / fade

    # 리미터 - 피크를 -1dBFS로 맞춘다.
    peak = max(abs(v) for v in buf) or 1.0
    g = 0.891 / peak
    pcm = array.array("h", bytes(2 * N))
    for i in range(N):
        v = buf[i] * g
        pcm[i] = int(max(-32767, min(32767, v * 32767)))

    path = os.path.join(CACHE, "reel_beat.wav")
    os.makedirs(CACHE, exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print("비트 트랙:", path, "%.2f초" % DUR)
    return path


if __name__ == "__main__":
    build()
