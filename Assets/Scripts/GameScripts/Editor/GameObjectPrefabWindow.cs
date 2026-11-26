using System.Collections.Generic;
using System.IO;
using System.Linq;
using GaussianSplatting.Editor;
using GaussianSplatting.Runtime;
using GaussianSplatting.Runtime.GaMeS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Creates a sortable prefab in Resources/Prefabs/GameObjects with the same components as the existing ones.
public class GameObjectPrefabWindow : EditorWindow
{
    private enum ColliderType { SplatMesh, CustomMesh, Box, Sphere, Capsule }

    private const string PrefabFolder = "Assets/Resources/" + GameObjectCatalog.PrefabFolder;
    private const string LayerName = "DraggableObject";
    private const float DefaultLongestSide = 0.2f;
    private const float ThrowAngularVelocityScale = 1.2f;

    [SerializeField] private GaussianSplatAsset splat;
    [SerializeField] private string prefabName = "";
    [SerializeField] private string suggestedName = "";
    [SerializeField] private string typeName = "";
    [SerializeField] private ObjectCategory category;
    [SerializeField] private float scale = 1f;
    [SerializeField] private float mass = 1f;
    [SerializeField] private ColliderType colliderType = ColliderType.Box;
    [SerializeField] private Mesh customMesh;
    [SerializeField] private bool mirrorCustomMesh = true;
    [SerializeField] private bool deformable;
    [SerializeField] private Vector2 scroll;

    private GaussianGaMeSSplatAsset Games => splat as GaussianGaMeSSplatAsset;
    private string PrefabPath => $"{PrefabFolder}/{prefabName}.prefab";
    private bool UsesGeneratedMesh => colliderType == ColliderType.SplatMesh || (colliderType == ColliderType.CustomMesh && mirrorCustomMesh);

    [MenuItem("Tools/Gaussian Splats/Create Game Object Prefab")]
    private static void Open()
    {
        var window = GetWindow<GameObjectPrefabWindow>("Game Object Prefab");
        window.minSize = new Vector2(380, 420);
        if (window.splat == null) window.SetSplat(Selection.activeObject as GaussianSplatAsset);
    }

    // The asset creator selects what it has just created.
    private void OnSelectionChange()
    {
        if (splat == null && Selection.activeObject is GaussianSplatAsset selected)
            SetSplat(selected);
        Repaint();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSplat();
        if (splat != null)
        {
            DrawIdentity();
            DrawSize();
            DrawCollider();
            DrawDeformation();
        }

        EditorGUILayout.Space();
        string error = FindError();
        if (error != null && splat != null)
            EditorGUILayout.HelpBox(error, MessageType.Error);

        using (new EditorGUI.DisabledScope(error != null))
        {
            if (GUILayout.Button("Create Prefab", GUILayout.Height(30)))
                EditorApplication.delayCall += Create;
        }
        EditorGUILayout.LabelField(splat != null ? PrefabPath : PrefabFolder, EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.EndScrollView();
    }

    private void DrawSplat()
    {
        EditorGUILayout.LabelField("Point cloud", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var picked = (GaussianSplatAsset)EditorGUILayout.ObjectField("Splat asset", splat, typeof(GaussianSplatAsset), false);
        if (EditorGUI.EndChangeCheck()) SetSplat(picked);

        if (splat == null)
            EditorGUILayout.HelpBox("Pick an imported splat asset, or import a .ply first. A newly imported one is picked automatically.", MessageType.Info);
        else
            EditorGUILayout.LabelField(" ", $"{(Games != null ? "GaMeS" : "3DGS")}, {splat.splatCount:N0} splats", EditorStyles.miniLabel);

        if (GUILayout.Button("Import .ply..."))
        {
            SetSplat(null);
            GaussianSplatAssetCreator.Init();
        }
    }

    private void DrawIdentity()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Object", EditorStyles.boldLabel);
        prefabName = EditorGUILayout.TextField("Prefab name", prefabName);
        typeName = EditorGUILayout.TextField(new GUIContent("Type name", "Empty = the prefab name."), typeName);
        category = (ObjectCategory)EditorGUILayout.ObjectField("Category", category, typeof(ObjectCategory), false);
        mass = Mathf.Max(0.001f, EditorGUILayout.FloatField("Mass (kg)", mass));
    }

    private void DrawSize()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Size", EditorStyles.boldLabel);
        Vector3 size = ObjectBounds().size;
        float longest = Mathf.Max(size.x, size.y, size.z);
        scale = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Scale", scale));

        EditorGUI.BeginChangeCheck();
        float side = EditorGUILayout.FloatField("Longest side (m)", longest * scale);
        if (EditorGUI.EndChangeCheck() && side > 0f && longest > 0f)
            scale = side / longest;

        size *= scale;
        EditorGUILayout.LabelField(" ", $"{size.x:0.###} x {size.y:0.###} x {size.z:0.###} m", EditorStyles.miniLabel);
    }

