using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 상단에 현재 점수를 실시간으로 보여준다(2026-08-19 Phase B - 점수 시스템 HUD).
///
/// 씬에 배치하지 않고 <see cref="EnsureAttached"/>가 Canvas 밑에 코드로 만들어 붙인다
/// (SettingsIconUI·DashGaugeUI와 같은 관례 - 씬 수정 0건). 설정(톱니바퀴) 아이콘 바로 왼쪽에
/// 붙는 작은 라벨 하나뿐이라 별도 배경판 없이 텍스트만 그린다.
///
/// <b>자기 자신이 붙은 GameObject를 절대 끄지 않는다</b>(SettingsIconUI 2026-08-19 주석과 동일한
/// 이유) - 숨길 때는 자식 <see cref="content"/>만 끈다.
/// </summary>
public class ScoreHudUI : MonoBehaviour
{
    // 설정 아이콘(0.930~0.985)의 왼쪽에 붙인다. 캔버스가 ConstantPixelSize라 정규화 앵커 +
    // offset 0으로만 배치한다(2026-08-18 HUD 재배치에서 확립된 규칙).
    private static readonly Vector2 AnchorMin = new Vector2(0.760f, 0.905f);
    private static readonly Vector2 AnchorMax = new Vector2(0.925f, 0.965f);

    private GameObject content;
    private TextMeshProUGUI label;

    public static ScoreHudUI EnsureAttached(RectTransform canvasRoot)
    {
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentInChildren<ScoreHudUI>(true);
        if (existing != null) return existing;

        var go = new GameObject("ScoreHud", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        go.layer = canvasRoot.gameObject.layer;
        Stretch((RectTransform)go.transform, Vector2.zero, Vector2.one);

        var ui = go.AddComponent<ScoreHudUI>();
        ui.Build((RectTransform)go.transform);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        contentGo.transform.SetParent(rootRect, false);
        contentGo.layer = rootRect.gameObject.layer;
        content = contentGo;
        Stretch((RectTransform)contentGo.transform, AnchorMin, AnchorMax);

        label = contentGo.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 6f;
        label.fontSizeMax = 30f;
    }

    private void LateUpdate()
    {
        if (content == null) return;

        // IsFullScreenPanelOpen: 정비·상점 패널이 실제로 켜져 있으면 무조건 숨긴다.
        // IsIntermission 플래그만 보면 플래그와 화면이 어긋난 순간 이 라벨이 상점 오른쪽 위
        // [Next Wave] 버튼 위에 겹쳐 그려진다(2026-08-26 사용자 리포트 "상점에서 점수랑
        // 다음 웨이브랑 겹쳐있음"). 자세한 내용은 GameFlowManager.IsFullScreenPanelOpen 주석.
        bool visible = !GameFlowManager.IsIntermission
                       && !GameFlowManager.IsFullScreenPanelOpen
                       && !GameOverManager.IsGameOver
                       && !GameWinManager.IsGameWon
                       && !(PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen);

        if (content.activeSelf != visible) content.SetActive(visible);
        if (!visible) return;

        if (label != null) label.text = Loc.T("hud.score", RunScore.ComputeTotal().ToString("N0"));
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
