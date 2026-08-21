using UnityEngine;

/// <summary>
/// 디스럭터가 자폭할 때 한 번만 재생되는 폭발 애니메이션(8프레임, 사용자 제공
/// `Assets/Resources/DisruptorExplosion`, 2026-08-21). <see cref="DisruptorUnit.Explode"/>가
/// 호출한다.
///
/// <b>다른 폭발형 무기(로켓런처 등)의 스플래시 연출과는 완전히 별개다</b>(사용자 지정: "다른
/// 폭발형 무기 등은 제외") - 그쪽은 <see cref="Projectile.ComputeBlastVisualScale"/>이 만드는
/// 원형 스프라이트를 그대로 쓰고, 이 클래스를 공유하지 않는다.
///
/// 스프라이트 크기는 하드코딩하지 않고 실제 스프라이트에서 읽어 폭발 반경과 맞춘다
/// (Projectile.ComputeBlastVisualScale과 같은 계산 방식 - 프레임 크기가 바뀌어도 저절로 맞는다).
/// </summary>
public class DisruptorExplosionEffect : MonoBehaviour
{
    private const string ResourceFolder = "DisruptorExplosion";
    private const float Fps = 16f; // 8프레임을 0.5초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>지정한 위치에 explosionRadius 지름 크기로 폭발 연출을 한 번 재생한다.</summary>
    public static void Play(Vector3 position, float explosionRadius, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("DisruptorExplosion");
        go.transform.position = position;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_radius = Mathf.Max(extents.x, extents.y, 0.0001f);
        float scale = Mathf.Max(0.01f, explosionRadius / unit_radius);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<DisruptorExplosionEffect>();
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
                Debug.LogWarning($"DisruptorExplosionEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
        }

        return cached_frames;
    }

    /// <summary>씬 재시작 시 이전 판의 캐시가 남지 않도록 비운다(MonsterAnimationLibrary.ResetCache와 같은 이유).</summary>
    public static void ResetCache() => cached_frames = null;

    private void Update()
    {
        // 정비 화면으로 넘어가는 순간 Time.timeScale이 0이 되어 frame_timer가 멈출 수 있다 -
        // DamageNumberUI/HitFlash와 같은 관례로, 재생 중이던 이펙트가 화면에 얼어붙어 남지
        // 않도록 즉시 지운다(2026-08-21).
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
