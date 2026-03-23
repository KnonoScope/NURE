using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RuneMapRelinker
{
    private const string MenuPath = "Tools/NURE/Relink Rune 125K FBX Into Wrapper";
    private const string WrapperPrefabPath = "Assets/Rune_125K_20x4096.prefab";
    private const string PreferredModelPath = "Assets/Rune_125K_20x4096.fbx";
    private const string PlaceholderChildName = "Visual";
    private const string ImportedChildName = "ImportedModel";
    private const string ModelNameToken = "Rune_125K_20x4096";

    [MenuItem(MenuPath, priority = 2010)]
    public static void RelinkRuneMapModel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Rune relink aborted: Unity is in Play Mode.");
            return;
        }

        GameObject wrapperAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPrefabPath);
        if (wrapperAsset == null)
        {
            Debug.LogError($"Rune relink aborted: wrapper prefab not found at {WrapperPrefabPath}.");
            return;
        }

        string modelPath = FindRuneModelPath();
        if (string.IsNullOrEmpty(modelPath))
        {
            Debug.LogError("Rune relink aborted: no valid FBX named Rune_125K_20x4096 was found in Assets.");
            return;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"Rune relink aborted: failed to load model at {modelPath}.");
            return;
        }

        GameObject prefabRoot = null;

        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(WrapperPrefabPath);
            Transform root = prefabRoot != null ? prefabRoot.transform : null;
            if (root == null)
            {
                Debug.LogError("Rune relink aborted: wrapper prefab root could not be loaded.");
                return;
            }

            RemoveChildIfExists(root, PlaceholderChildName);
            RemoveChildIfExists(root, ImportedChildName);

            var importedInstance = PrefabUtility.InstantiatePrefab(modelAsset, prefabRoot.scene) as GameObject;
            if (importedInstance == null)
            {
                Debug.LogError($"Rune relink aborted: failed to instantiate model {modelPath}.");
                return;
            }

            importedInstance.name = ImportedChildName;

            Transform importedTransform = importedInstance.transform;
            importedTransform.SetParent(root, false);
            importedTransform.SetAsFirstSibling();
            importedTransform.localPosition = Vector3.zero;
            importedTransform.localRotation = Quaternion.identity;
            importedTransform.localScale = Vector3.one;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, WrapperPrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Rune relink completed. Wrapper updated with model: {modelPath}");
        }
        finally
        {
            if (prefabRoot != null)
                PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateRelinkRuneMapModel()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
               AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPrefabPath) != null;
    }

    private static string FindRuneModelPath()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PreferredModelPath) != null)
            return PreferredModelPath;

        string[] guids = AssetDatabase.FindAssets($"{ModelNameToken} t:Model");
        string bestPath = null;
        DateTime bestWriteTime = DateTime.MinValue;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(fileName, ModelNameToken, StringComparison.OrdinalIgnoreCase))
                continue;

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                continue;

            DateTime writeTime = File.GetLastWriteTime(fullPath);
            if (writeTime > bestWriteTime)
            {
                bestWriteTime = writeTime;
                bestPath = path;
            }
        }

        return bestPath;
    }

    private static void RemoveChildIfExists(Transform root, string childName)
    {
        Transform child = root.Cast<Transform>().FirstOrDefault(t => string.Equals(t.name, childName, StringComparison.Ordinal));
        if (child == null)
            return;

        if (child.gameObject != null)
            UnityEngine.Object.DestroyImmediate(child.gameObject);
    }
}
