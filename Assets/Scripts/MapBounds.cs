using UnityEngine;

/// <summary>
/// 맵(배경 스프라이트)의 월드 영역을 알려주는 정적 유틸리티.
/// PlayerInventory / DropTableManager 등과 같은 "씬에 배치할 필요 없는 static class" 패턴을 따른다.
///
/// <b>맵 크기를 코드에 상수로 박지 않는다.</b> 배경 오브젝트(<see cref="MapObjectName"/>)의
/// SpriteRenderer가 실제로 차지하는 월드 bounds를 그대로 읽으므로, 인스펙터에서 배경의
/// 스케일이나 이미지를 바꾸면 플레이어 이동 한계와 카메라 한계가 저절로 따라온다
/// (2026-08-10 배경을 원본 스케일로 되돌리면서 신설 - 그전에는 맵 경계 개념 자체가 없어서
///  플레이어가 배경 밖 빈 공간으로 걸어나갈 수 있었다).
/// </summary>
public static class MapBounds
{
    /// <summary>배경 역할을 하는 씬 오브젝트의 이름. Ground01 씬의 배경 오브젝트와 같아야 한다.</summary>
    public const string MapObjectName = "map";

    private static Transform map_root;
    private static Bounds cached_bounds;
    private static bool has_cached_bounds;
    private static bool warned_missing;

    /// <summary>맵을 찾았는지. 못 찾았으면 호출부는 경계 제한을 적용하지 않는다(기존 동작 유지).</summary>
    public static bool HasBounds => TryResolveBounds(out _);

    /// <summary>맵의 월드 bounds. 맵을 못 찾으면 size가 0인 bounds를 돌려준다.</summary>
    public static Bounds WorldBounds => TryResolveBounds(out Bounds b) ? b : new Bounds(Vector3.zero, Vector3.zero);

    /// <summary>
    /// 씬 재시작 시 이전 판의 파괴된 렌더러 참조가 남지 않도록 캐시를 비운다.
    /// PlayerRobotController.Awake()가 다른 static 초기화와 함께 호출한다.
    /// </summary>
    public static void ResetCache()
    {
        map_root = null;
        has_cached_bounds = false;
        warned_missing = false;
    }

    /// <summary>
    /// 배경 오브젝트와 <b>그 자식들</b>의 SpriteRenderer bounds를 전부 합쳐서 맵 영역을 구한다.
    ///
    /// 자식까지 훑는 이유: 2026-08-10 "배경을 1/2 크기로 줄이고 4장 이어붙이기"를 적용하면서
    /// 배경이 렌더러 1개에서 <b>미러 타일 4장(자식)</b> 구성으로 바뀌었다. 부모의 렌더러만
    /// 읽으면 맵이 1/4로 인식된다. 꺼져 있는 렌더러는 제외하므로, 예전 단일 렌더러를 비활성
    /// 상태로 남겨둬도 경계 계산에 끼어들지 않는다.
    /// </summary>
    private static bool TryResolveBounds(out Bounds bounds)
    {
        if (has_cached_bounds && map_root != null)
        {
            bounds = cached_bounds;
            return true;
        }

        bounds = new Bounds(Vector3.zero, Vector3.zero);

        GameObject map = map_root != null ? map_root.gameObject : GameObject.Find(MapObjectName);
        if (map == null)
        {
            if (!warned_missing)
            {
                warned_missing = true;
                Debug.LogWarning($"MapBounds: '{MapObjectName}' 오브젝트를 찾을 수 없어 맵 경계 제한이 적용되지 않습니다.");
            }
            return false;
        }

        bool found = false;
        foreach (SpriteRenderer sr in map.GetComponentsInChildren<SpriteRenderer>(includeInactive: false))
        {
            if (!sr.enabled) continue;

            if (!found) { bounds = sr.bounds; found = true; }
            else bounds.Encapsulate(sr.bounds);
        }

        if (!found)
        {
            if (!warned_missing)
            {
                warned_missing = true;
                Debug.LogWarning($"MapBounds: '{MapObjectName}'에 켜져 있는 SpriteRenderer가 없어 맵 경계 제한이 적용되지 않습니다.");
            }
            return false;
        }

        map_root = map.transform;
        cached_bounds = bounds;
        has_cached_bounds = true;
        return true;
    }

    /// <summary>
    /// 위치를 맵 안으로 잘라낸다. <paramref name="margin"/>은 경계에서 추가로 안쪽으로
    /// 밀어넣을 여유(플레이어 몸 반지름 등)다. z는 건드리지 않는다.
    /// 맵을 못 찾으면 입력값을 그대로 돌려준다.
    /// </summary>
    public static Vector3 ClampPosition(Vector3 position, float margin = 0f)
    {
        if (!TryResolveBounds(out Bounds b)) return position;

        // 여유를 빼고 나면 반대로 뒤집히는(맵보다 여유가 큰) 경우가 있으므로 중심으로 접어준다.
        float halfX = Mathf.Max(0f, b.extents.x - margin);
        float halfY = Mathf.Max(0f, b.extents.y - margin);

        position.x = Mathf.Clamp(position.x, b.center.x - halfX, b.center.x + halfX);
        position.y = Mathf.Clamp(position.y, b.center.y - halfY, b.center.y + halfY);
        return position;
    }

    /// <summary>
    /// 카메라 중심을 "화면에 맵 바깥이 보이지 않는" 범위로 잘라낸다.
    /// <paramref name="halfWidth"/>/<paramref name="halfHeight"/>는 카메라가 z=0 평면에서
    /// 보여주는 범위의 절반 크기다. 맵이 화면보다 작은 축은 맵 중앙에 고정한다.
    /// </summary>
    public static Vector3 ClampCameraCenter(Vector3 center, float halfWidth, float halfHeight)
    {
        if (!TryResolveBounds(out Bounds b)) return center;

        center.x = ClampAxis(center.x, b.center.x, b.extents.x, halfWidth);
        center.y = ClampAxis(center.y, b.center.y, b.extents.y, halfHeight);
        return center;
    }

    private static float ClampAxis(float value, float mapCenter, float mapExtent, float viewHalf)
    {
        // 맵이 화면보다 좁으면 카메라를 아무리 움직여도 맵 밖이 보이므로 맵 중앙에 고정한다.
        if (viewHalf >= mapExtent) return mapCenter;

        float limit = mapExtent - viewHalf;
        return Mathf.Clamp(value, mapCenter - limit, mapCenter + limit);
    }
}
