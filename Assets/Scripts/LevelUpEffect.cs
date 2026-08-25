using UnityEngine;

/// <summary>
/// AI 코어가 레벨업하는 순간 플레이어 머리에서 재생되는 이펙트(24프레임, 사용자 제공
/// Assets/Resources/LevelUpEffect, 2026-08-23). <see cref="AiCoreManager.HandleRunStateChanged"/>가
/// 실제로 레벨이 오른 프레임에 한 번 호출한다(한 프레임에 여러 레벨이 오르더라도 한 번만 재생).
///
/// MuzzleFlashEffect와 같은 "생성 시점에 필요한 값만 넘기고 알아서 사라지는" 원샷 이펙트
/// 관례를 따르되, <b>따라갈 대상</b>만 예외적으로 계속 참조한다(2026-08-25 사용자 요청 -
/// "로봇 머리에 붙어있으면 좋겠어. 지금은 제자리에서만 나와"). 1초짜리 연출인데 플레이어는
/// 그동안 계속 움직이므로, 생성 시점 좌표에 고정하면 이펙트만 뒤에 남았다.
///
/// <b>부모로 붙이지 않고 매 프레임 위치만 따라가는 이유</b> — 머리(<c>bodyVisual</c>)는
/// 구르기 중 360도 회전하고 걷는 동안 스쿼시(<c>localScale</c> 변형)가 걸린다. 자식으로 붙이면
/// 그 회전·변형을 그대로 물려받아 이펙트가 같이 돌고 찌그러진다.
/// </summary>
public class LevelUpEffect : MonoBehaviour
{
    private const string ResourceFolder = "LevelUpEffect";
    private const float Fps = 24f; // 24프레임을 1초 안에 재생하고 끝낸다

    private static Sprite[] cached_frames;

    private SpriteRenderer sprite_renderer;
    private int frame_index;
    private float frame_timer;

    private Transform follow_target;
    private Vector3 follow_offset;

    /// <summary>position에 targetWidth 크기로 한 번 재생한다.
    /// <paramref name="followTarget"/>을 주면 재생 내내 그 Transform의 위치를 따라간다
    /// (생성 시점의 상대 위치를 유지한다). 대상이 도중에 파괴되면 마지막 위치에 남아 재생을 마친다.</summary>
    public static void Play(Vector3 position, float targetWidth, int sortingOrder, Transform followTarget = null)
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
        effect.follow_target = followTarget;
        if (followTarget != null) effect.follow_offset = position - followTarget.position;
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

    private void LateUpdate()
    {
        // 리그가 이번 프레임의 머리 위치를 정한 뒤에 따라붙어야 한 프레임 뒤처지지 않는다
        // (ProceduralCharacterRig는 Update에서 몸통을 배치한다).
        if (follow_target != null) transform.position = follow_target.position + follow_offset;
    }

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
