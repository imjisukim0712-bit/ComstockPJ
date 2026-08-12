using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PlayerShootManager가 Instantiate 시점에 Launch()를 호출해 초기화하는 투사체.
/// 지정된 방향으로 직선 이동하다가 EnemyUnit과 충돌하면 데미지를 준다.
///
/// 관통(PierceCount):
/// - 0  : 첫 충돌에 소멸
/// - N  : N번까지 뚫고 지나간다 (대물저격총 3회, 지정사수소총 1회 등)
/// - -1 : 무제한 관통 (플라즈마 계열)
/// 확률 관통(60%/40%/50%)은 <b>투사체를 만들기 전에</b> WeaponData.RollPierceCount()로 굴린
/// 결과를 넘겨받는다. 즉 산탄 8발은 각자 따로 관통 판정을 받는다.
///
/// 스플래시(SplashRadius > 0):
/// 착탄하는 순간(또는 사거리 끝에 도달하는 순간) 반경 안의 모든 적에게 한 번씩 데미지를 준다.
/// 예전에는 weapon_id 300400~300499 범위를 하드코딩해서 판별했지만, 이제는 무기 데이터의
/// 스플래시 반경만 보고 결정하므로 어떤 무기에도 폭발을 붙일 수 있다.
///
/// 전제:
/// - 이동은 X-Y 평면에서만 일어남 (Z축 미사용)
/// - 충돌 감지는 트리거(OnTriggerEnter) 방식 → 이 오브젝트의 Collider는 Is Trigger 체크 필요,
///   그리고 이 오브젝트 또는 상대(EnemyUnit) 쪽에 Rigidbody가 하나는 있어야 트리거 이벤트가 발생함
/// </summary>
[RequireComponent(typeof(Collider))]
public class Projectile : MonoBehaviour
{
    /// <summary>
    /// 발사 파라미터 묶음. 인자가 10개 가까이 되어 위치 인자로 넘기면 실수하기 쉬워서 구조체로 묶었다.
    /// </summary>
    public struct Spec
    {
        public Vector3 Direction;
        public float Speed;
        public int Damage;
        public float MaxRange;
        public float Size;

        /// <summary>0 = 관통 없음 / N = N회 관통 / -1 = 무제한 관통</summary>
        public int PierceCount;

        /// <summary>0보다 크면 착탄 시 이 반경(월드 유닛) 안의 적을 모두 타격</summary>
        public float SplashRadius;

        /// <summary>적 방어력을 무시하는 비율 (0~1)</summary>
        public float DefIgnore;

        /// <summary>적중한 적을 밀어내는 초기 속도(유닛/초)</summary>
        public float Knockback;

        /// <summary>폭발 범위를 눈으로 보여주는 시간(초). 0이면 연출 없이 즉시 사라짐</summary>
        public float BlastVisualDuration;
    }

    private const int UnlimitedPierce = -1;

    private Spec spec;
    private Vector3 spawn_position;
    private int remaining_pierce;
    private bool has_exploded;

    // 관통 시 같은 대상에게 중복으로 데미지가 들어가지 않도록 이미 맞은 대상을 기록
    private readonly HashSet<EnemyUnit> already_hit = new HashSet<EnemyUnit>();

    public void Launch(Spec launch_spec)
    {
        spec = launch_spec;
        spec.Direction = spec.Direction.sqrMagnitude > 0.0001f ? spec.Direction.normalized : Vector3.right;
        if (spec.MaxRange <= 0f) spec.MaxRange = WeaponData.DefaultRange; // 사거리가 0/미설정이면 기본 사거리로 폴백

        remaining_pierce = spec.PierceCount;
        spawn_position = transform.position;

        transform.localScale = Vector3.one * (spec.Size > 0f ? spec.Size : 1f);
    }

