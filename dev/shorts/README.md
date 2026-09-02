# ANKARA COMSTOCK 숏츠

- 최종 수정 영상: `output/Ankara_Comstock_FullHead_1080x1920.mp4`
- 움직임 미리보기: `output/Ankara_Comstock_FullHead_MovementPreview_1080x1920.mp4`
- 대표 이미지: `output/Ankara_Comstock_FullHead_Close_1080x1920.png`
- 규격: 1080×1920, 23.976fps, 26.49초, H.264 + AAC
- 원본: <https://www.youtube.com/watch?v=AlnHNi0hdO0>
- 합성 자산: `Assets/Resources/Heads/ComstockMk01.png`, `Assets/Resources/UI/title_logo.png`

최종 수정본은 `render_ankara_comstock_v5.py`가 만든다. 메시 머리의 실제 픽셀 이동·크기·기울기를
프레임마다 광학 흐름으로 추적하고, 세로 확대 카메라는 별도 데드존과 관성을 적용해 늦게 따라간다.
따라서 메시가 화면 안에서 달리고 흔들리며 합성 얼굴도 머리에 붙어 함께 움직인다. 원본 머리카락·두상·목·몸은
남긴다. 임의로 눈·입을 그리지 않고 **게임 원본 `ComstockMk01.png` 전체를 직접 사용**한다.
상단 구멍, 원통 옆면, 양쪽 귀, 눈·입, 둥근 하단 외곽선을 하나도 자르지 않는다. 위치·크기·회전과
장면 조명만 메시 머리에 맞추며 원본 알파 실루엣은 그대로 보존한다.

소스의 4.45초 이전은 메시 얼굴이 몇 픽셀에 불과한 원거리/디졸브 구간이라 큰 얼굴을 강제로
붙이지 않는다. 클로즈업부터 합성을 시작하고, 득점 뒤에는 광고판 뒤쪽으로 달리는 메시의 경로를
별도 키로 보정해 앞선 동료 선수에게 트래커가 옮겨가지 않게 했다.

오디오는 렌더 과정에서 다루지 않는다. 무음 영상이 완성된 뒤 원본 AAC 스트림을 `-c:a copy`로
그대로 결합한다. 원본과 최종본의 오디오 비트스트림 MD5는 모두
`1e6fe99b956595d104f87f1d767cc7a0`이다.

실행:

```powershell
$env:PYTHONPATH=(Resolve-Path 'dev\shorts\_vendor').Path
python 'dev\shorts\render_ankara_comstock_v5.py'
```
