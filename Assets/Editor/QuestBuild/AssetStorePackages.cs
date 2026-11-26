using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Asset Store packs may not be shared in a public repository, so they are downloaded separately.
// This window lists the missing ones; their materials are converted to URP right after the import.
public class AssetStorePackages : EditorWindow
{
    public class Package
    {
        public string Name;
        public string Folder;
        public string Url;
        // an asset the game uses from the pack
        public string MarkerGuid;

        public bool IsImported => !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(MarkerGuid));
    }

    public static readonly Package[] Packages =
    {
        new Package
        {
            Name = "Fantasy Spider",
            Folder = "Assets/Spiders",
            Url = "https://assetstore.unity.com/packages/3d/characters/animals/insects/fantasy-spider-236418",
            MarkerGuid = "fd02805ee801de64a828e8190cac7784"
        },
        new Package
        {
            Name = "Sci-Fi Construction Kit (Modular)",
            Folder = "Assets/SciFi Warehouse Kit",
            Url = "https://assetstore.unity.com/packages/3d/environments/sci-fi/sci-fi-construction-kit-modular-159280",
            MarkerGuid = "b2452a5c2cd649a4db9eebcd16f7b155"
        },
    };

    private const string ShownKey = "AssetStorePackages.Shown";

    [InitializeOnLoadMethod]
    private static void ShowWhenMissing()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(ShownKey, false) || Packages.All(p => p.IsImported)) return;
            SessionState.SetBool(ShownKey, true);
            Open();
        };
    }

    [MenuItem("Tools/Asset Store Packages")]
    public static void Open()
    {
        var window = GetWindow<AssetStorePackages>(true, "Asset Store Packages");
        window.minSize = new Vector2(460f, 300f);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "These free Asset Store packs are not in the repository and have to be imported once:\n" +
            "1. Open the page and click \"Add to My Assets\".\n" +
            "2. In Unity: Window > Package Manager > My Assets, select the pack, Download, then Import with all files.\n" +
            "Keep the folders where the import puts them. Materials are converted to URP automatically.",
            MessageType.Info);

        foreach (var package in Packages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField((package.IsImported ? "OK  " : "MISSING  ") + package.Name);
                if (GUILayout.Button("Open in Asset Store", GUILayout.Width(150f)))
                    Application.OpenURL(package.Url);
            }
        }

        if (Packages.All(p => p.IsImported))
            EditorGUILayout.HelpBox("All packs are imported.", MessageType.None);
    }

    private void OnFocus() => Repaint();

    public static void ConvertMaterials(string folder)
    {
        var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
        int converted = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material == null || material.shader == null) continue;
            var upgrader = upgraders.FirstOrDefault(u => u.OldShaderPath == material.shader.name);
            if (upgrader == null) continue;

            MaterialUpgrader.Upgrade(material, upgrader, MaterialUpgrader.UpgradeFlags.None);
            EditorUtility.SetDirty(material);
            converted++;
        }

        if (converted == 0) return;
        AssetDatabase.SaveAssets();
        Debug.Log($"Converted {converted} materials in {folder} to URP.");
    }
}

class AssetStoreMaterialConverter : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var package in AssetStorePackages.Packages)
        {
            if (!imported.Any(path => path.StartsWith(package.Folder + "/") && path.EndsWith(".mat"))) continue;
            // assets must not be changed while the import is still running
            EditorApplication.delayCall += () => AssetStorePackages.ConvertMaterials(package.Folder);
        }
    }
}
