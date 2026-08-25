using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플라즈마 캐논처럼 <b>일정 시간 동안 직선 범위에 반복 피해를 주는 빔</b>.
/// 투사체(Projectile)와 달리 날아가지 않고, 발사 순간의 위치·방향을 그대로 고정한 채
/// weapon_duration 동안 제자리에 머무르며 tick 간격마다 범위 안의 적을 타격한다.
///
/// 방향을 총구에 붙여 따라다니게 하지 않는 이유: 3초 내내 조준을 따라 회전하는 레이저가 되어
/// 사실상 화면 전체를 쓸어버리는 무기가 되기 때문이다. 발사한 방향으로만 뻗는다.
///
/// 빔이 여러 개 겹치는 문제는 발사 쪽에서 구조적으로 막혀 있다 - 모든 무기의 대기시간은
/// "발사 동작이 끝난 뒤"부터 흐르므로(PlayerShootManager.TryFireSlot 참고),
/// 3초 빔은 3초 + 대기시간이 지나야 다음 발이 나간다.
///
/// 전제: X-Y 평면만 사용 (Z축 미사용)
/// </summary>
public class BeamProjectile : MonoBehaviour
{
    /// <summary>피해 판정 간격(초). 3초 빔이면 15번 들어간다.</summary>
    public const float TickInterval = 0.2f;

    private Vector3 direction;
    private float length;
    private float half_width;
    private float tick_damage;
    private float def_ignore;
    private int source_weapon_id;
    private bool is_crit;
    private float knockback;
    private float remaining_time;
    private float next_tick_time;

    // OverlapBox 결과에서 중복 컴포넌트(자식 콜라이더 등)를 걸러내기 위한 틱 단위 임시 집합
    private readonly HashSet<EnemyUnit> hit_this_tick = new HashSet<EnemyUnit>();

    // 빔 비주얼 애니메이션(2026-08-21, 플라즈마캐논 탄환 이펙트 2프레임 적용) - 프레임이 1장뿐이면
    // 정지 이미지처럼 보인다(교체 전 단일 스프라이트 방식과 동일하게 동작).
    private const float VisualFps = 8f;

    /// <summary>
    /// 빔 그림의 정렬 순서. <b>이 값이 없어서(=기본 0) 이펙트가 안 붙은 것처럼 보였다</b>
    /// (2026-08-25 사용자 리포트: "플라즈마 캐논 이펙트 적용이 안되어있음. 내가 안올렸었나?" -
    /// 아트는 <c>Resources/PlasmaCannonBeam</c>에 이미 들어와 있었고 파일도 동일했다).
    /// 실측 정렬 순서: 지형 0 / 좀비 1 / 로봇 몸통·다리 2~13 / 손에 든 무기 14.
    /// 0이면 빔이 <b>지형 바로 위·모든 적과 로봇 아래</b>에 깔려서, 반투명(alpha 0.6)까지 겹쳐
    /// 사실상 보이지 않았다. 총구 화염(PlayerShootManager.muzzle_flash_sorting_order)과 같은
    /// 20을 써서 발사 연출끼리 같은 층에 둔다.
    /// </summary>
    private const int VisualSortingOrder = 20;

    private SpriteRenderer visual_renderer;
    private Sprite[] visual_frames;
    private float visual_frame_timer;
    private int visual_frame_index;

    /// <summary>
    /// 빔 오브젝트를 코드로 만들어 발사한다. 전용 프리팹 없이 동작하도록
    /// RewardPickupManager와 같은 방식으로 SpriteRenderer를 직접 구성한다.
    /// </summary>
    /// <param name="total_damage">지속시간 전체에 걸쳐 들어갈 총 데미지 (weapon_atk)</param>
    /// <param name="duration">빔이 유지되는 시간(초). 0 이하면 1틱만 발생</param>
    public static BeamProjectile Fire(Sprite[] visual_frames, Vector3 origin, Vector3 fire_direction, float beam_length,
                                      float beam_half_width, float total_damage, float duration,
                                      float def_ignore_percent, float knockback_strength, int source_weapon_id = 0,
                                      bool isCrit = false)
    {
        GameObject obj = new GameObject("Beam");
        obj.transform.position = origin;

        BeamProjectile beam = obj.AddComponent<BeamProjectile>();
        beam.Init(visual_frames, fire_direction, beam_length, beam_half_width, total_damage, duration,
                  def_ignore_percent, knockback_strength, source_weapon_id, isCrit);
        return beam;
    }

