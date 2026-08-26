using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 우상단 설정(톱니바퀴) 버튼. 누르면 ESC와 똑같이 일시정지 메뉴가 열린다
/// (2026-08-19 사용자 요청 "화면 오른쪽 상단에 설정 UI 아이콘 만들어줘").
///
/// 씬에 배치하지 않고 <see cref="EnsureAttached"/>가 Canvas 밑에 코드로 만들어 붙인다
/// (<see cref="PauseMenuUI"/>·<see cref="AiCoreExtraButtonsUI"/>와 같은 관례 - 씬 수정 0건).
///
/// <b>자기 자신이 붙은 GameObject를 절대 끄지 않는다.</b> 숨길 때는 자식
/// <see cref="content"/>만 끈다 - 루트를 끄면 Unity가 이 스크립트의 <see cref="LateUpdate"/>를
/// 더는 호출하지 않아 스스로 다시 켤 수 없게 된다(2026-08-19에 <see cref="DashGaugeUI"/>와
/// <see cref="PauseMenuUI"/> 두 곳에서 연달아 터진 함정).
///
/// 런타임 생성이라 <c>GameFlowManager.combatHudObjects</c>(씬 오브젝트 배열)에 등록할 수 없어
/// 정비·상점·게임오버 중 숨김을 스스로 확인한다(<see cref="DashGaugeUI"/>와 같은 방식).
/// </summary>
public class SettingsIconUI : MonoBehaviour
{
    // 캔버스가 ConstantPixelSize라 절대 픽셀로 잡으면 해상도마다 위치·크기가 어긋난다.
    // 정규화 앵커 + offset 0으로만 배치한다(2026-08-18 HUD 재배치에서 확립된 규칙).
    private static readonly Vector2 AnchorMin = new Vector2(0.930f, 0.895f);
    private static readonly Vector2 AnchorMax = new Vector2(0.985f, 0.975f);

    private GameObject content; // 버튼 본체. 숨길 때 이것만 끈다(루트는 항상 활성)

    /// <summary>이미 있으면 재사용하고, 없으면 Canvas 아래에 만든다.
    /// 에디터 도메인 리로드로 참조가 날아가도 오브젝트는 씬에 남아 있으므로 먼저 찾아본다
    /// (<see cref="PauseMenuUI.EnsureAttached"/>와 같은 대응).</summary>
    public static SettingsIconUI EnsureAttached(RectTransform canvasRoot)
    {
        if (canvasRoot == null) return null;

        var existing = canvasRoot.GetComponentInChildren<SettingsIconUI>(true);
        if (existing != null) return existing;

        var go = new GameObject("SettingsIcon", typeof(RectTransform));
        go.transform.SetParent(canvasRoot, false);
        go.layer = canvasRoot.gameObject.layer; // 캔버스와 같은 UI 레이어여야 그려진다
        Stretch((RectTransform)go.transform, Vector2.zero, Vector2.one);

        var ui = go.AddComponent<SettingsIconUI>();
        ui.Build((RectTransform)go.transform);
        return ui;
    }

    private void Build(RectTransform rootRect)
    {
        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        contentGo.transform.SetParent(rootRect, false);
        contentGo.layer = rootRect.gameObject.layer;
        content = contentGo;
        Stretch((RectTransform)contentGo.transform, AnchorMin, AnchorMax);

        // 버튼 배경(기존 버튼들과 같은 아트). 없으면 단색으로 폴백한다.
        Image backplate = contentGo.GetComponent<Image>();
        Sprite backSprite = Resources.Load<Sprite>("UI/Purple_button00");
        if (backSprite != null)
        {
            backplate.sprite = backSprite;
            backplate.type = Image.Type.Sliced;
            backplate.color = Color.white;
        }
        else backplate.color = new Color(0.26f, 0.28f, 0.34f, 0.95f);

        var button = contentGo.AddComponent<Button>();
        button.targetGraphic = backplate;
        button.onClick.AddListener(HandleClicked);

        // 톱니바퀴 아이콘. Assets/Resources/UI/Settings_icon.png를 넣으면 자동 교체된다
        // (UiIconLibrary.Settings()가 파일을 먼저 찾고 없으면 코드로 그린다).
        var iconGo = new GameObject("Gear", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGo.transform.SetParent(contentGo.transform, false);
        iconGo.layer = rootRect.gameObject.layer;
        // 배경 안쪽으로 조금 들여서 테두리가 보이게 한다.
        Stretch((RectTransform)iconGo.transform, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f));

        Image icon = iconGo.GetComponent<Image>();
        icon.sprite = UiIconLibrary.Settings();
        icon.preserveAspect = true;
        icon.raycastTarget = false; // 클릭은 배경 버튼이 받는다
    }

    private void HandleClicked()
    {
        if (PauseMenuUI.Instance != null) PauseMenuUI.Instance.TryOpenPause();
    }

    private void LateUpdate()
    {
        if (content == null) return;

        // 정비·상점·게임오버·승리 중에는 숨긴다(그 화면들에서는 ESC도 막혀 있어, 눌러도 아무
        // 일이 없는 버튼이 떠 있으면 혼란스럽다). 일시정지 메뉴가 이미 열려 있을 때도 숨긴다 -
        // 이 아이콘이 PauseMenu보다 뒤 형제라 딤 배경 위에 떠 버리기 때문이다.
        bool paused_menu_open = PauseMenuUI.Instance != null && PauseMenuUI.Instance.IsOpen;
        // 점수 HUD와 같은 이유로 패널의 실제 상태도 함께 본다(2026-08-26) -
        // 이 아이콘도 상점 오른쪽 위 [Next Wave] 버튼과 같은 자리를 쓴다.
        bool visible = !GameFlowManager.IsIntermission
                       && !GameFlowManager.IsFullScreenPanelOpen
                       && !GameOverManager.IsGameOver
                       && !GameWinManager.IsGameWon
                       && !paused_menu_open;

        if (content.activeSelf != visible) content.SetActive(visible);
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
