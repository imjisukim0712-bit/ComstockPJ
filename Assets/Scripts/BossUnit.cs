using System.Collections;
using UnityEngine;

/// <summary>
/// 보스 몹. EnemyUnit의 근접 접촉 공격은 그대로 물려받고(base.Awake()로 EnemyUnit.Alive에도
/// 등록되므로 PlayerShootManager의 자동 타겟팅이 별도 처리 없이 그대로 조준한다), 주기적으로
/// 예비 동작(텔레그래프) 후 범위 데미지를 주는 광역 공격 패턴을 추가한다.
///
/// 2026-08-20 보스 아트를 사용자 제공 "좀비 군집체"(보스몬스터기획서 Ver01)로 교체했다 -
/// 그 전까지는 일반 좀비 스프라이트를 확대·붉게 물들인 임시 비주얼이었다. 프레임 8장은
/// <see cref="MonsterAnimationLibrary.BossFolder"/>(Resources/BossMove)에 있고 제자리에서도
/// 계속 재생된다(<see cref="ResolveMoveClip"/>).
/// 광역 공격 범위 표시는 아직 전용 이펙트 에셋이 없어 런타임에 생성한 원형 스프라이트를 쓴다.
///
/// <b>텔레그래프 원은 보스의 자식이 아니라 독립 GameObject다</b>(위치를 세계 좌표로 직접
/// 잡기 위해). 그래서 <see cref="PerformAoeAttack"/> 코루틴이 끝까지 돌아야만 스스로 지워지는데,
/// 웨이브가 끝나면 <c>EnemySpawner.DespawnAllAliveEnemies()</c>가 <see cref="EnemyUnit.Die"/>를
/// 거치지 않고 보스를 그대로 <c>Destroy()</c>해버려 코루틴이 중간에 끊길 수 있다 - 이때 이미
/// 만들어진 원이 임자를 잃고 화면(정비 화면 뒤)에 영원히 남아있던 버그가 있었다(2026-08-21).
/// <see cref="OnDestroy"/>에서 남은 원을 확실히 정리해 고쳤다.
/// </summary>
public class BossUnit : EnemyUnit
{
    [Header("광역 공격 패턴 (전부 밸런스 미확정 임시값)")]
    [Tooltip("광역 공격 사이의 쿨다운(초)")]
    [SerializeField] private float aoeCooldown = 5f;

    [Tooltip("예비 동작(경고 범위 표시)이 유지되는 시간(초) - 이 동안 플레이어는 범위 밖으로 피할 수 있다")]
    [SerializeField] private float aoeTelegraphDuration = 1.2f;

    [Tooltip("광역 공격의 반경(월드 단위)")]
    [SerializeField] private float aoeRadius = 4f;

    [Tooltip("광역 공격 명중 시 데미지 (플레이어 방어력을 적용하지 않고 고정 데미지로 처리)")]
    [SerializeField] private int aoeDamage = 20;

    [Tooltip("경고 범위 표시 색상")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.15f, 0.15f, 0.35f);

    [Header("이동/대기 모션 (좀비 군집체 8프레임)")]
    [Tooltip("제자리에서도 재생하는 몸통 꿈틀거림 모션의 재생 속도(초당 프레임 수). " +
             "이동속도에 비례해 자동으로 빨라진다(EnemyUnit.UpdateWalkAnimation)")]
    [SerializeField] private float idleMotionFps = 5f;

    private float next_aoe_time;
    private bool telegraph_active;
    private GameObject active_telegraph;

    /// <summary>
    /// 보스 스탯은 데이터테이블 밖에서 WaveManager가 <c>monster_id = -1</c>로 만들어 넘기므로
    /// 몬스터ID로는 프레임 세트를 찾을 수 없다. 그래서 폴더명(<see cref="MonsterAnimationLibrary.BossFolder"/>)을
    /// 직접 지정한다.
    ///
    /// 이 세트(사용자 제공 "좀비 군집체" 8프레임)는 보행 사이클이 아니라 <b>제자리 꿈틀거림</b>
    /// 이라서 <c>playWhileIdle = true</c>로 둔다 - 멈춘 동안 얼어붙어 있으면 죽은 것처럼 보인다.
    /// </summary>
    protected override MonsterAnimationLibrary.Clip ResolveMoveClip() =>
        MonsterAnimationLibrary.GetByFolder(MonsterAnimationLibrary.BossFolder,
                                            stillFrameIndex: 0, fps: idleMotionFps, playWhileIdle: true);

