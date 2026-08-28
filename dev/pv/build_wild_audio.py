# -*- coding: utf-8 -*-
"""컴스톡 PV "야생의 좀비" 오디오 - 다큐멘터리 사운드.

기존 3종 영상은 모두 게임 BGM(Title/Game/Boss)을 잘라 붙였다. 다큐는 그 곡들이 어울리지
않으므로 **BGM 파일을 한 개도 쓰지 않고** 여기서 직접 합성한다.
  - 낮은 현악풍 패드(사인 몇 개를 살짝 디튠해 겹친 것)가 장면마다 화음을 바꾼다.
  - 저역 바람 소리(노이즈 -> 1극 저역통과)가 폐허의 공기를 채운다.
  - 포식자가 나타날 때 저역이 떨어지는 한 방, 마지막 한 줄에 종소리 하나.
  - 게임 효과음은 실제 화면과 붙는 곳에만 쓴다. 폭발음은 wild_scenes.KILLS에서
    **화면 폭발 시각을 그대로 읽어와** 프레임과 정확히 맞춘다.

내레이션 음성은 쓰지 않는다(자막만) - 언어별로 오디오가 같아서 한 번만 만들면 된다.

사용법:  python build_wild_audio.py [출력경로.m4a]
"""
import array
import math
import os
import random
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv_common import RES, CACHE
from wild_common import DUR, TIMELINE

SR = 48000
N = int(SR * DUR)
SFXDIR = os.path.join(RES, "SFX")
buf = array.array("d", bytes(8 * N))

T = {n: t0 for (t0, _d, n) in TIMELINE}      # 장면 시작 시각


# ---------------------------------------------------------------- 합성
def tone(t0, t1, f, g, attack=0.9, release=1.2, detune=0.0, vib=0.0, vibf=0.16):
    i0, i1 = max(0, int(t0 * SR)), min(N, int(t1 * SR))
    if i1 <= i0:
        return
    ln = (i1 - i0) / SR
    dt = 1.0 / SR
    ph = 0.0
    fr0 = f * (1.0 + detune / 100.0)
    for i in range(i0, i1):
        tt = (i - i0) * dt
        env = 1.0
        if tt < attack:
            env = 0.5 - 0.5 * math.cos(math.pi * tt / attack)
        rem = ln - tt
        if rem < release:
            env *= 0.5 - 0.5 * math.cos(math.pi * max(0.0, rem) / release)
        fr = fr0 * (1.0 + vib * math.sin(2 * math.pi * vibf * tt)) if vib else fr0
        ph += 2 * math.pi * fr * dt
        buf[i] += g * env * math.sin(ph)


def pad(t0, t1, freqs, g, attack=1.1, release=1.4, vib=0.0035):
    """화음 하나. 음마다 살짝 어긋난 사인을 겹쳐 현악처럼 두껍게 만든다."""
    for f in freqs:
        tone(t0, t1, f, g, attack, release, detune=0.0, vib=vib)
        tone(t0, t1, f, g * 0.72, attack, release, detune=+0.35, vib=vib, vibf=0.11)
        tone(t0, t1, f * 2.0, g * 0.26, attack, release, detune=-0.3, vib=vib, vibf=0.19)


def wind(t0, t1, g, cut=0.010, seed=1234):
    """저역만 남긴 노이즈 = 폐허의 바람. 장면 전환과 무관하게 계속 깔린다."""
    rng = random.Random(seed)
    i0, i1 = max(0, int(t0 * SR)), min(N, int(t1 * SR))
    y = 0.0
    ln = max(1e-6, (i1 - i0) / SR)
    for i in range(i0, i1):
        y += cut * (rng.uniform(-1.0, 1.0) - y)
        tt = (i - i0) / SR
        env = min(1.0, tt / 1.4, max(0.0, (ln - tt) / 1.4))
        buf[i] += g * env * y * 26.0


def sub_drop(t0, dur, f0, f1, g):
    """포식자가 나타날 때 바닥이 내려앉는 소리."""
    i0, i1 = max(0, int(t0 * SR)), min(N, int((t0 + dur) * SR))
    ph = 0.0
    dt = 1.0 / SR
    for i in range(i0, i1):
        p = (i - i0) / max(1, (i1 - i0))
        ph += 2 * math.pi * (f0 + (f1 - f0) * p) * dt
        buf[i] += g * (1.0 - p) ** 1.6 * math.sin(ph)


