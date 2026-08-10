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

    private static SpriteRenderer map_renderer;
    private static bool warned_missing;

    /// <summary>맵을 찾았는지. 못 찾았으면 호출부는 경계 제한을 적용하지 않는다(기존 동작 유지).</summary>
    public static bool HasBounds => ResolveRenderer() != null;

    /// <summary>맵의 월드 bounds. 맵을 못 찾으면 size가 0인 bounds를 돌려준다.</summary>
    public static Bounds WorldBounds
    {
        get
        {
            SpriteRenderer sr = ResolveRenderer();
            return sr != null ? sr.bounds : new Bounds(Vector3.zero, Vector3.zero);
        }
    }

    /// <summary>
    /// 씬 재시작 시 이전 판의 파괴된 렌더러 참조가 남지 않도록 캐시를 비운다.
    /// PlayerRobotController.Awake()가 다른 static 초기화와 함께 호출한다.
    /// </summary>
    public static void ResetCache()
    {
        map_renderer = null;
        warned_missing = false;
    }

    private static SpriteRenderer ResolveRenderer()
    {
        if (map_renderer != null) return map_renderer;

        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            if (!warned_missing)
            {
                warned_missing = true;
                Debug.LogWarning($"MapBounds: '{MapObjectName}' 오브젝트를 찾을 수 없어 맵 경계 제한이 적용되지 않습니다.");
            }
            return null;
        }

        map_renderer = map.GetComponent<SpriteRenderer>();
        return map_renderer;
    }

    /// <summary>
    /// 위치를 맵 안으로 잘라낸다. <paramref name="margin"/>은 경계에서 추가로 안쪽으로
    /// 밀어넣을 여유(플레이어 몸 반지름 등)다. z는 건드리지 않는다.
    /// 맵을 못 찾으면 입력값을 그대로 돌려준다.
    /// </summary>
    public static Vector3 ClampPosition(Vector3 position, float margin = 0f)
    {
        SpriteRenderer sr = ResolveRenderer();
        if (sr == null) return position;

        Bounds b = sr.bounds;

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
        SpriteRenderer sr = ResolveRenderer();
        if (sr == null) return center;

        Bounds b = sr.bounds;

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
