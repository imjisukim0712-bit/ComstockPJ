using UnityEngine;

/// <summary>
/// 스피터(원거리 좀비)가 발사하는 직선 투사체(좀비 기획서 Ver04 p.16 "직선으로 이동하는
/// 투사체 발사"). 플레이어와 충돌하면 데미지를 주고 사라진다.
///
/// 플레이어가 쏘는 <see cref="Projectile"/>은 EnemyUnit을 타격하는 반대 방향 전제라 여기
/// 재사용하지 않고 별도 클래스로 둔다.
///
/// <b>2026-08-23 사용자 제공 전용 투사체 아트(Assets/Resources/SpitterProjectile) 적용</b> -
/// 그 전까지는 전용 아트가 없어 코드로 작은 붉은 원형 스프라이트를 생성했다(BossUnit의 광역
/// 공격 경고 원과 같은 방식). 콜라이더 반지름은 여전히 <b>실제 스프라이트에서 읽어</b>
/// 계산한다(이 프로젝트의 "보이는 크기 = 맞는 크기" 원칙 - 2026-08-12에 이 클래스에서 실제로
/// 어긋났던 적이 있어 하드코딩하지 않는다).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    private const string SpriteResourceName = "SpitterProjectile";

    private Vector3 direction;
    private float speed;
    private float damage;
    private float max_range;
    private Vector3 spawn_position;

    private static Sprite cached_sprite;

    public static EnemyProjectile Spawn(Vector3 position, Vector3 direction, float speed, float damage, float range, float visualSize)
    {
        Sprite sprite = GetSprite();
        if (sprite == null) return null;

        GameObject go = new GameObject("EnemyProjectile");
        go.transform.position = position;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 4;

        // 스프라이트 실측 반지름과 같은 콜라이더를 사용한다. 크기는 런타임에서 정규화하지 않고
        // <b>PNG 해상도 자체</b>로 정한다(PPU는 항상 100) - 2026-08-24 사용자 요청으로
        // 40px -> 120px(3배)이 되어 캔버스 지름은 projectileVisualSize 1.05 포함 1.26유닛이다.
        // sprite.bounds는 캔버스(정사각) 기준이라 콜라이더가 실제 그려진 타원(1.20 x 0.71유닛)보다
        // 세로로 넉넉하다 - 예전 32~40px 시절에는 오차가 0.1유닛 미만이라 그대로 뒀던 부분이다.
        float sprite_radius = Mathf.Max(sprite.bounds.extents.x, sprite.bounds.extents.y, 0.0001f);

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = sprite_radius;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        EnemyProjectile proj = go.AddComponent<EnemyProjectile>();
        proj.Launch(direction, speed, damage, range, visualSize);
        return proj;
    }

    private void Launch(Vector3 dir, float spd, float dmg, float range, float visualSize)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
        speed = spd > 0f ? spd : 10f;
        damage = dmg;
        max_range = range > 0f ? range : 10f;
        spawn_position = transform.position;

        float visual_multiplier = visualSize > 0f ? visualSize : 1f;
        transform.localScale = Vector3.one * visual_multiplier;
    }

    private void Update()
    {
        transform.position += direction * speed * Time.deltaTime;

        if (Vector3.Distance(spawn_position, transform.position) >= max_range) Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerRobotController player = other.GetComponent<PlayerRobotController>();
        if (player == null) player = other.GetComponentInParent<PlayerRobotController>();
        if (player == null) return;

        player.TakeDamage(damage, transform.position);
        Destroy(gameObject);
    }

    private static Sprite GetSprite()
    {
        if (cached_sprite == null)
        {
            cached_sprite = Resources.Load<Sprite>(SpriteResourceName);
            if (cached_sprite == null)
                Debug.LogWarning($"EnemyProjectile: Resources/{SpriteResourceName}에서 스프라이트를 찾지 못했습니다.");
        }

        return cached_sprite;
    }

    /// <summary>씬 재로드로 Resources가 언로드됐을 때 대비용(EnemyUnit.ResetStaticCaches에서 호출).</summary>
    public static void ResetCache() => cached_sprite = null;
}
