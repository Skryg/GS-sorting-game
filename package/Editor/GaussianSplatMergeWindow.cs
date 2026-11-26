// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GaussianSplatting.Editor.Utils;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GaussianSplatting.Editor
{
    // Saves a copy of a splat asset with fewer splats. The source asset is never changed.
    public class GaussianSplatMergeWindow : EditorWindow
    {
        enum Quality { High, SameAsSource }

        const string kProgressTitle = "Merge Splats";
        const string kMergedLabel = "MergedSplats";
        const float kDefaultRatio = 0.2f;

        [SerializeField] GaussianSplatAsset m_Source;
        [SerializeField] int m_Target;
        [SerializeField] Quality m_Quality = Quality.High;
        [SerializeField] string m_Name = "";
        [SerializeField] string m_SuggestedName = "";

        string Folder => Path.GetDirectoryName(AssetDatabase.GetAssetPath(m_Source)).Replace('\\', '/');
        string OutputPath => $"{Folder}/{m_Name}.asset";

        [MenuItem("Tools/Gaussian Splats/Merge Splats")]
        static void Open()
        {
            var window = GetWindow<GaussianSplatMergeWindow>("Merge Splats");
            window.minSize = new Vector2(380, 260);
            if (Selection.activeObject is GaussianSplatAsset selected)
                window.SetSource(selected);
        }

        void OnSelectionChange()
        {
            if (m_Source == null && Selection.activeObject is GaussianSplatAsset selected)
                SetSource(selected);
            Repaint();
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (GaussianSplatAsset)EditorGUILayout.ObjectField("Splat asset", m_Source, typeof(GaussianSplatAsset), false);
            if (EditorGUI.EndChangeCheck())
                SetSource(picked);
            if (m_Source == null)
            {
                EditorGUILayout.HelpBox("Pick an imported splat asset.", MessageType.Info);
                return;
            }

            bool games = m_Source is GaussianGaMeSSplatAsset;
            EditorGUILayout.LabelField(" ", $"{(games ? "GaMeS" : "3DGS")}, {m_Source.splatCount:N0} splats", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            m_Target = EditorGUILayout.IntField("Target splats", m_Target);
            if (EditorGUI.EndChangeCheck())
                SuggestName();
            EditorGUI.BeginChangeCheck();
            float percent = EditorGUILayout.Slider("% of the source", 100f * m_Target / m_Source.splatCount, 1f, 99f);
            if (EditorGUI.EndChangeCheck())
            {
                m_Target = Mathf.RoundToInt(percent * 0.01f * m_Source.splatCount);
                SuggestName();
            }

            m_Quality = (Quality)EditorGUILayout.EnumPopup(new GUIContent("Quality",
                "Data is compressed for the second time. With Medium sources that loses more than merging itself, High avoids it at about twice the size per splat."), m_Quality);
            m_Name = EditorGUILayout.TextField("Name of the copy", m_Name);

            if (games)
                EditorGUILayout.HelpBox("The copy is a plain 3DGS asset, it cannot bend. Keep the original in prefabs with GSVerse or SplatGrabBend.", MessageType.Info);

            string error = FindError();
            if (error != null)
                EditorGUILayout.HelpBox(error, MessageType.Error);
            else if (File.Exists(OutputPath))
                EditorGUILayout.HelpBox("This copy already exists and will be replaced.", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(error != null))
            {
                if (GUILayout.Button("Merge", GUILayout.Height(30)))
                    EditorApplication.delayCall += Run;
            }
            EditorGUILayout.LabelField(OutputPath, EditorStyles.centeredGreyMiniLabel);
        }

        void SetSource(GaussianSplatAsset source)
        {
            m_Source = source;
            if (source == null)
                return;
            m_Target = Mathf.Max(1, Mathf.RoundToInt(source.splatCount * kDefaultRatio));
            m_Name = "";
            SuggestName();
        }

        void SuggestName()
        {
            string name = $"{m_Source.name}_merged_{(m_Target >= 1000 ? Mathf.RoundToInt(m_Target / 1000f) + "k" : m_Target.ToString())}";
            if (m_Name == "" || m_Name == m_SuggestedName)
                m_Name = name;
            m_SuggestedName = name;
        }

        string FindError()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Leave Play mode first.";
            if (!GaussianSplatCloud.CanDecode(m_Source.colorFormat, out var reason)) return reason + ".";
            if (m_Target < 1 || m_Target >= m_Source.splatCount) return $"The target has to be between 1 and {m_Source.splatCount - 1}.";
            if (string.IsNullOrWhiteSpace(m_Name) || m_Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Enter a valid name.";
            // also compares without case, the file system may not distinguish it
            if (string.Equals(OutputPath, AssetDatabase.GetAssetPath(m_Source), StringComparison.OrdinalIgnoreCase)) return "The copy needs a different name than the source.";
            if (File.Exists(OutputPath) ? !IsMergedCopy(OutputPath) : HasDataFiles()) return "Other files already have this name, choose another one.";
            return null;
        }

        static bool IsMergedCopy(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            return asset != null && Array.IndexOf(AssetDatabase.GetLabels(asset), kMergedLabel) >= 0;
        }

        // an asset can be renamed without its data files
        bool HasDataFiles()
        {
            return Array.Exists(new[] { "chk", "pos", "oth", "col", "shs" }, suffix => File.Exists(DataPath(suffix)));
        }

        string DataPath(string suffix) => $"{Folder}/{m_Name}_{suffix}.bytes";

        void Run()
        {
            if (m_Source == null || FindError() != null)
                return;

            try
            {
                var watch = Stopwatch.StartNew();
                EditorUtility.DisplayProgressBar(kProgressTitle, "Reading splats", 0f);
                var cloud = GaussianSplatCloud.Decode(ReadAsset(m_Source));
                var merged = GaussianSplatMerger.Merge(cloud, m_Target, t =>
                    !EditorUtility.DisplayCancelableProgressBar(kProgressTitle, "Merging splats", 0.05f + 0.85f * t));
                if (merged == null)
                    return;

                EditorUtility.DisplayProgressBar(kProgressTitle, "Saving", 0.95f);
                var result = Save(merged);
                Debug.Log($"{OutputPath}: merged {m_Source.splatCount:N0} splats into {result.splatCount:N0} in {watch.Elapsed.TotalSeconds:0.0} s.", result);
                if (result.splatCount > m_Target)
                    Debug.LogWarning($"{OutputPath}: the target of {m_Target:N0} splats was not reached.", result);
                Selection.activeObject = result;
                EditorGUIUtility.PingObject(result);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static GaussianSplatRawData ReadAsset(GaussianSplatAsset asset)
        {
            return new GaussianSplatRawData
            {
                count = asset.splatCount,
                posFormat = asset.posFormat,
                scaleFormat = asset.scaleFormat,
                colorFormat = asset.colorFormat,
                shFormat = asset.shFormat,
                pos = asset.posData.bytes,
                other = asset.otherData.bytes,
                color = asset.colorData.bytes,
                sh = asset.shData.bytes,
                chunks = asset.chunkData?.bytes
            };
        }

        GaussianSplatAsset Save(GaussianSplatCloud cloud)
        {
            // clustering is a separate, lossy step, so clustered SH is stored per splat instead
            var shFormat = m_Source.shFormat > GaussianSplatAsset.SHFormat.Norm6 ? GaussianSplatAsset.SHFormat.Norm6 : m_Source.shFormat;
            var posFormat = m_Source.posFormat;
            var scaleFormat = m_Source.scaleFormat;
            var colorFormat = m_Source.colorFormat;
            if (m_Quality == Quality.High)
            {
                // lower enum values are more precise
                posFormat = Min(posFormat, GaussianSplatAsset.VectorFormat.Norm16);
                scaleFormat = Min(scaleFormat, GaussianSplatAsset.VectorFormat.Norm16);
                colorFormat = Min(colorFormat, GaussianSplatAsset.ColorFormat.Float16x4);
                shFormat = Min(shFormat, GaussianSplatAsset.SHFormat.Norm11);
            }
            var data = cloud.Encode(posFormat, scaleFormat, colorFormat, shFormat, out var boundsMin, out var boundsMax);

            var asset = CreateInstance<GaussianSplatAsset>();
            asset.name = m_Name;
            asset.Initialize(data.count, data.posFormat, data.scaleFormat, data.colorFormat, data.shFormat, boundsMin, boundsMax, m_Source.cameras);

            var hash = new Hash128((uint)data.count, (uint)asset.formatVersion, 0, 0);
            var chunkFile = data.chunks != null ? WriteFile("chk", data.chunks, ref hash) : null;
            var posFile = WriteFile("pos", data.pos, ref hash);
            var otherFile = WriteFile("oth", data.other, ref hash);
            var colorFile = WriteFile("col", data.color, ref hash);
            var shFile = WriteFile("shs", data.sh, ref hash);
            asset.SetDataHash(hash);
            asset.SetAssetFiles(chunkFile, posFile, otherFile, colorFile, shFile);

            // an existing copy is updated in place, so that references to it stay valid
            var existing = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(OutputPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, OutputPath);
                AssetDatabase.SetLabels(asset, new[] { kMergedLabel });
                existing = asset;
            }
            else
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                DestroyImmediate(asset);
            }
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        static T Min<T>(T a, T b) where T : Enum => Comparer<T>.Default.Compare(a, b) <= 0 ? a : b;

        TextAsset WriteFile(string suffix, byte[] bytes, ref Hash128 hash)
        {
            string path = DataPath(suffix);
            hash.Append(bytes);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
    }
}
