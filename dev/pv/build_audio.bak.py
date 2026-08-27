# -*- coding: utf-8 -*-
"""컴스톡 PV 오디오 - 게임 BGM/효과음 + 브라운관 잡음을 30초에 맞춰 믹싱한다.

1) 효과음을 48kHz 모노로 디코딩해 파이썬에서 타임라인에 얹는다(정전기 잡음 포함).
2) BGM 4구간은 ffmpeg에서 잘라 붙이고, 마지막에 작은 TV 스피커처럼 필터를 건다.
"""
import array
import math
import os
import random
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv_common import RES, CACHE, DUR, CUTS

SR = 48000
N = int(SR * DUR)
SFX = os.path.join(RES, "SFX")
MUS = os.path.join(RES, "Musics")


def _decode(path):
    """어떤 포맷이든 48kHz 모노 16bit로 디코딩해 샘플 리스트로 돌려준다."""
    os.makedirs(CACHE, exist_ok=True)
    out = os.path.join(CACHE, "a_" + os.path.basename(path) + ".wav")
    if not os.path.exists(out):
        subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                        "-i", path, "-ac", "1", "-ar", str(SR), "-c:a", "pcm_s16le", out],
                       check=True)
    with wave.open(out, "rb") as w:
        raw = w.readframes(w.getnframes())
    a = array.array("h")
    a.frombytes(raw)
    return a


_cache = {}


def sfx(name):
    if name not in _cache:
        p = os.path.join(SFX, name)
        _cache[name] = _decode(p)
    return _cache[name]


def add(buf, name, t, gain=1.0):
    src = sfx(name)
    off = int(t * SR)
    if off < 0:
        return
    end = min(N, off + len(src))
    for i in range(off, end):
        buf[i] += int(src[i - off] * gain)


def rapid(buf, name, t0, t1, step, gain, jitter=0.008):
    rng = random.Random(int(t0 * 1000))
    t = t0
    while t < t1:
        add(buf, name, t + rng.uniform(-jitter, jitter), gain * rng.uniform(0.8, 1.15))
        t += step


