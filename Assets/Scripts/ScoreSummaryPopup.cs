using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 마지막(보스) 웨이브를 처음 클리어했을 때 뜨는 점수 정산 팝업(2026-08-19 Phase C, 엔드리스 모드).
/// 점수 내역(웨이브/처치/AI 코어 레벨/골드/악세사리)과 총점을 보여주고 "계속 진행"(엔드리스로
/// 이어감) / "타이틀로"(점수 제출 후 종료) 2택을 받는다.
///
/// 씬에 배치하지 않고 <see cref="EnsureAttached"/>가 Canvas 밑에 코드로 만들어 붙인다
/// (PauseMenuUI·EquipmentDetailPopup과 같은 관례 - 씬 수정 0건).
///
/// <b>자기 자신이 붙은 GameObject를 절대 끄지 않는다</b> - 숨길 때는 자식 <see cref="content"/>만
/// 끈다(DashGaugeUI·PauseMenuUI가 2026-08-19에 겪은 "루트를 끄면 스스로 다시 켤 수 없다" 함정과
/// 같은 예방 조치. 다만 이 팝업은 껐다가 다시 켤 일이 없어 실질적인 위험은 없지만 관례를 맞춘다).
/// </summary>
public class ScoreSummaryPopup : MonoBehaviour
{
    private GameObject content;
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI breakdownText;
    private Button continueButton;
    private Button declineButton;

    public static ScoreSummaryPopup EnsureAttached(RectTransform canvasRoot)
    {
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentInChildren<ScoreSummaryPopup>(true);
        if (existing != null) return existing;

        var go = new GameObject("ScoreSummaryPopup", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);

        var ui = go.AddComponent<ScoreSummaryPopup>();
        ui.Build((RectTransform)go.transform);
        return ui;
    }

    private void Build(RectTransform root)
    {
        Stretch(root);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(root, false);
        content = contentGo;
        Stretch((RectTransform)contentGo.transform);

        // 2026-08-20 사용자 요청("빅토리 뜨면서 화면 가리는거 없애") - 예전에는 여기 전체 화면을
        // 75% 검게 덮는 DimBackground가 있어서, 승리 직후 필드(쓰러진 보스·HUD)가 화면에서 완전히
        // 가려졌다. 정산 패널(아래 Panel)만 남기고 배경 암막은 없앤다 - 게임 화면이 그대로 보이는
        // 채로 점수 내역과 계속/타이틀로 버튼만 그 위에 뜬다.
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(contentGo.transform, false);
        var panelRect = (RectTransform)panelGo.transform;
        panelRect.anchorMin = new Vector2(0.28f, 0.16f);
        panelRect.anchorMax = new Vector2(0.72f, 0.84f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.97f);

        headerText = CreateLabel(panelRect, "Header", "", 0.06f, 0.86f, 0.94f, 0.96f, 40f);
        breakdownText = CreateLabel(panelRect, "Breakdown", "", 0.10f, 0.30f, 0.90f, 0.84f, 30f);
        breakdownText.alignment = TextAlignmentOptions.TopLeft;
        // 점수 내역은 줄 수가 고정돼 있고 숫자가 잘리면 안 되므로, 칸에 맞춰 줄어드는
        // 자동 크기 대신 고정 크기 + 줄바꿈 허용으로 바꾼다(ApplyTextSizing의 Ellipsis는
        // 여러 줄 표에서 뒷줄이 통째로 잘려나갈 수 있다).
        breakdownText.enableAutoSizing = false;
        breakdownText.fontSize = 26f;
        breakdownText.overflowMode = TextOverflowModes.Overflow;

        continueButton = CreateButton(panelRect, "ContinueButton", Loc.T("score.continue"), 0.10f, 0.155f, 0.90f, 0.26f);
        declineButton = CreateButton(panelRect, "DeclineButton", Loc.T("common.to_title"), 0.10f, 0.04f, 0.90f, 0.145f);

        content.SetActive(false);
    }

    /// <summary>정산 팝업을 띄운다. onContinue/onDecline은 버튼을 누른 뒤 정확히 한 번만 호출된다
    /// (버튼을 누르면 팝업을 먼저 닫고 콜백을 부른다 - 콜백 쪽에서 씬을 전환하거나 다음 화면을
    /// 여는 동안 이 팝업이 화면에 남아있지 않게 하려는 것).</summary>
    public void ShowClearChoice(int clearedWave, System.Action onContinue, System.Action onDecline)
    {
        RunScore.Breakdown b = RunScore.ComputeBreakdown();

        if (headerText != null) headerText.text = Loc.T("score.wave_cleared", clearedWave);

        if (breakdownText != null)
        {
            breakdownText.text =
                // 2026-08-20: WaveNumber/CoreLevel은 "현재 값"(1부터 시작)이라 그대로 곱하면
                // 시작하자마자 기본점수가 붙는다(RunScore.ComputeBreakdown 참고) - 실제 계산과
                // 똑같이 1을 뺀 값을 곱셈식으로 보여준다.
                $"{Loc.T("score.line.wave")} {Mathf.Max(0, RunState.WaveNumber - 1)} x {RunScore.WaveWeight}  = {b.WaveScore:N0}\n" +
                $"{Loc.T("score.line.kills")} {RunScore.KillCount} x {RunScore.KillWeight}    = {b.KillScore:N0}\n" +
                $"{Loc.T("score.line.corelevel")} {Mathf.Max(0, RunState.CoreLevel - 1)} x {RunScore.CoreLevelWeight}  = {b.CoreLevelScore:N0}\n" +
                $"{Loc.T("score.line.gold")} {RunState.Gold} x {RunScore.GoldWeight}    = {b.GoldScore:N0}\n" +
                $"{Loc.T("score.line.accessory")} = {b.AccessoryScore:N0}\n\n" +
                $"<b>{Loc.T("score.total", b.Total.ToString("N0"))}</b>\n\n" +
                Loc.T("score.endless_note");
        }

        continueButton.onClick.RemoveAllListeners();
        continueButton.onClick.AddListener(() => { content.SetActive(false); onContinue?.Invoke(); });

        declineButton.onClick.RemoveAllListeners();
        declineButton.onClick.AddListener(() => { content.SetActive(false); onDecline?.Invoke(); });

        content.SetActive(true);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateLabel(RectTransform parent, string name, string text,
                                               float xMin, float yMin, float xMax, float yMax, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(label, fontSize);
        return label;
    }

    private static Button CreateButton(RectTransform parent, string name, string label,
                                       float xMin, float yMin, float xMax, float yMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        Sprite buttonSprite = Resources.Load<Sprite>("UI/Purple_button00");
        if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; img.color = Color.white; }
        else img.color = new Color(0.33f, 0.29f, 0.55f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 4f);
        textRect.offsetMax = new Vector2(-4f, -4f);

        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, 30f);

        return button;
    }
}
