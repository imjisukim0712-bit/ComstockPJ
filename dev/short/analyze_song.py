# -*- coding: utf-8 -*-
"""원곡에서 박자 격자를 실측한다 - `short_common`의 BEAT / T0 / SEG_LEN 근거.

    python analyze_song.py [음원경로]

하는 일
1. 음원을 22.05kHz 모노로 디코딩한다
2. **스펙트럼 플럭스**(직전 프레임보다 밝아진 주파수 성분의 합) 온셋 포락선을 만든다
3. 자기상관으로 대략의 박자 주기를 잡고
4. **16박 격자(주기 + 위상)** 를 곡 전체에 맞춰 완전탐색해서 확정한다

★ 3번(자기상관)만으로는 위상을 모른다. "박자가 0.54초"인 것과 "첫 박이 0.11초"인 것은
  다른 정보이고, 도장이 찍히는 순간을 정하는 것은 **위상** 쪽이다. 그래서 4번이 필요하다.

★ 왜 16박인가: 구성이 4구간 x 4박이고 곡 전체가 딱 그 길이다(0.110 + 16 x 0.544 = 8.81초,
  곡 8.73초). 격자를 곡 전체에 걸면 국소 잡음에 흔들리지 않는다.
"""
import os
import subprocess
import sys
import wave

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from short_common import SONG, CACHE

SR = 22050
HOP = 256
WIN = 1024


def envelope(path):
    """온셋 포락선 (시각 배열, 세기 배열)을 돌려준다."""
    os.makedirs(CACHE, exist_ok=True)
    wav = os.path.join(CACHE, "song_mono.wav")
    subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error", "-i", path,
                    "-ac", "1", "-ar", str(SR), "-c:a", "pcm_s16le", wav], check=True)
    with wave.open(wav, "rb") as w:
        a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768.0

    n = 1 + (len(a) - WIN) // HOP
    win = np.hanning(WIN)
    idx = np.arange(WIN)[None, :] + HOP * np.arange(n)[:, None]
    S = np.log1p(np.abs(np.fft.rfft(a[idx] * win, axis=1)) * 20)
    flux = np.maximum(0, np.diff(S, axis=0)).sum(axis=1)
    flux /= flux.max() + 1e-9
    t = (np.arange(len(flux)) + 1) * HOP / SR
    return t, flux, len(a) / SR


def grid_search(t, flux, dur, beats=16):
    """박자 주기와 위상을 함께 완전탐색한다."""
    def score(phase, beat):
        s = 0.0
        for k in range(beats):
            tt = phase + k * beat
            if tt < 0 or tt > t[-1]:
                continue
            i = int(np.argmin(np.abs(t - tt)))
            s += flux[max(0, i - 1):i + 2].max()      # 한 프레임(11.6ms) 오차 허용
        return s

    best = None
    for beat in np.arange(0.500, 0.580, 0.0005):
        for phase in np.arange(0.0, beat, 0.005):
            if phase + (beats - 1) * beat > dur:
                continue
            s = score(phase, beat)
            if best is None or s > best[0]:
                best = (s, float(beat), float(phase))
    return best


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else SONG
    if not os.path.exists(path):
        raise SystemExit(f"음원이 없다: {path}")

    t, flux, dur = envelope(path)
    print(f"음원 {os.path.basename(path)}  길이 {dur:.4f}초  ({dur * 30:.1f}프레임 @30fps)")

    s, beat, phase = grid_search(t, flux, dur)
    print(f"박자 {beat:.4f}초 = {60 / beat:.1f} BPM,  첫 박 {phase:.4f}초  (점수 {s:.2f})")
    print(f"구간(4박) 길이 {4 * beat:.4f}초")
    print("구간 시작: " + "  ".join(f"{phase + k * 4 * beat:.3f}" for k in range(4)))
    print("\nshort_common.py 에 넣을 값:")
    print(f"    BEAT = {beat:.4f}")
    print(f"    T0 = {phase:.4f}")
    print(f"    NF = {int(round(dur * 30))}")


if __name__ == "__main__":
    main()
