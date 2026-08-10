using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터가 죽었을 때 골드/경험치 보상을 땅 위의 물리적 오브젝트로 만들어주는 매니저
/// (EnemyUnit.GrantKillRewards()가 호출한다). DropItemManager와 동일한 패턴 -
/// 씬에 따로 배치할 필요 없는 정적 유틸리티이며, 실제 획득 판정은 여기서 생성한
/// 오브젝트에 붙는 RewardPickup 컴포넌트가 트리거 콜라이더로 처리한다.
/// </summary>
public static class RewardPickupManager
{
    private const float PickupRange = 2f;    // 이 거리 안으로 플레이어가 들어오면 자동 습득
    // 2026-08-10 "인게임 모든 이미지 1/2" 지시로 0.7 → 0.35 (아이템 상자 0.6보다 조금 작게 표시)
    private const float VisualWorldSize = 0.35f;
    private const int SortingOrder = 5;      // 바닥/캐릭터에 가려지지 않도록

    private const string GoldResourceName = "Gold";
    private const string ExpResourceName = "Energy"; // 전용 EXP 아이콘이 아직 없어 임시로 사용 - 나중에 교체 권장
    private const string PartBoxResourceName = "ItemBox";

    private static Sprite gold_icon;
    private static Sprite exp_icon;
    private static Sprite part_box_icon;
    private static readonly HashSet<string> warned_missing_icons = new HashSet<string>();

    /// <summary>type/amount 보상을 world position 위치(약간의 무작위 산개 포함)에 생성한다.</summary>
    public static void SpawnReward(RewardType type, int amount, Vector3 position)
    {
        if (amount <= 0) return;

        // 한 번의 처치에서 골드+경험치가 같이 나오므로, 완전히 겹쳐 보이지 않도록 살짝 흩뿌린다
        Vector2 scatter = Random.insideUnitCircle * 0.6f;
        position += new Vector3(scatter.x, scatter.y, 0f);
        position.z = 0f; // X-Y 평면 규칙

        GameObject root = new GameObject($"Reward_{type}_{amount}");
        root.transform.position = position;

        SphereCollider pickup_collider = root.AddComponent<SphereCollider>();
        pickup_collider.isTrigger = true;
        pickup_collider.radius = PickupRange;

        // 트리거 이벤트가 안정적으로 발생하도록 최소한의(키네마틱) Rigidbody 부여
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        CreateVisual(root.transform, type);

        root.AddComponent<RewardPickup>().Init(type, amount);
    }

    private static void CreateVisual(Transform parent, RewardType type)
    {
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = ResolveIcon(type);
        sr.sortingOrder = SortingOrder;

        if (sr.sprite != null)
        {
            float spriteSize = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y, 0.0001f);
            visual.transform.localScale = Vector3.one * (VisualWorldSize / spriteSize);
        }
    }

    private static Sprite ResolveIcon(RewardType type)
    {
        switch (type)
        {
            case RewardType.Gold: return LoadIcon(ref gold_icon, GoldResourceName);
            case RewardType.PartBox: return LoadIcon(ref part_box_icon, PartBoxResourceName);
            default: return LoadIcon(ref exp_icon, ExpResourceName);
        }
    }

    private static Sprite LoadIcon(ref Sprite cache, string resourceName)
    {
        if (cache == null) cache = Resources.Load<Sprite>(resourceName);

        if (cache == null && warned_missing_icons.Add(resourceName))
        {
            Debug.LogWarning($"Resources/{resourceName}.png을(를) 찾을 수 없습니다. 해당 보상의 아이콘이 안 보일 수 있습니다.");
        }

        return cache;
    }
}
