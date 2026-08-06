using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 웨이브 진행을 관리한다: 웨이브는 시간이 다 되면 끝난다(적을 모두 잡아야 끝나는 방식이 아님 -
/// 뱀서라이크 장르 문법). 웨이브가 끝나면 남은 적을 정리하고 스폰을 멈춘 뒤 OnWaveEnded를
/// 발행한다. GameFlowManager가 이 이벤트를 받아 정비/상점 화면으로 전환하고,
/// 준비되면 StartNextWave()를 호출해 다음 웨이브를 연다.
///
/// 데모 목표(10웨이브 + 보스)에 맞춰 웨이브가 오를수록 제한시간이 늘고 스폰 압력(간격 감소)이
/// 세진다. 마지막 웨이브(finalWaveNumber)에는 보스가 추가로 등장하며, "보스를 처치하고 +
/// 웨이브 제한시간도 다 지나야" 승리로 처리한다(둘 중 하나만 먼저 끝나도 승리하지 않음 -
/// 사용자 확정 사항). 시간이 먼저 끝나도 보스가 살아있으면 계속 기다리고, 보스를 먼저 잡아도
/// 시간이 남았으면 그만큼 더 기다린다.
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Header("스폰 연결")]
    [SerializeField] private EnemySpawner enemySpawner;

    [Header("웨이브 제한시간 (초)")]
    [SerializeField] private float firstWaveDuration = 30f;
    [SerializeField] private float waveDurationIncreasePerWave = 3f;

    [Header("웨이브별 스폰 압력")]
    [Tooltip("웨이브가 오를 때마다 스폰 간격에 곱해지는 감소율 (예: 0.92 = 매 웨이브 8%씩 빨라짐)")]
    [SerializeField] private float spawnIntervalDecayPerWave = 0.92f;
    [SerializeField] private float minSpawnInterval = 0.4f;
    [Tooltip("웨이브가 오를 때마다 최대 동시 생존 몬스터 수에 더해지는 값")]
    [SerializeField] private int maxAliveIncreasePerWave = 2;

    [Header("보스 웨이브 (전부 밸런스 미확정 임시값)")]
    [Tooltip("이 웨이브 번호에 도달하면 보스를 스폰한다. 데모 목표: 10")]
    [SerializeField] private int finalWaveNumber = 10;

    [Tooltip("보스 프리팹(Assets/Prefebs/Boss.prefab). 전용 아트가 없어 좀비 프리팹을 확대·색조 변경해 임시로 사용 중")]
    [SerializeField] private GameObject bossPrefab;

    [Tooltip("보스 등장 시 플레이어로부터 이 거리만큼 떨어진 지점에 스폰")]
    [SerializeField] private float bossSpawnDistance = 16f;

    [SerializeField] private int bossMaxHp = 4500;
    [SerializeField] private int bossAtk = 25;
    [SerializeField] private int bossDef = 5;
    [SerializeField] private float bossMoveSpeed = 1.3f;
    [SerializeField] private float bossAttackRange = 1.5f;
    [SerializeField] private float bossAttackSpeed = 0.8f;

    public event Action<int> OnWaveStarted;
    public event Action<int> OnWaveEnded;

    private Coroutine wave_timer_routine;
    private bool boss_spawned_this_wave;
    private bool boss_defeated_this_wave;

    private void Start()
    {
        if (enemySpawner == null)
        {
            Debug.LogWarning("WaveManager.enemySpawner가 비어 있습니다. 인스펙터에서 EnemySpawner를 연결하세요.");
        }

        if (GameDataManager.Instance != null && GameDataManager.Instance.IsLoaded) StartFirstWave();
        else if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded += StartFirstWave;
    }

    private void OnDestroy()
    {
        if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded -= StartFirstWave;
    }

    private void StartFirstWave()
    {
        if (GameDataManager.Instance != null) GameDataManager.Instance.OnLoaded -= StartFirstWave;
        RunState.WaveNumber = 1;
        BeginWave();
    }

    /// <summary>정비/상점을 마치고 다음 웨이브로 넘어갈 때 GameFlowManager가 호출한다.</summary>
    public void StartNextWave()
    {
        RunState.WaveNumber++;
        BeginWave();
    }

    private void BeginWave()
    {
        int wave = RunState.WaveNumber;

        if (enemySpawner != null)
        {
            float spawnInterval = Mathf.Max(minSpawnInterval, enemySpawner.BaseSpawnInterval * Mathf.Pow(spawnIntervalDecayPerWave, wave - 1));
            int maxAlive = enemySpawner.BaseMaxAliveEnemies + maxAliveIncreasePerWave * (wave - 1);
            enemySpawner.ConfigureDifficulty(spawnInterval, maxAlive);
            enemySpawner.SetSpawningEnabled(true);
        }

        boss_spawned_this_wave = false;
        boss_defeated_this_wave = false;
        if (wave == finalWaveNumber) SpawnBoss();

        float duration = firstWaveDuration + waveDurationIncreasePerWave * (wave - 1);

        if (wave_timer_routine != null) StopCoroutine(wave_timer_routine);
        wave_timer_routine = StartCoroutine(WaveTimer(duration));

        Debug.Log($"웨이브 {wave} 시작 (제한시간 {duration:F0}초)" + (wave == finalWaveNumber ? " - 보스 웨이브" : ""));
        OnWaveStarted?.Invoke(wave);
    }

    private void SpawnBoss()
    {
        if (bossPrefab == null)
        {
            Debug.LogWarning("WaveManager.bossPrefab이 비어 있어 보스를 스폰하지 못했습니다.");
            return;
        }

        Vector3 position = ComputeBossSpawnPosition();

        GameObject obj = Instantiate(bossPrefab, position, Quaternion.identity);
        BossUnit boss = obj.GetComponent<BossUnit>();
        if (boss == null) boss = obj.AddComponent<BossUnit>();

        // 보스 스탯은 GameDataAsset(시트 연동) 밖에 둔다 - 몬스터 시트를 다시 가져오기(재임포트)
        // 할 때 이 값이 조용히 지워지는 것을 막기 위함(에디터 임포터가 매번 목록을 통째로 비우고
        // 다시 채우는 구조라, 시트에 없는 보스 행을 여기 넣어두면 재임포트 시 사라진다).
        var bossData = new MonsterData
        {
            monster_id = -1,
            monster_name = "보스",
            monster_hp = bossMaxHp,
            monster_atk = bossAtk,
            monster_def = bossDef,
            monster_speed = bossMoveSpeed,
            monster_range = bossAttackRange,
            monster_type = 1,
            monster_atsp = bossAttackSpeed
        };
        boss.Init(bossData);
        boss.OnDefeated += HandleBossDefeated;

        boss_spawned_this_wave = true;
        Debug.Log($"보스 등장 (HP {bossMaxHp})");
    }

    private Vector3 ComputeBossSpawnPosition()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector3 center = player != null ? player.transform.position : transform.position;

        Vector3 position = center + new Vector3(0f, bossSpawnDistance, 0f);
        position.z = 0f;
        return position;
    }

    private void HandleBossDefeated()
    {
        boss_defeated_this_wave = true;
        Debug.Log("보스 처치됨 - 웨이브 제한시간이 끝나면 승리 처리됩니다");
    }

    private IEnumerator WaveTimer(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!GameOverManager.IsGameOver && !GameWinManager.IsGameWon) elapsed += Time.deltaTime;
            yield return null;
        }

        if (RunState.WaveNumber == finalWaveNumber)
        {
            // 시간은 다 됐지만 보스가 아직 살아있으면, 처치할 때까지 계속 기다린다
            // (사용자 확정 사항: 처치와 시간 종료 둘 다 필요).
            while (boss_spawned_this_wave && !boss_defeated_this_wave && !GameOverManager.IsGameOver)
            {
                yield return null;
            }

            if (GameOverManager.IsGameOver) yield break; // 보스전 도중 플레이어가 죽었으면 승리로도 정비로도 진행하지 않음

            WinGame();
            yield break; // 정비/상점으로 넘어가는 일반 EndWave() 흐름을 타지 않는다
        }

        EndWave();
    }

    private void WinGame()
    {
        if (enemySpawner != null)
        {
            enemySpawner.SetSpawningEnabled(false);
            enemySpawner.DespawnAllAliveEnemies();
        }

        Debug.Log($"웨이브 {RunState.WaveNumber} 보스 처치 + 시간 종료 - 승리!");
        GameWinManager.TriggerWin();
    }

    private void EndWave()
    {
        int wave = RunState.WaveNumber;

        if (enemySpawner != null)
        {
            enemySpawner.SetSpawningEnabled(false);
            enemySpawner.DespawnAllAliveEnemies();
        }

        Debug.Log($"웨이브 {wave} 종료");
        OnWaveEnded?.Invoke(wave);
    }
}