    private void Init(Sprite[] frames, Vector3 fire_direction, float beam_length, float beam_half_width,
                      float total_damage, float duration, float def_ignore_percent, float knockback_strength,
                      int weapon_id = 0, bool isCrit = false)
    {
        is_crit = isCrit;
        direction = fire_direction.sqrMagnitude > 0.0001f ? fire_direction.normalized : Vector3.right;
        direction.z = 0f;

        length = Mathf.Max(0.1f, beam_length);
        half_width = Mathf.Max(0.1f, beam_half_width);
        def_ignore = def_ignore_percent;
        source_weapon_id = weapon_id;
        knockback = knockback_strength;

        remaining_time = Mathf.Max(TickInterval, duration);

        // 총 데미지를 틱 수로 나눠 분배한다. 반올림 때문에 총합이 조금 어긋날 수 있지만
        // 최소 1은 보장해서 "맞았는데 0 데미지"가 나오지 않게 한다.
        int tick_count = Mathf.Max(1, Mathf.RoundToInt(remaining_time / TickInterval));
        tick_damage = Mathf.Max(1, Mathf.RoundToInt((float)total_damage / tick_count));

        BuildVisual(frames);

        next_tick_time = 0f; // 발사 즉시 첫 틱
    }

    // 빔을 길쭉한 스프라이트로 보여준다. 프레임이 여러 장이면(플라즈마캐논 2프레임) 지속시간 내내
    // 순환 재생한다 - BuildVisual이 첫 프레임 기준으로 크기/위치를 한 번만 잡고, 이후 Update()가
    // sprite만 바꿔 낀다(크기는 프레임마다 다시 계산하지 않는다 - 두 프레임은 같은 크기다).
    private void BuildVisual(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0) return;

        visual_frames = frames;
        Sprite first = frames[0];

        GameObject visual_obj = new GameObject("BeamVisual");
        visual_obj.transform.SetParent(transform, false);

        visual_renderer = visual_obj.AddComponent<SpriteRenderer>();
        visual_renderer.sprite = first;
        visual_renderer.sortingOrder = VisualSortingOrder;
        visual_renderer.color = new Color(1f, 1f, 1f, 0.6f); // 겹쳐 보여도 눈이 아프지 않도록 반투명

        float sprite_length = Mathf.Max(0.0001f, first.bounds.size.x);
        float sprite_height = Mathf.Max(0.0001f, first.bounds.size.y);

        // 스프라이트의 왼쪽 끝이 총구에 오도록 절반 길이만큼 앞으로 밀어준다
        visual_obj.transform.localScale = new Vector3(length / sprite_length, (half_width * 2f) / sprite_height, 1f);
        visual_obj.transform.localPosition = direction * (length * 0.5f);
        visual_obj.transform.localRotation = Quaternion.FromToRotation(Vector3.right, direction);
    }

    private void UpdateVisualAnimation()
    {
        if (visual_renderer == null || visual_frames == null || visual_frames.Length < 2) return;

        visual_frame_timer += Time.deltaTime;
        if (visual_frame_timer < 1f / VisualFps) return;
        visual_frame_timer = 0f;

        visual_frame_index = (visual_frame_index + 1) % visual_frames.Length;
        visual_renderer.sprite = visual_frames[visual_frame_index];
    }

    private void Update()
    {
        // 정비 화면에서는 Time.timeScale=0이라 deltaTime이 0이므로 자연히 멈춘다.
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon)
        {
            Destroy(gameObject);
            return;
        }

        if (Time.time >= next_tick_time)
        {
            ApplyTick();
            next_tick_time = Time.time + TickInterval;
        }

        UpdateVisualAnimation();

        remaining_time -= Time.deltaTime;
        if (remaining_time <= 0f) Destroy(gameObject);
    }

    /// <summary>빔이 덮는 직사각형 범위 안의 모든 적에게 1틱 피해를 준다.</summary>
    private void ApplyTick()
    {
        hit_this_tick.Clear();

        Vector3 center = transform.position + direction * (length * 0.5f);
        Vector3 half_extents = new Vector3(length * 0.5f, half_width, 0.5f);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.right, direction);

        Collider[] hits = Physics.OverlapBox(center, half_extents, rotation);
        foreach (Collider hit in hits)
        {
            EnemyUnit enemy = hit.GetComponent<EnemyUnit>();
            if (enemy == null) enemy = hit.GetComponentInParent<EnemyUnit>();

            // 틱마다 집합을 비우므로 같은 적이 다음 틱에는 다시 맞는다(계속 태우는 것이 빔의 동작)
            if (enemy == null || enemy.IsDead || !hit_this_tick.Add(enemy)) continue;

            enemy.TakeDamage(tick_damage, def_ignore, source_weapon_id, is_crit);
            if (knockback > 0f) enemy.ApplyKnockback(direction, knockback);
        }
    }
}
