using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 발밑에 붙어 따라다니는 구르기(DASH) 게이지 바.
///
/// 2026-08-19 사용자 요청으로 <b>우하단 버튼형 아이콘 → 캐릭터 밑 게이지 바</b>로 교체했다.
/// 우하단 아이콘은 화면 반대쪽 구석이라 전투 중 시선이 캐릭터에 있을 때 쿨다운을 볼 수 없었다.
/// 게이지가 가득 차면 구를 수 있고, 구르면 비었다가 서서히 채워진다
/// (<see cref="PlayerRobotController.DashCooldownRatio"/>의 반대값).
/// 2026-08-19 사용자 요청으로 <b>완충되면 게이지 자체가 사라지고, 쿨타임(충전 중)일 때만
/// 보인다</b>(항상 떠 있던 이전 방식 대체).
///
/// <b>왜 월드 오브젝트가 아니라 캔버스 UI인가</b> — "DASH" 글자가 또렷해야 하고 막대 아트가
/// 9-슬라이스라, 적 체력바처럼 SpriteRenderer로 만들면 글자를 위해 별도 3D 텍스트를 얹어야 한다.
/// 대신 매 프레임 캐릭터의 월드 좌표를 화면 좌표로 바꿔 위치를 맞춘다. 크기도 카메라에서
/// 역산하므로(1유닛당 픽셀 수) 해상도가 바뀌어도 캐릭터 대비 비율이 유지된다.
///
/// 씬에 배치하지 않고 <see cref="Attach"/>가 코드로 만들어 붙인다. 그래서
/// <c>GameFlowManager.combatHudObjects</c>(씬 오브젝트 배열)에 등록할 수 없어 정비/상점 중
/// 숨김은 <see cref="GameFlowManager.IsIntermission"/>을 스스로 확인한다
/// (<see cref="PlayerHitFeedback"/>와 같은 관례).
/// </summary>
public class DashGaugeUI : MonoBehaviour
{
    // ── 배치/크기 (월드 유닛 기준. 화면 픽셀은 카메라에서 역산한다) ────────────────
    [Tooltip("캐릭터 원점(발밑)에서 아래로 내리는 거리(월드 유닛)")]
    private const float WorldOffsetY = -0.42f;

    [Tooltip("막대 폭(월드 유닛). 캐릭터 몸통 폭(약 1.1)보다 살짝 넓게 잡아 눈에 띄게 한다")]
    private const float WorldWidth = 1.55f;

    [Tooltip("막대 높이(월드 유닛)")]
    private const float WorldHeight = 0.30f;

    private PlayerRobotController player;
    private Camera cam;
    private RectTransform root;
    private GameObject visual; // 배경/채움/글자를 묶는 자식 - 숨길 때 이 오브젝트만 끈다
    private RectTransform fill_rect;
    private Image fill_image;
    private TextMeshProUGUI label;

    /// <summary>
    /// 캔버스 아래에 게이지를 만든다. 캔버스가 ConstantPixelSize라 위치·크기를 픽셀로 직접
    /// 계산해 넣는다(이 컨트롤만은 캐릭터를 따라다녀야 하므로 정규화 앵커를 쓸 수 없다).
    /// </summary>
    public static DashGaugeUI Attach(RectTransform parent, PlayerRobotController player)
    {
        if (parent == null || player == null) return null;

        var go = new GameObject("DashGauge", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var ui = go.AddComponent<DashGaugeUI>();
        ui.player = player;
        ui.Build((RectTransform)go.transform);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        root = rootRect;

        // 캐릭터를 따라다니므로 앵커는 화면 좌하단 고정 + anchoredPosition으로 옮긴다.
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(0.5f, 0.5f);

        // <b>캔버스의 맨 첫 자식으로 내린다</b>(2026-08-23 버그 수정). uGUI는 형제 순서가 곧
        // 그리기 순서라, 코드로 만들어 붙이면 항상 <b>맨 뒤 = 맨 위</b>가 된다 - 그래서 이 게이지가
        // 설정창·일시정지 메뉴·점수 정산 팝업처럼 나중에 열리는 패널들 위로 뚫고 올라왔다.
        // 첫 자식으로 내리면 캔버스 안의 다른 UI가 전부 이 위에 그려지므로, 앞으로 어떤 패널이
        // 추가돼도 같은 문제가 재발하지 않는다(개별 패널마다 숨김 조건을 늘리는 것보다 안전하다).
        // 캔버스가 Screen Space - Overlay라 첫 자식이어도 게임 월드보다는 항상 위에 그려진다.
        root.SetAsFirstSibling();

        // 배경/채움/글자는 전부 이 자식 하나에 묶는다 - 숨길 때 root가 아니라 이 오브젝트만
        // 꺼야 한다(아래 LateUpdate 주석 참고).
        var visualGo = new GameObject("Visual", typeof(RectTransform));
        visualGo.transform.SetParent(root, false);
        visual = visualGo;
        RectTransform visualRect = (RectTransform)visualGo.transform;
        Stretch(visualRect, Vector2.zero, Vector2.one);

        // 바탕(빈 게이지) - 아직 채워지지 않은 구간이 밝게 보이도록 흰색에 가깝게 둔다.
        Image bg = CreateImage(visualRect, "Background", new Color(0.93f, 0.94f, 0.93f, 0.95f));
        Stretch((RectTransform)bg.transform, Vector2.zero, Vector2.one);
        Sprite bgArt = Resources.Load<Sprite>("UI/Black_ui04");
        if (bgArt != null)
        {
            bg.sprite = bgArt;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.90f, 0.91f, 0.90f, 0.95f);
            // 막대가 원본 아트보다 훨씬 납작해서 9-슬라이스 테두리가 가운데를 다 먹는다 -
            // 테두리가 그려지는 크기를 줄여야 가운데가 살아난다(2026-08-13 체력 바와 같은 함정).
            bg.pixelsPerUnitMultiplier = 3f;
        }
        bg.raycastTarget = false;

        // 채움 - 왼쪽 고정, 오른쪽만 늘어난다(anchorMax.x로 조절하므로 9-슬라이스가 유지된다).
        fill_image = CreateImage(visualRect, "Fill", new Color(0.42f, 0.80f, 0.55f, 1f));
        fill_rect = (RectTransform)fill_image.transform;
        fill_rect.anchorMin = Vector2.zero;
        fill_rect.anchorMax = Vector2.one;
        fill_rect.offsetMin = Vector2.zero;
        fill_rect.offsetMax = Vector2.zero;
        Sprite fillArt = Resources.Load<Sprite>("UI/Green_bar00");
        if (fillArt != null)
        {
            fill_image.sprite = fillArt;
            fill_image.type = Image.Type.Sliced;
            fill_image.color = Color.white;
            fill_image.pixelsPerUnitMultiplier = 3f;
        }
        fill_image.raycastTarget = false;

        // "DASH" 글자는 채움 위에 얹혀야 게이지가 줄어도 계속 읽힌다(형제 순서 = 그리기 순서).
        label = CreateText(visualRect, "Label", "DASH");
    }

