using System.Collections;
using UnityEngine;

/// <summary>
/// 디스럭터(자폭 좀비, 좀비 기획서 Ver04 p.18) - "큰 피해를 입거나 로봇 주변에 다다르면
/// 폭발하며 주변에 피해". 두 경로 모두 같은 <see cref="StartFuse"/>로 귀결된다.
///
/// "큰 피해"의 구체적 기준이 기획서에 없어 <see cref="bigHitThresholdRatio"/>(현재 최대체력
/// 대비 비율)로 임시 정의했다.
///
/// <b>2026-08-20 사용자 요청 - 즉시 폭발 대신 깜빡이다 2초 뒤 폭발</b>: 자폭 신호(<see cref="StartFuse"/>)가
/// 켜지면 곧바로 터지지 않고 <see cref="fuseSeconds"/> 동안 몸이 깜빡이다 그 끝에 실제로 터진다.
/// 심지가 붙은 동안은 <b>완전히 무적</b>이다(사용자 지시: "이때는 무적 상태로, 디스럭터가 죽진
/// 않음") - <see cref="TakeDamage"/>가 <see cref="fuseStarted"/>일 때 아무 것도 하지 않고
/// 돌아간다. 심지에 불이 붙는 계기(큰 피해 / 사거리 안 도달)는 그대로이며, 이미 심지가 붙은
/// 뒤에 같은 계기가 다시 와도(예: 공격 쿨다운이 돌아 재시도) <see cref="fuseStarted"/> 가드가
/// 막아 폭발 코루틴이 중복 실행되지 않는다.
///
/// <b>2026-08-21 사용자 제공 폭발 애니메이션 적용</b> - 실제로 터지는 순간(<see cref="Explode"/>)
/// <see cref="DisruptorExplosionEffect"/>가 8프레임 애니메이션을 한 번 재생한다. 그 전까지는
/// 폭발 시각 효과가 전혀 없었다. 다른 폭발형 무기(로켓런처 등)의 스플래시 연출과는 완전히
/// 별개의 애셋/코드다.
/// </summary>
public class DisruptorUnit : EnemyUnit
{
    [Header("디스럭터 자폭 (전부 밸런스 미확정 임시값)")]
    [Tooltip("한 번에 이 비율(최대체력 대비) 이상의 피해를 받으면 사거리와 무관하게 즉시 자폭 신호를 켠다")]
    [SerializeField] private float bigHitThresholdRatio = 0.3f;

    [Tooltip("남은 체력이 이 비율(최대체력 대비) 이하로 떨어지면 무조건 자폭 신호를 켠다. " +
             "2026-08-24 사용자 지정: '자폭좀비는 항상 체력이 낮아지면 제자리에 멈춰 폭발을 시작해야함' - " +
             "예전에는 한 방에 큰 피해를 받는 경우만 자폭했으므로, 작은 피해를 여러 번 맞으면 " +
             "자폭하지 않고 그냥 죽었다")]
    [SerializeField] private float fuseHpRatio = 0.3f;
    // 2026-08-19 사용자 요청으로 폭발 범위 -25%(3.0 → 2.25).
    [Tooltip("자폭 폭발 반경(월드 유닛)")]
    [SerializeField] private float explosionRadius = 2.25f;

    [Header("자폭 예고 - 깜빡임 (2026-08-20)")]
    [Tooltip("자폭 신호가 켜진 뒤 실제로 터지기까지 걸리는 시간(초)")]
    [SerializeField] private float fuseSeconds = 2f;
    [Tooltip("깜빡일 때의 경고색")]
    [SerializeField] private Color fuseFlickerColor = new Color(1f, 0.1f, 0.1f, 1f);
    [Tooltip("원래색 <-> 경고색이 한 번 바뀌는 간격(초). 짧을수록 빠르게 깜빡인다")]
    [SerializeField] private float flickerInterval = 0.15f;

    // EnemyUnit.body_sprite_renderer는 private이라 서브클래스에서 못 쓴다 - 같은 오브젝트라
    // 직접 다시 받아온다. original_body_color는 EnemyUnit이 protected로 열어둬 그대로 쓴다.
    private SpriteRenderer body_sprite;

    private bool fuseStarted;

