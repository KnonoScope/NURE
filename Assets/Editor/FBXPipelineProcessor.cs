using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FBXPipelineProcessor
{
    private const string MenuPath = "Tools/NURE/Process Latest FBX";
    private const string ExtractedFolderName = "_Extracted";
    private const string TexturesFolderName = "Textures";
    private const string MaterialsFolderName = "Materials";
    private const string UrpUnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string FallbackUnlitShaderName = "Unlit/Texture";

    [MenuItem(MenuPath, priority = 2000)]
    public static void ProcessLatestFbxMenu()
    {
        ProcessLatestFbxPipeline();
    }

    public static void ProcessLatestFbxPipeline()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("FBX pipeline aborted: Unity is in Play Mode.");
            return;
        }

        try
        {
            AutoFixConsoleIssues();
        }
        catch
        {
            // Best-effort only. Silent by requirement.
        }

        string fbxPath = FindLatestFbxPath();
        if (string.IsNullOrEmpty(fbxPath))
        {
            Debug.LogError("FBX pipeline aborted: no FBX found in Assets.");
            return;
        }

        string fbxFolder = Path.GetDirectoryName(fbxPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(fbxFolder))
        {
            Debug.LogError("FBX pipeline aborted: invalid FBX path.");
            return;
        }

        string extractedFolder = EnsureFolder(fbxFolder, ExtractedFolderName);
        string texturesFolder = EnsureFolder(extractedFolder, TexturesFolderName);
        string materialsFolder = EnsureFolder(extractedFolder, MaterialsFolderName);

        ExtractTexturesAndMaterials(fbxPath, texturesFolder, materialsFolder);
        ConvertMaterialsToUrpUnlit(materialsFolder);
        ConfigureTextures(texturesFolder);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void AutoFixConsoleIssues()
    {
        List<EditorLogEntry> entries = EditorConsoleReader.GetEntries();
        if (entries.Count == 0)
        {
            return;
        }

        bool hasMissingScript = entries.Any(e => e.message.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                e.message.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0);
        bool hasMissingShader = entries.Any(e => e.message.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                 (e.message.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  e.message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                  e.message.IndexOf("internalerrorshader", StringComparison.OrdinalIgnoreCase) >= 0));
        List<string> nullRefTypes = entries
            .Where(e => e.type == LogType.Exception && e.message.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0)
            .SelectMany(e => ExtractTypesFromStacktrace(e.stackTrace))
            .Distinct()
            .ToList();

        if (hasMissingScript)
        {
            RemoveMissingScriptsInOpenScenes();
            RemoveMissingScriptsInPrefabs();
        }

        if (hasMissingShader)
        {
            FixMissingShaderMaterials();
        }

        if (nullRefTypes.Count > 0)
        {
            DisableProblemComponents(nullRefTypes);
        }
    }

    private static string FindLatestFbxPath()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model");
        string latestPath = null;
        DateTime latestTime = DateTime.MinValue;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            DateTime writeTime = File.GetLastWriteTime(fullPath);
            if (writeTime > latestTime)
            {
                latestTime = writeTime;
                latestPath = path;
            }
        }

        return latestPath;
    }

    private static string EnsureFolder(string parentFolder, string childFolder)
    {
        string combined = (parentFolder + "/" + childFolder).Replace("//", "/");
        if (!AssetDatabase.IsValidFolder(combined))
        {
            AssetDatabase.CreateFolder(parentFolder, childFolder);
        }
        return combined;
    }

    private static void ExtractTexturesAndMaterials(string fbxPath, string texturesFolder, string materialsFolder)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        try
        {
            importer.ExtractTextures(texturesFolder);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
        }
        catch
        {
            // Silent best-effort.
        }

        try
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialSearch = ModelImporterMaterialSearch.Local;
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

            MoveExtractedMaterialsToFolder(fbxPath, materialsFolder);
        }
        catch
        {
            // Silent best-effort.
        }
    }

    private static void MoveExtractedMaterialsToFolder(string fbxPath, string materialsFolder)
    {
        string fbxFolder = Path.GetDirectoryName(fbxPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(fbxFolder))
        {
            return;
        }

        string[] deps = AssetDatabase.GetDependencies(fbxPath, false);
        foreach (string dep in deps)
        {
            if (!dep.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (dep.StartsWith(materialsFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!dep.StartsWith(fbxFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(dep);
            string targetPath = (materialsFolder + "/" + fileName).Replace("//", "/");
            if (dep == targetPath)
            {
                continue;
            }

            string uniqueTarget = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            AssetDatabase.MoveAsset(dep, uniqueTarget);
        }
    }

    private static void ConvertMaterialsToUrpUnlit(string materialsFolder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { materialsFolder });
        Shader urpUnlit = Shader.Find(UrpUnlitShaderName);
        Shader fallback = Shader.Find(FallbackUnlitShaderName);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                continue;
            }

            Undo.RegisterCompleteObjectUndo(mat, "Convert Material To URP Unlit");

            Texture mainTex = null;
            Vector2 scale = Vector2.one;
            Vector2 offset = Vector2.zero;

            if (mat.HasProperty("_BaseMap"))
            {
                mainTex = mat.GetTexture("_BaseMap");
                scale = mat.GetTextureScale("_BaseMap");
                offset = mat.GetTextureOffset("_BaseMap");
            }
            else if (mat.HasProperty("_MainTex"))
            {
                mainTex = mat.GetTexture("_MainTex");
                scale = mat.GetTextureScale("_MainTex");
                offset = mat.GetTextureOffset("_MainTex");
            }

            if (urpUnlit != null)
            {
                mat.shader = urpUnlit;
            }
            else if (fallback != null)
            {
                mat.shader = fallback;
            }
            else
            {
                Debug.LogError("FBX pipeline error: no unlit shader found.");
                continue;
            }

            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
    }

    private static void ConfigureTextures(string texturesFolder)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { texturesFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            bool isNormal = IsNormalMap(path, importer);
            importer.maxTextureSize = 4096;

            if (isNormal)
            {
                importer.textureType = TextureImporterType.NormalMap;
            }

            importer.SaveAndReimport();
        }
    }

    private static bool IsNormalMap(string path, TextureImporter importer)
    {
        if (importer.textureType == TextureImporterType.NormalMap)
        {
            return true;
        }

        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return name.Contains("_n") || name.Contains("_normal") || name.Contains("normal") || name.Contains("norm");
    }

    private static void RemoveMissingScriptsInOpenScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                RemoveMissingScriptsRecursive(root);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static void RemoveMissingScriptsInPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            bool changed = RemoveMissingScriptsRecursive(prefabRoot);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool RemoveMissingScriptsRecursive(GameObject go)
    {
        bool changed = false;
        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0)
        {
            Undo.RegisterCompleteObjectUndo(go, "Remove Missing Scripts");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            changed = true;
        }

        foreach (Transform child in go.transform)
        {
            if (RemoveMissingScriptsRecursive(child.gameObject))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static void FixMissingShaderMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        Shader urpUnlit = Shader.Find(UrpUnlitShaderName);
        Shader fallback = Shader.Find(FallbackUnlitShaderName);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                continue;
            }

            if (mat.shader == null || mat.shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Undo.RegisterCompleteObjectUndo(mat, "Fix Missing Shader");
                if (urpUnlit != null)
                {
                    mat.shader = urpUnlit;
                }
                else if (fallback != null)
                {
                    mat.shader = fallback;
                }
                else
                {
                    Debug.LogError("FBX pipeline error: no unlit shader found.");
                    continue;
                }
                EditorUtility.SetDirty(mat);
            }
        }
    }

    private static void DisableProblemComponents(List<string> typeNames)
    {
        if (typeNames == null || typeNames.Count == 0)
        {
            return;
        }

        Dictionary<string, Type> map = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
            .Where(t => typeNames.Contains(t.Name))
            .ToDictionary(t => t.Name, t => t);

        foreach (string typeName in typeNames)
        {
            if (!map.TryGetValue(typeName, out Type type))
            {
                continue;
            }

            if (!ShouldDisableInEditor(type))
            {
                continue;
            }

            DisableComponentsInOpenScenes(type);
            DisableComponentsInPrefabs(type);
        }
    }

    private static bool ShouldDisableInEditor(Type type)
    {
        if (type.GetCustomAttributes(typeof(ExecuteAlways), true).Length > 0)
        {
            return true;
        }

        if (type.GetCustomAttributes(typeof(ExecuteInEditMode), true).Length > 0)
        {
            return true;
        }

        MethodInfo onValidate = type.GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return onValidate != null;
    }

    private static void DisableComponentsInOpenScenes(Type type)
    {
        UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(type);
        foreach (UnityEngine.Object obj in objects)
        {
            if (obj is Behaviour behaviour && behaviour.gameObject.scene.IsValid())
            {
                Undo.RegisterCompleteObjectUndo(behaviour, "Disable Component");
                behaviour.enabled = false;
                EditorUtility.SetDirty(behaviour);
            }
        }
    }

    private static void DisableComponentsInPrefabs(Type type)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            Component[] components = prefabRoot.GetComponentsInChildren(type, true);
            foreach (Component component in components)
            {
                if (component is Behaviour behaviour)
                {
                    Undo.RegisterCompleteObjectUndo(behaviour, "Disable Component");
                    behaviour.enabled = false;
                    changed = true;
                }
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static List<string> ExtractTypesFromStacktrace(string stackTrace)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrEmpty(stackTrace))
        {
            return result;
        }

        string[] lines = stackTrace.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            int atIndex = line.IndexOf("at ", StringComparison.Ordinal);
            int parenIndex = line.IndexOf("(", StringComparison.Ordinal);
            if (atIndex < 0 || parenIndex <= atIndex)
            {
                continue;
            }

            string methodFull = line.Substring(atIndex + 3, parenIndex - (atIndex + 3));
            int lastDot = methodFull.LastIndexOf('.');
            if (lastDot <= 0)
            {
                continue;
            }

            string typeFull = methodFull.Substring(0, lastDot);
            string typeName = typeFull.Split('.').Last();
            if (!string.IsNullOrEmpty(typeName))
            {
                result.Add(typeName);
            }
        }

        return result;
    }

    private struct EditorLogEntry
    {
        public string message;
        public string stackTrace;
        public LogType type;
    }

    private static class EditorConsoleReader
    {
        private static Type logEntriesType;
        private static Type logEntryType;
        private static MethodInfo getCountMethod;
        private static MethodInfo getEntryMethod;
        private static FieldInfo messageField;
        private static FieldInfo stackTraceField;
        private static FieldInfo typeField;

        public static List<EditorLogEntry> GetEntries()
        {
            List<EditorLogEntry> result = new List<EditorLogEntry>();

            EnsureReflection();
            if (logEntriesType == null || logEntryType == null || getCountMethod == null || getEntryMethod == null)
            {
                return result;
            }

            int count = (int)getCountMethod.Invoke(null, null);
            object logEntry = Activator.CreateInstance(logEntryType);

            for (int i = 0; i < count; i++)
            {
                getEntryMethod.Invoke(null, new[] { i, logEntry });

                EditorLogEntry entry = new EditorLogEntry
                {
                    message = messageField?.GetValue(logEntry) as string ?? string.Empty,
                    stackTrace = stackTraceField?.GetValue(logEntry) as string ?? string.Empty,
                    type = typeField != null ? (LogType)typeField.GetValue(logEntry) : LogType.Log
                };

                result.Add(entry);
            }

            return result;
        }

        private static void EnsureReflection()
        {
            if (logEntriesType != null)
            {
                return;
            }

            logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
            logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor.dll");

            if (logEntriesType == null || logEntryType == null)
            {
                return;
            }

            getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
            getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
            messageField = logEntryType.GetField("message");
            stackTraceField = logEntryType.GetField("stackTrace");
            typeField = logEntryType.GetField("type");
        }
    }
}

public class FBXPipelinePostprocessor : AssetPostprocessor
{
    private static readonly bool AutoTriggerEnabled = false;

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        bool hasRuneMapFbx = importedAssets.Any(path =>
            path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileNameWithoutExtension(path), "Rune_125K_20x4096", StringComparison.OrdinalIgnoreCase));

        bool hasRune250kFbx = Rune250kMaterialRelinker.ShouldHandleImportedAssets(importedAssets);

        if (hasRuneMapFbx)
        {
            EditorApplication.delayCall -= RuneMapRelinker.RelinkRuneMapModel;
            EditorApplication.delayCall += RuneMapRelinker.RelinkRuneMapModel;
        }

        if (hasRune250kFbx)
        {
            EditorApplication.delayCall -= Rune250kMaterialRelinker.FixRune250kMaterialsAfterImport;
            EditorApplication.delayCall += Rune250kMaterialRelinker.FixRune250kMaterialsAfterImport;
        }

        if (!AutoTriggerEnabled)
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        bool hasFbx = importedAssets.Any(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
        if (!hasFbx)
        {
            return;
        }

        FBXPipelineProcessor.ProcessLatestFbxPipeline();
    }
}
