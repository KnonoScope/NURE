using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NuragicWomenWardrobeRuntime : MonoBehaviour
{
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

    [Header("Update")]
    [Min(0.25f)] public float retryScanSeconds = 1.5f;
    public bool verboseLogs = false;

    private readonly Dictionary<string, Material> _materialCache = new Dictionary<string, Material>(24);
    private readonly HashSet<int> _styledRoots = new HashSet<int>();
    private float _nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        NuragicWomenWardrobeRuntime existing = FindFirstObjectByType<NuragicWomenWardrobeRuntime>();
        if (existing != null)
            return;

        GameObject go = new GameObject("NuragicWomenWardrobeRuntime");
        DontDestroyOnLoad(go);
        go.AddComponent<NuragicWomenWardrobeRuntime>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        _nextScanTime = 0f;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        _styledRoots.Clear();
        _nextScanTime = 0f;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScanTime)
            return;

        _nextScanTime = Time.unscaledTime + retryScanSeconds;
        TryApplyWardrobes();
    }

    private void TryApplyWardrobes()
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool anyApplied = false;

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

                int id = t.gameObject.GetInstanceID();
                if (_styledRoots.Contains(id))
                    continue;

                if (ApplyProfile(t, profile))
                {
                    _styledRoots.Add(id);
                    anyApplied = true;
                }
            }
        }

        if (verboseLogs && anyApplied)
            Debug.Log("[NuragicWomenWardrobeRuntime] Materiali vestiti applicati.");
    }

    private bool ApplyProfile(Transform root, WardrobeProfile profile)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;
        if (HasInstancedWardrobeMaterials(renderers))
            return false;

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

            Material[] existing = renderer.sharedMaterials;
            if (existing == null || existing.Length == 0)
                continue;

            Material[] mapped = new Material[existing.Length];
            bool changed = false;

            for (int m = 0; m < existing.Length; m++)
            {
                Material original = existing[m];
                if (IsSensitiveMaterial(original))
                {
                    mapped[m] = original;
                    continue;
                }

                if (forcedStyle != null)
                {
                    Material forced = GetOrCreateMaterial(profile.rootName, forcedStyle);
                    if (forced == null)
                    {
                        mapped[m] = original;
                        continue;
                    }

                    mapped[m] = forced;
                    if (original != forced)
                        changed = true;
                    continue;
                }

                if (ShouldKeepOriginalMaterial(original))
                {
                    mapped[m] = original;
                    continue;
                }

                FabricStyle style = profile.styles[(styleCursor + m) % profile.styles.Length];
                Material replacement = GetOrCreateMaterial(profile.rootName, style);
                if (replacement == null)
                {
                    mapped[m] = original;
                    continue;
                }

                mapped[m] = replacement;
                changed = true;
            }

            styleCursor += existing.Length;

            if (changed)
            {
                renderer.sharedMaterials = mapped;
                touched++;
            }
        }

        return touched > 0;
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

    private Material GetOrCreateMaterial(string profileId, FabricStyle style)
    {
        string key = profileId + "_" + style.name;
        if (_materialCache.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        Material mat = new Material(shader)
        {
            name = "MW_" + key
        };

        Texture2D tex = BuildFabricTexture(key, style);
        if (tex != null)
            SetTextureIfExists(mat, "_BaseMap", tex);
        if (tex != null)
            SetTextureIfExists(mat, "_MainTex", tex);

        SetColorIfExists(mat, "_BaseColor", Color.white);
        SetColorIfExists(mat, "_Color", Color.white);
        SetFloatIfExists(mat, "_Metallic", 0f);
        SetFloatIfExists(mat, "_Smoothness", style.smoothness);
        SetFloatIfExists(mat, "_Glossiness", style.smoothness);
        SetFloatIfExists(mat, "_SpecularHighlights", 1f);
        SetFloatIfExists(mat, "_EnvironmentReflections", 1f);

        _materialCache[key] = mat;
        return mat;
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

    private static bool HasInstancedWardrobeMaterials(Renderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || ShouldSkipRenderer(renderer))
                continue;

            Material[] mats = renderer.sharedMaterials;
            if (mats == null)
                continue;

            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null)
                    continue;

                if (mat.name.StartsWith("NW_", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
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

    private static Texture2D BuildFabricTexture(string seed, FabricStyle style)
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
        tex.name = seed + "_Fabric";
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
        tex.Apply(true, true);
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
                bool diagB = Mathf.Abs((x - y) % 18) < 2;
                return Mathf.Clamp01(weave + ((diagA || diagB) ? 0.40f : 0.02f));
            }
            default:
                return Mathf.Clamp01(weave + 0.03f);
        }
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

    private static void SetTextureIfExists(Material mat, string property, Texture texture)
    {
        if (mat.HasProperty(property))
            mat.SetTexture(property, texture);
    }

    private static void SetFloatIfExists(Material mat, string property, float value)
    {
        if (mat.HasProperty(property))
            mat.SetFloat(property, value);
    }

    private static void SetColorIfExists(Material mat, string property, Color value)
    {
        if (mat.HasProperty(property))
            mat.SetColor(property, value);
    }
}
