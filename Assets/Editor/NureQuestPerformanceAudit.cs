using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public static class NureQuestPerformanceAudit
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string LogsDirectory = "Logs";
    private const string AuditReportPath = "Logs/nure_quest_performance_audit.md";
    private const string DependencyReportPath = "Logs/nure_quest_dependency_audit.csv";

    [MenuItem("Codex/Quest Performance/Audit Scene")]
    public static void AuditSceneMenu()
    {
        AuditSceneBatch();
    }

    public static void AuditSceneBatch()
    {
        Directory.CreateDirectory(LogsDirectory);
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        SceneGroupManager manager = UnityEngine.Object.FindFirstObjectByType<SceneGroupManager>();
        if (manager == null)
            throw new InvalidOperationException("SceneGroupManager non trovato nella MainScene.");

        var report = new StringBuilder(64 * 1024);
        var dependencyRows = new List<DependencyRow>(4096);

        report.AppendLine("# NURE Quest Performance Audit");
        report.AppendLine();
        report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Unity: {Application.unityVersion}");
        report.AppendLine($"Scene: `{MainScenePath}`");
        report.AppendLine();

        AppendManagerRuntimeSettings(report, manager);

        List<SceneMetrics> metrics = new List<SceneMetrics>();
        for (int i = 0; i < manager.scenes.Count; i++)
        {
            SceneGroupManager.VirtualScene scene = manager.scenes[i];
            if (scene == null || scene.root == null)
                continue;

            string sceneName = string.IsNullOrWhiteSpace(scene.name) ? scene.root.name : scene.name;
            SceneMetrics sceneMetrics = CollectSceneMetrics(sceneName, scene.root);
            metrics.Add(sceneMetrics);
            dependencyRows.AddRange(sceneMetrics.Dependencies);
        }

        AppendSceneSummary(report, metrics);
        AppendSceneDetails(report, metrics);
        AppendGlobalSceneWarnings(report);
        AppendProjectDependencySummary(report);

        File.WriteAllText(AuditReportPath, report.ToString(), Encoding.UTF8);
        WriteDependencyCsv(dependencyRows);

        AssetDatabase.Refresh();
        Debug.Log($"[NureQuestPerformanceAudit] Report scritto: {Path.GetFullPath(AuditReportPath)}");
    }

    private static void AppendManagerRuntimeSettings(StringBuilder report, SceneGroupManager manager)
    {
        report.AppendLine("## Runtime Settings");
        report.AppendLine();
        report.AppendLine($"- enableLocalization: `{manager.enableLocalization}`");
        report.AppendLine($"- showLanguageSelectionOnStart: `{manager.showLanguageSelectionOnStart}`");
        report.AppendLine($"- preactivateFirstSceneDuringLanguageSelection: `{manager.preactivateFirstSceneDuringLanguageSelection}`");
        report.AppendLine($"- enforceGlobalObjectByScene: `{manager.enforceGlobalObjectByScene}`");
        report.AppendLine($"- enforceGlobalObjectContinuously: `{manager.enforceGlobalObjectContinuously}`");
        report.AppendLine($"- enforceSkyboxByScene: `{manager.enforceSkyboxByScene}`");
        report.AppendLine($"- enforceSkyboxContinuously: `{manager.enforceSkyboxContinuously}`");
        report.AppendLine($"- enableRuntimeSkyDomeFallback: `{manager.enableRuntimeSkyDomeFallback}`");
        report.AppendLine($"- forceRuntimeSkyDomeOnMobile: `{manager.forceRuntimeSkyDomeOnMobile}`");
        report.AppendLine($"- enableSceneSpawnDissolveFx: `{manager.enableSceneSpawnDissolveFx}`");
        report.AppendLine($"- disableSceneSpawnDissolveOnMobile: `{manager.disableSceneSpawnDissolveOnMobile}`");
        report.AppendLine($"- languageSelectionRestoreComponentsPerFrame: `{manager.languageSelectionRestoreComponentsPerFrame}`");
        report.AppendLine($"- languageSelectionRestoreFrameBudgetMs: `{manager.languageSelectionRestoreFrameBudgetMs}`");
        report.AppendLine();
    }

    private static SceneMetrics CollectSceneMetrics(string sceneName, GameObject root)
    {
        SceneMetrics metrics = new SceneMetrics(sceneName, root.name, GetHierarchyPath(root.transform));

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        metrics.Transforms = transforms.Length;
        metrics.GameObjects = transforms.Length;
        metrics.ActiveGameObjects = transforms.Count(t => t.gameObject.activeSelf);

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        metrics.Renderers = renderers.Length;
        metrics.EnabledRenderers = renderers.Count(r => r.enabled);
        metrics.MeshRenderers = renderers.Count(r => r is MeshRenderer);
        metrics.SkinnedMeshRenderers = renderers.Count(r => r is SkinnedMeshRenderer);
        metrics.ShadowCasters = renderers.Count(r => r.shadowCastingMode != ShadowCastingMode.Off);
        metrics.ShadowReceivers = renderers.Count(r => r.receiveShadows);
        metrics.LightProbeUsers = renderers.Count(r => r.lightProbeUsage != LightProbeUsage.Off);
        metrics.ReflectionProbeUsers = renderers.Count(r => r.reflectionProbeUsage != ReflectionProbeUsage.Off);

        metrics.Colliders = root.GetComponentsInChildren<Collider>(true).Length;
        metrics.EnabledColliders = root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled);
        metrics.Canvases = root.GetComponentsInChildren<Canvas>(true).Length;
        metrics.GraphicRaycasters = root.GetComponentsInChildren<GraphicRaycaster>(true).Length;
        metrics.Lights = root.GetComponentsInChildren<Light>(true).Length;
        metrics.EnabledRealtimeLights = root.GetComponentsInChildren<Light>(true).Count(IsRealtimeEnabledLight);
        metrics.ReflectionProbes = root.GetComponentsInChildren<ReflectionProbe>(true).Length;
        metrics.LightProbeGroups = root.GetComponentsInChildren<LightProbeGroup>(true).Length;
        metrics.ParticleSystems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
        metrics.Animators = root.GetComponentsInChildren<Animator>(true).Length;
        metrics.VideoPlayers = root.GetComponentsInChildren<VideoPlayer>(true).Length;

        var uniqueMaterials = new HashSet<Material>();
        var uniqueMeshes = new HashSet<Mesh>();
        var uniqueTextures = new HashSet<Texture>();
        var topMeshes = new List<MeshUse>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !uniqueMaterials.Add(material))
                    continue;

                CollectTextures(material, uniqueTextures);
            }

            Mesh mesh = GetRendererMesh(renderer);
            if (mesh == null)
                continue;

            uniqueMeshes.Add(mesh);
            int triangles = CountTriangles(mesh);
            metrics.TotalTriangles += triangles;
            metrics.TotalVertices += mesh.vertexCount;
            topMeshes.Add(new MeshUse(GetHierarchyPath(renderer.transform), mesh.name, triangles, mesh.vertexCount, AssetDatabase.GetAssetPath(mesh)));
        }

        metrics.UniqueMaterials = uniqueMaterials.Count;
        metrics.UniqueMeshes = uniqueMeshes.Count;
        metrics.UniqueTextures = uniqueTextures.Count;
        metrics.TextureMemoryBytes = uniqueTextures.Sum(t => SafeRuntimeMemorySize(t));
        metrics.MeshMemoryBytes = uniqueMeshes.Sum(m => SafeRuntimeMemorySize(m));
        metrics.TopMeshes = topMeshes.OrderByDescending(m => m.Triangles).Take(12).ToList();
        metrics.TopTextures = uniqueTextures
            .Select(TextureMetric.FromTexture)
            .OrderByDescending(t => t.RuntimeMemoryBytes)
            .Take(16)
            .ToList();

        metrics.Dependencies = CollectDependencies(sceneName, root);
        metrics.DependencyBytes = metrics.Dependencies.Sum(d => d.Bytes);
        return metrics;
    }

    private static bool IsRealtimeEnabledLight(Light light)
    {
        if (light == null || !light.enabled)
            return false;

        return light.lightmapBakeType != LightmapBakeType.Baked;
    }

    private static void CollectTextures(Material material, HashSet<Texture> textures)
    {
        Shader shader = material.shader;
        if (shader == null)
            return;

        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < propertyCount; i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            Texture texture = material.GetTexture(propertyName);
            if (texture != null)
                textures.Add(texture);
        }
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

    private static long SafeRuntimeMemorySize(UnityEngine.Object obj)
    {
        if (obj == null)
            return 0;

        try
        {
            return Profiler.GetRuntimeMemorySizeLong(obj);
        }
        catch
        {
            return 0;
        }
    }

    private static List<DependencyRow> CollectDependencies(string sceneName, GameObject root)
    {
        UnityEngine.Object[] dependencies = EditorUtility.CollectDependencies(new UnityEngine.Object[] { root });
        var rows = new Dictionary<string, DependencyRow>(StringComparer.OrdinalIgnoreCase);

        foreach (UnityEngine.Object dependency in dependencies)
        {
            if (dependency == null)
                continue;

            string path = AssetDatabase.GetAssetPath(dependency);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!rows.TryGetValue(path, out DependencyRow row))
            {
                row = new DependencyRow
                {
                    SceneName = sceneName,
                    Path = path,
                    Type = dependency.GetType().Name,
                    Bytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                };
                rows[path] = row;
            }
        }

        return rows.Values.OrderByDescending(r => r.Bytes).ToList();
    }

    private static void AppendSceneSummary(StringBuilder report, List<SceneMetrics> metrics)
    {
        report.AppendLine("## Scene Summary");
        report.AppendLine();
        report.AppendLine("| Scene | Tris | Verts | Renderers | Materials | Textures | Texture Mem | Mesh Mem | Lights | Colliders | Deps Size |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (SceneMetrics scene in metrics.OrderByDescending(m => m.TotalTriangles))
        {
            report.AppendLine(
                $"| {scene.SceneName} | {scene.TotalTriangles:N0} | {scene.TotalVertices:N0} | {scene.Renderers:N0} | {scene.UniqueMaterials:N0} | {scene.UniqueTextures:N0} | {FormatBytes(scene.TextureMemoryBytes)} | {FormatBytes(scene.MeshMemoryBytes)} | {scene.EnabledRealtimeLights:N0}/{scene.Lights:N0} | {scene.EnabledColliders:N0}/{scene.Colliders:N0} | {FormatBytes(scene.DependencyBytes)} |");
        }

        report.AppendLine();
    }

    private static void AppendSceneDetails(StringBuilder report, List<SceneMetrics> metrics)
    {
        foreach (SceneMetrics scene in metrics)
        {
            report.AppendLine($"## {scene.SceneName} (`{scene.RootName}`)");
            report.AppendLine();
            report.AppendLine($"- path: `{scene.RootPath}`");
            report.AppendLine($"- GameObjects: `{scene.GameObjects:N0}` (`{scene.ActiveGameObjects:N0}` activeSelf)");
            report.AppendLine($"- Renderers: `{scene.EnabledRenderers:N0}/{scene.Renderers:N0}`; MeshRenderer `{scene.MeshRenderers:N0}`, Skinned `{scene.SkinnedMeshRenderers:N0}`");
            report.AppendLine($"- Shadows: casters `{scene.ShadowCasters:N0}`, receivers `{scene.ShadowReceivers:N0}`");
            report.AppendLine($"- Probes: light users `{scene.LightProbeUsers:N0}`, reflection users `{scene.ReflectionProbeUsers:N0}`, LightProbeGroups `{scene.LightProbeGroups:N0}`, ReflectionProbes `{scene.ReflectionProbes:N0}`");
            report.AppendLine($"- UI/runtime: Canvas `{scene.Canvases:N0}`, GraphicRaycaster `{scene.GraphicRaycasters:N0}`, Animators `{scene.Animators:N0}`, VideoPlayers `{scene.VideoPlayers:N0}`, ParticleSystems `{scene.ParticleSystems:N0}`");
            report.AppendLine();

            report.AppendLine("Top meshes:");
            foreach (MeshUse mesh in scene.TopMeshes)
                report.AppendLine($"- `{mesh.ObjectPath}`: {mesh.Triangles:N0} tris, {mesh.Vertices:N0} verts, mesh `{mesh.MeshName}`, asset `{mesh.AssetPath}`");

            report.AppendLine();
            report.AppendLine("Top textures:");
            foreach (TextureMetric texture in scene.TopTextures)
            {
                report.AppendLine(
                    $"- `{texture.Path}`: {texture.Width}x{texture.Height}, {FormatBytes(texture.RuntimeMemoryBytes)}, type `{texture.Type}`, androidMax `{texture.AndroidMaxSize}`");
            }

            report.AppendLine();
            report.AppendLine("Top dependencies:");
            foreach (DependencyRow dependency in scene.Dependencies.Take(16))
                report.AppendLine($"- {FormatBytes(dependency.Bytes)} `{dependency.Path}` ({dependency.Type})");

            report.AppendLine();
        }
    }

    private static void AppendGlobalSceneWarnings(StringBuilder report)
    {
        report.AppendLine("## Global Object Search");
        report.AppendLine();
        string[] names =
        {
            "Rune_125K_20x4096",
            "Rune_250K_20x4096",
            "Rune_250K_20x4096 1",
            "PlasticoMappa",
            "Map_Low_nuvolafull_07",
            "MappaHD",
            "nuraghe",
            "Nuraghe",
            "old_olive_tree",
            "capanna",
        };

        foreach (string name in names)
        {
            Transform[] matches = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t != null && t.gameObject.scene.IsValid() && t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            report.AppendLine($"- `{name}`: `{matches.Length}` matches");
            foreach (Transform match in matches.Take(12))
                report.AppendLine($"  - `{GetHierarchyPath(match)}` activeSelf=`{match.gameObject.activeSelf}` activeInHierarchy=`{match.gameObject.activeInHierarchy}`");
        }

        report.AppendLine();
    }

    private static void AppendProjectDependencySummary(StringBuilder report)
    {
        report.AppendLine("## Build Scene Dependencies");
        report.AppendLine();

        string[] dependencies = AssetDatabase.GetDependencies(MainScenePath, true);
        long totalBytes = 0;
        var rows = new List<DependencyRow>();
        foreach (string path in dependencies)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                continue;

            long bytes = new FileInfo(path).Length;
            totalBytes += bytes;
            rows.Add(new DependencyRow { SceneName = "MainScene", Path = path, Type = Path.GetExtension(path), Bytes = bytes });
        }

        report.AppendLine($"- Dependency count: `{rows.Count:N0}`");
        report.AppendLine($"- Raw source size: `{FormatBytes(totalBytes)}`");
        report.AppendLine();
        foreach (DependencyRow row in rows.OrderByDescending(r => r.Bytes).Take(30))
            report.AppendLine($"- {FormatBytes(row.Bytes)} `{row.Path}`");

        report.AppendLine();
    }

    private static void WriteDependencyCsv(List<DependencyRow> rows)
    {
        var sb = new StringBuilder(rows.Count * 128);
        sb.AppendLine("Scene,Bytes,MB,Type,Path");
        foreach (DependencyRow row in rows.OrderByDescending(r => r.Bytes))
        {
            sb.Append(EscapeCsv(row.SceneName)).Append(',')
                .Append(row.Bytes).Append(',')
                .Append((row.Bytes / (1024d * 1024d)).ToString("0.###")).Append(',')
                .Append(EscapeCsv(row.Type)).Append(',')
                .Append(EscapeCsv(row.Path)).AppendLine();
        }

        File.WriteAllText(DependencyReportPath, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (value == null)
            return string.Empty;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
            return string.Empty;

        var names = new Stack<string>();
        Transform cursor = transform;
        while (cursor != null)
        {
            names.Push(cursor.name);
            cursor = cursor.parent;
        }

        return string.Join("/", names);
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
            return $"{bytes / gb:0.##} GB";
        if (bytes >= mb)
            return $"{bytes / mb:0.##} MB";
        if (bytes >= kb)
            return $"{bytes / kb:0.##} KB";

        return $"{bytes} B";
    }

    private sealed class SceneMetrics
    {
        public readonly string SceneName;
        public readonly string RootName;
        public readonly string RootPath;
        public int GameObjects;
        public int ActiveGameObjects;
        public int Transforms;
        public int Renderers;
        public int EnabledRenderers;
        public int MeshRenderers;
        public int SkinnedMeshRenderers;
        public int ShadowCasters;
        public int ShadowReceivers;
        public int LightProbeUsers;
        public int ReflectionProbeUsers;
        public int Colliders;
        public int EnabledColliders;
        public int Canvases;
        public int GraphicRaycasters;
        public int Lights;
        public int EnabledRealtimeLights;
        public int ReflectionProbes;
        public int LightProbeGroups;
        public int ParticleSystems;
        public int Animators;
        public int VideoPlayers;
        public int UniqueMaterials;
        public int UniqueMeshes;
        public int UniqueTextures;
        public long TotalTriangles;
        public long TotalVertices;
        public long TextureMemoryBytes;
        public long MeshMemoryBytes;
        public long DependencyBytes;
        public List<MeshUse> TopMeshes = new List<MeshUse>();
        public List<TextureMetric> TopTextures = new List<TextureMetric>();
        public List<DependencyRow> Dependencies = new List<DependencyRow>();

        public SceneMetrics(string sceneName, string rootName, string rootPath)
        {
            SceneName = sceneName;
            RootName = rootName;
            RootPath = rootPath;
        }
    }

    private readonly struct MeshUse
    {
        public readonly string ObjectPath;
        public readonly string MeshName;
        public readonly int Triangles;
        public readonly int Vertices;
        public readonly string AssetPath;

        public MeshUse(string objectPath, string meshName, int triangles, int vertices, string assetPath)
        {
            ObjectPath = objectPath;
            MeshName = meshName;
            Triangles = triangles;
            Vertices = vertices;
            AssetPath = assetPath;
        }
    }

    private readonly struct TextureMetric
    {
        public readonly string Path;
        public readonly int Width;
        public readonly int Height;
        public readonly long RuntimeMemoryBytes;
        public readonly string Type;
        public readonly int AndroidMaxSize;

        private TextureMetric(string path, int width, int height, long runtimeMemoryBytes, string type, int androidMaxSize)
        {
            Path = path;
            Width = width;
            Height = height;
            RuntimeMemoryBytes = runtimeMemoryBytes;
            Type = type;
            AndroidMaxSize = androidMaxSize;
        }

        public static TextureMetric FromTexture(Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            int androidMax = 0;
            string type = texture.GetType().Name;

            TextureImporter importer = !string.IsNullOrEmpty(path) ? AssetImporter.GetAtPath(path) as TextureImporter : null;
            if (importer != null)
            {
                type = importer.textureType.ToString();
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("Android");
                androidMax = settings != null && settings.overridden ? settings.maxTextureSize : importer.maxTextureSize;
            }

            return new TextureMetric(
                string.IsNullOrEmpty(path) ? texture.name : path,
                texture.width,
                texture.height,
                SafeRuntimeMemorySize(texture),
                type,
                androidMax);
        }
    }

    private sealed class DependencyRow
    {
        public string SceneName;
        public string Path;
        public string Type;
        public long Bytes;
    }
}
