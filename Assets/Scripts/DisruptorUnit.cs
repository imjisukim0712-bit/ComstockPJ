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
/// </summary>
public class DisruptorUnit : EnemyUnit
{
    [Header("디스럭터 자폭 (전부 밸런스 미확정 임시값)")]
    [Tooltip("한 번에 이 비율(최대체력 대비) 이상의 피해를 받으면 사거리와 무관하게 즉시 자폭 신호를 켠다")]
    [SerializeField] private float bigHitThresholdRatio = 0.3f;
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

    // "큰 피해"를 입으면 정상적인 체력 차감 대신 자폭 신호를 켠다. 심지가 붙은 동안은
    // 이 메서드 자체가 아무 일도 하지 않으므로 완전히 무적이다.
    public override void TakeDamage(float amount, float def_ignore_percent = 0f, int source_weapon_id = 0, bool isCrit = false)
    {
        if (IsDead || fuseStarted) return;

        bool big_hit = MaxHp > 0 && amount >= MaxHp * bigHitThresholdRatio;
        if (big_hit)
        {
            StartFuse();
            return;
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
        StartCoroutine(FuseThenExplode());
    }

    private IEnumerator FuseThenExplode()
    {
        float elapsed = 0f;
        bool flicker_on = false;

        while (elapsed < fuseSeconds)
        {
            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) yield break;

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
    }
}