    private void DrawCollider()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Collider", EditorStyles.boldLabel);
        if (deformable) colliderType = ColliderType.SplatMesh;

        using (new EditorGUI.DisabledScope(deformable))
        {
            colliderType = (ColliderType)EditorGUILayout.EnumPopup(new GUIContent("Type", "Splat Mesh is the mesh a GaMeS object was trained on."),
                colliderType, type => (ColliderType)type != ColliderType.SplatMesh || Games != null, false);
        }

        if (colliderType == ColliderType.CustomMesh)
        {
            customMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", customMesh, typeof(Mesh), false);
            mirrorCustomMesh = EditorGUILayout.Toggle(new GUIContent("Mirror X",
                "Unity flips X when it imports a model, the splats are not flipped. Keep it on for a mesh that has the same coordinates as the point cloud."), mirrorCustomMesh);
            if (customMesh != null && !MatchesSplats(customMesh.bounds, mirrorCustomMesh))
                EditorGUILayout.HelpBox("The mesh has a different size or position than the splats.", MessageType.Warning);
        }
        else if (colliderType != ColliderType.SplatMesh)
        {
            EditorGUILayout.HelpBox("Fitted to the bounds of all splats, stray ones included. Adjust it in the prefab if needed.", MessageType.None);
        }
    }

    private void DrawDeformation()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Deformation", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(Games == null))
            deformable = EditorGUILayout.Toggle(new GUIContent("Deformable", "Bends while it is held (GSVerse + SplatGrabBend)."), deformable);

        if (Games == null)
        {
            deformable = false;
            EditorGUILayout.HelpBox("Only GaMeS splats can deform.", MessageType.None);
            return;
        }
        if (!deformable) return;

        if (splat.shFormat > GaussianSplatAsset.SHFormat.Norm6)
            EditorGUILayout.HelpBox("Clustered SH cannot deform on the GPU, the slow CPU path will be used. Import with Medium quality or better.", MessageType.Warning);
        if (!GaMeSAssetRebuilder.CanRebuild(Games, out _) && GUILayout.Button("Bake Splat Order"))
            EditorApplication.delayCall += BakeOrder;
    }

    private string FindError()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Leave Play mode first.";
        if (splat == null) return "Pick a splat asset.";
        if (LayerMask.NameToLayer(LayerName) < 0) return $"Layer {LayerName} does not exist in the project.";
        if (string.IsNullOrWhiteSpace(prefabName)) return "Enter the prefab name.";
        if (prefabName != prefabName.Trim()) return "The prefab name starts or ends with a space.";
        if (prefabName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The prefab name has characters that are not allowed in a file name.";
        if (File.Exists(PrefabPath)) return $"{prefabName}.prefab already exists, choose another name.";
        if (category == null) return "Pick a category, objects without one are never spawned.";

        if (colliderType == ColliderType.SplatMesh)
        {
            if (Games == null) return "Only GaMeS splats have a mesh, choose another collider.";
            var mesh = FindSplatMesh();
            if (mesh == null) return $"Mesh '{Games.objPath}' not found in Resources.";
            if (!mesh.isReadable) return "The mesh is not readable, turn on Read/Write in its import settings.";
        }
        if (colliderType == ColliderType.CustomMesh)
        {
            if (customMesh == null) return "Pick the collider mesh.";
            if (mirrorCustomMesh && !customMesh.isReadable) return "The mesh is not readable, turn on Read/Write in its import settings.";
        }
        if (deformable && !GaMeSAssetRebuilder.CanRebuild(Games, out var reason)) return "Cannot deform: " + reason + ".";
        return null;
    }

    private void SetSplat(GaussianSplatAsset asset)
    {
        splat = asset;
        if (asset == null) return;

        string name = SuggestName(asset);
        if (prefabName == "" || prefabName == suggestedName) prefabName = name;
        suggestedName = name;

        colliderType = FindSplatMesh() != null ? ColliderType.SplatMesh : ColliderType.Box;
        customMesh = null;
        deformable = false;

        Vector3 size = ObjectBounds().size;
        float longest = Mathf.Max(size.x, size.y, size.z);
        scale = longest > 0f ? DefaultLongestSide / longest : 1f;
    }

    // Splat assets are usually all called points_cloud, the folder says what they are.
    private static string SuggestName(GaussianSplatAsset asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        string folder = Path.GetFileName(Path.GetDirectoryName(path));
        string source = string.IsNullOrEmpty(folder) || folder == "GaussianAssets" ? asset.name : folder;
        var words = source.Split(new[] { '_', '-', ' ', '.' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
    }

    private Mesh FindSplatMesh()
    {
        if (Games == null || string.IsNullOrEmpty(Games.objPath)) return null;
        var model = Resources.Load<GameObject>(Games.objPath);
        if (model == null || model.transform.childCount == 0) return null;
        var filter = model.transform.GetChild(0).GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private Bounds SplatBounds()
    {
        var bounds = new Bounds();
        bounds.SetMinMax(splat.boundsMin, splat.boundsMax);
        return bounds;
    }

    // The bounds of a 3DGS cloud include stray splats, a mesh made for it is more accurate.
    private Bounds ObjectBounds()
    {
        return colliderType == ColliderType.CustomMesh && customMesh != null ? customMesh.bounds : SplatBounds();
    }

    private bool MatchesSplats(Bounds mesh, bool mirrored)
    {
        Vector3 center = mesh.center;
        if (mirrored) center.x = -center.x;
        var target = SplatBounds();
        float tolerance = 0.25f * Mathf.Max(target.size.x, target.size.y, target.size.z);
        return (center - target.center).magnitude < tolerance && (mesh.size - target.size).magnitude < tolerance;
    }

    private void BakeOrder()
    {
        if (Games == null) return;
        bool baked;
        try
        {
            EditorUtility.DisplayProgressBar("Bake GaMeS Splat Order", Games.name, 0.5f);
            baked = GaMeSSplatOrderBaker.Bake(Games, AssetDatabase.GetAssetPath(Games));
            AssetDatabase.SaveAssetIfDirty(Games);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        if (!baked)
            EditorUtility.DisplayDialog("Bake Splat Order", "The splat order could not be baked, the reason is in the console.", "OK");
        Repaint();
    }

    private void Create()
    {
        if (FindError() != null) return;

        var scene = EditorSceneManager.NewPreviewScene();
        GameObject prefab;
        try
        {
            var root = new GameObject(prefabName) { layer = LayerMask.NameToLayer(LayerName) };
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.localScale = Vector3.one * scale;

            root.AddComponent<GaussianSplatRenderer>().m_Asset = splat;

            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            AddCollider(root);
            root.AddComponent<DragObject>();

            var id = root.AddComponent<ObjectIdentifier>();
            id.category = category;
            id.typeName = string.IsNullOrWhiteSpace(typeName) ? prefabName : typeName.Trim();

            root.AddComponent<XRGrabInteractable>().throwAngularVelocityScale = ThrowAngularVelocityScale;

            if (deformable)
            {
                root.AddComponent<GSVerse>();
                root.AddComponent<SplatGrabBend>();
            }

            Directory.CreateDirectory(PrefabFolder);
            prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved || prefab == null)
            {
                Debug.LogError($"{PrefabPath} could not be saved.");
                return;
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }

        Debug.Log($"Created {AssetDatabase.GetAssetPath(prefab)}.", prefab);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        AssetDatabase.OpenAsset(prefab);

        prefabName = typeName = suggestedName = "";
        SetSplat(null);
        Repaint();
    }

    private void AddCollider(GameObject root)
    {
        var bounds = SplatBounds();
        switch (colliderType)
        {
            case ColliderType.Box:
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                box.providesContacts = true;
                break;

            case ColliderType.Sphere:
                var sphere = root.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                sphere.providesContacts = true;
                break;

            case ColliderType.Capsule:
                var capsule = root.AddComponent<CapsuleCollider>();
                Vector3 e = bounds.extents;
                int axis = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
                capsule.center = bounds.center;
                capsule.direction = axis;
                capsule.height = bounds.size[axis];
                capsule.radius = Mathf.Max(e[(axis + 1) % 3], e[(axis + 2) % 3]);
                capsule.providesContacts = true;
                break;

            default:
                var mesh = root.AddComponent<MeshCollider>();
                mesh.cookingOptions = MeshColliderCookingOptions.None;
                mesh.convex = true;
                mesh.providesContacts = true;
                mesh.sharedMesh = UsesGeneratedMesh ? CreateColliderMesh() : customMesh;
                break;
        }
    }

    // The imported mesh is mirrored relative to the splats, so the collider gets its own copy next to the splat data.
    private Mesh CreateColliderMesh()
    {
        bool splatMesh = colliderType == ColliderType.SplatMesh;
        var source = splatMesh ? FindSplatMesh() : customMesh;
        var copy = GaMeSUtils.TransformMesh(Instantiate(source), splatMesh && Games.useMeshLeftHandedCS);

        var indices = new List<int>();
        for (int i = 0; i < copy.subMeshCount; i++)
            indices.AddRange(copy.GetTriangles(i));

        // physics only needs the shape
        var mesh = new Mesh { name = prefabName + "_collider", indexFormat = copy.indexFormat };
        mesh.SetVertices(copy.vertices);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        DestroyImmediate(copy);

        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(splat)).Replace('\\', '/');
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{mesh.name}.asset"));
        return mesh;
    }
}
