# -*- coding: utf-8 -*-
"""컴스톡 숏츠 광고 오디오 - 41초판의 "무음 -> 지직"(TV 정전기) 콘셉트를 쓰지 않는다.

컬러 밈 편집이라 CRT 잡음 대신 컷마다 효과음 한 방("펀치")과 BGM만으로 채운다.
"""
import array
import os
import random
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv_common import RES, CACHE, SHORTS_DUR

SR = 48000
DUR = SHORTS_DUR
N = int(SR * DUR)
SFX = os.path.join(RES, "SFX")
MUS = os.path.join(RES, "Musics")


def _decode(path):
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

    # 밈 셋업 (0.0~3.5) - 좀비가 다가오다가, 컷 -> 로봇 총 4정이 팝팝팝 붙는다
    add(buf, "Enemy_Hit_A.wav", 0.30, 0.35)
    add(buf, "Enemy_Hit_B.wav", 0.70, 0.40)
    add(buf, "Enemy_Hit_C.ogg", 1.05, 0.45)
    add(buf, "Weapon_Explosive.wav", 1.50, 0.9)          # 펀치 컷
    for t in (1.60, 1.72, 1.84, 1.96):
        add(buf, "UI_Click.wav", t, 0.7)
    add(buf, "LevelUp.wav", 2.10, 0.6)

    # 스펙시트 (3.5~7.5) - 항목이 툭툭 채워진다
    add(buf, "Weapon_Explosive.wav", 3.50, 0.8)           # 펀치 컷
    for t in (3.80, 4.35, 4.90, 5.45):
        add(buf, "UI_Click.wav", t, 0.75)
    add(buf, "LevelUp.wav", 5.90, 0.5)

    # 난사 개그 (7.5~11.0)
    add(buf, "Weapon_Explosive.wav", 7.50, 0.9)           # 펀치 컷
    rapid(buf, "Weapon_RapidFire.wav", 7.55, 10.90, 0.09, 0.42)
    for t in (7.90, 8.70, 9.50, 10.30):
        add(buf, "Weapon_Explosive.wav", t, 0.55)
    for t in (8.10, 9.00, 9.90):
        add(buf, "Enemy_Death.wav", t, 0.5)

    # 가격 + CTA (11.0~15.0)
    add(buf, "Weapon_Explosive.wav", 11.00, 0.9)          # 펀치 컷
    add(buf, "UI_Click.wav", 11.35, 0.8)                  # 취소선
    add(buf, "Weapon_Explosive.wav", 11.65, 0.95)         # FREE 도장
    add(buf, "LevelUp.wav", 12.15, 0.7)                   # 로고 등장
    add(buf, "UI_Click.wav", 12.55, 0.6)                  # CTA 문구

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
    ("Title_BGM.mp3", 8.0, 0.0, 3.5, 0.42),      # 밈 셋업
    ("Game_BGM01.mp3", 12.0, 3.5, 4.0, 0.44),    # 스펙시트
    ("Boss_BGM.wav", 10.0, 7.5, 3.5, 0.50),      # 난사 개그
    ("Title_BGM.mp3", 30.0, 11.0, 4.0, 0.46),    # 가격 + CTA
]


def build(out_path):
    bed = build_sfx_bed(os.path.join(CACHE, "sfx_bed_shorts.wav"))
    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error"]
    for (f, off, _t, dur, _v) in MUSIC:
        cmd += ["-ss", str(off), "-t", str(dur + 0.4), "-i", os.path.join(MUS, f)]
    cmd += ["-i", bed]

    parts = []
    for i, (_f, _off, t, dur, vol) in enumerate(MUSIC):
        parts.append(
            "[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
            "atrim=0:%.3f,afade=t=in:st=0:d=0.12,afade=t=out:st=%.3f:d=0.18,"
            "volume=%.3f,adelay=%d[m%d]"
            % (i, dur, max(0.0, dur - 0.18), vol, int(t * 1000), i))
    n = len(MUSIC)
    parts.append("[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
                 "volume=1.0[sx]" % n)
    mixin = "".join("[m%d]" % i for i in range(n)) + "[sx]"
    parts.append("%samix=inputs=%d:normalize=0:dropout_transition=0[mx]" % (mixin, n + 1))
    parts.append("[mx]acompressor=threshold=0.12:ratio=5:attack=6:release=140:makeup=1.6,"
                 "volume=1.15,alimiter=limit=0.95,"
                 "atrim=0:%.2f,afade=t=out:st=%.2f:d=0.25[out]" % (DUR, DUR - 0.25))

    cmd += ["-filter_complex", ";".join(parts), "-map", "[out]",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "1", out_path]
    subprocess.run(cmd, check=True)
    return out_path


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(CACHE, "shorts_audio.m4a")
    print(build(out))
