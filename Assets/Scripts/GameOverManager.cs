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
        Debug.Log("=== 1회차 게임 오버 ===");
        OnGameOver?.Invoke();
    }

    // 씬을 재시작(재시도)할 때 이 상태를 초기화하기 위해 사용.
    // static 이라 씬을 다시 로드해도 값이 남아있기 때문에, Player가 새로 생성될 때(Awake) 호출해준다.
    public static void Reset()
    {
        IsGameOver = false;
        OnGameOver = null;
    }
}
