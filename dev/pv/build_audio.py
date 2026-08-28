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
        if 6.2 < t < 7.2:                  # 좀비가 좁혀올 때 (문제 제기 4.6~7.2)
            e += 0.10 * (t - 6.2)
        if 29.70 < t < 30.15:              # 보스가 터질 때
            e += 0.16

        # ★ 로고 영상 구간(0~3.6)은 완전 무음이다. 3.6에서 로고가 사라질 때
        #   '지직' 한 번만 짧게 튄다.
        #   감쇠를 길게 끌면(1초) 천둥 우르릉 소리가 되므로 시정수를 0.07초로 짧게 잡고,
        #   대신 잔여 크래클 틱을 몇 번 흩뿌려 '치지직' 질감을 만든다.
        #   0.5초쯤 뒤에는 완전히 조용해지고 본편(4.6)이 정적에서 시작한다.
        if t < 3.58:
            e = 0.0
        elif t < 4.6:
            dt = t - 3.58
            e = 0.52 * math.exp(-dt / 0.07)
            for (ct, ca, cw) in ((0.13, 0.22, 0.020), (0.24, 0.15, 0.017),
                                 (0.37, 0.10, 0.015)):
                e += ca * math.exp(-((dt - ct) / cw) ** 2)
        env_tab.append(e)

    rng = random.Random(4242)
    prev = 0.0
    uni = rng.random
    for i in range(N):
        env = env_tab[i // STEP]
        w = uni() * 2 - 1
        # 1차 미분 = 고역 강조. 저역을 남기면 '지직'이 아니라 '우르릉'이 된다.
        hp = w - prev
        prev = w
        buf[i] = int(26000 * env * (w * 0.30 + hp * 0.42))

    # ---- 효과음 타임라인 (41초 / 로고 구간은 무음)
    # 0.0~3.6 로고 영상: 효과음 없음. TV 켜는 소리도 넣지 않는다.
    # 3.6 로고가 사라지며 지직(위 포락선) -> 4.6부터 본편.

    # 문제 제기: 좀비가 좁혀온다 (4.6~7.2)
    for i, t in enumerate((4.80, 5.15, 5.50, 5.85, 6.15, 6.45, 6.72, 6.95)):
        add(buf, ("Enemy_Hit_A.wav", "Enemy_Hit_B.wav", "Enemy_Hit_C.ogg")[i % 3],
            t, 0.28 + i * 0.022)
    add(buf, "Player_Hit.wav", 7.05, 0.55)
    add(buf, "Player_Hit.wav", 7.26, 0.9)        # "더 좋은 방법이 있습니다!"

    # 제품 등장 (8.9~11.8)
    add(buf, "LevelUp.wav", 8.96, 0.8)
    add(buf, "Weapon_Explosive.wav", 9.52, 0.95)  # 로고가 쾅 (t=0.60 슬램)
    add(buf, "UI_Click.wav", 9.98, 0.6)           # NEW 배지

    # 사용법 3단계 (11.8~16.6, 단계당 1.6초)
    add(buf, "UI_Click.wav", 11.86, 0.8)
    add(buf, "Enemy_Hit_B.wav", 12.48, 0.5)       # 1단계: 좀비 발견
    add(buf, "UI_Click.wav", 13.46, 0.8)
    add(buf, "Weapon_Melee.wav", 13.80, 0.8)      # 2단계: 총 장착
    add(buf, "UI_Click.wav", 15.06, 0.9)
    rapid(buf, "Weapon_RapidFire.wav", 15.08, 16.60, 0.105, 0.44)   # 3단계
    for t in (15.30, 15.80, 16.30):
        add(buf, "Weapon_Explosive.wav", t, 0.6)
    for t in (15.52, 16.05):
        add(buf, "Enemy_Death.wav", t, 0.55)
    add(buf, "Weapon_Explosive.wav", 16.62, 0.95)  # "잠깐! 이게 끝이 아닙니다!"

    # 사은품 (18.1~22.1)
    for t in (18.45, 18.88, 19.31, 19.74):        # 파츠 4개
        add(buf, "UI_Click.wav", t, 0.75)
    for t in (20.15, 20.51):                      # 추가 무기 2정
        add(buf, "UI_Click.wav", t, 0.75)
    add(buf, "LevelUp.wav", 20.96, 0.7)           # 무료! 배지
    add(buf, "UI_Click.wav", 21.55, 0.6)

    # 사용 전 / 사용 후 (22.1~25.0)
    add(buf, "UI_Click.wav", 22.12, 0.8)
    add(buf, "Weapon_PlasmaCannon.wav", 22.70, 0.45)   # 와이프
    rapid(buf, "Weapon_RapidFire.wav", 22.85, 24.85, 0.155, 0.27)
    for t in (23.10, 23.72, 24.32):
        add(buf, "Weapon_Explosive.wav", t, 0.5)

    # 고객 후기 (25.0~27.7)
    add(buf, "Enemy_Hit_A.wav", 25.32, 0.55)
    add(buf, "LevelUp.wav", 25.88, 0.45)          # 별점
    add(buf, "Enemy_Hit_C.ogg", 26.42, 0.4)

    # 산업용 강도 시연 (27.7~30.8)
    add(buf, "Boss_Hit_A.wav", 27.80, 0.9)
    rapid(buf, "Weapon_RapidFire.wav", 27.95, 29.62, 0.13, 0.34)
    add(buf, "Boss_Hit_B.wav", 28.52, 0.8)
    add(buf, "Boss_Hit_C.wav", 29.10, 0.8)
    add(buf, "Boss_Death.wav", 29.72, 1.0)
    add(buf, "Weapon_Explosive.wav", 29.82, 0.9)

    # 가격 (30.8~33.7)
    add(buf, "LevelUp.wav", 30.86, 0.85)
    add(buf, "UI_Click.wav", 31.42, 0.8)          # 정가에 쫙 (t=0.62)
    add(buf, "Weapon_Explosive.wav", 32.32, 0.95)  # 0원 도장 (t=1.52)
    add(buf, "UI_Click.wav", 32.78, 0.7)
    add(buf, "UI_Click.wav", 32.90, 0.7)

    # 지금 바로! (33.7~36.4) - 웨이브 카운터가 점점 빨리 넘어간다
    add(buf, "Weapon_Explosive.wav", 33.72, 0.7)
    t, step = 33.85, 0.24
    for _ in range(19):                            # 20웨이브까지 19번
        if t > 36.30:
            break
        add(buf, "UI_Click.wav", t, 0.75)
        step = max(0.050, step * 0.885)
        t += step

    # 마무리 (36.4~40.0)
    add(buf, "LevelUp.wav", 36.46, 0.9)
    add(buf, "Weapon_Explosive.wav", 36.82, 0.75)  # 로고 슬램 (t=0.40)
    add(buf, "UI_Click.wav", 40.04, 1.0)           # TV 끄기

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
    # 0.0~4.6(로고 + 지직 + 정적)은 음악을 깔지 않는다. 본편부터 들어온다.
    ("Title_BGM.mp3", 8.0, 4.60, 4.30, 0.40),     # 문제 제기
    ("Game_BGM01.mp3", 12.0, 8.90, 13.20, 0.42),  # 제품 등장 ~ 사은품
    ("Game_BGM02.mp3", 6.0, 22.10, 5.60, 0.46),   # 사용 전/후 + 후기
    ("Boss_BGM.wav", 10.0, 27.70, 3.10, 0.52),    # 산업용 강도 시연
    ("Game_BGM02.mp3", 20.0, 30.80, 5.60, 0.48),  # 가격 + 지금 바로
    ("Title_BGM.mp3", 30.0, 36.40, 4.60, 0.46),   # 마무리
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
