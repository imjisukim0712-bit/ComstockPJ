using UnityEngine;

/// <summary>
/// 보스(좀비 군집체)가 그로기 상태일 때 머리 위에 표시되는 회전하는 별(기절) 이펙트
/// (6프레임, 사용자 제공 Assets/Resources/BossGroggyStars, 2026-08-21).
/// <see cref="BossUnit"/>이 그로기 시작 시 한 번 생성하고, 지정한 duration 동안 프레임을
/// 순환 재생한 뒤 스스로 파괴된다(DisruptorExplosionEffect와 같은 "생성 시점에 필요한 값만
/// 넘기고 알아서 사라지는" 원샷 이펙트 관례).
///
/// 별 이펙트는 데미지 판정에 쓰이는 반경 같은 게 없어 크기 기준이 없다 - 그래서 폭발
/// 이펙트처럼 판정 반경에 맞추는 대신, 호출부가 넘긴 targetWidth(월드 유닛) 하나로
/// 스프라이트 실측 크기를 역산한다(DisruptorExplosionEffect.Play와 같은 계산 방식).
/// </summary>
public class GroggyStarsEffect : MonoBehaviour
{
    private const string ResourceFolder = "BossGroggyStars";
    private const float Fps = 8f;

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;
    private float remaining;

    /// <summary>
    /// parent 머리 위(heightOffset, 로컬 유닛)에 targetWidth 크기로 duration 동안 재생한다.
    /// parent의 자식으로 붙으므로 보스가 살짝 움직여도 함께 따라간다.
    /// </summary>
    public static void Play(Transform parent, float heightOffset, float duration, int sortingOrder, float targetWidth)
    {
        Sprite[] frames = GetFrames();
        if (frames.Length == 0) return;

        var go = new GameObject("GroggyStars");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, heightOffset, 0f);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = frames[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 extents = renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_width = Mathf.Max(extents.x * 2f, 0.0001f);
        float scale = Mathf.Max(0.01f, targetWidth / unit_width);
        go.transform.localScale = new Vector3(scale, scale, 1f);

        var effect = go.AddComponent<GroggyStarsEffect>();
        effect.sprite_renderer = renderer;
        effect.remaining = duration;
    }

    private static Sprite[] GetFrames()
    {
        if (cached_frames == null)
        {
            Sprite[] loaded = Resources.LoadAll<Sprite>(ResourceFolder);
            System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
            cached_frames = loaded;

            if (loaded.Length == 0)
                Debug.LogWarning($"GroggyStarsEffect: Resources/{ResourceFolder}에서 스프라이트를 찾지 못했습니다.");
        }

        return cached_frames;
    }

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => cached_frames = null;

    private void Update()
    {
        // 정비 화면 전환 시 Time.timeScale=0이라 그로기 코루틴(BossUnit)도 같이 멈추지만,
        // 혹시 보스가 그 전에 파괴되는 경로(웨이브 종료 DespawnAllAliveEnemies 등)를 타면
        // 이 이펙트만 부모를 잃고 남을 수 있어 DisruptorExplosionEffect와 같은 안전장치를 둔다.
        if (GameFlowManager.IsIntermission || transform.parent == null)
        {
            Destroy(gameObject);
            return;
        }

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        frame_timer += Time.deltaTime;
        if (frame_timer < 1f / Fps) return;
        frame_timer = 0f;

        frame_index = (frame_index + 1) % cached_frames.Length;
        sprite_renderer.sprite = cached_frames[frame_index];
    }
}
