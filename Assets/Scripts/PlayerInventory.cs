using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아주 단순한 정적 인벤토리. item_id별 보유 개수만 기록한다.
/// 실제 장비 장착/UI 표시는 아직 없으므로, 필요해지면 OnItemAcquired 이벤트에 구독해서 연결하면 된다.
/// </summary>
public static class PlayerInventory
{
    private static readonly Dictionary<int, int> counts = new Dictionary<int, int>();

    // (itemId, 획득 후 총 보유 개수)
    public static event Action<int, int> OnItemAcquired;

    public static void AddItem(int itemId, int amount = 1)
    {
        if (!counts.ContainsKey(itemId)) counts[itemId] = 0;
        counts[itemId] += amount;

        string category = DropTableManager.IsWeapon(itemId) ? "무기"
                         : DropTableManager.IsArmor(itemId) ? "방어구"
                         : "아이템";
        Debug.Log($"{category} {itemId} 획득 (보유: {counts[itemId]}개)");

        OnItemAcquired?.Invoke(itemId, counts[itemId]);
    }

    public static int GetCount(int itemId) => counts.TryGetValue(itemId, out int c) ? c : 0;

    // 씬 재시작(재시도) 시 이전 판의 인벤토리가 남아있지 않도록 초기화할 때 사용
    public static void Reset()
    {
        counts.Clear();
        OnItemAcquired = null;
    }
}
