using UnityEditor;
using UnityEngine;

/// <summary>
/// 하이라키(GameObject 메뉴)에서 <see cref="DebugPartBox"/>를 바로 추가할 수 있게 한다.
/// </summary>
public static class DebugPartBoxMenu
{
    [MenuItem("GameObject/Comstock/디버그 부품상자", false, 10)]
    private static void Create(MenuCommand command)
    {
        GameObject go = new GameObject("DebugPartBox");
        go.AddComponent<DebugPartBox>();

        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);

        // 씬 뷰가 보고 있는 지점 근처에 놓아 만들자마자 눈에 보이게 한다
        if (SceneView.lastActiveSceneView != null)
            go.transform.position = SceneView.lastActiveSceneView.pivot;

        Undo.RegisterCreatedObjectUndo(go, "Create Debug Part Box");
        Selection.activeGameObject = go;
    }
}
