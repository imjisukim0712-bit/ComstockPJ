using UnityEngine;

/// <summary>
/// 로봇이 구르기(Space)를 시작하는 순간 발밑에 재생되는 흙먼지 이펙트(3프레임, 사용자 제공
/// Assets/Resources/RollDust, 2026-08-23). <see cref="PlayerRobotController.TryStartDash"/>가
/// 구르기/폴짝 뛰기를 시작할 때마다 호출한다.
///
/// MuzzleFlashEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷 이펙트
/// 관례를 따른다.
///
/// <b>방향은 "좌우 반전 + 절대 뒤집히지 않는 회전"으로 표현한다</b>(2026-08-25).
///
/// 이 먼지 그림은 <b>바닥에 닿는 평평한 선이 아래</b>에 있고 먼지가 위로 피어오르는 구도다.
/// 처음에는 <c>Atan2(direction)</c>를 그대로 z 회전으로 넣었는데, 그러면 회전이 180도 근처가
/// 되는 방향에서 그림이 통째로 뒤집혀 먼지가 땅속에서 아래로 솟았다(사용자 리포트: "우측구르기
/// 상하반전을 수정하니 이번엔 좌측구르기가 상하반전이 되어버렸어"). 회전각을 한쪽에 맞추면
/// 반대쪽이 반드시 뒤집히는 구조다.
///
/// 그래서 <b>왼쪽 반구는 세로축 대칭(flipX)으로 접어서</b> 회전각을 항상 -90~+90도 안에 둔다.
/// 좌우로 구르면 회전 0(바닥선이 수평), 위/아래로 구르면 ±90도(<b>수직</b>), 대각선이면 그 사이
/// 각도가 되고, <b>어느 방향에서도 그림이 상하로 뒤집히지 않는다</b>
/// (2차 리포트: "좌우 방향은 제대로 되었으나 상하 방향 구르기는 수직방향이어야 한다").
///
/// <b>바닥선을 발바닥에 맞춘다</b> — 원본 512px 캔버스에서 그림은 아래쪽(밑단에서 62px 위)에만
/// 있는데 pivot은 캔버스 중앙이라, 오브젝트를 플레이어 위치에 그냥 두면 그림 바닥선이 실제로는
/// <b>0.64유닛 아래</b>에 그려졌다(실측). 메시 최하단을 재서 그 지점이 <paramref name="position"/>에
/// 오도록 올린다. 회전이 걸린 뒤에도 바닥선 중앙이 고정되도록 오프셋에 같은 회전을 적용한다.
/// </summary>
public class RollDustEffect : MonoBehaviour
{
    private const string ResourceFolder = "RollDust";
    private const float Fps = 12f; // 3프레임을 0.25초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    private Transform follow_target;
    private Vector3 follow_origin;
    private Vector3 spawn_position;
    private float follow_ratio;

    /// <summary>
    /// <paramref name="position"/>(발바닥)에 그림 바닥선을 맞춰 targetWidth 크기로 한 번 재생한다.
    /// <paramref name="direction"/>(먼지가 튀는 쪽)은 좌우 반전 + -90~+90도 회전으로 반영하므로
    /// 그림이 상하로 뒤집히는 일이 없다.
    ///
    /// <paramref name="followTarget"/>과 <paramref name="followRatio"/>를 주면 재생 내내 그 대상이
    /// 움직인 거리의 일정 비율만큼 <b>따라간다</b>(2026-08-25 사용자 요청: "좌우로 구를때 좀 멀리서
    /// 생성돼. 좀 더 캐릭터 쪽으로 붙여"). 먼지는 발밑에 정확히 생기지만 구르기가 0.28초에 2.5유닛을
    /// 이동해서, 0.25초짜리 먼지가 끝날 때쯤 캐릭터가 <b>2.2유닛(몸 두 개 거리)</b> 앞에 가 있어
    /// 멀리 떨어져 보였다. 비율 1이면 완전히 붙어 미끄러지는 것처럼 보이므로 일부만 따라가게 한다.
    /// </summary>
    public static void Play(Vector3 position, Vector3 direction, float targetWidth, int sortingOrder,
                            Transform followTarget = null, float followRatio = 0f)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("RollDust");

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_width = Mathf.Max(extents.x * 2f, 0.0001f);
        float scale = Mathf.Max(0.01f, targetWidth / unit_width);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        // 왼쪽 반구는 세로축 대칭으로 접는다 - 회전각이 항상 -90~+90도라 위아래가 뒤집히지 않는다.
        float aim_degrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        bool mirror = Mathf.Abs(aim_degrees) > 90f;
        float rotation_degrees = mirror ? Mathf.Sign(aim_degrees) * 180f - aim_degrees : aim_degrees;

        renderer.flipX = mirror;
        Quaternion rotation = Quaternion.Euler(0f, 0f, rotation_degrees);
        go.transform.rotation = rotation;

        // 그림 바닥선이 position에 오도록 올린다. 회전 뒤에도 바닥선 중앙이 그 자리에 남도록
        // 오프셋에 같은 회전을 먹인다(회전 중심은 pivot = 캔버스 중앙이다).
        float art_bottom_local = SpriteArtBottomLocalY(renderer.sprite);
        go.transform.position = position - rotation * new Vector3(0f, art_bottom_local * scale, 0f);

        var effect = go.AddComponent<RollDustEffect>();
        effect.sprite_renderer = renderer;
        effect.spawn_position = go.transform.position;
        effect.follow_ratio = Mathf.Clamp01(followRatio);

        if (followTarget != null && effect.follow_ratio > 0f)
        {
            effect.follow_target = followTarget;
            effect.follow_origin = followTarget.position;
        }
    }

    private void LateUpdate()
    {
        if (follow_target == null) return;

        // 대상이 움직인 거리의 일정 비율만 따라간다(전부 따라가면 먼지가 땅을 미끄러지는 것처럼 보인다).
        transform.position = spawn_position + (follow_target.position - follow_origin) * follow_ratio;
    }

    /// <summary>스프라이트 메시에서 실제 그림의 아래 끝(pivot 기준 로컬 y, 스케일 1).
    /// 프레임 3장의 밑단이 1px 안에서 같아서 첫 프레임 값을 그대로 쓴다.</summary>
    private static float SpriteArtBottomLocalY(Sprite sprite)
    {
        if (sprite == null) return 0f;

        Vector2[] verts = sprite.vertices;
        if (verts == null || verts.Length == 0) return -sprite.bounds.extents.y;

        float min = float.MaxValue;
        for (int i = 0; i < verts.Length; i++) if (verts[i].y < min) min = verts[i].y;
        return min;
    }

    private static Sprite[] GetFrames()
    {
        if (cached_frames == null)
        {
            Sprite[] loaded = Resources.LoadAll<Sprite>(ResourceFolder);
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
            cached_frames = loaded;

            if (loaded.Length == 0)
                Debug.LogWarning($"RollDustEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
        }

        return cached_frames;
    }

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => cached_frames = null;

    private void Update()
    {
        if (GameFlowManager.IsIntermission)
        {
            Destroy(gameObject);
            return;
        }

        frame_timer += Time.deltaTime;
        if (frame_timer < 1f / Fps) return;
        frame_timer = 0f;

        frame_index++;
        if (cached_frames == null || frame_index >= cached_frames.Length)
        {
            Destroy(gameObject);
            return;
        }

        sprite_renderer.sprite = cached_frames[frame_index];
    }
}
