#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NuragicWomenWardrobeInstantiator
{
    private const string RootFolder = "Assets/Materials/DonneNuragiche";
    private const string AutoRunSessionKey = "NURE.NuragicWomenWardrobeInstantiator.AutoRunDone.v2";

    private enum FabricPattern
    {
        Solid,
        Weave,
        VerticalPleats,
        HorizontalBands,
        FloralDots,
        LaceCross
    }

    private sealed class FabricStyle
    {
        public readonly string name;
        public readonly Color baseColor;
        public readonly Color accentColor;
        public readonly float smoothness;
        public readonly FabricPattern pattern;

        public FabricStyle(string name, Color baseColor, Color accentColor, float smoothness, FabricPattern pattern)
        {
            this.name = name;
            this.baseColor = baseColor;
            this.accentColor = accentColor;
            this.smoothness = smoothness;
            this.pattern = pattern;
        }
    }

    private sealed class WardrobeProfile
    {
        public readonly string rootName;
        public readonly FabricStyle[] styles;

        public WardrobeProfile(string rootName, FabricStyle[] styles)
        {
            this.rootName = rootName;
            this.styles = styles;
        }
    }

    private static readonly WardrobeProfile[] s_Profiles =
    {
        new WardrobeProfile(
            "Donna_UNo_Arm",
            new[]
            {
                new FabricStyle("Shawl_Ivory", new Color(0.93f, 0.90f, 0.85f), new Color(0.80f, 0.76f, 0.68f), 0.17f, FabricPattern.LaceCross),
                new FabricStyle("Bodice_Crimson", new Color(0.56f, 0.13f, 0.15f), new Color(0.72f, 0.32f, 0.26f), 0.26f, FabricPattern.Weave),
                new FabricStyle("Skirt_Ochre", new Color(0.60f, 0.46f, 0.18f), new Color(0.75f, 0.58f, 0.25f), 0.22f, FabricPattern.VerticalPleats),
                new FabricStyle("Apron_Indigo", new Color(0.13f, 0.17f, 0.30f), new Color(0.27f, 0.31f, 0.46f), 0.20f, FabricPattern.HorizontalBands),
                new FabricStyle("Blouse_OffWhite", new Color(0.90f, 0.88f, 0.82f), new Color(0.78f, 0.76f, 0.71f), 0.15f, FabricPattern.Weave),
                new FabricStyle("Embroidery_Red", new Color(0.50f, 0.16f, 0.12f), new Color(0.80f, 0.69f, 0.36f), 0.29f, FabricPattern.FloralDots)
            }),
        new WardrobeProfile(
            "Donna_DUe_Arm",
            new[]
            {
                new FabricStyle("Veil_Linen", new Color(0.95f, 0.93f, 0.90f), new Color(0.82f, 0.79f, 0.72f), 0.16f, FabricPattern.LaceCross),
                new FabricStyle("Jacket_Charcoal", new Color(0.60f, 0.80f, 0.92f), new Color(0.83f, 0.93f, 0.98f), 0.34f, FabricPattern.Weave),
                new FabricStyle("Skirt_Plum", new Color(0.22f, 0.10f, 0.23f), new Color(0.33f, 0.17f, 0.35f), 0.25f, FabricPattern.VerticalPleats),
                new FabricStyle("Apron_Garnet", new Color(0.49f, 0.11f, 0.14f), new Color(0.66f, 0.40f, 0.25f), 0.24f, FabricPattern.HorizontalBands),
                new FabricStyle("Blouse_Cream", new Color(0.89f, 0.86f, 0.80f), new Color(0.73f, 0.70f, 0.64f), 0.15f, FabricPattern.Weave),
                new FabricStyle("Panel_Olive", new Color(0.18f, 0.25f, 0.17f), new Color(0.39f, 0.43f, 0.25f), 0.23f, FabricPattern.FloralDots)
            })
    };

    private static readonly FabricStyle s_FixStyleSkinWarm =
        new FabricStyle("Fix_Skin_Warm", new Color(0.66f, 0.52f, 0.42f), new Color(0.70f, 0.57f, 0.46f), 0.34f, FabricPattern.Solid);

    private static readonly FabricStyle s_FixStyleHairDark =
        new FabricStyle("Fix_Hair_Dark", new Color(0.09f, 0.07f, 0.06f), new Color(0.16f, 0.12f, 0.09f), 0.42f, FabricPattern.Solid);

    private static readonly FabricStyle s_FixStyleDueRed =
        new FabricStyle("Fix_Due_3qal_Red", new Color(0.49f, 0.11f, 0.14f), new Color(0.66f, 0.40f, 0.25f), 0.24f, FabricPattern.HorizontalBands);

    private static readonly FabricStyle s_FixStyleUnoYellow =
        new FabricStyle("Fix_Uno_Dress_Ochre", new Color(0.60f, 0.46f, 0.18f), new Color(0.75f, 0.58f, 0.25f), 0.22f, FabricPattern.VerticalPleats);

    private static readonly FabricStyle s_FixStyleUnoBlue =
        new FabricStyle("Fix_Uno_3qal_Blue", new Color(0.13f, 0.17f, 0.30f), new Color(0.27f, 0.31f, 0.46f), 0.20f, FabricPattern.HorizontalBands);

    [MenuItem("Tools/NURE/Donne/Instanzia Materiali Donne Nuragiche")]
    public static void InstantiateAndAssign()
    {
        EnsureFolder(RootFolder);
        Dictionary<string, Material> materialMap = new Dictionary<string, Material>(24);

        for (int i = 0; i < s_Profiles.Length; i++)
        {
            WardrobeProfile profile = s_Profiles[i];
            string profileFolder = RootFolder + "/" + profile.rootName;
            EnsureFolder(profileFolder);

            for (int s = 0; s < profile.styles.Length; s++)
            {
                FabricStyle style = profile.styles[s];
                Material mat = CreateOrUpdateMaterialAsset(profile, style, profileFolder);
                if (mat == null)
                    continue;

                materialMap[profile.rootName + "_" + style.name] = mat;
            }

            if (string.Equals(profile.rootName, "Donna_DUe_Arm", System.StringComparison.OrdinalIgnoreCase))
            {
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleDueRed);
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleSkinWarm);
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleHairDark);
            }

            if (string.Equals(profile.rootName, "Donna_UNo_Arm", System.StringComparison.OrdinalIgnoreCase))
            {
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleUnoYellow);
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleUnoBlue);
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleSkinWarm);
                AddStyleAssetToMap(materialMap, profile, profileFolder, s_FixStyleHairDark);
            }
        }

        int rootsStyled = 0;
        int renderersStyled = 0;

        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.gameObject == null || !t.gameObject.scene.IsValid())
                continue;

            for (int p = 0; p < s_Profiles.Length; p++)
            {
                WardrobeProfile profile = s_Profiles[p];
                if (!string.Equals(t.name, profile.rootName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int touched = ApplyProfileToRoot(t, profile, materialMap);
                if (touched > 0)
                {
                    rootsStyled++;
                    renderersStyled += touched;
                    EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (rootsStyled == 0)
        {
            Debug.LogWarning("[NuragicWomenWardrobeInstantiator] Nessuna donna trovata nelle scene aperte.");
            return;
        }

        Debug.Log($"[NuragicWomenWardrobeInstantiator] Materiali istanziati. Root aggiornate: {rootsStyled}, renderers aggiornati: {renderersStyled}. Cartella: {RootFolder}");
    }

    private static void AddStyleAssetToMap(Dictionary<string, Material> materialMap, WardrobeProfile profile, string profileFolder, FabricStyle style)
    {
        Material mat = CreateOrUpdateMaterialAsset(profile, style, profileFolder);
        if (mat == null)
            return;

        materialMap[profile.rootName + "_" + style.name] = mat;
    }

    [InitializeOnLoadMethod]
    private static void AutoRunOnceInEditorSession()
    {
        if (SessionState.GetBool(AutoRunSessionKey, false))
            return;

        EditorApplication.delayCall += TryAutoRun;
    }

    private static void TryAutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (!HasTargetRootsInOpenScenes())
            return;

        InstantiateAndAssign();
        SessionState.SetBool(AutoRunSessionKey, true);
    }

    private static int ApplyProfileToRoot(Transform root, WardrobeProfile profile, Dictionary<string, Material> materialMap)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return 0;

        int styleCursor = 0;
        int touched = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            FabricStyle forcedStyle = TryGetForcedStyle(profile.rootName, renderer);
            if (forcedStyle == null && ShouldSkipRenderer(renderer))
                continue;

            Material[] source = renderer.sharedMaterials;
            if (source == null || source.Length == 0)
                continue;

            Material[] mapped = new Material[source.Length];
            bool changed = false;

            for (int m = 0; m < source.Length; m++)
            {
                Material current = source[m];
                if (IsSensitiveMaterial(current))
                {
                    mapped[m] = current;
                    continue;
                }

                if (forcedStyle != null)
                {
                    string forcedKey = profile.rootName + "_" + forcedStyle.name;
                    if (materialMap.TryGetValue(forcedKey, out Material forced) && forced != null)
                    {
                        mapped[m] = forced;
                        if (current != forced)
                            changed = true;
                        continue;
                    }

                    mapped[m] = current;
                    continue;
                }

                if (ShouldKeepOriginalMaterial(current))
                {
                    mapped[m] = current;
                    continue;
                }

                FabricStyle style = profile.styles[(styleCursor + m) % profile.styles.Length];
                string key = profile.rootName + "_" + style.name;
                if (!materialMap.TryGetValue(key, out Material instanced) || instanced == null)
                {
                    mapped[m] = current;
                    continue;
                }

                mapped[m] = instanced;
                if (current != instanced)
                    changed = true;
            }

            styleCursor += source.Length;

            if (!changed)
                continue;

            Undo.RecordObject(renderer, "Assign Nuragic Women Materials");
            renderer.sharedMaterials = mapped;
            EditorUtility.SetDirty(renderer);
            touched++;
        }

        return touched;
    }

    private static FabricStyle TryGetForcedStyle(string profileRootName, Renderer renderer)
    {
        if (renderer == null || string.IsNullOrEmpty(profileRootName))
            return null;

        if (string.Equals(profileRootName, "Donna_DUe_Arm", System.StringComparison.OrdinalIgnoreCase))
        {
            if (RendererMatchesToken(renderer, "nure2.3qallast"))
                return s_FixStyleDueRed;
            if (RendererMatchesToken(renderer, "nure2.female_generic") || RendererMatchesToken(renderer, "nure2.body"))
                return s_FixStyleSkinWarm;
            if (RendererMatchesToken(renderer, "nure2.braid01") || RendererMatchesToken(renderer, "nure2.eyebrow001"))
                return s_FixStyleHairDark;
        }

        if (string.Equals(profileRootName, "Donna_UNo_Arm", System.StringComparison.OrdinalIgnoreCase))
        {
            if (RendererMatchesToken(renderer, "nure1.medievaldress.002") || RendererMatchesToken(renderer, "nure1.medievaldress"))
                return s_FixStyleUnoYellow;
            if (RendererMatchesToken(renderer, "nure1.3qallast.002") || RendererMatchesToken(renderer, "nure1.3qallast"))
                return s_FixStyleUnoBlue;
            if (RendererMatchesToken(renderer, "nure1.female_generic") || RendererMatchesToken(renderer, "nure1.body"))
                return s_FixStyleSkinWarm;
            if (RendererMatchesToken(renderer, "nure1.long01"))
                return s_FixStyleHairDark;
        }

        return null;
    }

    private static bool RendererMatchesToken(Renderer renderer, string token)
    {
        if (renderer == null || string.IsNullOrWhiteSpace(token))
            return false;

        string t = token.ToLowerInvariant();
        if (renderer.name.ToLowerInvariant().Contains(t))
            return true;

        Material[] mats = renderer.sharedMaterials;
        if (mats == null)
            return false;

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat != null && mat.name.ToLowerInvariant().Contains(t))
                return true;
        }

        return false;
    }

    private static Material CreateOrUpdateMaterialAsset(WardrobeProfile profile, FabricStyle style, string folder)
    {
        string texPath = $"{folder}/{profile.rootName}_{style.name}_Albedo.png";
        string matPath = $"{folder}/NW_{profile.rootName}_{style.name}.mat";

        CreateOrUpdateTextureAsset(texPath, profile.rootName + "_" + style.name, style);
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader shader = ResolveShader();
            if (shader == null)
                return null;

            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        Shader resolved = ResolveShader();
        if (resolved != null && mat.shader != resolved)
            mat.shader = resolved;

        mat.name = Path.GetFileNameWithoutExtension(matPath);
        SetTextureIfExists(mat, "_BaseMap", albedo);
        SetTextureIfExists(mat, "_MainTex", albedo);
        SetColorIfExists(mat, "_BaseColor", Color.white);
        SetColorIfExists(mat, "_Color", Color.white);
        SetFloatIfExists(mat, "_Metallic", 0f);
        SetFloatIfExists(mat, "_Smoothness", style.smoothness);
        SetFloatIfExists(mat, "_Glossiness", style.smoothness);
        SetFloatIfExists(mat, "_SpecularHighlights", 1f);
        SetFloatIfExists(mat, "_EnvironmentReflections", 1f);
        EditorUtility.SetDirty(mat);

        return mat;
    }

    private static void CreateOrUpdateTextureAsset(string assetPath, string seed, FabricStyle style)
    {
        Texture2D generated = BuildFabricTexture(seed, style);
        byte[] png = generated.EncodeToPNG();
        Object.DestroyImmediate(generated);

        string absolutePath = ToAbsolutePath(assetPath);
        File.WriteAllBytes(absolutePath, png);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 2;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = false;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
    }

    private static Texture2D BuildFabricTexture(string seed, FabricStyle style)
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 2;

        uint seedHash = HashSeed(seed);
        Color[] pixels = new Color[size * size];
        int index = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float t = PatternBlend(style.pattern, x, y);
                float noise = (PseudoNoise(seedHash, x, y) - 0.5f) * 0.10f;
                Color c = Color.Lerp(style.baseColor, style.accentColor, t);
                c.r = Mathf.Clamp01(c.r + noise);
                c.g = Mathf.Clamp01(c.g + noise);
                c.b = Mathf.Clamp01(c.b + noise);
                c.a = 1f;
                pixels[index++] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(true, false);
        return tex;
    }

    private static float PatternBlend(FabricPattern pattern, int x, int y)
    {
        float weave = 0f;
        if (x % 8 == 0 || y % 8 == 0)
            weave = 0.12f;

        switch (pattern)
        {
            case FabricPattern.Solid:
                return 0f;
            case FabricPattern.VerticalPleats:
            {
                int stripe = x % 28;
                float pleat = stripe < 3 ? 0.32f : 0.02f;
                return Mathf.Clamp01(weave + pleat);
            }
            case FabricPattern.HorizontalBands:
            {
                int band = y % 36;
                float stripe = band < 3 ? 0.34f : 0.03f;
                return Mathf.Clamp01(weave + stripe);
            }
            case FabricPattern.FloralDots:
            {
                int cx = x % 42 - 21;
                int cy = y % 42 - 21;
                bool dot = cx * cx + cy * cy <= 20;
                return Mathf.Clamp01(weave + (dot ? 0.38f : 0.02f));
            }
            case FabricPattern.LaceCross:
            {
                bool diagA = (x + y) % 18 < 2;
                bool diagB = PositiveMod(x - y, 18) < 2;
                return Mathf.Clamp01(weave + ((diagA || diagB) ? 0.40f : 0.02f));
            }
            default:
                return Mathf.Clamp01(weave + 0.03f);
        }
    }

    private static bool ShouldKeepOriginalMaterial(Material material)
    {
        if (material == null)
            return false;

        string n = material.name.ToLowerInvariant();
        if (n.StartsWith("nw_"))
            return true;

        return n.Contains("skin")
            || n.Contains("face")
            || n.Contains("eye")
            || n.Contains("teeth")
            || n.Contains("hair")
            || n.Contains("capelli");
    }

    private static bool IsSensitiveMaterial(Material material)
    {
        if (material == null)
            return false;

        string n = material.name.ToLowerInvariant();
        return n.Contains("eye")
            || n.Contains("teeth");
    }

    private static bool ShouldSkipRenderer(Renderer renderer)
    {
        string n = renderer.name.ToLowerInvariant();
        return n.Contains("head")
            || n.Contains("face")
            || n.Contains("hair")
            || n.Contains("brow")
            || n.Contains("eyebrow")
            || n.Contains("eye")
            || n.Contains("highpolyeyes")
            || n.Contains("teeth")
            || n.Contains("hand")
            || n.Contains("mano");
    }

    private static Shader ResolveShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        return shader;
    }

    private static void SetTextureIfExists(Material mat, string property, Texture tex)
    {
        if (mat != null && mat.HasProperty(property))
            mat.SetTexture(property, tex);
    }

    private static void SetColorIfExists(Material mat, string property, Color color)
    {
        if (mat != null && mat.HasProperty(property))
            mat.SetColor(property, color);
    }

    private static void SetFloatIfExists(Material mat, string property, float value)
    {
        if (mat != null && mat.HasProperty(property))
            mat.SetFloat(property, value);
    }

    private static uint HashSeed(string seed)
    {
        unchecked
        {
            uint h = 2166136261u;
            for (int i = 0; i < seed.Length; i++)
                h = (h ^ seed[i]) * 16777619u;
            return h;
        }
    }

    private static float PseudoNoise(uint seed, int x, int y)
    {
        unchecked
        {
            uint h = seed;
            h ^= (uint)(x * 374761393);
            h ^= (uint)(y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 1023u) / 1023f;
        }
    }

    private static int PositiveMod(int value, int modulo)
    {
        int r = value % modulo;
        return r < 0 ? r + modulo : r;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        if (parts.Length == 0)
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string ToAbsolutePath(string assetPath)
    {
        if (assetPath.StartsWith("Assets/"))
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        return Path.GetFullPath(assetPath);
    }

    private static bool HasTargetRootsInOpenScenes()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.gameObject == null || !t.gameObject.scene.IsValid())
                continue;

            for (int p = 0; p < s_Profiles.Length; p++)
            {
                if (string.Equals(t.name, s_Profiles[p].rootName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
#endif