    private void LateUpdate()
    {
        if (player == null || root == null) return;

        // 정비/상점/AI 코어 화면과 게임오버 중에는 숨긴다. 런타임 생성 오브젝트라
        // GameFlowManager.combatHudObjects에 등록할 수 없어 직접 확인한다.
        //
        // <b>2026-08-19 버그 수정</b>: 예전에는 여기서 숨길 때 `root.gameObject.SetActive(false)`로
        // "이 스크립트가 붙어 있는 바로 그 오브젝트"를 껐다. Unity는 비활성 오브젝트의
        // LateUpdate를 아예 호출하지 않으므로, 한 번 숨은 뒤에는 자기 자신을 다시 켤 기회가
        // 영원히 없었다(웨이브 1 종료 후 정비 화면에서 숨은 채로 다음 웨이브가 시작돼도 계속
        // 사라져 있던 버그의 원인). 이제 root는 항상 켜 둔 채, 배경/채움/글자를 묶은 자식
        // <see cref="visual"/>만 껐다 켠다 - root의 LateUpdate는 항상 돌아가야 스스로 다시
        // 보여줄 수 있다.
        // 일시정지(= 설정창을 여는 경로)는 IsIntermission이 아니라서 따로 확인한다 -
        // 멈춰 있는 화면 뒤에 게이지가 남아 있을 이유가 없다(그리기 순서는 위 SetAsFirstSibling이
        // 이미 보장하므로, 이건 정확성을 위한 추가 조건이다).
        bool paused = PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen;

        bool scene_visible = !GameFlowManager.IsIntermission && !GameOverManager.IsGameOver
                       && !GameWinManager.IsGameWon && !player.IsDead && !paused;

        if (cam == null) cam = Camera.main;

        // 게이지는 "쓸 수 있는 정도" = 쿨다운의 반대값. 쿨다운이 없는 머리(팬봇)는 항상 가득이다.
        float ready = 1f - Mathf.Clamp01(player.DashCooldownRatio);

        // 사용자 요청(2026-08-19): 완충되면 게이지를 아예 숨기고, 쿨타임(충전 중)일 때만 보인다.
        bool visible = scene_visible && cam != null && ready < 0.999f;

        if (visual != null && visual.activeSelf != visible) visual.SetActive(visible);
        if (!visible) return;

        // 1유닛이 화면에서 몇 픽셀인지(직교 카메라). 이 값으로 막대 크기를 잡으면
        // 해상도·줌이 바뀌어도 캐릭터 대비 비율이 유지된다.
        float pixelsPerUnit = cam.orthographic && cam.orthographicSize > 0f
            ? Screen.height / (2f * cam.orthographicSize)
            : 100f;

        root.sizeDelta = new Vector2(WorldWidth * pixelsPerUnit, WorldHeight * pixelsPerUnit);

        Vector3 worldPoint = player.transform.position + new Vector3(0f, WorldOffsetY, 0f);
        Vector3 screenPoint = cam.WorldToScreenPoint(worldPoint);
        root.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y);

        fill_rect.anchorMax = new Vector2(ready, 1f);

        // 보이는 동안은 항상 "충전 중"이므로(다 차면 숨는다) 글자는 항상 살짝 죽인 상태로 둔다.
        if (label != null) label.color = new Color(1f, 1f, 1f, 0.55f);
    }

    // ── 생성 헬퍼 ────────────────────────────────────────────────────

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, string content)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, Vector2.zero, Vector2.one);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = content;
        text.alignment = TextAlignmentOptions.Midline;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 6f;
        text.fontSizeMax = 22f;
        return text;
    }
}
