using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 디스크 기획서(`20260810_디스크기획서_Ver01_김재원.pdf`, 2026-08-12 반영)의 21종 효과 중
/// <b>상시 스탯 가감이 아닌 나머지 전부</b>(처치 시 발동/주기/조건부/확률/아우라/1회성)를 실행한다.
///
/// 상시 스탯 가감(DiscEffectType.StatModifier 전체, PassiveAuraSlow의 자기 회피율 성분)은
/// ShopManager.EquipDisc()가 장착하는 순간 RunState.DiscStatBonuses에 한 번만 반영하므로
/// 여기서 다루지 않는다 - 그 두 곳의 책임이 겹치지 않도록 주의할 것.
///
/// Player에 자동으로 붙는다(PlayerRobotController.Awake()가 HitFlash와 같은 방식으로
/// AddComponent한다). 같은 종류 디스크를 여러 장 장착해도(기획서 "디스크는 중복하여 착용
/// 가능") 디스크별로 독립된 타이머를 두지 않고 <see cref="CountCopies"/>로 장 수를 구해
/// 수치에 곱하는 방식으로 단순화했다 - 예: 이끼 낀 2장 = 5초마다 회복량이 2배.
/// </summary>
[DefaultExecutionOrder(10000)]
public class DiscEffectRuntime : MonoBehaviour
{
    private PlayerRobotController player;
    private PlayerShootManager shootManager;
    private Dictionary<int, DiscData> disc_by_id;
    private WaveManager subscribed_wave_manager;

    // 주기 효과(이끼 낀)의 다음 발동 시각. discId -> 시각.
    private readonly Dictionary<int, float> periodic_next_time = new Dictionary<int, float>();

    // 오실레이션 효과(공명의 소리)의 위상과 다음 전환 시각.
    private readonly Dictionary<int, bool> oscillate_phase = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> oscillate_next_time = new Dictionary<int, float>();

    private void Awake()
    {
        player = GetComponent<PlayerRobotController>();
        shootManager = FindFirstObjectByType<PlayerShootManager>();
    }

    private void OnEnable()
    {
        EnemyUnit.OnKilledByPlayer += HandleEnemyKilled;

        subscribed_wave_manager = FindFirstObjectByType<WaveManager>();
        if (subscribed_wave_manager != null) subscribed_wave_manager.OnWaveStarted += HandleWaveStarted;
    }

    private void OnDisable()
    {
        EnemyUnit.OnKilledByPlayer -= HandleEnemyKilled;
        if (subscribed_wave_manager != null) subscribed_wave_manager.OnWaveStarted -= HandleWaveStarted;
    }

    private void Update()
    {
        if (player == null || player.IsDead) return;
        if (GameOverManager.IsGameOver || GameWinManager.IsGameWon || GameFlowManager.IsIntermission) return;
        if (!EnsureCatalog() || RunState.EquippedDiscIds.Count == 0) return;

        UpdatePeriodicAndOscillating();
        UpdateConditionalMoveSpeed();
        UpdateAuraSlow();
    }

    /// <summary>ShopManager.Catalog(ShopCatalog)에서 disc_id -> DiscData 캐시를 한 번만 만든다.</summary>
    private bool EnsureCatalog()
    {
        if (disc_by_id != null) return disc_by_id.Count > 0;

        ShopManager shopManager = FindFirstObjectByType<ShopManager>();
        ShopCatalog catalog = shopManager != null ? shopManager.Catalog : null;

        disc_by_id = new Dictionary<int, DiscData>();
        if (catalog == null) return false;

        foreach (DiscData d in catalog.Discs) disc_by_id[d.discId] = d;
        return disc_by_id.Count > 0;
    }

    private int CountCopies(int discId)
    {
        int count = 0;
        foreach (int id in RunState.EquippedDiscIds) if (id == discId) count++;
        return count;
    }

    // ── 처치 시 발동 (EnemyUnit.OnKilledByPlayer 구독) ──────────────

