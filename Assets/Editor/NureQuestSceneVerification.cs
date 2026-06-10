using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NureQuestSceneVerification
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string ReportPath = "Logs/nure_quest_surgical_verification.md";
    private const string OptimizedMeshFolder = "Assets/Created/QuestSurgicalMeshes";

    [MenuItem("Codex/Quest Performance/Verify Surgical Meshes")]
    public static void VerifySurgicalMeshesMenu()
    {
        VerifySurgicalMeshesBatch();
    }

    public static void VerifySurgicalMeshesBatch()
    {
        Directory.CreateDirectory("Logs");
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        MeshFilter[] filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        var runeRows = new List<Row>();
        var optimizedRows = new List<Row>();

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            string objectPath = GetHierarchyPath(filter.transform);
            string assetPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            var row = new Row
            {
                ObjectPath = objectPath,
                MeshName = filter.sharedMesh.name,
                Triangles = CountTriangles(filter.sharedMesh),
                Vertices = filter.sharedMesh.vertexCount,
                AssetPath = assetPath,
                ActiveSelf = filter.gameObject.activeSelf,
                ActiveInHierarchy = filter.gameObject.activeInHierarchy,
                UsesSurgicalAsset = assetPath.StartsWith(OptimizedMeshFolder, StringComparison.OrdinalIgnoreCase),
            };

            if (objectPath.IndexOf("Rune_250K", StringComparison.OrdinalIgnoreCase) >= 0
                || assetPath.IndexOf("Rune_250K", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                runeRows.Add(row);
            }

            if (row.UsesSurgicalAsset)
                optimizedRows.Add(row);
        }

        WriteReport(runeRows.OrderBy(r => r.ObjectPath).ToList(), optimizedRows.OrderBy(r => r.ObjectPath).ToList());
        AssetDatabase.Refresh();
        Debug.Log($"[NureQuestSceneVerification] Report scritto: {Path.GetFullPath(ReportPath)}");
    }

    private static void WriteReport(List<Row> runeRows, List<Row> optimizedRows)
    {
        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("# NURE Quest Surgical Verification");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Scene: `{MainScenePath}`");
        sb.AppendLine();

        sb.AppendLine("## Rune Meshes");
        sb.AppendLine();
        AppendTable(sb, runeRows);
        sb.AppendLine();

        sb.AppendLine("## Surgical Mesh Assignments");
        sb.AppendLine();
        AppendTable(sb, optimizedRows);

        File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
    }

    private static void AppendTable(StringBuilder sb, List<Row> rows)
    {
        sb.AppendLine("| Object | Tris | Verts | Mesh | Asset | Surgical | Active |");
        sb.AppendLine("|---|---:|---:|---|---|---|---|");

        foreach (Row row in rows)
        {
            sb.AppendLine($"| `{row.ObjectPath}` | {row.Triangles:N0} | {row.Vertices:N0} | `{row.MeshName}` | `{row.AssetPath}` | `{row.UsesSurgicalAsset}` | `{row.ActiveSelf}/{row.ActiveInHierarchy}` |");
        }
    }

    private static int CountTriangles(Mesh mesh)
    {
        int triangles = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            triangles += (int)mesh.GetIndexCount(i) / 3;
        return triangles;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var parts = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent)
            parts.Push(current.name);
        return string.Join("/", parts);
    }

    private struct Row
    {
        public string ObjectPath;
        public string MeshName;
        public int Triangles;
        public int Vertices;
        public string AssetPath;
        public bool ActiveSelf;
        public bool ActiveInHierarchy;
        public bool UsesSurgicalAsset;
    }
}
