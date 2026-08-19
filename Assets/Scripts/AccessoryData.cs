using UnityEngine;

/// <summary>
/// 악세사리(2026-08-19 Phase D) - 엔드리스 모드 상점에서만 등장하는 점수 전용 아이템.
/// 효과는 없고(기획서: "별도의 효과를 부여하지 않고 구매량에 따라 점수 보너스 지급") 구매할
/// 때마다 <see cref="RunScore.AddAccessoryScore"/>로 점수만 더한다.
///
/// 여러 개 겹쳐 구매할 수 있고, 어디에 그려지는지는 <see cref="AccessoryAttachPoint"/>가 정한다.
/// </summary>
public enum AccessoryAttachPoint
{
    Face,   // 얼굴에 고정 1개만 보인다(여러 개 사도 시각적으로는 1개, 점수는 매번 더해진다)
    Neck,   // 목에 고정 1개만 보인다(Face와 동일 규칙)
    Stack   // 머리 위로 구매 순서대로 쌓인다(뿔 위에 왕관 위에 뿔... - 사용자 지정 예시)
}

[System.Serializable]
public struct AccessoryData
{
    public int accessoryId;
    public string accessoryName;
    public int price;
    public int score;
    public string iconResourceName; // Assets/Resources 기준 경로(확장자 제외)
    public AccessoryAttachPoint attachPoint;

    /// <summary>캐릭터에 그릴 때의 <b>가로 크기</b>(머리에서 실제로 픽셀이 그려진 폭의 배수).
    /// 원본 PNG는 6장 모두 128x128이지만 그림이 차지하는 영역은 제각각이라(고양이 귀는 넓고
    /// 조이스틱은 좁다) 파일 크기를 그대로 쓰면 크기가 들쭉날쭉해진다. <see cref="AccessoryVisual"/>이
    /// 알파 영역(타이트 메시) 기준으로 이 배수에 맞춰 스케일을 계산한다.</summary>
    public float visualWidthRatio;

    public Sprite LoadIcon() => string.IsNullOrEmpty(iconResourceName) ? null : Resources.Load<Sprite>(iconResourceName);
}

/// <summary>
/// 악세사리 6종 고정 데이터. 무기/디스크와 달리 스프레드시트 연동 없이 종류·가격이 거의
/// 바뀌지 않는 작은 목록이라 ScriptableObject 대신 코드에 직접 둔다(HeadEffects의 조건표와
/// 같은 판단).
///
/// <b>가격은 기획서(악세사리기획서 Ver03) 원안의 절반</b>이다(사용자 지시: "너무 비싸면
/// 안된다" - 원안 100~300골드는 무기 한 정 값과 맞먹어 엔드리스 상점에서 살 이유가 사라진다).
/// 점수는 기획서 값을 그대로 썼다(20웨이브 클리어 총점 약 45,000점 대비 하나가 0.4~2.2%).
/// </summary>
public static class AccessoryCatalog
{
    public static readonly AccessoryData[] All =
    {
        new AccessoryData
        {
            accessoryId = 600001, accessoryName = "8비트 선글라스", price = 50, score = 200,
            iconResourceName = "Accessories/8Bitsunglass-transparent", attachPoint = AccessoryAttachPoint.Face, visualWidthRatio = 0.72f
        },
        new AccessoryData
        {
            accessoryId = 600002, accessoryName = "왕관", price = 100, score = 500,
            iconResourceName = "Accessories/Crown-transparent", attachPoint = AccessoryAttachPoint.Stack, visualWidthRatio = 0.55f
        },
        new AccessoryData
        {
            accessoryId = 600003, accessoryName = "합격 목걸이", price = 150, score = 1000,
            iconResourceName = "Accessories/Passneck-transparent", attachPoint = AccessoryAttachPoint.Neck, visualWidthRatio = 0.45f
        },
        new AccessoryData
        {
            accessoryId = 600004, accessoryName = "유니콘 뿔", price = 100, score = 400,
            iconResourceName = "Accessories/Unicon-transparent", attachPoint = AccessoryAttachPoint.Stack, visualWidthRatio = 0.30f
        },
        new AccessoryData
        {
            accessoryId = 600005, accessoryName = "조이스틱", price = 50, score = 200,
            iconResourceName = "Accessories/Joystick-transparent", attachPoint = AccessoryAttachPoint.Stack, visualWidthRatio = 0.20f
        },
        new AccessoryData
        {
            accessoryId = 600006, accessoryName = "의문의 검은 고양이 귀", price = 75, score = 444,
            iconResourceName = "Accessories/Kkami-transparent", attachPoint = AccessoryAttachPoint.Stack, visualWidthRatio = 0.80f
        }
    };

    public static bool TryGet(int accessoryId, out AccessoryData data)
    {
        foreach (AccessoryData d in All)
        {
            if (d.accessoryId != accessoryId) continue;
            data = d;
            return true;
        }

        data = default;
        return false;
    }

    /// <summary>
    /// <b>해금된</b> 악세사리 중에서만 무작위로 하나 고른다(2026-08-19 Phase E). 하나도 해금되지
    /// 않았으면 false를 돌려주고, 상점은 그 칸을 무기/디스크로 채운다.
    ///
    /// 6종의 해금 조건이 전부 엔드리스 기준이고 그중 8비트 선글라스가 "엔드리스 상점에서
    /// 아이템 1회 구매"라, 처음 엔드리스에 들어가면 아무 물건이나 하나 사는 것으로 첫 칸이
    /// 열린다(악세사리를 사야 악세사리가 열리는 교착은 없다).
    /// </summary>
    public static bool TryGetRandomUnlocked(out AccessoryData data)
    {
        unlockedBuffer.Clear();
        foreach (AccessoryData d in All)
        {
            if (UnlockState.IsUnlocked(d.accessoryId)) unlockedBuffer.Add(d);
        }

        if (unlockedBuffer.Count == 0)
        {
            data = default;
            return false;
        }

        data = unlockedBuffer[Random.Range(0, unlockedBuffer.Count)];
        return true;
    }

    // 매 칸 추첨마다 리스트를 새로 만들지 않도록 재사용한다(상점은 웨이브마다 4칸씩 굴린다).
    private static readonly System.Collections.Generic.List<AccessoryData> unlockedBuffer =
        new System.Collections.Generic.List<AccessoryData>();
}
