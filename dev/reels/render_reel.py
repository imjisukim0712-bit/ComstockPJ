# -*- coding: utf-8 -*-
"""컴스톡 세로 릴스 렌더러.

    python3 render_reel.py all              # EN/KO 두 벌 (무음 트랙)
    python3 render_reel.py en --beat        # 게임 효과음으로 만든 비트 얹기
    python3 render_reel.py en --preview 0,2.1,7.6,13.2   # 그 시각 프레임만 PNG로

프레임은 파일로 떨구지 않고 **rawvideo로 ffmpeg에 바로 흘린다**(1080x1920 PNG 450장을
디스크에 썼다 읽으면 인코딩보다 그게 더 오래 걸린다).
"""
import os
import subprocess
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import reel_draw as D
from reel_common import (W, H, FPS, DUR, BAR, BARS, NFRAMES, BG, INK, LANG,
                         FACES_PER_BAR, ENDCARD_BAR, HEAD_ORDER, OUT, CACHE,
                         SAFE_TOP, SAFE_BOTTOM, ffmpeg_exe, run)

FF = ffmpeg_exe()

# 마디별로 무대에 서는 얼굴들. 앞 마디 얼굴은 그대로 두고 새 얼굴이 합류한다.
CAST = {}
for _b, _n in enumerate(FACES_PER_BAR):
    CAST[_b] = list(HEAD_ORDER[:_n])


def endcard_layer(lang):
    """제목 카드를 한 번만 그려 둔다(프레임마다 슬램 배율/투명도만 달리 준다)."""
    L = LANG[lang]
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    from PIL import ImageDraw
    d = ImageDraw.Draw(im, "RGBA")
    d.rounded_rectangle([64, 762, W - 64, 1186], radius=48, fill=(255, 255, 255, 236),
                        outline=INK + (255,), width=9)
    inner = W - 64 * 2 - 80
    D.text_center(im, L["title"], D.font(L["font_title"], 138), 876,
                  max_w=inner, path=L["font_title"])
    D.text_center(im, L["sub"], D.font(L["font_sub"], 48), 1010,
                  fill=(86, 86, 98), max_w=inner, path=L["font_sub"])
    D.text_center(im, L["url"], D.font(L["font_url"], 42), 1098,
                  fill=(196, 148, 20), max_w=inner, path=L["font_url"])
    return im


