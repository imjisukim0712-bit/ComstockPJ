using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 상점 품목 카드의 <b>내용물을 카드 테두리 아트 안쪽으로 밀어 넣는다</b>
/// (2026-08-25 사용자 지적: "상점에서 여전히 이미지 어긋난다").
///
/// <para><b>원인(1080p 실측)</b>: 카드는 432x259px인데 배경 아트(<c>*_ui01</c>)의 9-slice
/// border가 <b>사방 34px</b>이다. 그런데 카드 안 요소들은 씬에서 카드 <b>전체</b> 기준 정규화
/// 앵커(아이콘 x 0.05~0.18 / y 0.74~0.96 등)로 놓여 있어서, 아이콘이 왼쪽 베젤을 12px,
/// 위 베젤을 24px 침범하고 있었다. 가격 박스도 아래 베젤을 21px 파고들었다.</para>
///
/// <para><b>고치는 방식</b>: 요소마다 여백을 손으로 조정하지 않는다(그러면 아트가 바뀔 때마다
/// 다시 손대야 하고, "UI 제작 규칙" 1번이 금지하는 임의의 숫자가 늘어난다). 대신
/// <b>설계 좌표(0~1)를 테두리 안쪽 영역(안전 밴드)으로 선형 사상</b>한다 -
/// 디자인이 정한 비율·간격은 그대로 유지되면서 전체가 베젤 안으로 들어온다.
/// 안전 밴드는 <see cref="UiSafeArea.GetBorderRatio"/>로 <b>실제 렌더 픽셀</b>에서 구하므로
/// 해상도가 바뀌어도(9-slice 코너는 항상 고정 픽셀이다) 저절로 맞는다.</para>
///
/// <para><b>설계 좌표는 이 컴포넌트에 등록된 값이 원본이며, 현재 앵커를 읽어서 쓰지 않는다.</b>
/// 현재값을 읽으면 정비·상점 화면이 웨이브마다 다시 열릴 때 사상이 <b>누적</b>돼 내용물이
/// 점점 쪼그라든다("UI 제작 규칙" 5번에 적힌 함정과 같은 종류). 순수 함수로 두면 몇 번
/// 불려도 결과가 같다.</para>
/// </summary>
[DisallowMultipleComponent]
public class ShopCardLayout : MonoBehaviour
{
    /// <summary>베젤 안쪽으로 한 번 더 띄우는 여백(px).</summary>
    private const float InnerPadding = 4f;

    private struct Entry
    {
        public RectTransform target;
        public Vector2 designMin; // 카드 전체(0~1) 기준 설계 좌표
        public Vector2 designMax;
    }

    private Image background;
    private readonly List<Entry> entries = new List<Entry>();
    private Vector2 applied_size = new Vector2(-1f, -1f);

    /// <summary>카드 배경(테두리 아트). 여기서 실제 베젤 두께를 읽는다.</summary>
    public void SetBackground(Image cardBackground)
    {
        background = cardBackground;
        applied_size = new Vector2(-1f, -1f);
    }

    /// <summary>
    /// 요소 하나를 등록한다. <paramref name="designMin"/>/<paramref name="designMax"/>는
    /// <b>테두리를 무시한 설계 좌표</b>(카드 전체 0~1)이며, 실제 앵커는 이 값을 안전 밴드로
    /// 사상한 결과가 된다. 같은 대상을 다시 등록하면 설계 좌표만 갱신된다.
    /// </summary>
    public void Track(RectTransform target, Vector2 designMin, Vector2 designMax)
    {
        if (target == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].target != target) continue;
            entries[i] = new Entry { target = target, designMin = designMin, designMax = designMax };
            applied_size = new Vector2(-1f, -1f);
            return;
        }

        entries.Add(new Entry { target = target, designMin = designMin, designMax = designMax });
        applied_size = new Vector2(-1f, -1f);
    }

    private void OnEnable() => applied_size = new Vector2(-1f, -1f);

    private void OnRectTransformDimensionsChange() => applied_size = new Vector2(-1f, -1f);

    private void LateUpdate()
    {
        var rect = (RectTransform)transform;
        Vector2 size = rect.rect.size;
        if (size.x <= 1f || size.y <= 1f) return;
        if ((size - applied_size).sqrMagnitude < 0.01f) return;
        applied_size = size;

        Vector4 border = UiSafeArea.GetBorderRatio(background);
        float padX = InnerPadding / size.x;
        float padY = InnerPadding / size.y;

        var bandMin = new Vector2(border.x + padX, border.y + padY);
        var bandMax = new Vector2(1f - border.z - padX, 1f - border.w - padY);
        if (bandMax.x - bandMin.x <= 0.05f || bandMax.y - bandMin.y <= 0.05f) return; // 테두리가 카드를 다 먹었다

        Vector2 span = bandMax - bandMin;

        foreach (Entry entry in entries)
        {
            if (entry.target == null) continue;

            entry.target.anchorMin = bandMin + Vector2.Scale(entry.designMin, span);
            entry.target.anchorMax = bandMin + Vector2.Scale(entry.designMax, span);
            entry.target.offsetMin = Vector2.zero;
            entry.target.offsetMax = Vector2.zero;
        }
    }
}
