using System.Collections.Generic;
using UnityEngine;

public static class DropTableManager
{
    /// <summary>
    /// 누적합 방식 가중치 랜덤으로 해당 몬스터의 드랍 후보 중 1개를 뽑는다.
    /// 후보가 없거나 가중치 합이 0이면 null 반환.
    /// </summary>
    public static int? RollDrop(int monsterId)
    {
        if (!GameDataManager.Instance.DropsByMonster.TryGetValue(monsterId, out List<DropEntry> candidates) || candidates.Count == 0)
            return null;

        float total = 0f;
        foreach (var c in candidates) total += c.item_drop;
        if (total <= 0f) return null;

        float r = Random.Range(0f, total);
        float cumulative = 0f;

        foreach (var c in candidates)
        {
            cumulative += c.item_drop;
            if (r < cumulative) return c.item_id;
        }

        return candidates[candidates.Count - 1].item_id; // 부동소수점 오차 방지 폴백
    }

    // item_id 자릿수로 무기(300000번대)/방어구(400000번대) 구분
    public static bool IsWeapon(int itemId) => itemId >= 300000 && itemId < 400000;
    public static bool IsArmor(int itemId) => itemId >= 400000 && itemId < 500000;
}
