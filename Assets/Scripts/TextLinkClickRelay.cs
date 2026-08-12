using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// TMP 텍스트 안의 <c>&lt;link="id"&gt;…&lt;/link&gt;</c> 구간을 클릭하면 그 id를 콜백으로
/// 넘겨주는 어댑터.
///
/// 상점의 "장착 무기 / 로봇 모딩 상태 / 디스크" 목록은 원래부터 <b>여러 줄이 한 덩어리로 들어간
/// TMP 텍스트 1개</b>씩이라, 줄마다 클릭을 받으려면 원래는 씬에 항목 수만큼 버튼을 깔아야 한다.
/// 항목 수가 소켓 개수·디스크 슬롯 수에 따라 런타임에 변하므로(무기 소켓 개별화 이후 특히),
/// 씬 오브젝트를 늘리는 대신 링크 태그 + 이 릴레이로 처리한다 - 씬 수정이 전혀 필요 없고
/// 항목이 몇 개로 늘어나도 텍스트만 다시 만들면 된다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TextLinkClickRelay : MonoBehaviour, IPointerClickHandler
{
    private TMP_Text target_text;
    private System.Action<string> on_link_clicked;

    /// <summary>
    /// 대상 텍스트에 릴레이를 붙이고(이미 있으면 재사용) 콜백을 갈아끼운다.
    /// 링크를 클릭으로 잡으려면 그래픽 레이캐스트 대상이어야 해서 raycastTarget도 켠다.
    /// </summary>
    public static void Attach(TMP_Text text, System.Action<string> handler)
    {
        if (text == null) return;

        TextLinkClickRelay relay = text.GetComponent<TextLinkClickRelay>();
        if (relay == null) relay = text.gameObject.AddComponent<TextLinkClickRelay>();

        relay.target_text = text;
        relay.on_link_clicked = handler;
        text.raycastTarget = true;
    }

    private void Awake()
    {
        if (target_text == null) target_text = GetComponent<TMP_Text>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (target_text == null || on_link_clicked == null) return;

        // ScreenSpaceOverlay 캔버스는 카메라가 null이어야 좌표 변환이 맞는다.
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera event_camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        int link_index = TMP_TextUtilities.FindIntersectingLink(target_text, eventData.position, event_camera);
        if (link_index < 0) return;

        on_link_clicked(target_text.textInfo.linkInfo[link_index].GetLinkID());
    }
}
