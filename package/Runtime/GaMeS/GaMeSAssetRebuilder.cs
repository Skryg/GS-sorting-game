// SPDX-License-Identifier: MIT
using System;
using GaussianSplatting.Runtime.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GaussianSplatting.Runtime.GaMeS
{
    // Rebuilds a deformed GaMeS splat asset from the imported asset alone (no source .ply).
    // Only positions, rotations and scales follow the mesh; colors and SH are reused as stored,
    // so the new asset keeps the imported splat order and the original color/SH chunk ranges.
    public class GaMeSAssetRebuilder : IDisposable
    {
        readonly GaussianGaMeSSplatAsset m_Source;
        NativeArray<int> m_Order;
        NativeArray<GaussianSplatAsset.ChunkInfo> m_SourceChunks;
        NativeArray<int> m_SHIndices;
        uint m_Version;

        public int splatCount => m_Source.splatCount;
        public NativeArray<int> order => m_Order;

        public static bool CanRebuild(GaussianGaMeSSplatAsset asset, out string reason)
        {
            reason = null;
            if (asset == null) reason = "no GaMeS asset";
            else if (asset.splatOrderData == null) reason = "splat order is not baked (Tools > Gaussian Splats > Bake GaMeS Splat Order)";
            else if (asset.splatOrderData.GetData<byte>().Length != asset.splatCount * 4) reason = "baked splat order does not match the splat count, bake it again";
            else if (asset.alphaData == null || asset.scaleData == null) reason = "alpha/scale data missing";
            return reason == null;
        }

        public GaMeSAssetRebuilder(GaussianGaMeSSplatAsset source)
        {
            if (!CanRebuild(source, out var reason))
                throw new InvalidOperationException($"{(source != null ? source.name : "asset")}: {reason}");

            m_Source = source;
            m_Order = new NativeArray<int>(source.splatOrderData.GetData<int>(), Allocator.Persistent);

            var chunkData = source.chunkData;
            if (chunkData != null && chunkData.dataSize != 0)
                m_SourceChunks = new NativeArray<GaussianSplatAsset.ChunkInfo>(chunkData.GetData<GaussianSplatAsset.ChunkInfo>(), Allocator.Persistent);

            if (source.shFormat >= GaussianSplatAsset.SHFormat.Cluster64k)
                m_SHIndices = ReadSHIndices(source);
        }

        public void Dispose()
        {
            if (m_Order.IsCreated) m_Order.Dispose();
            if (m_SourceChunks.IsCreated) m_SourceChunks.Dispose();
            if (m_SHIndices.IsCreated) m_SHIndices.Dispose();
        }

        // Fills 'output' (asset order) with geometry computed per GaMeS face splat.
        public JobHandle ScheduleGather(NativeArray<float3> positions, NativeArray<quaternion> rotations, NativeArray<float3> scales, NativeArray<InputSplatData> output)
        {
            var job = new GatherJob
            {
                order = m_Order,
                positions = positions,
                rotations = rotations,
                scales = scales,
                output = output
            };
            return job.Schedule(output.Length, 8192);
        }

        // Consumes the gathered splats (they are modified in place).
        public unsafe GaussianSplatRuntimeAsset Build(NativeArray<InputSplatData> splats)
        {
            GaussianSplatAssetCreateTask.LinearizeData(splats);

            float3 boundsMin, boundsMax;
            new GaussianSplatAssetCreateTask.CalcBoundsJob
            {
                m_BoundsMin = &boundsMin,
                m_BoundsMax = &boundsMax,
                m_SplatData = splats
            }.Schedule().Complete();

            var hash = new Hash128((uint)splats.Length, (uint)GaussianSplatAsset.kCurrentVersion, ++m_Version, 0);

            NativeArray<byte> chunkData = default;
            if (m_SourceChunks.IsCreated)
            {
                // pos/scale ranges are recomputed; color/SH ranges must stay, the stored color/SH data is relative to them
                GaussianSplatAssetCreateTask.CreateChunkData(splats, out chunkData, ref hash);
                var chunks = chunkData.Reinterpret<GaussianSplatAsset.ChunkInfo>(1);
                for (int i = 0; i < chunks.Length; i++)
                {
                    var c = chunks[i];
                    var src = m_SourceChunks[i];
                    c.colR = src.colR; c.colG = src.colG; c.colB = src.colB; c.colA = src.colA;
                    c.shR = src.shR; c.shG = src.shG; c.shB = src.shB;
                    chunks[i] = c;
                }
            }

            GaussianSplatAssetCreateTask.CreatePositionsData(m_Source.posFormat, splats, out var posData, ref hash);
            GaussianSplatAssetCreateTask.CreateOtherData(m_Source.scaleFormat, splats, out var otherData, ref hash, m_SHIndices);

            // the runtime asset owns (and later disposes) its arrays, so the shared color/SH data is copied
            var colorData = new NativeArray<byte>(m_Source.colorData.GetData<byte>(), Allocator.Persistent);
            var shData = new NativeArray<byte>(m_Source.shData.GetData<byte>(), Allocator.Persistent);

            var asset = new GaussianSplatRuntimeAsset();
            asset.Initialize(m_Source.name, splats.Length, m_Source.posFormat, m_Source.scaleFormat, m_Source.colorFormat, m_Source.shFormat,
                boundsMin, boundsMax, m_Source.cameras, null);
            asset.SetObjPath(m_Source.objPath);
            asset.SetDataHash(hash);
            asset.setAssetData(chunkData, posData, otherData, colorData, shData, m_Source.alphaData, m_Source.scaleData);
            return asset;
        }

        // Everything CSDeformSplats needs per splat; constant for an asset, so it is uploaded once.
        public static NativeArray<DeformSplat> CreateDeformSplats(GaussianGaMeSSplatAsset asset, NativeArray<int> triangles, Allocator allocator)
        {
            int perFace = asset.numberOfSplatsPerFace;
            int faceCount = asset.splatCount / perFace;
            if (triangles.Length != faceCount * 3)
                throw new InvalidOperationException($"{asset.name}: mesh has {triangles.Length / 3} faces, the splats need {faceCount}");

            using var alphas = GaMeSUtils.DecodeAlphasToNativeFloat3(asset.alphaData.bytes, faceCount, perFace, Allocator.TempJob);
            using var scales = GaMeSUtils.DecodeScalesToNative(asset.scaleData.bytes, asset.splatCount, asset.scaleFormat, Allocator.TempJob);
            using var order = new NativeArray<int>(asset.splatOrderData.GetData<int>(), Allocator.TempJob);
            var output = new NativeArray<DeformSplat>(asset.splatCount, allocator, NativeArrayOptions.UninitializedMemory);
            new DeformSplatsJob
            {
                order = order,
                alphas = alphas,
                scales = scales,
                triangles = triangles,
                perFace = perFace,
                output = output
            }.Schedule(output.Length, 8192).Complete();
            return output;
        }

        static NativeArray<int> ReadSHIndices(GaussianGaMeSSplatAsset source)
        {
            int stride = GaussianSplatAsset.GetOtherSizeNoSHIndex(source.scaleFormat) + 2;
            var indices = new NativeArray<int>(source.splatCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            new ReadSHIndicesJob
            {
                other = source.otherData.GetData<byte>(),
                stride = stride,
                indices = indices
            }.Schedule(indices.Length, 8192).Complete();
            return indices;
        }

        [BurstCompile]
        struct ReadSHIndicesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> other;
            public int stride;
            public NativeArray<int> indices;

            public void Execute(int index)
            {
                int offset = index * stride + stride - 2;
                indices[index] = other[offset] | (other[offset + 1] << 8);
            }
        }

        [BurstCompile]
        struct DeformSplatsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> order;
            [ReadOnly] public NativeArray<float3> alphas;
            [ReadOnly] public NativeArray<float> scales;
            [ReadOnly] public NativeArray<int> triangles;
            public int perFace;
            public NativeArray<DeformSplat> output;

            public void Execute(int index)
            {
                int src = order[index];
                int face = src / perFace * 3;
                output[index] = new DeformSplat
                {
                    alpha = alphas[src],
                    scale = scales[src],
                    vertices = new uint3((uint)triangles[face], (uint)triangles[face + 1], (uint)triangles[face + 2])
                };
            }
        }

        [BurstCompile]
        struct GatherJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> order;
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<quaternion> rotations;
            [ReadOnly] public NativeArray<float3> scales;
            public NativeArray<InputSplatData> output;

            public void Execute(int index)
            {
                int src = order[index];
                output[index] = new InputSplatData
                {
                    pos = positions[src],
                    rot = rotations[src],
                    scale = scales[src]
                };
            }
        }
    }
}
