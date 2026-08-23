using UnityEngine;

/// <summary>
/// AI 코어가 레벨업하는 순간 플레이어 위치에 재생되는 이펙트(24프레임, 사용자 제공
/// Assets/Resources/LevelUpEffect, 2026-08-23). <see cref="AiCoreManager.HandleRunStateChanged"/>가
/// 실제로 레벨이 오른 프레임에 한 번 호출한다(한 프레임에 여러 레벨이 오르더라도 한 번만 재생).
///
/// MuzzleFlashEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷 이펙트
/// 관례를 따른다.
/// </summary>
public class LevelUpEffect : MonoBehaviour
{
    private const string ResourceFolder = "LevelUpEffect";
    private const float Fps = 24f; // 24프레임을 1초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    /// <summary>position에 targetWidth 크기로 한 번 재생한다.</summary>
    public static void Play(Vector3 position, float targetWidth, int sortingOrder)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("LevelUpEffect");
        go.transform.position = position;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_width = Mathf.Max(extents.x * 2f, 0.0001f);
        float scale = Mathf.Max(0.01f, targetWidth / unit_width);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<LevelUpEffect>();
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
                Debug.LogWarning($"LevelUpEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
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
