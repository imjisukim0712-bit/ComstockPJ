using System;
using UnityEngine;

/// <summary>
/// 승리(게임 클리어) 상태 관리자. GameOverManager(패배)와 완전히 대칭되는 구조를 따른다 -
/// 이름을 하나로 합치지 않은 이유는 "게임오버"라는 이름이 패배를 뜻하는 게 자연스러워서다.
///
/// WaveManager가 마지막 웨이브(기본 10웨이브)의 보스를 처치하고 웨이브 타이머까지 끝나면
/// TriggerWin()을 호출한다. PlayerShootManager/EnemyUnit/EnemySpawner 등은 매 프레임
/// GameOverManager.IsGameOver와 함께 이 IsGameWon도 확인해서, 승리 이후에는 전투 관련 로직이
/// 더 이상 진행되지 않도록 한다.
/// </summary>
public static class GameWinManager
{
    public static bool IsGameWon { get; private set; }
    public static event Action OnGameWon;

    public static void TriggerWin()
    {
        if (IsGameWon) return; // 중복 호출 방지
        IsGameWon = true;
        Debug.Log("=== 게임 클리어 ===");
        OnGameWon?.Invoke();
    }

    // 씬을 재시작(재시도)할 때 이 상태를 초기화하기 위해 사용. PlayerRobotController.Awake()에서
    // 함께 호출한다. OnGameWon은 여기서 null로 비우지 않는다 - RunState.Reset()과 같은 이유
    // (작업.md Phase 2 "RunState.OnChanged 초기화 순서 버그" 참고): Unity의 Awake 호출 순서는
    // 오브젝트별로 보장되지 않아서, GameHUD.Awake()의 구독이 PlayerRobotController.Awake()보다
    // 먼저 실행되면 여기서 강제로 비울 때 그 구독이 지워져 버리는 문제가 실제로 재현됐다.
    // 각 구독자는 자신의 OnDestroy에서 스스로 구독 해제하므로 여기서 강제로 비울 필요가 없다.
    public static void Reset()
    {
        IsGameWon = false;
    }
}
