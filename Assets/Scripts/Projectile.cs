using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlayerShootManager가 Instantiate 시점에 Launch()를 호출해 초기화하는 투사체.
/// 지정된 방향으로 직선 이동하다가 EnemyUnit과 충돌하면 데미지를 준다.
/// 관통(can_penetrate) 여부에 따라 첫 충돌 시 사라지거나, 여러 대상을 뚫고 계속 나아간다.
///
/// 지연 폭발(SetDelayedBlast) 모드:
/// 수류탄처럼 날아가는 동안은 아주 작은 크기(travel size)로 이동하다가,
/// 사거리 끝(또는 적과 접촉)에 도달하는 순간 weapon_atsize만큼의 범위에 한 번에 데미지를 준다.
///
/// 전제:
/// - 이동은 X-Y 평면에서만 일어남 (Z축 미사용)
/// - 충돌 감지는 트리거(OnTriggerEnter) 방식 → 이 오브젝트의 Collider는 Is Trigger 체크 필요,
///   그리고 이 오브젝트 또는 상대(EnemyUnit) 쪽에 Rigidbody가 하나는 있어야 트리거 이벤트가 발생함
/// </summary>
[RequireComponent(typeof(Collider))]
public class Projectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private int damage;
    private float max_range;
    private bool can_penetrate;
    private Vector3 spawn_position;

    // --- 지연 폭발(수류탄류) 관련 ---
    private bool is_delayed_blast;      // 사거리 끝에서 범위 데미지를 주는 모드인지
    private float blast_size;           // 폭발 시 적용할 크기 (weapon_atsize)
    private bool explode_on_contact;    // 날아가는 중 적과 닿았을 때도 즉시 폭발할지
    private float blast_visual_duration; // 폭발 범위를 눈으로 보여주는 시간(초)
    private bool has_exploded;

    // 관통 시 같은 대상에게 중복으로 데미지가 들어가지 않도록 이미 맞은 대상을 기록
    private readonly HashSet<EnemyUnit> already_hit = new HashSet<EnemyUnit>();

    public void Launch(Vector3 fire_direction, float fire_speed, int fire_damage, float fire_range, float size, bool penetrate)
    {
        direction = fire_direction.sqrMagnitude > 0.0001f ? fire_direction.normalized : Vector3.right;
        speed = fire_speed;
        damage = fire_damage;
        max_range = fire_range > 0f ? fire_range : 20f; // weapon_range가 0/미설정이면 기본 사거리로 폴백
        can_penetrate = penetrate;
        spawn_position = transform.position;

        // weapon_atsize(투사체 크기) 반영
        transform.localScale = Vector3.one * (size > 0f ? size : 1f);
    }

    /// <summary>
    /// 지연 폭발 모드로 전환한다. Launch()를 작은 travel size로 호출한 뒤 이어서 호출할 것.
    /// </summary>
    /// <param name="explosion_size">폭발 시 적용할 크기 (weapon_atsize)</param>
    /// <param name="contact_triggers_explosion">날아가는 중 적과 닿아도 즉시 폭발할지</param>
    /// <param name="visual_duration">폭발 범위를 화면에 보여주는 시간(초). 0이면 즉시 사라짐</param>
    public void SetDelayedBlast(float explosion_size, bool contact_triggers_explosion, float visual_duration)
    {
        is_delayed_blast = true;
        blast_size = explosion_size > 0f ? explosion_size : 1f;
        explode_on_contact = contact_triggers_explosion;
        blast_visual_duration = Mathf.Max(0f, visual_duration);
    }

    private void Update()
    {
        if (has_exploded) return; // 폭발 연출 중에는 더 이상 움직이지 않음

        transform.position += direction * speed * Time.deltaTime;

        if (Vector3.Distance(spawn_position, transform.position) >= max_range)
        {
            // 사거리 끝 도달 → 지연 폭발 무기는 이 순간에 범위 데미지
            if (is_delayed_blast) Explode();
            else Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (has_exploded) return;

        EnemyUnit enemy = FindEnemy(other);
        if (enemy == null) return;

        // 지연 폭발 무기는 접촉 시 단일 데미지를 주지 않는다.
        // (설정에 따라 즉시 폭발하거나, 그대로 통과해서 사거리 끝까지 날아간다)
        if (is_delayed_blast)
        {
            if (explode_on_contact) Explode();
            return;
        }

        if (already_hit.Contains(enemy)) return; // 관통 중 같은 대상 중복 히트 방지

        enemy.TakeDamage(damage);

        if (can_penetrate)
        {
            already_hit.Add(enemy); // 관통이면 파괴하지 않고 계속 진행, 이 대상만 다시 안 맞도록 기록
        }
        else
        {
            Destroy(gameObject); // 관통이 아니면 첫 충돌에서 소멸
        }
    }

    /// <summary>
    /// 폭발 지점 주변 blast 반경 안의 모든 EnemyUnit에게 한 번씩 데미지를 준다.
    /// </summary>
    private void Explode()
    {
        if (has_exploded) return;
        has_exploded = true;

        float radius = GetWorldBlastRadius();

        // 반경 안의 적을 모두 찾아 중복 없이 한 번씩만 데미지 적용
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);
        foreach (Collider hit in hits)
        {
            EnemyUnit enemy = FindEnemy(hit);
            if (enemy == null || already_hit.Contains(enemy)) continue;

            already_hit.Add(enemy);
            enemy.TakeDamage(damage);
        }

        // 터진 범위를 눈으로 확인할 수 있도록 잠깐 크기를 키워서 보여준 뒤 제거
        if (blast_visual_duration > 0f)
        {
            transform.localScale = Vector3.one * blast_size;

            Collider own_collider = GetComponent<Collider>();
            if (own_collider != null) own_collider.enabled = false; // 연출 중 추가 트리거 방지

            Destroy(gameObject, blast_visual_duration);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 폭발 반경(월드 단위). 투사체가 blast_size 크기였을 때의 콜라이더 반경과 같게 맞춘다.
    /// → 데이터테이블의 weapon_atsize가 기존 '공격 범위'와 동일한 의미를 유지한다.
    /// </summary>
    private float GetWorldBlastRadius()
    {
        if (GetComponent<Collider>() is SphereCollider sphere) return sphere.radius * blast_size;
        return blast_size * 0.5f; // 구형 콜라이더가 아니면 지름 기준으로 근사
    }

    // 콜라이더가 자식에 붙어 있는 경우까지 대응
    private static EnemyUnit FindEnemy(Collider col)
    {
        EnemyUnit enemy = col.GetComponent<EnemyUnit>();
        return enemy != null ? enemy : col.GetComponentInParent<EnemyUnit>();
    }
}
