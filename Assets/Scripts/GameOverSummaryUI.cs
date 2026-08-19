using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 게임오버 화면에 이번 런의 최종 상태를 정리해서 보여준다: 도달 웨이브, 보유 골드,
/// 로봇 모딩 상태(장착 파츠 8부위 + 헤드), 장착 무기·디스크, 최종 스탯.
///
/// 표시 로직은 ShopPanelUI의 "로봇 모딩 상태/장착 무기/디스크/능력치" 섹션과 동일한 방식
/// (FindFirstObjectByType + 문자열 조립)을 그대로 따른다 - 이미 검증된 패턴을 재사용한다.
///
/// GameHUD가 GameOverManager.OnGameOver 시점에 이 오브젝트를 SetActive(true)로 켜므로,
/// OnEnable에서 한 번만 채운다(게임오버 이후에는 상태가 더 바뀌지 않으므로 매 프레임 갱신 불필요).
/// </summary>
public class GameOverSummaryUI : MonoBehaviour
{
    [Header("요약 텍스트")]
    [SerializeField] private TextMeshProUGUI summaryHeaderText; // "도달 웨이브 N / 보유 골드 G"
    [SerializeField] private TextMeshProUGUI moddingStatusText;
    [SerializeField] private TextMeshProUGUI equippedWeaponsText;
    [SerializeField] private TextMeshProUGUI equippedDiscsText;
    [SerializeField] private TextMeshProUGUI statsText;

    [Header("타이틀로 복귀")]
    [SerializeField] private Button titleButton;
    [SerializeField] private string titleSceneName = "Title";

    // 이 화면이 여러 번 켜져도(이론상) 랭킹에 중복 제출되지 않도록 막는 가드(2026-08-19 Phase C).
    private bool scoreSubmitted;

    private void Awake()
    {
        if (titleButton != null) titleButton.onClick.AddListener(GoToTitle);
    }

    private void OnEnable()
    {
        RefreshSummary();

        // 죽음도 엔드리스 런의 정식 종료 조건이다(사용자 확정 - "플레이어 사망도 점수 정산 화면
        // 으로 간다"). 정산 팝업의 "타이틀로"와 달리 사용자 선택이 필요 없으니(이미 죽어서 더 할
        // 수 있는 게 없다) 화면을 띄우는 시점에 바로 제출한다. 엔드리스에 진입하지 않은 일반
        // 사망(1~19웨이브)도 점수 자체는 유효하므로 함께 제출한다 - 낮은 점수는 랭킹에서
        // 자연스럽게 아래로 밀린다.
        if (!scoreSubmitted)
        {
            scoreSubmitted = true;
            RunScore.SubmitToLeaderboard();
        }
    }

    private void GoToTitle()
    {
        // 정비 중 사망 등으로 timeScale이 0으로 멈춰 있었을 수 있으므로 되돌려놓고 이동한다.
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    public void RefreshSummary()
    {
        PlayerRobotController player = FindFirstObjectByType<PlayerRobotController>();
        ModdingManager modding = FindFirstObjectByType<ModdingManager>();
        ShopManager shop = FindFirstObjectByType<ShopManager>();
        PlayerShootManager shoot = FindFirstObjectByType<PlayerShootManager>();

        if (summaryHeaderText != null)
        {
            // 2026-08-19 Phase C(점수 시스템) - 새 텍스트 필드를 씬에 늘리는 대신 기존 헤더에
            // 한 줄 붙인다(씬 수정 없이 반영 가능한 가장 간단한 자리).
            summaryHeaderText.text = $"도달 웨이브 {RunState.WaveNumber}     보유 골드 {RunState.Gold}\n" +
                                      $"총점 {RunScore.ComputeTotal():N0}";
        }

        RefreshModdingStatus(modding, shoot, player);
        RefreshWeapons(shoot);
        RefreshDiscs(shop);
        RefreshStats(player);
    }

    private void RefreshModdingStatus(ModdingManager modding, PlayerShootManager shoot, PlayerRobotController player)
    {
        if (moddingStatusText == null) return;

        int socketCount = shoot != null ? shoot.SocketCount : 0;

        moddingStatusText.text =
            "[로봇 모딩]\n" +
            $"헤드: {GetRobotName(player)}\n" +
            $"헬멧: {PartLine(modding, PartSlot.Helmet)}\n" +
            $"{BuildWeaponSocketPartsBlock(modding, socketCount)}\n" +
            $"팔 장갑: {PartLine(modding, PartSlot.ArmArmor)}\n" +
            $"자기장 코어: {PartLine(modding, PartSlot.MagneticCore)}\n" +
            $"다리: {PartLine(modding, PartSlot.Leg)}\n" +
            $"다리 장갑: {PartLine(modding, PartSlot.LegArmor)}\n" +
            $"발: {PartLine(modding, PartSlot.Foot)}\n" +
            $"디스크 슬롯: {PartLine(modding, PartSlot.DiscSlot)}\n" +
            $"무게 {(modding != null ? modding.GetTotalWeight() : 0f):0.#} / " +
            $"{(modding != null ? modding.GetTotalWeightCapacity() : 0f):0.#}";
    }

    private void RefreshWeapons(PlayerShootManager shoot)
    {
        if (equippedWeaponsText == null) return;

        var lines = new List<string> { "[장착 무기]" };

        if (shoot == null)
        {
            lines.Add("(정보 없음)");
        }
        else
        {
            for (int i = 0; i < shoot.SocketCount; i++)
            {
                lines.Add(shoot.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade)
                    ? $"소켓 {i + 1}: <color={grade.ToColorHex()}>{grade.ToKorean()}</color> {weapon.weapon_name}"
                    : $"소켓 {i + 1}: (비어 있음)");
            }
        }

        equippedWeaponsText.text = string.Join("\n", lines);
    }

