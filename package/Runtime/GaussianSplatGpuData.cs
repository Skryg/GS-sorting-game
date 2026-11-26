// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GaussianSplatting.Runtime
{
    // Ties one splat (in asset order) to the mesh face it deforms with. Matches CSDeformSplats.
    [StructLayout(LayoutKind.Sequential)]
    public struct DeformSplat
    {
        public float3 alpha;
        public float scale;
        public uint3 vertices;
    }

    // Splat data of one asset on the GPU. All renderers of an asset share one copy, uploaded once;
    // in play mode it also outlives the last of them, so that respawning objects does not upload it again.
    class GaussianSplatGpuData
    {
        static readonly Dictionary<GaussianSplatAsset, GaussianSplatGpuData> s_Cache = new();
        static readonly List<GaussianSplatAsset> s_Unused = new();

        public GraphicsBuffer pos, other, sh, chunks;
        public Texture color;
        public bool chunksValid;

        // only exist once some renderer of the asset was deformed
        public GraphicsBuffer deformSplats { get; private set; }
        public GraphicsBuffer deformChunks { get; private set; }

        public bool cached { get; private set; }
        Hash128 m_Hash;
        int m_SplatCount;
        int m_Refs;

        public static GaussianSplatGpuData Acquire(IGaussianSplatAsset asset)
        {
            // assets built at runtime belong to a single renderer
            if (asset is not GaussianSplatAsset key)
                return CreatePrivate(asset);

            if (s_Cache.TryGetValue(key, out var data))
            {
                if (data.m_Hash == key.dataHash && data.m_SplatCount == key.splatCount)
                {
                    ++data.m_Refs;
                    return data;
                }
                // reimported, current users still hold the old data
                s_Cache.Remove(key);
                data.cached = false;
                if (data.m_Refs <= 0)
                    data.Dispose();
            }

            data = CreatePrivate(asset);
            data.cached = true;
            s_Cache.Add(key, data);
            return data;
        }

        public static GaussianSplatGpuData CreatePrivate(IGaussianSplatAsset asset)
        {
            return new GaussianSplatGpuData(asset) { m_Refs = 1 };
        }

        public static void Release(ref GaussianSplatGpuData data)
        {
            if (data == null)
                return;
            --data.m_Refs;
            if (data.m_Refs <= 0)
            {
                if (!data.cached)
                    data.Dispose();
                else if (!Application.isPlaying)
                    FreeUnused();
            }
            data = null;
        }

        public static void FreeUnused()
        {
            s_Unused.Clear();
            foreach (var entry in s_Cache)
            {
                if (entry.Value.m_Refs <= 0)
                    s_Unused.Add(entry.Key);
            }
            foreach (var key in s_Unused)
            {
                s_Cache[key].Dispose();
                s_Cache.Remove(key);
            }
            s_Unused.Clear();
        }

        GaussianSplatGpuData(IGaussianSplatAsset asset)
        {
            m_Hash = asset.dataHash;
            m_SplatCount = asset.splatCount;
            pos = Upload(asset.posData, GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, "GaussianPosData");
            other = Upload(asset.otherData, GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, "GaussianOtherData");
            sh = Upload(asset.shData, GraphicsBuffer.Target.Raw, "GaussianSHData");

            var (texWidth, texHeight) = GaussianSplatAsset.CalcTextureSize(asset.splatCount);
            var texFormat = GaussianSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var tex = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate) { name = "GaussianColorData" };
            tex.SetPixelData(asset.colorData.GetData<byte>(), 0);
            tex.Apply(false, true);
            color = tex;

            int chunkSize = UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>();
            chunksValid = asset.chunkData != null && asset.chunkData.dataSize != 0;
            // without chunks a dummy buffer is still needed for binding
            int chunkCount = chunksValid ? (int)(asset.chunkData.dataSize / chunkSize) : 1;
            chunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, chunkCount, chunkSize) { name = "GaussianChunkData" };
            if (chunksValid)
                chunks.SetData(asset.chunkData.GetData<GaussianSplatAsset.ChunkInfo>());
        }

        static GraphicsBuffer Upload(IGaussianSplatData data, GraphicsBuffer.Target target, string name)
        {
            var buffer = new GraphicsBuffer(target, (int)(data.dataSize / 4), 4) { name = name };
            buffer.SetData(data.GetData<uint>());
            return buffer;
        }

        // Editing can change the splat count, which only happens on private data.
        public void Replace(GraphicsBuffer newPos, GraphicsBuffer newOther, GraphicsBuffer newSH, Texture newColor)
        {
            pos?.Dispose();
            other?.Dispose();
            sh?.Dispose();
            UnityEngine.Object.DestroyImmediate(color);
            pos = newPos;
            other = newOther;
            sh = newSH;
            color = newColor;
        }

        public void EnsureDeformData(IGaussianSplatAsset asset, Func<NativeArray<DeformSplat>> createSplats)
        {
            if (deformSplats != null)
                return;

            using var splats = createSplats();
            deformSplats = new GraphicsBuffer(GraphicsBuffer.Target.Raw, splats.Length * UnsafeUtility.SizeOf<DeformSplat>() / 4, 4) { name = "GaussianDeformSplats" };
            deformSplats.SetData(splats.Reinterpret<uint>(UnsafeUtility.SizeOf<DeformSplat>()));

            if (!chunksValid)
            {
                deformChunks = chunks;
                return;
            }
            // deformed positions and scales are not chunk relative, colors and SH still are
            using var info = new NativeArray<GaussianSplatAsset.ChunkInfo>(asset.chunkData.GetData<GaussianSplatAsset.ChunkInfo>(), Allocator.Temp);
            var identity = info;
            uint unitRange = math.f32tof16(1f) << 16;
            for (int i = 0; i < identity.Length; i++)
            {
                var c = identity[i];
                c.posX = c.posY = c.posZ = new float2(0f, 1f);
                c.sclX = c.sclY = c.sclZ = unitRange;
                identity[i] = c;
            }
            deformChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, identity.Length, UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()) { name = "GaussianDeformChunks" };
            deformChunks.SetData(identity);
        }

        void Dispose()
        {
            if (deformChunks != chunks)
                deformChunks?.Dispose();
            deformSplats?.Dispose();
            pos?.Dispose();
            other?.Dispose();
            sh?.Dispose();
            chunks?.Dispose();
            UnityEngine.Object.DestroyImmediate(color);
            pos = other = sh = chunks = deformSplats = deformChunks = null;
            color = null;
        }
    }
}
