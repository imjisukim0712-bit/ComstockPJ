# ComfyUI 배경 플레이트 만들기 (세로 쇼츠용)

`render_shorts.py --bg <영상>` 에 넣을 **배경 플레이트**를 ComfyUI로 만드는 절차다.
플레이트는 세로 화면에서 TV 밴드 뒤에 깔리는 배경이고, 게임 스프라이트와 절대 섞이지 않는다.

## 원칙 — 스프라이트는 ComfyUI에 넣지 않는다

로봇·좀비·무기·UI는 전부 `render_shorts.py`가 **원본 픽셀 그대로** 그린다.
ComfyUI는 배경만 담당한다. 이유는 이 프로젝트가 이미 기록해 둔 것이다:

> 픽셀아트는 회전·비정수 배율 금지 — `rotate`는 형태를 지우고, 0.9배는 픽셀을
> 들쭉날쭉하게 만든다. (프로젝트 안내.md)

영상 확산 모델은 픽셀 그리드를 전혀 존중하지 않는다. 4색 양자화 + 1px 어두운
외곽선이라는 이 게임의 그림 문법은 i2v를 한 번 통과하면 남지 않는다.
**게임 아트를 시드로 넣는 것도 권하지 않는다** — 뭉개진 픽셀아트는 깨끗한 AI 배경보다
나쁘다. 톤만 맞춘 새 그림을 뽑는 편이 낫다.

## 만들 것은 5초 클립 하나뿐이다

`encode_final`이 ffmpeg `-stream_loop -1`로 배경을 반복 재생한다.
쇼츠가 18.8초라고 18.8초를 만들 필요가 없다 — **5초짜리 하나면 끝난다.**
12~16GB VRAM에서 충분히 나오는 분량이다.

### 해상도는 낮아도 된다

플레이트는 (1) TV 밴드(960×720)에 가운데를 가려지고, (2) 기본값에서 채도 0 ·
밝기 −0.14로 눌린다. 즉 **어두운 흑백 배경**으로 깔린다. 디테일이 살아날 자리가 없다.

- 권장 생성 해상도: **540×960** (9:16). 넉넉하면 720×1280.
- ffmpeg이 `scale=...:force_original_aspect_ratio=increase, crop=1080:1920` 으로
  올려 자르므로 **세로(9:16)로 뽑는 것이 중요하다.** 가로 16:9로 뽑으면 좌우가
  크게 잘려 의도한 구도가 남지 않는다.
- 길이 5초, 16~24fps.

## PC에서의 절차

이 저장소를 **PC의 Claude Code**에서 열고 진행한다. 원격 세션에는 GPU도
ComfyUI도 없어서 comfy-mcp 도구가 전부 실패한다.

comfy-mcp의 표준 흐름을 그대로 따른다:

1. `server_info` — 로컬 ComfyUI가 떠 있는지, 코어/노드팩이 낡지 않았는지 확인.
   안 떠 있으면 `launch_comfyui`.
2. `search_templates(query="image to video", exclude_api=True)` —
   **`exclude_api=True`가 중요하다.** `API` 태그가 붙은 템플릿은 유료 호스팅
   모델이라 크레딧을 쓴다. 무료 로컬 실행만 남긴다.
3. `fetch_template(...)` — 워크플로 JSON을 파일로 저장. 반환값의 `local_check`가
   **통과했는지 반드시 확인한다**. `runnable: false`면 빠진 노드팩을
   `install_node`로 깔거나 다른 템플릿을 고른다. `checked: false`는 "판정 못 함"이지
   합격이 아니므로 `validate_workflow`를 직접 돌린다.
4. `list_workflow_slots` → `set_workflow_slot` — 프롬프트/시드/스텝/해상도를 넣는다.
5. `run_workflow(wait=False)` → `job(action="wait")` → `fetch_outputs`.
   영상 생성은 오래 걸리므로 `wait=True`로 막지 않는다.

