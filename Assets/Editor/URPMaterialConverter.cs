using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class URPMaterialConverter
{
    // Nome dello shader URP target
    private const string TargetShaderName = "Universal Render Pipeline/Lit";

    [MenuItem("Tools/NURE/Convert All Materials To URP Lit")]
    public static void ConvertAllMaterialsToUrp()
    {
        Shader targetShader = Shader.Find(TargetShaderName);
        if (targetShader == null)
        {
            Debug.LogError($"[URPMaterialConverter] Shader non trovato: {TargetShaderName}. " +
                           "Assicurati che il progetto sia URP e che lo shader esista.");
            return;
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int total = materialGuids.Length;
        int changedShader = 0;
        int assignedTextures = 0;

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
                    "Converting Materials to URP",
                    $"Processing: {mat.name}",
                    (float)i / total
                );

                bool dirty = false;

                // 1) Imposta shader URP Lit se diverso
                if (mat.shader != targetShader)
                {
                    mat.shader = targetShader;
                    dirty = true;
                    changedShader++;
                }

                // 2) Se manca la texture base, prova ad assegnarla
                if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null)
                {
                    Texture2D tex = FindTextureForMaterial(mat);
                    if (tex != null)
                    {
                        mat.SetTexture("_BaseMap", tex);
                        dirty = true;
                        assignedTextures++;
                        // Se vuoi, puoi anche settare il tiling/offset di default qui
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(mat);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[URPMaterialConverter] Completato. " +
                  $"Material con shader cambiato: {changedShader}, " +
                  $"Texture assegnate: {assignedTextures}.");
    }

    /// <summary>
    /// Cerca una texture da associare al materiale in base al nome.
    /// Convenzione: stessa stringa nel nome (es. Material 'Rock_01' -> Texture 'Rock_01', 'Rock_01_BaseColor', ecc.).
    /// </summary>
    private static Texture2D FindTextureForMaterial(Material mat)
    {
        // Base name senza eventuali "(Instance)" o spazi extra
        string baseName = mat.name;
        int idx = baseName.IndexOf('(');
        if (idx >= 0)
            baseName = baseName.Substring(0, idx).Trim();

        // Cerca texture il cui nome contenga il nome del materiale
        // Esempio: "Rock_01" trova "Rock_01", "Rock_01_BaseColor", ecc.
        string[] texGuids = AssetDatabase.FindAssets($"{baseName} t:Texture2D");

        if (texGuids.Length == 0)
            return null;

        // Prendiamo la prima trovata (se vuoi puoi migliorare la logica qui)
        string texPath = AssetDatabase.GUIDToAssetPath(texGuids[0]);
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        if (tex != null)
        {
            Debug.Log($"[URPMaterialConverter] Assegno texture '{tex.name}' al material '{mat.name}'.");
        }

        return tex;
    }
}
