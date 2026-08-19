using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 해금 진행도와 해금 여부를 <b>판을 넘어 영구 보관</b>하는 저장 계층(2026-08-19 Phase E).
/// 볼륨 설정(`MusicManager`/`SFXManager`)·로컬 랭킹(`LeaderboardService`)과 같은 PlayerPrefs 패턴이다.
///
/// <b>진행도와 해금 여부를 둘 다 저장하는 이유</b> — 진행도만 저장하면 나중에 밸런스 조정으로
/// 목표치를 올렸을 때 이미 해금한 항목이 도로 잠긴다. 반대로 해금 여부만 저장하면 도감의
/// "38 / 50" 같은 진행 표시를 만들 수 없다.
///
/// <b>저장 시점</b> — 값이 바뀔 때마다 디스크에 쓰지 않는다(처치 1마리마다 직렬화하는 낭비를
/// 피한다). 더티 표시만 해 두고 <see cref="Flush"/>에서 실제로 쓰며, 해금이 발생한 순간과
/// 웨이브 종료·게임 오버·게임 종료 시점에 호출한다.
/// </summary>
public static class UnlockState
{
    private const string SaveKey = "unlock_state_v1";

    /// <summary>새로 해금됐을 때. 해금 알림 UI가 붙을 수 있도록 열어 둔다.</summary>
    public static event Action<UnlockEntry> OnUnlocked;

    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    private static readonly Dictionary<string, HashSet<int>> distinctSets = new Dictionary<string, HashSet<int>>();
    private static readonly HashSet<int> unlockedItemIds = new HashSet<int>();

    private static bool loaded;
    private static bool dirty;

    // ── 조회 ──────────────────────────────────────────────────────────────────────

    public static bool IsUnlocked(int itemId)
    {
        EnsureLoaded();

        if (!UnlockCatalog.TryGet(itemId, out UnlockEntry entry)) return true; // 조건표에 없는 것은 잠그지 않는다
        return entry.UnlockedFromStart || unlockedItemIds.Contains(itemId);
    }

    /// <summary>이 항목의 현재 진행도(목표치와 같은 단위). 초기 해금 항목은 항상 0을 돌려준다.</summary>
    public static int GetProgress(UnlockEntry entry)
    {
        EnsureLoaded();
        if (entry.UnlockedFromStart) return 0;
        return GetValue(entry.counterKey);
    }

