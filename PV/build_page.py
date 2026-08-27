#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PV 공유용 HTML 페이지 빌더
==========================

`make_pv.py`가 만든 MP4를 웹용으로 줄여 base64로 박아 넣고, 컷별 콘티와
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
ROOT = os.path.dirname(HERE)
OUT_DIR = os.path.join(HERE, "out")
SRC_MP4 = os.path.join(OUT_DIR, "comstock_pv.mp4")
WEB_MP4 = os.path.join(OUT_DIR, "comstock_pv_web.mp4")

# (시작초, 썸네일 시각, 컷 제목, 화면에 나오는 문구, 쓰인 게임 에셋)
CUTS = [
    (0.0,  1.4,  "사인온",        "잠시 후 방송을 시작합니다",
     "코드로 그린 테스트 패턴"),
    (1.8,  3.1,  "문제 제기",     "혹시… 좀비 때문에 고민이십니까? (네.)",
     "ZombieMove 8프레임 · ZombieAttack 12프레임"),
    (4.6,  5.2,  "절망 3연타",    "출근길에도 좀비! 퇴근길에도 좀비! 주말에도 좀비!!",
     "SprinterMove · ChargerMove · LeaderMove"),
    (6.8,  7.7,  "전환",          "그 . 래 . 서 !",
     "코드로 그린 집중선"),
    (8.0,  9.8,  "제품 등장",     "신형 전투 로봇 컴스톡 — 자동 조준! 탄약 무제한! 무게 제한 있음",
     "Comstock.png"),
    (11.2, 13.2, "무기 카탈로그", "전투 무기 65종 · 로봇 파츠 134개 — 전부 임시 수치입니다",
     "무기 스프라이트 12종"),
    (15.0, 16.4, "머리 12종",     "머리도 골라 쓰십시오 — …이것도 로봇입니다",
     "Heads/ 12종"),
    (17.8, 19.4, "전투 몽타주",   "웨이브 20회! 1회당 단돈 60초!",
     "Comstock · MuzzleFlash · Explosion · BasicBullet"),
    (21.4, 22.3, "보스",          "20웨이브, 보스 등장! 이길 수 있겠습니까? 저희도 모릅니다",
     "BossRoar 36프레임"),
    (24.6, 26.2, "로고 / CTA",    "지금 바로 플레이! + 초고속 면책 조항 + TV 꺼짐",
     "Orbitron-Black · Comstock.png"),
]


def ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        return "ffmpeg"


def ensure_web_version(ff):
    """페이지에 심을 경량본(960x540)을 만든다. 16MB 아티팩트 한도 안에 들어가야 한다."""
    if os.path.exists(WEB_MP4) and os.path.getmtime(WEB_MP4) > os.path.getmtime(SRC_MP4):
        return
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-i", SRC_MP4,
                    "-vf", "scale=960:540:flags=lanczos",
                    "-c:v", "libx264", "-preset", "slower", "-crf", "31",
                    "-maxrate", "2400k", "-bufsize", "4800k", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
                    WEB_MP4], check=True)


def grab(ff, t):
    """지정 시각의 프레임을 320x180 흑백 JPEG data URI로 뽑는다."""
    raw = os.path.join(OUT_DIR, "_grab.png")
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-ss", str(t),
                    "-i", SRC_MP4, "-frames:v", "1", raw], check=True)
    im = Image.open(raw).convert("L").resize((320, 180), Image.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=72, optimize=True)
    os.remove(raw)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def tc(sec):
    return f"{int(sec // 60):02d}:{sec % 60:04.1f}"


