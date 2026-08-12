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

    [Tooltip("음악 볼륨 슬라이더를 붙일 캔버스. 비워두면 씬의 첫 번째 캔버스를 찾아 쓴다")]
    [SerializeField] private RectTransform volumeSliderParent;

    private void Awake()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        AttachVolumeSlider();
    }

    /// <summary>
    /// 음악 볼륨 설정을 타이틀 화면에 붙인다(2026-08-13). 컨트롤은 씬에 배치하지 않고
    /// <see cref="MusicVolumeSliderUI"/>가 코드로 만든다 - 같은 컨트롤을 상점 화면에서도
    /// 쓰기 때문에 씬을 두 번 편집하지 않으려는 것이다.
    /// </summary>
    private void AttachVolumeSlider()
    {
        RectTransform parent = volumeSliderParent;
        if (parent == null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null) parent = canvas.transform as RectTransform;
        }
        if (parent == null) return;

        // 종료 버튼 아래의 빈 공간
        MusicVolumeSliderUI.Attach(parent, new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.10f));
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
