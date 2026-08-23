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
///
/// <b>보스 웨이브가 아닌 웨이브는 제한시간이 끝나는 즉시 종료된다</b>(2026-08-10 사용자 지정).
/// 예전에는 "스폰만 멈추고 남은 적을 전부 처치할 때까지 대기 → 2초 여유" 규칙이었는데,
/// 잔적을 쫓아다니는 시간이 길어져 템포가 늘어졌다. 지금은 남은 적을 EndWave()가 그대로
/// 소멸시키며(EnemySpawner.DespawnAllAliveEnemies는 Die()를 거치지 않으므로 보상/아이템이
/// 드랍되지 않는다), 이미 필드에 떨어져 있던 보상·아이템은 GameFlowManager가 정비 진입 시
/// 전부 자동 수령(자석 흡수) 처리한다.
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Header("스폰 연결")]
    [SerializeField] private EnemySpawner enemySpawner;

    [Header("웨이브 제한시간 (초) - 기획서: 웨이브당 약 1분")]
    [SerializeField] private float firstWaveDuration = 60f;
    [SerializeField] private float waveDurationIncreasePerWave = 0f;

    [Header("웨이브별 스폰 압력")]
    [Tooltip("웨이브가 오를 때마다 스폰 간격에 곱해지는 감소율 (예: 0.96 = 매 웨이브 4%씩 빨라짐). 20웨이브 기준")]
    [SerializeField] private float spawnIntervalDecayPerWave = 0.96f;
    [SerializeField] private float minSpawnInterval = 0.3f;
    [Tooltip("웨이브가 오를 때마다 최대 동시 생존 몬스터 수에 더해지는 값")]
    [SerializeField] private int maxAliveIncreasePerWave = 4;

    [Header("초반 완충 구간 (2026-08-13 사용자 지정: 5웨이브까지는 쉽게)")]
    [Tooltip("이 웨이브까지는 스폰 압력을 완만하게 올리는 '연습 구간'으로 취급한다")]
    [SerializeField] private int easyWaveCount = 5;

    [Tooltip("웨이브 1의 스폰 간격에 곱해지는 배율(1보다 크면 더 느리게 나온다). " +
             "easyWaveCount 웨이브에 걸쳐 1.0으로 선형 복귀한다")]
    [SerializeField] private float firstWaveIntervalMultiplier = 1.6f;

    [Tooltip("완충 구간에서 최대 동시 생존 수 증가폭에 곱해지는 배율. " +
             "초반에는 화면이 적으로 메워지지 않게 절반만 올린다")]
    [SerializeField] private float easyWaveMaxAliveScale = 0.5f;

    [Tooltip("한 번에 스폰하는 마리 수를 1 늘리는 데 필요한 웨이브 수(5면 6·11·16웨이브에 +1). " +
             "완충 구간(easyWaveCount=5) 안에서 배치가 늘지 않도록 같은 값으로 맞춰 두는 것이 좋다. " +
             "0 이하면 배치 수를 늘리지 않는다")]
    [SerializeField] private int wavesPerSpawnBatchIncrease = 5;

    [Header("보스 웨이브 (전부 밸런스 미확정 임시값)")]
    [Tooltip("이 웨이브 번호에 도달하면 보스를 스폰한다. 기획서 확정: 20")]
    [SerializeField] private int finalWaveNumber = 20;

    [Tooltip("보스 프리팹(Assets/Prefebs/Boss.prefab). 2026-08-20부터 전용 아트 " +
             "'좀비 군집체'(Resources/BossMove, 초대형 규격 800px 상당)를 쓴다")]
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

    /// <summary>UI 표시용 - 마지막(보스) 웨이브 번호. 정비 화면의 "WAVE 07 / 20" 표기에 쓴다.</summary>
    public int FinalWaveNumber => finalWaveNumber;

    /// <summary>HUD 표시용 - 현재 웨이브의 남은 제한시간(초). 시간이 끝나면 0.</summary>
    public float RemainingSeconds { get; private set; }

    /// <summary>
    /// HUD 표시용 - 보스 웨이브에서 제한시간은 끝났지만 보스가 아직 살아있어 기다리는 중인지.
    /// 이때는 남은 초(0:00에서 멈춘 것처럼 보인다) 대신 "보스 처치" 상태를 보여준다.
    /// 보스 웨이브가 아닌 웨이브는 제한시간이 끝나는 즉시 종료되므로 항상 false다.
    /// </summary>
    public bool IsWaitingForBossDefeat { get; private set; }

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
            enemySpawner.ConfigureDifficulty(ComputeSpawnInterval(wave), ComputeMaxAlive(wave), ComputeSpawnBatchSize(wave));
            enemySpawner.SetCurrentWave(wave);
            enemySpawner.SetSpawningEnabled(true);
        }

        boss_spawned_this_wave = false;
        boss_defeated_this_wave = false;
        if (IsBossWave(wave)) SpawnBoss();

        float duration = firstWaveDuration + waveDurationIncreasePerWave * (wave - 1);

        if (wave_timer_routine != null) StopCoroutine(wave_timer_routine);
        wave_timer_routine = StartCoroutine(WaveTimer(duration));

        UnlockTracker.ReportWaveStarted(); // 무피격 클리어(바람 소리) 판정을 이 웨이브부터 다시 잰다
        Debug.Log($"웨이브 {wave} 시작 (제한시간 {duration:F0}초)" + (wave == finalWaveNumber ? " - 보스 웨이브" : ""));
        OnWaveStarted?.Invoke(wave);
    }

    /// <summary>
    /// 이 웨이브의 스폰 간격(초). 완충 구간(1~easyWaveCount)은 웨이브 1을
    /// firstWaveIntervalMultiplier배로 느리게 시작해 완충 구간이 끝나는 웨이브에 기준값(x1.0)이
    /// 되고, 그 뒤부터 기존 감소율(spawnIntervalDecayPerWave)이 적용된다.
    ///
    /// 계산의 기준은 항상 <see cref="EnemySpawner.BaseSpawnInterval"/>(웨이브 1 원본값)이다 -
    /// 예전에는 이미 조정된 값에 다시 배율을 곱해 난이도가 이중으로 누적됐다(2026-08-13 수정).
    /// </summary>
    private float ComputeSpawnInterval(int wave)
    {
        float multiplier;

        if (wave <= easyWaveCount && easyWaveCount > 1)
        {
            float t = (wave - 1) / (float)(easyWaveCount - 1); // 0 → 1
            multiplier = Mathf.Lerp(Mathf.Max(1f, firstWaveIntervalMultiplier), 1f, t);
        }
        else
        {
            int wavesAfterEasy = Mathf.Max(0, wave - Mathf.Max(1, easyWaveCount));
            multiplier = Mathf.Pow(spawnIntervalDecayPerWave, wavesAfterEasy);
        }

        return Mathf.Max(minSpawnInterval, enemySpawner.BaseSpawnInterval * multiplier);
    }

    /// <summary>
    /// 이 웨이브의 최대 동시 생존 수. 완충 구간에서는 증가폭을 easyWaveMaxAliveScale배로 줄인다
    /// ("초반 5라운드까지는 쉽게" - 사용자 지정).
    /// </summary>
    private int ComputeMaxAlive(int wave)
    {
        int easyWaves = Mathf.Clamp(wave - 1, 0, Mathf.Max(0, easyWaveCount - 1));
        int hardWaves = Mathf.Max(0, wave - 1 - easyWaves);

        float increase = maxAliveIncreasePerWave * (easyWaves * easyWaveMaxAliveScale + hardWaves);
        int computed = enemySpawner.BaseMaxAliveEnemies + Mathf.RoundToInt(increase);

        // 엔드리스 모드(2026-08-19)는 wave가 무한정 커질 수 있어 이 값도 무한정 커진다.
        // 기획상 상한은 없지만(오히려 "적 스탯 상승" 곡선의 일부), 동시 생존 개체 수만큼은
        // 성능 안전장치로 상한을 둔다 - 여기서 막지 않으면 아주 늦은 웨이브에서 스폰 자체가
        // 프레임을 잡아먹어 "어려워서 죽는" 게 아니라 "버벅여서 죽는" 상태가 될 수 있다.
        const int enduranceSafetyCap = 150;
        return Mathf.Min(computed, enduranceSafetyCap);
    }

    /// <summary>
    /// 이 웨이브에 한 번에 스폰하는 마리 수. 웨이브가 오를수록 조금씩(wavesPerSpawnBatchIncrease
    /// 웨이브마다 +1) 늘어난다 - "웨이브가 지날수록 스폰량을 조금씩 늘린다"(사용자 지정).
    /// </summary>
    private int ComputeSpawnBatchSize(int wave)
    {
        if (wavesPerSpawnBatchIncrease <= 0) return enemySpawner.BaseSpawnBatchSize;

        return enemySpawner.BaseSpawnBatchSize + (wave - 1) / wavesPerSpawnBatchIncrease;
    }

    /// <summary>
    /// 이 웨이브가 보스 웨이브인지. 첫 보스는 <see cref="finalWaveNumber"/>(20)에 등장하고,
    /// 엔드리스 모드(2026-08-19 Phase C)에서는 그 뒤로 <c>finalWaveNumber - 1</c>웨이브
    /// (19웨이브)마다 반복된다 - 웨이브 20 → 39 → 58 → ... (기획 확정값).
    /// public인 이유: GameFlowManager의 웨이브 전환 배너(2026-08-21)가 "BOSS INCOMING" 문구를
    /// 틀 웨이브인지 판단할 때 이 판정을 그대로 재사용한다(중복 구현 방지).
    /// </summary>
    public bool IsBossWave(int wave)
    {
        if (wave < finalWaveNumber) return false;
        if (wave == finalWaveNumber) return true;

        int period = Mathf.Max(1, finalWaveNumber - 1);
        return (wave - finalWaveNumber) % period == 0;
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

        // 첫 보스(웨이브 20)는 기존 밸런스(bossMaxHp 등 인스펙터 값)를 그대로 쓴다 - 이미
        // "웨이브 20 도달 플레이어" 기준으로 실측해 잡아둔 값이라 추가 배율을 곱하면 검증 안 된
        // 난이도가 된다. <b>엔드리스 사이클(39, 58, ...)의 재등장 보스만</b> 일반 몬스터와 같은
        // 웨이브 스탯 배율(EnemySpawner.CurrentStatMultiplier)을 곱해 갈수록 강해지게 한다
        // (2026-08-19 Phase C) - 안 그러면 플레이어는 계속 강해지는데 보스만 제자리라
        // 엔드리스가 무의미해진다.
        float multiplier = RunState.WaveNumber == finalWaveNumber || enemySpawner == null
            ? 1f
            : enemySpawner.CurrentStatMultiplier;

        // 보스 스탯은 GameDataAsset(시트 연동) 밖에 둔다 - 몬스터 시트를 다시 가져오기(재임포트)
        // 할 때 이 값이 조용히 지워지는 것을 막기 위함(에디터 임포터가 매번 목록을 통째로 비우고
        // 다시 채우는 구조라, 시트에 없는 보스 행을 여기 넣어두면 재임포트 시 사라진다).
        var bossData = new MonsterData
        {
            monster_id = -1,
            monster_name = "보스",
            monster_hp = Mathf.RoundToInt(bossMaxHp * multiplier),
            monster_atk = Mathf.RoundToInt(bossAtk * multiplier),
            monster_def = bossDef,
            monster_speed = bossMoveSpeed,
            monster_range = bossAttackRange,
            monster_type = 1,
            monster_atsp = bossAttackSpeed
        };
        boss.Init(bossData);
        boss.OnDefeated += HandleBossDefeated;

        // 등장(소환) 연출 - 2026-08-23 보스연출가이드라인 반영. 예전에는 Init 직후 곧바로
        // 싸울 수 있는 즉시 스폰이었다. 이제 소환진(BossSummon 99프레임)이 도는 동안 보스는
        // 완전 무적 + 본체 숨김이고, 절반쯤에서 드러나 포효까지 마쳐야 전투가 시작된다.
        boss.PlaySpawnIntro();

        boss_spawned_this_wave = true;
        Debug.Log($"보스 등장 (웨이브 {RunState.WaveNumber}, HP {bossData.monster_hp}, 배율 x{multiplier:0.##})");
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
        IsWaitingForBossDefeat = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (!GameOverManager.IsGameOver && !GameWinManager.IsGameWon) elapsed += Time.deltaTime;
            RemainingSeconds = Mathf.Max(0f, duration - elapsed);
            yield return null;
        }
        RemainingSeconds = 0f;

        if (enemySpawner != null) enemySpawner.SetSpawningEnabled(false);

        if (IsBossWave(RunState.WaveNumber))
        {
            // 보스 웨이브만 예외다. 시간은 다 됐지만 보스가 아직 살아있으면 처치할 때까지 계속
            // 기다린다(사용자 확정 사항: 처치와 시간 종료 둘 다 필요).
            IsWaitingForBossDefeat = true;

            while (boss_spawned_this_wave && !boss_defeated_this_wave && !GameOverManager.IsGameOver)
            {
                yield return null;
            }

            IsWaitingForBossDefeat = false;

            if (GameOverManager.IsGameOver) yield break; // 보스전 도중 플레이어가 죽었으면 승리로도 정비로도 진행하지 않음

            // <b>2026-08-19 Phase C(엔드리스)</b>: 처음 finalWaveNumber(20)에 도달했을 때만
            // WinGame()으로 빠져 점수 정산 팝업(GameFlowManager.HandleGameWon)을 띄운다.
            // 이미 "계속 진행"을 선택해 엔드리스 중이라면(RunState.IsEndless) 재등장 보스
            // (39, 58, ...)는 그냥 일반 웨이브처럼 EndWave() → 정비/상점으로 이어간다.
            if (RunState.WaveNumber == finalWaveNumber && !RunState.IsEndless)
            {
                WinGame(); // 정비/상점으로 넘어가는 일반 EndWave() 흐름을 타지 않는다
            }
            else
            {
                EndWave();
            }
            yield break;
        }

        if (GameOverManager.IsGameOver) yield break;

        // 보스 웨이브가 아니면 제한시간이 끝나는 즉시 종료한다(2026-08-10 사용자 지정).
        // 남은 적은 EndWave()의 DespawnAllAliveEnemies()가 Die()를 거치지 않고 통째로 소멸시키므로
        // 경험치·골드·부품 상자·아이템이 전혀 드랍되지 않는다.
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

        // 남은 적은 여기서 통째로 소멸한다. Destroy만 하고 EnemyUnit.Die()를 부르지 않으므로
        // 보상 픽업/드랍 아이템이 생기지 않는다("남은 좀비는 아이템 드랍 없이 사라진다").
        if (enemySpawner != null)
        {
            enemySpawner.SetSpawningEnabled(false);
            enemySpawner.DespawnAllAliveEnemies();
        }

        // 해금 진행도는 <b>체력 회복 전에</b> 기록해야 한다(2026-08-19 Phase E) - 조건 2건이
        // "체력 30 이하로 클리어"/"최대 체력 200 이상으로 클리어"라 회복 후에 재면 의미가 없다.
        // GameFlowManager의 회복은 아래 OnWaveEnded를 받은 뒤에 일어나므로 이 자리가 맞다.
        UnlockTracker.ReportWaveCleared(wave);

        // 이미 필드에 떨어져 있던 보상·아이템은 이 이벤트를 받은 GameFlowManager가
        // ResetFieldForIntermission()에서 전부 자동 수령(자석 흡수) 처리한다.
        Debug.Log($"웨이브 {wave} 종료 (제한시간 종료 즉시)");
        OnWaveEnded?.Invoke(wave);
    }
}