HEAD = """<title>컴스톡 방송국</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Anton&family=Black+Han+Sans&family=IBM+Plex+Mono:wght@400;600&family=Noto+Sans+KR:wght@400;500;700&display=swap">
<style>
/* 이 페이지는 '켜져 있는 브라운관' 한 세계에 고정한다(단일 테마).
   그래서 배경·글자색을 전부 명시적으로 칠하고 테마 분기를 두지 않는다. */
:root{
  --ink:#0A0A0B;        /* 브라운관 유리가 꺼져 있을 때의 검정 */
  --panel:#131316;
  --panel-2:#1B1B1F;
  --line:#2C2C31;
  --phos:#E9E7DF;       /* 인광체 백색 - 순백이 아니라 살짝 따뜻하다 */
  --dim:#8B8A84;
  --dimmer:#605F5B;
  --onair:#B8352C;      /* 이 페이지에서 유일하게 허용된 색 */
  --maxw:1080px;
}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%}
body{
  margin:0; background:var(--ink); color:var(--phos);
  font-family:"Noto Sans KR",system-ui,-apple-system,"Segoe UI",sans-serif;
  font-size:16px; line-height:1.75;
  background-image:
    radial-gradient(120% 80% at 50% -10%, rgba(233,231,223,.055), transparent 60%);
  background-attachment:fixed;
}
.wrap{max-width:var(--maxw); margin:0 auto; padding:0 24px}

/* ── 방송국 헤더 ─────────────────────────────────────────────── */
.slate{
  display:flex; align-items:center; gap:18px; flex-wrap:wrap;
  border-bottom:1px solid var(--line); padding:26px 0 20px;
}
.lamp{
  display:inline-flex; align-items:center; gap:9px;
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-weight:600;
  font-size:12px; letter-spacing:.20em; color:var(--onair);
  border:1px solid rgba(184,53,44,.5); border-radius:2px; padding:5px 10px;
}
.lamp i{
  width:7px; height:7px; border-radius:50%; background:var(--onair);
  box-shadow:0 0 9px rgba(184,53,44,.9); animation:blink 2.6s steps(1,end) infinite;
}
@keyframes blink{0%,88%{opacity:1}90%,94%{opacity:.25}96%,100%{opacity:1}}
.chan{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:12px;
  letter-spacing:.18em; color:var(--dimmer);
}
.chan b{color:var(--dim); font-weight:600}

h1{
  font-family:"Anton","Black Han Sans",Impact,sans-serif;
  font-weight:400; letter-spacing:.035em; line-height:.95;
  font-size:clamp(46px,9.5vw,104px); margin:34px 0 0; text-wrap:balance;
}
.kr-title{
  font-family:"Black Han Sans","Noto Sans KR",sans-serif;
  font-size:clamp(20px,3.4vw,30px); letter-spacing:.02em;
  color:var(--phos); margin:10px 0 0;
}
.lede{
  color:var(--dim); max-width:60ch; margin:16px 0 0; font-size:16.5px;
}
.specs{
  display:flex; flex-wrap:wrap; gap:8px; margin:22px 0 0; padding:0; list-style:none;
}
.specs li{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11.5px;
  letter-spacing:.10em; color:var(--dim);
  border:1px solid var(--line); border-radius:2px; padding:5px 9px;
  font-variant-numeric:tabular-nums;
}

/* ── 브라운관 ────────────────────────────────────────────────── */
.tv{margin:34px 0 0}
.set{
  position:relative; background:#08080A; border:1px solid var(--line);
  border-radius:18px; padding:14px;
  box-shadow:0 0 0 1px rgba(233,231,223,.04) inset, 0 26px 70px -30px rgba(0,0,0,.95);
}
.tube{position:relative; border-radius:9px; overflow:hidden; background:#000; line-height:0}
.tube video{width:100%; height:auto; display:block}
.tube::after{                       /* 주사선 유리 - 조작은 통과시킨다 */
  content:""; position:absolute; inset:0; pointer-events:none; border-radius:9px;
  background:repeating-linear-gradient(to bottom,
    rgba(0,0,0,.20) 0 1px, rgba(0,0,0,0) 1px 3px);
  mix-blend-mode:multiply; opacity:.55;
}
.knobs{
  display:flex; align-items:center; justify-content:space-between; gap:14px;
  margin-top:12px; padding:0 4px;
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11px;
  letter-spacing:.16em; color:var(--dimmer);
}
.knobs span{display:inline-flex; align-items:center; gap:8px}
.knobs em{
  width:12px; height:12px; border-radius:50%; font-style:normal;
  border:1px solid var(--line); background:linear-gradient(160deg,#242429,#0E0E11);
}

/* ── 섹션 ───────────────────────────────────────────────────── */
section{padding:64px 0 0}
h2{
  font-family:"Black Han Sans","Noto Sans KR",sans-serif; font-weight:400;
  font-size:clamp(24px,3.6vw,34px); margin:0; letter-spacing:.01em;
}
.sub{color:var(--dimmer); font-size:14px; margin:6px 0 0}
.rule{height:1px; background:var(--line); margin:18px 0 0}

/* ── 컷 표 ──────────────────────────────────────────────────── */
.cuts{list-style:none; margin:0; padding:0}
.cut{
  display:grid; gap:18px; align-items:start;
  grid-template-columns:86px 160px 1fr;
  padding:18px 0; border-bottom:1px solid var(--line);
}
.seek{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:13px; font-weight:600;
  font-variant-numeric:tabular-nums; letter-spacing:.04em;
  color:var(--dim); background:none; border:0; border-left:2px solid var(--line);
  padding:2px 0 2px 10px; cursor:pointer; text-align:left; width:100%;
  transition:color .15s ease, border-color .15s ease;
}
.seek:hover,.seek:focus-visible{color:var(--onair); border-color:var(--onair)}
.seek:focus-visible{outline:2px solid var(--onair); outline-offset:3px}
.shot{width:100%; height:auto; border-radius:3px; display:block; filter:contrast(1.05)}
.cut h3{
  font-family:"Black Han Sans","Noto Sans KR",sans-serif; font-weight:400;
  font-size:19px; margin:0 0 4px; letter-spacing:.01em;
}
.line{color:var(--phos); margin:0; font-size:15px}
.asset{
  color:var(--dimmer); margin:8px 0 0;
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11.5px; letter-spacing:.04em;
}

/* ── 제작 노트 ──────────────────────────────────────────────── */
.notes{display:grid; gap:18px; grid-template-columns:repeat(auto-fit,minmax(258px,1fr)); margin-top:26px}
.note{background:var(--panel); border:1px solid var(--line); border-radius:4px; padding:20px 22px}
.note h3{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:11px; font-weight:600;
  letter-spacing:.20em; color:var(--dim); margin:0 0 10px; text-transform:uppercase;
}
.note p{margin:0; font-size:14.5px; color:var(--phos); line-height:1.7}
.note p + p{margin-top:10px}
code{
  font-family:"IBM Plex Mono",ui-monospace,monospace; font-size:12.5px;
  background:var(--panel-2); border:1px solid var(--line); border-radius:3px;
  padding:1px 5px; color:var(--phos);
}
pre{
  background:var(--panel); border:1px solid var(--line); border-radius:4px;
  padding:16px 18px; overflow-x:auto; margin:20px 0 0;
}
pre code{background:none; border:0; padding:0; font-size:12.5px; line-height:1.85}

footer{
  margin-top:70px; border-top:1px solid var(--line); padding:22px 0 60px;
  font-family:"IBM Plex Mono",ui-monospace,monospace;
  font-size:11px; letter-spacing:.16em; color:var(--dimmer);
  display:flex; justify-content:space-between; gap:16px; flex-wrap:wrap;
}

@media (max-width:720px){
  .cut{grid-template-columns:74px 1fr; }
  .cut .shot{grid-column:1 / -1; max-width:320px}
}
@media (prefers-reduced-motion:reduce){
  *{animation:none !important; transition:none !important}
}
</style>"""


