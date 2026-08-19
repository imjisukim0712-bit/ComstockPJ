using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 시작 시 1회 나오는 머리(로봇) 선택 화면. `머리 기획서 Ver04`(2026-08-18) 반영.
///
/// <b>왜 씬이 아니라 코드로 만드나</b> — 머리 개수가 데이터(PartsCatalog.headModdingInfos)에서
/// 오기 때문에 씬에 칸을 미리 깔아둘 수 없다. 상점의 소켓 선택 창이 씬에 버튼 4개를 고정으로
/// 깔아뒀다가 6소켓 로봇에서 막혔던 것과 같은 함정을 피하려는 것이다(2026-08-19 Phase C).
/// 그래서 <see cref="Attach"/>가 <see cref="Title"/> 씬의 캔버스 아래에 전부 만들어 붙인다.
///
/// <b>머리는 상점에 등장하지 않는 캐릭터 정체성</b>이라 여기서 고른 값이 런 내내 고정이다
/// (<see cref="PlayerSession.SelectedRobotId"/>). 정비 화면의 머리 칸도 조회 전용이다.
///
/// 캔버스가 ConstantPixelSize라 <b>모든 배치는 정규화 앵커 + offset 0</b>으로 한다
/// (절대 픽셀을 쓰면 FHD 밖 해상도에서 어긋난다 - 2026-08-13/18에 이미 겪은 문제).
/// </summary>
public class HeadSelectPanelUI : MonoBehaviour
{
    private const int GridColumns = 4;

