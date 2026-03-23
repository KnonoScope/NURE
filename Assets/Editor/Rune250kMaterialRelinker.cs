using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class Rune250kMaterialRelinker
{
    private const string MenuPath = "Tools/NURE/Fix Rune 250K Materials";
    private const string PrimaryMaterialsFolder = "Assets/Materials";
    private const string FallbackMaterialsFolder = "Assets/_Extracted/Materials";
    private const string SourcePrefix = "Rune_125K_20x4096";
    private const string TargetPrefix = "Rune_250K_20x4096";
    private const string ReimportGuardKeyPrefix = "Rune250kMaterialRelinker.SkipNextImport.";
    private static bool isRunning;

    [InitializeOnLoadMethod]
    private static void ScheduleAutoFix()
    {
        EditorApplication.delayCall -= AutoFixIfNeeded;
        EditorApplication.delayCall += AutoFixIfNeeded;
    }

    [MenuItem(MenuPath, priority = 2020)]
    public static void FixRune250kMaterials()
    {
        RunFix(logIfNoWork: true, forceModelRemap: true);
    }

    public static void FixRune250kMaterialsAfterImport()
    {
        RunFix(logIfNoWork: false, forceModelRemap: false);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateFixRune250kMaterials()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
               FindRune250kModelPaths().Length > 0;
    }

    public static bool IsRune250kModelPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = Path.GetFileNameWithoutExtension(path);
        return string.Equals(fileName, TargetPrefix, StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(TargetPrefix + " ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldHandleImportedAssets(string[] importedAssets)
    {
        bool shouldHandle = false;

        foreach (string path in importedAssets)
        {
            if (!IsRune250kModelPath(path))
                continue;

            if (ConsumeSkipNextImport(path))
                continue;

            shouldHandle = true;
        }

        return shouldHandle;
    }

    private static void AutoFixIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            return;

        if (FindRune250kModelPaths().Length == 0)
            return;

        if (CountTargetMaterials() >= CountSourceMaterials())
            return;

        RunFix(logIfNoWork: false, forceModelRemap: false);
    }

    private static void RunFix(bool logIfNoWork, bool forceModelRemap)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || isRunning)
            return;

        isRunning = true;

        try
        {
            string[] modelPaths = FindRune250kModelPaths();
            if (modelPaths.Length == 0)
            {
                if (logIfNoWork)
                    Debug.LogWarning("Rune 250K material fix aborted: no Rune_250K_20x4096 FBX found in Assets.");
                return;
            }

            string[] sourceMaterialPaths = FindSourceMaterialPaths();
            if (sourceMaterialPaths.Length == 0)
            {
                if (logIfNoWork)
                    Debug.LogError("Rune 250K material fix aborted: no Rune_125K_20x4096 source materials were found.");
                return;
            }

            int createdCount = 0;
            int renamedCount = 0;
            int importerUpdatedCount = 0;
            int remappedCount = 0;

            foreach (string sourcePath in sourceMaterialPaths)
            {
                string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
                string suffix = sourceName.Substring(SourcePrefix.Length);
                string targetName = TargetPrefix + suffix;
                string targetPath = $"{PrimaryMaterialsFolder}/{targetName}.mat";

                if (AssetDatabase.LoadAssetAtPath<Material>(targetPath) == null)
                {
                    if (AssetDatabase.CopyAsset(sourcePath, targetPath))
                        createdCount++;
                }

                Material targetMaterial = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (targetMaterial == null)
                    continue;

                if (!string.Equals(targetMaterial.name, targetName, StringComparison.Ordinal))
                {
                    targetMaterial.name = targetName;
                    EditorUtility.SetDirty(targetMaterial);
                    renamedCount++;
                }
            }

            AssetDatabase.SaveAssets();

            bool hasMaterialAssetChanges = createdCount > 0 || renamedCount > 0;

            foreach (string modelPath in modelPaths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null)
                    continue;

                bool importerChanged = false;

                if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importerChanged = true;
                }

                if (importer.materialLocation != ModelImporterMaterialLocation.External)
                {
                    importer.materialLocation = ModelImporterMaterialLocation.External;
                    importerChanged = true;
                }

                if (importer.materialName != ModelImporterMaterialName.BasedOnMaterialName)
                {
                    importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
                    importerChanged = true;
                }

                if (importer.materialSearch != ModelImporterMaterialSearch.Everywhere)
                {
                    importer.materialSearch = ModelImporterMaterialSearch.Everywhere;
                    importerChanged = true;
                }

                bool shouldAttemptRemap = forceModelRemap || hasMaterialAssetChanges || importerChanged;
                if (!shouldAttemptRemap)
                    continue;

                bool remapped = false;

                try
                {
                    remapped = importer.SearchAndRemapMaterials(
                        ModelImporterMaterialName.BasedOnMaterialName,
                        ModelImporterMaterialSearch.Everywhere);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Rune 250K material fix: remap failed for {modelPath}. {ex.Message}");
                }

                if (importerChanged || remapped)
                {
                    MarkSkipNextImport(modelPath);
                    importer.SaveAndReimport();
                    if (importerChanged)
                        importerUpdatedCount++;
                    if (remapped)
                        remappedCount++;
                }
            }

            AssetDatabase.SaveAssets();

            if (logIfNoWork || createdCount > 0 || renamedCount > 0 || importerUpdatedCount > 0 || remappedCount > 0)
            {
                Debug.Log(
                    "Rune 250K material fix completed. " +
                    $"Aliases created: {createdCount}, " +
                    $"renamed: {renamedCount}, " +
                    $"importers updated: {importerUpdatedCount}, " +
                    $"models remapped: {remappedCount}.");
            }
        }
        finally
        {
            isRunning = false;
        }
    }

    private static void MarkSkipNextImport(string path)
    {
        SessionState.SetBool(BuildReimportGuardKey(path), true);
    }

    private static bool ConsumeSkipNextImport(string path)
    {
        string key = BuildReimportGuardKey(path);
        bool shouldSkip = SessionState.GetBool(key, false);
        if (shouldSkip)
            SessionState.SetBool(key, false);
        return shouldSkip;
    }

    private static string BuildReimportGuardKey(string path)
    {
        return ReimportGuardKeyPrefix + path.Replace('\\', '/');
    }

    private static int CountSourceMaterials()
    {
        return FindSourceMaterialPaths().Length;
    }

    private static int CountTargetMaterials()
    {
        return FindMatchingMaterialPaths(PrimaryMaterialsFolder, TargetPrefix).Length;
    }

    private static string[] FindSourceMaterialPaths()
    {
        string[] primaryPaths = FindMatchingMaterialPaths(PrimaryMaterialsFolder, SourcePrefix);
        if (primaryPaths.Length > 0)
            return primaryPaths;

        return FindMatchingMaterialPaths(FallbackMaterialsFolder, SourcePrefix);
    }

    private static string[] FindRune250kModelPaths()
    {
        return AssetDatabase.FindAssets($"{TargetPrefix} t:Model")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsRune250kModelPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] FindMatchingMaterialPaths(string folderPath, string prefix)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
            return Array.Empty<string>();

        return AssetDatabase.FindAssets($"{prefix} t:Material", new[] { folderPath })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            .Where(path => IsCanonicalRuneMaterialName(Path.GetFileNameWithoutExtension(path), prefix))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsCanonicalRuneMaterialName(string materialName, string prefix)
    {
        if (string.Equals(materialName, prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = materialName.Substring(prefix.Length);
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }
}
