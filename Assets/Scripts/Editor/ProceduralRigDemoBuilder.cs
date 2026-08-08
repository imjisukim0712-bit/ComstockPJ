using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 절차적 관절 리그 데모 씬을 만드는 에디터 도구.
/// 메뉴: Comstock/절차적 관절 리그 데모 씬 생성
///
/// 기존 게임 씬(Ground01 등)은 전혀 건드리지 않는다. 데모 전용 씬만 새로 만든다.
/// </summary>
public static class ProceduralRigDemoBuilder
{
    private const string ScenePath = "Assets/Scenes/JointRigDemo.unity";

    [MenuItem("Comstock/절차적 관절 리그 데모 씬 생성")]
    public static void CreateDemoScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 2.0f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.42f, 0.47f, 0.53f);
        camGo.transform.position = new Vector3(0f, 0.95f, -10f);

        // 지면 표시용 얇은 바 (스프라이트 없이 Quad 대신 LineRenderer로 가볍게)
        GameObject ground = new GameObject("GroundLine");
        LineRenderer line = ground.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, new Vector3(-8f, 0f, 0f));
        line.SetPosition(1, new Vector3(8f, 0f, 0f));
        line.widthMultiplier = 0.03f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = new Color(0.2f, 0.22f, 0.25f);

        // 리그
        GameObject rigGo = new GameObject("ProceduralCharacter");
        rigGo.transform.position = Vector3.zero;
        rigGo.AddComponent<ProceduralCharacterRig>();
        rigGo.AddComponent<ProceduralRigDemo>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"데모 씬 생성 완료: {ScenePath} — 재생(Play)하면 리그가 걷습니다.");
        Selection.activeGameObject = rigGo;
    }
}
