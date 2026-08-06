using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 웨이브 진행을 관리한다: 웨이브는 시간이 다 되면 끝난다(적을 모두 잡아야 끝나는 방식이 아님 -
/// 뱀서라이크 장르 문법). 웨이브가 끝나면 남은 적을 정리하고 스폰을 멈춘 뒤 OnWaveEnded를
/// 발행한다. GameFlowManager가 이 이벤트를 받아 정비/상점 화면으로 전환하고,
/// 준비되면 StartNextWave()를 호출해 다음 웨이브를 연다.
///
/// 기획서 확정(20웨이브 + 보스, 웨이브당 약 1분)에 맞춰 웨이브가 오를수록 스폰 압력(간격 감소,
/// 최대 생존수 증가)이 세진다. 마지막 웨이브(finalWaveNumber)에는 보스가 추가로 등장하며
/// 잡몹 스폰도 계속된다(기획서: "보스와 잡몹이 동시에 등장"). "보스를 처치하고 +
/// 웨이브 제한시간도 다 지나야" 승리로 처리한다(둘 중 하나만 먼저 끝나도 승리하지 않음 -
/// 사용자 확정 사항). 시간이 먼저 끝나도 보스가 살아있으면 계속 기다리고, 보스를 먼저 잡아도
/// 시간이 남았으면 그만큼 더 기다린다.
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Header("스폰 연결")]
    [SerializeField] private EnemySpawner enemySpawner;

    [Header("웨이브 제한시간 (초) - 기획서: 웨이브당 약 1분")]
    [SerializeField] private float firstWaveDuration = 60f;
    [SerializeField] private float waveDurationIncreasePerWave = 0f;

    [Header("웨이브 종료 처리")]
    [Tooltip("제한시간이 끝나면 스폰을 멈추고 남은 적을 전부 처치할 때까지 기다린다. 다 잡은 뒤 이 시간만큼 더 있다가 정비로 넘어간다")]
    [SerializeField] private float clearedGraceSeconds = 2f;
    [Tooltip("남은 적을 기다리는 최대 시간(안전장치). 적이 어딘가에 끼어 영원히 안 죽는 상황에서 게임이 멈추지 않도록 이 시간이 지나면 강제로 정리하고 넘어간다")]
    [SerializeField] private float maxClearWaitSeconds = 45f;

    [Header("웨이브별 스폰 압력")]
    [Tooltip("웨이브가 오를 때마다 스폰 간격에 곱해지는 감소율 (예: 0.96 = 매 웨이브 4%씩 빨라짐). 20웨이브 기준")]
    [SerializeField] private float spawnIntervalDecayPerWave = 0.96f;
    [SerializeField] private float minSpawnInterval = 0.3f;
    [Tooltip("웨이브가 오를 때마다 최대 동시 생존 몬스터 수에 더해지는 값")]
    [SerializeField] private int maxAliveIncreasePerWave = 4;

    [Header("보스 웨이브 (전부 밸런스 미확정 임시값)")]
    [Tooltip("이 웨이브 번호에 도달하면 보스를 스폰한다. 기획서 확정: 20")]
    [SerializeField] private int finalWaveNumber = 20;

    [Tooltip("보스 프리팹(Assets/Prefebs/Boss.prefab). 전용 아트가 없어 좀비 프리팹을 확대·색조 변경해 임시로 사용 중")]
    [SerializeField] private GameObject bossPrefab;

    [Tooltip("보스 등장 시 플레이어로부터 이 거리만큼 떨어진 지점에 스폰")]
    [SerializeField] private float bossSpawnDistance = 16f;

    // 4500은 "10웨이브 도달 플레이어" 기준으로 실측해 잡은 값이었다. 보스가 20웨이브로 밀리면서
    // 플레이어가 훨씬 강해진 상태로 만나게 되므로 임시로 상향한다. 실제 플레이테스트 후 재조정 필요.
    [SerializeField] private int bossMaxHp = 12000;
    [SerializeField] private int bossAtk = 25;
    [SerializeField] private int bossDef = 5;
    [SerializeField] private float bossMoveSpeed = 1.3f;
    [SerializeField] private float bossAttackRange = 1.5f;
    [SerializeField] private float bossAttackSpeed = 0.8f;

    public event Action<int> OnWaveStarted;
    public event Action<int> OnWaveEnded;

    /// <summary>HUD 표시용 - 현재 웨이브의 남은 제한시간(초). 시간이 끝나면 0.</summary>
    public float RemainingSeconds { get; private set; }

    /// <summary>
    /// HUD 표시용 - 제한시간은 끝났고 남은 적을 처치하는 중인지.
    /// 이때는 남은 초 대신 "잔적 처치" 상태를 보여준다.
    /// </summary>
    public bool IsClearingRemainingEnemies { get; private set; }

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
        IsClearingRemainingEnemies = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!GameOverManager.IsGameOver && !GameWinManager.IsGameWon) elapsed += Time.deltaTime;
            RemainingSeconds = Mathf.Max(0f, duration - elapsed);
            yield return null;
        }
        RemainingSeconds = 0f;

        // 제한시간이 끝나면 더 이상 새로 스폰하지 않는다. 이미 나와 있는 적은 그대로 두고
        // 플레이어가 정리하게 한다(사용자 확정 사항: "1분 종료 후 남은 좀비가 없으면 2초 뒤 끝난다").
        if (enemySpawner != null) enemySpawner.SetSpawningEnabled(false);

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

        IsClearingRemainingEnemies = true;

        // 남은 적을 전부 처치할 때까지 대기. 적이 어딘가에 끼어 영영 안 죽는 상황에서
        // 게임이 멈춰버리지 않도록 maxClearWaitSeconds를 넘기면 강제로 진행한다.
        float waited = 0f;
        while (CountAliveEnemies() > 0 && !GameOverManager.IsGameOver && waited < maxClearWaitSeconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (GameOverManager.IsGameOver) { IsClearingRemainingEnemies = false; yield break; }

        if (waited >= maxClearWaitSeconds)
            Debug.LogWarning($"웨이브 {RunState.WaveNumber}: 남은 적을 {maxClearWaitSeconds:F0}초 동안 정리하지 못해 강제로 종료합니다.");

        // 다 잡은 뒤 잠깐(기본 2초) 여유를 두고 정비로 넘어간다 - 마지막 처치 연출과
        // 보상 픽업을 주울 시간을 준다.
        float grace = 0f;
        while (grace < clearedGraceSeconds && !GameOverManager.IsGameOver)
        {
            grace += Time.deltaTime;
            yield return null;
        }

        IsClearingRemainingEnemies = false;
        if (GameOverManager.IsGameOver) yield break;

        EndWave();
    }

    private static int CountAliveEnemies()
    {
        int count = 0;
        foreach (EnemyUnit enemy in EnemyUnit.Alive)
        {
            if (enemy != null && !enemy.IsDead) count++;
        }
        return count;
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
