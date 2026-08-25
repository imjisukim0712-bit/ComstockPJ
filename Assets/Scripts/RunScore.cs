using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 점수(Score) 시스템 - 엔드리스 모드·악세사리·랭킹의 선행 조건(2026-08-19 Phase B).
///
/// 공식(임시값, 20웨이브 클리어 기준 약 45,000점이 되도록 잡았다):
/// <c>클리어한 웨이브 수 x 1,000 + 누적 처치 수 x 10 + AI 코어 레벨업 횟수 x 200 + 남은 골드 x 1
/// + 악세사리 점수 합계</c>.
///
/// <b>웨이브·코어 레벨은 "진행도"가 아니라 "현재 값"이라 그대로 곱하면 시작하자마자 기본점수가
/// 생긴다</b>(2026-08-20 버그 리포트 - "기본점수 1200점 주는거 고치셈, 0점부터 시작해야됨").
/// `WaveNumber`는 웨이브 1을 시작하는 순간 1이 되고(<see cref="WaveManager"/>), `CoreLevel`은
/// 업그레이드를 하나도 안 해도 기본값이 1이다(<see cref="RunState.CoreLevel"/>) - 그래서 런을
/// 시작하자마자 1x1000 + 1x200 = 1200점이 조건 없이 붙어 있었다. 그래서 <see cref="ComputeBreakdown"/>
/// 은 둘 다 1을 뺀 값(= 실제로 "클리어한" 웨이브 수 / "레벨업한" 횟수)을 곱한다.
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
    }

    private static void HandleEnemyKilled(EnemyUnit unit) => KillCount++;

    public static void Reset()
    {
        KillCount = 0;
        AccessoryScore = 0;
        // 처치 이벤트 구독은 여기서 풀지 않는다 - 다음 판에도 그대로 필요하고,
        // EnsureKillTrackingSubscribed()가 -=/+=로 중복 구독을 막는다.
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
            // WaveNumber(웨이브 1 시작 시 1)·CoreLevel(기본값 1)은 둘 다 "현재 값"이지 "진행도"가
            // 아니므로 1을 빼서 실제로 쌓은 진행도만 점수화한다(위 클래스 설명 참고).
            WaveScore = Mathf.Max(0, RunState.WaveNumber - 1) * WaveWeight,
            KillScore = KillCount * KillWeight,
            CoreLevelScore = Mathf.Max(0, RunState.CoreLevel - 1) * CoreLevelWeight,
            GoldScore = RunState.Gold * GoldWeight,
            AccessoryScore = AccessoryScore
        };
        b.Total = b.WaveScore + b.KillScore + b.CoreLevelScore + b.GoldScore + b.AccessoryScore;
        return b;
    }

    public static int ComputeTotal() => ComputeBreakdown().Total;

    /// <summary>
    /// 닉네임 입력 팝업(<see cref="NicknameInputPopup"/>)의 기본값/제안값으로 쓸 이름을 정한다
    /// (2026-08-20). 선택한 로봇(머리) 이름을 기본으로 삼는다 - 아직 로봇 정보를 못 찾으면
    /// "플레이어"로 대체한다.
    /// </summary>
    public static string ResolveDefaultPlayerName()
    {
        PlayerRobotController player = Object.FindFirstObjectByType<PlayerRobotController>();
        if (player != null && GameDataManager.Instance != null &&
            GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data))
        {
            return data.Robot();
        }

        return Loc.T("common.player");
    }

    /// <summary>
    /// 현재 점수를 랭킹 서비스에 제출한다(2026-08-19 Phase C). 정산 팝업("타이틀로")과
    /// 일시정지 메뉴("나가기", 엔드리스 중일 때만)가 공유하는 진입점이라 여기 한 곳에 둔다.
    ///
    /// <paramref name="playerName"/>은 <see cref="NicknameInputPopup"/>에서 사용자가 확정한
    /// 닉네임이다(2026-08-20 - 예전에는 로봇 이름을 자동으로 썼지만 사용자 요청으로 닉네임
    /// 입력 화면이 생겼다). <b>맵마다 랭킹이 분리된다</b>(앞으로 맵이 여러 개 추가될 예정이라
    /// 현재 활성 씬 이름을 mapId로 그대로 쓴다. 이 메서드는 항상 맵 씬(Ground01 등) 안에서
    /// 호출되므로 별도 맵 선택 상태 없이 <see cref="SceneManager.GetActiveScene"/>만으로 충분하다).
    /// </summary>
    public static void SubmitToLeaderboard(string playerName)
    {
        string name = string.IsNullOrWhiteSpace(playerName) ? ResolveDefaultPlayerName() : playerName;
        string mapId = SceneManager.GetActiveScene().name;
        int score = ComputeTotal();
        LeaderboardService.Current.SubmitScore(mapId, name, score, success =>
        {
            Debug.Log(success ? $"랭킹 제출 완료 (맵 {mapId}, 닉네임 {name}, 점수 {score})" : "랭킹 제출 실패");
        });
    }
}
