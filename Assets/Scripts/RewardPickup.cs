using System.Collections.Generic;
using UnityEngine;

public enum RewardType
{
    Gold,
    Exp,
    PartBox // 부품 상자 - 습득 즉시 개봉되지 않고 RunState.UnopenedPartBoxCount만 늘어난다(개봉은 정비 화면에서)
}

/// <summary>
/// 골드/AI 코어 경험치 보상 하나를 필드 위의 물리적 오브젝트로 표현한다.
/// 플레이어가 SphereCollider(트리거) 범위 안에 들어오면 자동으로 흡수되어
/// RunState에 더해지고 사라진다.
/// RewardPickupManager.SpawnReward()가 생성 직후 Init()으로 종류/수량을 넘겨준다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RewardPickup : MonoBehaviour
{
    // 화면 밖 부품 상자 위치를 화살표로 안내하는 PartBoxIndicatorUI가 순회하는 목록
    // (EnemyUnit.Alive와 동일한 패턴). PartBox 타입만 등록한다.
    public static readonly List<RewardPickup> AlivePartBoxes = new List<RewardPickup>();

    public RewardType Type { get; private set; }
    public int Amount { get; private set; }

    private bool collected;

    /// <summary>
    /// 씬을 다시 시작할 때 이전 판의 파괴된 픽업이 목록에 남지 않도록 비운다
    /// (EnemyUnit.ResetStaticCaches()와 같은 이유). PlayerRobotController.Awake()가 호출한다.
    /// </summary>
    public static void ResetStaticCaches() => AlivePartBoxes.Clear();

    public void Init(RewardType type, int amount)
    {
        Type = type;
        Amount = amount;

        if (Type == RewardType.PartBox) AlivePartBoxes.Add(this);
    }

    // 수령을 거치지 않고 파괴되는 경로(씬 전환, 웨이브 종료 정리 등)에서도 목록이 새지 않도록
    // 여기서 한 번 더 지운다. CollectImmediately()에서 이미 지운 경우 Remove는 아무 일도 하지 않는다.
    private void OnDestroy() => AlivePartBoxes.Remove(this);

    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;

        PlayerRobotController player = other.GetComponent<PlayerRobotController>();
        if (player == null) player = other.GetComponentInParent<PlayerRobotController>();
        if (player == null) return; // 플레이어 외의 다른 콜라이더(투사체 등)와는 반응하지 않음

        CollectImmediately();
    }

    /// <summary>
    /// 플레이어가 닿지 않았어도 즉시 수령 처리한다. 웨이브가 끝나 정비 화면으로 넘어갈 때
    /// GameFlowManager가 필드에 남은 픽업을 정리하면서 호출한다 - 그냥 지우면 못 주운 보상이
    /// 사라져 플레이어가 손해를 보기 때문.
    /// </summary>
    public void CollectImmediately()
    {
        if (collected) return;
        collected = true;

        if (Type == RewardType.PartBox) AlivePartBoxes.Remove(this);

        switch (Type)
        {
            case RewardType.Gold:
                // "금화의 잔향 디스크" - 골드 획득량에 %가 곱해진다(StatType.GoldGain, RobotStats와
                // 무관한 별도 값이라 RunState.DiscStatBonuses에서 직접 읽는다).
                // 머리 효과(미니 픽시 -50%)는 그 위에 곱한다.
                //
                // 2026-08-19 버그 수정: 예전에는 곱한 결과를 여기서 곧바로 Mathf.RoundToInt로
                // 잘라서, 골드 1~3짜리 픽업(사실상 대부분의 몬스터)에서 배율이 통째로 사라졌다
                // - 미니 픽시(-50%)는 골드 1짜리가 RoundToInt(0.5)=0이 되어 아예 안 올랐고,
                // 금화의 잔향(+10%)은 1.1/2.2/3.3이 전부 원래 값으로 반올림돼 증가분이 없었다.
                // 이제 소수점 나머지를 이월하는 누적기로 넘겨 기대값을 정확히 보존한다
                // (RunState.AddGoldWithFraction 주석 참고).
                // 2026-08-20 아카식 레지스터(메모리 파츠)도 골드 획득량을 올린다 - 파츠 보너스는
                // RunState.PartStatBonuses에 있으므로 PartEffects가 배율로 바꿔 함께 곱한다.
                float gold_gain_percent = RunState.DiscStatBonuses.TryGetValue(StatType.GoldGain, out float g) ? g : 0f;
                RunState.AddGoldWithFraction(Amount * (1f + gold_gain_percent / 100f) * HeadEffects.GoldGainMultiplier
                                             * PartEffects.GainMultiplier(StatType.GoldGain));
                break;

            // 부품 상자는 머리(로봇)의 적재량 상한이 있으므로 정비 매니저를 거쳐 지급한다.
            // EnemyUnit이 드랍 시점에도 상한을 보지만, 여러 몬스터가 거의 동시에 죽으면
            // 둘 다 그 검사를 통과한 뒤 여기서 상한을 넘을 수 있다(초과분은 버려진다).
            case RewardType.PartBox:
                if (ModdingManager.Instance != null) ModdingManager.Instance.AddPartBoxes(Amount);
                else RunState.UnopenedPartBoxCount += Amount;
                break;

            default:
                // 머리 효과(미니 픽시 경험치 +50%). 반올림 후 최소 1은 보장한다 - 경험치 1짜리
                // 픽업에 배율이 곱해져 0으로 사라지면 "먹었는데 아무 일도 없는" 픽업이 된다.
                // 2026-08-20 뉴럴 캐시·아카식 레지스터(메모리 파츠)의 경험치 획득량 +%도 함께 곱한다.
                RunState.CoreExp += Mathf.Max(1, Mathf.RoundToInt(Amount * HeadEffects.ExpGainMultiplier
                                                                  * PartEffects.GainMultiplier(StatType.ExpGain)));
                break;
        }

        RunState.NotifyChanged();
        Destroy(gameObject);
    }
}
