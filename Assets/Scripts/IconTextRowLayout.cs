using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "왼쪽에 아이콘 + 오른쪽에 숫자"로 된 한 줄 UI에서, 숫자가 아이콘에 가려지지 않도록
/// 글자 왼쪽 여백을 <b>줄 너비에 비례하게</b> 다시 계산해 준다(2026-08-23 버그 수정).
///
/// <b>왜 필요한가</b> — 상점·정비 화면의 골드 표시는 아이콘이 TMP 오브젝트의 자식으로 들어가
/// 있고, 아이콘은 <b>정규화 앵커</b>(0.02~0.19)로, 글자를 밀어내는
/// <see cref="TMP_Text.margin"/>은 <b>절대 픽셀</b>(56~58)로 잡혀 있었다. 캔버스가
/// ConstantPixelSize라 화면이 커지면 아이콘만 같이 커지고 여백은 그대로여서,
/// <b>FHD보다 큰 해상도에서 아이콘이 숫자를 덮는다</b>(FHD 기준 여백 13.5px → 4K에서는 29px 겹침).
/// "절대 픽셀을 쓰면 FHD 밖 해상도에서 어긋난다"는 이 프로젝트의 HUD 배치 원칙에 걸린 사례다.
///
/// 인게임 HUD의 골드 줄(<c>Canvas/HP/GoldRow</c>)은 아이콘과 글자가 <b>형제</b>로 나뉘어
/// 둘 다 정규화 앵커를 쓰기 때문에 이 문제가 없다. 씬 구조를 그쪽에 맞춰 바꾸는 대신,
/// 여백만 매 갱신마다 비례로 다시 계산해 같은 결과를 낸다(씬 계층 변경 0건).
/// </summary>
public static class IconTextRowLayout
{
    /// <summary>아이콘 오른쪽 끝과 글자 사이에 두는 여백(줄 너비 대비 비율).</summary>
    public const float DefaultGapRatio = 0.09f;

    /// <summary>
    /// <paramref name="text"/>의 자식 중 <see cref="Image"/>를 가진 첫 번째 오브젝트를 아이콘으로 보고,
    /// 그 아이콘의 오른쪽 끝 + 여백만큼 글자 왼쪽 여백을 다시 잡는다.
    /// 아이콘이 없으면 아무것도 하지 않는다(여백을 0으로 덮어쓰지 않는다 - 아이콘 없이
    /// 들여쓰기만 하려고 margin을 준 텍스트가 따로 있다).
    /// </summary>
    public static void FitTextAfterLeadingIcon(TextMeshProUGUI text, float gapRatio = DefaultGapRatio)
    {
        if (text == null) return;

        var rect = text.rectTransform;
        float width = rect.rect.width;
        if (width <= 1f) return; // 아직 레이아웃이 잡히기 전(패널이 꺼져 있는 등)이면 다음 갱신에 맡긴다

        RectTransform icon = FindIcon(rect);
        if (icon == null) return;

        // 아이콘의 오른쪽 끝(로컬 px). offsetMax가 0인 정규화 앵커 배치라 anchorMax.x로 바로 구해진다.
        float iconRight = icon.anchorMax.x * width + icon.offsetMax.x;
        float wanted = iconRight + width * Mathf.Max(0f, gapRatio);

        Vector4 margin = text.margin;
        if (Mathf.Abs(margin.x - wanted) < 0.5f) return; // 매 프레임 레이아웃을 다시 시키지 않는다

        margin.x = wanted;
        text.margin = margin;
    }

    private static RectTransform FindIcon(RectTransform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.GetComponent<Image>() != null) return child as RectTransform;
        }
        return null;
    }
}
