using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 도감(해금 목록) 화면(2026-08-19 Phase E). 타이틀 화면의 "도감" 버튼으로 연다.
///
/// <b>왜 씬이 아니라 코드로 만드나</b> — 항목 수(머리 12 / 디스크 21 / 악세사리 6)가 데이터에서
/// 오므로 씬에 칸을 미리 깔 수 없다. <see cref="HeadSelectPanelUI"/>와 같은 판단·같은 관례다
/// (정규화 앵커 + offset 0, 캔버스가 ConstantPixelSize라 절대 픽셀을 쓰면 다른 해상도에서 어긋난다).
///
/// 해금 여부와 진행도는 <see cref="UnlockState"/>가 유일한 출처이고, 이름·아이콘은 각
/// 카탈로그(파츠/상점/악세사리)에서 그때그때 조회한다 - 도감이 데이터를 따로 들고 있으면
/// 밸런스 조정 때 두 곳을 고쳐야 한다.
/// </summary>
public class CollectionPanelUI : MonoBehaviour
{
    private const int GridColumns = 5;

    private static readonly Color AccentColor = new Color(0.95f, 0.75f, 0.15f, 1f);
    private static readonly Color CellIdleColor = new Color(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Color LockedIconColor = new Color(0.10f, 0.10f, 0.12f, 1f);
    private static readonly Color MutedTextColor = new Color(0.72f, 0.74f, 0.78f, 1f);

    private sealed class CategoryTab
    {
        public UnlockCategory category;
        public Button button;
        public TextMeshProUGUI label;
    }

    private PartsCatalog partsCatalog;
    private ShopCatalog shopCatalog;
    private System.Action onClose;

    private readonly List<CategoryTab> tabs = new List<CategoryTab>();
    private RectTransform gridArea;

    private UnlockCategory currentCategory = UnlockCategory.Head;
    private int selectedItemId;

    private Image detailIcon;
    private TextMeshProUGUI detailName;
    private TextMeshProUGUI detailCondition;
    private TextMeshProUGUI detailProgress;
    private Image detailProgressFill;

    /// <summary>타이틀 캔버스 아래에 도감을 만들어 돌려준다.</summary>
    public static CollectionPanelUI Attach(RectTransform parent, PartsCatalog parts, ShopCatalog shop,
                                           System.Action onClose)
    {
        if (parent == null) return null;

        var root = new GameObject("CollectionPanel", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        Stretch(rootRect, Vector2.zero, Vector2.one);
        rootRect.SetAsLastSibling(); // UI는 형제 순서가 곧 그리기 순서다

        var ui = root.AddComponent<CollectionPanelUI>();
        ui.partsCatalog = parts;
        ui.shopCatalog = shop;
        ui.onClose = onClose;
        ui.Build(rootRect);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        Image backdrop = CreateImage(rootRect, "Backdrop", Vector2.zero, Vector2.one, new Color(0.04f, 0.04f, 0.06f, 0.95f));
        backdrop.raycastTarget = true;

        TextMeshProUGUI title = CreateText(rootRect, "Title", new Vector2(0.05f, 0.915f), new Vector2(0.95f, 0.985f),
                                           TextAlignmentOptions.Midline, 40f);
        title.text = "도감";
        title.color = AccentColor;

        BuildTabs(rootRect);

        var area = new GameObject("Grid", typeof(RectTransform));
        area.transform.SetParent(rootRect, false);
        gridArea = (RectTransform)area.transform;
        Stretch(gridArea, new Vector2(0.045f, 0.145f), new Vector2(0.615f, 0.812f));

        BuildDetail(rootRect);

        Button close = CreateButton(rootRect, "CloseButton", new Vector2(0.70f, 0.045f), new Vector2(0.955f, 0.115f),
                                    "닫기", out _);
        close.onClick.AddListener(Close);

        ShowCategory(UnlockCategory.Head);
    }

    private void BuildTabs(RectTransform rootRect)
    {
        var categories = new[] { UnlockCategory.Head, UnlockCategory.Disc, UnlockCategory.Accessory };

        const float x0 = 0.045f;
        const float x1 = 0.615f;
        const float gap = 0.012f;
        float width = (x1 - x0 - gap * (categories.Length - 1)) / categories.Length;

        for (int i = 0; i < categories.Length; i++)
        {
            UnlockCategory category = categories[i];
            float left = x0 + i * (width + gap);

            Button button = CreateButton(rootRect, $"Tab_{category}", new Vector2(left, 0.828f),
                                         new Vector2(left + width, 0.898f), string.Empty, out TextMeshProUGUI label);
            button.onClick.AddListener(() => ShowCategory(category));

            tabs.Add(new CategoryTab { category = category, button = button, label = label });
        }

        RefreshTabLabels();
    }

    private void RefreshTabLabels()
    {
        foreach (CategoryTab tab in tabs)
        {
            int unlocked = UnlockState.CountUnlocked(tab.category);
            int total = UnlockCatalog.GetByCategory(tab.category).Count;

            tab.label.text = $"{CategoryName(tab.category)}  {unlocked} / {total}";
            tab.label.color = tab.category == currentCategory ? AccentColor : Color.white;
        }
    }

    private void ShowCategory(UnlockCategory category)
    {
        currentCategory = category;
        RefreshTabLabels();
        BuildGrid();
    }

    private void BuildGrid()
    {
        // Destroy는 프레임 끝에 처리되므로 부모에서 먼저 떼어낸다 - 그러지 않으면 탭을 바꾼
        // 그 프레임에 이전 탭의 칸과 새 칸이 같은 자리에 겹쳐 그려지고, 자식 수를 세는 검증도
        // 이전 칸까지 함께 세게 된다(2026-08-19 실측으로 발견: 디스크 탭에서 21개가 아니라 33개).
        for (int i = gridArea.childCount - 1; i >= 0; i--)
        {
            Transform child = gridArea.GetChild(i);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }

        List<UnlockEntry> entries = UnlockCatalog.GetByCategory(currentCategory);
        if (entries.Count == 0) return;

        int rows = Mathf.CeilToInt(entries.Count / (float)GridColumns);
        const float gapX = 0.016f;
        const float gapY = 0.026f;

        // 칸 높이는 <b>최소 3행</b>을 가정해 나눈다(2026-08-19). 악세사리는 6종뿐이라 2행인데,
        // 남은 높이를 2행이 다 나눠 가지면 칸이 세로로 길게 늘어나 머리·디스크 탭과 생김새가
        // 달라진다. 3행보다 많으면 그대로 꽉 채우고, 적으면 위쪽부터 채우고 아래를 비운다.
        int heightRows = Mathf.Max(rows, 3);
        float cellW = (1f - gapX * (GridColumns - 1)) / GridColumns;
        float cellH = (1f - gapY * (heightRows - 1)) / heightRows;

        for (int i = 0; i < entries.Count; i++)
        {
            int col = i % GridColumns;
            int row = i / GridColumns;

            float left = col * (cellW + gapX);
            float top = 1f - row * (cellH + gapY);   // 앵커 y는 아래가 0이라 행을 뒤집는다

            BuildCell(entries[i], new Vector2(left, top - cellH), new Vector2(left + cellW, top));
        }

        Select(entries[0].itemId);
    }

    private void BuildCell(UnlockEntry entry, Vector2 anchorMin, Vector2 anchorMax)
    {
        bool unlocked = UnlockState.IsUnlocked(entry.itemId);

        var cellGo = new GameObject($"Item_{entry.itemId}", typeof(RectTransform));
        cellGo.transform.SetParent(gridArea, false);
        Stretch((RectTransform)cellGo.transform, anchorMin, anchorMax);
        var cellRect = (RectTransform)cellGo.transform;

        Image bg = CreateImage(cellRect, "BG", Vector2.zero, Vector2.one, CellIdleColor);
        Sprite plate = Resources.Load<Sprite>("UI/Black_ui04");
        if (plate != null)
        {
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.2f;
        }

        Image icon = CreateImage(cellRect, "Icon", new Vector2(0.14f, 0.28f), new Vector2(0.86f, 0.94f),
                                 unlocked ? Color.white : LockedIconColor);
        icon.sprite = ResolveIcon(entry);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        if (!unlocked)
        {
            // 잠긴 항목은 실루엣만 보여주고 그 위에 자물쇠를 얹는다(무엇이 있는지는 알되
            // 정체는 가린다 - 해금 기획서 목업과 같은 표현).
            Image padlock = CreateImage(cellRect, "Lock", new Vector2(0.34f, 0.42f), new Vector2(0.66f, 0.80f), Color.white);
            padlock.sprite = UiIconLibrary.Lock();
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;
        }

        TextMeshProUGUI label = CreateText(cellRect, "Name", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.26f),
                                           TextAlignmentOptions.Midline, 15f);
        label.text = unlocked ? ResolveName(entry) : "???";
        label.color = unlocked ? Color.white : MutedTextColor;

        var button = cellGo.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        button.colors = colors;

        int itemId = entry.itemId; // 람다 클로저 캡처용 로컬
        button.onClick.AddListener(() => Select(itemId));
    }

    private void BuildDetail(RectTransform rootRect)
    {
        var panel = new GameObject("Detail", typeof(RectTransform));
        panel.transform.SetParent(rootRect, false);
        var panelRect = (RectTransform)panel.transform;
        Stretch(panelRect, new Vector2(0.635f, 0.145f), new Vector2(0.955f, 0.898f));

        Image bg = CreateImage(panelRect, "BG", Vector2.zero, Vector2.one, new Color(0.10f, 0.11f, 0.14f, 1f));
        Sprite plate = Resources.Load<Sprite>("UI/Black_ui04");
        if (plate != null)
        {
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1.6f;
        }

        detailIcon = CreateImage(panelRect, "Icon", new Vector2(0.28f, 0.63f), new Vector2(0.72f, 0.95f), Color.white);
        detailIcon.preserveAspect = true;
        detailIcon.raycastTarget = false;

        detailName = CreateText(panelRect, "Name", new Vector2(0.06f, 0.53f), new Vector2(0.94f, 0.62f),
                                TextAlignmentOptions.Midline, 26f);
        detailName.color = AccentColor;

        TextMeshProUGUI conditionTitle = CreateText(panelRect, "ConditionTitle", new Vector2(0.06f, 0.44f),
                                                    new Vector2(0.94f, 0.51f), TextAlignmentOptions.Midline, 17f);
        conditionTitle.text = "해금 조건";
        conditionTitle.color = MutedTextColor;

        detailCondition = CreateText(panelRect, "Condition", new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.43f),
                                     TextAlignmentOptions.Top, 19f);

        // 진행도 막대 - 배경 위에 채움 이미지를 얹고 가로 앵커로 비율을 표현한다.
        CreateImage(panelRect, "ProgressBG", new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.20f),
                    new Color(0.22f, 0.23f, 0.27f, 1f));
        detailProgressFill = CreateImage(panelRect, "ProgressFill", new Vector2(0.08f, 0.15f), new Vector2(0.92f, 0.20f),
                                         AccentColor);
        detailProgressFill.raycastTarget = false;

