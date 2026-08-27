#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PV 공유용 HTML 페이지 빌더
==========================

`make_pv.py`가 만든 세로 MP4를 웹용으로 줄여 base64로 박아 넣고, 컷별 콘티와
제작 노트를 붙인 한 장짜리 페이지를 만든다. 외부 요청 없이 혼자 재생되므로
파일 하나만 넘기면 어디서든 열린다.

    python3 PV/build_page.py [-o 출력.html]
"""

import argparse
import base64
import io
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "out")
SRC_MP4 = os.path.join(OUT_DIR, "comstock_pv.mp4")
WEB_MP4 = os.path.join(OUT_DIR, "comstock_pv_web.mp4")

# (시작초, 썸네일 시각, 컷 제목, 화면 문구, 이 컷이 흉내 내는 광고 클리셰)
CUTS = [
    (0.0,  1.90, "1. 컴스톡 등장", "어둠 속으로 낙하 → 착지 충격 → 빛이 들며 드러난다",
     "착지 순간 카메라가 확 당겨졌다가 끝까지 천천히 풀린다"),
    (2.4,  3.20, "2. 좀비가 뒤돌아본다", "한 마리가 돌아보고 \"!\" → 대군단이 화면을 채운다",
     "카메라를 얼굴에 붙여 뒀다가 빼면서 화면 밖 떼를 드러낸다"),
    (5.0,  6.10, "3. 잡으면서 진행", "가만히 서 있어도 알아서 쏜다 / 자동 조준·자동 공격",
     "사방으로 뿌리는 탄과 빨려 들어오는 경험치 보석"),
    (7.6,  9.40, "4. 게임으로 전환", "쓰러뜨릴수록 강해진다 → LEVEL UP → 3장 중 1장",
     "HUD가 얹히며 연출 화면이 게임 화면이 된다"),
    (10.4, 11.00, "5-1. 장비 전환", "무기가 착착 바뀐다 — 무기 65종 · 파츠 134개",
     "머리는 고정하고 좌우 무기만 교체한다"),
    (11.7, 12.30, "5-2. 각 컴스톡", "머리 12종을 빠르게 넘긴다",
     "하단 12칸 로스터가 '고를 수 있다'를 한눈에 보여 준다"),
    (12.82, 13.05, "5-3. 합격", "도장이 쿵 — 골라서 조립한다 · 로봇 12종",
     "위에서 크게 내려와 순식간에 제 크기가 되는 스탬프"),
    (13.4, 14.70, "6. 게임 로고", "COMSTOCK / 컴스톡 / STEAM 위시리스트에 추가",
     "로고가 착지하며 화면이 한 번 하얗게 튄다"),
]


def ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return "ffmpeg"


def ensure_web_version(ff):
    """페이지에 심을 경량본을 만든다. 16MB 아티팩트 한도 안에 들어가야 한다."""
    if os.path.exists(WEB_MP4) and os.path.getmtime(WEB_MP4) > os.path.getmtime(SRC_MP4):
        return
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-i", SRC_MP4,
                    "-vf", "scale=1280:720:flags=lanczos",
                    "-c:v", "libx264", "-preset", "slower", "-crf", "30",
                    "-maxrate", "2600k", "-bufsize", "5200k", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
                    WEB_MP4], check=True)


def grab(ff, t):
    raw = os.path.join(OUT_DIR, "_grab.png")
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-ss", str(t),
                    "-i", SRC_MP4, "-frames:v", "1", raw], check=True)
    im = Image.open(raw).convert("RGB").resize((320, 180), Image.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=74, optimize=True)
    os.remove(raw)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def tc(sec):
    return f"{sec:05.2f}"


HEAD = """<title>컴스톡 스팀 트레일러</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Black+Han+Sans&family=IBM+Plex+Mono:wght@400;600&family=Noto+Sans+KR:wght@400;500;700&display=swap">
<style>
/* 영상이 원색으로 시끄러운 만큼 페이지는 조용히 간다. 자홍색 하나만 강조로 쓰고
   나머지는 보라 기가 도는 중성색으로 맞춘다(순회색은 고른 티가 안 난다). */