def pulse_low(t0, t1, f, rate, g):
    """사냥 장면의 심장 박동 같은 저역 펄스(패드를 빼고 이것만 남긴다)."""
    i0, i1 = max(0, int(t0 * SR)), min(N, int(t1 * SR))
    ph = 0.0
    dt = 1.0 / SR
    for i in range(i0, i1):
        tt = (i - i0) * dt
        beat = (tt * rate) % 1.0
        env = math.exp(-beat * 5.5)
        ph += 2 * math.pi * f * dt
        buf[i] += g * env * math.sin(ph)


def blip(t0, f, dur, g):
    # 개체 수 표기가 한 칸 떨어질 때 나는 짧은 데이터 신호음
    i0, i1 = max(0, int(t0 * SR)), min(N, int((t0 + dur) * SR))
    ph = 0.0
    dt = 1.0 / SR
    for i in range(i0, i1):
        p = (i - i0) / max(1, (i1 - i0))
        ph += 2 * math.pi * f * dt
        buf[i] += g * math.exp(-p * 6.0) * math.sin(ph)


def bell(t0, f, dur, g):
    """마지막 한 줄이 뜰 때 울리는 종소리 하나."""
    for (mul, gg, dd) in ((1.0, 1.0, 1.0), (2.0, 0.45, 0.7), (3.01, 0.22, 0.5)):
        i0, i1 = max(0, int(t0 * SR)), min(N, int((t0 + dur * dd) * SR))
        ph = 0.0
        dt = 1.0 / SR
        for i in range(i0, i1):
            p = (i - i0) / max(1, (i1 - i0))
            ph += 2 * math.pi * f * mul * dt
            buf[i] += g * gg * math.exp(-p * 4.2) * math.sin(ph)


# ---------------------------------------------------------------- 게임 효과음
_dec = {}


def sfx(name):
    a = _dec.get(name)
    if a is None:
        os.makedirs(CACHE, exist_ok=True)
        out = os.path.join(CACHE, "a_" + name + ".wav")
        if not os.path.exists(out):
            subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                            "-i", os.path.join(SFXDIR, name), "-ac", "1", "-ar", str(SR),
                            "-c:a", "pcm_s16le", out], check=True)
        with wave.open(out, "rb") as w:
            raw = w.readframes(w.getnframes())
        a = array.array("h")
        a.frombytes(raw)
        _dec[name] = a
    return a


def add(name, t, g=1.0):
    src = sfx(name)
    off = int(t * SR)
    if off < 0:
        return
    end = min(N, off + len(src))
    for i in range(off, end):
        buf[i] += src[i - off] * g / 32768.0


def rapid(name, t0, t1, step, g, jitter=0.006, seed=7):
    rng = random.Random(seed)
    t = t0
    while t < t1:
        add(name, t + rng.uniform(-jitter, jitter), g * rng.uniform(0.8, 1.15))
        t += step


