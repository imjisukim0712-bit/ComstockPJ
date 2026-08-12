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

    [Tooltip("보유한 부품 상자를 '부품 상자 5/20' 형식으로 표시할 텍스트 (비워두면 표시 생략).\n" +
             "분모는 머리(로봇)의 적재량이며, 이 개수에 도달하면 몬스터가 상자를 더 드랍하지 않는다")]
    [SerializeField] private TextMeshProUGUI partBoxText;

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
            UpdateHpBarArt(current / (float)max);
        }

        if (hpValueText != null)
        {
            hpValueText.text = $"{current} / {max}";
        }

        if (waveText != null) waveText.text = $"웨이브 {RunState.WaveNumber}";
        if (goldText != null) goldText.text = $"골드 {RunState.Gold}";

        UpdateWaveTime();
        UpdateExpBar();
        UpdatePartBoxCount();
        UpdateDashCooldown();
    }

    // ── 체력 바 색 ───────────────────────────────────────────────────
    // 체력이 넉넉하면 초록, 위험하면 빨강 막대 아트로 바꿔 한눈에 위험을 알 수 있게 한다
    // (2026-08-13 UI 아트 적용). 스프라이트는 Resources에서 한 번만 읽어 캐시한다.
    [Header("체력 바 아트 (Resources/UI)")]
    [Tooltip("이 비율보다 체력이 높으면 초록 막대, 낮으면 빨강 막대를 쓴다")]
    [SerializeField] private float hpDangerRatio = 0.4f;

    private Image hp_fill_image;
    private Sprite hp_bar_healthy;
    private Sprite hp_bar_danger;
    private bool hp_bar_art_loaded;

    private void UpdateHpBarArt(float ratio)
    {
        if (!hp_bar_art_loaded)
        {
            hp_bar_art_loaded = true;
            hp_bar_healthy = Resources.Load<Sprite>("UI/Green_bar00");
            hp_bar_danger = Resources.Load<Sprite>("UI/Red_bar00");
            if (hpSlider.fillRect != null) hp_fill_image = hpSlider.fillRect.GetComponent<Image>();
        }

        if (hp_fill_image == null || hp_bar_healthy == null || hp_bar_danger == null) return;

        Sprite wanted = ratio > hpDangerRatio ? hp_bar_healthy : hp_bar_danger;
        if (hp_fill_image.sprite != wanted) hp_fill_image.sprite = wanted;
    }

    // ── 구르기(Space) 재사용 대기 표시 ────────────────────────────────
    [Header("구르기 쿨다운 아이콘")]
    [Tooltip("구르기가 도는 동안 위에서 아래로 차오르는 어두운 덮개(Image type=Filled). " +
             "비워두면 아무 것도 하지 않는다")]
    [SerializeField] private Image dashCooldownOverlay;

    private void UpdateDashCooldown()
    {
        if (dashCooldownOverlay == null) return;

        float ratio = player.DashCooldownRatio; // 0 = 사용 가능, 1 = 방금 사용
        dashCooldownOverlay.fillAmount = ratio;
        dashCooldownOverlay.enabled = ratio > 0.001f;
    }

    // 부품 상자는 머리(로봇)의 적재량만큼만 보유할 수 있고 상한에 도달하면 더 드랍되지 않으므로,
    // 플레이어가 "지금 몇 개까지 더 얻을 수 있는지" 알 수 있게 보유량/상한을 함께 보여준다.
    private void UpdatePartBoxCount()
    {
        if (partBoxText == null) return;

        ModdingManager modding = ModdingManager.Instance;
        int capacity = modding != null ? modding.PartBoxCapacity : 0;

        partBoxText.text = $"부품 상자 {RunState.UnopenedPartBoxCount}/{capacity}";
    }

    // 웨이브 남은 초. 일반 웨이브는 제한시간이 끝나는 즉시 종료되지만, 보스 웨이브만은
    // 보스를 처치해야 끝나므로 그 구간에서는 초 대신 "보스 처치"를 보여준다
    // (0:00에서 멈춰 있으면 게임이 멈춘 것처럼 보인다).
    private void UpdateWaveTime()
    {
        if (waveTimeText == null || waveManager == null) return;

        if (waveManager.IsWaitingForBossDefeat)
        {
            waveTimeText.text = "보스 처치";
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
