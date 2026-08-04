using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터를 주기적으로 자동 스폰한다.
/// 스폰된 EnemyUnit은 자체적으로 플레이어를 향해 다가가고(monster_speed),
/// 닿으면 공격하며(monster_atsp), 플레이어의 투사체에 맞아 체력(monster_hp)이
/// 0 이하가 되면 사망한다 - 해당 로직은 EnemyUnit.cs 쪽에 있다.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Serializable]
    public struct MonsterPrefabEntry
    {
        public int monsterId;
        public GameObject prefab;
    }

    [Header("몬스터ID ↔ 프리팹")]
    [Tooltip("몬스터ID와 프리팹을 인스펙터에서 직접 드래그해서 연결")]
    [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();

    [Header("자동 스폰 설정")]
    [Tooltip("스폰할 몬스터ID 목록. 매 스폰마다 이 중 하나를 무작위로 골라 소환한다")]
    [SerializeField] private List<int> spawnMonsterIds = new List<int> { 200001 }; // 기본: 좀비

    [Tooltip("몇 초마다 한 마리씩 스폰할지")]
    [SerializeField] private float spawnInterval = 3f;

    [Tooltip("스폰 기준점. 비어 있으면 Player를 자동으로 찾고, 그것도 없으면 이 스포너 자신의 위치를 기준으로 삼는다")]
    [SerializeField] private Transform spawnCenter;

    [Tooltip("기준점으로부터 이 거리~이 거리 사이(원 둘레)에 무작위로 스폰된다")]
    [SerializeField] private float minSpawnRadius = 18f;
    [SerializeField] private float maxSpawnRadius = 26f;

    [Tooltip("동시에 살아있을 수 있는 최대 몬스터 수 (0이면 제한 없음)")]
    [SerializeField] private int maxAliveEnemies = 15;

    private Dictionary<int, GameObject> prefabMap;
    private readonly List<EnemyUnit> alive_enemies = new List<EnemyUnit>();

    private void Awake()
    {
        prefabMap = new Dictionary<int, GameObject>();
        foreach (var entry in monsterPrefabs)
            prefabMap[entry.monsterId] = entry.prefab;
    }

    private void Start()
    {
        // GameDataManager는 CSV를 비동기로 불러오므로, 데이터가 준비된 뒤에 스폰 루프를 시작한다.
        if (GameDataManager.Instance.IsLoaded) BeginSpawning();
        else GameDataManager.Instance.OnLoaded += BeginSpawning;
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded -= BeginSpawning;
    }

    private void BeginSpawning()
    {
        StartCoroutine(SpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, spawnInterval));

            if (GameOverManager.IsGameOver) continue; // 게임오버 후에는 더 이상 스폰하지 않음

            alive_enemies.RemoveAll(e => e == null); // 죽어서 파괴된 개체는 목록에서 정리
            if (maxAliveEnemies > 0 && alive_enemies.Count >= maxAliveEnemies) continue;

            if (spawnMonsterIds.Count == 0) continue;

            int monsterId = spawnMonsterIds[UnityEngine.Random.Range(0, spawnMonsterIds.Count)];
            Vector3 position = GetRandomSpawnPosition();

            EnemyUnit unit = SpawnMonster(monsterId, position);
            if (unit != null) alive_enemies.Add(unit);
        }
    }

    // 기준점을 중심으로 minSpawnRadius~maxSpawnRadius 사이의 원 둘레 위 무작위 지점을 고른다.
    private Vector3 GetRandomSpawnPosition()
    {
        Transform center = ResolveSpawnCenter();

        float radius = UnityEngine.Random.Range(minSpawnRadius, maxSpawnRadius);
        float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;

        Vector3 position = center.position + offset;
        position.z = 0f; // X-Y 평면만 사용
        return position;
    }

    private Transform ResolveSpawnCenter()
    {
        if (spawnCenter != null) return spawnCenter;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) return player.transform;

        return transform;
    }

    public EnemyUnit SpawnMonster(int monsterId, Vector3 position)
    {
        if (!prefabMap.TryGetValue(monsterId, out GameObject prefab))
        {
            Debug.LogWarning($"몬스터ID {monsterId}에 연결된 프리팹이 없습니다. 인스펙터에서 등록해주세요.");
            return null;
        }

        if (!GameDataManager.Instance.Monsters.TryGetValue(monsterId, out MonsterData data))
        {
            Debug.LogWarning($"몬스터ID {monsterId}의 데이터가 아직 로드되지 않았습니다.");
            return null;
        }

        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        EnemyUnit unit = obj.GetComponent<EnemyUnit>();
        if (unit == null) unit = obj.AddComponent<EnemyUnit>();
        unit.Init(data);
        return unit;
    }
}