    private static Sprite cached_circle_sprite;

    /// <summary>보스가 죽은 순간 딱 한 번 발행된다. WaveManager가 승리 판정에 사용한다.</summary>
    public event System.Action OnDefeated;

    protected override void Awake()
    {
        base.Awake();
        next_aoe_time = Time.time + aoeCooldown; // 스폰 직후 곧바로 터지지 않도록 첫 쿨다운을 부여
    }

    protected override void Update()
    {
        base.Update(); // 근접 접촉 판정(추적/공격)은 그대로 유지

        if (IsDead || GameOverManager.IsGameOver || telegraph_active) return;
        if (Time.time >= next_aoe_time) StartCoroutine(PerformAoeAttack());
    }

    // 예비 동작(경고 범위 표시) → 대기 → 그 시점에 범위 안에 있으면 데미지, 순서로 진행한다.
    // 목표 지점은 시전 "시작" 시점의 플레이어 위치로 고정한다(공지 이후에는 쫓아가지 않음 -
    // 플레이어가 범위 밖으로 피할 수 있어야 "패턴"으로서 의미가 있다).
    private IEnumerator PerformAoeAttack()
    {
        telegraph_active = true;
        next_aoe_time = Time.time + aoeCooldown;

        Vector3 target = transform.position;
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj != null) target = player_obj.transform.position;
        target.z = 0f;

        GameObject telegraph = CreateTelegraphVisual(target);
        active_telegraph = telegraph;

        yield return new WaitForSeconds(aoeTelegraphDuration);

        if (telegraph != null) Destroy(telegraph);
        active_telegraph = null;

        if (!IsDead && !GameOverManager.IsGameOver)
        {
            Collider[] hits = Physics.OverlapSphere(target, aoeRadius);
            foreach (Collider hit in hits)
            {
                PlayerRobotController player = hit.GetComponent<PlayerRobotController>();
                if (player == null) player = hit.GetComponentInParent<PlayerRobotController>();
                if (player != null)
                {
                    player.TakeDamage(aoeDamage, target);
                    break; // 플레이어는 한 명뿐이라 찾으면 바로 종료
                }
            }
        }

        telegraph_active = false;
    }

    private GameObject CreateTelegraphVisual(Vector3 position)
    {
        GameObject go = new GameObject("BossAoeTelegraph");
        go.transform.position = position;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateCircleSprite();
        sr.color = telegraphColor;
        sr.sortingOrder = 1; // 바닥/그림자보다는 위, 캐릭터 스프라이트보다는 아래 정도의 임시값

        float diameter = aoeRadius * 2f;
        go.transform.localScale = new Vector3(diameter, diameter, 1f);

        return go;
    }

    // 전용 이펙트 아트가 없어 런타임에 부드러운 가장자리를 가진 원형 스프라이트를 한 번만 만들어 재사용한다.
    private static Sprite GetOrCreateCircleSprite()
    {
        if (cached_circle_sprite != null) return cached_circle_sprite;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = 0f;
                if (dist <= radius - 4f) alpha = 1f;
                else if (dist <= radius) alpha = (radius - dist) / 4f; // 가장자리 4px만 부드럽게

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        // pixelsPerUnit = size로 만들어서, 이 스프라이트를 가진 오브젝트의 localScale(월드 단위)이
        // 곧바로 "지름"이 되도록 한다(위 CreateTelegraphVisual에서 diameter를 그대로 스케일에 대입).
        cached_circle_sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return cached_circle_sprite;
    }

    protected override void Die()
    {
        bool was_already_dead = IsDead;
        base.Die();

        if (!was_already_dead) OnDefeated?.Invoke();
    }

    /// <summary>
    /// 텔레그래프 코루틴이 끝까지 돌지 못하고 보스가 먼저 파괴돼도(웨이브 종료 시
    /// DespawnAllAliveEnemies() 등) 떠 있던 원이 남지 않도록 확실히 지운다.
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (active_telegraph != null) Destroy(active_telegraph);
    }
}
