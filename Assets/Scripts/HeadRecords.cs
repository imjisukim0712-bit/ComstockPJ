using UnityEngine;

/// <summary>
/// 머리(로봇) 하나로 달성한 <b>최고 기록</b>(점수 / 도달 웨이브)을 기기에 저장한다
/// (2026-08-24 사용자 요청: "머리 선택창에서 머리 클릭해서 볼때 오른쪽에 나오는 정보창에
/// 이 머리를 사용하여 달성한 최대 점수, 최대 웨이브 등이 나오면 좋겠음").
///
/// 랭킹(<see cref="LeaderboardService"/>)과 달리 <b>머리별로</b> 나뉘고 서버로 올라가지 않는
/// 개인 기록이라 <see cref="PlayerPrefs"/>에 그대로 둔다(<see cref="MusicManager"/>의 볼륨,
/// <see cref="NicknameInputPopup"/>의 닉네임과 같은 관례).
///
/// 기록 시점은 런이 끝나는 지점 하나(<see cref="RunScore.SubmitToLeaderboard"/> 옆)로 모았다 -
/// 게임오버/승리/엔드리스 중단이 전부 그 경로를 지나므로 여기 한 번만 걸어두면 된다.
/// </summary>
public static class HeadRecords
{
    private const string ScoreKeyPrefix = "comstock_head_best_score_";
    private const string WaveKeyPrefix = "comstock_head_best_wave_";
    private const string PlayCountKeyPrefix = "comstock_head_play_count_";

    /// <summary>이 머리로 기록한 최고 점수(없으면 0).</summary>
    public static int GetBestScore(int robotId) => PlayerPrefs.GetInt(ScoreKeyPrefix + robotId, 0);

    /// <summary>이 머리로 도달한 최고 웨이브(없으면 0).</summary>
    public static int GetBestWave(int robotId) => PlayerPrefs.GetInt(WaveKeyPrefix + robotId, 0);

    /// <summary>이 머리로 끝까지 진행한 런 횟수(없으면 0).</summary>
    public static int GetPlayCount(int robotId) => PlayerPrefs.GetInt(PlayCountKeyPrefix + robotId, 0);

    public static bool HasRecord(int robotId) => GetPlayCount(robotId) > 0;

    /// <summary>
    /// 런 하나의 결과를 기록한다. 점수·웨이브는 <b>기존 기록보다 클 때만</b> 갱신하고,
    /// 플레이 횟수는 항상 1 올린다.
    /// </summary>
    public static void ReportRunFinished(int robotId, int score, int waveReached)
    {
        if (robotId <= 0) return;

        PlayerPrefs.SetInt(PlayCountKeyPrefix + robotId, GetPlayCount(robotId) + 1);

        if (score > GetBestScore(robotId)) PlayerPrefs.SetInt(ScoreKeyPrefix + robotId, Mathf.Max(0, score));
        if (waveReached > GetBestWave(robotId)) PlayerPrefs.SetInt(WaveKeyPrefix + robotId, Mathf.Max(0, waveReached));

        PlayerPrefs.Save();
    }
}
