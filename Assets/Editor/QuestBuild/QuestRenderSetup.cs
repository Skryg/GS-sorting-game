using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

// One-time project setup for the native Quest build: URP with the splat renderer feature, single pass stereo,
// Vulkan + ARM64 on Android, and conversion of the Built-in materials. Safe to run again.
static class QuestRenderSetup
{
    const string Folder = "Assets/Settings/URP";
    const string RendererPath = Folder + "/QuestRenderer.asset";
    const string PipelinePath = Folder + "/QuestPipeline.asset";

    [MenuItem("Tools/Quest Build/Set Up URP and Convert Materials")]
    static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Quest Build", "Leave Play mode first.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Quest Build",
                "This switches the project to URP and rewrites all Built-in materials in place. Save open scenes first.",
                "Continue", "Cancel"))
            return;

        AssetDatabase.SaveAssets();

        var pipeline = CreateOrLoadPipeline();
        GraphicsSettings.defaultRenderPipeline = pipeline;
        AssignToAllQualityLevels(pipeline);
        ConfigureStereo();
        ConfigureAndroid();
        AssetDatabase.SaveAssets();

        ConvertMaterials();
        Debug.Log("Quest Build: URP is active and the materials were converted. Check the console for materials that could not be converted.", pipeline);
    }

    [MenuItem("Tools/Quest Build/Convert Materials Only")]
    static void ConvertMaterials()
    {
        // URP 17.3's Converters.RunInBatchMode throws on an abstract converter type, so the upgraders are driven directly.
        var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
        var materials = MaterialUpgrader.FetchAllUpgradableMaterialsForPipeline(typeof(UniversalRenderPipelineAsset));
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < materials.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Quest Build", $"Converting {materials[i].name}", i / (float)materials.Count);
                MaterialUpgrader.Upgrade(materials[i], upgraders, MaterialUpgrader.UpgradeFlags.None);
                EditorUtility.SetDirty(materials[i]);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        int samples = ConvertXRISampleMaterials();
        AssetDatabase.SaveAssets();
        if (samples > 0)
            Debug.Log($"Quest Build: moved {samples} XRI sample materials to their Shader Graph versions.");

        // Materials embedded in models are generated on import, a reimport recreates them for URP.
        var models = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var guid in models)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        Debug.Log($"Quest Build: converted {materials.Count} materials and reimported {models.Length} models.");
    }

    // The XRI starter assets ship a Built-in only fresnel shader next to Shader Graph versions that also target URP.
    static int ConvertXRISampleMaterials()
    {
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Samples" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (material == null || material.shader == null) continue;
            string shaderPath = AssetDatabase.GetAssetPath(material.shader);
            if (!shaderPath.EndsWith("/BiRP_Fresnel.shader")) continue;

            string folder = System.IO.Path.GetDirectoryName(shaderPath).Replace('\\', '/');
            string graph = material.name == "Interactable" ? "Interactable" : "Unlit_Fresnel";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>($"{folder}/{graph}.shadergraph");
            if (shader == null) continue;

            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            Color rim = material.HasProperty("_RimColor") ? material.GetColor("_RimColor") : Color.white;
            material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_RimColor")) material.SetColor("_RimColor", rim);
            EditorUtility.SetDirty(material);
            count++;
        }
        return count;
    }

    static UniversalRenderPipelineAsset CreateOrLoadPipeline()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Settings", "URP");

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, RendererPath);
        }
        ConfigureRenderer(renderer);

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }
        ConfigurePipeline(pipeline);
        return pipeline;
    }

    static void ConfigureRenderer(UniversalRendererData renderer)
    {
        renderer.renderingMode = RenderingMode.Forward;
        renderer.depthPrimingMode = DepthPrimingMode.Disabled;
        // Auto lets URP render straight to the eye buffer on Quest instead of paying for an intermediate texture.
        renderer.intermediateTextureMode = IntermediateTextureMode.Auto;

        if (!renderer.rendererFeatures.Exists(feature => feature is GaussianSplatURPFeature))
        {
            var feature = ScriptableObject.CreateInstance<GaussianSplatURPFeature>();
            feature.name = "Gaussian Splats";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        EditorUtility.SetDirty(renderer);
    }

    static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
    {
        // These fields are not all public, the names match URP 17's serialized layout.
        var so = new SerializedObject(pipeline);
        SetBool(so, "m_SupportsHDR", false);
        SetBool(so, "m_RequireDepthTexture", false);
        SetBool(so, "m_RequireOpaqueTexture", false);
        SetBool(so, "m_UseSRPBatcher", true);
        SetBool(so, "m_AdditionalLightShadowsSupported", false);
        SetBool(so, "m_SoftShadowsSupported", false);
        SetInt(so, "m_MSAA", (int)MsaaQuality._4x);
        SetFloat(so, "m_RenderScale", 1f);
        SetFloat(so, "m_ShadowDistance", 15f);
        SetInt(so, "m_ShadowCascadeCount", 1);
        SetInt(so, "m_MainLightShadowmapResolution", (int)UnityEngine.Rendering.Universal.ShadowResolution._1024);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);
    }

    static void SetBool(SerializedObject so, string name, bool value) => Find(so, name).boolValue = value;
    static void SetInt(SerializedObject so, string name, int value) => Find(so, name).intValue = value;
    static void SetFloat(SerializedObject so, string name, float value) => Find(so, name).floatValue = value;

    static SerializedProperty Find(SerializedObject so, string name)
    {
        var property = so.FindProperty(name);
        if (property == null)
            throw new System.InvalidOperationException($"URP asset has no serialized field {name}.");
        return property;
    }

    static void AssignToAllQualityLevels(RenderPipelineAsset pipeline)
    {
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(current, false);
    }

    static void ConfigureStereo()
    {
        foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null) continue;
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(settings);
        }
    }

    static void ConfigureAndroid()
    {
        // Splats need compute with wave intrinsics, so the GLES fallback could never draw them.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
    }
}
