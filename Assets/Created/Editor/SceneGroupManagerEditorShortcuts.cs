#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

public static class SceneGroupManagerEditorShortcuts
{
    [Shortcut("NURE/Scenes/1", KeyCode.F1)]
    private static void Scene1()
    {
        ActivateIndex(0);
    }

    [Shortcut("NURE/Scenes/2", KeyCode.F2)]
    private static void Scene2()
    {
        ActivateIndex(1);
    }

    [Shortcut("NURE/Scenes/3", KeyCode.F3)]
    private static void Scene3()
    {
        ActivateIndex(2);
    }

    [Shortcut("NURE/Scenes/4", KeyCode.F4)]
    private static void Scene4()
    {
        ActivateIndex(3);
    }

    [Shortcut("NURE/Scenes/5", KeyCode.F5)]
    private static void Scene5()
    {
        ActivateIndex(4);
    }

    [Shortcut("NURE/Scenes/6", KeyCode.F6)]
    private static void Scene6()
    {
        ActivateIndex(5);
    }

    [Shortcut("NURE/Scenes/7", KeyCode.F7)]
    private static void Scene7()
    {
        ActivateIndex(6);
    }

    [Shortcut("NURE/Scenes/8", KeyCode.F8)]
    private static void Scene8()
    {
        ActivateIndex(7);
    }

    [Shortcut("NURE/Scenes/9", KeyCode.F9)]
    private static void Scene9()
    {
        ActivateIndex(8);
    }

    [Shortcut("NURE/Scenes/10", KeyCode.F10)]
    private static void Scene10()
    {
        ActivateIndex(9);
    }

    [Shortcut("NURE/Scenes/Next", KeyCode.PageDown)]
    private static void Next()
    {
        ActivateRelative(+1);
    }

    [Shortcut("NURE/Scenes/Previous", KeyCode.PageUp)]
    private static void Previous()
    {
        ActivateRelative(-1);
    }

    private static void ActivateRelative(int delta)
    {
        if (!EditorApplication.isPlaying)
            return;

        SceneGroupManager mgr = Object.FindFirstObjectByType<SceneGroupManager>();
        if (mgr == null || mgr.scenes == null || mgr.scenes.Count == 0)
            return;

        int count = mgr.scenes.Count;
        int current = FindCurrentIndex(mgr);
        int next = current < 0 ? 0 : (current + delta + count) % count;
        ActivateIndex(next);
    }

    private static int FindCurrentIndex(SceneGroupManager mgr)
    {
        for (int i = 0; i < mgr.scenes.Count; i++)
        {
            SceneGroupManager.VirtualScene cfg = mgr.scenes[i];
            if (cfg != null && cfg.root != null && cfg.root.activeInHierarchy)
                return i;
        }

        return -1;
    }

    private static void ActivateIndex(int index)
    {
        if (!EditorApplication.isPlaying)
            return;

        SceneGroupManager mgr = Object.FindFirstObjectByType<SceneGroupManager>();
        if (mgr == null || mgr.scenes == null)
            return;

        if (index < 0 || index >= mgr.scenes.Count)
            return;

        SceneGroupManager.VirtualScene cfg = mgr.scenes[index];
        if (cfg == null || cfg.root == null)
            return;

        mgr.ActivateScene(cfg.root);
        Debug.Log($"[SceneHotkeys] Scene {index + 1} -> {cfg.root.name}");
    }
}
#endif
