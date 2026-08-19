using UnityEngine;

/// <summary>
/// 점수(Score) 시스템 - 엔드리스 모드·악세사리·랭킹의 선행 조건(2026-08-19 Phase B).
///
/// 공식(임시값, 20웨이브 클리어 기준 약 45,000점이 되도록 잡았다):
/// <c>도달 웨이브 x 1,000 + 누적 처치 수 x 10 + 최종 AI 코어 레벨 x 200 + 남은 골드 x 1
/// + 악세사리 점수 합계</c>.
///
/// 누적 처치 수는 지금까지 어디에도 집계되지 않았으므로(해금 조건 "적 N마리 처치"도 이 값을
/// 공유한다) <see cref="EnsureKillTrackingSubscribed"/>가 <see cref="EnemyUnit.OnKilledByPlayer"/>를
/// 직접 구독해서 센다.
/// </summary>
public static class RunScore
{
    public const int WaveWeight = 1000;
    public const int KillWeight = 10;
    public const int CoreLevelWeight = 200;
    public const int GoldWeight = 1;

    public static int KillCount { get; private set; }

    /// <summary>Phase D(악세사리)가 구매 시점에 더한다. 효과 없는 순수 점수 보너스 아이템이라
    /// 여기 외에는 아무 데도 쓰이지 않는다.</summary>
    public static int AccessoryScore { get; private set; }

    public static void AddAccessoryScore(int amount) => AccessoryScore += Mathf.Max(0, amount);

    private static bool killTrackingSubscribed;

    /// <summary>
    /// <see cref="EnemyUnit.OnKilledByPlayer"/>는 static 이벤트라, 씬을 재시작해도(도메인 리로드가
    /// 없는 한) 이전 판의 구독이 그대로 남을 수 있다. DiscEffectRuntime처럼 MonoBehaviour의
    /// OnEnable/OnDisable로 자동 해제되는 구조가 아니므로, <b>먼저 해제하고 다시 구독</b>하는
    /// 방식으로 중복 카운트를 막는다(HeadEffects.RegisterPlayer/LuckBonus.RegisterPlayer와 같은
    /// 자리 - PlayerRobotController.Awake에서 호출한다).
    /// </summary>
    public static void EnsureKillTrackingSubscribed()
    {
        EnemyUnit.OnKilledByPlayer -= HandleEnemyKilled;
        EnemyUnit.OnKilledByPlayer += HandleEnemyKilled;
        killTrackingSubscribed = true;
    }

    private static void HandleEnemyKilled(EnemyUnit unit) => KillCount++;

    public static void Reset()
    {
        KillCount = 0;
        AccessoryScore = 0;
        // killTrackingSubscribed는 그대로 둔다 - 구독 자체는 씬이 살아있는 동안 계속 유지해야
        // 하고(다음 판에도 필요), EnsureKillTrackingSubscribed()가 어차피 -=/+=로 중복을 막는다.
    }

    /// <summary>점수 내역 한 줄씩 + 합계. 정산 팝업/게임오버 요약 화면이 함께 쓴다.</summary>
    public struct Breakdown
    {
        public int WaveScore;
        public int KillScore;
        public int CoreLevelScore;
        public int GoldScore;
        public int AccessoryScore;
        public int Total;
    }

    public static Breakdown ComputeBreakdown()
    {
        var b = new Breakdown
        {
            WaveScore = RunState.WaveNumber * WaveWeight,
            KillScore = KillCount * KillWeight,
            CoreLevelScore = RunState.CoreLevel * CoreLevelWeight,
            GoldScore = RunState.Gold * GoldWeight,
            AccessoryScore = AccessoryScore
        };
        b.Total = b.WaveScore + b.KillScore + b.CoreLevelScore + b.GoldScore + b.AccessoryScore;
        return b;
    }

    public static int ComputeTotal() => ComputeBreakdown().Total;

    /// <summary>
    /// 현재 점수를 랭킹 서비스에 제출한다(2026-08-19 Phase C). 정산 팝업("타이틀로")과
    /// 일시정지 메뉴("나가기", 엔드리스 중일 때만)가 공유하는 진입점이라 여기 한 곳에 둔다.
    ///
    /// 플레이어 이름 입력 UI가 아직 없어(이번 범위 밖) 선택한 로봇(머리) 이름을 대신 쓴다.
    /// 실제 네트워크 연동(Firebase)은 없고 <see cref="LocalLeaderboardService"/>가 PlayerPrefs에
    /// 저장한다 - 사용자 지시: "파이어베이스는 나중에 연결할 거니까 그것만 빼고 만들어놔"
    /// (LeaderboardService.cs 참고).
    /// </summary>
    public static void SubmitToLeaderboard()
    {
        PlayerRobotController player = Object.FindFirstObjectByType<PlayerRobotController>();
        string name = "플레이어";
        if (player != null && GameDataManager.Instance != null &&
            GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data))
        {
            name = data.robot_name;
        }

        int score = ComputeTotal();
        LeaderboardService.Current.SubmitScore(name, score, success =>
        {
            Debug.Log(success ? $"랭킹 제출 완료 (점수 {score})" : "랭킹 제출 실패");
        });
    }
}
