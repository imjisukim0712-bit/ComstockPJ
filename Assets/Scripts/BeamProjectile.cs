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
    private int tick_damage;
    private float def_ignore;
    private int source_weapon_id;
    private float knockback;
    private float remaining_time;
    private float next_tick_time;

    // OverlapBox 결과에서 중복 컴포넌트(자식 콜라이더 등)를 걸러내기 위한 틱 단위 임시 집합
    private readonly HashSet<EnemyUnit> hit_this_tick = new HashSet<EnemyUnit>();

    /// <summary>
    /// 빔 오브젝트를 코드로 만들어 발사한다. 전용 프리팹 없이 동작하도록
    /// RewardPickupManager와 같은 방식으로 SpriteRenderer를 직접 구성한다.
    /// </summary>
    /// <param name="total_damage">지속시간 전체에 걸쳐 들어갈 총 데미지 (weapon_atk)</param>
    /// <param name="duration">빔이 유지되는 시간(초). 0 이하면 1틱만 발생</param>
    public static BeamProjectile Fire(Sprite visual, Vector3 origin, Vector3 fire_direction, float beam_length,
                                      float beam_half_width, int total_damage, float duration,
                                      float def_ignore_percent, float knockback_strength, int source_weapon_id = 0)
    {
        GameObject obj = new GameObject("Beam");
        obj.transform.position = origin;

        BeamProjectile beam = obj.AddComponent<BeamProjectile>();
        beam.Init(visual, fire_direction, beam_length, beam_half_width, total_damage, duration,
                  def_ignore_percent, knockback_strength, source_weapon_id);
        return beam;
    }

    private void Init(Sprite visual, Vector3 fire_direction, float beam_length, float beam_half_width,
                      int total_damage, float duration, float def_ignore_percent, float knockback_strength,
                      int weapon_id = 0)
    {
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

        BuildVisual(visual);

        next_tick_time = 0f; // 발사 즉시 첫 틱
    }

    // 빔을 길쭉한 스프라이트로 보여준다. 전용 아트가 없어 투사체 스프라이트를 늘려서 대신 쓴다.
    private void BuildVisual(Sprite visual)
    {
        if (visual == null) return;

        GameObject visual_obj = new GameObject("BeamVisual");
        visual_obj.transform.SetParent(transform, false);

        SpriteRenderer renderer = visual_obj.AddComponent<SpriteRenderer>();
        renderer.sprite = visual;
        renderer.color = new Color(1f, 1f, 1f, 0.6f); // 겹쳐 보여도 눈이 아프지 않도록 반투명

        float sprite_length = Mathf.Max(0.0001f, visual.bounds.size.x);
        float sprite_height = Mathf.Max(0.0001f, visual.bounds.size.y);

        // 스프라이트의 왼쪽 끝이 총구에 오도록 절반 길이만큼 앞으로 밀어준다
        visual_obj.transform.localScale = new Vector3(length / sprite_length, (half_width * 2f) / sprite_height, 1f);
        visual_obj.transform.localPosition = direction * (length * 0.5f);
        visual_obj.transform.localRotation = Quaternion.FromToRotation(Vector3.right, direction);
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

            enemy.TakeDamage(tick_damage, def_ignore, source_weapon_id);
            if (knockback > 0f) enemy.ApplyKnockback(direction, knockback);
        }
    }
}
