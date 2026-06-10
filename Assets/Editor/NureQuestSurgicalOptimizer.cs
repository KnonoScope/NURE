using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class NureQuestSurgicalOptimizer
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string MeshOutputFolder = "Assets/Created/QuestSurgicalMeshes";
    private const string MaterialOutputFolder = "Assets/Created/QuestSurgicalMaterials";
    private const string ReportPath = "Logs/nure_quest_surgical_optimizer_report.md";
    private const int CapannaRoofTargetTriangles = 100000;
    private const int CapannaPaliMicroTargetTriangles = 3200;
    private const int CapannaCordaMicroTargetTriangles = 2600;
    private const int Scena1TreeLargeTargetTriangles = 12000;
    private const int Scena1TreeMediumTargetTriangles = 3000;
    private const int Scena1TreeSmallTargetTriangles = 900;
    private const int Scena1RuneModelTargetTriangles = 150000;
    private const bool OptimizeScena1RunePlastico = false;

    [MenuItem("Codex/Quest Performance/Surgical Optimize Level 1")]
    public static void SurgicalOptimizeLevel1Menu()
    {
        SurgicalOptimizeLevel1Batch();
    }

    public static void SurgicalOptimizeLevel1Batch()
    {
        Directory.CreateDirectory("Logs");
        EnsureFolder(MeshOutputFolder);
        EnsureFolder(MaterialOutputFolder);
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        MeshFilter[] filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        var materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<OptimizationRow>(64);
        var materialRows = new List<MaterialOptimizationRow>(16);
        int rendererFlagsUpdated = 0;

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null || !filter.gameObject.scene.IsValid())
                continue;

            Mesh source = filter.sharedMesh;
            int sourceTriangles = CountTriangles(source);
            string objectPath = GetHierarchyPath(filter.transform);
            Renderer renderer = filter.GetComponent<Renderer>();

            if (renderer != null && ShouldApplyStaticQuestRendererFlags(objectPath) && ApplyStaticQuestRendererFlags(renderer))
                rendererFlagsUpdated++;

            if (renderer != null && IsScena1OldOliveTreePath(objectPath))
                OptimizeScena1TreeMaterials(renderer, materialCache, materialRows);

            OptimizationPlan plan = BuildPlan(filter, source, sourceTriangles);
            if (!plan.ShouldOptimize)
                continue;

            string cacheKey = GetMeshCacheKey(source, plan.TargetTriangles, plan.Kind);
            if (!meshCache.TryGetValue(cacheKey, out Mesh optimized))
            {
                optimized = CreateOrLoadOptimizedMesh(source, plan, sourceTriangles);
                if (optimized == null)
                    continue;

                meshCache.Add(cacheKey, optimized);
            }

            filter.sharedMesh = optimized;
            rows.Add(new OptimizationRow
            {
                Kind = plan.Kind,
                ObjectPath = objectPath,
                SourceAssetPath = AssetDatabase.GetAssetPath(source),
                OptimizedAssetPath = AssetDatabase.GetAssetPath(optimized),
                SourceTriangles = sourceTriangles,
                OptimizedTriangles = CountTriangles(optimized),
            });
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WriteReport(rows, materialRows, rendererFlagsUpdated);
        Debug.Log($"[NureQuestSurgicalOptimizer] Ottimizzazione chirurgica Level 3 alberi/micro: {rows.Count} MeshFilter, {materialRows.Count} materiali, {rendererFlagsUpdated} renderer flags. Report: {Path.GetFullPath(ReportPath)}");
    }

    private static OptimizationPlan BuildPlan(MeshFilter filter, Mesh source, int sourceTriangles)
    {
        if (sourceTriangles <= 0)
            return OptimizationPlan.Skip;

        string objectPath = GetHierarchyPath(filter.transform);
        string meshName = source.name ?? string.Empty;
        string sourcePath = AssetDatabase.GetAssetPath(source) ?? string.Empty;
        string combined = objectPath + " " + meshName + " " + sourcePath;

        if (IsScena1OldOliveTreePath(objectPath))
        {
            int treeTarget = GetScena1TreeTargetTriangles(sourceTriangles);
            if (treeTarget > 0 && sourceTriangles > treeTarget)
                return new OptimizationPlan(true, treeTarget, "scena1-old-olive-tree-max");
        }

        if (IsScenePath(objectPath, "Scena1") || IsScenePath(objectPath, "Scena4"))
        {
            if (combined.IndexOf("sistema_pali_Mesh.002_pali", StringComparison.OrdinalIgnoreCase) >= 0
                && sourceTriangles > CapannaPaliMicroTargetTriangles)
            {
                return new OptimizationPlan(true, CapannaPaliMicroTargetTriangles, "capanna-pali-micro");
            }

            if (combined.IndexOf("corda_Mesh.003", StringComparison.OrdinalIgnoreCase) >= 0
                && sourceTriangles > CapannaCordaMicroTargetTriangles)
            {
                return new OptimizationPlan(true, CapannaCordaMicroTargetTriangles, "capanna-corda-micro");
            }
        }

        if (OptimizeScena1RunePlastico
            && objectPath.IndexOf("Scena1/Rune_250K_20x4096 1/Model", StringComparison.OrdinalIgnoreCase) >= 0
            && sourceTriangles > Scena1RuneModelTargetTriangles)
        {
            return new OptimizationPlan(true, Scena1RuneModelTargetTriangles, "scena1-rune-plastico");
        }

        if (combined.IndexOf("tetto_Mesh.004_paglia", StringComparison.OrdinalIgnoreCase) >= 0
            && sourceTriangles > CapannaRoofTargetTriangles)
        {
            return new OptimizationPlan(true, CapannaRoofTargetTriangles, "capanna-roof-level2");
        }

        return OptimizationPlan.Skip;
    }

    private static Mesh CreateOrLoadOptimizedMesh(Mesh source, OptimizationPlan plan, int sourceTriangles)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string safeName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(sourcePath)}_{source.name}_{plan.Kind}_{plan.TargetTriangles}");
        string outputPath = $"{MeshOutputFolder}/{safeName}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
        if (existing != null)
            return existing;

        Mesh optimized = BuildClusteredMesh(source, plan.TargetTriangles);
        if (optimized == null)
            return null;

        optimized.name = $"QuestSurgical_{source.name}_{plan.TargetTriangles}";
        int optimizedTriangles = CountTriangles(optimized);
        if (optimizedTriangles <= 0 || optimizedTriangles >= sourceTriangles)
            return null;

        AssetDatabase.CreateAsset(optimized, outputPath);
        return optimized;
    }

    private static Mesh BuildClusteredMesh(Mesh source, int targetTriangles)
    {
        Vector3[] sourceVertices = source.vertices;
        if (sourceVertices == null || sourceVertices.Length == 0)
            return null;

        Vector3[] sourceNormals = source.normals;
        Vector2[] sourceUvs = source.uv;
        Bounds bounds = source.bounds;

        Mesh bestMesh = null;
        int bestDelta = int.MaxValue;
        int[] gridCandidates =
        {
            8, 10, 12, 14, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 72, 80, 88, 96, 112, 128, 144, 160, 192, 224, 256
        };

        for (int i = 0; i < gridCandidates.Length; i++)
        {
            Mesh candidate = BuildClusteredMeshForGrid(source, sourceVertices, sourceNormals, sourceUvs, bounds, gridCandidates[i]);
            if (candidate == null)
                continue;

            int triangles = CountTriangles(candidate);
            if (triangles <= 0)
                continue;

            int delta = Mathf.Abs(triangles - targetTriangles);
            bool betterBelowTarget = triangles <= targetTriangles && (bestMesh == null || triangles > CountTriangles(bestMesh));
            bool closestFallback = bestMesh == null || delta < bestDelta;

            if (betterBelowTarget || closestFallback)
            {
                bestMesh = candidate;
                bestDelta = delta;
            }
        }

        return bestMesh;
    }

    private static Mesh BuildClusteredMeshForGrid(
        Mesh source,
        Vector3[] sourceVertices,
        Vector3[] sourceNormals,
        Vector2[] sourceUvs,
        Bounds bounds,
        int grid)
    {
        Vector3 min = bounds.min;
        Vector3 size = bounds.size;
        size.x = Mathf.Max(size.x, 0.0001f);
        size.y = Mathf.Max(size.y, 0.0001f);
        size.z = Mathf.Max(size.z, 0.0001f);

        var clusters = new List<VertexCluster>(sourceVertices.Length);
        var clusterByKey = new Dictionary<long, int>(sourceVertices.Length);
        int[] vertexToCluster = new int[sourceVertices.Length];

        for (int i = 0; i < sourceVertices.Length; i++)
        {
            Vector3 p = sourceVertices[i];
            int x = Mathf.Clamp(Mathf.FloorToInt(((p.x - min.x) / size.x) * grid), 0, grid - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(((p.y - min.y) / size.y) * grid), 0, grid - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(((p.z - min.z) / size.z) * grid), 0, grid - 1);
            long key = PackKey(x, y, z);

            if (!clusterByKey.TryGetValue(key, out int clusterIndex))
            {
                clusterIndex = clusters.Count;
                clusterByKey.Add(key, clusterIndex);
                clusters.Add(new VertexCluster());
            }

            VertexCluster cluster = clusters[clusterIndex];
            cluster.Position += p;
            if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
                cluster.Normal += sourceNormals[i];
            if (sourceUvs != null && sourceUvs.Length == sourceVertices.Length)
                cluster.Uv += sourceUvs[i];
            cluster.Count++;
            clusters[clusterIndex] = cluster;
            vertexToCluster[i] = clusterIndex;
        }

        Vector3[] vertices = new Vector3[clusters.Count];
        Vector3[] normals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length ? new Vector3[clusters.Count] : null;
        Vector2[] uvs = sourceUvs != null && sourceUvs.Length == sourceVertices.Length ? new Vector2[clusters.Count] : null;

        for (int i = 0; i < clusters.Count; i++)
        {
            VertexCluster cluster = clusters[i];
            float invCount = 1f / Mathf.Max(1, cluster.Count);
            vertices[i] = cluster.Position * invCount;
            if (normals != null)
                normals[i] = cluster.Normal.sqrMagnitude > 0.000001f ? cluster.Normal.normalized : Vector3.up;
            if (uvs != null)
                uvs[i] = cluster.Uv * invCount;
        }

        var mesh = new Mesh
        {
            indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            vertices = vertices
        };
        if (normals != null)
            mesh.normals = normals;
        if (uvs != null)
            mesh.uv = uvs;

        mesh.subMeshCount = source.subMeshCount;
        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            int[] sourceTriangles = source.GetTriangles(submesh);
            var remapped = new List<int>(sourceTriangles.Length);
            var seenTriangles = new HashSet<TriangleKey>();

            for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
            {
                int a = vertexToCluster[sourceTriangles[i]];
                int b = vertexToCluster[sourceTriangles[i + 1]];
                int c = vertexToCluster[sourceTriangles[i + 2]];

                if (a == b || b == c || a == c)
                    continue;

                var key = new TriangleKey(a, b, c);
                if (!seenTriangles.Add(key))
                    continue;

                remapped.Add(a);
                remapped.Add(b);
                remapped.Add(c);
            }

            mesh.SetTriangles(remapped, submesh, true);
        }

        mesh.RecalculateBounds();
        if (normals == null)
            mesh.RecalculateNormals();
        return mesh;
    }

    private static string GetMeshCacheKey(Mesh mesh, int targetTriangles, string kind)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        return $"{path}|{mesh.name}|{targetTriangles}|{kind}";
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

    private static int GetScena1TreeTargetTriangles(int sourceTriangles)
    {
        if (sourceTriangles > 30000)
            return Scena1TreeLargeTargetTriangles;
        if (sourceTriangles > 8000)
            return Scena1TreeMediumTargetTriangles;
        if (sourceTriangles > 1500)
            return Scena1TreeSmallTargetTriangles;

        return 0;
    }

    private static bool IsScena1OldOliveTreePath(string objectPath)
    {
        return IsScenePath(objectPath, "Scena1")
            && objectPath.IndexOf("old_olive_tree", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsScenePath(string objectPath, string sceneName)
    {
        return !string.IsNullOrEmpty(objectPath)
            && objectPath.StartsWith(sceneName + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldApplyStaticQuestRendererFlags(string objectPath)
    {
        return IsScenePath(objectPath, "Scena1") || IsScenePath(objectPath, "Scena4");
    }

    private static bool ApplyStaticQuestRendererFlags(Renderer renderer)
    {
        bool changed = false;
        if (renderer.shadowCastingMode != ShadowCastingMode.Off)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            changed = true;
        }

        if (renderer.receiveShadows)
        {
            renderer.receiveShadows = false;
            changed = true;
        }

        if (renderer.lightProbeUsage != LightProbeUsage.Off)
        {
            renderer.lightProbeUsage = LightProbeUsage.Off;
            changed = true;
        }

        if (renderer.reflectionProbeUsage != ReflectionProbeUsage.Off)
        {
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            changed = true;
        }

        if (renderer.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion)
        {
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            changed = true;
        }

        return changed;
    }

    private static void OptimizeScena1TreeMaterials(
        Renderer renderer,
        Dictionary<string, Material> materialCache,
        List<MaterialOptimizationRow> rows)
    {
        Material[] materials = renderer.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material source = materials[i];
            if (source == null)
                continue;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (sourcePath.StartsWith(MaterialOutputFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            string cacheKey = $"{sourcePath}|{source.name}|scena1-tree-mobile";
            if (!materialCache.TryGetValue(cacheKey, out Material optimized))
            {
                optimized = CreateOrLoadScena1TreeMaterial(source, out int sourceTextures, out int optimizedTextures);
                if (optimized == null)
                    continue;

                materialCache.Add(cacheKey, optimized);
                rows.Add(new MaterialOptimizationRow
                {
                    SourceMaterial = source.name,
                    SourceAssetPath = sourcePath,
                    OptimizedAssetPath = AssetDatabase.GetAssetPath(optimized),
                    SourceTextures = sourceTextures,
                    OptimizedTextures = optimizedTextures,
                });
            }

            if (optimized != null && optimized != source)
            {
                materials[i] = optimized;
                changed = true;
            }
        }

        if (changed)
            renderer.sharedMaterials = materials;
    }

    private static Material CreateOrLoadScena1TreeMaterial(Material source, out int sourceTextures, out int optimizedTextures)
    {
        sourceTextures = CountAssignedTextures(source);

        string sourcePath = AssetDatabase.GetAssetPath(source);
        string safeName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(sourcePath)}_{source.name}_scena1_tree_mobile");
        string outputPath = $"{MaterialOutputFolder}/{safeName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(outputPath);
        if (existing != null)
        {
            optimizedTextures = CountAssignedTextures(existing);
            return existing;
        }

        Material optimized = new Material(source)
        {
            name = $"{source.name}_Scena1TreeMobile",
            enableInstancing = true
        };

        StripTreeMaterialTextures(optimized);
        SetMaterialFloatIfPresent(optimized, "_Metallic", 0f);
        SetMaterialFloatIfPresent(optimized, "_Smoothness", 0.18f);
        SetMaterialFloatIfPresent(optimized, "_Glossiness", 0.18f);
        SetMaterialFloatIfPresent(optimized, "_EnvironmentReflections", 0f);
        optimized.DisableKeyword("_NORMALMAP");
        optimized.DisableKeyword("_PARALLAXMAP");
        optimized.DisableKeyword("_OCCLUSIONMAP");
        optimized.DisableKeyword("_DETAIL_MULX2");
        optimized.DisableKeyword("_METALLICSPECGLOSSMAP");
        optimized.DisableKeyword("_EMISSION");
        optimized.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");

        AssetDatabase.CreateAsset(optimized, outputPath);
        optimizedTextures = CountAssignedTextures(optimized);
        return optimized;
    }

    private static void StripTreeMaterialTextures(Material material)
    {
        List<string> textureProperties = GetAssignedTextureProperties(material);
        if (textureProperties.Count == 0)
            return;

        string baseTextureProperty = textureProperties.Find(IsLikelyBaseTextureProperty)
            ?? textureProperties.Find(property => !IsLikelyExpensiveTextureProperty(property))
            ?? textureProperties[0];

        for (int i = 0; i < textureProperties.Count; i++)
        {
            string property = textureProperties[i];
            if (string.Equals(property, baseTextureProperty, StringComparison.Ordinal))
                continue;

            material.SetTexture(property, null);
        }
    }

    private static List<string> GetAssignedTextureProperties(Material material)
    {
        var properties = new List<string>();
        if (material == null || material.shader == null)
            return properties;

        int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
        for (int i = 0; i < propertyCount; i++)
        {
            if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
            if (material.GetTexture(propertyName) != null)
                properties.Add(propertyName);
        }

        return properties;
    }

    private static int CountAssignedTextures(Material material)
    {
        return GetAssignedTextureProperties(material).Count;
    }

    private static bool IsLikelyBaseTextureProperty(string property)
    {
        if (string.IsNullOrWhiteSpace(property))
            return false;

        string lower = property.ToLowerInvariant();
        if (IsLikelyExpensiveTextureProperty(lower))
            return false;

        return lower.Contains("base")
            || lower.Contains("main")
            || lower.Contains("albedo")
            || lower.Contains("color");
    }

    private static bool IsLikelyExpensiveTextureProperty(string property)
    {
        if (string.IsNullOrWhiteSpace(property))
            return false;

        string lower = property.ToLowerInvariant();
        return lower.Contains("normal")
            || lower.Contains("bump")
            || lower.Contains("metal")
            || lower.Contains("rough")
            || lower.Contains("occlusion")
            || lower.Contains("height")
            || lower.Contains("parallax")
            || lower.Contains("detail")
            || lower.Contains("emission");
    }

    private static void SetMaterialFloatIfPresent(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var parts = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent)
            parts.Push(current.name);
        return string.Join("/", parts);
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "mesh";

        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value.Replace(' ', '_');
    }

    private static long PackKey(int x, int y, int z)
    {
        return ((long)x << 42) | ((long)y << 21) | (uint)z;
    }

    private static void WriteReport(List<OptimizationRow> rows, List<MaterialOptimizationRow> materialRows, int rendererFlagsUpdated)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("# NURE Quest Surgical Optimizer Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Scene: `{MainScenePath}`");
        sb.AppendLine("Mode: `Level 3 Scena1 tree max + Nuraghe micro`");
        sb.AppendLine($"Capanna roof target: `{CapannaRoofTargetTriangles:N0}` tris");
        sb.AppendLine($"Scena1 old olive tree targets: `{Scena1TreeLargeTargetTriangles:N0}` / `{Scena1TreeMediumTargetTriangles:N0}` / `{Scena1TreeSmallTargetTriangles:N0}` tris");
        sb.AppendLine($"Capanna micro targets: pali `{CapannaPaliMicroTargetTriangles:N0}` tris, corda `{CapannaCordaMicroTargetTriangles:N0}` tris");
        sb.AppendLine($"Scena1 Rune plastico optimization: `{OptimizeScena1RunePlastico}`");
        sb.AppendLine($"Renderer static Quest flags updated: `{rendererFlagsUpdated:N0}`");
        sb.AppendLine();
        sb.AppendLine("| Kind | Object | Before | After | Reduction | Optimized Mesh |");
        sb.AppendLine("|---|---|---:|---:|---:|---|");

        foreach (OptimizationRow row in rows)
        {
            float reduction = row.SourceTriangles <= 0
                ? 0f
                : 1f - ((float)row.OptimizedTriangles / row.SourceTriangles);
            sb.AppendLine($"| {row.Kind} | `{row.ObjectPath}` | {row.SourceTriangles:N0} | {row.OptimizedTriangles:N0} | {reduction:P1} | `{row.OptimizedAssetPath}` |");
        }

        sb.AppendLine();
        sb.AppendLine("## Material Optimizations");
        sb.AppendLine();
        sb.AppendLine("| Source | Before Tex | After Tex | Optimized Material |");
        sb.AppendLine("|---|---:|---:|---|");
        foreach (MaterialOptimizationRow row in materialRows)
            sb.AppendLine($"| `{row.SourceAssetPath}/{row.SourceMaterial}` | {row.SourceTextures:N0} | {row.OptimizedTextures:N0} | `{row.OptimizedAssetPath}` |");

        File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
    }

    private readonly struct OptimizationPlan
    {
        public static readonly OptimizationPlan Skip = new OptimizationPlan(false, 0, string.Empty);

        public OptimizationPlan(bool shouldOptimize, int targetTriangles, string kind)
        {
            ShouldOptimize = shouldOptimize;
            TargetTriangles = targetTriangles;
            Kind = kind;
        }

        public bool ShouldOptimize { get; }
        public int TargetTriangles { get; }
        public string Kind { get; }
    }

    private struct VertexCluster
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
        public int Count;
    }

    private readonly struct TriangleKey : IEquatable<TriangleKey>
    {
        private readonly int _a;
        private readonly int _b;
        private readonly int _c;

        public TriangleKey(int a, int b, int c)
        {
            if (a > b)
                Swap(ref a, ref b);
            if (b > c)
                Swap(ref b, ref c);
            if (a > b)
                Swap(ref a, ref b);

            _a = a;
            _b = b;
            _c = c;
        }

        public bool Equals(TriangleKey other)
        {
            return _a == other._a && _b == other._b && _c == other._c;
        }

        public override bool Equals(object obj)
        {
            return obj is TriangleKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _a;
                hash = (hash * 397) ^ _b;
                hash = (hash * 397) ^ _c;
                return hash;
            }
        }

        private static void Swap(ref int left, ref int right)
        {
            int temp = left;
            left = right;
            right = temp;
        }
    }

    private struct OptimizationRow
    {
        public string Kind;
        public string ObjectPath;
        public string SourceAssetPath;
        public string OptimizedAssetPath;
        public int SourceTriangles;
        public int OptimizedTriangles;
    }

    private struct MaterialOptimizationRow
    {
        public string SourceMaterial;
        public string SourceAssetPath;
        public string OptimizedAssetPath;
        public int SourceTextures;
        public int OptimizedTextures;
    }
}