    // 등급색이 아니라 "선택됨"을 나타내는 강조색. 정비 화면의 슬롯 강조와 같은 노란색을 쓴다
    // (파랑 계열은 희귀 등급과 헷갈린다 - 2026-08-18에 정비 화면에서 겪은 문제).
    private static readonly Color SelectedColor = new Color(0.95f, 0.75f, 0.15f, 1f);
    private static readonly Color CellIdleColor = new Color(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Color CellHoverColor = new Color(0.24f, 0.26f, 0.30f, 1f);

    /// <summary>해금 전인 머리의 실루엣 색(2026-08-19 Phase E).</summary>
    private static readonly Color LockedIconColor = new Color(0.10f, 0.10f, 0.12f, 1f);

    private sealed class HeadCell
    {
        public PartsCatalog.HeadModdingInfo info;
        public Image background;
        public Image border;
        public Image icon;
        public bool animated;
    }

    private readonly List<HeadCell> cells = new List<HeadCell>();

    private PartsCatalog catalog;
    private System.Action<int> on_confirm;
    private System.Action on_cancel;

    private int selected_robot_id = -1;

    private Image detail_icon;
    private TextMeshProUGUI detail_name;
    private TextMeshProUGUI detail_effect_title;
    private TextMeshProUGUI detail_effect_body;
    private TextMeshProUGUI detail_stats;
    private Button confirm_button;
    private TextMeshProUGUI confirm_label;

    /// <summary>
    /// 부모 캔버스 아래에 선택 화면을 만들고 돌려준다.
    /// <paramref name="onConfirm"/>에 선택된 robot_id가 전달된다.
    /// </summary>
    public static HeadSelectPanelUI Attach(RectTransform parent, PartsCatalog catalog,
                                           System.Action<int> onConfirm, System.Action onCancel)
    {
        if (parent == null || catalog == null) return null;

        var root = new GameObject("HeadSelectPanel", typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rootRect = (RectTransform)root.transform;
        Stretch(rootRect, Vector2.zero, Vector2.one);

        // 런타임에 만들어진 다른 UI(볼륨 슬라이더 등)보다 뒤 형제여야 위에 그려진다.
        // UI는 형제 순서가 곧 그리기 순서다(2026-08-19 상점 소켓 창이 격자에 가려졌던 문제).
        rootRect.SetAsLastSibling();

        var ui = root.AddComponent<HeadSelectPanelUI>();
        ui.catalog = catalog;
        ui.on_confirm = onConfirm;
        ui.on_cancel = onCancel;
        ui.Build(rootRect);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        // 타이틀 배경을 가리는 반투명 암막. raycastTarget을 켜둬야 뒤의 "게임 시작"·"종료"
        // 버튼이 이 화면 위에서 눌리지 않는다.
        Image backdrop = CreateImage(rootRect, "Backdrop", Vector2.zero, Vector2.one, new Color(0.04f, 0.04f, 0.06f, 0.94f));
        backdrop.raycastTarget = true;

        TextMeshProUGUI title = CreateText(rootRect, "Title", new Vector2(0.05f, 0.905f), new Vector2(0.95f, 0.975f),
                                           TextAlignmentOptions.Midline, 40f);
        title.text = "머리 선택";
        title.color = SelectedColor;

        TextMeshProUGUI hint = CreateText(rootRect, "Hint", new Vector2(0.05f, 0.862f), new Vector2(0.95f, 0.902f),
                                          TextAlignmentOptions.Midline, 17f);
        hint.text = "머리는 게임 시작 시 한 번만 고르며 런 중에는 바꿀 수 없습니다";
        hint.color = new Color(0.72f, 0.74f, 0.78f, 1f);

        BuildGrid(rootRect);
        BuildDetail(rootRect);
        BuildButtons(rootRect);

        // 기본 선택 = 해금된 첫 머리(보통 컴스톡 MK-01). 아무것도 안 고른 상태로 출발할 수 없게 한다.
        List<PartsCatalog.HeadModdingInfo> heads = catalog.GetSelectableHeads();
        foreach (PartsCatalog.HeadModdingInfo head in heads)
        {
            if (!UnlockState.IsUnlocked(head.robotId)) continue;
            Select(head.robotId);
            break;
        }
    }

    private void BuildGrid(RectTransform rootRect)
    {
        List<PartsCatalog.HeadModdingInfo> heads = catalog.GetSelectableHeads();
        if (heads.Count == 0)
        {
            Debug.LogWarning("머리 선택 화면에 띄울 머리가 없습니다. PartsCatalog의 headModdingInfos에서 " +
                             "selectableInHeadSelect가 켜진 항목이 있는지 확인하세요.");
            return;
        }

        // 격자 영역(좌측). 칸 수에 맞춰 행 수를 계산하므로 머리를 추가해도 저절로 늘어난다.
        var area = new GameObject("Grid", typeof(RectTransform));
        area.transform.SetParent(rootRect, false);
        var areaRect = (RectTransform)area.transform;
        Stretch(areaRect, new Vector2(0.045f, 0.175f), new Vector2(0.575f, 0.845f));

        int rows = Mathf.CeilToInt(heads.Count / (float)GridColumns);
        const float gapX = 0.018f;
        const float gapY = 0.028f;
        float cellW = (1f - gapX * (GridColumns - 1)) / GridColumns;
        float cellH = (1f - gapY * (rows - 1)) / rows;

        for (int i = 0; i < heads.Count; i++)
        {
            int col = i % GridColumns;
            int row = i / GridColumns;

            float x0 = col * (cellW + gapX);
            // 위에서 아래로 채운다(앵커 y는 아래가 0이라 행을 뒤집어야 한다)
            float y1 = 1f - row * (cellH + gapY);

            BuildCell(areaRect, heads[i], new Vector2(x0, y1 - cellH), new Vector2(x0 + cellW, y1));
        }
    }

    private void BuildCell(RectTransform parent, PartsCatalog.HeadModdingInfo info, Vector2 anchorMin, Vector2 anchorMax)
    {
        var cellGo = new GameObject($"Head_{info.robotId}", typeof(RectTransform));
        cellGo.transform.SetParent(parent, false);
        var cellRect = (RectTransform)cellGo.transform;
        Stretch(cellRect, anchorMin, anchorMax);

        Image bg = CreateImage(cellRect, "BG", Vector2.zero, Vector2.one, CellIdleColor);
        Sprite plate = Resources.Load<Sprite>("UI/Black_ui04");
        if (plate != null)
        {
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
            // 붙는 자리가 원본보다 작으면 9-슬라이스 테두리가 가운데를 다 먹는다 - 테두리가
            // 그려지는 크기를 줄여 가운데가 살아있게 한다(2026-08-13에 체력 바에서 겪은 문제).
            bg.pixelsPerUnitMultiplier = 2.2f;
        }

        // 선택 강조용 테두리. 코드로 그린 흰색 실루엣이라 색을 곱해 노랗게 쓸 수 있다
        // (프로젝트의 기존 테두리 아트는 거의 검정이라 색을 곱하면 죽는다).
        Image border = CreateImage(cellRect, "Border", new Vector2(-0.015f, -0.015f), new Vector2(1.015f, 1.015f), SelectedColor);
        border.sprite = UiIconLibrary.Frame();
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        border.enabled = false;

        // 해금 전인 머리는 실루엣 + 자물쇠로만 보여주고 고를 수 없다(2026-08-19 Phase E).
        bool unlocked = UnlockState.IsUnlocked(info.robotId);

        Image icon = CreateImage(cellRect, "Icon", new Vector2(0.13f, 0.26f), new Vector2(0.87f, 0.95f),
                                 unlocked ? Color.white : LockedIconColor);
        icon.sprite = HeadSpriteLibrary.GetIcon(info);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        if (!unlocked)
        {
            Image padlock = CreateImage(cellRect, "Lock", new Vector2(0.33f, 0.45f), new Vector2(0.67f, 0.82f), Color.white);
            padlock.sprite = UiIconLibrary.Lock();
            padlock.preserveAspect = true;
            padlock.raycastTarget = false;
        }

        TextMeshProUGUI label = CreateText(cellRect, "Name", new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.24f),
                                           TextAlignmentOptions.Midline, 16f);
        label.text = unlocked ? GetHeadName(info.robotId) : "???";
        if (!unlocked) label.color = new Color(0.72f, 0.74f, 0.78f, 1f);

        var button = cellGo.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.6f, 1f);
        button.colors = colors;
        button.interactable = unlocked;

        int robotId = info.robotId; // 람다 클로저 캡처용 로컬
        button.onClick.AddListener(() => Select(robotId));

        Sprite[] frames = HeadSpriteLibrary.GetFrames(info);
        cells.Add(new HeadCell
        {
            info = info,
            background = bg,
            border = border,
            icon = icon,
            animated = frames != null && frames.Length > 1
        });
    }

