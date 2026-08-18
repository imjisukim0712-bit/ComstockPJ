using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// AI 코어 3택 카드 화면 아래에 붙는 골드 리롤 버튼(2026-08-18 사용자 요청).
/// 골드를 내고 3택을 다시 뽑는다. 비용은 상점 새로고침과 같은 누적 방식이며 카드 화면을
/// 새로 열 때마다 기본값으로 돌아간다(<see cref="AiCoreManager.CurrentRerollCost"/>).
///
/// (2026-08-18 추가 변경: "레벨업 취소" 버튼은 경험치 반환 없이는 의미가 없다는 사용자 판단으로
/// 통째로 제거했다. 되돌리려면 이 커밋 이전 버전의 <c>HandleAiCoreCancelClicked</c>/
/// <c>AiCoreManager.CancelLevelUp</c> 계열을 git 이력에서 참고할 것.)
///
/// 씬에 미리 배치하지 않고 <see cref="Attach"/>가 코드로 만들어 붙인다
/// (<see cref="MusicVolumeSliderUI"/>·<see cref="EquipmentDetailPopup"/>과 같은 방식).
/// AI 코어 화면은 전용 UI 스크립트 없이 <c>GameFlowManager</c>가 직접 그리고 있어서, 버튼을
/// 씬에 새로 깔면 그쪽 직렬화 필드까지 늘어난다 - 씬 수정 0으로 끝내려고 코드 생성을 택했다.
///
/// 배치는 전부 <b>앵커 비율</b>이다. 캔버스가 ConstantPixelSize라 절대 픽셀로 만들면 Game View
/// 해상도마다 크기가 제각각이 된다. 카드(중앙 기준 높이 320px)보다 확실히 아래인 y 0.16~0.24에
/// 두어 어떤 해상도에서도 카드와 겹치지 않는다.
/// </summary>
public class AiCoreExtraButtonsUI : MonoBehaviour
{
    private TextMeshProUGUI reroll_label;
    private TextMeshProUGUI message_text;
    private Button reroll_button;
    private TextMeshProUGUI gold_text;

