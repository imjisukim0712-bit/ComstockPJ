using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 중 상시 HUD(체력/웨이브/골드/AI 코어 경험치 바)와 게임오버·승리 문구를 관리한다.
///
/// 사용법: Canvas 밑 아무 오브젝트(예: HP)에 이 스크립트를 붙이고, 인스펙터의 각 슬롯에
/// 대응하는 UI 오브젝트를 드래그해서 연결하면 된다. 비워두면 그 항목만 표시가 생략된다.
///
/// - 매 프레임 PlayerRobotController의 CurrentHp/MaxHp, RunState의 웨이브/골드/AI 코어
///   경험치를 읽어 실시간으로 갱신한다.
/// - 게임 시작 시 Game Over/Victory 오브젝트를 항상 비활성화한다(씬에 켜진 채로 저장돼 있어도 무시).
/// - 체력이 0 이하가 되면(GameOverManager.OnGameOver) Game Over 오브젝트를,
///   마지막 웨이브 보스를 처치하면(GameWinManager.OnGameWon) Victory 오브젝트를 활성화한다.
///   플레이어 이동/발사 정지는 각 매니저가 이미 처리하므로 여기서는 UI 표시만 담당한다.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("체력 UI")]
    [Tooltip("체력 숫자를 표시할 TextMeshProUGUI (예: Hp_value) - '80 / 100' 형식으로 표시")]
    [SerializeField] private TextMeshProUGUI hpValueText;

    [Tooltip("체력 비율을 표시할 슬라이더 (예: Hp_Slider)")]
    [SerializeField] private Slider hpSlider;

    [Header("게임오버 / 승리")]
    [Tooltip("체력이 0 이하가 되면 활성화할 오브젝트 (예: GameOver 텍스트)")]
    [SerializeField] private GameObject gameOverObject;

    [Tooltip("마지막 웨이브 보스를 처치하면 활성화할 오브젝트")]
    [SerializeField] private GameObject victoryObject;

    [Header("웨이브/런 진행 표시 (비워두면 표시 생략)")]
    [Tooltip("현재 웨이브 번호를 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI waveText;
    [Tooltip("골드를 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI goldText;

    [Tooltip("웨이브 남은 시간을 표시할 텍스트 (비워두면 표시 생략)")]
    [SerializeField] private TextMeshProUGUI waveTimeText;
    [Tooltip("남은 시간을 조회할 웨이브 매니저")]
    [SerializeField] private WaveManager waveManager;

    [Header("AI 코어 경험치 바")]
    [Tooltip("AI 코어 레벨/경험치를 조회할 매니저 (다음 레벨 필요 경험치 계산에 사용)")]
    [SerializeField] private AiCoreManager aiCoreManager;
    [Tooltip("'레벨 5  32/50' 형식으로 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI expLevelText;
    [Tooltip("경험치 비율을 표시할 슬라이더")]
    [SerializeField] private Slider expSlider;

    private PlayerRobotController player;

    private void Awake()
    {
        if (gameOverObject != null) gameOverObject.SetActive(false); // 게임 시작 시 항상 비활성화
        if (victoryObject != null) victoryObject.SetActive(false);

        FindPlayer();

        GameOverManager.OnGameOver += HandleGameOver;
        GameWinManager.OnGameWon += HandleGameWon;
    }

    private void OnDestroy()
    {
        GameOverManager.OnGameOver -= HandleGameOver;
        GameWinManager.OnGameWon -= HandleGameWon;
    }

    private void FindPlayer()
    {
        GameObject player_obj = GameObject.FindGameObjectWithTag("Player");
        if (player_obj != null) player = player_obj.GetComponent<PlayerRobotController>();
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer(); // 로딩 순서상 Awake 시점엔 아직 없을 수 있어 계속 재시도
            return;
        }

        int max = Mathf.Max(1, player.MaxHp);
        int current = Mathf.Clamp(player.CurrentHp, 0, max);

        if (hpSlider != null)
        {
            hpSlider.maxValue = max;
            hpSlider.value = current;
        }

        if (hpValueText != null)
        {
            hpValueText.text = $"{current} / {max}";
        }

        if (waveText != null) waveText.text = $"웨이브 {RunState.WaveNumber}";
        if (goldText != null) goldText.text = $"골드 {RunState.Gold}";

        UpdateWaveTime();
        UpdateExpBar();
    }

    // 웨이브 남은 초. 제한시간이 끝난 뒤에는 남은 적을 다 잡아야 웨이브가 끝나므로,
    // 그 구간에서는 초 대신 "잔적 처치"를 보여준다(0초에서 멈춰 있으면 멈춘 것처럼 보인다).
    private void UpdateWaveTime()
    {
        if (waveTimeText == null || waveManager == null) return;

        if (waveManager.IsClearingRemainingEnemies)
        {
            waveTimeText.text = "잔적 처치";
            return;
        }

        int seconds = Mathf.CeilToInt(waveManager.RemainingSeconds);
        waveTimeText.text = $"{seconds / 60}:{seconds % 60:00}";
    }

    // 기획서 p.10 표시 예시(레벨 5(+2) 80/100)를 따라 "레벨 N  현재/필요" 형식으로 보여준다.
    // 최대 레벨(GetRequiredExpForNextLevel()이 -1)이면 바를 꽉 채우고 "MAX"로 표시한다.
    private void UpdateExpBar()
    {
        if (aiCoreManager == null) return;

        int required = aiCoreManager.GetRequiredExpForNextLevel();
        bool isMaxLevel = required < 0;

        if (expLevelText != null)
        {
            expLevelText.text = isMaxLevel
                ? $"레벨 {RunState.CoreLevel} (MAX)"
                : $"레벨 {RunState.CoreLevel}  {RunState.CoreExp}/{required}";
        }

        if (expSlider != null)
        {
            expSlider.maxValue = isMaxLevel ? 1 : Mathf.Max(1, required);
            expSlider.value = isMaxLevel ? expSlider.maxValue : RunState.CoreExp;
        }
    }

    private void HandleGameOver()
    {
        if (gameOverObject != null) gameOverObject.SetActive(true);
    }

    private void HandleGameWon()
    {
        if (victoryObject != null) victoryObject.SetActive(true);
    }
}
