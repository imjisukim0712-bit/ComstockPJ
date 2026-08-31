using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

// 현재 프로젝트 설정 그대로 별도 웹 배포 폴더를 만든다.
public static class Probe
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("플레이 모드를 종료한 뒤 빌드하세요.");
        // 백그라운드 에디터에서는 delayCall이 멈출 수 있어 도구 요청 안에서 직접 빌드한다.
        if (EditorApplication.delayCall != null)
            foreach (EditorApplication.CallbackFunction callback in EditorApplication.delayCall.GetInvocationList())
                if (callback.Method.DeclaringType.Name == "Probe" && callback.Method.Name == "Build")
                    EditorApplication.delayCall -= callback;
        Build();
        return "웹빌드 종료 — build-result.txt 확인";
    }

    static void Build()
    {
        string output = "Builds/ComstockWebUI";
        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.WebGL,
                locationPathName = output,
                options = BuildOptions.None
            });
            File.WriteAllText("dev/web-ui/build-result.txt",
                "결과: " + report.summary.result + "\n오류: " + report.summary.totalErrors +
                "\n경고: " + report.summary.totalWarnings + "\n용량: " + report.summary.totalSize +
                "\n소요: " + report.summary.totalTime + "\n폴더: " + Path.GetFullPath(output));
        }
        catch (Exception e) { File.WriteAllText("dev/web-ui/build-result.txt", e.ToString()); }
    }
}
