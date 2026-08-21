using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터가 죽었을 때 골드/경험치 보상을 땅 위의 물리적 오브젝트로 만들어주는 매니저
/// (EnemyUnit.GrantKillRewards()가 호출한다). 씬에 따로 배치할 필요 없는 정적 유틸리티이며,
/// 실제 획득 판정은 여기서 생성한 오브젝트에 붙는 RewardPickup 컴포넌트가 트리거 콜라이더로 처리한다.
/// </summary>
public static class RewardPickupManager
{
    // 2026-08-12 사용자 지적 "아이템 획득 반경이 PC 크기보다 과하게 큼" - 플레이어 몸통 반폭이
    // 약 0.33유닛인데 반경 2유닛은 6배가 넘어 몸에서 한참 떨어진 보상도 저절로 빨려들어왔다. 1로 축소.
    private const float PickupRange = 1f;    // 이 거리 안으로 플레이어가 들어오면 자동 습득
    private const int SortingOrder = 5;      // 바닥/캐릭터에 가려지지 않도록

    // 2026-08-21 사용자 제공 "드롭아이템 신규" 아이콘(경험치/금화 108x108, 상자 180x180) 적용 -
    // 상자가 골드/경험치보다 크게 보이도록 사용자가 픽셀 크기를 직접 맞춰 왔으므로, 예전처럼
    // "타입마다 다른 고정 월드 크기"로 강제로 다시 맞추지는 않는다(그러면 상자 배율이 사용자가
    // 정한 1.667배가 아니라 코드가 정한 값으로 덮어써진다).
    //
    // 다만 임포트 PPU(100)를 그대로 따라 스케일 1로 렌더링했더니 "너무 커졌다"는 재지적을
    // 받았다 - 108px가 그대로 1.08유닛이 되어 기존에 익숙했던 크기(0.6)보다 훨씬 컸다.
    // 그래서 세 아이콘 전부에 <b>같은 배율 하나</b>를 곱한다: 기준 아이콘(Gold)이 예전과 같은
    // ReferenceIconWorldSize(0.6)로 보이는 배율을 구해서 그대로 재사용 - 상대 비율(1.667배)은
    // 그대로 유지되면서 절대 크기만 익숙한 수준으로 돌아온다.
    private const string ReferenceIconResourceName = "Gold";
    private const float ReferenceIconWorldSize = 0.6f;
    private static float? icon_scale_multiplier;

    private const string GoldResourceName = "Gold";
    private const string ExpResourceName = "Exp"; // 2026-08-19 전용 EXP 아이콘 적용(Assets/Resources/Exp.png)
    private const string PartBoxResourceName = "ItemBox";

    private static Sprite gold_icon;
    private static Sprite exp_icon;
    private static Sprite part_box_icon;
    private static readonly HashSet<string> warned_missing_icons = new HashSet<string>();

    /// <summary>
    /// 기준 아이콘(Gold)의 실측 크기에서 역산한 배율. 모든 타입에 동일하게 곱하므로
    /// 아이콘 간 상대 크기(사용자가 정한 픽셀 비율)는 그대로 유지된다.
    /// </summary>
    private static float GetIconScaleMultiplier()
    {
        if (icon_scale_multiplier.HasValue) return icon_scale_multiplier.Value;

        Sprite reference = Resources.Load<Sprite>(ReferenceIconResourceName);
        float natural_size = reference != null
            ? Mathf.Max(reference.bounds.size.x, reference.bounds.size.y, 0.0001f)
            : ReferenceIconWorldSize;

        icon_scale_multiplier = ReferenceIconWorldSize / natural_size;
        return icon_scale_multiplier.Value;
    }

    /// <summary>type/amount 보상을 world position 위치(약간의 무작위 산개 포함)에 생성한다.</summary>
    public static void SpawnReward(RewardType type, int amount, Vector3 position)
    {
        if (amount <= 0) return;

        // 한 번의 처치에서 골드+경험치가 같이 나오므로, 완전히 겹쳐 보이지 않도록 살짝 흩뿌린다
        Vector2 scatter = Random.insideUnitCircle * 0.6f;
        position += new Vector3(scatter.x, scatter.y, 0f);
        position.z = 0f; // X-Y 평면 규칙

        // 2026-08-20 사용자 리포트 "가끔 상자가 맵 밖으로 나간다" - 몬스터가 맵 경계 근처에서
        // 죽으면 위 산개(scatter)가 맵 밖으로 밀어낼 수 있었다. 절반 크기(픽업이 화면 밖으로
        // 반쯖 삐져나오지 않도록)만큼 여유를 두고 맵 안으로 되접는다. MapBounds가 맵을 못 찾으면
        // 기존 동작 그대로(제한 없음) 통과한다.
        position = MapBounds.ClampPosition(position, GetVisualHalfSize(type));

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
        visual.transform.localScale = Vector3.one * GetIconScaleMultiplier();
    }

    /// <summary>맵 경계 클램프용 절반 크기(월드 유닛). 아이콘의 실측 크기에 렌더 배율을 곱한다.</summary>
    private static float GetVisualHalfSize(RewardType type)
    {
        Sprite icon = ResolveIcon(type);
        if (icon == null) return 0.3f; // 아이콘을 못 찾았을 때의 안전한 기본값

        return Mathf.Max(icon.bounds.extents.x, icon.bounds.extents.y) * GetIconScaleMultiplier();
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
