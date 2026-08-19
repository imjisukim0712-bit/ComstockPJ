using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 조이스틱 악세사리의 해금 조건인 코나미 커맨드(위 위 아래 아래 좌 우 좌 우 스페이스 스페이스)를
/// 감지한다(2026-08-19 Phase E). <see cref="PlayerRobotController"/>가 <see cref="AccessoryVisual"/>과
/// 같은 관례로 Awake에서 자동 부착하므로 씬 수정이 필요 없다.
///
/// 엔드리스 모드에서만 인정되지만(<see cref="UnlockTracker.ReportKonamiCode"/>가 확인한다) 입력
/// 자체는 항상 받아둔다 - 엔드리스인지 아닌지를 매 입력마다 따지면 "엔드리스 진입 직전에 절반쯤
/// 입력해 둔" 애매한 상태가 생긴다.
///
/// 입력 방식은 프로젝트 관례대로 <see cref="Keyboard.current"/> 직접 폴링이다
/// (InputSystem_Actions 에셋은 이 프로젝트에서 쓰지 않는다 - 프로젝트 안내.md "알려진 이슈").
/// </summary>
[DisallowMultipleComponent]
public class KonamiCodeListener : MonoBehaviour
{
    private static readonly Key[] Sequence =
    {
        Key.UpArrow, Key.UpArrow,
        Key.DownArrow, Key.DownArrow,
        Key.LeftArrow, Key.RightArrow,
        Key.LeftArrow, Key.RightArrow,
        Key.Space, Key.Space
    };

    /// <summary>입력이 끊긴 것으로 보고 처음부터 다시 받는 시간(초). 커맨드 중간에 한참 멈추면
    /// 다음 우연한 입력이 이어붙는 것을 막는다.</summary>
    private const float ResetAfterIdleSeconds = 3f;

    private int matchedCount;
    private float lastInputTime;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (matchedCount > 0 && Time.unscaledTime - lastInputTime > ResetAfterIdleSeconds) matchedCount = 0;

        if (!TryGetPressedKey(keyboard, out Key pressed)) return;

        lastInputTime = Time.unscaledTime;

        if (pressed == Sequence[matchedCount])
        {
            matchedCount++;
            if (matchedCount < Sequence.Length) return;

            matchedCount = 0;
            UnlockTracker.ReportKonamiCode();
            return;
        }

        // 틀렸으면 처음부터. 단 방금 누른 키가 첫 글자면 그것부터 다시 세기 시작한다
        // (위위위아래... 처럼 같은 키가 연달아 오는 입력을 놓치지 않기 위함).
        matchedCount = pressed == Sequence[0] ? 1 : 0;
    }

    /// <summary>이번 프레임에 새로 눌린 커맨드 관련 키 하나. 여러 개가 동시에 눌리면 순서상 앞의 것.</summary>
    private static bool TryGetPressedKey(Keyboard keyboard, out Key key)
    {
        if (keyboard.upArrowKey.wasPressedThisFrame) { key = Key.UpArrow; return true; }
        if (keyboard.downArrowKey.wasPressedThisFrame) { key = Key.DownArrow; return true; }
        if (keyboard.leftArrowKey.wasPressedThisFrame) { key = Key.LeftArrow; return true; }
        if (keyboard.rightArrowKey.wasPressedThisFrame) { key = Key.RightArrow; return true; }
        if (keyboard.spaceKey.wasPressedThisFrame) { key = Key.Space; return true; }

        key = default;
        return false;
    }
}
