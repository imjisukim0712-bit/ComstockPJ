using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 근접무기(생존단검/전술 마체테/전기톱검)의 타격 판정.
/// 투사체를 만들지 않고 <b>칼이 차지한 공간</b>을 즉시 판정한다.
///
/// 짧은 사거리의 투사체로 근접을 흉내내지 않는 이유: 사거리 2유닛짜리 탄을 속도 10으로 쏘면
/// 0.2초 만에 사라져서 화면에 아무것도 안 보이고, 초당 3회(전기톱검) Instantiate/Destroy만
/// 반복된다. 범위 즉시 판정이 근접 무기의 실제 동작에도 맞다.
///
/// <b>2026-08-13 판정 기준 교체(사용자 리포트 "코앞에서 휘두르는데 맵 전체의 좀비가 맞는다")</b>:
/// 예전에는 판정 원점이 총구, 반경이 <b>weapon_range 전체</b>(근접 4.95~5.85유닛 = 화면 세로
/// 반경 5.4에 맞먹는 크기)였다. 지금은 <see cref="PlayerShootManager"/>가 찌르는 동안 매 프레임
/// <b>무기 스프라이트의 실제 월드 위치·크기</b>를 넘겨주고, 이 클래스는 그 범위만 판정한다.
/// 그래서 "보이는 칼에 닿은 적"만 맞는다. 같은 스윙 안에서 한 적이 여러 번 맞지 않도록
/// 호출부가 <paramref name="already_hit"/> 집합을 스윙 단위로 유지한다.
///
/// RewardPickupManager와 같은 static 유틸리티 패턴을 따른다.
/// 전제: X-Y 평면만 사용 (Z축 미사용)
/// </summary>
public static class MeleeSwing
{
    /// <summary>휘두르는 부채꼴의 반각(도). 60이면 정면 기준 좌우 60도씩 총 120도를 벤다.</summary>
    public const float DefaultHalfAngleDegrees = 60f;

    /// <summary>각도 제한 없이 반경 전체를 판정한다(칼 그림이 차지한 공간을 그대로 쓸 때).</summary>
    public const float FullAngleDegrees = 180f;

    // 매 호출마다 새로 만들지 않도록 재사용하는 중복 제거용 집합(한 번의 스윙 안에서만 의미가 있다)
    private static readonly HashSet<EnemyUnit> hit_buffer = new HashSet<EnemyUnit>();

    /// <summary>
    /// 원점 기준 반경 안의 적 중, 진행 방향에서 half_angle_degrees 안에 있는 대상을 모두 타격한다.
    /// </summary>
    /// <param name="origin">판정 중심 (지금은 찌르는 칼 그림의 중심)</param>
    /// <param name="direction">휘두르는(찌르는) 방향</param>
    /// <param name="radius">판정 반경</param>
    /// <param name="already_hit">
    /// 여러 프레임에 걸친 한 번의 스윙에서 중복 타격을 막을 집합. null이면 이 호출 안에서만
    /// 중복을 막는다(1프레임 즉발 판정용 기존 동작).
    /// </param>
    /// <returns>실제로 맞은 적의 수 (검증/로그용)</returns>
    public static int Execute(Vector3 origin, Vector3 direction, float radius, int damage,
                              float def_ignore_percent, float knockback_strength,
                              float half_angle_degrees = DefaultHalfAngleDegrees,
                              HashSet<EnemyUnit> already_hit = null)
    {
        if (radius <= 0f) return 0;

        direction.z = 0f;
        if (direction.sqrMagnitude <= 0.0001f) return 0;
        direction = direction.normalized;

        HashSet<EnemyUnit> hit_buffer = already_hit ?? MeleeSwing.hit_buffer;
        if (already_hit == null) hit_buffer.Clear();

        int hit_count = 0;

        Collider[] nearby = Physics.OverlapSphere(origin, radius);
        foreach (Collider col in nearby)
        {
            EnemyUnit enemy = col.GetComponent<EnemyUnit>();
            if (enemy == null) enemy = col.GetComponentInParent<EnemyUnit>();
            if (enemy == null || enemy.IsDead || !hit_buffer.Add(enemy)) continue;

            Vector3 to_enemy = enemy.transform.position - origin;
            to_enemy.z = 0f;
            if (to_enemy.sqrMagnitude <= 0.0001f)
            {
                // 완전히 겹쳐 있으면 방향을 잴 수 없으므로 무조건 맞은 것으로 본다
                ApplyHit(enemy, direction, damage, def_ignore_percent, knockback_strength);
                hit_count++;
                continue;
            }

            if (Vector3.Angle(direction, to_enemy) > half_angle_degrees) continue; // 부채꼴 밖

            ApplyHit(enemy, to_enemy, damage, def_ignore_percent, knockback_strength);
            hit_count++;
        }

        // 호출부가 스윙 단위 집합을 넘겼다면 그 집합은 스윙이 끝날 때까지 유지되어야 하므로 비우지 않는다.
        if (already_hit == null) hit_buffer.Clear(); // 다음 호출까지 참조를 남겨두지 않는다
        return hit_count;
    }

    private static void ApplyHit(EnemyUnit enemy, Vector3 push_direction, int damage,
                                 float def_ignore_percent, float knockback_strength)
    {
        enemy.TakeDamage(damage, def_ignore_percent);
        if (knockback_strength > 0f) enemy.ApplyKnockback(push_direction, knockback_strength);
    }
}
