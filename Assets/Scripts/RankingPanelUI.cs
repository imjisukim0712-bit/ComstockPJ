using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 랭킹(엔드리스 점수) 목록 화면(2026-08-20). 타이틀 화면 우하단 버튼·일시정지 메뉴·게임오버
/// 요약 화면 세 곳에서 공통으로 연다. 씬에 배치하지 않고 <see cref="CollectionPanelUI"/>와 같은
/// 관례로 코드로 만든다(호출 시점 캔버스 아래에 붙었다가 닫으면 파괴된다).
///
/// 데이터는 <see cref="LeaderboardService.Current"/> 하나만 바라본다 - Firebase든 로컬이든
/// 이 화면은 신경 쓰지 않는다. <b>맵마다 랭킹이 분리되므로</b>(2026-08-20 - 앞으로 맵이 여러 개
/// 추가될 예정) 항상 mapId를 받아 그 맵의 랭킹만 보여준다.
/// </summary>
public class RankingPanelUI : MonoBehaviour
{
    private static readonly Color AccentColor = new Color(0.95f, 0.75f, 0.15f, 1f);

    private TextMeshProUGUI listText;
    private System.Action onClose;
    private string mapId;
    private bool destroyed;

    /// <summary>부모(보통 최상위 캔버스) 아래에 랭킹 화면을 만들어 돌려준다.
    /// <paramref name="mapId"/>는 보여줄 맵의 랭킹 키(보통 씬 이름)다.</summary>
    public static RankingPanelUI Attach(RectTransform parent, string mapId, System.Action onClose)
    {
        if (parent == null) return null;

        var root = new GameObject("RankingPanel", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        Stretch(rootRect, Vector2.zero, Vector2.one);
        rootRect.SetAsLastSibling(); // UI는 형제 순서가 곧 그리기 순서다

        var ui = root.AddComponent<RankingPanelUI>();
        ui.onClose = onClose;
        ui.mapId = string.IsNullOrEmpty(mapId) ? "Ground01" : mapId;
        ui.Build(rootRect);
        return ui;
    }

    private void OnDestroy() => destroyed = true;

    private void Build(RectTransform rootRect)
    {
        Image backdrop = CreateImage(rootRect, "Backdrop", Vector2.zero, Vector2.one, new Color(0.04f, 0.04f, 0.06f, 0.95f));
        backdrop.raycastTarget = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelGo.transform.SetParent(rootRect, false);
        var panelRect = (RectTransform)panelGo.transform;
        Stretch(panelRect, new Vector2(0.28f, 0.08f), new Vector2(0.72f, 0.92f));

        Image panelImg = panelGo.GetComponent<Image>();
        Sprite panelSprite = Resources.Load<Sprite>("UI/Black_ui04");
        if (panelSprite != null) { panelImg.sprite = panelSprite; panelImg.type = Image.Type.Sliced; panelImg.color = Color.white; }
        else panelImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

        TextMeshProUGUI title = CreateText(panelRect, "Title", new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.965f),
                                           TextAlignmentOptions.Midline, 36f);
        title.text = Loc.T("ranking.title", mapId); // 맵마다 랭킹이 갈리므로 어느 맵인지 항상 보여준다
        title.color = AccentColor;

        listText = CreateText(panelRect, "ListText", new Vector2(0.07f, 0.16f), new Vector2(0.93f, 0.87f),
                              TextAlignmentOptions.TopLeft, 26f);
        listText.text = Loc.T("common.loading");

        Button close = CreateButton(panelRect, "CloseButton", new Vector2(0.32f, 0.035f), new Vector2(0.68f, 0.115f), Loc.T("common.close"));
        close.onClick.AddListener(Close);

        LeaderboardService.Current.FetchTopScores(mapId, 20, OnFetched);
    }

    private void OnFetched(List<ScoreEntry> entries)
    {
        if (destroyed || listText == null) return;

        if (entries == null || entries.Count == 0)
        {
            listText.text = Loc.T("ranking.empty");
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < entries.Count; i++)
            sb.AppendLine(Loc.T("ranking.row", i + 1, entries[i].PlayerName, entries[i].Score.ToString("N0")));

        listText.text = sb.ToString();
    }

    public void Close()
    {
        onClose?.Invoke();
        Destroy(gameObject);
    }

    // ── UI 헬퍼 (CollectionPanelUI와 같은 관례) ──────────────────────────────────

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                              TextAlignmentOptions alignment, float maxSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        ItemCellUI.ApplyTextSizing(text, maxSize);
        return text;
    }

    private static Image CreateImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Button CreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        Sprite art = Resources.Load<Sprite>("UI/Purple_button00");
        if (art != null) { image.sprite = art; image.type = Image.Type.Sliced; }
        else image.color = new Color(0.30f, 0.24f, 0.52f, 1f);

        TextMeshProUGUI labelText = CreateText((RectTransform)go.transform, "Label", Vector2.zero, Vector2.one,
                                               TextAlignmentOptions.Midline, 24f);
        labelText.text = label;

        return go.AddComponent<Button>();
    }
}
