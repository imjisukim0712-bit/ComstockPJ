using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Firebase Realtime Database REST API 기반 랭킹 구현(2026-08-20).
/// 공식 Firebase Unity SDK(EDM4U/Gradle 설정) 없이 UnityWebRequest로 REST 엔드포인트를
/// 직접 호출하므로 PC/Android/iOS 어디서든 동일 코드로 동작한다.
///
/// 데이터 형태: {databaseUrl}/leaderboard/{mapId}/{pushId}.json = {PlayerName, Score, DateIso}
/// - 맵마다 랭킹을 분리한다(2026-08-20, 앞으로 맵이 여러 개 추가될 예정이라 mapId로 노드를 나눴다).
/// (Firebase 콘솔 Rules에서 leaderboard/$mapId 하위 쓰기 시 이 세 필드 형식을 검증하도록 제한해둔 것을 전제로 한다)
/// </summary>
public class FirebaseLeaderboardService : ILeaderboardService
{
    private readonly string _baseUrl;

    public FirebaseLeaderboardService(string databaseUrl)
    {
        _baseUrl = databaseUrl.TrimEnd('/');
    }

    public void SubmitScore(string mapId, string playerName, int score, Action<bool> onComplete)
    {
        var entry = new ScoreEntry
        {
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? "익명" : playerName,
            Score = score,
            DateIso = DateTime.UtcNow.ToString("o")
        };

        byte[] body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entry));
        var request = new UnityWebRequest($"{_baseUrl}/leaderboard/{Escape(mapId)}.json", "POST")
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer()
        };
        request.SetRequestHeader("Content-Type", "application/json");

        request.SendWebRequest().completed += _ =>
        {
            bool success = request.result == UnityWebRequest.Result.Success;
            if (!success)
                Debug.LogWarning($"[Firebase] 점수 제출 실패: {request.error}");
            request.Dispose();
            onComplete?.Invoke(success);
        };
    }

    public void FetchTopScores(string mapId, int count, Action<List<ScoreEntry>> onComplete)
    {
        // Realtime Database는 orderBy 조회 시 오름차순 기준으로 반환하므로,
        // limitToLast로 상위 count개를 가져온 뒤 코드에서 내림차순으로 뒤집는다.
        string url = $"{_baseUrl}/leaderboard/{Escape(mapId)}.json?orderBy=%22Score%22&limitToLast={Mathf.Max(1, count)}";
        var request = UnityWebRequest.Get(url);

        request.SendWebRequest().completed += _ =>
        {
            var result = new List<ScoreEntry>();
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string text = request.downloadHandler.text;
                    if (!string.IsNullOrEmpty(text) && text != "null")
                    {
                        JObject obj = JObject.Parse(text);
                        foreach (JProperty prop in obj.Properties())
                            result.Add(prop.Value.ToObject<ScoreEntry>());

                        result.Sort((a, b) => b.Score.CompareTo(a.Score));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Firebase] 랭킹 파싱 실패: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[Firebase] 랭킹 조회 실패: {request.error}");
            }

            request.Dispose();
            onComplete?.Invoke(result);
        };
    }

    /// <summary>mapId를 URL 경로 조각으로 안전하게 쓰기 위한 이스케이프(씬 이름은 보통 순수
    /// 영문/숫자라 실질적으로 그대로 나오지만, 방어적으로 둔다).</summary>
    private static string Escape(string mapId) => Uri.EscapeDataString(mapId);
}
