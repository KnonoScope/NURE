using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class MapPopupSceneAutoSync
{
    static MapPopupSceneAutoSync()
    {
        EditorApplication.delayCall -= SyncOpenPopupControllers;
        EditorApplication.delayCall += SyncOpenPopupControllers;
    }

    private static void SyncOpenPopupControllers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        MapPopupMenuController[] controllers = Object.FindObjectsByType<MapPopupMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (MapPopupMenuController controller in controllers)
        {
            if (controller == null)
                continue;

            controller.SyncAll();

            if (controller.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }
    }
}
