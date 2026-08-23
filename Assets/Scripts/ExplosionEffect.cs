using UnityEngine;

/// <summary>
/// 스플래시(폭발) 무기가 터질 때 재생되는 폭발 애니메이션(10프레임, 사용자 제공
/// Assets/Resources/Explosion, 2026-08-23). <see cref="Projectile.Explode"/>가 호출한다.
///
/// 예전에는 날아온 투사체 자신의 스프라이트를 폭발 반경만큼 확대해서 보여줬다(전용 이펙트가
/// 없었을 때의 임시 처리). 이 이펙트가 생기면서
/// 그 자리를 대신한다 - 디스럭터 자폭(DisruptorExplosionEffect)과는 별개의 클래스로 둔다
/// (사용자 지정: "다른 폭발형 무기 등은 제외"였던 디스럭터 전용 이펙트와 반대로, 이쪽은
/// 로켓런처/유탄발사기 등 <b>일반 스플래시 무기 전용</b>이다).
///
/// 스프라이트 크기는 하드코딩하지 않고 실제 스프라이트에서 읽어 폭발 반경과 맞춘다
/// (DisruptorExplosionEffect.Play와 같은 계산 방식 - 프레임 크기가 바뀌어도 저절로 맞는다).
/// </summary>
public class ExplosionEffect : MonoBehaviour
{
    private const string ResourceFolder = "Explosion";
    private const float Fps = 20f; // 10프레임을 0.5초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>지정한 위치에 explosionRadius 지름 크기로 폭발 연출을 한 번 재생한다.</summary>
    public static void Play(Vector3 position, float explosionRadius, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("Explosion");
        go.transform.position = position;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_radius = Mathf.Max(extents.x, extents.y, 0.0001f);
        float scale = Mathf.Max(0.01f, explosionRadius / unit_radius);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<ExplosionEffect>();
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
                Debug.LogWarning($"ExplosionEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
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
