using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public class SceneActivationUrpDissolveVfx : MonoBehaviour
{
    public struct Settings
    {
        public int maxRenderers;
        public float duration;
        public float groupStagger;
        public float minBoundsSize;
        public float edgeWidth;
        public float edgeColorIntensity;
        public float noiseScale;
        public Color edgeColor;
    }

    private sealed class RendererEntry
    {
        public Renderer Renderer;
        public Material[] OriginalSharedMaterials;
        public Material[] AnimatedMaterials;
        public float StartDelay;
    }

    private sealed class RendererGroup
    {
        public Transform Root;
        public Bounds Bounds;
        public bool HasBounds;
        public readonly List<Renderer> Renderers = new List<Renderer>(8);
    }

    private const string LitTemplateResourceName = "SceneSpawnDissolveLitTemplate";
    private const string UnlitTemplateResourceName = "SceneSpawnDissolveUnlitTemplate";
    private const string PriorityScene10Name = "Scena10";
    private const string PriorityScene10PlaneName = "Plane";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int TilingId = Shader.PropertyToID("_Tiling");
    private static readonly int OffsetId = Shader.PropertyToID("_Offest");
    private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
    private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
    private static readonly int NormalScaleId = Shader.PropertyToID("_NormalScale");
    private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
    private static readonly int OcclusionStrengthId = Shader.PropertyToID("_OcclusionStrength");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int NoiseUvSpeedId = Shader.PropertyToID("_NoiseUVSpeed");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int EdgeColorIntensityId = Shader.PropertyToID("_EdgeColorIntensity");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

    private readonly List<RendererEntry> _entries = new List<RendererEntry>(96);
    private readonly List<Material> _runtimeMaterials = new List<Material>(192);

    private Coroutine _playRoutine;
    private Material _litTemplate;
    private Material _unlitTemplate;

    public float Play(GameObject sceneRoot, Transform referencePoint, Settings settings)
    {
        StopAndClear();

        if (sceneRoot == null)
            return 0f;

        Settings sanitized = Sanitize(settings);
        List<RendererGroup> groups = CollectGroups(sceneRoot.transform, referencePoint, sanitized.minBoundsSize);
        if (groups.Count == 0)
            return 0f;

        int registeredRenderers = 0;
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            RendererGroup group = groups[groupIndex];
            if (group == null)
                continue;

            float startDelay = sanitized.groupStagger * groupIndex;
            for (int rendererIndex = 0; rendererIndex < group.Renderers.Count; rendererIndex++)
            {
                if (registeredRenderers >= sanitized.maxRenderers)
                    break;

                if (TryRegisterRenderer(group.Renderers[rendererIndex], startDelay, sanitized))
                    registeredRenderers++;
            }

            if (registeredRenderers >= sanitized.maxRenderers)
                break;
        }

        if (_entries.Count == 0)
        {
            DestroyRuntimeMaterials();
            return 0f;
        }

        float totalDuration = CalculateTotalDuration(_entries, sanitized.duration);
        _playRoutine = StartCoroutine(PlayRoutine(sanitized));
        return totalDuration;
    }

    public void StopAndClear()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        RestoreOriginalMaterials();
        DestroyRuntimeMaterials();
    }

    private IEnumerator PlayRoutine(Settings settings)
    {
        float totalDuration = CalculateTotalDuration(_entries, settings.duration);

        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            UpdateEntries(elapsed, settings.duration);
            yield return null;
        }

        UpdateEntries(totalDuration + 0.001f, settings.duration);
        RestoreOriginalMaterials();
        DestroyRuntimeMaterials();
        _playRoutine = null;
    }

    private static float CalculateTotalDuration(List<RendererEntry> entries, float duration)
    {
        float totalDuration = duration;
        if (entries == null)
            return totalDuration;

        for (int i = 0; i < entries.Count; i++)
        {
            RendererEntry entry = entries[i];
            if (entry == null)
                continue;

            totalDuration = Mathf.Max(totalDuration, entry.StartDelay + duration);
        }

        return totalDuration;
    }

    private void UpdateEntries(float elapsed, float duration)
    {
        for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
        {
            RendererEntry entry = _entries[entryIndex];
            if (entry == null || entry.AnimatedMaterials == null)
                continue;

            float normalized = Mathf.Clamp01((elapsed - entry.StartDelay) / Mathf.Max(0.01f, duration));
            float dissolve = 1f - Smooth01(normalized);

            for (int materialIndex = 0; materialIndex < entry.AnimatedMaterials.Length; materialIndex++)
            {
                Material material = entry.AnimatedMaterials[materialIndex];
                if (material == null)
                    continue;

                material.SetFloat(DissolveId, dissolve);
            }
        }
    }

    private bool TryRegisterRenderer(Renderer renderer, float startDelay, Settings settings)
    {
        if (renderer == null)
            return false;

        Material[] originalSharedMaterials = renderer.sharedMaterials;
        if (originalSharedMaterials == null || originalSharedMaterials.Length == 0)
            return false;

        Material[] runtimeSharedMaterials = new Material[originalSharedMaterials.Length];
        List<Material> animatedMaterials = new List<Material>(originalSharedMaterials.Length);

        for (int materialIndex = 0; materialIndex < originalSharedMaterials.Length; materialIndex++)
        {
            Material source = originalSharedMaterials[materialIndex];
            if (!CanDissolveMaterial(source))
            {
                runtimeSharedMaterials[materialIndex] = source;
                continue;
            }

            Material template = ResolveTemplateFor(source);
            if (template == null)
            {
                runtimeSharedMaterials[materialIndex] = source;
                continue;
            }

            Material runtimeMaterial = new Material(template)
            {
                name = "RuntimeSceneSpawnDissolve_" + source.name,
                hideFlags = HideFlags.DontSave
            };

            CopySourceMaterial(source, runtimeMaterial, settings);
            runtimeSharedMaterials[materialIndex] = runtimeMaterial;
            animatedMaterials.Add(runtimeMaterial);
            _runtimeMaterials.Add(runtimeMaterial);
        }

        if (animatedMaterials.Count == 0)
            return false;

        renderer.sharedMaterials = runtimeSharedMaterials;
        _entries.Add(new RendererEntry
        {
            Renderer = renderer,
            OriginalSharedMaterials = originalSharedMaterials,
            AnimatedMaterials = animatedMaterials.ToArray(),
            StartDelay = startDelay,
        });

        return true;
    }

    private Material ResolveTemplateFor(Material source)
    {
        if (source == null)
            return GetLitTemplate();

        string shaderName = source.shader != null ? source.shader.name : string.Empty;
        return IsUnlitShaderName(shaderName) ? GetUnlitTemplate() : GetLitTemplate();
    }

    private Material GetLitTemplate()
    {
        if (_litTemplate == null)
            _litTemplate = Resources.Load<Material>(LitTemplateResourceName);

        return _litTemplate;
    }

    private Material GetUnlitTemplate()
    {
        if (_unlitTemplate == null)
            _unlitTemplate = Resources.Load<Material>(UnlitTemplateResourceName);

        return _unlitTemplate != null ? _unlitTemplate : GetLitTemplate();
    }

    private static bool CanDissolveMaterial(Material material)
    {
        if (material == null || material.shader == null)
            return false;

        if (material.renderQueue >= (int)RenderQueue.Transparent)
            return false;

        if (material.HasProperty(SurfaceId) && material.GetFloat(SurfaceId) > 0.5f)
            return false;

        string shaderName = material.shader.name;
        return !shaderName.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase)
               && shaderName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) < 0
               && shaderName.IndexOf("Skybox", StringComparison.OrdinalIgnoreCase) < 0
               && shaderName.IndexOf("UI/", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool IsUnlitShaderName(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName))
            return false;

        return shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0
               || shaderName.IndexOf("Sprites", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CopySourceMaterial(Material source, Material target, Settings settings)
    {
        if (source == null || target == null)
            return;

        Texture baseTexture = null;
        Vector2 baseScale = Vector2.one;
        Vector2 baseOffset = Vector2.zero;

        if (source.HasProperty(BaseMapId))
        {
            baseTexture = source.GetTexture(BaseMapId);
            baseScale = source.GetTextureScale("_BaseMap");
            baseOffset = source.GetTextureOffset("_BaseMap");
        }
        else if (source.HasProperty(MainTexId))
        {
            baseTexture = source.GetTexture(MainTexId);
            baseScale = source.GetTextureScale("_MainTex");
            baseOffset = source.GetTextureOffset("_MainTex");
        }

        if (target.HasProperty(BaseMapId))
        {
            target.SetTexture(BaseMapId, baseTexture);
            target.SetTextureScale("_BaseMap", baseScale);
            target.SetTextureOffset("_BaseMap", baseOffset);
        }

        if (target.HasProperty(TilingId))
            target.SetVector(TilingId, new Vector4(baseScale.x, baseScale.y, 0f, 0f));

        if (target.HasProperty(OffsetId))
            target.SetVector(OffsetId, new Vector4(baseOffset.x, baseOffset.y, 0f, 0f));

        Color baseColor = Color.white;
        if (source.HasProperty(BaseColorId))
            baseColor = source.GetColor(BaseColorId);
        else if (source.HasProperty(ColorId))
            baseColor = source.GetColor(ColorId);

        if (target.HasProperty(BaseColorId))
            target.SetColor(BaseColorId, baseColor);

        Texture normalTexture = null;
        float normalScale = 1f;
        if (source.HasProperty(NormalMapId))
        {
            normalTexture = source.GetTexture(NormalMapId);
            if (source.HasProperty(NormalScaleId))
                normalScale = source.GetFloat(NormalScaleId);
        }
        else if (source.HasProperty(BumpMapId))
        {
            normalTexture = source.GetTexture(BumpMapId);
            if (source.HasProperty(BumpScaleId))
                normalScale = source.GetFloat(BumpScaleId);
        }

        if (target.HasProperty(NormalMapId))
            target.SetTexture(NormalMapId, normalTexture);

        if (target.HasProperty(NormalScaleId))
            target.SetFloat(NormalScaleId, normalScale);

        float metallic = source.HasProperty(MetallicId) ? source.GetFloat(MetallicId) : 0f;
        float smoothness = source.HasProperty(SmoothnessId)
            ? source.GetFloat(SmoothnessId)
            : (source.HasProperty(GlossinessId) ? source.GetFloat(GlossinessId) : 0.5f);

        if (target.HasProperty(MetallicId))
            target.SetFloat(MetallicId, metallic);

        if (target.HasProperty(SmoothnessId))
            target.SetFloat(SmoothnessId, smoothness);

        if (target.HasProperty(OcclusionStrengthId))
            target.SetFloat(OcclusionStrengthId, 1f);

        if (target.HasProperty(NoiseUvSpeedId))
            target.SetVector(NoiseUvSpeedId, Vector4.zero);

        if (target.HasProperty(NoiseScaleId))
            target.SetFloat(NoiseScaleId, settings.noiseScale);

        if (target.HasProperty(EdgeWidthId))
            target.SetFloat(EdgeWidthId, settings.edgeWidth);

        if (target.HasProperty(EdgeColorId))
            target.SetColor(EdgeColorId, settings.edgeColor);

        if (target.HasProperty(EdgeColorIntensityId))
            target.SetFloat(EdgeColorIntensityId, settings.edgeColorIntensity);

        if (target.HasProperty(DissolveId))
            target.SetFloat(DissolveId, 1f);
    }

    private static Settings Sanitize(Settings settings)
    {
        settings.maxRenderers = Mathf.Clamp(settings.maxRenderers, 1, 256);
        settings.duration = Mathf.Clamp(settings.duration, 0.15f, 6f);
        settings.groupStagger = Mathf.Clamp(settings.groupStagger, 0f, 0.25f);
        settings.minBoundsSize = Mathf.Clamp(settings.minBoundsSize, 0.01f, 2f);
        settings.edgeWidth = Mathf.Clamp(settings.edgeWidth, 0.001f, 0.35f);
        settings.edgeColorIntensity = Mathf.Clamp(settings.edgeColorIntensity, 0f, 8f);
        settings.noiseScale = Mathf.Clamp(settings.noiseScale, 0.1f, 128f);

        if (settings.edgeColor.a <= 0f)
            settings.edgeColor = new Color(0.62f, 0.94f, 1f, 1f);

        return settings;
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - (2f * value));
    }

    private static List<RendererGroup> CollectGroups(Transform sceneRoot, Transform referencePoint, float minBoundsSize)
    {
        Dictionary<Transform, RendererGroup> grouped = new Dictionary<Transform, RendererGroup>(32);
        Renderer[] renderers = sceneRoot.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (!ShouldUseRenderer(sceneRoot, renderer, minBoundsSize))
                continue;

            Transform groupRoot = ResolveTopLevelGroup(sceneRoot, renderer.transform);
            if (groupRoot == null)
                groupRoot = renderer.transform;

            RendererGroup group;
            if (!grouped.TryGetValue(groupRoot, out group))
            {
                group = new RendererGroup { Root = groupRoot };
                grouped.Add(groupRoot, group);
            }

            group.Renderers.Add(renderer);
            if (!group.HasBounds)
            {
                group.Bounds = renderer.bounds;
                group.HasBounds = true;
            }
            else
            {
                group.Bounds.Encapsulate(renderer.bounds);
            }
        }

        List<RendererGroup> groups = new List<RendererGroup>(grouped.Values);
        groups.Sort((a, b) =>
        {
            bool aIsPriority = IsPriorityGroup(sceneRoot, a);
            bool bIsPriority = IsPriorityGroup(sceneRoot, b);
            if (aIsPriority != bIsPriority)
                return aIsPriority ? -1 : 1;

            if (referencePoint == null)
                return string.CompareOrdinal(a.Root != null ? a.Root.name : string.Empty, b.Root != null ? b.Root.name : string.Empty);

            float distanceA = a.HasBounds ? (a.Bounds.center - referencePoint.position).sqrMagnitude : float.MaxValue;
            float distanceB = b.HasBounds ? (b.Bounds.center - referencePoint.position).sqrMagnitude : float.MaxValue;
            return distanceA.CompareTo(distanceB);
        });

        return groups;
    }

    private static bool IsPriorityGroup(Transform sceneRoot, RendererGroup group)
    {
        return sceneRoot != null
               && group != null
               && group.Root != null
               && string.Equals(sceneRoot.name, PriorityScene10Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(group.Root.name, PriorityScene10PlaneName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseRenderer(Transform sceneRoot, Renderer renderer, float minBoundsSize)
    {
        if (sceneRoot == null || renderer == null)
            return false;

        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
            return false;

        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            return false;

        if (!renderer.transform.IsChildOf(sceneRoot))
            return false;

        if (renderer.GetComponentInParent<Canvas>(true) != null)
            return false;

        Bounds bounds = renderer.bounds;
        float planarSize = Mathf.Max(bounds.size.x, bounds.size.z);
        return bounds.size.y >= minBoundsSize || planarSize >= minBoundsSize;
    }

    private static Transform ResolveTopLevelGroup(Transform sceneRoot, Transform target)
    {
        if (sceneRoot == null || target == null || target == sceneRoot)
            return target;

        Transform current = target;
        while (current != null && current.parent != null)
        {
            if (current.parent == sceneRoot)
                return current;

            current = current.parent;
        }

        return target;
    }

    private void RestoreOriginalMaterials()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            RendererEntry entry = _entries[i];
            if (entry == null || entry.Renderer == null)
                continue;

            entry.Renderer.sharedMaterials = entry.OriginalSharedMaterials;
        }

        _entries.Clear();
    }

    private void DestroyRuntimeMaterials()
    {
        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            Material material = _runtimeMaterials[i];
            if (material == null)
                continue;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        _runtimeMaterials.Clear();
    }

    private void OnDisable()
    {
        StopAndClear();
    }

    private void OnDestroy()
    {
        StopAndClear();
    }
}