        detailProgress = CreateText(panelRect, "ProgressText", new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.135f),
                                    TextAlignmentOptions.Midline, 18f);
        detailProgress.color = MutedTextColor;
    }

    private void Select(int itemId)
    {
        selectedItemId = itemId;
        if (!UnlockCatalog.TryGet(itemId, out UnlockEntry entry)) return;

        bool unlocked = UnlockState.IsUnlocked(itemId);

        if (detailIcon != null)
        {
            detailIcon.sprite = ResolveIcon(entry);
            detailIcon.color = unlocked ? Color.white : LockedIconColor;
        }

        if (detailName != null) detailName.text = unlocked ? ResolveName(entry) : "???";
        if (detailCondition != null)
        {
            detailCondition.text = entry.conditionText;
            detailCondition.color = unlocked ? Color.white : MutedTextColor;
        }

        UpdateProgress(entry, unlocked);
    }

    private void UpdateProgress(UnlockEntry entry, bool unlocked)
    {
        if (detailProgress == null || detailProgressFill == null) return;

        if (entry.UnlockedFromStart)
        {
            detailProgress.text = "기본 제공";
            SetFillRatio(1f);
            return;
        }

        int current = Mathf.Min(UnlockState.GetProgress(entry), entry.requiredAmount);
        detailProgress.text = unlocked ? $"해금 완료 ({entry.requiredAmount} / {entry.requiredAmount})"
                                       : $"{current} / {entry.requiredAmount}";
        SetFillRatio(entry.requiredAmount > 0 ? (float)current / entry.requiredAmount : 0f);
    }

    private void SetFillRatio(float ratio01)
    {
        var rect = (RectTransform)detailProgressFill.transform;
        rect.anchorMin = new Vector2(0.08f, 0.15f);
        rect.anchorMax = new Vector2(0.08f + (0.92f - 0.08f) * Mathf.Clamp01(ratio01), 0.20f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void Close()
    {
        onClose?.Invoke();
        Destroy(gameObject);
    }

    // ── 이름/아이콘 조회 ──────────────────────────────────────────────────────────

    private string ResolveName(UnlockEntry entry)
    {
        switch (entry.category)
        {
            case UnlockCategory.Head:
                if (GameDataManager.Instance != null &&
                    GameDataManager.Instance.Robots.TryGetValue(entry.itemId, out RobotData robot))
                    return robot.robot_name;
                break;

            case UnlockCategory.Disc:
                if (TryGetDisc(entry.itemId, out DiscData disc)) return disc.discName;
                break;

            case UnlockCategory.Accessory:
                if (AccessoryCatalog.TryGet(entry.itemId, out AccessoryData accessory)) return accessory.accessoryName;
                break;
        }

        return entry.fallbackName;
    }

    private Sprite ResolveIcon(UnlockEntry entry)
    {
        switch (entry.category)
        {
            case UnlockCategory.Head:
                if (partsCatalog == null) return null;
                return HeadSpriteLibrary.GetIcon(partsCatalog.GetHeadModdingInfo(entry.itemId));

            case UnlockCategory.Disc:
                return TryGetDisc(entry.itemId, out DiscData disc) ? disc.LoadIcon() : null;

            case UnlockCategory.Accessory:
                return AccessoryCatalog.TryGet(entry.itemId, out AccessoryData accessory) ? accessory.LoadIcon() : null;
        }

        return null;
    }

    private bool TryGetDisc(int discId, out DiscData result)
    {
        if (shopCatalog != null)
        {
            foreach (DiscData disc in shopCatalog.Discs)
            {
                if (disc.discId != discId) continue;
                result = disc;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static string CategoryName(UnlockCategory category)
    {
        switch (category)
        {
            case UnlockCategory.Head: return "머리";
            case UnlockCategory.Disc: return "디스크";
            default: return "악세사리";
        }
    }

    // ── UI 헬퍼 (HeadSelectPanelUI와 같은 관례) ──────────────────────────────────

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
        text.enableAutoSizing = true;
        text.fontSizeMin = 8f;
        text.fontSizeMax = maxSize;
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

    private static Button CreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                       string label, out TextMeshProUGUI labelText)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        Sprite art = Resources.Load<Sprite>("UI/Purple_ui02");
        if (art != null)
        {
            image.sprite = art;
            image.type = Image.Type.Sliced;
        }
        else
        {
            image.color = new Color(0.30f, 0.24f, 0.52f, 1f);
        }

        labelText = CreateText((RectTransform)go.transform, "Label", Vector2.zero, Vector2.one,
                               TextAlignmentOptions.Midline, 22f);
        labelText.text = label;

        return go.AddComponent<Button>();
    }
}