### 모델 선택은 실제 목록을 보고 정한다

여기에 특정 모델 이름을 못 박아 두지 않았다. 설치된 ComfyUI 버전과 노드팩에 따라
돌릴 수 있는 템플릿이 다르고, 2번 단계의 `search_templates`가 **지금 이 PC에서
실제로 돌아가는 목록**을 돌려주기 때문이다. 기억에 의존해 모델명을 적어 두면
없는 걸 받으러 가게 된다.

12~16GB에서의 원칙만 적어 둔다:
- 양자화(GGUF/fp8) 빌드를 우선 고른다.
- 낮은 해상도로 뽑고 필요하면 나중에 올린다 (위에 적었듯 올릴 필요도 거의 없다).
- OOM은 노드 오류가 아니라 **ComfyUI 프로세스가 통째로 죽는 것**으로 나타난다
  (연결 끊김/타임아웃). 그때는 `get_logs`로 로그를 읽는다.

## 워크플로 JSON을 미리 넣어두지 않은 이유

ComfyUI의 API 포맷 JSON은 **설치된 노드 클래스명과 체크포인트 파일명을 그대로
박아 넣는다.** 그 PC에 뭐가 깔려 있는지 모르는 상태로 미리 써 두면 `validate_workflow`에서
`class_type` 불일치로 떨어지고, 고치는 시간이 템플릿을 받아 슬롯만 바꾸는 것보다
길어진다. 위 3~4번 절차가 설치 상태에 맞춰 자동으로 맞는 그래프를 가져온다.

## 프롬프트

톤 기준: 게임의 배경은 폐허가 된 도시(`ground_ruined_city_v2.png`)이고,
최종 영상은 옛날 미국 흑백 TV 광고다. 배경은 **어둡고, 느리고, 비어 있어야** 한다 —
가운데를 TV가 가리므로 구도의 주인공은 위아래 여백이다.

### 1. 폐허 도시 (기본 추천)

```
a ruined city skyline at dusk, collapsed concrete buildings, thick drifting smoke,
ash falling slowly, empty streets, heavy overcast sky, desaturated, cinematic,
volumetric haze, slow subtle camera drift upward, no people, no text
```
부정 프롬프트:
```
people, characters, robots, text, watermark, logo, bright colors, fast motion,
camera shake, close-up, cluttered foreground
```

### 2. 하늘/재 (가장 안전 — 실패해도 티가 안 난다)

```
low angle view of a heavy overcast sky, slow moving dark clouds, falling ash,
faint distant smoke columns, monochrome mood, cinematic, very slow drift,
no people, no text
```

### 3. 실내 (TV가 방에 놓인 느낌)

```
a dim empty room at night, old wallpaper, dust motes floating in a single shaft
of light, static camera, vintage 1960s interior, monochrome mood, no people, no text
```

**`no people`, `no text`를 반드시 넣는다.** 사람이 들어오면 게임 캐릭터와 충돌하고,
글자가 들어오면 세로 자막과 겹쳐 읽힌다.

**빠른 움직임을 피한다.** 배경이 빠르면 가운데 TV의 픽셀아트와 속도가 안 맞아
따로 논다. `slow`, `subtle`, `static camera` 를 넣는다.

## 만든 뒤

```
python render_shorts.py --lang ko --bg comfy/plates/ruined_city.mp4
python render_shorts.py --lang en --bg comfy/plates/ruined_city.mp4
```

컬러를 그대로 살리고 싶으면 `--bg-color`를 붙인다(기본은 흑백으로 깎는다).
배경 없이 검은 화면으로도 완성된다 — `--bg`를 생략하면 된다.

플레이트 영상 파일은 `comfy/plates/`에 둔다. `.gitattributes`가 `*.mp4`를 LFS로
보내지 않으므로 용량이 크면 저장소에 넣기 전에 한 번 생각할 것.