    protected override void Awake()
    {
        base.Awake();
        body_sprite = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 피해 처리. 심지가 붙은 동안은 이 메서드 자체가 아무 일도 하지 않으므로 완전히 무적이다.
    ///
    /// <b>디스럭터는 "그냥 죽는" 경로가 없다</b>(2026-08-24 사용자 지정) - 큰 피해 한 방이든
    /// 작은 피해 누적이든, 체력이 <see cref="fuseHpRatio"/> 밑으로 내려가는 순간 자폭 신호가
    /// 켜지고 그 뒤로는 무적이라 <b>반드시 폭발한다</b>. 예전에는 <see cref="bigHitThresholdRatio"/>
    /// 조건만 있어서, 약한 무기로 여러 번 때리면 자폭하지 않고 조용히 죽었다.
    /// </summary>
    public override void TakeDamage(float amount, float def_ignore_percent = 0f, int source_weapon_id = 0, bool isCrit = false)
    {
        if (IsDead || fuseStarted) return;

        bool big_hit = MaxHp > 0 && amount >= MaxHp * bigHitThresholdRatio;

        // 이 피해가 들어가면 자폭 임계치 밑으로 내려가는지를 <b>맞기 전에</b> 판단한다.
        // 맞은 뒤에 보면 그 피해로 이미 죽어(체력 0) base가 Die()를 불러버려서, 자폭할 기회가
        // 사라진다("죽기 전에 처치하면 자폭하지 않는" 문제의 원인).
        float predicted_hp = CurrentHp - ComputeEffectiveDamage(amount, def_ignore_percent);
        bool drops_low = MaxHp > 0f && predicted_hp <= MaxHp * Mathf.Clamp01(fuseHpRatio);

        if (big_hit || drops_low)
        {
            StartFuse();
            return; // 이 피해는 들어가지 않는다 - 심지가 붙은 뒤로는 완전 무적이라 어차피 같은 결과다
        }

        base.TakeDamage(amount, def_ignore_percent, source_weapon_id, isCrit);
    }

    // 사거리 안(= "로봇 주변에 다다르면")에 들어와 공격 모션이 끝나면 자폭 신호를 켠다.
    protected override void ExecuteAttackEffect()
    {
        StartFuse();
    }

    private void StartFuse()
    {
        if (fuseStarted) return;
        fuseStarted = true;

        // "제자리에 멈춰 폭발을 시작"(2026-08-24 사용자 지정) - IsAttacking을 켜 두면 베이스의
        // 이동(ComputeSeekDirection)·추가 공격 시도·걷기 애니메이션이 한꺼번에 잠긴다.
        // 아래 FixedUpdate 오버라이드가 속도까지 0으로 붙잡는다(분리 벡터·넉백으로도 밀리지 않게).
        IsAttacking = true;
        if (rb != null) rb.linearVelocity = Vector3.zero;

        StartCoroutine(FuseThenExplode());
    }

    /// <summary>심지가 붙은 뒤로는 어떤 힘으로도 움직이지 않는다(넉백·밀림·분리 벡터 포함).</summary>
    protected override void FixedUpdate()
    {
        if (fuseStarted)
        {
            if (rb != null) rb.linearVelocity = Vector3.zero;
            return;
        }

        base.FixedUpdate();
    }

    private IEnumerator FuseThenExplode()
    {
        float elapsed = 0f;
        bool flicker_on = false;

        while (elapsed < fuseSeconds)
        {
            // 판이 끝나면 폭발 판정 없이 즉시 사라진다. 예전에는 여기서 그냥 yield break라
            // <b>무적인 채로 필드에 영원히 남았다</b>(자폭도 안 하고 죽지도 않는 상태).
            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon)
            {
                Destroy(gameObject);
                yield break;
            }

            if (body_sprite != null)
            {
                flicker_on = !flicker_on;
                body_sprite.color = flicker_on ? fuseFlickerColor : original_body_color;
            }

            float wait = Mathf.Min(flickerInterval, fuseSeconds - elapsed);
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }

        if (body_sprite != null) body_sprite.color = original_body_color;

        Explode();
    }

    private void Explode()
    {
        // 2026-08-21 사용자 제공 폭발 애니메이션(Resources/DisruptorExplosion) - 다른 폭발형
        // 무기(로켓런처 등)의 스플래시 연출과는 별개로 디스럭터 자폭에만 적용한다.
        int sorting_order = body_sprite != null ? body_sprite.sortingOrder + 10 : 20;
        DisruptorExplosionEffect.Play(transform.position, explosionRadius, sorting_order);

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (Collider hit in hits)
        {
            PlayerRobotController hit_player = hit.GetComponent<PlayerRobotController>();
            if (hit_player == null) hit_player = hit.GetComponentInParent<PlayerRobotController>();
            if (hit_player != null)
            {
                hit_player.TakeDamage(Atk, transform.position);
                break; // 플레이어는 한 명뿐이라 찾으면 바로 종료
            }
        }

        Die(); // 자폭 = 사망 처리(처치 보상/드랍은 그대로 유지)

        // <b>폭발했으면 반드시 사라진다</b>(2026-08-24 사용자 지정). Die()는 이미
        // Destroy(gameObject)까지 하지만, 서브클래스가 Die()를 오버라이드해 연출을 넣는 경우
        // (보스처럼)에도 자폭체가 필드에 남지 않도록 여기서 한 번 더 못을 박는다.
        // 이미 파괴 예약된 오브젝트에 Destroy를 다시 불러도 안전하다.
        if (this != null && gameObject != null) Destroy(gameObject);
    }
}