    private void BuildDetail(RectTransform rootRect)
    {
        var panel = new GameObject("Detail", typeof(RectTransform));
        panel.transform.SetParent(rootRect, false);
        var panelRect = (RectTransform)panel.transform;
        Stretch(panelRect, new Vector2(0.60f, 0.175f), new Vector2(0.955f, 0.845f));

        Image bg = CreateImage(panelRect, "BG", Vector2.zero, Vector2.one, new Color(0.11f, 0.12f, 0.145f, 1f));
        Sprite plate = Resources.Load<Sprite>("UI/Panel02");
        if (plate != null)
        {
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
        }
        bg.raycastTarget = false;

        detail_icon = CreateImage(panelRect, "Icon", new Vector2(0.30f, 0.70f), new Vector2(0.70f, 0.975f), Color.white);
        detail_icon.preserveAspect = true;
        detail_icon.raycastTarget = false;

        detail_name = CreateText(panelRect, "Name", new Vector2(0.05f, 0.625f), new Vector2(0.95f, 0.70f),
                                 TextAlignmentOptions.Midline, 30f);
        detail_name.color = SelectedColor;

        detail_effect_title = CreateText(panelRect, "EffectTitle", new Vector2(0.05f, 0.555f), new Vector2(0.95f, 0.62f),
                                         TextAlignmentOptions.Midline, 19f);
        detail_effect_title.color = new Color(0.62f, 0.88f, 1f, 1f);

        detail_effect_body = CreateText(panelRect, "EffectBody", new Vector2(0.06f, 0.35f), new Vector2(0.94f, 0.55f),
                                        TextAlignmentOptions.Top, 17f);
        detail_effect_body.color = new Color(0.93f, 0.94f, 0.96f, 1f);

        detail_stats = CreateText(panelRect, "Stats", new Vector2(0.06f, 0.03f), new Vector2(0.94f, 0.335f),
                                  TextAlignmentOptions.TopLeft, 18f);
        detail_stats.color = new Color(0.86f, 0.88f, 0.92f, 1f);
    }

    private void BuildButtons(RectTransform rootRect)
    {
        confirm_button = CreateButton(rootRect, "ConfirmButton", new Vector2(0.60f, 0.065f), new Vector2(0.80f, 0.145f),
                                      "출발", out confirm_label);
        confirm_button.onClick.AddListener(() =>
        {
            if (selected_robot_id < 0) return;
            on_confirm?.Invoke(selected_robot_id);
        });

        Button back = CreateButton(rootRect, "BackButton", new Vector2(0.815f, 0.065f), new Vector2(0.955f, 0.145f),
                                   "뒤로", out _);
        back.onClick.AddListener(() => on_cancel?.Invoke());
    }