def build(out_path):
    ff = ffmpeg_exe()
    if not os.path.exists(SRC_MP4):
        sys.exit(f"먼저 make_pv.py로 영상을 만들어야 한다: {SRC_MP4}")
    ensure_web_version(ff)

    with open(WEB_MP4, "rb") as f:
        vid = base64.b64encode(f.read()).decode()

    rows = []
    for start, at, title, line, asset in CUTS:
        thumb = grab(ff, at)
        rows.append(f"""    <li class="cut">
      <button class="seek" type="button" data-t="{start}">{tc(start)}</button>
      <img class="shot" src="{thumb}" alt="{title} 컷" width="320" height="180" loading="lazy">
      <div>
        <h3>{title}</h3>
        <p class="line">{line}</p>
        <p class="asset">{asset}</p>
      </div>
    </li>""")
    cuts_html = "\n".join(rows)

    body = f"""{HEAD}
<div class="wrap">

  <header class="slate">
    <span class="lamp"><i></i>ON AIR</span>
    <span class="chan">CH <b>20</b> · 흑백 · 30.0초 · 1280&times;720 · 24fps</span>
  </header>

  <h1>COMSTOCK</h1>
  <p class="kr-title">컴스톡 30초 소개 영상</p>
  <p class="lede">
    1950년대 미국 흑백 TV 광고 톤으로 만든 게임 PV다. 화면에 나오는 좀비·보스·무기·로봇은
    전부 게임에 실제로 들어 있는 스프라이트고, 소리도 게임의 BGM과 효과음을 그대로 썼다.
    지직거림·주사선·수직 흔들림·필름 먼지는 프레임마다 코드로 얹었다.
  </p>
  <ul class="specs">
    <li>컷 10개</li>
    <li>프레임 720장</li>
    <li>게임 스프라이트 130여 장</li>
    <li>효과음 큐 23개</li>
  </ul>

  <div class="tv">
    <div class="set">
      <div class="tube">
        <video id="pv" controls playsinline preload="metadata"
               src="data:video/mp4;base64,{vid}"></video>
      </div>
      <div class="knobs">
        <span><em></em><em></em>V-HOLD · TRACKING</span>
        <span>COMSTOCK BROADCASTING</span>
      </div>
    </div>
  </div>

  <section>
    <h2>컷 구성</h2>
    <p class="sub">시각을 누르면 그 컷부터 재생된다.</p>
    <div class="rule"></div>
    <ol class="cuts">
{cuts_html}
    </ol>
  </section>

  <section>
    <h2>어떻게 만들었나</h2>
    <div class="rule"></div>
    <div class="notes">
      <div class="note">
        <h3>재료</h3>
        <p><code>Assets/Resources/</code>의 실제 게임 스프라이트만 썼다. 좀비 걷기 8프레임,
           보스 포효 36프레임, 폭발 10프레임, 총구 화염 3프레임, 무기 12종, 머리 12종,
           폐허 도시 배경.</p>
        <p>새로 그린 그림은 없고, 집중선·테스트 패턴·자막만 코드로 그렸다.</p>
      </div>
      <div class="note">
        <h3>흑백 브라운관</h3>
        <p>4:3(960&times;720)으로 그린 뒤 흑백 변환 → 대비 강화 → 컴포지트 번짐 →
           수직 롤바 → 주사선 → 비네팅 → 필름 그레인 → 수평 찢김 → 먼지·스크래치 →
           프레임 지터 순으로 얹고, 16:9 한가운데에 필러박스로 앉혔다.</p>
        <p>컷이 바뀌는 순간마다 정전기를 터뜨려 채널이 튀는 것처럼 보이게 했다.</p>
      </div>
      <div class="note">
        <h3>소리</h3>
        <p><code>Game_BGM01</code>에 게임 효과음 16종을 타임코드에 맞춰 얹고,
           백색 잡음(브라운관 히스)과 60Hz 험을 섞었다.</p>
        <p>전체를 320~3600Hz로 잘라 낡은 TV 스피커처럼 만든 뒤 방송 수준으로 정규화했다.</p>
      </div>
      <div class="note">
        <h3>애니메이션 프레임의 함정</h3>
        <p>연속 프레임을 <b>프레임마다 따로</b> 알파 bbox로 자르면 안 된다. 실루엣이 변하는
           걷기·포효는 프레임마다 bbox가 달라서, 각자 잘라 같은 높이로 그리면 그림이 매 프레임
           튄다.</p>
        <p>전체 프레임의 <b>합집합 bbox로 한 번에</b> 잘라야 상대 위치와 크기가 보존된다.</p>
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
    <span>※ 좀비는 실제로 제거되지 않습니다</span>
  </footer>

</div>

<script>
  // 컷 표의 시각을 누르면 그 지점부터 재생한다.
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
    args = ap.parse_args()
    build(args.out)


if __name__ == "__main__":
    main()
