using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class NureQuestSceneRuntimeOptimizer : MonoBehaviour
{
    private const float ActiveSceneScanInterval = 0.5f;
    private const float SlowSceneScanInterval = 2.0f;
    private const int FastScanPasses = 24;
    private const int RoofProxySegments = 14;
    private static readonly bool StripMaterialTextureFeatures = false;
    private static readonly bool DisableRuntimeLights = false;
    private static readonly bool DisableRuneChildObjects = false;
    private static readonly bool ReplaceRoofsWithRuntimeProxy = false;
    private static bool s_Installed;

    private readonly HashSet<int> _optimizedSceneRoots = new HashSet<int>();
    private readonly HashSet<int> _optimizedRuneRoots = new HashSet<int>();
    private readonly HashSet<int> _disabledRoofRenderers = new HashSet<int>();
    private readonly HashSet<Material> _optimizedMaterials = new HashSet<Material>();
    private SceneGroupManager _manager;
    private float _nextScanTime;
    private int _scanPasses;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (s_Installed || !ShouldRunOnThisPlatform())
            return;

        s_Installed = true;
        GameObject go = new GameObject(nameof(NureQuestSceneRuntimeOptimizer));
        DontDestroyOnLoad(go);
        go.AddComponent<NureQuestSceneRuntimeOptimizer>();
    }

    private static bool ShouldRunOnThisPlatform()
    {
#if UNITY_ANDROID || NURE_QUEST_PERFORMANCE || NURE_METAQUEST_BUILD
        return true;
#else
        return false;
#endif
    }

    private IEnumerator Start()
    {
        for (int i = 0; i < 8; i++)
        {
            TryBindManager();
            OptimizeActiveRuntimeState(force: true);
            yield return null;
        }
    }

    private void Update()
    {
        TryBindManager();

        if (Time.unscaledTime < _nextScanTime)
            return;

        _scanPasses++;
        _nextScanTime = Time.unscaledTime + (_scanPasses < FastScanPasses ? ActiveSceneScanInterval : SlowSceneScanInterval);
        OptimizeActiveRuntimeState(force: _scanPasses < FastScanPasses);
    }

    private void OnDestroy()
    {
        if (_manager != null)
            _manager.SceneActivated -= HandleSceneActivated;
    }

    private void TryBindManager()
    {
        if (_manager != null)
            return;

        _manager = FindFirstObjectByType<SceneGroupManager>();
        if (_manager == null)
            return;

        _manager.SceneActivated -= HandleSceneActivated;
        _manager.SceneActivated += HandleSceneActivated;
        ApplyManagerQuestOverrides(_manager);
    }

    private void HandleSceneActivated(SceneGroupManager.VirtualScene scene)
    {
        ApplyManagerQuestOverrides(_manager);
        if (scene != null && scene.root != null)
            OptimizeSceneRoot(scene.root);

        OptimizeRuneContextRoots();
    }

    private void OptimizeActiveRuntimeState(bool force)
    {
        if (_manager != null)
            ApplyManagerQuestOverrides(_manager);

        OptimizeRuneContextRoots();

        GameObject activeRoot = FindActiveVirtualSceneRoot();
        if (activeRoot == null)
            return;

        if (force || !_optimizedSceneRoots.Contains(activeRoot.GetInstanceID()))
            OptimizeSceneRoot(activeRoot);
    }

    private static void ApplyManagerQuestOverrides(SceneGroupManager manager)
    {
        if (manager == null)
            return;

        manager.showFirstSceneBehindLanguageSelection = false;
        manager.preactivateFirstSceneDuringLanguageSelection = false;
        manager.enforceGlobalObjectContinuously = false;
        manager.enforceSkyboxContinuously = false;
        manager.forceRuntimeSkyDomeOnMobile = false;
        manager.enableSceneSpawnDissolveFx = false;
        manager.languageSelectionRestoreComponentsPerFrame = Mathf.Min(manager.languageSelectionRestoreComponentsPerFrame, 96);
        manager.languageSelectionRestoreFrameBudgetMs = Mathf.Min(manager.languageSelectionRestoreFrameBudgetMs, 1.25f);
    }

    private GameObject FindActiveVirtualSceneRoot()
    {
        if (_manager == null || _manager.scenes == null)
            return null;

        for (int i = 0; i < _manager.scenes.Count; i++)
        {
            SceneGroupManager.VirtualScene scene = _manager.scenes[i];
            if (scene == null || scene.root == null)
                continue;

            if (scene.root.activeInHierarchy)
                return scene.root;
        }

        return null;
    }

    private void OptimizeSceneRoot(GameObject root)
    {
        if (root == null)
            return;

        int rootId = root.GetInstanceID();
        _optimizedSceneRoots.Add(rootId);

        OptimizeRenderers(root);
        if (DisableRuntimeLights)
            DisableExpensiveRuntimeComponents(root);
        if (ReplaceRoofsWithRuntimeProxy)
            ReplaceHighPolyCapannaRoofs(root);

        if (root.name.IndexOf("Scena1", StringComparison.OrdinalIgnoreCase) >= 0)
            OptimizeRuneContextRoot(root);
    }

    private void OptimizeRuneContextRoots()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null)
                continue;

            OptimizeRuneContextRoot(root);
        }
    }

    private void OptimizeRuneContextRoot(GameObject root)
    {
        if (root == null)
            return;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform transform = transforms[i];
            if (transform == null || !IsRuneContextRoot(transform.name))
                continue;

            int id = transform.gameObject.GetInstanceID();
            bool firstPass = _optimizedRuneRoots.Add(id);

            if (DisableRuneChildObjects)
                PruneRuneHeavyChildren(transform);
            OptimizeRenderers(transform.gameObject);

            if (firstPass && ReplaceRoofsWithRuntimeProxy)
                ReplaceHighPolyCapannaRoofs(transform.gameObject);
        }
    }

    private static bool IsRuneContextRoot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.IndexOf("Rune_250K_20x4096", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("Rune_125K_20x4096", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void PruneRuneHeavyChildren(Transform runeRoot)
    {
        if (runeRoot == null)
            return;

        Transform[] children = runeRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || child == runeRoot)
                continue;

            string name = child.name;
            if (ContainsAny(name, "Trees", "old_olive_tree", "capanna", "Popup_", "Targhetta", "Button"))
                child.gameObject.SetActive(false);
        }
    }

    private void OptimizeRenderers(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            if (!StripMaterialTextureFeatures)
                continue;

            Material[] materials = renderer.sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
                OptimizeMaterial(materials[m]);
        }
    }

    private void OptimizeMaterial(Material material)
    {
        if (material == null || !_optimizedMaterials.Add(material))
            return;

        DisableTextureFeature(material, "_BumpMap", "_NORMALMAP");
        DisableTextureFeature(material, "_ParallaxMap", "_PARALLAXMAP");
        DisableTextureFeature(material, "_OcclusionMap", "_OCCLUSIONMAP");
        DisableTextureFeature(material, "_DetailAlbedoMap", "_DETAIL_MULX2");
        DisableTextureFeature(material, "_DetailNormalMap", "_DETAIL_MULX2");

        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", Color.black);

        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
    }

    private static void DisableTextureFeature(Material material, string textureProperty, string keyword)
    {
        if (material == null)
            return;

        if (material.HasProperty(textureProperty))
            material.SetTexture(textureProperty, null);

        if (!string.IsNullOrWhiteSpace(keyword))
            material.DisableKeyword(keyword);
    }

    private static void DisableExpensiveRuntimeComponents(GameObject root)
    {
        Light[] lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null)
                continue;

            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForceVertex;
            light.enabled = false;
        }

        ReflectionProbe[] probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        for (int i = 0; i < probes.Length; i++)
        {
            if (probes[i] != null)
                probes[i].enabled = false;
        }
    }

    private static void DisableColliders(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private static void DisableNonInteractiveRaycasters(GameObject root)
    {
        GraphicRaycaster[] raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);
        for (int i = 0; i < raycasters.Length; i++)
        {
            GraphicRaycaster raycaster = raycasters[i];
            if (raycaster == null)
                continue;

            if (raycaster.GetComponentInChildren<Button>(true) == null)
                raycaster.enabled = false;
        }
    }

    private void ReplaceHighPolyCapannaRoofs(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsHighPolyCapannaRoof(renderer))
                continue;

            int id = renderer.GetInstanceID();
            if (!_disabledRoofRenderers.Add(id))
            {
                renderer.enabled = false;
                continue;
            }

            CreateRoofProxy(renderer);
            renderer.enabled = false;
        }
    }

    private static bool IsHighPolyCapannaRoof(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Mesh mesh = GetRendererMesh(renderer);
        if (mesh == null || CountTriangles(mesh) <= 5000)
            return false;

        string name = renderer.name ?? string.Empty;
        string meshName = mesh.name ?? string.Empty;
        return name.IndexOf("tetto_Mesh.004_paglia", StringComparison.OrdinalIgnoreCase) >= 0
            || meshName.IndexOf("tetto_Mesh.004_paglia", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Mesh GetRendererMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static int CountTriangles(Mesh mesh)
    {
        if (mesh == null)
            return 0;

        int triangles = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            triangles += (int)mesh.GetIndexCount(i) / 3;

        return triangles;
    }

    private static void CreateRoofProxy(Renderer source)
    {
        Mesh sourceMesh = GetRendererMesh(source);
        Bounds localBounds = sourceMesh != null ? sourceMesh.bounds : new Bounds(Vector3.zero, Vector3.one);

        GameObject proxy = new GameObject("_QuestProxyRoof_" + source.name);
        proxy.transform.SetParent(source.transform, false);

        MeshFilter filter = proxy.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildRoofProxyMesh(localBounds);

        MeshRenderer renderer = proxy.AddComponent<MeshRenderer>();
        Material[] materials = source.sharedMaterials;
        renderer.sharedMaterial = materials != null && materials.Length > 0 ? materials[0] : null;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private static Mesh BuildRoofProxyMesh(Bounds bounds)
    {
        int segments = Mathf.Max(8, RoofProxySegments);
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        float radiusX = Mathf.Max(0.01f, size.x * 0.5f);
        float radiusZ = Mathf.Max(0.01f, size.z * 0.5f);
        float baseY = center.y - size.y * 0.48f;
        float apexY = center.y + size.y * 0.5f;

        var vertices = new List<Vector3>(segments + 2);
        var triangles = new List<int>(segments * 6);

        vertices.Add(new Vector3(center.x, apexY, center.z));
        vertices.Add(new Vector3(center.x, baseY, center.z));

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            vertices.Add(new Vector3(
                center.x + Mathf.Cos(angle) * radiusX,
                baseY,
                center.z + Mathf.Sin(angle) * radiusZ));
        }

        for (int i = 0; i < segments; i++)
        {
            int current = 2 + i;
            int next = 2 + ((i + 1) % segments);

            triangles.Add(0);
            triangles.Add(next);
            triangles.Add(current);

            triangles.Add(1);
            triangles.Add(current);
            triangles.Add(next);
        }

        Mesh mesh = new Mesh
        {
            name = "QuestProxy_CapannaRoof"
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        for (int i = 0; i < tokens.Length; i++)
        {
            if (value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