    public static int GetValue(string key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key)) return 0;

        if (UnlockProgressKey.DistinctKeys.Contains(key))
            return distinctSets.TryGetValue(key, out HashSet<int> set) ? set.Count : 0;

        return counters.TryGetValue(key, out int value) ? value : 0;
    }

    /// <summary>카테고리별 해금 개수(도감의 `3 / 12` 표시용).</summary>
    public static int CountUnlocked(UnlockCategory category)
    {
        EnsureLoaded();

        int count = 0;
        foreach (UnlockEntry entry in UnlockCatalog.All)
        {
            if (entry.category != category) continue;
            if (IsUnlocked(entry.itemId)) count++;
        }

        return count;
    }

    // ── 진행도 쌓기 ───────────────────────────────────────────────────────────────

    /// <summary>누적형 진행도를 더한다(처치 수·사용 횟수 등). "한 번이라도 있었나"는 목표치 1로 쓴다.</summary>
    public static void AddProgress(string key, int amount = 1)
    {
        if (string.IsNullOrEmpty(key) || amount <= 0) return;
        EnsureLoaded();

        counters.TryGetValue(key, out int current);
        counters[key] = current + amount;
        dirty = true;

        EvaluateKey(key);
    }

    /// <summary>최고 기록형 진행도(방어력 10 달성 등). 지금까지 도달한 최댓값만 남는다.</summary>
    public static void ReportMax(string key, int value)
    {
        if (string.IsNullOrEmpty(key)) return;
        EnsureLoaded();

        counters.TryGetValue(key, out int current);
        if (value <= current) return;

        counters[key] = value;
        dirty = true;

        EvaluateKey(key);
    }

    /// <summary>"서로 다른 것"을 세는 진행도(다리 종류·무기 종류). 같은 ID를 여러 번 넣어도 1개다.</summary>
    public static void AddDistinct(string key, int id)
    {
        if (string.IsNullOrEmpty(key)) return;
        EnsureLoaded();

        if (!distinctSets.TryGetValue(key, out HashSet<int> set))
        {
            set = new HashSet<int>();
            distinctSets[key] = set;
        }

        if (!set.Add(id)) return;
        dirty = true;

        EvaluateKey(key);
    }

    private static void EvaluateKey(string key)
    {
        int value = GetValue(key);

        foreach (UnlockEntry entry in UnlockCatalog.All)
        {
            if (entry.counterKey != key) continue;
            if (entry.UnlockedFromStart) continue;
            if (unlockedItemIds.Contains(entry.itemId)) continue;
            if (value < entry.requiredAmount) continue;

            unlockedItemIds.Add(entry.itemId);
            dirty = true;

            Debug.Log($"해금: {entry.fallbackName} ({entry.conditionText})");
            OnUnlocked?.Invoke(entry);

            // 해금은 드물게 일어나므로 이 순간만큼은 즉시 디스크에 쓴다(게임이 갑자기 꺼져도 남도록).
            Flush();
        }
    }

    // ── 저장/불러오기 ─────────────────────────────────────────────────────────────

    [Serializable]
    private class SaveData
    {
        public List<string> counterKeys = new List<string>();
        public List<int> counterValues = new List<int>();
        public List<string> setKeys = new List<string>();
        public List<string> setValues = new List<string>();   // "1,2,3" 형태
        public List<int> unlocked = new List<int>();
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true; // 아래에서 다시 들어오지 않도록 먼저 세운다

        string json = PlayerPrefs.GetString(SaveKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;

        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"해금 저장 데이터를 읽지 못해 초기화합니다: {e.Message}");
            return;
        }

        if (data == null) return;

        for (int i = 0; i < data.counterKeys.Count && i < data.counterValues.Count; i++)
            counters[data.counterKeys[i]] = data.counterValues[i];

        for (int i = 0; i < data.setKeys.Count && i < data.setValues.Count; i++)
        {
            var set = new HashSet<int>();
            foreach (string piece in data.setValues[i].Split(','))
            {
                if (int.TryParse(piece, out int id)) set.Add(id);
            }

            distinctSets[data.setKeys[i]] = set;
        }

        foreach (int id in data.unlocked) unlockedItemIds.Add(id);
    }

    /// <summary>메모리에 쌓인 진행도를 실제로 저장한다. 웨이브 종료·게임 오버·종료 시점에 부른다.</summary>
    public static void Flush()
    {
        if (!loaded || !dirty) return;

        var data = new SaveData();
        foreach (KeyValuePair<string, int> pair in counters)
        {
            data.counterKeys.Add(pair.Key);
            data.counterValues.Add(pair.Value);
        }

        foreach (KeyValuePair<string, HashSet<int>> pair in distinctSets)
        {
            data.setKeys.Add(pair.Key);
            data.setValues.Add(string.Join(",", pair.Value));
        }

        foreach (int id in unlockedItemIds) data.unlocked.Add(id);

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        dirty = false;
    }

    /// <summary>게임을 끄기 직전에도 저장되도록 걸어 둔다(웨이브 도중 종료 대비).</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HookApplicationQuit()
    {
        Application.quitting -= Flush;
        Application.quitting += Flush;
    }

    // ── 디버그/검증용 ─────────────────────────────────────────────────────────────

    /// <summary>모든 진행도와 해금을 지운다(검증용. 게임 안에서 부르는 곳은 없다).</summary>
    public static void ResetAll()
    {
        EnsureLoaded();
        counters.Clear();
        distinctSets.Clear();
        unlockedItemIds.Clear();
        dirty = true;
        Flush();
    }

    /// <summary>조건과 무관하게 전부 해금한다(검증용).</summary>
    public static void UnlockAll()
    {
        EnsureLoaded();
        foreach (UnlockEntry entry in UnlockCatalog.All) unlockedItemIds.Add(entry.itemId);
        dirty = true;
        Flush();
    }
}
