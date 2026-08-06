using UnityEngine;

public enum RewardType
{
    Gold,
    Exp,
    PartBox // 부품 상자 - 습득 즉시 개봉되지 않고 RunState.UnopenedPartBoxCount만 늘어난다(개봉은 정비 화면에서)
}

/// <summary>
/// 골드/AI 코어 경험치 보상 하나를 필드 위의 물리적 오브젝트로 표현한다.
/// ItemPickup과 동일한 패턴: 플레이어가 SphereCollider(트리거) 범위 안에 들어오면
/// 자동으로 흡수되어 RunState에 더해지고 사라진다.
/// RewardPickupManager.SpawnReward()가 생성 직후 Init()으로 종류/수량을 넘겨준다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RewardPickup : MonoBehaviour
{
    public RewardType Type { get; private set; }
    public int Amount { get; private set; }

    private bool collected;

    public void Init(RewardType type, int amount)
    {
        Type = type;
        Amount = amount;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;

        PlayerRobotController player = other.GetComponent<PlayerRobotController>();
        if (player == null) player = other.GetComponentInParent<PlayerRobotController>();
        if (player == null) return; // 플레이어 외의 다른 콜라이더(투사체 등)와는 반응하지 않음

        CollectImmediately();
    }

    /// <summary>
    /// 플레이어가 닿지 않았어도 즉시 수령 처리한다. 웨이브가 끝나 정비 화면으로 넘어갈 때
    /// GameFlowManager가 필드에 남은 픽업을 정리하면서 호출한다 - 그냥 지우면 못 주운 보상이
    /// 사라져 플레이어가 손해를 보기 때문.
    /// </summary>
    public void CollectImmediately()
    {
        if (collected) return;
        collected = true;

        switch (Type)
        {
            case RewardType.Gold: RunState.Gold += Amount; break;
            case RewardType.PartBox: RunState.UnopenedPartBoxCount += Amount; break;
            default: RunState.CoreExp += Amount; break;
        }

        RunState.NotifyChanged();
        Destroy(gameObject);
    }
}
