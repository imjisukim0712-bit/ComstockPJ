using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 랭킹(엔드리스 모드 점수) 서비스 추상화(2026-08-19 Phase C).
///
/// 사용자 지시: "랭킹은 나중에 파이어베이스 연결할 거니까 그거 고려해서, 연결하는 것만 제외하고
/// 만들어놔." 즉 UI·제출 호출부·데이터 모델은 지금 전부 완성하되, 실제 네트워크 연동(Firebase)만
/// 비워둔다. 그래서 <see cref="ILeaderboardService"/> 인터페이스로 호출부를 고정하고,
/// 지금은 <see cref="LocalLeaderboardService"/>(PlayerPrefs 기반)를 기본 구현으로 꽂아둔다.
///
/// <b>나중에 Firebase를 붙일 때</b>: 새 클래스(예: FirebaseLeaderboardService)가 이 인터페이스를
/// 구현하게 만들고, 아래 <see cref="Current"/>의 기본값 한 줄만 바꾸면 된다. 호출부
/// (ScoreSummaryPopup/GameOverSummaryUI)는 인터페이스만 알고 있어 수정이 필요 없다.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>점수를 제출한다. onComplete(성공 여부)는 반드시 호출된다(동기 구현이라도 콜백
    /// 방식을 유지해야 Firebase의 비동기 응답과 호출부 코드가 그대로 호환된다).</summary>
    void SubmitScore(string playerName, int score, Action<bool> onComplete);

    /// <summary>점수 내림차순 상위 count개를 가져온다.</summary>
    void FetchTopScores(int count, Action<List<ScoreEntry>> onComplete);
}

[Serializable]
public struct ScoreEntry
{
    public string PlayerName;
    public int Score;
    public string DateIso; // System.DateTime.UtcNow.ToString("o") - 문자열로 저장해야 JsonUtility가 직렬화한다
}

/// <summary>
/// PlayerPrefs에 JSON으로 저장하는 로컬 랭킹 구현. Firebase 연결 전까지의 기본값이자,
/// 오프라인/미연동 상태의 폴백으로도 계속 쓸 수 있다.
///
/// JsonUtility는 최상위 배열/리스트를 직접 (역)직렬화하지 못하므로 <see cref="Wrapper"/>로 감싼다.
/// </summary>
public class LocalLeaderboardService : ILeaderboardService
{
    private const string PrefsKey = "Comstock.Leaderboard.Local.v1";
    private const int MaxStoredEntries = 50; // 기기에 무한정 쌓이지 않도록 상위 N개만 보관

    [Serializable]
    private class Wrapper
    {
        public List<ScoreEntry> entries = new List<ScoreEntry>();
    }

    public void SubmitScore(string playerName, int score, Action<bool> onComplete)
    {
        Wrapper wrapper = Load();

        wrapper.entries.Add(new ScoreEntry
        {
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "익명" : playerName,
            Score = score,
            DateIso = DateTime.UtcNow.ToString("o")
        });

        wrapper.entries.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (wrapper.entries.Count > MaxStoredEntries)
            wrapper.entries.RemoveRange(MaxStoredEntries, wrapper.entries.Count - MaxStoredEntries);

        Save(wrapper);
        onComplete?.Invoke(true);
    }

    public void FetchTopScores(int count, Action<List<ScoreEntry>> onComplete)
    {
        Wrapper wrapper = Load();
        int take = Mathf.Clamp(count, 0, wrapper.entries.Count);
        onComplete?.Invoke(wrapper.entries.GetRange(0, take));
    }

    private static Wrapper Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return new Wrapper();

        Wrapper wrapper = JsonUtility.FromJson<Wrapper>(json);
        return wrapper ?? new Wrapper();
    }

    private static void Save(Wrapper wrapper)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(wrapper));
        PlayerPrefs.Save();
    }
}

/// <summary>호출부가 쓰는 진입점. 기본값은 로컬 구현 - Firebase 연동 시 이 한 줄만 바꾼다.</summary>
public static class LeaderboardService
{
    public static ILeaderboardService Current { get; set; } = new LocalLeaderboardService();
}
