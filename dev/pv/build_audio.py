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
        if 5.2 < t < 6.2:                  # 좀비가 좁혀올 때 (문제 제기 3.6~6.2)
            e += 0.10 * (t - 5.2)
        if 28.70 < t < 29.15:              # 보스가 터질 때
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

    # ---- 효과음 타임라인 (40초 광고 구성에 맞춘다)
    add(buf, "UI_Click.wav", 0.02, 1.0)          # TV 켜기

    # 스튜디오 로고 카드 (0.8~3.6)
    add(buf, "LevelUp.wav", 0.86, 0.55)          # 로고가 팡 하고 뜬다
    add(buf, "UI_Click.wav", 1.22, 0.35)         # "제공" 자막
    add(buf, "UI_Click.wav", 2.00, 0.28)         # 하단 장식선

    # 문제 제기: 좀비가 좁혀온다 (3.6~6.2)
    for i, t in enumerate((3.80, 4.15, 4.50, 4.85, 5.15, 5.45, 5.72, 5.95)):
        add(buf, ("Enemy_Hit_A.wav", "Enemy_Hit_B.wav", "Enemy_Hit_C.ogg")[i % 3],
            t, 0.28 + i * 0.022)
    add(buf, "Player_Hit.wav", 6.05, 0.55)
    add(buf, "Player_Hit.wav", 6.26, 0.9)        # "더 좋은 방법이 있습니다!"

    # 제품 등장 (7.9~10.8)
    add(buf, "LevelUp.wav", 7.96, 0.8)
    add(buf, "Weapon_Explosive.wav", 8.52, 0.95)  # 로고가 쾅 (t=0.60 슬램)
    add(buf, "UI_Click.wav", 8.98, 0.6)           # NEW 배지

    # 사용법 3단계 (10.8~15.6, 단계당 1.6초)
    add(buf, "UI_Click.wav", 10.86, 0.8)
    add(buf, "Enemy_Hit_B.wav", 11.48, 0.5)       # 1단계: 좀비 발견
    add(buf, "UI_Click.wav", 12.46, 0.8)
    add(buf, "Weapon_Melee.wav", 12.80, 0.8)      # 2단계: 총 장착
    add(buf, "UI_Click.wav", 14.06, 0.9)
    rapid(buf, "Weapon_RapidFire.wav", 14.08, 15.60, 0.105, 0.44)   # 3단계
    for t in (14.30, 14.80, 15.30):
        add(buf, "Weapon_Explosive.wav", t, 0.6)
    for t in (14.52, 15.05):
        add(buf, "Enemy_Death.wav", t, 0.55)
    add(buf, "Weapon_Explosive.wav", 15.62, 0.95)  # "잠깐! 이게 끝이 아닙니다!"

    # 사은품 (17.1~21.1)
    for t in (17.45, 17.88, 18.31, 18.74):        # 파츠 4개
        add(buf, "UI_Click.wav", t, 0.75)
    for t in (19.15, 19.51):                      # 추가 무기 2정
        add(buf, "UI_Click.wav", t, 0.75)
    add(buf, "LevelUp.wav", 19.96, 0.7)           # 무료! 배지
    add(buf, "UI_Click.wav", 20.55, 0.6)

    # 사용 전 / 사용 후 (21.1~24.0)
    add(buf, "UI_Click.wav", 21.12, 0.8)
    add(buf, "Weapon_PlasmaCannon.wav", 21.70, 0.45)   # 와이프
    rapid(buf, "Weapon_RapidFire.wav", 21.85, 23.85, 0.155, 0.27)
    for t in (22.10, 22.72, 23.32):
        add(buf, "Weapon_Explosive.wav", t, 0.5)

    # 고객 후기 (24.0~26.7)
    add(buf, "Enemy_Hit_A.wav", 24.32, 0.55)
    add(buf, "LevelUp.wav", 24.88, 0.45)          # 별점
    add(buf, "Enemy_Hit_C.ogg", 25.42, 0.4)

    # 산업용 강도 시연 (26.7~29.8)
    add(buf, "Boss_Hit_A.wav", 26.80, 0.9)
    rapid(buf, "Weapon_RapidFire.wav", 26.95, 28.62, 0.13, 0.34)
    add(buf, "Boss_Hit_B.wav", 27.52, 0.8)
    add(buf, "Boss_Hit_C.wav", 28.10, 0.8)
    add(buf, "Boss_Death.wav", 28.72, 1.0)
    add(buf, "Weapon_Explosive.wav", 28.82, 0.9)

    # 가격 (29.8~32.7)
    add(buf, "LevelUp.wav", 29.86, 0.85)
    add(buf, "UI_Click.wav", 30.42, 0.8)          # 정가에 쫙 (t=0.62)
    add(buf, "Weapon_Explosive.wav", 31.32, 0.95)  # 0원 도장 (t=1.52)
    add(buf, "UI_Click.wav", 31.78, 0.7)
    add(buf, "UI_Click.wav", 31.90, 0.7)

    # 지금 바로! (32.7~35.4) - 웨이브 카운터가 점점 빨리 넘어간다
    add(buf, "Weapon_Explosive.wav", 32.72, 0.7)
    t, step = 32.85, 0.24
    for _ in range(19):                            # 20웨이브까지 19번
        if t > 35.30:
            break
        add(buf, "UI_Click.wav", t, 0.75)
        step = max(0.050, step * 0.885)
        t += step

    # 마무리 (35.4~39.0)
    add(buf, "LevelUp.wav", 35.46, 0.9)
    add(buf, "Weapon_Explosive.wav", 35.82, 0.75)  # 로고 슬램 (t=0.40)
    add(buf, "UI_Click.wav", 39.04, 1.0)           # TV 끄기

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
    ("Title_BGM.mp3", 8.0, 0.55, 7.35, 0.40),     # TV 켜짐 + 로고 카드 + 문제 제기
    ("Game_BGM01.mp3", 12.0, 7.90, 13.20, 0.42),  # 제품 등장 ~ 사은품
    ("Game_BGM02.mp3", 6.0, 21.10, 5.60, 0.46),   # 사용 전/후 + 후기
    ("Boss_BGM.wav", 10.0, 26.70, 3.10, 0.52),    # 산업용 강도 시연
    ("Game_BGM02.mp3", 20.0, 29.80, 5.60, 0.48),  # 가격 + 지금 바로
    ("Title_BGM.mp3", 30.0, 35.40, 4.60, 0.46),   # 마무리
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
