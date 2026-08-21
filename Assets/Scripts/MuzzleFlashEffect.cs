using UnityEngine;

/// <summary>
/// 원거리 무기(투사체/빔)를 발사하는 순간 총구 끝에서 잠깐 재생되는 총구 화염 이펙트
/// (3프레임, 사용자 제공 Assets/Resources/MuzzleFlash, 2026-08-21).
/// <see cref="PlayerShootManager"/>가 근접무기를 제외한 모든 소켓 발사 시점에 호출한다.
///
/// DisruptorExplosionEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷
/// 이펙트 관례를 따른다. 판정에 쓰이는 반경이 없으므로 targetWidth(월드 유닛) 하나로
/// 스프라이트 실측 크기를 역산해 크기를 맞춘다.
/// </summary>
public class MuzzleFlashEffect : MonoBehaviour
{
    private const string ResourceFolder = "MuzzleFlash";
    private const float Fps = 24f; // 3프레임을 0.125초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>position에서 direction 방향을 바라보도록 targetWidth 크기로 한 번 재생한다.</summary>
    public static void Play(Vector3 position, Vector3 direction, float targetWidth, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("MuzzleFlash");
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

        var effect = go.AddComponent<MuzzleFlashEffect>();
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
                Debug.LogWarning($"MuzzleFlashEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
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
