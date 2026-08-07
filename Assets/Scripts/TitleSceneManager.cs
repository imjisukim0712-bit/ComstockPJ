using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 아주 단순한 타이틀 화면. "게임 시작" 버튼을 누르면 바로 플레이 씬(Ground01)으로 넘어간다.
///
/// 로봇 선택 화면(RobotSelectManager)은 아직 어떤 씬에도 배치되지 않은 미구현 상태라
/// (프로젝트 안내.md "구현 현황" 참고) 여기서 거치지 않는다 - Ground01의
/// PlayerRobotController.InitFromSession()이 PlayerSession.SelectedRobotId가 -1(미선택)이면
/// 테스트용 기본 로봇으로 자동 대체하므로 그대로 둬도 게임이 정상 시작된다.
/// </summary>
public class TitleSceneManager : MonoBehaviour
{
    [Tooltip("게임 시작 버튼을 누르면 로드할 씬 이름")]
    [SerializeField] private string nextSceneName = "Ground01";

    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void OnStartClicked()
    {
        SceneManager.LoadScene(nextSceneName);
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
