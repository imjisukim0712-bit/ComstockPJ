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

    [Header("랭킹 (2026-08-20)")]
    [SerializeField] private Button rankingButton;

    // 이 화면이 여러 번 켜져도(이론상) 랭킹에 중복 제출되지 않도록 막는 가드(2026-08-19 Phase C).
    private bool scoreSubmitted;
    private RankingPanelUI rankingPanel;

    private void Awake()
    {
        if (titleButton != null) titleButton.onClick.AddListener(GoToTitle);
        if (rankingButton != null) rankingButton.onClick.AddListener(OpenRanking);
    }

    /// <summary>"랭킹" - 연타 방어(닫을 때 파괴되므로 열려 있으면 다시 만들지 않는다).
    /// 방금 끝난 런의 맵(활성 씬)의 랭킹을 보여준다 - 맵마다 랭킹이 분리된다(2026-08-20).</summary>
    private void OpenRanking()
    {
        if (rankingPanel != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        RectTransform parent = canvas != null ? canvas.transform as RectTransform : null;
        string mapId = SceneManager.GetActiveScene().name;
        rankingPanel = RankingPanelUI.Attach(parent, mapId, () => rankingPanel = null);
    }

    private void OnEnable()
    {
        RefreshSummary();

        // 죽음도 엔드리스 런의 정식 종료 조건이다(사용자 확정 - "플레이어 사망도 점수 정산 화면
        // 으로 간다"). 정산 팝업의 "타이틀로"와 달리 계속/포기를 고를 필요는 없지만, 2026-08-20
        // 사용자 요청으로 제출 전에 닉네임만 입력받는다(로봇 이름으로 미리 채워져 있어 그냥
        // 확인만 눌러도 예전과 동일하게 동작한다). 엔드리스에 진입하지 않은 일반 사망(1~19웨이브)
        // 도 점수 자체는 유효하므로 함께 제출한다 - 낮은 점수는 랭킹에서 자연스럽게 아래로 밀린다.
        if (!scoreSubmitted)
        {
            scoreSubmitted = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform parent = canvas != null ? canvas.transform as RectTransform : null;
            // "다음에"(2026-08-25)를 누르면 제출 없이 팝업만 닫힌다 - 이 화면은 팝업 뒤에
            // 이어지는 흐름이 없어 onSkip 콜백이 필요 없다(요약 화면이 그대로 남는다).
            NicknameInputPopup.Attach(parent, RunScore.ResolveDefaultPlayerName(), RunScore.SubmitToLeaderboard);
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
            summaryHeaderText.text = Loc.T("summary.header", RunState.WaveNumber, RunState.Gold) + "\n" +
                                      Loc.T("score.total", RunScore.ComputeTotal().ToString("N0"));
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
            $"{Loc.T("summary.modding")}\n" +
            $"{Loc.T("modding.head")}: {GetRobotName(player)}\n" +
            $"{Loc.T("partslot.helmet")}: {PartLine(modding, PartSlot.Helmet)}\n" +
            $"{BuildWeaponSocketPartsBlock(modding, socketCount)}\n" +
            $"{Loc.T("partslot.armarmor")}: {PartLine(modding, PartSlot.ArmArmor)}\n" +
            $"{Loc.T("partslot.magneticcore")}: {PartLine(modding, PartSlot.MagneticCore)}\n" +
            $"{Loc.T("partslot.leg")}: {PartLine(modding, PartSlot.Leg)}\n" +
            $"{Loc.T("partslot.legarmor")}: {PartLine(modding, PartSlot.LegArmor)}\n" +
            $"{Loc.T("partslot.foot")}: {PartLine(modding, PartSlot.Foot)}\n" +
            $"{Loc.T("partslot.discslot")}: {PartLine(modding, PartSlot.DiscSlot)}\n" +
            $"{Loc.T("modding.weight")} {(modding != null ? modding.GetTotalWeight() : 0f):0.#} / " +
            $"{(modding != null ? modding.GetTotalWeightCapacity() : 0f):0.#}";
    }

    private void RefreshWeapons(PlayerShootManager shoot)
    {
        if (equippedWeaponsText == null) return;

        var lines = new List<string> { Loc.T("modding.equipped_weapons") };

        if (shoot == null)
        {
            lines.Add(Loc.T("common.no_info"));
        }
        else
        {
            for (int i = 0; i < shoot.SocketCount; i++)
            {
                lines.Add(shoot.TryGetSocketInfo(i, out WeaponData weapon, out ItemGrade grade)
                    ? $"{Loc.T("shop.socket_n", i + 1)}: <color={grade.ToColorHex()}>{grade.ToDisplayName()}</color> {weapon.Weapon()}"
                    : $"{Loc.T("shop.socket_n", i + 1)}: {Loc.T("common.empty")}");
            }
        }

        equippedWeaponsText.text = string.Join("\n", lines);
    }

    private void RefreshDiscs(ShopManager shop)
    {
        if (equippedDiscsText == null) return;

        int slotCount = shop != null ? shop.DiscSlotCount : 0;
        var lines = new List<string> { $"{Loc.T("modding.discs")} {RunState.EquippedDiscIds.Count}/{slotCount}" };

        if (RunState.EquippedDiscIds.Count == 0)
        {
            lines.Add(Loc.T("common.none_paren"));
        }
        else if (shop != null && shop.Catalog != null)
        {
            foreach (int discId in RunState.EquippedDiscIds)
            {
                string name = discId.ToString();
                foreach (DiscData disc in shop.Catalog.Discs)
                {
                    if (disc.discId != discId) continue;
                    name = $"<color={disc.grade.ToColorHex()}>{disc.grade.ToDisplayName()}</color> {disc.Disc()}";
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
            statsText.text = $"{Loc.T("summary.final_stats")}\n{Loc.T("common.no_info")}";
            return;
        }

        statsText.text =
            $"{Loc.T("summary.final_stats")}\n" +
            // 2026-08-24 사용자 지정 표기 규칙(StatFormat 참고).
            $"{StatTypeNames.ToDisplayName(StatType.MaxHp)} {StatFormat.Int(Mathf.Max(0f, player.CurrentHp))}/{StatFormat.Int(player.MaxHp)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Atk)} {StatFormat.Int(player.Atk)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Def)} {StatFormat.Int(player.Def)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritChance)} {StatFormat.Percent(player.Cc)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.CritDamage)} {StatFormat.RatioPercent(player.Cd)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.MoveSpeed)} {StatFormat.Decimal(player.MoveSpeed)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Avoid)} {StatFormat.Percent(player.Avoid)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Luck)} {StatFormat.Int(player.Luck)}\n" +
            $"{StatTypeNames.ToDisplayName(StatType.Mass)} {StatFormat.Decimal(player.Mess)}";
    }

    private static string PartLine(ModdingManager modding, PartSlot slot)
    {
        if (modding == null || !modding.TryGetEquippedPart(slot, out PartData part)) return Loc.T("common.none_paren");
        return $"<color={part.grade.ToColorHex()}>{part.grade.ToDisplayName()}</color> {part.Part()}";
    }

    // ShopPanelUI.BuildWeaponSocketPartsBlock과 동일 - 소켓마다 다른 파츠를 낄 수 있어
    // "무기 소켓: N칸" 한 줄로는 표현할 수 없다(2026-08-12 "무기 소켓 개별화" 플랜).
    private static string BuildWeaponSocketPartsBlock(ModdingManager modding, int socketCount)
    {
        var lines = new List<string>();

        for (int i = 0; i < socketCount; i++)
        {
            string part = modding != null && modding.TryGetEquippedWeaponSocketPart(i, out PartData socketPart)
                ? $"<color={socketPart.grade.ToColorHex()}>{socketPart.grade.ToDisplayName()}</color> {socketPart.Part()}"
                : Loc.T("common.none_paren");

            lines.Add($"{Loc.T("modding.weaponsocket_n", i + 1)}: {part}");
        }

        return lines.Count > 0 ? string.Join("\n", lines) : $"{Loc.T("partslot.weaponsocket")}: {Loc.T("common.none_paren")}";
    }

    private static string GetRobotName(PlayerRobotController player)
    {
        if (player == null || GameDataManager.Instance == null) return Loc.T("common.unknown");

        return GameDataManager.Instance.Robots.TryGetValue(player.RobotId, out RobotData data)
            ? data.Robot()
            : $"ID {player.RobotId}";
    }
}
