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
    (0.0,  0.9,  "훅", "이게 진짜 무료라고? / 설치 1분 만에 만렙",
     "첫 1초에 가짜 조작 UI와 손가락 커서를 보여 준다"),
    (1.5,  2.6,  "방치형", "폰 꺼놔도 알아서 렙업! → 접속만 해도 Lv.999",
     "AUTO 토글, 레벨 1→999 롤링, 골드 비"),
    (3.4,  4.6,  "SSR 뽑기", "100연차 무료 → ★★★★★ 전설등급 획득!",
     "암전 → 무지개 광선 → 폭발 → 등장의 뽑기 3단 연출"),
    (5.6,  6.4,  "무기 자랑", "무기 65종 전부 무료 지급 (진짜로)",
     "등급 카드가 위에서 우수수 떨어진다"),
    (7.3,  8.2,  "가짜 선택지", "당신의 선택은? → 실패! → 정답: 둘 다 장착",
     "3초 카운트다운과 오답 연출. 이 장르의 대표 밈"),
    (9.5,  10.4, "보스", "DPS 999,999,999 / 보스도 3초컷",
     "보스 포효 → 데미지 숫자 폭주 → 폭사"),
    (11.4, 12.2, "가짜 랭킹", "전 서버 1위 달성! ★★★★★ 4.9 / 다운로드 100만 돌파",
     "가짜 랭킹표와 가짜 리뷰로 만드는 사회적 증거"),
    (13.1, 14.2, "CTA", "지금 플레이! / 선착순 마감까지 00:0X",
     "뛰는 버튼, 무료 리본, 초소형 면책 조항"),
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
                    "-vf", "scale=720:1280:flags=lanczos",
                    "-c:v", "libx264", "-preset", "slower", "-crf", "30",
                    "-maxrate", "2600k", "-bufsize", "5200k", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
                    WEB_MP4], check=True)


def grab(ff, t):
    raw = os.path.join(OUT_DIR, "_grab.png")
    subprocess.run([ff, "-y", "-hide_banner", "-loglevel", "error", "-ss", str(t),
                    "-i", SRC_MP4, "-frames:v", "1", raw], check=True)
    im = Image.open(raw).convert("RGB").resize((198, 352), Image.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=74, optimize=True)
    os.remove(raw)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def tc(sec):
    return f"{sec:05.2f}"


HEAD = """<title>컴스톡 15초 광고</title>
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
.stage{display:grid; gap:34px; grid-template-columns:minmax(0,300px) 1fr;
       align-items:start; margin:40px 0 0}
.phone{
  background:linear-gradient(160deg,#2B2438,#120F1A);
  border:1px solid var(--line); border-radius:38px; padding:11px;
  box-shadow:0 30px 80px -34px rgba(0,0,0,.9);
}
.phone video{width:100%; height:auto; display:block; border-radius:28px; background:#000}
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
     grid-template-columns:96px 108px 1fr;
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
  .stage{grid-template-columns:1fr}
  .phone{max-width:320px; margin:0 auto}
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
      <img class="shot" src="{grab(ff, at)}" alt="{title} 컷" width="198" height="352" loading="lazy">
      <div>
        <h3>{title}</h3>
        <p class="line">{line}</p>
        <p class="trope">{trope}</p>
      </div>
    </li>""")

    body = f"""{HEAD}
<div class="wrap">

  <p class="eyebrow">15.0s · 1080&times;1920 · 30fps</p>
  <h1>어디서 본 것 같은<br><span class="hot">그 모바일 광고</span></h1>
  <p class="lede">
    양산형 모바일 게임 광고의 문법을 그대로 흉내 낸 컴스톡 PV다. 가짜 조작 UI,
    손가락 커서, 미친 듯이 오르는 숫자, SSR 뽑기, 가짜 선택지, 가짜 랭킹까지
    클리셰를 8컷에 몰아 넣었다. 화면에 나오는 좀비·보스·무기·로봇은 전부 게임에
    실제로 들어 있는 스프라이트고, 소리도 게임의 BGM과 효과음이다.
  </p>
  <ul class="specs">
    <li>컷 8개</li>
    <li>프레임 450장</li>
    <li>게임 스프라이트 140여 장</li>
    <li>효과음 큐 27개</li>
  </ul>

  <div class="stage">
    <div class="phone">
      <video id="pv" controls playsinline preload="metadata"
             src="data:video/mp4;base64,{vid}"></video>
    </div>
    <div class="aside">
      <h2>세로로 만든 이유</h2>
      <p>
        이 장르는 <b>비율 자체가 신호</b>다. 9:16 세로 화면에 조이스틱과 스킬 버튼이
        깔려 있으면, 내용을 보기도 전에 "모바일 게임 광고"로 읽힌다. 흑백 TV 광고가
        4:3 필러박스만으로 시대를 알려 주던 것과 같은 장치다.
      </p>
      <p>
        가로가 필요하면 이 영상을 16:9 한가운데 놓고 양옆을 블러로 채우면 된다.
        실제 광고들이 유튜브에 올릴 때 쓰는 방식이라 오히려 더 그럴듯해진다.
      </p>
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
           보스 포효 36프레임, 보스 폭사 60프레임, 레벨업 24프레임, 무기 12종,
           머리 12종, 그리고 <b>UI 아트로 조립한 가짜 조작 화면</b>.</p>
        <p>새로 그린 건 손가락 커서와 등급 카드뿐이고, 둘 다 코드로 그렸다.</p>
      </div>
      <div class="note">
        <h3>자막 3겹</h3>
        <p>이 장르의 서명은 자막이다. <b>굵은 검은 외곽선 → 흰 테두리 → 금색
           그라데이션 속살</b>, 이 세 겹을 겹쳐야 그 느낌이 난다. 한 겹이라도 빠지면
           그냥 평범한 글씨가 된다.</p>
      </div>
      <div class="note">
        <h3>후처리</h3>
        <p>채도 +34%, 대비 살짝, 밝은 부분만 뽑아 번지게 하는 블룸, 그리고 임팩트
           15곳마다 <b>줌 펀치와 화면 흔들림</b>. 컷이 바뀔 때는 흰 플래시로 넘긴다.</p>
        <p>소리도 같은 방향이다. 저역을 올리고 세게 눌러 붙인 뒤 <code>loudnorm=I=-13</code>
           으로 방송보다 높게 끌어올렸다.</p>
      </div>
      <div class="note">
        <h3>솔직한 면책</h3>
        <p>마지막 2초에 깔리는 초소형 글씨는 이 장르의 필수 요소이자, 여기서는
           진짜 고지다. <b>랭킹·리뷰·100연차·선착순은 전부 연출</b>이고 게임에
           존재하지 않는다.</p>
        <p>반대로 <b>무기 65종은 사실</b>이다.</p>
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
    <span>※ 실제 게임 화면과 다를 수 있습니다</span>
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
