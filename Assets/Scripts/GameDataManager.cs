using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 데이터 테이블(몬스터/로봇/무기/방어구/드랍)을 제공하는 싱글톤.
/// 이전에는 Google Sheets CSV를 런타임에 다운로드했으나, 네트워크 의존을 없애기 위해
/// localData(GameDataAsset)에 미리 저장된 값을 Awake에서 동기적으로 읽어온다.
/// IsLoaded/OnLoaded는 기존 호출부(EnemySpawner, PlayerRobotController 등)를 그대로
/// 두기 위해 API 형태만 유지한다 - 로컬 로드는 항상 즉시 끝나므로 사실상 동기 동작이다.
/// 시트 값을 갱신할 때는 GameDataCsvImporter(Editor 전용) 도구로 localData를 다시 채운다.
/// </summary>
public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    [Header("로컬 데이터 에셋")]
    [Tooltip("GameDataCsvImporter로 채워진 GameDataAsset. Assets/Data 아래에 생성해서 연결한다")]
    [SerializeField] private GameDataAsset localData;

    public Dictionary<int, MonsterData> Monsters { get; private set; } = new Dictionary<int, MonsterData>();
    public Dictionary<int, RobotData> Robots { get; private set; } = new Dictionary<int, RobotData>();
    public Dictionary<int, WeaponData> Weapons { get; private set; } = new Dictionary<int, WeaponData>();
    public Dictionary<int, AmorData> Amors { get; private set; } = new Dictionary<int, AmorData>();
    public Dictionary<int, List<DropEntry>> DropsByMonster { get; private set; } = new Dictionary<int, List<DropEntry>>();

    public bool IsLoaded { get; private set; } = false;
    public event Action OnLoaded;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 창이 포커스를 잃으면(다른 창 클릭, MCP 자동 플레이테스트 등) 업데이트 루프가
        // 거의 멈추는 현상이 있어(웨이브 타이머 등 실시간 로직에 영향) 항상 정상 진행되게 한다.
        Application.runInBackground = true;

        LoadFromLocalData();
    }

    private void LoadFromLocalData()
    {
        if (localData == null)
        {
            Debug.LogError("GameDataManager.localData가 비어 있습니다. GameDataAsset을 만들고 인스펙터에서 연결하세요.");
            return;
        }

        Monsters.Clear();
        foreach (var d in localData.monsters) Monsters[d.monster_id] = d;

        Robots.Clear();
        foreach (var d in localData.robots) Robots[d.robot_id] = d;

        Weapons.Clear();
        foreach (var d in localData.weapons) Weapons[d.weapon_id] = d;

        Amors.Clear();
        foreach (var d in localData.amors) Amors[d.amor_id] = d;

        DropsByMonster.Clear();
        foreach (var d in localData.drops)
        {
            if (!DropsByMonster.ContainsKey(d.monster_id)) DropsByMonster[d.monster_id] = new List<DropEntry>();
            DropsByMonster[d.monster_id].Add(d);
        }

        IsLoaded = true;
        OnLoaded?.Invoke();
    }
}