    /// <summary>머리를 고른다(아직 확정은 아니다 - "출발"을 눌러야 씬이 넘어간다).</summary>
    private void Select(int robotId)
    {
        selected_robot_id = robotId;

        foreach (HeadCell cell in cells)
        {
            bool isSelected = cell.info.robotId == robotId;
            if (cell.border != null) cell.border.enabled = isSelected;
            if (cell.background != null) cell.background.color = isSelected ? CellHoverColor : CellIdleColor;
        }

        RefreshDetail(robotId);
    }

    private void RefreshDetail(int robotId)
    {
        PartsCatalog.HeadModdingInfo info = catalog.GetHeadModdingInfo(robotId);

        if (detail_icon != null) detail_icon.sprite = HeadSpriteLibrary.GetIcon(info);
        if (detail_name != null) detail_name.text = GetHeadName(robotId);

        if (detail_effect_title != null) detail_effect_title.text = $"[{info.effect.ToKorean()}]";
        if (detail_effect_body != null) detail_effect_body.text = info.effect.ToDescription();

        if (detail_stats == null) return;

        // 체력·질량은 RobotData(GameDataAsset)가 유일한 출처다 - 머리 데이터에 중복 저장하지
        // 않았으므로 여기서 조회한다. 타이틀 씬에도 DataManager를 둔 이유가 이것이다.
        string hp = "-";
        string mass = "-";
        if (GameDataManager.Instance != null && GameDataManager.Instance.Robots.TryGetValue(robotId, out RobotData data))
        {
            hp = data.robot_hp.ToString();
            mass = $"{data.robot_mess:0.##}";
        }

        detail_stats.text =
            $"체력  <b>{hp}</b>      질량  <b>{mass}</b>\n" +
            $"무기 소켓  <b>{info.weaponSocketCount}</b>      디스크 슬롯  <b>{info.discSlotCount}</b>\n" +
            $"적재량  <b>{info.partBoxCapacity}</b>\n" +
            $"기본 무기  <b>{DescribeDefaultWeapons(info)}</b>";
    }

    /// <summary>기본 장착 무기 이름 목록. 데이터가 없으면 "-".</summary>
    private static string DescribeDefaultWeapons(PartsCatalog.HeadModdingInfo info)
    {
        if (info.defaultWeaponIds == null || info.defaultWeaponIds.Length == 0) return "-";

        var names = new List<string>(info.defaultWeaponIds.Length);
        foreach (int weaponId in info.defaultWeaponIds)
        {
            if (GameDataManager.Instance != null &&
                GameDataManager.Instance.Weapons.TryGetValue(weaponId, out WeaponData weapon))
            {
                names.Add(weapon.weapon_name);
            }
            else
            {
                names.Add(weaponId.ToString());
            }
        }

        return string.Join(", ", names);
    }

    private static string GetHeadName(int robotId)
    {
        if (GameDataManager.Instance != null && GameDataManager.Instance.Robots.TryGetValue(robotId, out RobotData data))
        {
            return data.robot_name;
        }
        return robotId.ToString();
    }

    /// <summary>
    /// 네온아이처럼 눈 색이 순환하는 머리의 아이콘을 계속 갱신한다.
    /// 선택 화면에서도 "눈 색이 천천히 바뀐다"는 컨셉이 그대로 보이게 하려는 것이다.
    /// 타이틀 씬은 timeScale이 1이지만 다른 화면에서 재사용될 때를 대비해 unscaledTime을 쓴다.
    /// </summary>
    private void Update()
    {
        float t = Time.unscaledTime;

        foreach (HeadCell cell in cells)
        {
            if (!cell.animated || cell.icon == null) continue;
            cell.icon.sprite = HeadSpriteLibrary.GetAnimatedFrame(cell.info, t);
        }

        if (detail_icon != null && selected_robot_id >= 0)
        {
            PartsCatalog.HeadModdingInfo info = catalog.GetHeadModdingInfo(selected_robot_id);
            Sprite[] frames = HeadSpriteLibrary.GetFrames(info);
            if (frames != null && frames.Length > 1) detail_icon.sprite = HeadSpriteLibrary.GetAnimatedFrame(info, t);
        }
    }

    // ── 생성 헬퍼 (MusicVolumeSliderUI와 같은 패턴) ─────────────────────────────────

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
                               TextAlignmentOptions.Midline, 24f);
        labelText.text = label;

        return go.AddComponent<Button>();
    }
}
