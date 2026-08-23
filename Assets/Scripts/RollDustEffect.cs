using UnityEngine;

/// <summary>
/// 로봇이 구르기(Space)를 시작하는 순간 발밑에 재생되는 흙먼지 이펙트(3프레임, 사용자 제공
/// Assets/Resources/RollDust, 2026-08-23). <see cref="PlayerRobotController.TryStartDash"/>가
/// 구르기/폴짝 뛰기를 시작할 때마다 호출한다.
///
/// MuzzleFlashEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷 이펙트
/// 관례를 따른다. direction 방향으로 회전시켜 굴러가는 방향을 향해 먼지가 튀는 것처럼 보이게 한다.
/// </summary>
public class RollDustEffect : MonoBehaviour
{
    private const string ResourceFolder = "RollDust";
    private const float Fps = 12f; // 3프레임을 0.25초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>position에서 direction 방향을 바라보도록 targetWidth 크기로 한 번 재생한다.</summary>
    public static void Play(Vector3 position, Vector3 direction, float targetWidth, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("RollDust");
        go.transform.position = position;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_width = Mathf.Max(extents.x * 2f, 0.0001f);
        float scale = Mathf.Max(0.01f, targetWidth / unit_width);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<RollDustEffect>();
        effect.sprite_renderer = renderer;
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