def build_sfx_bed(path):
    buf = array.array("i", [0]) * N

    # ---- 정전기: 장면 전환마다 확 튀고, 평소에도 옅게 깔린다
    # 포락선은 128샘플 간격으로만 계산한다(샘플마다 exp를 부르면 24M번이 된다).
    STEP = 128
    env_tab = []
    for k in range(N // STEP + 2):
        t = k * STEP / SR
        e = 0.020
        for c in CUTS:
            dt = (t - c) / 0.085
            if -4 < dt < 4:
                e += 0.34 * math.exp(-dt * dt)
        if t < 0.62:                       # TV를 켤 때
            e += 0.30 * (1 - t / 0.62)
        if 8.5 < t < 9.4:                  # 좀비가 몰려올 때
            e += 0.10 * (t - 8.5)
        if 23.2 < t < 23.9:                # 보스가 터질 때
            e += 0.16
        env_tab.append(e)

    rng = random.Random(4242)
    lp = 0.0
    uni = rng.random
    for i in range(N):
        env = env_tab[i // STEP]
        w = uni() * 2 - 1
        lp = lp * 0.55 + w * 0.45          # 살짝 눌러 '치직' 소리에 가깝게
        buf[i] = int(32767 * env * (w * 0.6 + lp * 0.5))

    # ---- 효과음 타임라인
    add(buf, "UI_Click.wav", 0.02, 1.0)
    add(buf, "UI_Click.wav", 0.95, 0.6)
    add(buf, "UI_Click.wav", 3.02, 0.4)

    add(buf, "Enemy_Hit_B.wav", 4.45, 0.35)
    add(buf, "Player_Hit.wav", 5.32, 0.9)
    add(buf, "UI_Click.wav", 5.62, 0.5)

    for i, t in enumerate((7.1, 7.5, 7.85, 8.15, 8.45, 8.7, 8.95, 9.15)):
        add(buf, ("Enemy_Hit_A.wav", "Enemy_Hit_B.wav", "Enemy_Hit_C.ogg")[i % 3],
            t, 0.30 + i * 0.02)
    add(buf, "LevelUp.wav", 9.44, 0.7)

    for i in range(8):                     # 총이 하나씩 붙는 소리
        add(buf, "UI_Click.wav", 10.92 + i * 0.16, 0.85)

    add(buf, "Weapon_PlasmaCannon.wav", 12.42, 0.5)
    rapid(buf, "Weapon_RapidFire.wav", 12.40, 15.00, 0.115, 0.42)
    for t in (12.72, 13.30, 13.92, 14.52):
        add(buf, "Weapon_Explosive.wav", t, 0.6)
    for t in (12.95, 13.55, 14.15, 14.75):
        add(buf, "Enemy_Death.wav", t, 0.55)
    add(buf, "Weapon_Explosive.wav", 15.02, 0.85)

    for i, t in enumerate((16.36, 16.58, 16.88, 17.18, 17.48, 18.02)):
        add(buf, "UI_Click.wav", t, 0.7)
    add(buf, "LevelUp.wav", 17.62, 0.6)

    t, step = 18.55, 0.22                  # 웨이브 카운터가 점점 빨리 넘어간다
    for _ in range(19):                    # 20웨이브까지 19번 넘어간다
        if t > 20.42:
            break
        add(buf, "UI_Click.wav", t, 0.75)
        step = max(0.048, step * 0.88)
        t += step
    add(buf, "Boss_Hit_A.wav", 20.54, 0.8)

    add(buf, "Boss_Hit_A.wav", 21.78, 0.9)
    add(buf, "Weapon_PlasmaCannon.wav", 21.92, 0.7)
    rapid(buf, "Weapon_RapidFire.wav", 22.00, 23.90, 0.135, 0.34)
    add(buf, "Boss_Hit_B.wav", 22.62, 0.8)
    add(buf, "Boss_Hit_C.wav", 23.14, 0.8)
    add(buf, "Boss_Death.wav", 23.92, 1.0)
    add(buf, "Weapon_Explosive.wav", 24.02, 0.9)

    add(buf, "LevelUp.wav", 25.02, 0.9)
    add(buf, "Weapon_Explosive.wav", 25.06, 0.6)
    add(buf, "UI_Click.wav", 27.62, 0.5)
    add(buf, "UI_Click.wav", 29.04, 1.0)

    out = array.array("h", [0]) * N
    for i in range(N):
        v = buf[i]
        out[i] = -32768 if v < -32768 else (32767 if v > 32767 else v)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(out.tobytes())
    return path


# (파일, 시작오프셋, PV상 시작, 길이, 음량)
MUSIC = [
    ("Title_BGM.mp3", 8.0, 0.55, 2.55, 0.42),
    ("Game_BGM01.mp3", 12.0, 3.00, 12.05, 0.42),
    ("Game_BGM02.mp3", 6.0, 15.00, 5.55, 0.46),
    ("Boss_BGM.wav", 10.0, 20.50, 4.55, 0.50),
    ("Title_BGM.mp3", 30.0, 25.00, 5.00, 0.46),
]


def build(out_path):
    bed = build_sfx_bed(os.path.join(CACHE, "sfx_bed.wav"))
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error"]
    for (f, off, _t, dur, _v) in MUSIC:
        cmd += ["-ss", str(off), "-t", str(dur + 0.4), "-i", os.path.join(MUS, f)]
    cmd += ["-i", bed]

    parts = []
    for i, (_f, _off, t, dur, vol) in enumerate(MUSIC):
        parts.append(
            "[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
            "atrim=0:%.3f,afade=t=in:st=0:d=0.18,afade=t=out:st=%.3f:d=0.22,"
            "volume=%.3f,adelay=%d[m%d]"
            % (i, dur, max(0.0, dur - 0.22), vol, int(t * 1000), i))
    n = len(MUSIC)
    parts.append("[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
                 "volume=1.0[sx]" % n)
    mixin = "".join("[m%d]" % i for i in range(n)) + "[sx]"
    parts.append("%samix=inputs=%d:normalize=0:dropout_transition=0[mx]" % (mixin, n + 1))
    # 작은 브라운관 스피커 흉내: 저음/고음을 깎고 눌러 붙인다
    parts.append("[mx]highpass=f=290,lowpass=f=3500,acompressor=threshold=0.10:ratio=6:"
                 "attack=6:release=140:makeup=2,volume=1.25,alimiter=limit=0.94,"
                 "atrim=0:%.2f,afade=t=out:st=%.2f:d=0.35[out]" % (DUR, DUR - 0.35))

    cmd += ["-filter_complex", ";".join(parts), "-map", "[out]",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "1", out_path]
    subprocess.run(cmd, check=True)
    return out_path


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(CACHE, "pv_audio.m4a")
    print(build(out))