    /// <summary>부모(AI 코어 패널) 아래에 리롤 버튼 + 안내 문구를 만든다.</summary>
    public static AiCoreExtraButtonsUI Attach(RectTransform parent, UnityAction onReroll)
    {
        if (parent == null) return null;

        var root = new GameObject("AiCoreExtraButtons", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.layer = parent.gameObject.layer; // 패널과 같은 UI 레이어여야 캔버스가 그린다
        Stretch((RectTransform)root.transform, Vector2.zero, Vector2.one);

        var ui = root.AddComponent<AiCoreExtraButtonsUI>();
        ui.Build((RectTransform)root.transform, onReroll);
        return ui;
    }

    private void Build(RectTransform rootRect, UnityAction onReroll)
    {
        // 이 화면은 정비 중이라 GameHUD가 꺼져 있어(GameFlowManager.SetCombatHudVisible(false))
        // 골드가 어디에도 안 보인다 - 리롤 비용을 낼 수 있는지 판단하려면 보유 골드를 알아야
        // 하므로 우상단에 작게 표시한다. 카드 배너(y 0.76~0.89 부근)보다 위인 y 0.90~0.955에
        // 둬서 겹치지 않는다.
        CreateGoldDisplay(rootRect);

        // 버튼이 리롤 하나만 남아 중앙에 둔다(취소 버튼 제거 - 경험치 반환 없이는 의미가 없다는
        // 사용자 판단으로 기능 자체를 뺐다).
        reroll_button = CreateButton(rootRect, "RerollButton",
            new Vector2(0.410f, 0.160f), new Vector2(0.590f, 0.240f), onReroll, out reroll_label);

        message_text = CreateText(rootRect, "Message",
            new Vector2(0.300f, 0.105f), new Vector2(0.700f, 0.150f), TextAlignmentOptions.Center, 8f, 24f);
        message_text.color = new Color(1f, 0.72f, 0.35f, 1f); // 골드 부족 등 경고성 안내
        message_text.text = string.Empty;
    }

    private void CreateGoldDisplay(RectTransform parent)
    {
        Vector2 bgMin = new Vector2(0.780f, 0.895f);
        Vector2 bgMax = new Vector2(0.965f, 0.955f);

        Sprite plate = Resources.Load<Sprite>("UI/Panel01"); // HUD 골드 표시와 같은 판때기(GameHUD 참고)
        if (plate != null)
        {
            Image bg = CreateImage(parent, "GoldDisplay_BG", bgMin, bgMax, Color.white);
            bg.sprite = plate;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
        }

        float width = bgMax.x - bgMin.x;
        float iconMaxX = bgMin.x + width * 0.30f;

        Image icon = CreateImage(parent, "GoldIcon",
            new Vector2(bgMin.x + width * 0.04f, bgMin.y + 0.008f), new Vector2(iconMaxX, bgMax.y - 0.008f), Color.white);
        icon.raycastTarget = false;
        Sprite iconSprite = Resources.Load<Sprite>("UI/Gold_icon00");
        if (iconSprite != null)
        {
            icon.sprite = iconSprite;
            icon.preserveAspect = true; // 고정 픽셀 크기를 쓰면 해상도가 바뀔 때 바탕만 늘어나고 아이콘은 그대로라 어긋난다
        }

        gold_text = CreateText(parent, "GoldText",
            new Vector2(iconMaxX, bgMin.y), new Vector2(bgMax.x - width * 0.03f, bgMax.y),
            TextAlignmentOptions.MidlineLeft, 8f, 28f);
    }

    /// <summary>카드를 새로 그릴 때마다 호출해 버튼 문구·보유 골드·사용 가능 여부를 현재 상태에 맞춘다.</summary>
    public void Refresh(int rerollCost, bool canAffordReroll, int gold)
    {
        if (reroll_label != null) reroll_label.text = $"리롤 ({rerollCost}골드)";
        if (reroll_button != null) reroll_button.interactable = canAffordReroll;

        if (gold_text != null) gold_text.text = gold.ToString();
    }

    public void SetMessage(string message)
    {
        if (message_text != null) message_text.text = message ?? string.Empty;
    }

    // ── 생성 헬퍼 (EquipmentDetailPopup / MusicVolumeSliderUI와 같은 관례) ──

    private static Button CreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                       UnityAction onClick, out TextMeshProUGUI label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        Image image = go.GetComponent<Image>();
        ApplySkin(image, "Purple_button00", new Color(0.26f, 0.28f, 0.34f, 1f));

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        if (onClick != null) button.onClick.AddListener(onClick);

        label = CreateText((RectTransform)go.transform, "Label", Vector2.zero, Vector2.one,
            TextAlignmentOptions.Center, 10f, 26f);
        return button;
    }

    /// <summary>
    /// Resources/UI의 9-슬라이스 아트를 입힌다. 못 찾으면 단색으로 남긴다 - 이미지가 없다고
    /// 버튼이 아예 안 보이는 사고를 막기 위한 폴백(EquipmentDetailPopup.ApplySkin과 동일).
    /// </summary>
    private static void ApplySkin(Image image, string spriteName, Color fallbackColor)
    {
        if (image == null) return;

        Sprite sprite = Resources.Load<Sprite>("UI/" + spriteName);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            return;
        }

        image.color = fallbackColor;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Image CreateImage(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, Vector2 anchorMin,
                                              Vector2 anchorMax, TextAlignmentOptions alignment,
                                              float fontMin, float fontMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        Stretch((RectTransform)go.transform, anchorMin, anchorMax);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false; // 클릭은 부모 버튼이 받아야 한다
        text.enableAutoSizing = true;
        text.fontSizeMin = fontMin;
        text.fontSizeMax = fontMax;
        return text;
    }
}
