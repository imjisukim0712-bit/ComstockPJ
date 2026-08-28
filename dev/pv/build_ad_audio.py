# -*- coding: utf-8 -*-
"""컴스톡 광고 영상 공용 오디오 엔진 - 스펙 모듈의 audio(A) 콜백에 도구를 넘겨준다.

게임 효과음(Resources/SFX)과 직접 합성한 소리(삐 / 쿵 / 종 / 영사기 잡음)를 한 버퍼에
쌓고, 배경음은 ffmpeg 쪽에서 낮게 섞는다. 내레이션 음성은 쓰지 않으므로 영/한 공용이다.

사용법:  python build_ad_audio.py --spec safety --out a.m4a
"""
import argparse
import array
import importlib
import math
import os
import random
import subprocess
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pv_common import RES, CACHE

SR = 48000
SFXDIR = os.path.join(RES, "SFX")
MUSDIR = os.path.join(RES, "Musics")


class Api(object):
    def __init__(self, dur):
        self.dur = dur
        self.n = int(SR * dur)
        self.buf = array.array("d", bytes(8 * self.n))
        self.music_cues = []
        self._dec = {}

    # ---------------------------------------------------------- 게임 효과음
    def _decode(self, name):
        a = self._dec.get(name)
        if a is None:
            os.makedirs(CACHE, exist_ok=True)
            out = os.path.join(CACHE, "a_" + name + ".wav")
            if not os.path.exists(out):
                subprocess.run(["ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                                "-i", os.path.join(SFXDIR, name), "-ac", "1",
                                "-ar", str(SR), "-c:a", "pcm_s16le", out], check=True)
            with wave.open(out, "rb") as w:
                raw = w.readframes(w.getnframes())
            a = array.array("h")
            a.frombytes(raw)
            self._dec[name] = a
        return a

    def sfx(self, name, t, g=1.0):
        src = self._decode(name)
        off = int(t * SR)
        if off < 0:
            return
        end = min(self.n, off + len(src))
        for i in range(off, end):
            self.buf[i] += src[i - off] * g / 32768.0

    def rapid(self, name, t0, t1, step, g, jitter=0.006, seed=7):
        rng = random.Random(seed)
        t = t0
        while t < t1:
            self.sfx(name, t + rng.uniform(-jitter, jitter), g * rng.uniform(0.8, 1.15))
            t += step

    # ---------------------------------------------------------- 합성
    def tone(self, t0, t1, f, g, attack=0.05, release=0.1):
        i0, i1 = max(0, int(t0 * SR)), min(self.n, int(t1 * SR))
        if i1 <= i0:
            return
        ln = (i1 - i0) / SR
        ph = 0.0
        dt = 1.0 / SR
        for i in range(i0, i1):
            tt = (i - i0) * dt
            env = 1.0
            if tt < attack:
                env = tt / attack
            rem = ln - tt
            if rem < release:
                env *= max(0.0, rem) / release
            ph += 2 * math.pi * f * dt
            self.buf[i] += g * env * math.sin(ph)

    def blip(self, t0, f, dur, g):
        """양식/항목이 넘어갈 때 나는 짧은 삐 소리."""
        i0, i1 = max(0, int(t0 * SR)), min(self.n, int((t0 + dur) * SR))
        ph = 0.0
        dt = 1.0 / SR
        for i in range(i0, i1):
            p = (i - i0) / max(1, (i1 - i0))
            ph += 2 * math.pi * f * dt
            self.buf[i] += g * math.exp(-p * 4.5) * math.sin(ph)

    def thud(self, t0, dur, f0, f1, g):
        """도장이 찍히는 저역 충격."""
        i0, i1 = max(0, int(t0 * SR)), min(self.n, int((t0 + dur) * SR))
        ph = 0.0
        dt = 1.0 / SR
        for i in range(i0, i1):
            p = (i - i0) / max(1, (i1 - i0))
            ph += 2 * math.pi * (f0 + (f1 - f0) * p) * dt
            self.buf[i] += g * (1.0 - p) ** 1.6 * math.sin(ph)

    def bell(self, t0, f, dur, g):
        for (mul, gg, dd) in ((1.0, 1.0, 1.0), (2.0, 0.45, 0.7), (3.01, 0.2, 0.5)):
            i0, i1 = max(0, int(t0 * SR)), min(self.n, int((t0 + dur * dd) * SR))
            ph = 0.0
            dt = 1.0 / SR
            for i in range(i0, i1):
                p = (i - i0) / max(1, (i1 - i0))
                ph += 2 * math.pi * f * mul * dt
                self.buf[i] += g * gg * math.exp(-p * 4.2) * math.sin(ph)

    def hum(self, t0, t1, g, seed=5):
        """영사기/형광등 잡음 - 교육 비디오의 공기."""
        rng = random.Random(seed)
        i0, i1 = max(0, int(t0 * SR)), min(self.n, int(t1 * SR))
        y = 0.0
        ph = 0.0
        dt = 1.0 / SR
        ln = max(1e-6, (i1 - i0) / SR)
        for i in range(i0, i1):
            y += 0.02 * (rng.uniform(-1.0, 1.0) - y)
            tt = (i - i0) / SR
            env = min(1.0, tt / 0.6, max(0.0, (ln - tt) / 0.6))
            ph += 2 * math.pi * 120.0 * dt
            self.buf[i] += g * env * (y * 20.0 + 0.25 * math.sin(ph))

    def music(self, name, offset, at, dur, vol):
        self.music_cues.append((name, offset, at, dur, vol))

    # ---------------------------------------------------------- 출력
    def write_bed(self, path):
        out = array.array("h", bytes(2 * self.n))
        peak = 0.0
        for i in range(self.n):
            v = abs(self.buf[i])
            if v > peak:
                peak = v
        k = 0.90 / peak if peak > 0.90 else 1.0
        for i in range(self.n):
            v = int(self.buf[i] * k * 32767)
            out[i] = -32768 if v < -32768 else (32767 if v > 32767 else v)
        with wave.open(path, "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(SR)
            w.writeframes(out.tobytes())
        print("  최대치 %.2f (보정 x%.2f)" % (peak, k), flush=True)
        return path


def build(spec_name, out_path):
    S = importlib.import_module("ad_" + spec_name)
    A = Api(S.DUR)
    print("  소리 쌓기...", flush=True)
    S.audio(A)
    bed = A.write_bed(os.path.join(CACHE, "ad_bed_%s.wav" % spec_name))

    cmd = ["ffmpeg", "-y", "-hide_banner", "-loglevel", "error"]
    for (f, off, _t, dur, _v) in A.music_cues:
        cmd += ["-ss", str(off), "-t", str(dur + 0.4), "-i", os.path.join(MUSDIR, f)]
    cmd += ["-i", bed]
    parts = []
    for i, (_f, _off, t, dur, vol) in enumerate(A.music_cues):
        parts.append(
            "[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
            "atrim=0:%.3f,afade=t=in:st=0:d=0.35,afade=t=out:st=%.3f:d=0.45,"
            "volume=%.3f,adelay=%d[m%d]"
            % (i, dur, max(0.0, dur - 0.45), vol, int(t * 1000), i))
    nm = len(A.music_cues)
    parts.append("[%d:a]aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=mono,"
                 "volume=1.0[sx]" % nm)
    mixin = "".join("[m%d]" % i for i in range(nm)) + "[sx]"
    parts.append("%samix=inputs=%d:normalize=0:dropout_transition=0[mx]" % (mixin, nm + 1))
    parts.append("[mx]acompressor=threshold=0.16:ratio=4:attack=8:release=180,"
                 "volume=1.30,alimiter=limit=0.96,atrim=0:%.2f,"
                 "afade=t=out:st=%.2f:d=0.30[out]" % (S.DUR, S.DUR - 0.30))
    cmd += ["-filter_complex", ";".join(parts), "-map", "[out]",
            "-c:a", "aac", "-b:a", "192k", "-ar", str(SR), "-ac", "1", out_path]
    subprocess.run(cmd, check=True)
    return out_path


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", required=True, choices=("safety", "helpdesk"))
    ap.add_argument("--out", default=None)
    a = ap.parse_args()
    dest = a.out or os.path.join(CACHE, "ad_audio_%s.m4a" % a.spec)
    print(build(a.spec, dest))