:root{
  --bg:#0E0B14;
  --panel:#17131F;
  --panel-2:#1F1A2A;
  --line:#2E2739;
  --fg:#F2EEF7;
  --dim:#A79FB6;
  --dimmer:#756C86;
  --hot:#FF3D8B;
  --gold:#FFC24D;
  --maxw:1120px;
}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%}
body{
  margin:0; background:var(--bg); color:var(--fg);
  font-family:"Noto Sans KR",system-ui,-apple-system,"Segoe UI",sans-serif;
  font-size:16px; line-height:1.75;
}
.wrap{max-width:var(--maxw); margin:0 auto; padding:0 24px}

.eyebrow{
  display:inline-flex; align-items:center; gap:9px; margin:30px 0 0;
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-weight:600;
  font-size:11.5px; letter-spacing:.20em; color:var(--hot);
  border:1px solid rgba(255,61,139,.42); border-radius:2px; padding:6px 11px;
}
h1{
  font-family:"Black Han Sans","Noto Sans KR",sans-serif; font-weight:400;
  font-size:clamp(38px,7vw,74px); line-height:1.08; letter-spacing:.005em;
  margin:20px 0 0; text-wrap:balance;
}
h1 .hot{color:var(--hot)}
.lede{color:var(--dim); max-width:58ch; margin:16px 0 0}
.specs{display:flex; flex-wrap:wrap; gap:8px; margin:22px 0 0; padding:0; list-style:none}
.specs li{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11.5px;
  letter-spacing:.10em; color:var(--dim); font-variant-numeric:tabular-nums;
  border:1px solid var(--line); border-radius:2px; padding:5px 9px;
}

