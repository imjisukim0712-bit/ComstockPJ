using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 웨이브가 시작될 때 화면 중앙에 잠깐 떴다 사라지는 전환 배너("WAVE 08", 보스 웨이브면
/// "BOSS INCOMING"). `UI 기획서.pdf`의 "웨이브 전환 / 보스 등장 알림" 반영(2026-08-21).
///
/// 이 클래스는 <b>순수하게 보여주기만</b> 한다 - 정지(Time.timeScale)·재개 타이밍은
/// <see cref="GameFlowManager"/>가 <see cref="WaveManager.OnWaveStarted"/>를 구독해 직접
/// 맡는다(이 프로젝트의 timeScale 전환은 전부 GameFlowManager 한 곳에서만 일어난다 -
/// EnterPostWaveIntermission/HandleGameWon/HandleNextWaveRequested와 같은 원칙).
///
/// 씬에 미리 배치하지 않고 <see cref="EnsureAttached"/>가 Canvas 밑에 코드로 만들어 쓴다
/// (PauseMenuUI/ScoreSummaryPopup과 같은 관례).
/// </summary>
public class WaveTransitionBannerUI : MonoBehaviour
{
    private static readonly Color NormalBg = new Color(0.08f, 0.09f, 0.11f, 0.95f);
    private static readonly Color BossBg = new Color(0.16f, 0.04f, 0.04f, 0.95f);
    private static readonly Color BossRibbonColor = new Color(0.86f, 0.24f, 0.24f, 1f);

    private RectTransform root;
    private Image background;
    private GameObject ribbon;
    private TextMeshProUGUI normalLabel;

    /// <summary>Canvas 밑에 이미 있으면 그걸 돌려주고, 없으면 새로 만들어 붙인다.</summary>
    public static WaveTransitionBannerUI EnsureAttached(RectTransform canvasRoot)
    {
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentInChildren<WaveTransitionBannerUI>(true);
        if (existing != null) return existing;

        var go = new GameObject("WaveTransitionBanner", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);

        var banner = go.AddComponent<WaveTransitionBannerUI>();
        banner.Build((RectTransform)go.transform);
        return banner;
    }

    private void Build(RectTransform rootRect)
    {
        root = rootRect;
        root.anchorMin = new Vector2(0.26f, 0.40f);
        root.anchorMax = new Vector2(0.74f, 0.60f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bgGo.transform.SetParent(root, false);
        Stretch((RectTransform)bgGo.transform);

        background = bgGo.GetComponent<Image>();
        Sprite frame = Resources.Load<Sprite>("UI/Black_ui03");
        if (frame != null)
        {
            background.sprite = frame;
            background.type = Image.Type.Sliced;
        }
        background.color = NormalBg;
        background.raycastTarget = false;

        // 일반 웨이브 - 배경 위에 "WAVE 08" 문구만 중앙에 띄운다.
        normalLabel = CreateText(root, "WaveLabel", new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));
        normalLabel.text = "WAVE 01";

        // 보스 웨이브 - 배경 안쪽에 별도의 빨간 리본을 얹고 그 위에 경고 문구를 띄운다
        // (기획서 시안처럼 카드 자체보다 리본이 한 겹 더 강조되는 형태).
        ribbon = new GameObject("BossRibbon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ribbon.transform.SetParent(root, false);
        var ribbonRect = (RectTransform)ribbon.transform;
        ribbonRect.anchorMin = new Vector2(0.04f, 0.28f);
        ribbonRect.anchorMax = new Vector2(0.96f, 0.72f);
        ribbonRect.offsetMin = Vector2.zero;
        ribbonRect.offsetMax = Vector2.zero;

        Image ribbonImage = ribbon.GetComponent<Image>();
        if (frame != null)
        {
            ribbonImage.sprite = frame;
            ribbonImage.type = Image.Type.Sliced;
        }
        ribbonImage.color = BossRibbonColor;
        ribbonImage.raycastTarget = false;

        TextMeshProUGUI ribbonLabel = CreateText((RectTransform)ribbon.transform, "Label",
            new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
        ribbonLabel.text = "⚠ BOSS INCOMING ⚠";

        ribbon.SetActive(false);
        root.gameObject.SetActive(false);
    }

    /// <summary>배너를 채우고 보여준다. isBossWave면 경고 색/문구로 바뀐다.</summary>
    public void Show(int waveNumber, bool isBossWave)
    {
        if (root == null) return;

        background.color = isBossWave ? BossBg : NormalBg;
        ribbon.SetActive(isBossWave);
        normalLabel.gameObject.SetActive(!isBossWave);
        if (!isBossWave) normalLabel.text = $"WAVE {waveNumber:00}";

        root.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, 64f);

        return text;
    }
}
