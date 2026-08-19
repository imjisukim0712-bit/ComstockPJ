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
/// - 2026-08-18 HUD 정리: 항목명 글자("HP"/"골드"/"부품 상자"/"레벨")를 전부 없애고 아이콘으로
///   대신한다. 체력·경험치는 게이지와 숫자를 한 줄로 합쳐 좌상단에 세로로 쌓고, 웨이브/남은 시간은
///   상단 중앙, 구르기 쿨다운은 우하단에 둔다. 배치는 씬(Ground01)이 갖고 있고 여기서는 문구만 만든다.
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

    [Tooltip("보유한 부품 상자를 '03 / 20' 형식으로 표시할 텍스트 (비워두면 표시 생략).\n" +
             "항목명은 옆에 붙은 상자 아이콘이 대신하므로 글자로 쓰지 않는다.\n" +
             "분모는 머리(로봇)의 적재량이며, 이 개수에 도달하면 몬스터가 상자를 더 드랍하지 않는다")]
    [SerializeField] private TextMeshProUGUI partBoxText;

    [Header("AI 코어 경험치 바")]
    [Tooltip("AI 코어 레벨/경험치를 조회할 매니저 (다음 레벨 필요 경험치 계산에 사용)")]
    [SerializeField] private AiCoreManager aiCoreManager;
    [Tooltip("경험치 바 왼쪽(별 아이콘 옆)에 레벨 숫자만 표시할 텍스트")]
    [SerializeField] private TextMeshProUGUI expLevelText;
    [Tooltip("경험치 바 가운데에 '16 / 30' 형식으로 표시할 텍스트 (최대 레벨이면 'MAX')")]
    [SerializeField] private TextMeshProUGUI expValueText;
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

        // 항목명("웨이브"/"골드")은 상단 중앙 패널 위치와 골드 아이콘이 대신하므로 숫자만 쓴다
        if (waveText != null) waveText.text = $"WAVE [{RunState.WaveNumber}]";
        if (goldText != null) goldText.text = RunState.Gold.ToString();

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
    // 2026-08-19 사용자 요청으로 <b>우하단 버튼형 아이콘 → 캐릭터 발밑 게이지 바</b>로 교체했다.
    // 우하단은 시선이 캐릭터에 있을 때 눈에 안 들어와 쿨다운을 사실상 못 봤다.
    // 새 게이지는 DashGaugeUI가 코드로 만들어 캐릭터를 따라다닌다.
    [Header("구르기 쿨다운")]
    [Tooltip("(구버전) 우하단 아이콘의 어두운 덮개. 게이지 바로 교체했으므로 비워두면 된다. " +
             "값이 남아 있으면 그 오브젝트를 자동으로 숨긴다")]
    [SerializeField] private Image dashCooldownOverlay;

    private DashGaugeUI dash_gauge;

    /// <summary>
    /// 캐릭터 발밑 구르기 게이지를 붙인다(없으면 만든다).
    ///
    /// <b>Awake가 아니라 갱신 시점에도 확인하는 이유</b>: 에디터 도메인 리로드로 직렬화되지 않는
    /// private 필드가 null로 돌아가면 게이지가 사라진다. 2026-08-18 AI 코어 리롤 버튼에서 똑같은
    /// 함정을 밟아 <c>EnsureAiCoreExtraButtons()</c>를 넣었던 것과 같은 처리다.
    /// </summary>
    private void EnsureDashGauge()
    {
        if (dash_gauge != null) return;

        // 구버전 우하단 아이콘은 지우지 않고 숨긴다(되돌리려면 이 오브젝트를 다시 켜고
        // DashGaugeUI 생성만 막으면 된다 - 프로젝트 관례상 씬 오브젝트는 삭제하지 않는다).
        if (dashCooldownOverlay != null && dashCooldownOverlay.transform.parent != null)
        {
            GameObject legacyIcon = dashCooldownOverlay.transform.parent.gameObject;
            if (legacyIcon.activeSelf) legacyIcon.SetActive(false);
        }

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        dash_gauge = DashGaugeUI.Attach(canvas.transform as RectTransform, player);
    }

    private void UpdateDashCooldown()
    {
        // 게이지 자체가 매 프레임 스스로 위치·채움을 갱신하므로 여기서는 존재만 보장한다.
        EnsureDashGauge();
    }

    // 부품 상자는 머리(로봇)의 적재량만큼만 보유할 수 있고 상한에 도달하면 더 드랍되지 않으므로,
    // 플레이어가 "지금 몇 개까지 더 얻을 수 있는지" 알 수 있게 보유량/상한을 함께 보여준다.
    private void UpdatePartBoxCount()
    {
        if (partBoxText == null) return;

        ModdingManager modding = ModdingManager.Instance;
        int capacity = modding != null ? modding.PartBoxCapacity : 0;

        partBoxText.text = $"{RunState.UnopenedPartBoxCount:00} / {capacity}";
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
        waveTimeText.text = $"{seconds / 60:00}:{seconds % 60:00}"; // 폭이 흔들리지 않게 분도 2자리 고정
    }

    // 경험치 바 하나에 전부 겹쳐 표시한다(2026-08-18 HUD 정리) - 별 아이콘 옆에 레벨 숫자,
    // 바 가운데에 "현재 / 필요". "레벨"이라는 글자는 별 아이콘이 대신하므로 쓰지 않는다.
    // 최대 레벨(GetRequiredExpForNextLevel()이 -1)이면 바를 꽉 채우고 "MAX"로 표시한다.
    private void UpdateExpBar()
    {
        if (aiCoreManager == null) return;

        int required = aiCoreManager.GetRequiredExpForNextLevel();
        bool isMaxLevel = required < 0;

        if (expLevelText != null)
        {
            expLevelText.text = RunState.CoreLevel.ToString();
        }

        if (expValueText != null)
        {
            expValueText.text = isMaxLevel ? "MAX" : $"{RunState.CoreExp} / {required}";
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
