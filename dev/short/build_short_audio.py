# -*- coding: utf-8 -*-
"""컴스톡 쇼츠 「Dam Dididi」 오디오 - 게임 BGM/효과음만으로 9초 트랙을 만든다.

★ 레퍼런스의 노래("Dam Dididi")는 저작물이라 쓰지 않는다. 대신 게임 안에 이미 있는
  BGM과 효과음만으로 같은 박자(111.1 BPM)를 만든다. 편집기에서 원곡을 얹고 싶으면
  무음본(`*_silent.mp4`)에 오디오만 갈아 끼우면 된다.

구성
  - 바탕: Game_BGM02 를 낮게 깔고 앞뒤로 페이드
  - 박자: 매 박(0.54초)마다 UI_Click. 구간 첫 박은 조금 세게
  - 품목: 구간이 시작될 때 등장음
  - 판정: O 는 LevelUp, X 는 Enemy_Death
"""
import array
import os
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from short_common import RES, CACHE, DUR, T0, SEG_LEN, BEAT, SEG_MARK, SEGMENTS

SR = 48000
N = int(SR * DUR)
SFX = os.path.join(RES, "SFX")
MUS = os.path.join(RES, "Musics")
BGM = "Game_BGM02.mp3"
BGM_FROM = 12.0          # BGM에서 잘라 쓸 시작 지점(도입부의 조용한 구간을 피한다)

_cache = {}


def _decode(path):
    """어떤 포맷이든 48kHz 모노 16bit로 디코딩해 샘플 배열로 돌려준다."""
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


def sfx(name):
    if name not in _cache:
        _cache[name] = _decode(os.path.join(SFX, name))
    return _cache[name]


def add(buf, name, t, gain=1.0):
    src = sfx(name)
    off = int(t * SR)
    if off < 0:
        return
    end = min(N, off + len(src))
    for i in range(off, end):
        buf[i] += int(src[i - off] * gain)


def build(out_path):
    buf = array.array("i", [0]) * N

    # ---- 바탕 BGM
    bgm = _decode(os.path.join(MUS, BGM))
    off = int(BGM_FROM * SR)
    fade = int(0.35 * SR)
    for i in range(N):
        j = off + i
        if j >= len(bgm):
            break
        g = 0.30
        if i < fade:
            g *= i / fade
        if i > N - fade:
            g *= (N - i) / fade
        buf[i] += int(bgm[j] * g)

    # ---- 박자: 구간마다 4박씩, 첫 박을 세게
    for i in range(len(SEGMENTS)):
        t0 = T0 + i * SEG_LEN
        for b in range(4):
            add(buf, "UI_Click.wav", t0 + b * BEAT, 0.85 if b == 0 else 0.42)

    # ---- 구간별 등장음 / 판정음
    for i, (key, ok) in enumerate(SEGMENTS):
        t0 = T0 + i * SEG_LEN
        add(buf, "Weapon_Melee.wav", t0 + 0.02, 0.45)                 # 품목이 솟아오르는 소리
        add(buf, "LevelUp.wav" if ok else "Enemy_Death.wav",
            t0 + SEG_MARK, 1.0 if ok else 0.9)                        # 판정 도장

    # ---- 클리핑 방지 후 16bit로
    peak = max(1, max(abs(v) for v in buf))
    k = min(1.0, 30000.0 / peak)
    pcm = array.array("h", [int(v * k) for v in buf])

    raw_wav = os.path.join(CACHE, "short_mix.wav")
    os.makedirs(CACHE, exist_ok=True)
    with wave.open(raw_wav, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())

    subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                    "-i", raw_wav, "-c:a", "aac", "-b:a", "192k", out_path], check=True)
    print(f"오디오: {out_path}  (피크 정규화 계수 {k:.3f})")
    return out_path


if __name__ == "__main__":
    build(os.path.join(os.path.dirname(os.path.abspath(__file__)), "short_audio.m4a"))
