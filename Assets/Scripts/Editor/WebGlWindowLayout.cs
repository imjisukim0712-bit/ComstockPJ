using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>웹 출력 HTML에만 적용한다. PlayerSettings와 itch.io 페이지 설정은 변경하지 않는다.</summary>
public sealed class WebGlWindowLayout : IPostprocessBuildWithReport
{
    public int callbackOrder => 1000;
    private const string Marker = "<!-- Comstock: 창 크기에 맞춘 16:9 게임 영역 -->";

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.WebGL) Apply(report.summary.outputPath);
    }

    public static void Apply(string buildDirectory)
    {
        string path = Path.Combine(buildDirectory, "index.html");
        string html = File.ReadAllText(path);
        if (html.Contains(Marker)) return;
        if (!html.Contains("id=\"unity-canvas\"") || !html.Contains("</head>"))
            throw new BuildFailedException("웹 템플릿의 unity-canvas/head를 찾지 못해 창 크기 보정을 적용할 수 없습니다.");

        // 기본 템플릿의 고정 960×600 + 하단 38px 바가 itch.io iframe보다 커지는 문제를 해결한다.
        // CSS 표시 크기를 바꾸면 Unity 로더가 렌더 버퍼와 마우스 좌표를 함께 갱신한다.
        // 넓거나 높은 창에서도 PC와 같은 16:9 화면을 유지하고 남는 공간만 검게 채운다.
        string layout = Marker + @"
    <style>
      html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #101014; }
      #unity-container, #unity-container.unity-desktop, #unity-container.unity-mobile {
        position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
        width: min(100vw, calc(100vh * 16 / 9)); height: min(100vh, calc(100vw * 9 / 16));
      }
      #unity-canvas { display: block; width: 100% !important; height: 100% !important; outline: none; }
      /* 전체화면 전환은 게임의 설정 메뉴를 사용한다. 별도 버튼이 상점 카드를 덮지 않게 한다. */
      #unity-footer { display: none; }
      #unity-warning { box-sizing: border-box; max-width: 95%; max-height: 90%; overflow: auto; }
    </style>
";
        File.WriteAllText(path, html.Replace("</head>", layout + "</head>"));
        Debug.Log("웹 창모드: 16:9 자동 맞춤 적용 완료 — " + path);
    }
}
