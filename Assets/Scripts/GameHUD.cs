using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체력 UI(슬라이더 + 숫자 텍스트)와 게임오버 문구를 관리한다.
///
/// 사용법: Canvas 밑 아무 오브젝트(예: HP)에 이 스크립트를 붙이고,
/// 인스펙터의 Hp Value Text / Hp Slider / Game Over Object 세 슬롯에
/// 각각 Hp_value, Slider(Hp_Slider), GameOver 오브젝트를 드래그해서 연결하면 된다.
///
/// - 매 프레임 PlayerRobotController의 CurrentHp/MaxHp를 읽어 슬라이더와 텍스트를 실시간으로 갱신한다.
/// - 게임 시작 시 Game Over 오브젝트를 항상 비활성화한다(씬에 켜진 채로 저장돼 있어도 무시).
/// - 체력이 0 이하가 되면(GameOverManager.OnGameOver) Game Over 오브젝트를 활성화한다.
///   플레이어 이동/발사 정지는 PlayerRobotController.Die()와 PlayerShootManager가 이미 처리하므로
///   여기서는 UI 표시만 담당한다.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("체력 UI")]
    [Tooltip("체력 숫자를 표시할 TextMeshProUGUI (예: Hp_value) - '80 / 100' 형식으로 표시")]
    [SerializeField] private TextMeshProUGUI hpValueText;

    [Tooltip("체력 비율을 표시할 슬라이더 (예: Hp_Slider)")]
    [SerializeField] private Slider hpSlider;

    [Header("게임오버")]
    [Tooltip("체력이 0 이하가 되면 활성화할 오브젝트 (예: GameOver 텍스트)")]
    [SerializeField] private GameObject gameOverObject;

    private PlayerRobotController player;

    private void Awake()
    {
        if (gameOverObject != null) gameOverObject.SetActive(false); // 게임 시작 시 항상 비활성화

        FindPlayer();

        GameOverManager.OnGameOver += HandleGameOver;
    }

    private void OnDestroy()
    {
        GameOverManager.OnGameOver -= HandleGameOver;
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
    }

    private void HandleGameOver()
    {
        if (gameOverObject != null) gameOverObject.SetActive(true);
    }
}
