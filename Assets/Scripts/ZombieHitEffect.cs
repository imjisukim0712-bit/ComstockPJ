using UnityEngine;

/// <summary>
/// 좀비(적)가 피해를 입는 순간 맞은 자리에 재생되는 타격 스파크 이펙트(8프레임, 사용자 제공
/// Assets/Resources/ZombieHitEffect, 2026-08-23). <see cref="EnemyUnit.TakeDamage"/>가 매
/// 피격마다 호출한다.
///
/// 기존 HitFlash(몸 전체가 잠깐 흰색으로 물드는 셰이더 연출)와는 완전히 별개다 - 이쪽은
/// "맞은 지점"에 따로 생성되는 스프라이트 오브젝트라 서로 간섭하지 않는다(동시에 재생된다).
///
/// MuzzleFlashEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷 이펙트
/// 관례를 따른다.
/// </summary>
public class ZombieHitEffect : MonoBehaviour
{
    private const string ResourceFolder = "ZombieHitEffect";
    private const float Fps = 24f; // 8프레임을 약 0.33초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>position에 targetWidth 크기로 한 번 재생한다.</summary>
    public static void Play(Vector3 position, float targetWidth, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("ZombieHitEffect");
        go.transform.position = position;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_width = Mathf.Max(extents.x * 2f, 0.0001f);
        float scale = Mathf.Max(0.01f, targetWidth / unit_width);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<ZombieHitEffect>();
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
                Debug.LogWarning($"ZombieHitEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
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
