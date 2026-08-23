using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스(좀비 군집체) 전용 프레임 이펙트의 공용 재생기(2026-08-23 보스 구현 시 신설).
///
/// 이 프로젝트의 원샷 이펙트 관례는 "이펙트 하나 = 클래스 하나"였다(MuzzleFlashEffect /
/// DisruptorExplosionEffect / ChargeWarningEffect / ZombieHitEffect / RollDustEffect /
/// LevelUpEffect / GroggyStarsEffect / ExplosionEffect). 그런데 보스 하나에만 성격이 똑같은
/// 이펙트가 <b>5종</b>(소환 이펙트 99프레임 / 사망 폭발 60 / 잔해 낙하 18 / 잔해 낙하 주의 16 /
/// 돌진 경고 14) 붙어서, 같은 코드를 다섯 번 복사하는 대신 폴더명만 바꿔 쓰는 재생기 하나로
/// 묶었다. 기존 이펙트 클래스들은 각자 고유한 인자(판정 반경 역산, 부모 추적 등)가 있어
/// 그대로 둔다 - 이 클래스는 <b>보스 신규 이펙트 전용</b>이다.
///
/// 기존 관례에서 그대로 가져온 것: 폴더 단위 정적 캐시 + <see cref="ResetCache"/>(씬 재로드로
/// Resources가 언로드됐을 때 대비 - EnemyUnit.ResetStaticCaches에서 호출), 정비 화면
/// (<see cref="GameFlowManager.IsIntermission"/>) 진입 시 즉시 자멸.
///
/// <b>크기 지정은 "스프라이트 캔버스 전체"가 기준이다</b>(그림이 그려진 영역이 아니라).
/// 보스 이펙트는 800px/256px 캔버스 안에서 그림이 차지하는 비율이 제각각이라, 호출부
/// (<see cref="BossUnit"/>)가 실측 비율 상수로 환산해서 넘긴다.
/// </summary>
public class BossFrameEffect : MonoBehaviour
{
    private static readonly Dictionary<string, Sprite[]> frame_cache = new Dictionary<string, Sprite[]>();

    private SpriteRenderer sprite_renderer;
    private Sprite[] frames;
    private float duration;
    private float elapsed;
    private bool loop;

    /// <summary>폴더 하나의 프레임(파일명 오름차순 = 재생 순서). 없으면 길이 0 배열.</summary>
    public static Sprite[] GetFrames(string folder)
    {
        if (frame_cache.TryGetValue(folder, out Sprite[] cached)) return cached;

        Sprite[] loaded = Resources.LoadAll<Sprite>(folder);
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
        frame_cache[folder] = loaded;

        if (loaded.Length == 0)
            Debug.LogWarning($"BossFrameEffect: Resources/{folder}에서 스프라이트를 찾지 못했습니다.");

        return loaded;
    }

    /// <summary>
    /// folder의 전체 프레임을 duration 동안 한 번(또는 loop면 반복) 재생한다.
    /// canvasWidth/canvasHeight는 <b>스프라이트 캔버스</b>의 목표 월드 크기다.
    /// canvasHeight가 0 이하면 가로 기준 균등 스케일을 쓴다.
    /// </summary>
    public static BossFrameEffect Play(string folder, Vector3 position, float canvasWidth, int sortingOrder,
                                       float duration, float rotationDegrees = 0f,
                                       float canvasHeight = 0f, bool loop = false)
    {
        Sprite[] loaded = GetFrames(folder);
        if (loaded.Length == 0) return null;

        var go = new GameObject("BossFx_" + folder);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = loaded[0];
        renderer.sortingOrder = sortingOrder;

        Vector3 unit_size = loaded[0].bounds.size; // 스케일 1 기준 캔버스 크기(월드 단위)
        float scale_x = canvasWidth / Mathf.Max(0.0001f, unit_size.x);
        float scale_y = canvasHeight > 0f ? canvasHeight / Mathf.Max(0.0001f, unit_size.y) : scale_x;
        go.transform.localScale = new Vector3(Mathf.Max(0.01f, scale_x), Mathf.Max(0.01f, scale_y), 1f);

        var effect = go.AddComponent<BossFrameEffect>();
        effect.sprite_renderer = renderer;
        effect.frames = loaded;
        effect.duration = Mathf.Max(0.01f, duration);
        effect.loop = loop;
        return effect;
    }

    /// <summary>패턴이 캔슬됐을 때(그로기/페이즈 전환) 호출부가 남은 이펙트를 즉시 치운다.</summary>
    public void Stop()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => frame_cache.Clear();

    private void Update()
    {
        if (GameFlowManager.IsIntermission)
        {
            Destroy(gameObject);
            return;
        }

        elapsed += Time.deltaTime;
        float t = elapsed / duration;

        if (!loop && t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        int index = Mathf.FloorToInt(t * frames.Length);
        index = loop ? ((index % frames.Length) + frames.Length) % frames.Length
                     : Mathf.Clamp(index, 0, frames.Length - 1);
        sprite_renderer.sprite = frames[index];
    }
}