# ---------------------------------------------------------------- 구성
def build_bed(path):
    # ★ 편집이 바뀌면 여기 좌표도 같이 옮겨야 한다 - 전부 T[장면이름] 기준으로 적는다.
    print("  바람...", flush=True)
    wind(0.0, T["outro"] + 0.6, 0.055)
    wind(T["slaughter"], T["fallen"] + 0.4, 0.055, cut=0.016, seed=99)

    print("  패드...", flush=True)
    # 셋업 - 낮은 한 음만(조용히 관찰하는 긴장)
    pad(0.1, T["title"] + 0.2, (110.0,), 0.030, attack=1.3, release=0.7)
    # 타이틀 - 단조 화음이 짧게 열린다
    pad(T["title"], T["herd"] + 0.4, (110.0, 164.8, 261.6), 0.021, attack=0.6, release=0.9)
    # 무리 - 9음을 얹어 여유로운 분위기
    pad(T["herd"] - 0.2, T["arrival"] + 0.4, (110.0, 164.8, 246.9), 0.018)
    # 예외가 나타난다 - 반음 어긋난 화음 + 서서히 올라가는 한 음
    pad(T["arrival"] - 0.2, T["predator"] + 0.15, (146.8, 220.0, 233.1), 0.021,
        attack=0.9, release=0.6)
    tone(T["arrival"] + 0.7, T["predator"], 174.6, 0.017, attack=1.3, release=0.5, vib=0.004)
    # ★ 천적입니다 - 여기서 화음을 끊고 아주 낮은 드론만 남긴다(정적이 대비를 만든다)
    tone(T["predator"], T["slaughter"] + 0.1, 73.4, 0.034, attack=0.25, release=0.5)
    # 학살 - 저역 펄스만
    pulse_low(T["slaughter"], T["attack"] + 0.2, 55.0, 2.2, 0.13)
    # 넘어진 카메라 - 높고 아주 조용한 화음(거의 정적)
    pad(T["fallen"] + 0.3, T["outro"] + 0.4, (261.6, 392.0, 659.3), 0.010,
        attack=1.4, release=1.4)

    print("  효과음...", flush=True)
    add("Enemy_Hit_A.wav", 0.55, 0.34)                 # 코앞의 개체가 내는 소리
    add("Enemy_Hit_C.ogg", 1.90, 0.24)
    sub_drop(T["title"], 0.70, 104.0, 46.0, 0.40)      # 타이틀이 붙는 순간
    add("Enemy_Hit_A.wav", T["herd"] + 0.9, 0.15)
    add("Enemy_Hit_B.wav", T["herd"] + 2.1, 0.13)
    sub_drop(T["arrival"] + 0.70, 1.10, 92.0, 42.0, 0.44)   # 지평선에 뭔가 나타난다
    sub_drop(T["predator"], 0.90, 120.0, 40.0, 0.52)        # 얼굴 클로즈업 하드컷

    # ★ 폭발음과 개체 수 표기음은 화면 폭발 시각(wild_scenes.KILLS)을 그대로 읽어와 붙인다
    import wild_scenes as S
    p0 = T["slaughter"]
    rapid("Weapon_RapidFire.wav", p0 + 0.10, p0 + S.CUT_B - 0.05, 0.115, 0.30)
    rapid("Weapon_RapidFire.wav", p0 + S.CUT_B, p0 + S.CUT_C - 0.05, 0.095, 0.42, seed=21)
    rapid("Weapon_RapidFire.wav", p0 + S.CUT_C, p0 + 6.05, 0.115, 0.32, seed=33)
    for (i, z) in enumerate(S.KILLS):
        td = p0 + z["death"]
        near = (z["cut"] == "b")
        add("Enemy_Death.wav", td, 0.42 if near else 0.26)
        if near or i % 3 == 0:
            add("Weapon_Explosive.wav", td, 0.46 if near else 0.30)
        if i % 5 == 2:
            add("Enemy_DisruptorExplode.wav", td + 0.04, 0.24)
        blip(td + 0.02, 1180.0, 0.055, 0.085)          # 개체 수 표기가 한 칸 떨어지는 소리

    # 촬영진 차례 - 다가오는 발사음, 그리고 한 방
    a0 = T["attack"]
    rapid("Weapon_RapidFire.wav", a0 + 0.10, a0 + S.FLASH_AT - 0.05, 0.105, 0.34, seed=51)
    add("Weapon_Explosive.wav", a0 + S.FLASH_AT, 0.85)
    sub_drop(a0 + S.FLASH_AT, 1.30, 128.0, 34.0, 0.58)
    add("Boss_Hit_A.wav", a0 + S.FLASH_AT + 0.30, 0.55)     # 카메라가 바닥에 떨어진다
    add("Player_Hit.wav", a0 + S.FLASH_AT + 0.46, 0.30)

    # 엔딩 - 정적 뒤에 종소리 하나(마지막 한 줄이 뜨는 순간)
    bell(T["outro"] + 1.30, 784.0, 2.4, 0.16)

    print("  기록...", flush=True)
    out = array.array("h", bytes(2 * N))
    peak = 0.0
    for i in range(N):
        v = abs(buf[i])
        if v > peak:
            peak = v
    k = 0.92 / peak if peak > 0.92 else 1.0
    for i in range(N):
        v = int(buf[i] * k * 32767)
        out[i] = -32768 if v < -32768 else (32767 if v > 32767 else v)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(out.tobytes())
    print("  최대치 %.2f (보정 x%.2f)" % (peak, k), flush=True)
    return path


def build(out_path):
    bed = build_bed(os.path.join(CACHE, "wild_bed.wav"))
    subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", bed,
                    "-af", ("acompressor=threshold=0.20:ratio=3:attack=12:release=220,"
                            "volume=1.35,alimiter=limit=0.96,"
                            "afade=t=in:st=0:d=0.5,afade=t=out:st=%.2f:d=0.55" % (DUR - 0.55)),
                    "-c:a", "aac", "-b:a", "192k", "-ar", str(SR), "-ac", "1",
                    out_path], check=True)
    return out_path


if __name__ == "__main__":
    dest = sys.argv[1] if len(sys.argv) > 1 else os.path.join(CACHE, "wild_audio.m4a")
    print(build(dest))