def render_frame(i, lang, card):
    t = i / FPS
    bar = min(BARS - 1, int(t / BAR))
    cast = CAST[bar]
    n = len(cast)
    slots = D.layout(n)

    frame = Image.new("RGBA", (W, H), BG + (255,))

    # 히트에서 나오는 압축량. 슬롯 0 것을 화면 전체 펄스와 방사선에도 재활용한다.
    s0, hit_n = D.hit_state(t, 0, 1)
    k = hit_n % 6 if hit_n >= 0 else -1
    # "담"(4분음표)에서만, 그리고 화면이 얼굴로 꽉 차지 않은 마디에서만 방사선을 켠다.
    if k in (0, 1) and n <= 4:
        D.speed_lines(frame, s0, {1: 430, 2: 380, 4: 470}[n])

    zoom = D.camera_pulse(t)
    ccy = (SAFE_TOP + SAFE_BOTTOM) / 2.0

    for idx, (cx, cy, size) in enumerate(slots):
        name = cast[idx]
        s, hn = D.hit_state(t, idx, n)
        anim = (hn if hn >= 0 else 0) % D.frame_count(name)
        rot = (6.0 if idx % 2 == 0 else -6.0) * s
        zx = W / 2 + (cx - W / 2) * zoom
        zy = ccy + (cy - ccy) * zoom
        D.paste_face(frame, name, anim, zx, zy, size * zoom, s, rot)

    L = LANG[lang]
    if bar < ENDCARD_BAR:
        D.text_center(frame, L["caption"], D.font(L["font_caption"], 54), 178)
    else:
        # 마지막 마디: 제목 카드가 크게 나타나 자리를 잡는다(슬램).
        u = t - ENDCARD_BAR * BAR
        p = min(1.0, u / 0.22)
        sc = max(0.5, 1.0 + 1.15 * (1.0 - p) ** 2.2)
        lay = card if abs(sc - 1.0) < 0.01 else card.resize(
            (max(2, int(W * sc)), max(2, int(H * sc))), D.LANCZOS)
        if p < 1.0:
            # ★ 커진 채로 들어오는 동안은 투명도도 같이 올린다 - 안 그러면 화면 밖에서
            #    갑자기 잘린 테두리가 나타나 슬램이 아니라 "튀는 오류"처럼 보인다.
            lay = lay.copy()
            lay.putalpha(lay.getchannel("A").point(lambda v, a=p: int(v * a)))
        frame.alpha_composite(lay, ((W - lay.width) // 2, (H - lay.height) // 2))

    return frame.convert("RGB")


def render_silent(lang):
    """프레임을 rawvideo로 흘려 영상만 인코딩한다(오디오는 나중에 붙인다)."""
    card = endcard_layer(lang)
    os.makedirs(CACHE, exist_ok=True)
    tmp = os.path.join(CACHE, "video_%s.mp4" % lang)
    cmd = [FF, "-y", "-hide_banner", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "%dx%d" % (W, H),
           "-r", str(FPS), "-i", "-",
           "-an", "-c:v", "libx264", "-preset", "slow", "-crf", "19",
           "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.0",
           "-r", str(FPS), tmp]
    p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(NFRAMES):
        p.stdin.write(render_frame(i, lang, card).tobytes())
        if i % 90 == 0:
            print("  %d/%d" % (i, NFRAMES), flush=True)
    p.stdin.close()
    if p.wait() != 0:
        raise RuntimeError("ffmpeg 인코딩 실패")
    return tmp


def mux(video, lang, beat):
    """이미 인코딩된 영상에 오디오만 갈아 붙인다(`-c:v copy`라 다시 렌더하지 않는다)."""
    out = os.path.join(OUT, "Comstock_Reel_%s%s.mp4" % (lang.upper(), "_beat" if beat else ""))
    if beat:
        import build_beat
        audio_in = ["-i", build_beat.build()]
        bitrate = "192k"
    else:
        # ★ 무음이라도 오디오 트랙은 넣는다 - 트랙이 아예 없는 mp4를 거부하는 업로더가 있다.
        audio_in = ["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]
        bitrate = "64k"
    run([FF, "-y", "-hide_banner", "-loglevel", "error", "-i", video] + audio_in
        + ["-shortest", "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
           "-c:a", "aac", "-b:a", bitrate, "-ac", "2", "-ar", "48000",
           "-movflags", "+faststart", out])
    print("완료:", out, "%.1fMB" % (os.path.getsize(out) / 1e6))
    return out


def build_video(lang, beat=False):
    """무음판을 항상 만들고, `beat`면 비트판까지 같은 영상으로 하나 더 뽑는다."""
    video = render_silent(lang)
    outs = [mux(video, lang, False)]
    if beat:
        outs.append(mux(video, lang, True))
    return outs


def preview(lang, times):
    card = endcard_layer(lang)
    os.makedirs(CACHE, exist_ok=True)
    paths = []
    for t in times:
        i = int(round(float(t) * FPS))
        p = os.path.join(CACHE, "preview_%s_%06.2f.png" % (lang, float(t)))
        render_frame(i, lang, card).save(p)
        paths.append(p)
        print(p)
    return paths


def main():
    args = [a for a in sys.argv[1:]]
    beat = "--beat" in args
    args = [a for a in args if a != "--beat"]
    if "--preview" in args:
        j = args.index("--preview")
        lang = args[0] if j > 0 else "en"
        preview(lang, args[j + 1].split(","))
        return
    which = (args[0] if args else "all").lower()
    langs = ["en", "ko"] if which == "all" else [which]
    for lg in langs:
        print("[%s] 렌더 시작 (%d프레임 / %.2f초)" % (lg, NFRAMES, DUR))
        build_video(lg, beat=beat)


if __name__ == "__main__":
    main()
