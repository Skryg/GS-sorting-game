using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Keeps Resources/GameObjectCatalog.asset in sync with the prefabs in Resources/Prefabs/GameObjects.
public class GameObjectCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
{
    private const string PrefabFolder = "Assets/Resources/" + GameObjectCatalog.PrefabFolder;
    private const string CatalogPath = "Assets/Resources/" + GameObjectCatalog.ResourcePath + ".asset";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Rebuild();

    [InitializeOnLoadMethod]
    private static void RebuildAfterReload()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.delayCall += Rebuild;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsSortablePrefab))
            Rebuild();
    }

    private static bool IsSortablePrefab(string path) =>
        path.StartsWith(PrefabFolder + "/") && path.EndsWith(".prefab");

    [MenuItem("Tools/Rebuild Game Object Catalog")]
    public static void Rebuild()
    {
        var entries = new List<GameObjectCatalog.Entry>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var id = prefab != null ? prefab.GetComponent<ObjectIdentifier>() : null;
            if (id == null || id.category == null)
            {
                Debug.LogWarning($"{path} has no ObjectIdentifier with a category, it will not be spawned.", prefab);
                continue;
            }

            entries.Add(new GameObjectCatalog.Entry
            {
                resourcePath = path.Substring("Assets/Resources/".Length).Replace(".prefab", ""),
                category = id.category
            });
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.resourcePath, b.resourcePath));

        var catalog = AssetDatabase.LoadAssetAtPath<GameObjectCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<GameObjectCatalog>();
            catalog.entries = entries;
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return;
        }

        if (catalog.entries.SequenceEqual(entries)) return;

        catalog.entries = entries;
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
    }
}