/* ── 세로 영상은 폰 안에 넣어야 크기가 납득된다 ─────────────── */
.stage{margin:40px 0 0}
.phone{
  background:linear-gradient(160deg,#2B2438,#120F1A);
  border:1px solid var(--line); border-radius:16px; padding:10px;
  box-shadow:0 30px 80px -34px rgba(0,0,0,.9);
}
.phone video{width:100%; height:auto; display:block; border-radius:8px; background:#000}
.aside{display:grid; gap:22px; grid-template-columns:repeat(auto-fit,minmax(300px,1fr));
       margin-top:30px}
.aside h2{margin-top:0}
.aside p{color:var(--dim); margin:10px 0 0}

section{padding:60px 0 0}
h2{
  font-family:"Black Han Sans","Noto Sans KR",sans-serif; font-weight:400;
  font-size:clamp(23px,3.4vw,32px); margin:0; letter-spacing:.01em;
}
.sub{color:var(--dimmer); font-size:14px; margin:6px 0 0}
.rule{height:1px; background:var(--line); margin:18px 0 0}

/* ── 컷 표 ──────────────────────────────────────────────── */
.cuts{list-style:none; margin:0; padding:0}
.cut{display:grid; gap:20px; align-items:start;
     grid-template-columns:96px 200px 1fr;
     padding:20px 0; border-bottom:1px solid var(--line)}
.seek{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:13px; font-weight:600;
  font-variant-numeric:tabular-nums; color:var(--dim); background:none;
  border:0; border-left:2px solid var(--line); padding:2px 0 2px 11px;
  cursor:pointer; text-align:left; width:100%;
  transition:color .15s ease, border-color .15s ease;
}
.seek:hover,.seek:focus-visible{color:var(--hot); border-color:var(--hot)}
.seek:focus-visible{outline:2px solid var(--hot); outline-offset:3px}
.shot{width:100%; height:auto; border-radius:6px; display:block; border:1px solid var(--line)}
.cut h3{font-family:"Black Han Sans","Noto Sans KR",sans-serif; font-weight:400;
        font-size:19px; margin:0 0 5px}
.line{margin:0; font-size:15px}
.trope{color:var(--dimmer); margin:9px 0 0; font-size:13.5px}

/* ── 노트 ───────────────────────────────────────────────── */
.notes{display:grid; gap:18px; grid-template-columns:repeat(auto-fit,minmax(262px,1fr));
       margin-top:26px}
.note{background:var(--panel); border:1px solid var(--line); border-radius:5px;
      padding:20px 22px}
.note h3{font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11px;
         font-weight:600; letter-spacing:.20em; color:var(--dim);
         margin:0 0 10px; text-transform:uppercase}
.note p{margin:0; font-size:14.5px; line-height:1.72}
.note p + p{margin-top:10px}
.note b{color:var(--gold); font-weight:700}
code{font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:12.5px;
     background:var(--panel-2); border:1px solid var(--line); border-radius:3px;
     padding:1px 5px}
pre{background:var(--panel); border:1px solid var(--line); border-radius:5px;
    padding:16px 18px; overflow-x:auto; margin:20px 0 0}
pre code{background:none; border:0; padding:0; line-height:1.85}

footer{margin-top:66px; border-top:1px solid var(--line); padding:22px 0 60px;
       font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11px;
       letter-spacing:.14em; color:var(--dimmer);
       display:flex; justify-content:space-between; gap:16px; flex-wrap:wrap}

@media (max-width:760px){
  .cut{grid-template-columns:84px 1fr}
  .cut .shot{grid-column:1 / -1; max-width:180px}
}
@media (prefers-reduced-motion:reduce){*{animation:none !important; transition:none !important}}
</style>"""


def build(out_path):
    ff = ffmpeg_exe()
    if not os.path.exists(SRC_MP4):
        sys.exit(f"먼저 make_pv.py로 영상을 만들어야 한다: {SRC_MP4}")
    ensure_web_version(ff)

    with open(WEB_MP4, "rb") as f:
        vid = base64.b64encode(f.read()).decode()

    rows = []
    for start, at, title, line, trope in CUTS:
        rows.append(f"""    <li class="cut">
      <button class="seek" type="button" data-t="{start}">{tc(start)}s</button>
      <img class="shot" src="{grab(ff, at)}" alt="{title} 컷" width="320" height="180" loading="lazy">
      <div>
        <h3>{title}</h3>
        <p class="line">{line}</p>
        <p class="trope">{trope}</p>
      </div>
    </li>""")

    body = f"""{HEAD}
<div class="wrap">

  <p class="eyebrow">15.0s · 1080&times;1920 · 30fps</p>
  <h1>가만히 서 있어도<br><span class="hot">알아서 쏜다</span></h1>
  <p class="lede">
    콘티 6컷을 그대로 옮긴 컴스톡의 스팀 트레일러다. 컴스톡이 떨어져 등장하고, 좀비 한
    마리가 돌아보는 순간 대군단이 밀려오고, 사방으로 자동 사격하며 쓸어 담다가 게임
    화면으로 넘어간다. 장비와 머리를 갈아 끼운 뒤 "합격" 도장으로 마무리한다. 화면에
    나오는 좀비·무기·로봇은 전부 게임에 실제로 들어 있는 스프라이트고, 소리도 게임의
    BGM과 효과음이다.
  </p>
  <ul class="specs">
    <li>컷 6개</li>
    <li>프레임 450장</li>
    <li>게임 스프라이트 120여 장</li>
    <li>효과음 큐 27개</li>
  </ul>

  <div class="stage">
    <div class="phone">
      <video id="pv" controls playsinline preload="metadata"
             src="data:video/mp4;base64,{vid}"></video>
    </div>
    <div class="aside">
      <div>
        <h2>무엇이 스팀 톤을 만드나</h2>
        <p>
          세 가지다. <b>가로 16:9</b>가 PC 게임이라는 가장 빠른 신호이고, 조이스틱·터치
          버튼·손가락 커서를 걷어낸 자리에 <b>상단 경험치 바·웨이브 타이머·재화</b>가 들어가
          뱀서라이크 PC HUD가 된다. 자막은 광고 톤의 금색 3겹 대신 하단 스크림 위의 담백한
          흰 글씨다.
        </p>
        <p>
          후처리도 절제했다. 채도는 +18%(광고 톤은 +34%), 줌 펀치와 화면 흔들림은 여섯 곳에만
          남겼다. 트레일러는 흔들릴수록 싸 보인다.
        </p>
      </div>
      <div>
        <h2>4번 컷이 축이다</h2>
        <p>
          콘티의 <b>"게임으로 전환"</b>을 화면으로 옮기려면 앞 세 컷은 게임 화면이 아니어야
          한다. 1~3번은 HUD 없이 연출로만 가고, 4번에서 HUD가 얹히며 게임 화면이 된다.
        </p>
        <p>
          2번 컷의 대군단도 같은 원리다. 카메라를 좀비 얼굴에 바짝 당겨 두고, 돌아보는 순간
          쭉 빼면 화면 밖에 있던 떼가 한꺼번에 드러난다.
        </p>
      </div>
    </div>
  </div>

  <section>
    <h2>컷 구성</h2>
    <p class="sub">시각을 누르면 그 컷부터 재생된다.</p>
    <div class="rule"></div>
    <ol class="cuts">
{chr(10).join(rows)}
    </ol>
  </section>

  <section>
    <h2>어떻게 만들었나</h2>
    <div class="rule"></div>
    <div class="notes">
      <div class="note">
        <h3>재료</h3>
        <p><code>Assets/Resources/</code>의 실제 게임 에셋만 썼다. 좀비 걷기 8프레임,
           폭발 10프레임, 레벨업 24프레임, 착지 먼지 3프레임, 무기 12종, 머리 12종,
           그리고 <b>UI 아트로 조립한 조작 화면</b>.</p>
        <p>새로 그린 건 손가락 커서와 합격 도장뿐이고, 둘 다 코드로 그렸다.</p>
      </div>
      <div class="note">
        <h3>각 컴스톡 조립</h3>
        <p><code>Comstock.png</code>는 무기까지 통째로 그려진 한 장이라 장비를 바꿀 수 없다.
           그래서 <b>머리(=몸통) + 다리 파츠 + 좌우 무기</b>를 코드로 조립했다. 게임의
           <code>ProceduralCharacterRig</code>가 하는 일과 같은 발상이다.</p>
        <p>무기만 띄워 두면 몸에서 떨어져 보여서 <b>팔 연결부</b>를 선으로 그려 이었다.</p>
      </div>
      <div class="note">
        <h3>카메라가 이야기를 만든다</h3>
        <p>2번 컷은 좀비 얼굴에 바짝 당겨 두고 <b>돌아보는 순간 쭉 뺀다</b>. 화면 밖에 있던
           떼가 한꺼번에 드러나면서 "대군단이 온다"가 된다.</p>
        <p>1번 컷은 반대다. 낙하 중에는 거의 당기지 않고(당기면 떨어지는 몸이 잘린다),
           <b>착지 순간 확 당겼다가</b> 끝까지 천천히 풀어 준다.</p>
      </div>
      <div class="note">
        <h3>후처리</h3>
        <p>채도 +30%, 밝은 부분만 뽑아 번지게 하는 블룸, 그리고 임팩트 12곳마다
           <b>줌 펀치와 화면 흔들림</b>. 컷이 바뀔 때는 흰 플래시로 넘긴다.</p>
        <p>소리는 게임 BGM에 효과음 27개를 타임코드에 맞춰 얹고
           <code>loudnorm=I=-14</code>로 정규화했다.</p>
      </div>
    </div>

    <pre><code># 다시 뽑으려면 (Git LFS 에셋이 받아져 있어야 한다)
git lfs pull
python3 PV/make_pv.py              # PV/out/comstock_pv.mp4
python3 PV/make_pv.py --stills     # 확인용 정지 프레임
python3 PV/build_page.py           # 이 페이지</code></pre>
  </section>

  <footer>
    <span>COMSTOCK · 웨이브 서바이벌 · 로봇 모딩</span>
    <span>화면 속 좀비·무기·로봇은 전부 실제 게임 에셋입니다</span>
  </footer>

</div>

<script>
  var pv = document.getElementById('pv');
  document.querySelectorAll('.seek').forEach(function (b) {{
    b.addEventListener('click', function () {{
      pv.currentTime = parseFloat(b.dataset.t);
      pv.play();
      pv.scrollIntoView({{ block: 'center', behavior: 'smooth' }});
    }});
  }});
</script>
"""
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(body)
    print(f"완료: {out_path}  ({os.path.getsize(out_path)/1e6:.2f} MB)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-o", "--out", default=os.path.join(OUT_DIR, "comstock_pv.html"))
    build(ap.parse_args().out)


if __name__ == "__main__":
    main()
