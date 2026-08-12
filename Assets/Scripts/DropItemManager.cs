using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터가 죽어서 아이템을 드랍했을 때, 그 아이템을 땅 위의 물리적인 "아이템 상자"로 만들어주는 매니저.
/// (EnemyUnit.Die()에서 DropTableManager.RollDrop()으로 item_id를 뽑은 뒤 이 매니저를 호출한다)
///
/// 실제 획득 판정("일정 범위로 플레이어가 다가가면 습득")은 여기서 생성한 오브젝트에 붙는
/// ItemPickup 컴포넌트가 트리거 콜라이더로 처리한다.
///
/// 씬에 따로 배치할 필요 없는 정적 유틸리티 - 필요한 오브젝트를 그때그때 코드로 만든다.
/// </summary>
public static class DropItemManager
{
    // 2026-08-12 사용자 지적 "아이템 획득 반경이 PC 크기보다 과하게 큼" - RewardPickupManager와
    // 동일한 이유로 2 → 1 축소.
    private const float PickupRange = 1f;   // 이 거리 안으로 플레이어가 들어오면 자동 습득
    // 2026-08-10 "인게임 모든 이미지 1/2" 지시로 1.2 → 0.6. 습득 거리(PickupRange)는 그림이 아니라
    // 조작 편의 값이라 함께 줄이지 않았다.
    private const float BoxWorldSize = 0.6f; // 아이템 상자 스프라이트의 월드 크기(변 길이, 유닛)
    private const int SortingOrder = 5;      // 바닥/캐릭터에 가려지지 않도록

    // Assets/Resources 안에 있는 스프라이트 이름 (확장자 제외) - Resources.Load로 바로 불러온다
    private const string ItemBoxResourceName = "ItemBox";
    private const string GoldResourceName = "Gold";

    private static Sprite item_box_icon;
    private static Sprite gold_icon;
    private static readonly HashSet<string> warned_missing_icons = new HashSet<string>();

    /// <summary>
    /// itemId에 해당하는 아이템 상자를 world position 위치에 생성한다.
    /// </summary>
    public static void SpawnDrop(int itemId, Vector3 position)
    {
        position.z = 0f; // X-Y 평면 규칙

        GameObject root = new GameObject($"ItemBox_{itemId}");
        root.transform.position = position;

        // 픽업 판정용 콜라이더. 시각적 크기(Visual 자식)와 분리해서 반경이 서로 영향을 주지 않게 한다.
        SphereCollider pickup_collider = root.AddComponent<SphereCollider>();
        pickup_collider.isTrigger = true;
        pickup_collider.radius = PickupRange;

        // 트리거 이벤트가 안정적으로 발생하도록 최소한의(키네마틱) Rigidbody 부여
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        CreateVisual(root.transform, itemId);

        root.AddComponent<ItemPickup>().Init(itemId);
    }

    private static void CreateVisual(Transform parent, int itemId)
    {
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = ResolveIcon(itemId);
        sr.sortingOrder = SortingOrder;

        if (sr.sprite != null)
        {
            float spriteSize = Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y, 0.0001f);
            visual.transform.localScale = Vector3.one * (BoxWorldSize / spriteSize);
        }
    }

    // item_id 분류(무기/방어구/골드)에 따라 다른 그림을 보여준다.
    // - 골드(700000번대, DropTableManager.IsGold)      → Resources/Gold.png
    // - 그 외(무기/방어구 등 실제 장비 아이템)           → Resources/ItemBox.png (상자로 통일)
    // 아이템별 개별 아이콘이 데이터테이블에 추가되면 itemId(weapon_id/amor_id)로
    // GameDataManager.Weapons/Amors를 조회해 여기서 더 세분화하면 된다.
    private static Sprite ResolveIcon(int itemId)
    {
        if (DropTableManager.IsGold(itemId))
        {
            return LoadIcon(ref gold_icon, GoldResourceName);
        }

        return LoadIcon(ref item_box_icon, ItemBoxResourceName);
    }

    private static Sprite LoadIcon(ref Sprite cache, string resourceName)
    {
        if (cache == null) cache = Resources.Load<Sprite>(resourceName);

        if (cache == null && warned_missing_icons.Add(resourceName))
        {
            Debug.LogWarning($"Resources/{resourceName}.png을(를) 찾을 수 없습니다. 해당 아이템의 아이콘이 안 보일 수 있습니다.");
        }

        return cache;
    }
}