    private void HandleEnemyKilled(EnemyUnit killed)
    {
        if (player == null || player.IsDead || !EnsureCatalog()) return;

        HashSet<int> processed = new HashSet<int>();
        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (!processed.Add(discId)) continue; // 같은 디스크는 copies로 한 번에 반영(아래 각 case 참고)
            if (!disc_by_id.TryGetValue(discId, out DiscData disc)) continue;

            int copies = CountCopies(discId);

            switch (disc.effectType)
            {
                case DiscEffectType.OnKillChainLightning:
                    for (int i = 0; i < copies; i++) TryChainLightning(killed, disc.flatValue);
                    break;

                case DiscEffectType.OnKillTempMoveAtkSpeed:
                    player.ApplyTempStatBonus(StatType.MoveSpeed, disc.amountA * copies, disc.duration);
                    if (shootManager != null) shootManager.ApplyTempAttackSpeedBuff(disc.amountB * copies, disc.duration);
                    break;

                case DiscEffectType.OnKillTempDefDodge:
                    player.ApplyTempStatBonus(StatType.Def, disc.amountA * copies, disc.duration);
                    player.ApplyTempStatBonus(StatType.Avoid, disc.amountB * copies, disc.duration);
                    break;

                case DiscEffectType.OnKillHeal:
                    player.Heal(Mathf.RoundToInt(disc.flatValue * copies));
                    break;

                case DiscEffectType.OnKillStackStat:
                    ApplyKillStack(discId, disc, copies);
                    break;
            }
        }
    }

    /// <summary>가장 가까운 다른 살아있는 적에게 고정 피해를 준다(교향곡:번개).</summary>
    private void TryChainLightning(EnemyUnit source, float damage)
    {
        EnemyUnit best = null;
        float best_dist = float.MaxValue;

        foreach (EnemyUnit e in EnemyUnit.Alive)
        {
            if (e == null || e == source || e.IsDead) continue;
            float d = (e.transform.position - source.transform.position).sqrMagnitude;
            if (d < best_dist) { best_dist = d; best = e; }
        }

        if (best != null) best.TakeDamage(Mathf.RoundToInt(damage));
    }

    /// <summary>처치마다 stat이 cap까지 누적되는 효과(교향곡:화염/바람 소리/금속음/교향곡:암석).</summary>
    private void ApplyKillStack(int discId, DiscData disc, int copies)
    {
        RunState.DiscStackProgress.TryGetValue(discId, out float progress);
        float next = Mathf.Min(disc.cap, progress + disc.amountA * copies);
        float delta = next - progress;
        if (delta <= 0f) return;

        RunState.DiscStackProgress[discId] = next;
        if (!RunState.DiscStatBonuses.ContainsKey(disc.statA)) RunState.DiscStatBonuses[disc.statA] = 0f;
        RunState.DiscStatBonuses[disc.statA] += delta;
        RunState.NotifyChanged();
    }

    // ── 주기/오실레이션 (매 프레임 시각 비교) ────────────────────────

    private void UpdatePeriodicAndOscillating()
    {
        HashSet<int> processed = new HashSet<int>();
        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (!processed.Add(discId)) continue;
            if (!disc_by_id.TryGetValue(discId, out DiscData disc)) continue;

            int copies = CountCopies(discId);

            if (disc.effectType == DiscEffectType.PeriodicHeal)
            {
                if (!periodic_next_time.TryGetValue(discId, out float next)) next = Time.time + disc.interval;
                if (Time.time >= next)
                {
                    player.Heal(Mathf.RoundToInt(disc.flatValue * copies));
                    periodic_next_time[discId] = Time.time + disc.interval;
                }
            }
            else if (disc.effectType == DiscEffectType.OscillatingAtkDef)
            {
                if (!oscillate_next_time.TryGetValue(discId, out float next)) next = Time.time; // 장착 즉시 첫 위상 적용
                if (Time.time >= next)
                {
                    bool phase = oscillate_phase.TryGetValue(discId, out bool p) && p;
                    phase = !phase;
                    oscillate_phase[discId] = phase;
                    oscillate_next_time[discId] = Time.time + disc.interval;

                    float atk_delta = disc.amountA * copies * (phase ? 1f : -1f);
                    float def_delta = disc.amountB * copies * (phase ? -1f : 1f);
                    // 다음 전환까지(여유 0.1초) 유지 - HandleEnemyKilled류 처럼 즉시 만료되면 안 되므로
                    // interval 전체를 지속시간으로 준다.
                    player.ApplyTempStatBonus(StatType.Atk, atk_delta, disc.interval + 0.1f);
                    player.ApplyTempStatBonus(StatType.Def, def_delta, disc.interval + 0.1f);
                }
            }
        }
    }

    // ── 조건부 이동속도 (위장) ────────────────────────────────────

    private void UpdateConditionalMoveSpeed()
    {
        float bonus = 0f;

        if (shootManager != null && !shootManager.IsTargetingEnemy)
        {
            foreach (int discId in RunState.EquippedDiscIds)
            {
                if (disc_by_id.TryGetValue(discId, out DiscData disc) && disc.effectType == DiscEffectType.MoveSpeedWhenNotAttacking)
                    bonus += disc.amountA;
            }
        }

        player.SetConditionalMoveSpeedBonus(bonus);
    }

    // ── 아우라 (물 빠지는 소리) ───────────────────────────────────

    private void UpdateAuraSlow()
    {
        bool any_aura = false;
        float combined_multiplier = 1f;
        float max_radius = 0f;

        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (!disc_by_id.TryGetValue(discId, out DiscData disc) || disc.effectType != DiscEffectType.PassiveAuraSlow) continue;

            any_aura = true;
            combined_multiplier *= Mathf.Clamp01(1f - disc.amountB / 100f);
            if (disc.radius > max_radius) max_radius = disc.radius;
        }

        // 아우라가 아예 없으면 굳이 전체 적을 순회하지 않는다(매 프레임 O(n) 비용 절약).
        if (!any_aura)
        {
            foreach (EnemyUnit e in EnemyUnit.Alive) if (e != null) e.SetAuraSlowMultiplier(1f);
            return;
        }

        float radius_sq = max_radius * max_radius;
        Vector3 origin = player.transform.position;

        foreach (EnemyUnit e in EnemyUnit.Alive)
        {
            if (e == null) continue;
            Vector3 diff = e.transform.position - origin;
            diff.z = 0f;
            e.SetAuraSlowMultiplier(diff.sqrMagnitude <= radius_sq ? combined_multiplier : 1f);
        }
    }

    // ── 웨이브 시작 (에너지 베리어 실드 초기화) ───────────────────────

    private void HandleWaveStarted(int wave)
    {
        if (player == null || !EnsureCatalog()) return;

        float shield_total = 0f;
        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (disc_by_id.TryGetValue(discId, out DiscData disc) && disc.effectType == DiscEffectType.WaveShieldMaxHp)
                shield_total += disc.flatValue;
        }

        player.SetShieldMaxHp(Mathf.RoundToInt(shield_total));
        player.RefillShield();
    }

    // ── 공격 시 확률 발동 (777) - PlayerShootManager.ComputeDamage가 호출 ───

    /// <summary>등록된 확률형 공격 디스크(777)를 굴려 데미지를 배로 늘린다. 없으면 원래 값 그대로.</summary>
    public float ApplyOnAttackProcs(float damage)
    {
        if (!EnsureCatalog()) return damage;

        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (!disc_by_id.TryGetValue(discId, out DiscData disc) || disc.effectType != DiscEffectType.OnAttackChanceBonusDamage) continue;

            if (Random.value < disc.chance01) damage *= (1f + disc.multiplier);
        }

        return damage;
    }

    // ── 마지막 발악 - PlayerRobotController.TakeDamage가 치명타 직전에 호출 ───

    /// <summary>남은 "마지막 발악" 사용 횟수가 있으면 하나 소비하고 true를 돌려준다.</summary>
    public bool TryTriggerLastStand(out float speedBonusRatio, out float invulnDuration)
    {
        speedBonusRatio = 0f;
        invulnDuration = 0f;
        if (!EnsureCatalog()) return false;

        foreach (int discId in RunState.EquippedDiscIds)
        {
            if (!disc_by_id.TryGetValue(discId, out DiscData disc) || disc.effectType != DiscEffectType.LastStand) continue;
            if (!RunState.DiscUsesRemaining.TryGetValue(discId, out int remaining) || remaining <= 0) continue;

            RunState.DiscUsesRemaining[discId] = remaining - 1;
            speedBonusRatio = disc.amountA;
            invulnDuration = disc.duration;
            return true;
        }

        return false;
    }
}
