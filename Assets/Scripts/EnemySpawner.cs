using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Serializable]
    public struct MonsterPrefabEntry
    {
        public int monsterId;
        public GameObject prefab;
    }

    [Tooltip("몬스터ID와 프리팹을 인스펙터에서 직접 드래그해서 연결")]
    [SerializeField] private List<MonsterPrefabEntry> monsterPrefabs = new List<MonsterPrefabEntry>();

    private Dictionary<int, GameObject> prefabMap;

    private void Awake()
    {
        prefabMap = new Dictionary<int, GameObject>();
        foreach (var entry in monsterPrefabs)
            prefabMap[entry.monsterId] = entry.prefab;
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
