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

        // 바탕(빈 게이지) - 아직 채워지지 않은 구간이 밝게 보이도록 흰색에 가깝게 둔다.
        Image bg = CreateImage(root, "Background", new Color(0.93f, 0.94f, 0.93f, 0.95f));
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
        fill_image = CreateImage(root, "Fill", new Color(0.42f, 0.80f, 0.55f, 1f));
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
        label = CreateText(root, "Label", "DASH");
    }

    private void LateUpdate()
    {
        if (player == null || root == null) return;

        // 정비/상점/AI 코어 화면과 게임오버 중에는 숨긴다. 런타임 생성 오브젝트라
        // GameFlowManager.combatHudObjects에 등록할 수 없어 직접 확인한다.
        bool visible = !GameFlowManager.IsIntermission && !GameOverManager.IsGameOver
                       && !GameWinManager.IsGameWon && !player.IsDead;

        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // 1유닛이 화면에서 몇 픽셀인지(직교 카메라). 이 값으로 막대 크기를 잡으면
        // 해상도·줌이 바뀌어도 캐릭터 대비 비율이 유지된다.
        float pixelsPerUnit = cam.orthographic && cam.orthographicSize > 0f
            ? Screen.height / (2f * cam.orthographicSize)
            : 100f;

        root.sizeDelta = new Vector2(WorldWidth * pixelsPerUnit, WorldHeight * pixelsPerUnit);

        Vector3 worldPoint = player.transform.position + new Vector3(0f, WorldOffsetY, 0f);
        Vector3 screenPoint = cam.WorldToScreenPoint(worldPoint);
        root.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y);

        // 게이지는 "쓸 수 있는 정도" = 쿨다운의 반대값. 쿨다운이 없는 머리(팬봇)는 항상 가득이다.
        float ready = 1f - Mathf.Clamp01(player.DashCooldownRatio);
        fill_rect.anchorMax = new Vector2(ready, 1f);

        // 다 찼을 때만 글자를 또렷하게 - 차오르는 중에는 살짝 죽여 "아직 못 쓴다"를 알린다.
        if (label != null)
        {
            label.color = ready >= 0.999f ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        }
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
