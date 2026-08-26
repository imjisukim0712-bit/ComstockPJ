using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F10을 누르면 현재 화면을 캡처해 저장한다(디버그/버그 리포트용).
///
/// <see cref="MusicManager"/>/<see cref="SFXManager"/>와 같은 관례로 씬에 배치하지 않고
/// <see cref="RuntimeInitializeOnLoadMethod"/>로 스스로 생성되어 <see cref="DontDestroyOnLoad"/>로
/// 씬 전환에도 살아남는다.
///
/// 저장 위치는 <see cref="Application.persistentDataPath"/> 아래 "Screenshots" 폴더다 -
/// 빌드 실행 파일 옆(<see cref="Application.dataPath"/> 상위)은 설치 위치에 따라 쓰기 권한이
/// 없을 수 있지만(예: Program Files), persistentDataPath는 항상 쓰기가 보장된다.
/// </summary>
public class ScreenshotHotkey : MonoBehaviour
{
    private static ScreenshotHotkey Instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("ScreenshotHotkey");
        go.AddComponent<ScreenshotHotkey>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.f10Key.wasPressedThisFrame) return;

        string folder = Path.Combine(Application.persistentDataPath, "Screenshots");
        Directory.CreateDirectory(folder);

        string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(folder, fileName);

        ScreenCapture.CaptureScreenshot(fullPath);
        Debug.Log($"[스크린샷] 저장됨: {fullPath}");
    }
}
