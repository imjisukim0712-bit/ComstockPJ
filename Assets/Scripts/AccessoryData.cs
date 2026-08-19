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
            iconResourceName = "Accessories/8Bitsunglass-transparent", attachPoint = AccessoryAttachPoint.Face
        },
        new AccessoryData
        {
            accessoryId = 600002, accessoryName = "왕관", price = 100, score = 500,
            iconResourceName = "Accessories/Crown-transparent", attachPoint = AccessoryAttachPoint.Stack
        },
        new AccessoryData
        {
            accessoryId = 600003, accessoryName = "합격 목걸이", price = 150, score = 1000,
            iconResourceName = "Accessories/Passneck-transparent", attachPoint = AccessoryAttachPoint.Neck
        },
        new AccessoryData
        {
            accessoryId = 600004, accessoryName = "유니콘 뿔", price = 100, score = 400,
            iconResourceName = "Accessories/Unicon-transparent", attachPoint = AccessoryAttachPoint.Stack
        },
        new AccessoryData
        {
            accessoryId = 600005, accessoryName = "조이스틱", price = 50, score = 200,
            iconResourceName = "Accessories/Joystick-transparent", attachPoint = AccessoryAttachPoint.Stack
        },
        new AccessoryData
        {
            accessoryId = 600006, accessoryName = "의문의 검은 고양이 귀", price = 75, score = 444,
            iconResourceName = "Accessories/Kkami-transparent", attachPoint = AccessoryAttachPoint.Stack
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

    public static AccessoryData GetRandom() => All[Random.Range(0, All.Length)];
}
