using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 투사체 프리팹에 붙여서 여러 프레임을 순환 재생하는 보조 컴포넌트(레이저피스톨 탄환
/// 이펙트 2프레임, 사용자 제공 Assets/Resources/LaserPistolBullet, 2026-08-21).
///
/// <see cref="Projectile"/> 자체는 건드리지 않는다 - 이동/충돌/관통/폭발 판정과 스프라이트
/// 애니메이션을 완전히 분리해서, 애니메이션이 없는 다른 무기(기본탄환/산탄 등)는 이 컴포넌트가
/// 프리팹에 없으므로 전혀 영향받지 않는다.
/// </summary>
public class ProjectileSpriteAnimator : MonoBehaviour
{
    [Tooltip("Resources 폴더 이름 (파일명 오름차순 = 재생 순서)")]
    [SerializeField] private string resourceFolder = "LaserPistolBullet";

    [Tooltip("초당 프레임 수")]
    [SerializeField] private float fps = 12f;

    // 폴더명 단위 캐시 - 같은 세트를 여러 투사체가 동시에 써도 한 번만 로드한다.
    private static readonly Dictionary<string, Sprite[]> folder_cache = new Dictionary<string, Sprite[]>();

    private SpriteRenderer sprite_renderer;
    private Sprite[] frames;
    private float phase;

    private void Awake()
    {
        sprite_renderer = GetComponent<SpriteRenderer>();
        frames = GetFrames(resourceFolder);
    }

    private static Sprite[] GetFrames(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return System.Array.Empty<Sprite>();
        if (folder_cache.TryGetValue(folder, out Sprite[] cached)) return cached;

        Sprite[] loaded = Resources.LoadAll<Sprite>(folder);
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
        folder_cache[folder] = loaded;

        if (loaded.Length == 0)
            Debug.LogWarning($"ProjectileSpriteAnimator: Resources/{folder}에서 스프라이트를 찾지 못했습니다.");

        return loaded;
    }

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => folder_cache.Clear();

    private void Update()
    {
        // 프레임이 1장 이하면(못 찾았을 때 포함) 프리팹에 미리 박혀 있는 정지 스프라이트를 그대로 둔다.
        if (sprite_renderer == null || frames == null || frames.Length < 2) return;

        phase += Time.deltaTime * fps;
        sprite_renderer.sprite = frames[Mathf.FloorToInt(phase) % frames.Length];
    }
}
