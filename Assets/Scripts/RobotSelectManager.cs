using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RobotSelectManager : MonoBehaviour
{
    [Serializable]
    public struct RobotButtonEntry
    {
        public int robotId;
        public Button button;
    }

    [Tooltip("로봇ID와 선택 버튼을 인스펙터에서 연결")]
    [SerializeField] private List<RobotButtonEntry> robotButtons = new List<RobotButtonEntry>();

    [Header("선택 시 갱신되는 우측 패널")]
    [SerializeField] private Image panelImage;
    [SerializeField] private TMP_Text panelNameText;

    [Header("출발 버튼")]
    [SerializeField] private Button chulbalButton;
    [SerializeField] private string nextSceneName = "Ground01";

    private int selectedRobotId = -1;

    private void Start()
    {
        foreach (var entry in robotButtons)
        {
            int id = entry.robotId; // 람다 클로저 캡처 방지용 로컬 변수
            entry.button.onClick.AddListener(() => OnSelectRobot(id));
        }

        chulbalButton.onClick.AddListener(OnChulbal);
    }

    private void OnSelectRobot(int robotId)
    {
        selectedRobotId = robotId;

        if (GameDataManager.Instance.Robots.TryGetValue(robotId, out RobotData data))
        {
            panelNameText.text = data.robot_name;
            // 로봇별 이미지를 따로 관리한다면 여기서 panelImage.sprite 갱신
        }
    }

    private void OnChulbal()
    {
        if (selectedRobotId < 0)
        {
            Debug.LogWarning("로봇을 먼저 선택해주세요.");
            return;
        }

        PlayerSession.SelectedRobotId = selectedRobotId;
        SceneManager.LoadScene(nextSceneName);
    }
}