    private void RefreshDiscs(ShopManager shop)
    {
        if (equippedDiscsText == null) return;

        int slotCount = shop != null ? shop.DiscSlotCount : 0;
        var lines = new List<string> { $"[디스크] {RunState.EquippedDiscIds.Count}/{slotCount}" };

        if (RunState.EquippedDiscIds.Count == 0)
        {
            lines.Add("(없음)");
        }
        else if (shop != null && shop.Catalog != null)
        {
            foreach (int discId in RunState.EquippedDiscIds)
            {
                string name = discId.ToString();
                foreach (DiscData disc in shop.Catalog.Discs)
                {
                    if (disc.discId != discId) continue;
                    name = $"<color={disc.grade.ToColorHex()}>{disc.grade.ToKorean()}</color> {disc.discName}";
                    break;
                }
                lines.Add(name);
            }
        }

        equippedDiscsText.text = string.Join("\n", lines);
    }

    private void RefreshStats(PlayerRobotController player)
    {
        if (statsText == null) return;

        if (player == null)
        {
            statsText.text = "[최종 능력치]\n(정보 없음)";
            return;
        }

        statsText.text =
            "[최종 능력치]\n" +
            $"체력 {Mathf.Max(0, player.CurrentHp)}/{player.MaxHp}\n" +
            $"공격력 {player.Atk}\n" +
            $"방어력 {player.Def}\n" +
            $"치명타 확률 {player.Cc:0.##}%\n" +
            $"치명타 피해 {player.Cd:0.##}\n" +
            $"이동속도 {player.MoveSpeed:0.##}\n" +
            $"회피율 {player.Avoid:0.##}\n" +
            $"행운 {player.Luck:0.##}\n" +
            $"질량 {player.Mess:0.##}";
    }

    private static string PartLine(ModdingManager modding, PartSlot slot)
    {
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return "(없음)";
        return $"<color={part.grade.ToColorHex()}>{part.grade.ToKorean()}</color> {part.partName}";
    }

    // ShopPanelUI.BuildWeaponSocketPartsBlock과 동일 - 소켓마다 다른 파츠를 낄 수 있어
    // "무기 소켓: N칸" 한 줄로는 표현할 수 없다(2026-08-12 "무기 소켓 개별화" 플랜).
    private static string BuildWeaponSocketPartsBlock(ModdingManager modding, int socketCount)
    {
        var lines = new List<string>();

        for (int i = 0; i < socketCount; i++)
        {
            string part = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out PartData socketPart)
                ? $"<color={socketPart.grade.ToColorHex()}>{socketPart.grade.ToKorean()}</color> {socketPart.partName}"
                : "(없음)";

            lines.Add($"무기 소켓 {i + 1}: {part}");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "무기 소켓: (없음)";
    }

    private static string GetRobotName(PlayerRobotController player)
    {
        if (player == null || GameDataManager.Instance == null) return "(알 수 없음)";

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.robot_name
            : $"ID {player.RobotId}";
    }
}
