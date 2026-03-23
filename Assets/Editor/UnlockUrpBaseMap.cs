using UnityEditor;
using UnityEngine;

public static class UnlockUrpBaseMaps
{
    private const string TargetShaderName = "Universal Render Pipeline/Lit";

    [MenuItem("Tools/NURE/Unlock URP BaseMaps + Assign Textures")]
    public static void UnlockBaseMapsAndAssignTextures()
    {
        Shader targetShader = Shader.Find(TargetShaderName);
        if (targetShader == null)
        {
            Debug.LogError($"[UnlockUrpBaseMaps] Shader non trovato: {TargetShaderName}.");
            return;
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int total = materialGuids.Length;

        int processed = 0;
        int texturesAssigned = 0;

        try
        {
            for (int i = 0; i < total; i++)
            {
                string guid = materialGuids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null)
                    continue;

                EditorUtility.DisplayProgressBar(
                    "Unlocking URP BaseMaps",
                    $"Processing: {mat.name}",
                    (float)i / total
                );

                // Considera solo i materiali URP Lit
                if (mat.shader == null || mat.shader.name != TargetShaderName)
                    continue;

                if (!mat.HasProperty("_BaseMap"))
                    continue;

                processed++;

                // Se non ha ancora una BaseMap, proviamo ad assegnarne una
                if (mat.GetTexture("_BaseMap") == null)
                {
                    Texture2D tex = FindTextureForMaterial(mat);
                    if (tex != null)
                    {
                        mat.SetTexture("_BaseMap", tex);
                        EditorUtility.SetDirty(mat);
                        texturesAssigned++;

                        Debug.Log($"[UnlockUrpBaseMaps] Assegnata '{tex.name}' come BaseMap a '{mat.name}'.");
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[UnlockUrpBaseMaps] Completato. Material URP elaborati: {processed}, " +
                  $"texture BaseMap assegnate: {texturesAssigned}.");
    }

    /// <summary>
    /// Cerca una texture compatibile per nome.
    /// Es: materiale 'tetto_Mesh.004_pagliaMat' → texture con nome contenente 'tetto_Mesh.004_pagliaMat'
    /// oppure con lo stesso pezzo base del nome.
    /// </summary>
    private static Texture2D FindTextureForMaterial(Material mat)
    {
        // Pulisce il nome da eventuali "(Instance)" ecc.
        string baseName = mat.name;
        int idx = baseName.IndexOf('(');
        if (idx >= 0)
            baseName = baseName.Substring(0, idx).Trim();

        // Primo tentativo: match diretto
        string[] texGuids = AssetDatabase.FindAssets($"{baseName} t:Texture2D");
        if (texGuids.Length == 0)
        {
            // Secondo tentativo: usa solo la parte prima di un underscore
            string[] parts = baseName.Split('_');
            if (parts.Length > 1)
            {
                string shortName = parts[0];
                texGuids = AssetDatabase.FindAssets($"{shortName} t:Texture2D");
            }
        }

        if (texGuids.Length == 0)
            return null;

        string texPath = AssetDatabase.GUIDToAssetPath(texGuids[0]);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
    }
}
