# -*- coding: utf-8 -*-
"""외부 음원의 템포·첫 박을 재서 릴스 히트 격자에 맞출 값을 뽑는다.

    python3 analyze_audio.py <음원파일>

**왜 필요한가**: 영상의 얼굴 튐은 `reel_common`의 `BPM`/`HITS` 격자에 묶여 있다.
외부 음원을 그냥 얹으면 그 곡의 실제 템포·첫 박과 어긋나서 로봇이 박자 밖에서 튄다.
여기서 잰 `BPM`과 `AUDIO_OFFSET`을 `reel_common`에 넣어야 비로소 맞는다.

방법: 스펙트럼 플럭스로 온셋 포락선을 만들고 → 자기상관으로 박 간격을 찾고 →
펄스 열을 밀어 가며 맞춰 첫 박 위치를 찾는다.
"""
import sys
import wave

import numpy as np

SR = 48000
HOP = 256                      # 5.33ms - 8분음표를 구분하기에 충분하다
NFFT = 1024


def load(path):
    with wave.open(path, "rb") as w:
        assert w.getframerate() == SR and w.getnchannels() == 1, "48kHz 모노로 디코딩해서 넣을 것"
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
    return x


def onset_envelope(x):
    """스펙트럼 플럭스 - 프레임마다 '새로 커진 주파수 성분'의 합."""
    win = np.hanning(NFFT)
    nfr = 1 + (len(x) - NFFT) // HOP
    frames = np.lib.stride_tricks.as_strided(
        x, shape=(nfr, NFFT), strides=(x.strides[0] * HOP, x.strides[0])) * win
    mag = np.abs(np.fft.rfft(frames, axis=1))
    logmag = np.log1p(mag * 200.0)
    flux = np.diff(logmag, axis=0)
    env = np.maximum(flux, 0).sum(axis=1)
    # 느린 흐름을 빼서 0 기준으로 만든다(곡 전체 음량 변화에 안 휘둘리게).
    k = 31
    base = np.convolve(env, np.ones(k) / k, mode="same")
    env = np.maximum(env - base, 0)
    return env / (env.max() or 1.0)


def estimate_bpm(env, lo=70.0, hi=190.0):
    """자기상관으로 박 간격을 찾는다. 배/반 템포는 흔한 함정이라 같이 본다."""
    e = env - env.mean()
    ac = np.correlate(e, e, mode="full")[len(e) - 1:]
    ac /= (ac[0] or 1.0)
    fps = SR / HOP
    best = None
    for bpm in np.arange(lo, hi, 0.05):
        lag = 60.0 / bpm * fps
        i = int(round(lag))
        if i < 2 or i >= len(ac):
            continue
        # 그 박의 2·4배 지점도 같이 봐서 "진짜 박"을 고른다(반박에 걸리는 걸 막는다).
        score = ac[i] + 0.5 * ac[min(len(ac) - 1, i * 2)] + 0.25 * ac[min(len(ac) - 1, i * 4)]
        if best is None or score > best[1]:
            best = (float(bpm), float(score))
    return best


def estimate_offset(env, bpm, nbeats=64):
    """펄스 열을 밀어 가며 온셋 포락선과 가장 잘 맞는 첫 박 위치를 찾는다."""
    fps = SR / HOP
    period = 60.0 / bpm * fps
    best = None
    for off in np.arange(0.0, period, 0.25):
        idx = (off + period * np.arange(nbeats)).astype(int)
        idx = idx[idx < len(env)]
        if len(idx) < 8:
            continue
        score = float(env[idx].mean())
        if best is None or score > best[1]:
            best = (float(off / fps), score)
    return best


def main():
    path = sys.argv[1]
    x = load(path)
    dur = len(x) / SR
    env = onset_envelope(x)
    bpm, sc = estimate_bpm(env)
    off, osc = estimate_offset(env, bpm)
    beat = 60.0 / bpm
    print("길이            : %.3f초" % dur)
    print("추정 BPM        : %.2f  (자기상관 점수 %.3f)" % (bpm, sc))
    print("한 박           : %.4f초 / 한 마디(4박) %.4f초" % (beat, beat * 4))
    print("첫 박 오프셋    : %.4f초  (점수 %.3f)" % (off, osc))
    for bars in range(4, 13):
        end = off + beat * 4 * bars
        mark = "  <= 음원 안에 들어감" if end <= dur + 0.02 else ""
        print("   %2d마디 = %6.3f초 (끝 %6.3f)%s" % (bars, beat * 4 * bars, end, mark))


if __name__ == "__main__":
    main()
