using UnityEngine;

/// <summary>
/// 스피터(원거리 좀비)가 발사하는 직선 투사체(좀비 기획서 Ver04 p.16 "직선으로 이동하는
/// 투사체 발사"). 플레이어와 충돌하면 데미지를 주고 사라진다.
///
/// 플레이어가 쏘는 <see cref="Projectile"/>은 EnemyUnit을 타격하는 반대 방향 전제라 여기
/// 재사용하지 않고 별도 클래스로 둔다. 전용 투사체 아트가 없어 코드로 작은 원형 스프라이트를
/// 생성한다(BossUnit의 광역 공격 경고 원과 같은 방식).
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private int damage;
    private float max_range;
    private Vector3 spawn_position;

    private static Sprite cached_sprite;

    public static EnemyProjectile Spawn(Vector3 position, Vector3 direction, float speed, int damage, float range, float visualSize)
    {
        GameObject go = new GameObject("EnemyProjectile");
        go.transform.position = position;

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.5f;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateSprite();
        sr.color = new Color(0.85f, 0.15f, 0.15f, 1f); // 적 투사체 구분용 붉은색
        sr.sortingOrder = 4;

        EnemyProjectile proj = go.AddComponent<EnemyProjectile>();
        proj.Launch(direction, speed, damage, range, visualSize);
        return proj;
    }

    private void Launch(Vector3 dir, float spd, int dmg, float range, float visualSize)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.right;
        speed = spd > 0f ? spd : 10f;
        damage = dmg;
        max_range = range > 0f ? range : 10f;
        spawn_position = transform.position;

        if (visualSize > 0f) transform.localScale = Vector3.one * visualSize;
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

        player.TakeDamage(damage);
        Destroy(gameObject);
    }

    private static Sprite GetOrCreateSprite()
    {
        if (cached_sprite != null) return cached_sprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = dist <= radius - 2f ? 1f : (dist <= radius ? (radius - dist) / 2f : 0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        cached_sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return cached_sprite;
    }
}
