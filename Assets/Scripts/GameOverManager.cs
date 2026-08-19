using System;
using UnityEngine;

/// <summary>
/// 아주 단순한 게임오버 상태 관리자.
///
/// 로봇 체력(CurrentHp)이 0이 되면 PlayerRobotController가 TriggerGameOver()를 호출해
/// IsGameOver가 true로 바뀐다. PlayerShootManager 등 다른 스크립트는 매 프레임 IsGameOver를
/// 확인해서, 게임오버 이후에는 입력(발사 등)을 더 이상 처리하지 않도록 한다.
///
/// 실제 게임오버 UI/연출/재시작 화면은 아직 없으므로, 필요해지면 OnGameOver 이벤트에
/// UI 스크립트를 구독시키면 된다. 지금은 콘솔 로그만 남긴다.
/// </summary>
public static class GameOverManager
{
    public static bool IsGameOver { get; private set; }
    public static event Action OnGameOver;

    public static void TriggerGameOver()
    {
        if (IsGameOver) return; // 중복 호출 방지
        IsGameOver = true;

        // 이번 판에서 쌓인 해금 진행도를 여기서 디스크에 쓴다(2026-08-19 Phase E).
        // UnlockState는 처치 1마리마다 저장하지 않고 더티 표시만 해 두므로, 웨이브 도중에
        // 죽으면 그 웨이브의 진행도가 저장되지 않은 채로 남는다.
        UnlockState.Flush();

        Debug.Log("=== 1회차 게임 오버 ===");
        OnGameOver?.Invoke();
    }

    // 씬을 재시작(재시도)할 때 이 상태를 초기화하기 위해 사용.
    // static 이라 씬을 다시 로드해도 값이 남아있기 때문에, Player가 새로 생성될 때(Awake) 호출해준다.
    // OnGameOver는 여기서 null로 비우지 않는다 - RunState.Reset()과 같은 이유(작업.md Phase 2
    // "RunState.OnChanged 초기화 순서 버그" 참고, GameWinManager.Reset()에서 실제로 재현되어
    // 함께 수정함): Unity의 Awake 호출 순서가 오브젝트별로 보장되지 않아, 구독자(GameHUD 등)의
    // Awake가 이 Reset()보다 먼저 실행되면 그 구독이 여기서 지워져 버릴 수 있다. 각 구독자는
    // 자신의 OnDestroy에서 스스로 구독 해제하므로 여기서 강제로 비울 필요가 없다.
    public static void Reset()
    {
        IsGameOver = false;
    }
}
