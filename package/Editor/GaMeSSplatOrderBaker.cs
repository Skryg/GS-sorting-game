// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using GaussianSplatting.Runtime;
using GaussianSplatting.Runtime.GaMeS;
using GaussianSplatting.Shared;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GaussianSplatting.Editor
{
    // The importer stores splats in Morton order, while GaMeS deformation works per mesh face.
    // This recovers the mapping from data already in the project: splat positions decoded from the asset
    // are matched against positions computed from the mesh and alpha data, exactly as GSBase computes them.
    public static class GaMeSSplatOrderBaker
    {
        const float kMaxErrorOfBounds = 0.01f;

        [MenuItem("Tools/Gaussian Splats/Bake GaMeS Splat Order")]
        public static void BakeAll()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(GaussianGaMeSSplatAsset));
            int baked = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("Bake GaMeS Splat Order", path, (float)i / guids.Length);
                    var asset = AssetDatabase.LoadAssetAtPath<GaussianGaMeSSplatAsset>(path);
                    if (asset != null && Bake(asset, path)) baked++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"GaMeS splat order baked for {baked}/{guids.Length} asset(s).");
        }

        public static bool Bake(GaussianGaMeSSplatAsset asset, string assetPath)
        {
            int count = asset.splatCount;
            int perFace = asset.numberOfSplatsPerFace;
            if (perFace <= 0 || asset.alphaData == null || asset.posData == null)
            {
                Debug.LogError($"{assetPath}: missing GaMeS data, skipped.", asset);
                return false;
            }

            var meshPositions = ComputeMeshPositions(asset);
            if (meshPositions == null)
            {
                Debug.LogError($"{assetPath}: mesh '{asset.objPath}' not found in Resources or does not match the splat count, skipped.", asset);
                return false;
            }

            var assetPositions = DecodeAssetPositions(asset);
            var order = Match(assetPositions, meshPositions, out var errors);

            float3 size = (float3)asset.boundsMax - (float3)asset.boundsMin;
            float limit = math.length(size) * kMaxErrorOfBounds;
            if (order == null)
            {
                Debug.LogError($"{assetPath}: splat positions could not be matched to the mesh; nothing was written.", asset);
                return false;
            }

            // Greedy matching can swap a handful of near-coincident splats in dense areas, which only swaps
            // colors between neighbours. A wrong mesh or transform shifts everything, so judge by the bulk.
            double errorSum = 0;
            int outliers = 0;
            foreach (var e in errors)
            {
                errorSum += e;
                if (e > limit) outliers++;
            }
            float meanError = (float)(errorSum / errors.Length);
            Array.Sort(errors);
            float p999 = errors[(int)((errors.Length - 1) * 0.999f)];
            float maxError = errors[errors.Length - 1];

            string stats = $"mean error {meanError:G3}, 99.9% {p999:G3}, max {maxError:G3}, allowed {limit:G3}, {outliers} splat(s) over the limit";
            if (p999 > limit)
            {
                Debug.LogError($"{assetPath}: splat positions do not match the mesh ({stats}). " +
                               "The asset was probably imported with a different mesh or transform; nothing was written.", asset);
                return false;
            }

            var orderPath = Path.Combine(Path.GetDirectoryName(assetPath), Path.GetFileNameWithoutExtension(assetPath) + "_order.bytes").Replace('\\', '/');
            var bytes = new byte[count * 4];
            Buffer.BlockCopy(order, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(orderPath, bytes);
            AssetDatabase.ImportAsset(orderPath);

            asset.SetSplatOrderData(AssetDatabase.LoadAssetAtPath<TextAsset>(orderPath));
            EditorUtility.SetDirty(asset);
            Debug.Log($"{assetPath}: splat order baked ({count} splats, {stats}).", asset);
            return true;
        }

        // Mirrors GSBase: Resources mesh -> TransformMesh -> face vertices -> barycentric alphas.
        static float3[] ComputeMeshPositions(GaussianGaMeSSplatAsset asset)
        {
            var loaded = Resources.Load<GameObject>(asset.objPath);
            var source = loaded != null && loaded.transform.childCount > 0 ? loaded.transform.GetChild(0).GetComponent<MeshFilter>() : null;
            if (source == null || source.sharedMesh == null) return null;

            var mesh = GaMeSUtils.TransformMesh(Object.Instantiate(source.sharedMesh), asset.useMeshLeftHandedCS);
            try
            {
                int perFace = asset.numberOfSplatsPerFace;
                int faceCount = asset.splatCount / perFace;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                if (triangles.Length / 3 != faceCount) return null;

                // same as SplatMathUtils.GetMeshFaceVerticesNative, without needing a MeshFilter
                var faceVertexArray = new float3[triangles.Length];
                for (int i = 0; i < triangles.Length; i++)
                    faceVertexArray[i] = SplatMathUtils.TransformVertex(vertices[triangles[i]]);

                using var faceVertices = new NativeArray<float3>(faceVertexArray, Allocator.TempJob);
                using var alphas = GaMeSUtils.DecodeAlphasToNativeFloat3(asset.alphaData.bytes, faceCount, perFace, Allocator.TempJob);
                using var xyz = GaMeSUtils.CreateXYZData(alphas, faceVertices, faceCount, perFace);
                return xyz.ToArray();
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        static float3[] DecodeAssetPositions(GaussianGaMeSSplatAsset asset)
        {
            int count = asset.splatCount;
            var format = asset.posFormat;
            int stride = GaussianSplatAsset.GetVectorSize(format);
            var data = asset.posData.GetData<byte>();
            var chunks = asset.chunkData != null && asset.chunkData.dataSize != 0
                ? asset.chunkData.GetData<GaussianSplatAsset.ChunkInfo>()
                : default;

            var result = new float3[count];
            for (int i = 0; i < count; i++)
            {
                float3 p = DecodeVector(data, i * stride, format);
                if (chunks.IsCreated)
                {
                    var c = chunks[i / GaussianSplatAsset.kChunkSize];
                    p = new float3(math.lerp(c.posX.x, c.posX.y, p.x), math.lerp(c.posY.x, c.posY.y, p.y), math.lerp(c.posZ.x, c.posZ.y, p.z));
                }
                result[i] = p;
            }
            return result;
        }

        static float3 DecodeVector(NativeArray<byte> data, int offset, GaussianSplatAsset.VectorFormat format)
        {
            uint U32(int o) => (uint)(data[o] | (data[o + 1] << 8) | (data[o + 2] << 16) | (data[o + 3] << 24));
            uint U16(int o) => (uint)(data[o] | (data[o + 1] << 8));

            switch (format)
            {
                case GaussianSplatAsset.VectorFormat.Float32:
                    return new float3(math.asfloat(U32(offset)), math.asfloat(U32(offset + 4)), math.asfloat(U32(offset + 8)));
                case GaussianSplatAsset.VectorFormat.Norm16:
                    return new float3(U16(offset) / 65535f, U16(offset + 2) / 65535f, U16(offset + 4) / 65535f);
                case GaussianSplatAsset.VectorFormat.Norm11:
                {
                    uint v = U32(offset);
                    return new float3((v & 2047) / 2047f, ((v >> 11) & 1023) / 1023f, ((v >> 21) & 2047) / 2047f);
                }
                default:
                {
                    uint v = U16(offset);
                    return new float3((v & 63) / 63f, ((v >> 6) & 31) / 31f, ((v >> 11) & 31) / 31f);
                }
            }
        }

        // Greedy nearest unused neighbour on a uniform grid. Returns order[assetIndex] = meshIndex.
        static int[] Match(float3[] assetPositions, float3[] meshPositions, out float[] errors)
        {
            errors = null;
            int count = assetPositions.Length;
            if (meshPositions.Length != count) return null;

            float3 min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var p in meshPositions) { min = math.min(min, p); max = math.max(max, p); }
            float cell = math.max(math.cmax(max - min) / math.max(1f, math.pow(count, 1f / 3f)), 1e-6f);

            var grid = new Dictionary<int3, List<int>>();
            for (int i = 0; i < count; i++)
            {
                var key = (int3)math.floor((meshPositions[i] - min) / cell);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            var used = new bool[count];
            var order = new int[count];
            var matchErrors = new float[count];
            for (int a = 0; a < count; a++)
            {
                var p = assetPositions[a];
                var key = (int3)math.floor((p - min) / cell);
                int best = -1;
                float bestDist = float.MaxValue;
                // widen the search until an unused candidate turns up
                for (int radius = 1; best < 0 && radius <= 8; radius++)
                {
                    for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    for (int z = -radius; z <= radius; z++)
                    {
                        if (!grid.TryGetValue(key + new int3(x, y, z), out var list)) continue;
                        foreach (int m in list)
                        {
                            if (used[m]) continue;
                            float d = math.distancesq(p, meshPositions[m]);
                            if (d < bestDist) { bestDist = d; best = m; }
                        }
                    }
                }
                if (best < 0) return null;

                used[best] = true;
                order[a] = best;
                matchErrors[a] = math.sqrt(bestDist);
            }
            errors = matchErrors;
            return order;
        }
    }
}