    private void Update()
    {
        if (has_exploded) return; // 폭발 연출 중에는 더 이상 움직이지 않음

        transform.position += spec.Direction * spec.Speed * Time.deltaTime;

        if (Vector3.Distance(spawn_position, transform.position) >= spec.MaxRange)
        {
            // 사거리 끝 도달 → 폭발 무기는 이 순간에 범위 데미지, 나머지는 그냥 소멸
            if (spec.SplashRadius > 0f) Explode();
            else Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (has_exploded) return;

        EnemyUnit enemy = FindEnemy(other);
        if (enemy == null) return;

        // 폭발 무기는 접촉 지점에서 즉시 터진다(단일 데미지는 따로 주지 않는다 - 폭발 데미지에 포함)
        if (spec.SplashRadius > 0f)
        {
            Explode();
            return;
        }

        if (already_hit.Contains(enemy)) return; // 관통 중 같은 대상 중복 히트 방지
        already_hit.Add(enemy);

        ApplyHit(enemy);

        if (remaining_pierce == UnlimitedPierce) return;    // 무제한 관통 - 계속 진행
        if (remaining_pierce > 0)
        {
            remaining_pierce--;                             // 남은 관통 횟수 소모
            return;
        }

        Destroy(gameObject); // 관통이 없거나 전부 소진되면 소멸
    }

    /// <summary>
    /// 폭발 지점 주변 반경 안의 모든 EnemyUnit에게 한 번씩 데미지를 준다.
    /// </summary>
    private void Explode()
    {
        if (has_exploded) return;
        has_exploded = true;

        // 반경 안의 적을 모두 찾아 중복 없이 한 번씩만 데미지 적용
        Collider[] hits = Physics.OverlapSphere(transform.position, spec.SplashRadius);
        foreach (Collider hit in hits)
        {
            EnemyUnit enemy = FindEnemy(hit);
            if (enemy == null || already_hit.Contains(enemy)) continue;

            already_hit.Add(enemy);
            ApplyHit(enemy);
        }

        // 터진 범위를 눈으로 확인할 수 있도록 잠깐 폭발 반경만큼 키워서 보여준 뒤 제거
        if (spec.BlastVisualDuration > 0f)
        {
            transform.localScale = Vector3.one * ComputeBlastVisualScale();

            Collider own_collider = GetComponent<Collider>();
            if (own_collider != null) own_collider.enabled = false; // 연출 중 추가 트리거 방지

            Destroy(gameObject, spec.BlastVisualDuration);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 폭발 연출 스프라이트를 <b>실제 데미지 반경(SplashRadius)과 정확히 같은 크기</b>로 보이게
    /// 하는 localScale을 구한다.
    ///
    /// 예전에는 그냥 <c>localScale = SplashRadius</c>를 넣었는데, 이건 "스프라이트가 스케일 1에서
    /// 반지름 1유닛"일 때만 맞는 식이다. 실제 투사체 스프라이트(Bullets.png)는 640x640px에
    /// PPU 100이라 스케일 1에서 <b>반지름 3.2유닛</b>이다. 그래서 로켓런처(폭발 2.4)의 연출이
    /// 반지름 3.2 x 2.4 = 7.68유닛(면적 약 10배)으로 그려져, 눈에 보이는 폭발 범위가 실제
    /// 판정 범위보다 훨씬 넓어 보였다(2026-08-12 사용자 리포트로 수정).
    ///
    /// 스프라이트 크기는 하드코딩하지 않고 매번 실제 스프라이트에서 읽는다 - 투사체 프리팹은
    /// 무기 데이터(weapon_tanhwan)마다 다를 수 있어서(Bullets/Energy…) 프리팹이 바뀌어도
    /// 저절로 맞는다. 회전한 상태에서도 정확하도록 월드 bounds(회전하면 AABB가 커진다)가 아니라
    /// 스프라이트 자체의 로컬 bounds를 쓴다.
    /// </summary>
    private float ComputeBlastVisualScale()
    {
        float fallback = Mathf.Max(0.1f, spec.SplashRadius);

        SpriteRenderer sprite_renderer = GetComponentInChildren<SpriteRenderer>();
        if (sprite_renderer == null || sprite_renderer.sprite == null) return fallback;

        Vector3 extents = sprite_renderer.sprite.bounds.extents; // 스케일 1 기준 로컬 반지름
        float unit_radius = Mathf.Max(extents.x, extents.y);
        if (unit_radius <= 0.0001f) return fallback;

        // 스프라이트가 자식에 있고 자체 스케일을 가진 경우까지 보정(루트에 있으면 1배)
        float root_scale = Mathf.Abs(transform.lossyScale.x);
        if (root_scale > 0.0001f)
        {
            unit_radius *= Mathf.Abs(sprite_renderer.transform.lossyScale.x) / root_scale;
        }

        return Mathf.Max(0.01f, spec.SplashRadius / unit_radius);
    }

    /// <summary>데미지 + 넉백을 한 번에 적용한다. 넉백 방향은 투사체 진행 방향이다.</summary>
    private void ApplyHit(EnemyUnit enemy)
    {
        enemy.TakeDamage(spec.Damage, spec.DefIgnore);

        if (spec.Knockback > 0f)
        {
            // 폭발은 터진 지점에서 바깥으로, 직격탄은 날아가던 방향으로 민다
            Vector3 push = spec.SplashRadius > 0f
                ? (enemy.transform.position - transform.position)
                : spec.Direction;
            enemy.ApplyKnockback(push, spec.Knockback);
        }
    }

    // 콜라이더가 자식에 붙어 있는 경우까지 대응
    private static EnemyUnit FindEnemy(Collider col)
    {
        EnemyUnit enemy = col.GetComponent<EnemyUnit>();
        return enemy != null ? enemy : col.GetComponentInParent<EnemyUnit>();
    }
}
