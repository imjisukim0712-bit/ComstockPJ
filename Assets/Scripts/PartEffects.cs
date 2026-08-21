using UnityEngine;

/// <summary>
/// 파츠(<see cref="PartData.effect"/>)의 <b>특수효과 계산</b>을 한곳에 모은다.
/// 머리 고유 효과의 <c>HeadEffects</c>와 같은 역할·같은 구조이며, 이유도 같다 - 단순 가산은
/// <c>RunState.PartStatBonuses</c> + <c>RobotStats.ApplyBonus</c>로 끝나지만
/// "다른 스탯에서 파생되는 값"이나 "조건부 발동"은 합산 딕셔너리로 표현할 수 없다.
///
/// <b>호출 순서가 중요하다.</b> <see cref="ApplyStatBonuses"/>는 가산 보너스와 HeadEffects가
/// 모두 끝난 뒤에 불려야 한다 - 연극 마스크는 <b>최종</b> 행운을, 철인 헬멧은 <b>최종</b> 공격력을
/// 읽어야 하기 때문이다(RobotStats.Compute 참고).
///
/// static이지만 상태를 들고 있지 않다(매번 ModdingManager.Instance에서 장착 파츠를 읽는다) -
/// 씬을 다시 시작해도 이전 판의 값이 남지 않는다.
/// </summary>
public static class PartEffects
{
    /// <summary>탈(DefWhenLowHp)이 발동하는 체력 비율. 명세 표기 "체력이 50%이하일 때".</summary>
    public const float LowHpThreshold = 0.5f;

    /// <summary>
    /// 파생·비율 효과를 최종 스탯에 반영한다.
    /// 적용 순서: (1) 파생 방어력을 모두 더한 뒤 → (2) 방어력 %증가를 곱한다.
    /// 순서를 바꾸면 "행운에서 온 방어력"에는 %가 안 붙어 결과가 달라진다.
    /// </summary>
    public static void ApplyStatBonuses(ref AggregatedRobotStats stats)
    {
        ModdingManager modding = ModdingManager.Instance;
        if (modding == null) return;

        float defFromDerived = 0f;
        float defPercent = 0f;
        float moveSpeedPercent = 0f;
        float massPercent = 0f;

        foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
        {
            if (slot == PartSlot.ArmWeaponSocket) continue; // 소켓 파츠 효과는 무기 쪽에서 처리한다
            if (!modding.TryGetEquippedPart(slot, out PartData part)) continue;

            switch (part.effect)
            {
                case PartEffect.DefFromLuckPercent:
                    defFromDerived += stats.Luck * part.effectAmount * 0.01f;
                    break;

                case PartEffect.DefFromAtkPercent:
                    defFromDerived += stats.Atk * part.effectAmount * 0.01f;
                    break;

                case PartEffect.DefPercentBonus:
                    defPercent += part.effectAmount;
                    break;

                case PartEffect.MoveSpeedPercentBonus:
                    moveSpeedPercent += part.effectAmount;
                    break;

                case PartEffect.MassPercentBonus:
                    massPercent += part.effectAmount;
                    break;
            }
        }

        // 거미 다리의 질량 %효과("질량 * 0.5") - effect 필드는 다리마다 이동속도 %효과가 이미
        // 쓰고 있어서(파츠 하나엔 PartEffect가 하나뿐이다) 전용 필드(legMassPercent)로 뺐다.
        if (modding.TryGetEquippedPart(PartSlot.Leg, out PartData legPart) && legPart.legMassPercent != 0f)
        {
            massPercent += legPart.legMassPercent;
        }

        stats.Def += defFromDerived;
        if (defPercent != 0f) stats.Def *= 1f + defPercent * 0.01f;

        // 다리 기획서 Ver02 - 이동속도/질량 %효과도 방어력 %증가와 같은 자리에서 곱해진다
        // (가산 보너스·HeadEffects가 모두 끝난 값 기준).
        if (moveSpeedPercent != 0f) stats.MoveSpeed *= 1f + moveSpeedPercent * 0.01f;
        if (massPercent != 0f) stats.Mess *= 1f + massPercent * 0.01f;
    }

    /// <summary>
    /// 체력이 절반 이하일 때만 붙는 추가 방어력(탈). <see cref="RobotStats.Compute"/>는 현재 체력을
    /// 알 수 없으므로(스탯을 만드는 쪽이다) 피해를 계산하는 순간에 더한다 -
    /// <c>PlayerRobotController.TakeDamage</c>가 호출한다.
    /// </summary>
    public static float GetLowHpDefBonus(float currentHp, float maxHp)
    {
        if (maxHp <= 0f || currentHp / maxHp > LowHpThreshold) return 0f;

        ModdingManager modding = ModdingManager.Instance;
        if (modding == null) return 0f;

        float bonus = 0f;

        foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
        {
            if (slot == PartSlot.ArmWeaponSocket) continue;
            if (!modding.TryGetEquippedPart(slot, out PartData part)) continue;
            if (part.effect == PartEffect.DefWhenLowHp) bonus += part.effectAmount;
        }

        return bonus;
    }

    /// <summary>
    /// 근접 공격으로 받은 피해 중 공격자에게 되돌릴 비율(%). 가시 플레이트.
    /// 원거리(스피터 투사체)는 이 경로를 타지 않으므로 명세대로 근접에만 적용된다.
    /// </summary>
    public static float GetMeleeReflectPercent()
    {
        ModdingManager modding = ModdingManager.Instance;
        if (modding == null) return 0f;

        float percent = 0f;

        foreach (PartSlot slot in System.Enum.GetValues(typeof(PartSlot)))
        {
            if (slot == PartSlot.ArmWeaponSocket) continue;
            if (!modding.TryGetEquippedPart(slot, out PartData part)) continue;
            if (part.effect == PartEffect.MeleeReflectPercent) percent += part.effectAmount;
        }

        return percent;
    }

    /// <summary>
    /// 경험치·골드 획득량 배율. 파츠의 <see cref="StatType.ExpGain"/>/<see cref="StatType.GoldGain"/>
    /// 보너스는 <c>RunState.PartStatBonuses</c>에 이미 들어 있으므로 여기서 읽어 배율로 바꿔준다
    /// (디스크의 GoldGain을 RewardPickup이 읽는 것과 같은 패턴).
    /// </summary>
    public static float GainMultiplier(StatType type)
    {
        return RunState.PartStatBonuses.TryGetValue(type, out float percent)
            ? Mathf.Max(0f, 1f + percent * 0.01f)
            : 1f;
    }
}
