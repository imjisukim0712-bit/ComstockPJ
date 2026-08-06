using UnityEngine;

/// <summary>
/// 땅에 떨어진 아이템 상자 하나. 플레이어가 이 오브젝트의 SphereCollider(트리거) 범위 안에
/// 들어오면 자동으로 습득되어 PlayerInventory에 더해지고 사라진다.
/// DropItemManager.SpawnDrop()이 생성 직후 Init()으로 itemId를 넘겨준다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ItemPickup : MonoBehaviour
{
    public int ItemId { get; private set; }
    private bool collected;

    public void Init(int itemId)
    {
        ItemId = itemId;
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
    /// 플레이어가 닿지 않았어도 즉시 습득 처리한다. RewardPickup과 같은 이유로,
    /// 정비 화면 진입 시 GameFlowManager가 필드를 정리하면서 호출한다.
    /// </summary>
    public void CollectImmediately()
    {
        if (collected) return;

        collected = true;
        PlayerInventory.AddItem(ItemId, 1);
        Destroy(gameObject);
    }
}
