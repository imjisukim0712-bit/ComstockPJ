using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 전투 ↔ 정비 사이의 흐름을 담당하는 상태 머신.
/// 기획서 흐름: 웨이브 전투 → (AI 코어 업그레이드) → (로봇 정비) → 상점 → 다음 웨이브.
///
/// AI 코어 업그레이드 카드는 레벨업이 있었을 때만 노출되고(RunState.PendingCoreUpgradeChoices),
/// 로봇 정비 화면은 획득한 부품 상자가 있을 때만 노출된다(RunState.UnopenedPartBoxCount).
/// 상점은 기획서대로 웨이브 종료 후 항상 노출되며, 다음 웨이브로 넘어가는 버튼("정비 종료")도
/// 상점 화면 안에 있다(기획서 p.13의 3번 요소).
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public enum State
    {
        Combat,
        Intermission
    }

    [Header("연결")]
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private AiCoreManager aiCoreManager;

    [Header("AI 코어 업그레이드 카드 (레벨업했을 때만 노출) - 항상 3개 고정")]
    [SerializeField] private GameObject aiCoreUpgradePanel;
    [SerializeField] private Button option1Button;
    [SerializeField] private TextMeshProUGUI option1Text;
    [SerializeField] private Button option2Button;
    [SerializeField] private TextMeshProUGUI option2Text;
    [SerializeField] private Button option3Button;
    [SerializeField] private TextMeshProUGUI option3Text;

    [Header("로봇 정비 화면 (부품 상자가 있을 때만 노출)")]
    [SerializeField] private ModdingPanelUI moddingPanel;

    [Header("상점 화면 (웨이브 종료 후 항상 노출)")]
    [SerializeField] private ShopPanelUI shopPanel;

    [Header("정비 화면 진입 시 처리")]
    [Tooltip("정비 화면이 열려 있는 동안 Time.timeScale을 0으로 만들어 인게임을 완전히 정지시킨다")]
    [SerializeField] private bool freezeTimeDuringIntermission = true;
    [Tooltip("정비 화면에 들어갈 때 필드에 남은 보상 픽업을 자동 수령 처리하고, 투사체를 정리하며, 플레이어를 시작 위치로 되돌린다")]
    [SerializeField] private bool resetFieldOnIntermission = true;
    [Tooltip("자동 수령 전 골드/경험치 픽업이 플레이어 쪽으로 날아가는 자석 연출의 총 시간(초). " +
             "0이면 연출 없이 즉시 수령한다(기존 동작)")]
    [SerializeField] private float magnetCollectDuration = 0.35f;

    [Tooltip("정비 화면이 열려 있는 동안 숨길 전투 전용 HUD(HP, 상단 웨이브/골드/경험치 바 등).\n" +
             "예전에는 ShopPanelUI/ModdingPanelUI가 각자 숨겼는데, AI 코어 업그레이드 화면에는 그 처리가\n" +
             "없어서 HUD가 비쳐 보였다. 정비 단계 전체를 아는 여기서 한 번만 처리한다")]
    [SerializeField] private GameObject[] combatHudObjects = new GameObject[0];

    public State CurrentState { get; private set; } = State.Combat;

    /// <summary>
    /// 정비 화면(AI 코어 업그레이드/로봇 정비/상점)이 열려 있는지. 플레이어 조작·자동공격 등
    /// 인게임 로직이 이 값을 보고 스스로 멈춘다. GameOverManager.IsGameOver와 같은 용도로 쓰는
    /// 전역 플래그라 static으로 둔다.
    /// </summary>
    public static bool IsIntermission { get; private set; }

    /// <summary>
    /// ESC 일시정지 메뉴(<see cref="PauseMenuUI"/>)가 열려 있는지. IsIntermission과 별개의
    /// 플래그다 - 정비/상점 화면(그 자체로 전체 화면 UI)과 일시정지(어느 화면 위에도 반투명하게
    /// 덮이는 오버레이)는 여는 이유도 겪는 부작용도 다르다. 입력을 멈추는 Update 가드들
    /// (PlayerRobotController/PlayerShootManager/DiscEffectRuntime)은 이 값도 함께 확인한다.
    /// </summary>
    public static bool IsPaused { get; private set; }

    /// <summary>PauseMenuUI만 호출한다. GameFlowManager가 이 상태의 유일한 진실 공급원이라
    /// 다른 스크립트의 Update 가드들이 안심하고 참조할 수 있다.</summary>
    public static void SetPaused(bool paused) => IsPaused = paused;

    /// <summary>
    /// 씬을 다시 시작할 때 이전 판의 값이 남지 않도록 PlayerRobotController.Awake()가 호출한다
    /// (EnemyUnit.ResetStaticCaches()와 같은 이유).
    /// </summary>
    public static void ResetStaticState()
    {
        IsIntermission = false;
        IsPaused = false;
        Time.timeScale = 1f; // 정비 중에 플레이모드를 껐다 켠 경우 0으로 굳어있지 않도록
    }

    private int lastEndedWaveNumber;

    // 골드 리롤 / 레벨업 취소 버튼(2026-08-18). 씬에 깔지 않고 코드로 만들어 붙인다.
    // ShowAiCoreUpgradeStep()이 카드 버튼마다 RemoveAllListeners()를 돌리므로, 이 두 버튼은
    // 그 배열에 섞지 않고 여기서 한 번만 리스너를 단다(ShopPanelUI.Awake와 같은 관례).
    private AiCoreExtraButtonsUI aiCoreExtraButtons;

    // ESC 일시정지 메뉴(2026-08-18). 씬에 깔지 않고 Canvas 밑에 코드로 만들어 붙인다.
    private PauseMenuUI pauseMenu;

    // 우상단 설정(톱니바퀴) 아이콘(2026-08-19). 같은 Canvas에 코드로 붙인다.
    private SettingsIconUI settingsIcon;

    // 엔드리스 모드 점수 정산 팝업(2026-08-19 Phase C). 같은 Canvas에 코드로 붙인다.
    private ScoreSummaryPopup scoreSummaryPopup;

    private void Awake()
    {
        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
        EnsureAiCoreExtraButtons();
        EnsurePauseMenu();
    }

    /// <summary>일시정지 메뉴와 우상단 설정 아이콘이 없으면 만들어 붙인다. AiCoreExtraButtons와
    /// 같은 이유로 Awake에서 한 번, 필요하면 그 이후에도 다시 확인할 수 있게 열어 둔다.</summary>
    private void EnsurePauseMenu()
    {
        if (pauseMenu != null && settingsIcon != null && scoreSummaryPopup != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        var canvasRect = (RectTransform)canvas.transform;

        if (pauseMenu == null) pauseMenu = PauseMenuUI.EnsureAttached(canvasRect);
        // 설정 아이콘은 일시정지 메뉴보다 뒤에 붙여야 한다 - 형제 순서가 곧 그리기 순서라
        // 먼저 붙이면 딤 배경에 가려진다(아이콘 쪽에서 메뉴가 열리면 스스로 숨기기도 한다).
        if (settingsIcon == null) settingsIcon = SettingsIconUI.EnsureAttached(canvasRect);
        if (scoreSummaryPopup == null) scoreSummaryPopup = ScoreSummaryPopup.EnsureAttached(canvasRect);
    }

    /// <summary>
    /// 보조 버튼이 없으면 만들어 붙인다. Awake에서 한 번 부르지만 카드 화면을 열 때도 다시
    /// 확인한다 - 에디터에서 플레이 도중 스크립트가 재컴파일되면 도메인 리로드로 이 참조
    /// (직렬화되지 않는 private 필드)가 null로 돌아가기 때문이다. 이미 붙어 있으면 아무것도 안 한다.
    /// </summary>
    private void EnsureAiCoreExtraButtons()
    {
        if (aiCoreExtraButtons != null || aiCoreUpgradePanel == null) return;

        var existing = aiCoreUpgradePanel.GetComponentInChildren<AiCoreExtraButtonsUI>(true);
        if (existing != null)
        {
            aiCoreExtraButtons = existing;
            return;
        }

        aiCoreExtraButtons = AiCoreExtraButtonsUI.Attach(
            (RectTransform)aiCoreUpgradePanel.transform,
            HandleAiCoreRerollClicked);
    }

    private void Start()
    {
        if (waveManager != null) waveManager.OnWaveEnded += HandleWaveEnded;
        if (moddingPanel != null) moddingPanel.OnProceedRequested += HandleModdingProceedRequested;
        if (shopPanel != null) shopPanel.OnNextWaveRequested += HandleNextWaveRequested;
        GameWinManager.OnGameWon += HandleGameWon;
    }

    private void OnDestroy()
    {
        if (waveManager != null) waveManager.OnWaveEnded -= HandleWaveEnded;
        if (moddingPanel != null) moddingPanel.OnProceedRequested -= HandleModdingProceedRequested;
        if (shopPanel != null) shopPanel.OnNextWaveRequested -= HandleNextWaveRequested;
        GameWinManager.OnGameWon -= HandleGameWon;
    }

    private void HandleWaveEnded(int waveNumber)
    {
        // 플레이어가 이 웨이브 도중 사망했다면(웨이브 타이머는 게임오버와 무관하게 그대로 끝까지
        // 흐른다) 정비/상점 화면으로 넘어가지 않는다 - GameOverManager의 게임오버 화면이 이미 떠 있다.
        if (GameOverManager.IsGameOver) return;

        EnterPostWaveIntermission(waveNumber);
    }

    /// <summary>
    /// 웨이브 하나(정규 종료 또는 엔드리스 "계속 진행" 선택 직후)를 마치고 정비 화면으로
    /// 들어가기 전의 공통 처리 - 체력 전부 회복, 필드 투사체 정리, (설정에 따라) 자석 연출.
    ///
    /// 2026-08-19 Phase C(엔드리스)에서 <see cref="HandleWaveEnded"/>와
    /// <see cref="HandleEndlessContinueChosen"/> 두 곳이 똑같은 절차를 필요로 해서 분리했다 -
    /// 원래는 <c>HandleWaveEnded</c> 안에 있던 내용을 그대로 옮긴 것뿐이다(동작 변경 없음).
    /// </summary>
    private void EnterPostWaveIntermission(int waveNumber)
    {
        CurrentState = State.Intermission;
        IsIntermission = true;
        lastEndedWaveNumber = waveNumber;

        // 웨이브를 무사히 넘겼으니 체력을 전부 회복한다(사용자 확정 사항, 2026-08-12).
        // fromWaveEnd=true - 에너지 베리어 디스크의 해금 조건("웨이브 종료 회복 외의 회복")에서
        // 이 회복만 제외하기 위한 구분이다(2026-08-19 Phase E).
        PlayerRobotController player = FindFirstObjectByType<PlayerRobotController>();
        if (player != null) player.Heal(player.MaxHp, true);

        // 필드에 날아다니던 투사체는 여기서 <b>즉시</b> 전부 없앤다(2026-08-13 버그 수정).
        // 웨이브가 끝나면 남은 적은 소멸하는데(WaveManager.EndWave) 그 적들이 이미 쏴 둔 투사체는
        // 남아 있었다. timeScale=0으로 정지 화면에 들어가면 그 상태로 얼어 있다가 다음 웨이브가
        // 시작되는 순간 그대로 날아와 플레이어를 때린다.
        int clearedNow = ClearFieldProjectiles();
        if (clearedNow > 0) Debug.Log($"웨이브 종료 - 필드에 남은 투사체 {clearedNow}개 정리");

        // 정비는 "전체 화면 UI + 인게임 완전 정지" 상태여야 한다(사용자 확정 사항).
        // 필드 정리를 먼저 하고 나서 시간을 멈춘다 - timeScale=0 상태에서 물리 이동을 시키면
        // Rigidbody가 그대로 반영되지 않을 수 있기 때문. 자석 연출이 여러 프레임에 걸쳐 재생돼야
        // 하므로(코루틴) 정지 화면 진입 자체를 코루틴 완료 이후로 미룬다.
        if (resetFieldOnIntermission) StartCoroutine(ResetFieldForIntermissionRoutine());
        else EnterIntermissionScreens();
    }

    // ── 엔드리스 모드 - 20웨이브 첫 클리어 시 점수 정산 팝업(2026-08-19 Phase C) ──────────

    /// <summary>WaveManager.WinGame()이 GameWinManager.TriggerWin()을 부르면(마지막 웨이브를
    /// <b>처음</b> 클리어했을 때만 - RunState.IsEndless가 이미 true면 그 뒤로는 EndWave()로
    /// 빠져 이 이벤트 자체가 발생하지 않는다) 호출된다.</summary>
    private void HandleGameWon()
    {
        EnsurePauseMenu(); // scoreSummaryPopup이 아직 없으면 여기서 만든다(EnsureAiCoreExtraButtons와 같은 방어)

        if (freezeTimeDuringIntermission) Time.timeScale = 0f;
        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);

        int clearedWave = waveManager != null ? waveManager.FinalWaveNumber : RunState.WaveNumber;
        if (scoreSummaryPopup != null)
        {
            scoreSummaryPopup.ShowClearChoice(clearedWave, HandleEndlessContinueChosen, HandleEndlessDeclineChosen);
        }
        else
        {
            Debug.LogWarning("ScoreSummaryPopup을 만들지 못해 정산 화면을 띄우지 못했습니다 - Canvas를 찾을 수 없습니다.");
        }
    }

    private void HandleEndlessContinueChosen()
    {
        RunState.IsEndless = true;
        UnlockTracker.ReportEndlessEntered(); // 교향곡: 암석 디스크

        // WinGame()이 걸어 둔 "게임 종료" 플래그를 풀어야 플레이어·적·스포너 Update 가드들이
        // 다시 움직인다(PlayerRobotController/PlayerShootManager/EnemyUnit 등 - GameWinManager.cs
        // 상단 주석 참고). GameWinManager.Reset()은 원래 씬 재시작용이지만 하는 일이 "IsGameWon을
        // false로"뿐이라 여기서 그대로 재사용해도 안전하다.
        GameWinManager.Reset();

        // 정규 웨이브 종료(HandleWaveEnded)와 완전히 같은 절차(회복 + 필드 정리 + 정비 화면)를
        // 밟아야 웨이브 21이 정상적으로 이어진다 - WinGame()은 이 절차를 건너뛰고 곧장 팝업으로
        // 왔기 때문에 여기서 대신 밟아준다.
        EnterPostWaveIntermission(RunState.WaveNumber);
    }

    private void HandleEndlessDeclineChosen()
    {
        RunScore.SubmitToLeaderboard();

        Time.timeScale = 1f;
        GameFlowManager.SetPaused(false);
        UnityEngine.SceneManagement.SceneManager.LoadScene("Title");
    }

    private void EnterIntermissionScreens()
    {
        if (freezeTimeDuringIntermission) Time.timeScale = 0f;

        CloseAllIntermissionPanels(); // 이전 단계에서 열린 패널이 남아있지 않도록 항상 깨끗하게 시작
        SetCombatHudVisible(false);   // CloseAll보다 뒤에 와야 한다 - 패널의 Close()가 HUD를 다시 켤 수 있으므로
        ShowNextIntermissionStep();
    }

    // 정비 단계 내내 전투 HUD를 숨긴다. 각 Show*() 단계에서도 CloseAllIntermissionPanels() 뒤에
    // 다시 호출해, 패널이 닫히며 HUD를 되살리는 일이 없도록 한다.
    private void SetCombatHudVisible(bool visible)
    {
        foreach (GameObject hud in combatHudObjects)
        {
            if (hud != null) hud.SetActive(visible);
        }
    }

    /// <summary>
    /// 정비 화면에 들어가기 전 필드를 깨끗하게 만든다.
    ///
    /// 골드·경험치는 그냥 지우면 플레이어가 못 주운 보상이 증발해 손해이므로 지우기 전에
    /// 자동으로 수령 처리한다(= 자석 흡수). 예전에는 <c>CollectImmediately()</c>를 바로
    /// 불러 시각적 연출 없이 조용히 사라지기만 했는데, 사용자가 "자석으로 끌어모으는 연출이
    /// 없다"고 지적해(2026-08-12) 실제로 플레이어 쪽으로 날아가는 애니메이션을 재생한 뒤
    /// 수령하도록 바꿨다. <see cref="Time.timeScale"/>이 아직 1인 상태에서(이 시점에는 아직
    /// 멈추지 않았다) 코루틴으로 여러 프레임에 걸쳐 재생한다.
    ///
    /// <b>단, 부품 상자는 자석 흡수 대상이 아니다</b>(2026-08-10 사용자 지정) - 상자는
    /// 직접 가서 주워야 얻는 보상이라, 웨이브가 끝날 때까지 줍지 못했으면 그냥 사라진다.
    /// 화면 밖 상자를 화살표로 안내하는 <see cref="PartBoxIndicatorUI"/>가 의미를 갖는 것도
    /// 이 규칙 때문이다(자동으로 받아지면 굳이 찾아갈 이유가 없다).
    /// </summary>
    private IEnumerator ResetFieldForIntermissionRoutine()
    {
        PlayerRobotController player = FindFirstObjectByType<PlayerRobotController>();

        var rewardTargets = new List<RewardPickup>();
        int discardedPartBoxes = 0;
        foreach (RewardPickup pickup in FindObjectsByType<RewardPickup>(FindObjectsSortMode.None))
        {
            if (pickup == null) continue;

            if (pickup.Type == RewardType.PartBox)
            {
                Destroy(pickup.gameObject); // 수령하지 않고 버린다 - 직접 주웠어야 하는 보상
                discardedPartBoxes++;
                continue;
            }

            rewardTargets.Add(pickup);
        }

        int collectedRewards = rewardTargets.Count;

        if (collectedRewards > 0 && magnetCollectDuration > 0f && player != null)
        {
            yield return StartCoroutine(PlayMagnetFlightRoutine(rewardTargets, player.transform));
        }

        // 애니메이션 도중 플레이어와 실제로 겹쳐 트리거로 먼저 수령됐을 수도 있으므로
        // CollectImmediately()의 idempotent 가드(collected 플래그)를 그대로 믿고 다시 호출한다.
        foreach (RewardPickup pickup in rewardTargets)
        {
            if (pickup != null) pickup.CollectImmediately();
        }

        // 자석 연출이 끝나는 동안(0.35초) 늦게 사라진 적이 쏜 투사체가 새로 생겼을 수 있어 한 번 더 훑는다.
        int clearedProjectiles = ClearFieldProjectiles();

        if (player != null) player.ReturnToStartPosition();

        if (collectedRewards > 0 || clearedProjectiles > 0 || discardedPartBoxes > 0)
        {
            Debug.Log($"정비 진입 - 필드 초기화 (보상 픽업 {collectedRewards}개 자동 수령, " +
                      $"투사체 {clearedProjectiles}개 정리, 못 주운 부품 상자 {discardedPartBoxes}개 소멸)");
        }

        EnterIntermissionScreens();
    }

    /// <summary>
    /// 필드에 날아다니는 <b>모든 종류의</b> 투사체를 없앤다.
    ///
    /// 예전에는 플레이어가 쏘는 <see cref="Projectile"/>만 지웠고, <see cref="EnemyProjectile"/>
    /// (스피터가 뱉는 탄)과 <see cref="BeamProjectile"/>(플라즈마캐논 빔)은 그대로 남아 있었다.
    /// 그래서 "이전 웨이브의 투사체가 다음 웨이브에 남아있다"는 버그가 있었다(2026-08-13 수정).
    /// 앞으로 투사체 종류를 추가하면 반드시 여기에도 등록해야 한다.
    /// </summary>
    /// <returns>정리한 투사체 개수(로그/검증용)</returns>
    private int ClearFieldProjectiles()
    {
        int cleared = 0;

        foreach (Projectile projectile in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            Destroy(projectile.gameObject);
            cleared++;
        }

        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            Destroy(projectile.gameObject);
            cleared++;
        }

        foreach (BeamProjectile beam in FindObjectsByType<BeamProjectile>(FindObjectsSortMode.None))
        {
            if (beam == null) continue;
            Destroy(beam.gameObject);
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// 골드/경험치 픽업을 플레이어 위치로 끌어당기는 시각 연출만 담당한다(실제 수령/파괴는
    /// 호출부가 애니메이션이 끝난 뒤 처리). 도중에 플레이어와 물리적으로 겹쳐 트리거가 먼저
    /// 발동해도 안전하도록, 애니메이션 시작 전 각 픽업의 Collider를 꺼서 이동 중 재수령을 막는다
    /// (트리거가 없으면 OnTriggerEnter 자체가 안 불린다 - CollectImmediately의 collected 가드에만
    /// 의존하는 것보다 확실하다).
    /// </summary>
    private IEnumerator PlayMagnetFlightRoutine(List<RewardPickup> rewards, Transform target)
    {
        var movers = new List<Transform>(rewards.Count);
        var starts = new List<Vector3>(movers.Capacity);

        foreach (RewardPickup pickup in rewards) CollectMover(pickup.GetComponent<Collider>(), pickup.transform, movers, starts);

        float elapsed = 0f;
        while (elapsed < magnetCollectDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 이 시점엔 아직 timeScale=1이지만, 정지 직전이라도 안전하게 unscaled 사용
            float t = Mathf.Clamp01(elapsed / magnetCollectDuration);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic: 처음엔 빠르게 딸려가고 끝에 감속

            Vector3 targetPos = target != null ? target.position : Vector3.zero;
            for (int i = 0; i < movers.Count; i++)
            {
                if (movers[i] == null) continue; // 애니메이션 도중 다른 경로로 파괴됐을 가능성 방어
                movers[i].position = Vector3.Lerp(starts[i], targetPos, eased);
            }

            yield return null;
        }
    }

    private static void CollectMover(Collider col, Transform t, List<Transform> movers, List<Vector3> starts)
    {
        if (col != null) col.enabled = false;
        movers.Add(t);
        starts.Add(t.position);
    }

    // 정비 단계는 세 화면(AI 코어/로봇 정비/상점)이 순서대로 하나씩만 보여야 하므로,
    // 다음 화면을 열기 전에 항상 나머지를 닫는다.
    private void CloseAllIntermissionPanels()
    {
        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
        if (moddingPanel != null) moddingPanel.Close();
        if (shopPanel != null) shopPanel.Close();
    }

    // 대기 중인 AI 코어 업그레이드 선택이 있으면 그것부터 전부 처리하고,
    // 그 다음 부품 상자가 있으면 로봇 정비 화면을, 없으면 바로 상점을 연다.
    private void ShowNextIntermissionStep()
    {
        if (RunState.PendingCoreUpgradeChoices > 0 && aiCoreManager != null && aiCoreUpgradePanel != null)
        {
            ShowAiCoreUpgradeStep();
        }
        else if (RunState.UnopenedPartBoxCount > 0 && moddingPanel != null)
        {
            ShowModdingStep();
        }
        else
        {
            ShowShop();
        }
    }

    private void ShowAiCoreUpgradeStep()
    {
        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);

        EnsureAiCoreExtraButtons();

        // 카드 화면을 새로 열 때마다 리롤 누적 비용을 기본값으로 되돌린다(사용자 확정).
        aiCoreManager.ResetRerollCount();
        if (aiCoreExtraButtons != null) aiCoreExtraButtons.SetMessage(string.Empty);

        DrawAndRenderAiCoreChoices();

        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(true);
    }

    // 카드 3장만 다시 뽑아 그린다. 리롤은 패널을 닫았다 여는 게 아니라 이 부분만 반복하므로
    // ShowAiCoreUpgradeStep()에서 분리했다(CloseAllIntermissionPanels가 패널을 꺼버리기 때문).
    private void DrawAndRenderAiCoreChoices()
    {
        Button[] buttons = { option1Button, option2Button, option3Button };
        TextMeshProUGUI[] texts = { option1Text, option2Text, option3Text };

        List<AiCoreManager.UpgradeChoice> choices = aiCoreManager.DrawChoices(buttons.Length);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;

            button.onClick.RemoveAllListeners();

            if (i < choices.Count)
            {
                // 등급까지 확정된 카드다(2026-08-13) - 문구도 등급 색상 + 그 등급의 실제 증가량으로 만든다
                AiCoreManager.UpgradeChoice choice = choices[i];
                if (texts[i] != null) texts[i].text = choice.BuildLabel();
                button.gameObject.SetActive(true);
                button.onClick.AddListener(() => HandleUpgradeChosen(choice));
            }
            else
            {
                button.gameObject.SetActive(false);
            }
        }

        RefreshAiCoreExtraButtons();
    }

    private void RefreshAiCoreExtraButtons()
    {
        if (aiCoreExtraButtons == null || aiCoreManager == null) return;

        int cost = aiCoreManager.CurrentRerollCost;
        aiCoreExtraButtons.Refresh(cost, RunState.Gold >= cost, RunState.Gold);
    }

    private void HandleAiCoreRerollClicked()
    {
        if (aiCoreManager == null) return;

        if (!aiCoreManager.TryReroll())
        {
            if (aiCoreExtraButtons != null)
                aiCoreExtraButtons.SetMessage($"골드가 부족합니다 (리롤 {aiCoreManager.CurrentRerollCost}골드)");
            return;
        }

        if (aiCoreExtraButtons != null) aiCoreExtraButtons.SetMessage(string.Empty);
        DrawAndRenderAiCoreChoices();
    }

    private void HandleUpgradeChosen(AiCoreManager.UpgradeChoice choice)
    {
        aiCoreManager.ApplyChoice(choice);

        if (aiCoreUpgradePanel != null) aiCoreUpgradePanel.SetActive(false);
        ShowNextIntermissionStep(); // 레벨업이 여러 번 밀려있으면 다음 선택 카드를 이어서 보여준다
    }

    private void ShowModdingStep()
    {
        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);
        moddingPanel.Open();
    }

    // 로봇 정비 화면의 "상점으로" 버튼이 눌렸을 때. 부품 상자가 남아있어도 강제로 다 열게
    // 하지 않고 바로 상점으로 넘어갈 수 있다(다음 웨이브 정비 때 다시 열 수 있다).
    private void HandleModdingProceedRequested() => ShowShop();

    private void ShowShop()
    {
        if (shopPanel == null)
        {
            Debug.LogWarning($"상점 패널이 연결되지 않아 웨이브 {lastEndedWaveNumber} 종료 후 화면을 띄우지 못했습니다.");
            return;
        }

        CloseAllIntermissionPanels();
        SetCombatHudVisible(false);
        shopPanel.Open();
    }

    private void HandleNextWaveRequested()
    {
        CurrentState = State.Combat;
        IsIntermission = false;
        SetCombatHudVisible(true);
        Time.timeScale = 1f; // 정지 해제 - freezeTimeDuringIntermission이 꺼져 있어도 안전한 값

        if (waveManager != null) waveManager.StartNextWave();
    }
}
