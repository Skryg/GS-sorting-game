using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Lists sortable prefabs and their categories without loading the prefabs themselves (splat data is heavy).
// Rebuilt automatically in the editor by GameObjectCatalogBuilder; do not edit by hand.
public class GameObjectCatalog : ScriptableObject
{
    public const string ResourcePath = "GameObjectCatalog";
    public const string PrefabFolder = "Prefabs/GameObjects";

    [Serializable]
    public struct Entry
    {
        public string resourcePath;
        public ObjectCategory category;
    }

    public List<Entry> entries = new();

    public static GameObjectCatalog Load()
    {
        var catalog = Resources.Load<GameObjectCatalog>(ResourcePath);
        if (catalog == null)
            Debug.LogError($"Resources/{ResourcePath}.asset not found. Use Tools > Rebuild Game Object Catalog in the editor.");
        return catalog;
    }

    public List<ObjectCategory> Categories =>
        entries.Where(e => e.category != null).Select(e => e.category).Distinct().ToList();

    public List<GameObject> LoadPrefabs(ObjectCategory category)
    {
        var prefabs = new List<GameObject>();
        foreach (var entry in entries.Where(e => e.category == category))
        {
            var prefab = Resources.Load<GameObject>(entry.resourcePath);
            if (prefab != null) prefabs.Add(prefab);
            else Debug.LogWarning($"Catalog entry '{entry.resourcePath}' could not be loaded.");
        }
        return prefabs;
    }
}
