using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NureProjectWideMaterialTextureFix
{
    private const string MenuPath = "Tools/NURE/Fix All GO Materials + Textures (Project Wide)";
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string UrpUnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string LegacyDiffuseShaderName = "Standard";

    private enum TextureRole
    {
        BaseColor,
        Normal,
        MetallicOrMask,
        Occlusion,
        Emission,
        AlphaMask
    }

    [MenuItem(MenuPath, priority = 1600)]
    public static void RunProjectWideFix()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[NURE Fix] Abort: Unity is in Play Mode.");
            return;
        }

        Shader urpLit = Shader.Find(UrpLitShaderName);
        Shader urpUnlit = Shader.Find(UrpUnlitShaderName);
        Shader legacyFallback = Shader.Find(LegacyDiffuseShaderName);

        if (urpLit == null)
        {
            Debug.LogError($"[NURE Fix] Shader not found: {UrpLitShaderName}");
            return;
        }

        int fixedTextures = 0;
        int convertedMaterials = 0;
        int assignedBaseMaps = 0;
        int assignedNormalMaps = 0;
        int assignedMaskMaps = 0;
        int fixedImporters = 0;

        try
        {
            fixedTextures = FixAllTextureImporters();

            string[] materialGuids = AssetDatabase.FindAssets("t:Material");
            int totalMaterials = materialGuids.Length;

            for (int i = 0; i < totalMaterials; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                if (string.IsNullOrEmpty(path) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    continue;

                EditorUtility.DisplayProgressBar(
                    "NURE Project Fix",
                    $"Material: {mat.name}",
                    (float)i / Mathf.Max(1, totalMaterials)
                );

                bool changed = false;
                bool wantsUnlit = IsVideoMaterial(mat, path);
                Shader targetShader = wantsUnlit ? (urpUnlit != null ? urpUnlit : legacyFallback) : urpLit;
                if (targetShader == null)
                    continue;

                Texture main = GetFirstTexture(mat, "_BaseMap", "_MainTex", "_BaseColorMap", "_ColorMap");
                Texture normal = GetFirstTexture(mat, "_BumpMap", "_NormalMap");
                Texture metallic = GetFirstTexture(mat, "_MetallicGlossMap", "_MaskMap", "_SpecGlossMap");
                Texture occlusion = GetFirstTexture(mat, "_OcclusionMap");
                Texture emission = GetFirstTexture(mat, "_EmissionMap");
                Color baseColor = GetFirstColor(mat, "_BaseColor", "_Color");
                bool isTransparent = InferTransparent(mat, path);
                bool isCutout = InferCutout(mat, path);

                if (mat.shader != targetShader)
                {
                    mat.shader = targetShader;
                    changed = true;
                    convertedMaterials++;
                }

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", baseColor);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", baseColor);

                if (main == null)
                    main = FindBestTextureForMaterial(mat, path, TextureRole.BaseColor);
                if (normal == null)
                    normal = FindBestTextureForMaterial(mat, path, TextureRole.Normal);
                if (metallic == null)
                    metallic = FindBestTextureForMaterial(mat, path, TextureRole.MetallicOrMask);
                if (occlusion == null)
                    occlusion = FindBestTextureForMaterial(mat, path, TextureRole.Occlusion);
                if (emission == null)
                    emission = FindBestTextureForMaterial(mat, path, TextureRole.Emission);

                if (main != null)
                {
                    if (SetTextureIfPropertyExists(mat, "_BaseMap", main)) changed = true;
                    if (SetTextureIfPropertyExists(mat, "_MainTex", main)) changed = true;
                    assignedBaseMaps++;
                }

                if (normal != null)
                {
                    EnsureTextureIsNormalMap(normal);
                    if (SetTextureIfPropertyExists(mat, "_BumpMap", normal)) changed = true;
                    if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
                    assignedNormalMaps++;
                }

                if (metallic != null)
                {
                    if (SetTextureIfPropertyExists(mat, "_MetallicGlossMap", metallic)) changed = true;
                    if (SetTextureIfPropertyExists(mat, "_MaskMap", metallic)) changed = true;
                    assignedMaskMaps++;
                }

                if (occlusion != null)
                {
                    if (SetTextureIfPropertyExists(mat, "_OcclusionMap", occlusion)) changed = true;
                    if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 1f);
                }

                if (emission != null)
                {
                    if (SetTextureIfPropertyExists(mat, "_EmissionMap", emission)) changed = true;
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white);
                }

                if (!wantsUnlit)
                {
                    SetupUrpLitSurface(mat, isTransparent, isCutout);
                }

                if (changed)
                {
                    EditorUtility.SetDirty(mat);
                }
            }

            fixedImporters = FixAllModelImporters();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(
            "[NURE Fix] Completed. " +
            $"Textures fixed: {fixedTextures}, " +
            $"Materials converted: {convertedMaterials}, " +
            $"Base maps assigned: {assignedBaseMaps}, " +
            $"Normal maps assigned: {assignedNormalMaps}, " +
            $"Mask/metal maps assigned: {assignedMaskMaps}, " +
            $"Model importers updated: {fixedImporters}."
        );
    }

    private static int FixAllTextureImporters()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");
        int fixedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                continue;

            EditorUtility.DisplayProgressBar(
                "NURE Project Fix",
                $"Texture: {Path.GetFileName(path)}",
                (float)i / Mathf.Max(1, guids.Length)
            );

            bool changed = false;
            string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool isNormal = IsNormalName(lower);
            bool isMaskLike = IsMaskName(lower);
            bool needsAlphaTransparency = IsAlphaClipName(lower);

            if (importer.maxTextureSize < 4096)
            {
                importer.maxTextureSize = 4096;
                changed = true;
            }

            TextureImporterType targetType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != targetType)
            {
                importer.textureType = targetType;
                changed = true;
            }

            bool targetSRGB = !(isNormal || isMaskLike);
            if (importer.sRGBTexture != targetSRGB)
            {
                importer.sRGBTexture = targetSRGB;
                changed = true;
            }

            if (importer.alphaIsTransparency != needsAlphaTransparency)
            {
                importer.alphaIsTransparency = needsAlphaTransparency;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                fixedCount++;
            }
        }

        return fixedCount;
    }

    private static int FixAllModelImporters()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model");
        int fixedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                continue;

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                continue;

            bool changed = false;

            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                changed = true;
            }

            if (importer.materialLocation != ModelImporterMaterialLocation.External)
            {
                importer.materialLocation = ModelImporterMaterialLocation.External;
                changed = true;
            }

            if (importer.materialSearch != ModelImporterMaterialSearch.Local)
            {
                importer.materialSearch = ModelImporterMaterialSearch.Local;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                fixedCount++;
            }
        }

        return fixedCount;
    }

    private static void SetupUrpLitSurface(Material mat, bool transparent, bool cutout)
    {
        if (mat == null)
            return;

        if (transparent)
        {
            SetFloatIf(mat, "_Surface", 1f);
            SetFloatIf(mat, "_AlphaClip", 0f);
            SetFloatIf(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIf(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloatIf(mat, "_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return;
        }

        SetFloatIf(mat, "_Surface", 0f);
        SetFloatIf(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloatIf(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        SetFloatIf(mat, "_ZWrite", 1f);

        if (cutout)
        {
            SetFloatIf(mat, "_AlphaClip", 1f);
            SetFloatIf(mat, "_Cutoff", 0.4f);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else
        {
            SetFloatIf(mat, "_AlphaClip", 0f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }

    private static bool InferTransparent(Material mat, string materialPath)
    {
        string n = (mat.name + " " + materialPath).ToLowerInvariant();
        if (n.Contains("glass") || n.Contains("transparent") || n.Contains("transp") || n.Contains("water"))
            return true;
        if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent)
            return true;
        if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f)
            return true;
        return false;
    }

    private static bool InferCutout(Material mat, string materialPath)
    {
        string n = (mat.name + " " + materialPath).ToLowerInvariant();
        if (n.Contains("leaf") || n.Contains("leaves") || n.Contains("olive") || n.Contains("foliage") || n.Contains("branch") || n.Contains("tree"))
            return true;
        if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") > 0.5f)
            return true;
        if (mat.HasProperty("_Cutoff") && mat.GetFloat("_Cutoff") > 0f)
            return true;
        return false;
    }

    private static bool IsVideoMaterial(Material mat, string path)
    {
        string n = (mat.name + " " + path).ToLowerInvariant();
        if (n.Contains("video"))
            return true;

        Texture t = GetFirstTexture(mat, "_BaseMap", "_MainTex");
        return t is RenderTexture;
    }

    private static Texture GetFirstTexture(Material mat, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            string p = propertyNames[i];
            if (!mat.HasProperty(p))
                continue;
            Texture tex = mat.GetTexture(p);
            if (tex != null)
                return tex;
        }
        return null;
    }

    private static Color GetFirstColor(Material mat, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            string p = propertyNames[i];
            if (mat.HasProperty(p))
                return mat.GetColor(p);
        }
        return Color.white;
    }

    private static bool SetTextureIfPropertyExists(Material mat, string property, Texture tex)
    {
        if (mat.HasProperty(property) && mat.GetTexture(property) != tex)
        {
            mat.SetTexture(property, tex);
            return true;
        }
        return false;
    }

    private static void SetFloatIf(Material mat, string property, float value)
    {
        if (mat.HasProperty(property))
            mat.SetFloat(property, value);
    }

    private static void EnsureTextureIsNormalMap(Texture tex)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path))
            return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
    }

    private static Texture2D FindBestTextureForMaterial(Material mat, string materialPath, TextureRole role)
    {
        string folder = Path.GetDirectoryName(materialPath)?.Replace("\\", "/");
        string[] searchFolders = string.IsNullOrEmpty(folder)
            ? Array.Empty<string>()
            : new[] { folder };

        string[] candidates = searchFolders.Length > 0
            ? AssetDatabase.FindAssets("t:Texture2D", searchFolders)
            : Array.Empty<string>();

        Texture2D best = FindBestAmongGuids(mat.name, candidates, role);
        if (best != null)
            return best;

        string[] global = AssetDatabase.FindAssets("t:Texture2D");
        return FindBestAmongGuids(mat.name, global, role);
    }

    private static Texture2D FindBestAmongGuids(string materialName, string[] guids, TextureRole role)
    {
        if (guids == null || guids.Length == 0)
            return null;

        string[] tokens = TokenizeName(materialName);
        int bestScore = int.MinValue;
        Texture2D best = null;

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
                continue;

            string tName = tex.name.ToLowerInvariant();
            int score = 0;

            for (int t = 0; t < tokens.Length; t++)
            {
                if (tName.Contains(tokens[t]))
                    score += 5;
            }

            if (RoleMatchesName(role, tName))
                score += 20;
            if (RoleConflictsName(role, tName))
                score -= 20;

            if (score > bestScore)
            {
                bestScore = score;
                best = tex;
            }
        }

        return bestScore >= 10 ? best : null;
    }

    private static bool RoleMatchesName(TextureRole role, string name)
    {
        switch (role)
        {
            case TextureRole.BaseColor:
                return name.Contains("_d") || name.Contains("basecolor") || name.Contains("albedo") || name.Contains("diffuse") || name.Contains("color");
            case TextureRole.Normal:
                return IsNormalName(name);
            case TextureRole.MetallicOrMask:
                return name.Contains("metal") || name.Contains("rough") || name.Contains("smooth") || name.Contains("spec") || name.Contains("mask");
            case TextureRole.Occlusion:
                return name.Contains("ao") || name.Contains("occlusion");
            case TextureRole.Emission:
                return name.Contains("emiss") || name.Contains("glow");
            case TextureRole.AlphaMask:
                return name.Contains("alpha") || name.Contains("opacity") || name.Contains("cutout");
            default:
                return false;
        }
    }

    private static bool RoleConflictsName(TextureRole role, string name)
    {
        if (role == TextureRole.BaseColor && (IsNormalName(name) || IsMaskName(name)))
            return true;
        if (role == TextureRole.Normal && !IsNormalName(name))
            return true;
        if (role == TextureRole.Occlusion && IsNormalName(name))
            return true;
        return false;
    }

    private static string[] TokenizeName(string name)
    {
        char[] separators = { '_', '-', '.', ' ', '(', ')', '[', ']' };
        string[] raw = name.ToLowerInvariant().Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return raw
            .Where(t => t.Length > 2)
            .Where(t => t != "mat" && t != "material" && t != "mesh")
            .Distinct()
            .ToArray();
    }

    private static bool IsNormalName(string lower)
    {
        return lower.Contains("_n") || lower.Contains("normal") || lower.Contains("norm");
    }

    private static bool IsMaskName(string lower)
    {
        return lower.Contains("metal") || lower.Contains("rough") || lower.Contains("smooth") || lower.Contains("spec") || lower.Contains("ao") || lower.Contains("occlusion") || lower.Contains("mask");
    }

    private static bool IsAlphaClipName(string lower)
    {
        return lower.Contains("alpha") || lower.Contains("opacity") || lower.Contains("cutout") || lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("branch") || lower.Contains("foliage") || lower.Contains("tree");
    }
}
