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
    [Tooltip("스폰할 몬스터ID 목록(항상 후보). 매 스폰마다 이 중 하나를 무작위로 골라 소환한다")]
    [SerializeField] private List<int> spawnMonsterIds = new List<int> { 200001 }; // 기본: 좀비

    [Serializable]
    public struct MonsterUnlockEntry
    {
        public int monsterId;
        [Tooltip("이 웨이브 번호에 도달하면 스폰 후보에 합류한다")]
        public int unlockWave;
    }

    [Header("스폰 비중 (2026-08-19 사용자 지정: 상위 몬스터를 줄이고 일반 좀비를 늘린다)")]
    [Tooltip("spawnMonsterIds(기본 좀비)의 추첨 비중. 예전에는 모든 몬스터가 똑같이 1이라 " +
             "상위 몬스터가 전부 해금되면 일반 좀비가 1/6(16.7%)까지 밀려났다")]
    [SerializeField] private float basicMonsterSpawnWeight = 2f;

    [Tooltip("unlockSchedule로 해금되는 상위 몬스터(차저·부머 등)의 추첨 비중. " +
             "1이 예전 동작이며 0.5면 절반으로 줄어든다")]
    [SerializeField] private float advancedMonsterSpawnWeight = 0.5f;

    [Header("상위 몬스터 해금 (좀비 기획서 Ver04 - 6종 단계적 등장)")]
    [Tooltip("웨이브별로 스폰 후보에 합류하는 몬스터ID들. spawnMonsterIds(기본 항상 스폰)에는 없는 상위 몬스터를 여기 등록한다")]
    [SerializeField]
    private List<MonsterUnlockEntry> unlockSchedule = new List<MonsterUnlockEntry>
    {
        new MonsterUnlockEntry { monsterId = 200003, unlockWave = 3 },  // 스피터
        new MonsterUnlockEntry { monsterId = 200002, unlockWave = 4 },  // 차저
        new MonsterUnlockEntry { monsterId = 200004, unlockWave = 5 },  // 스프린터
        new MonsterUnlockEntry { monsterId = 200005, unlockWave = 8 },  // 디스럭터
        new MonsterUnlockEntry { monsterId = 200006, unlockWave = 10 }, // 리더
    };

    [Header("웨이브별 체력·공격력 상승 (2026-08-13 사용자 지정)")]
    [Tooltip("웨이브가 하나 지날 때마다 몬스터 체력과 공격력에 곱해지는 증가율(0.025 = 2.5%). " +
             "웨이브 N의 값 = 데이터 값 x (1 + 이 값)^(N-1). 보스는 WaveManager가 직접 " +
             "스탯을 만들어 스폰하므로 이 배율을 받지 않는다(사용자 지정: 보스 제외). " +
             "2026-08-19 사용자 지정으로 5% -> 2.5%(기존의 50%)로 완화했다")]
    [SerializeField] private float statIncreasePerWave = 0.025f;

    private int currentWave = 1;
    private readonly List<int> spawn_pool_buffer = new List<int>();
    // spawn_pool_buffer와 <b>같은 인덱스</b>로 짝지어지는 추첨 비중(가중 추첨용).
    private readonly List<float> spawn_weight_buffer = new List<float>();

    /// <summary>WaveManager가 웨이브 시작 시 호출해 상위 몬스터 해금 여부를 갱신한다.</summary>
    public void SetCurrentWave(int wave) => currentWave = wave;

    /// <summary>현재 웨이브의 몬스터 체력·공격력 배율. HUD/검증용으로도 조회할 수 있게 공개한다.</summary>
    public float CurrentStatMultiplier => Mathf.Pow(1f + Mathf.Max(0f, statIncreasePerWave), Mathf.Max(0, currentWave - 1));

    [Tooltip("한 번에 몇 마리씩 스폰할지. 뱀서라이크 문법상 화면이 적으로 가득 차야 하므로 1보다 크게 둔다")]
    [SerializeField] private int spawnBatchSize = 3;

    [Tooltip("몇 초마다 한 묶음씩 스폰할지")]
    [SerializeField] private float spawnInterval = 0.8f;

    [Tooltip("스폰 기준점. 비어 있으면 Player를 자동으로 찾고, 그것도 없으면 이 스포너 자신의 위치를 기준으로 삼는다")]
    [SerializeField] private Transform spawnCenter;

    [Header("스폰 위치 (카메라 화면 바깥 테두리 기준)")]
    [Tooltip("화면(카메라 가시 영역) 테두리에서 이만큼 바깥에 스폰한다. 값이 크면 적이 화면에 들어오기까지 오래 걸린다")]
    [SerializeField] private float offScreenMargin = 1.5f;
    [Tooltip("offScreenMargin에 더해지는 무작위 여유 폭. 적들이 한 줄로 딱 맞춰 나타나지 않도록 흩뜨린다")]
    [SerializeField] private float offScreenMarginJitter = 2.5f;
    [Tooltip("카메라를 못 찾았을 때 쓰는 폴백 스폰 반경(원형)")]
    [SerializeField] private float fallbackSpawnRadius = 12f;

    [Tooltip("동시에 살아있을 수 있는 최대 몬스터 수 (0이면 제한 없음)")]
    [SerializeField] private int maxAliveEnemies = 30;

    [Header("최소 지속 스폰 (2026-08-19 사용자 지정)")]
    [Tooltip("동시 생존 상한(maxAliveEnemies)에 도달해 정규 스폰이 막혀도 이 간격(초)마다 " +
             "기본 좀비를 1마리씩 계속 스폰한다. 상한에 걸리면 적이 죽을 때까지 스폰이 완전히 " +
             "멈춰 '전투가 끊기는' 구간이 생기던 것을 막는다.\n" +
             "0 이하면 비활성(예전 동작). 이 스폰만 상한을 무시하지만, 웨이브 길이가 유한하므로 " +
             "무한정 쌓이지는 않는다(60초 웨이브 기준 최대 20마리)")]
    [SerializeField] private float trickleSpawnInterval = 3f;

    [Header("리더 무리 (좀비 기획서 Ver04 p.20/p.22)")]
    [Tooltip("리더와 함께 스폰할 팔로워 몬스터ID(기본: 일반 좀비). 어떤 몬스터ID가 '리더'인지는 " +
             "몬스터ID→행동 컴포넌트 매핑(MonsterComponentTypes)에서 LeaderUnit으로 등록된 것을 기준으로 자동 판별한다")]
    [SerializeField] private int leaderPackMonsterId = 200001;
    [Tooltip("리더 한 마리당 함께 스폰할 팔로워 수")]
    [SerializeField] private int leaderPackSize = 3;
    [Tooltip("팔로워가 리더 주위에 흩어져 스폰되는 반경(월드 유닛)")]
    [SerializeField] private float leaderPackSpawnRadius = 2.5f;

    private Dictionary<int, GameObject> prefabMap;
    private readonly List<EnemyUnit> alive_enemies = new List<EnemyUnit>();

    // WaveManager가 웨이브 사이(정비/상점)에는 스폰을 멈추고, 웨이브별 난이도(간격/최대 생존수)를
    // 조절할 수 있도록 노출하는 제어 인터페이스. 기본값은 항상 스폰 진행.
    public bool IsSpawningEnabled { get; private set; } = true;

    // 웨이브 1 기준값(인스펙터에 적힌 원본). <b>Awake에서 한 번만 찍어둔다</b> -
    // 2026-08-13 발견한 버그: 예전에는 이 프로퍼티들이 spawnInterval/maxAliveEnemies 필드를
    // 그대로 돌려줬는데, WaveManager가 매 웨이브 "기준값 x 웨이브 배율"을 계산해 같은 필드에
    // 덮어쓰기 때문에 다음 웨이브의 "기준값"이 이미 조정된 값이 되어 난이도가 <b>이중으로</b>
    // 누적됐다(최대 생존수 실측: 웨이브 10에 의도한 54마리가 아니라 198마리, 웨이브 20에는 778마리).
    // 초반 3~4웨이브부터 화면이 적으로 메워져 진행이 막힌 원인 중 하나다.
    public float BaseSpawnInterval { get; private set; }
    public int BaseMaxAliveEnemies { get; private set; }
    public int BaseSpawnBatchSize { get; private set; }

    public void SetSpawningEnabled(bool enabled) => IsSpawningEnabled = enabled;

    // 웨이브 번호에 따른 스폰 간격/최대 생존수/배치 크기를 직접 지정한다. 0 이하 값은 무시(변경 없음)한다.
    // 배치 크기는 2026-08-13 "웨이브가 지나며 스폰량을 조금씩 늘린다" 요청으로 추가됐다 -
    // 예전에는 간격만 줄여서 초반을 완만하게 만들면 후반 물량도 같이 얇아졌다.
    public void ConfigureDifficulty(float newSpawnInterval, int newMaxAliveEnemies, int newSpawnBatchSize = 0)
    {
        if (newSpawnInterval > 0f) spawnInterval = newSpawnInterval;
        if (newMaxAliveEnemies > 0) maxAliveEnemies = newMaxAliveEnemies;
        if (newSpawnBatchSize > 0) spawnBatchSize = newSpawnBatchSize;
    }

    // 웨이브 종료 시 화면에 남은 적을 한꺼번에 정리한다(기획서: 웨이브 종료 후 정비 진입).
    public void DespawnAllAliveEnemies()
    {
        foreach (EnemyUnit enemy in alive_enemies)
        {
            if (enemy != null) Destroy(enemy.gameObject);
        }
        alive_enemies.Clear();
    }

    private void Awake()
    {
        // 웨이브 배율 계산의 기준이 되는 원본 값을 먼저 확보한다(ConfigureDifficulty가 필드를 덮어쓰기 전에).
        BaseSpawnInterval = spawnInterval;
        BaseMaxAliveEnemies = maxAliveEnemies;
        BaseSpawnBatchSize = spawnBatchSize;

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
        // 정규 스폰 루프와 <b>별도 코루틴</b>으로 돌린다 - SpawnLoop 안에 넣으면 주기가
        // spawnInterval(웨이브마다 WaveManager가 바꾼다)에 끌려가서 "3초에 1마리"가 지켜지지 않는다.
        StartCoroutine(TrickleSpawnLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, spawnInterval));

            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) continue; // 게임오버/승리 후에는 더 이상 스폰하지 않음
            if (!IsSpawningEnabled) continue; // 웨이브 사이(정비/상점)에는 스폰하지 않음

            alive_enemies.RemoveAll(e => e == null); // 죽어서 파괴된 개체는 목록에서 정리

            // 2026-08-19: 예전에는 후보를 그냥 나열하고 균등 추첨했다. 그러면 상위 몬스터가 전부
            // 해금된 뒤 일반 좀비가 1/6(16.7%)까지 밀려나, 화면이 차저·부머로 가득 찼다.
            // 이제 후보마다 비중을 함께 담아 가중 추첨한다(기본 좀비 2 / 상위 0.5 = 4배 차이).
            spawn_pool_buffer.Clear();
            spawn_weight_buffer.Clear();

            foreach (int basicId in spawnMonsterIds)
            {
                spawn_pool_buffer.Add(basicId);
                spawn_weight_buffer.Add(Mathf.Max(0f, basicMonsterSpawnWeight));
            }
            foreach (MonsterUnlockEntry entry in unlockSchedule)
            {
                if (currentWave < entry.unlockWave) continue;
                spawn_pool_buffer.Add(entry.monsterId);
                spawn_weight_buffer.Add(Mathf.Max(0f, advancedMonsterSpawnWeight));
            }
            if (spawn_pool_buffer.Count == 0) continue;

            for (int i = 0; i < Mathf.Max(1, spawnBatchSize); i++)
            {
                if (maxAliveEnemies > 0 && alive_enemies.Count >= maxAliveEnemies) break;

                int monsterId = PickWeightedMonsterId();
                Vector3 position = GetRandomSpawnPosition();

                EnemyUnit unit = SpawnMonster(monsterId, position);
                if (unit == null) continue;

                alive_enemies.Add(unit);
                if (unit is LeaderUnit leader) SpawnLeaderPack(leader, position);
            }
        }
    }

    /// <summary>
    /// 동시 생존 상한에 막혀 정규 스폰이 멈춘 동안에도 <b>trickleSpawnInterval초마다 1마리</b>는
    /// 계속 내보내 전투가 끊기지 않게 한다(2026-08-19 사용자 지정).
    ///
    /// <b>상한을 무시하는 유일한 정규 경로</b>다(리더의 팔로워 스폰과 같은 예외). 그래서 여기서는
    /// 가중 추첨을 쓰지 않고 <see cref="spawnMonsterIds"/>(기본 좀비)에서만 고른다 - 상위 몬스터가
    /// 뽑히면 차저·부머가 상한 위로 쌓이고, 특히 리더가 뽑히면 팔로워 3마리까지 함께 상한을
    /// 우회해 한 번에 4마리가 추가되기 때문이다.
    /// </summary>
    private IEnumerator TrickleSpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, trickleSpawnInterval > 0f ? trickleSpawnInterval : 3f));

            if (trickleSpawnInterval <= 0f) continue;                                   // 비활성(예전 동작)
            if (GameOverManager.IsGameOver || GameWinManager.IsGameWon) continue;
            if (!IsSpawningEnabled) continue;                                            // 정비/상점 중에는 멈춘다
            if (spawnMonsterIds.Count == 0) continue;

            alive_enemies.RemoveAll(e => e == null);

            // 상한에 걸리지 않았다면 정규 스폰이 이미 돌고 있으므로 굳이 더 뿌리지 않는다.
            if (maxAliveEnemies > 0 && alive_enemies.Count < maxAliveEnemies) continue;

            int monsterId = spawnMonsterIds[UnityEngine.Random.Range(0, spawnMonsterIds.Count)];
            EnemyUnit unit = SpawnMonster(monsterId, GetRandomSpawnPosition());
            if (unit != null) alive_enemies.Add(unit);
        }
    }

    /// <summary>
    /// spawn_pool_buffer / spawn_weight_buffer(같은 인덱스로 짝지어진 후보와 비중)에서
    /// 가중 추첨으로 몬스터ID 하나를 고른다. 모든 비중이 0이면 균등 추첨으로 폴백한다
    /// (인스펙터에서 실수로 전부 0을 넣어도 스폰이 멈추지 않도록).
    /// </summary>
    private int PickWeightedMonsterId()
    {
        float total = 0f;
        for (int i = 0; i < spawn_weight_buffer.Count; i++) total += spawn_weight_buffer[i];

        if (total <= 0f)
        {
            return spawn_pool_buffer[UnityEngine.Random.Range(0, spawn_pool_buffer.Count)];
        }

        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < spawn_pool_buffer.Count; i++)
        {
            roll -= spawn_weight_buffer[i];
            if (roll <= 0f) return spawn_pool_buffer[i];
        }

        // 부동소수점 오차로 끝까지 떨어지지 않은 경우의 안전망
        return spawn_pool_buffer[spawn_pool_buffer.Count - 1];
    }

    /// <summary>
    /// 리더가 스폰되면 곧바로 무리(팔로워)를 함께 스폰해 등록한다(좀비 기획서 Ver04 p.20 "다른
    /// 좀비 무리를 이끌고 다니는 우두머리" / p.22 "무리가 전멸하면 리더는 도주").
    /// 팔로워는 maxAliveEnemies 제한을 우회한다 - 리더 등장 자체가 하나의 "이벤트"라
    /// 무리가 쪼개져서 나오면 리더 혼자 덩그러니 나오는 것보다 부자연스럽기 때문이다.
    /// </summary>
    private void SpawnLeaderPack(LeaderUnit leader, Vector3 center)
    {
        var followers = new List<EnemyUnit>();

        for (int i = 0; i < leaderPackSize; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * leaderPackSpawnRadius;
            Vector3 position = center + new Vector3(offset.x, offset.y, 0f);

            EnemyUnit follower = SpawnMonster(leaderPackMonsterId, position);
            if (follower == null) continue;

            followers.Add(follower);
            alive_enemies.Add(follower);
        }

        leader.SetPack(followers);
    }

    /// <summary>
    /// 카메라 가시 영역(사각형)의 바깥 테두리 위 무작위 지점을 고른다.
    ///
    /// 예전에는 기준점 중심의 "원" 둘레에 스폰했는데, 화면은 원이 아니라 16:9 사각형이라
    /// 같은 반경이라도 세로 방향은 화면에서 한참 멀고 가로 방향은 화면 안쪽이 되는 문제가 있었다.
    /// (실측: 원근 카메라 FOV 60 / z=-15 기준 가시 범위가 세로 ±8.7, 가로 ±15.4유닛인데
    ///  스폰 반경은 18~26유닛이라 적이 화면에 들어오기 훨씬 전에 사거리 안에 걸려 죽었다.)
    /// 이제는 어느 방향에서 오든 "화면 밖에서 걸어 들어오는" 체감이 일정하다.
    /// </summary>
    private Vector3 GetRandomSpawnPosition()
    {
        Vector3 origin = ResolveSpawnOrigin();

        if (!TryGetCameraHalfExtents(out float halfWidth, out float halfHeight))
        {
            // 카메라를 못 찾은 경우에만 예전 방식(원형)으로 폴백
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 fallback = origin + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * fallbackSpawnRadius;
            fallback.z = 0f;
            return fallback;
        }

        float margin = offScreenMargin + UnityEngine.Random.Range(0f, Mathf.Max(0f, offScreenMarginJitter));
        float outerWidth = halfWidth + margin;
        float outerHeight = halfHeight + margin;

        // 네 변 중 하나를 고르되, 변의 길이에 비례한 확률로 골라야 테두리 전체에 고르게 퍼진다
        // (그냥 4등분하면 짧은 좌우 변에 적이 몰린다).
        float horizontalWeight = outerWidth;   // 위/아래 변의 길이 비중
        float verticalWeight = outerHeight;    // 좌/우 변의 길이 비중
        bool onHorizontalEdge = UnityEngine.Random.value < horizontalWeight / (horizontalWeight + verticalWeight);

        Vector3 offset;
        if (onHorizontalEdge)
        {
            float x = UnityEngine.Random.Range(-outerWidth, outerWidth);
            float y = UnityEngine.Random.value < 0.5f ? -outerHeight : outerHeight;
            offset = new Vector3(x, y, 0f);
        }
        else
        {
            float x = UnityEngine.Random.value < 0.5f ? -outerWidth : outerWidth;
            float y = UnityEngine.Random.Range(-outerHeight, outerHeight);
            offset = new Vector3(x, y, 0f);
        }

        Vector3 position = origin + offset;
        position.z = 0f; // X-Y 평면만 사용
        return position;
    }

    /// <summary>
    /// 카메라가 z=0 평면에서 실제로 보여주는 범위의 절반 크기(가로/세로)를 구한다.
    /// 원근 카메라면 카메라~평면 거리와 FOV로, 직교 카메라면 orthographicSize로 계산한다.
    /// </summary>
    private static bool TryGetCameraHalfExtents(out float halfWidth, out float halfHeight)
    {
        halfWidth = 0f;
        halfHeight = 0f;

        Camera cam = Camera.main;
        if (cam == null) return false;

        if (cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
        }
        else
        {
            float distanceToPlane = Mathf.Abs(cam.transform.position.z); // 게임 평면은 z=0
            if (distanceToPlane <= 0.01f) return false;
            halfHeight = distanceToPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        halfWidth = halfHeight * cam.aspect;
        return halfHeight > 0.01f;
    }

    /// <summary>
    /// 화면 밖 테두리를 계산할 기준점.
    ///
    /// <b>플레이어가 아니라 카메라 위치를 기준으로 삼는다.</b> 이 계산의 목적 자체가
    /// "지금 화면에 보이는 사각형의 바깥"이기 때문이다. 예전에는 플레이어를 기준으로 삼았고
    /// 카메라가 항상 플레이어를 정확히 따라다녔으므로 둘이 같았지만, 2026-08-10에 카메라를
    /// 맵 안으로 제한하면서(CameraFollow.clampToMap) 플레이어가 맵 가장자리로 가면 화면 중심과
    /// 플레이어가 최대 15유닛까지 어긋나게 됐다. 그대로 두면 플레이어 반대편 스폰 지점이
    /// 화면 안쪽에 들어와 적이 화면 한복판에서 튀어나온다.
    ///
    /// 인스펙터에서 spawnCenter를 명시적으로 지정했다면 그 의도를 존중해 그대로 쓴다.
    /// </summary>
    private Vector3 ResolveSpawnOrigin()
    {
        if (spawnCenter != null) return spawnCenter.position;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 camPos = cam.transform.position;
            camPos.z = 0f; // 게임 평면
            return camPos;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) return player.transform.position;

        return transform.position;
    }

    /// <summary>
    /// 몬스터ID별로 붙일 행동 컴포넌트 타입(2026-08-10 좀비 기획서 Ver04 - 6종 공격 방식).
    /// 여기 없는 ID(예: 200001 좀비)는 기본 EnemyUnit(근접 접촉)을 그대로 쓴다.
    /// 프리팹 자체에는 행동 컴포넌트를 바로 넣지 않는다 - Zombie/Charger가 원래부터 그랬듯
    /// Transform+SpriteRenderer+Collider+Rigidbody만 있는 셸이고, 스폰 시점에 이 표를 보고
    /// AddComponent한다(씬을 재시작해도 컴포넌트 참조가 꼬이지 않고, 프리팹을 몬스터ID와
    /// 완전히 분리해 재사용할 수 있다).
    /// </summary>
    private static readonly Dictionary<int, Type> MonsterComponentTypes = new Dictionary<int, Type>
    {
        { 200002, typeof(ChargerUnit) },
        { 200003, typeof(SpitterUnit) },
        { 200004, typeof(SprinterUnit) },
        { 200005, typeof(DisruptorUnit) },
        { 200006, typeof(LeaderUnit) },
    };

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

        // 웨이브가 지날수록 체력과 공격력을 조금씩 올린다(2026-08-13 사용자 지정 5%/웨이브).
        // MonsterData는 struct라 여기서 값을 바꿔도 GameDataManager의 원본 데이터는 그대로다
        // (class였다면 딕셔너리 안의 원본이 영구히 오염돼 웨이브마다 값이 누적 상승한다).
        float statMultiplier = CurrentStatMultiplier;
        data.monster_hp = Mathf.Max(1, Mathf.RoundToInt(data.monster_hp * statMultiplier));
        data.monster_atk = Mathf.Max(1, Mathf.RoundToInt(data.monster_atk * statMultiplier));

        GameObject obj = Instantiate(prefab, position, Quaternion.identity);

        Type componentType = MonsterComponentTypes.TryGetValue(monsterId, out Type mapped) ? mapped : typeof(EnemyUnit);
        EnemyUnit unit = obj.GetComponent(componentType) as EnemyUnit;
        if (unit == null) unit = (EnemyUnit)obj.AddComponent(componentType);

        unit.Init(data);
        return unit;
    }
}
