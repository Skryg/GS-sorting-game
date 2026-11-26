// SPDX-License-Identifier: MIT
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GaussianSplatting.Runtime;
using Unity.Mathematics;
using ChunkInfo = GaussianSplatting.Runtime.GaussianSplatAsset.ChunkInfo;
using ColorFormat = GaussianSplatting.Runtime.GaussianSplatAsset.ColorFormat;
using SHFormat = GaussianSplatting.Runtime.GaussianSplatAsset.SHFormat;
using VectorFormat = GaussianSplatting.Runtime.GaussianSplatAsset.VectorFormat;

namespace GaussianSplatting.Editor.Utils
{
    // Raw contents of the data files of a splat asset.
    public class GaussianSplatRawData
    {
        public int count;
        public VectorFormat posFormat;
        public VectorFormat scaleFormat;
        public ColorFormat colorFormat;
        public SHFormat shFormat;
        public byte[] pos, other, color, sh;
        public byte[] chunks; // null when the data is not chunk relative
    }

    // Symmetric 3x3 matrix.
    public struct SplatCovariance
    {
        public double xx, xy, xz, yy, yz, zz;

        // Product of the two largest standard deviations, i.e. how much area the splat covers.
        public double Area()
        {
            return math.sqrt(math.max(1e-30, xx * yy - xy * xy + xx * zz - xz * xz + yy * zz - yz * yz));
        }

        public double LogDet(double epsilon)
        {
            double a = xx + epsilon, d = yy + epsilon, f = zz + epsilon;
            double det = a * (d * f - yz * yz) - xy * (xy * f - xz * yz) + xz * (xy * yz - xz * d);
            return math.log(math.max(1e-60, det));
        }
    }

    // Splat asset decoded to plain numbers. Decoding matches LoadSplatData in GaussianSplatting.hlsl.
    public class GaussianSplatCloud
    {
        public const int kSHCount = 15;

        public int count;
        public double3[] pos;
        public SplatCovariance[] cov;
        public float[] opacity;
        public float3[] color;
        public float3[] sh; // kSHCount per splat

        public GaussianSplatCloud(int splatCount)
        {
            count = splatCount;
            pos = new double3[splatCount];
            cov = new SplatCovariance[splatCount];
            opacity = new float[splatCount];
            color = new float3[splatCount];
            sh = new float3[splatCount * kSHCount];
        }

        public static bool CanDecode(ColorFormat colorFormat, out string reason)
        {
            reason = colorFormat == ColorFormat.BC7 ? "BC7 colors cannot be read back, import the .ply with Low quality or better" : null;
            return reason == null;
        }

        public static GaussianSplatCloud Decode(GaussianSplatRawData data)
        {
            if (!CanDecode(data.colorFormat, out var reason))
                throw new InvalidOperationException(reason);

            var cloud = new GaussianSplatCloud(data.count);
            var chunks = data.chunks != null && data.chunks.Length > 0 ? MemoryMarshal.Cast<byte, ChunkInfo>(data.chunks).ToArray() : null;
            bool clusteredSH = data.shFormat > SHFormat.Norm6;
            int posStride = GaussianSplatAsset.GetVectorSize(data.posFormat);
            int otherStride = GaussianSplatAsset.GetOtherSizeNoSHIndex(data.scaleFormat) + (clusteredSH ? 2 : 0);

            Parallel.For(0, data.count, i =>
            {
                float3 pos = ReadVector(data.pos, i * posStride, data.posFormat);
                float4 rot = DecodeRotation(ReadU32(data.other, i * otherStride));
                float3 scale = ReadVector(data.other, i * otherStride + 4, data.scaleFormat);
                float4 col = ReadColor(data.color, SplatIndexToTextureIndex((uint)i), data.colorFormat);
                int shIndex = clusteredSH ? (int)ReadU16(data.other, (i + 1) * otherStride - 2) : i;

                float3 shMin = 0, shMax = 1;
                if (chunks != null)
                {
                    var c = chunks[i / GaussianSplatAsset.kChunkSize];
                    pos = math.lerp(new float3(c.posX.x, c.posY.x, c.posZ.x), new float3(c.posX.y, c.posY.y, c.posZ.y), pos);
                    scale = math.lerp(
                        math.f16tof32(new uint3(c.sclX, c.sclY, c.sclZ) & 0xFFFF),
                        math.f16tof32(new uint3(c.sclX, c.sclY, c.sclZ) >> 16), scale);
                    scale *= scale;
                    scale *= scale;
                    scale *= scale;
                    var packed = new uint4(c.colR, c.colG, c.colB, c.colA);
                    col = math.lerp(math.f16tof32(packed & 0xFFFF), math.f16tof32(packed >> 16), col);
                    col.w = GaussianUtils.InvSquareCentered01(col.w);
                    shMin = math.f16tof32(new uint3(c.shR, c.shG, c.shB) & 0xFFFF);
                    shMax = math.f16tof32(new uint3(c.shR, c.shG, c.shB) >> 16);
                }

                cloud.pos[i] = pos;
                cloud.cov[i] = CreateCovariance(rot, scale);
                cloud.color[i] = col.xyz;
                cloud.opacity[i] = math.saturate(col.w);
                bool relativeSH = chunks != null && IsChunkRelative(data.shFormat);
                for (int k = 0; k < kSHCount; k++)
                {
                    float3 v = ReadSH(data.sh, shIndex, k, data.shFormat);
                    cloud.sh[i * kSHCount + k] = relativeSH ? math.lerp(shMin, shMax, v) : v;
                }
            });
            return cloud;
        }

        // Clustered SH is not supported as an output format. The order of splats is kept: it decides which one
        // is drawn on top at the same depth, and data from the importer already is in Morton order.
        public GaussianSplatRawData Encode(VectorFormat posFormat, VectorFormat scaleFormat, ColorFormat colorFormat, SHFormat shFormat, out float3 boundsMin, out float3 boundsMax)
        {
            if (shFormat > SHFormat.Norm6 || colorFormat == ColorFormat.BC7)
                throw new ArgumentException($"Cannot write {shFormat} / {colorFormat}");

            CalcBounds(out boundsMin, out boundsMax);

            int posStride = GaussianSplatAsset.GetVectorSize(posFormat);
            int otherStride = GaussianSplatAsset.GetOtherSizeNoSHIndex(scaleFormat);
            var (texWidth, texHeight) = GaussianSplatAsset.CalcTextureSize(count);
            bool useChunks = posFormat != VectorFormat.Float32 || scaleFormat != VectorFormat.Float32 ||
                             colorFormat != ColorFormat.Float32x4 || shFormat != SHFormat.Float32;
            int chunkCount = (count + GaussianSplatAsset.kChunkSize - 1) / GaussianSplatAsset.kChunkSize;
            var chunks = useChunks ? new ChunkInfo[chunkCount] : null;

            var data = new GaussianSplatRawData
            {
                count = count,
                posFormat = posFormat,
                scaleFormat = scaleFormat,
                colorFormat = colorFormat,
                shFormat = shFormat,
                pos = new byte[NextMultipleOf8(count * posStride)],
                other = new byte[NextMultipleOf8(count * otherStride)],
                color = new byte[texWidth * texHeight * GaussianSplatAsset.GetColorSize(colorFormat)],
                sh = new byte[count * GetSHStride(shFormat)]
            };

            Parallel.For(0, chunkCount, chunkIdx =>
            {
                int begin = chunkIdx * GaussianSplatAsset.kChunkSize;
                int end = math.min(begin + GaussianSplatAsset.kChunkSize, count);
                var scales = new float3[end - begin];
                var colors = new float4[end - begin];

                float3 minPos = float.PositiveInfinity, minScale = float.PositiveInfinity, minSH = float.PositiveInfinity;
                float3 maxPos = float.NegativeInfinity, maxScale = float.NegativeInfinity, maxSH = float.NegativeInfinity;
                float4 minCol = float.PositiveInfinity, maxCol = float.NegativeInfinity;

                for (int i = begin; i < end; i++)
                {
                    DecomposeCovariance(cov[i], out float4 rot, out float3 scale);
                    WriteU32(data.other, i * otherStride, EncodeRotation(rot));

                    float alpha = opacity[i];
                    if (useChunks)
                    {
                        scale = math.pow(scale, 1.0f / 8.0f);
                        alpha = GaussianUtils.SquareCentered01(alpha);
                    }
                    scales[i - begin] = scale;
                    colors[i - begin] = new float4(color[i], alpha);

                    minPos = math.min(minPos, (float3)pos[i]);
                    maxPos = math.max(maxPos, (float3)pos[i]);
                    minScale = math.min(minScale, scale);
                    maxScale = math.max(maxScale, scale);
                    minCol = math.min(minCol, colors[i - begin]);
                    maxCol = math.max(maxCol, colors[i - begin]);
                    for (int k = 0; k < kSHCount; k++)
                    {
                        minSH = math.min(minSH, sh[i * kSHCount + k]);
                        maxSH = math.max(maxSH, sh[i * kSHCount + k]);
                    }
                }

                // make sure bounds are not zero
                maxPos = math.max(maxPos, minPos + 1.0e-5f);
                maxScale = math.max(maxScale, minScale + 1.0e-5f);
                maxCol = math.max(maxCol, minCol + 1.0e-5f);
                maxSH = math.max(maxSH, minSH + 1.0e-5f);

                if (useChunks)
                {
                    uint3 scl = math.f32tof16(minScale) | (math.f32tof16(maxScale) << 16);
                    uint4 col = math.f32tof16(minCol) | (math.f32tof16(maxCol) << 16);
                    uint3 shs = math.f32tof16(minSH) | (math.f32tof16(maxSH) << 16);
                    chunks[chunkIdx] = new ChunkInfo
                    {
                        posX = new float2(minPos.x, maxPos.x),
                        posY = new float2(minPos.y, maxPos.y),
                        posZ = new float2(minPos.z, maxPos.z),
                        sclX = scl.x, sclY = scl.y, sclZ = scl.z,
                        colR = col.x, colG = col.y, colB = col.z, colA = col.w,
                        shR = shs.x, shG = shs.y, shB = shs.z
                    };
                }
                bool relativeSH = useChunks && IsChunkRelative(shFormat);

                for (int i = begin; i < end; i++)
                {
                    float3 p = (float3)pos[i];
                    float3 scale = scales[i - begin];
                    float4 col = colors[i - begin];
                    if (useChunks)
                    {
                        p = (p - minPos) / (maxPos - minPos);
                        scale = (scale - minScale) / (maxScale - minScale);
                        col = (col - minCol) / (maxCol - minCol);
                    }
                    WriteVector(data.pos, i * posStride, p, posFormat);
                    WriteVector(data.other, i * otherStride + 4, scale, scaleFormat);
                    WriteColor(data.color, SplatIndexToTextureIndex((uint)i), col, colorFormat);
                    for (int k = 0; k < kSHCount; k++)
                    {
                        float3 v = sh[i * kSHCount + k];
                        WriteSH(data.sh, i, k, relativeSH ? (v - minSH) / (maxSH - minSH) : v, shFormat);
                    }
                }
            });

            if (useChunks)
                data.chunks = MemoryMarshal.AsBytes(chunks.AsSpan()).ToArray();
            return data;
        }

        void CalcBounds(out float3 min, out float3 max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                min = math.min(min, (float3)pos[i]);
                max = math.max(max, (float3)pos[i]);
            }
        }

        static SplatCovariance CreateCovariance(float4 q, float3 scale)
        {
            double3x3 m = new double3x3(new float3x3(new quaternion(q)));
            double3 s = (double3)scale * scale;
            return new SplatCovariance
            {
                xx = m.c0.x * m.c0.x * s.x + m.c1.x * m.c1.x * s.y + m.c2.x * m.c2.x * s.z,
                xy = m.c0.x * m.c0.y * s.x + m.c1.x * m.c1.y * s.y + m.c2.x * m.c2.y * s.z,
                xz = m.c0.x * m.c0.z * s.x + m.c1.x * m.c1.z * s.y + m.c2.x * m.c2.z * s.z,
                yy = m.c0.y * m.c0.y * s.x + m.c1.y * m.c1.y * s.y + m.c2.y * m.c2.y * s.z,
                yz = m.c0.y * m.c0.z * s.x + m.c1.y * m.c1.z * s.y + m.c2.y * m.c2.z * s.z,
                zz = m.c0.z * m.c0.z * s.x + m.c1.z * m.c1.z * s.y + m.c2.z * m.c2.z * s.z
            };
        }

        // Jacobi eigenvalue decomposition.
        static void DecomposeCovariance(in SplatCovariance c, out float4 rot, out float3 scale)
        {
            double[,] a = { { c.xx, c.xy, c.xz }, { c.xy, c.yy, c.yz }, { c.xz, c.yz, c.zz } };
            double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            double trace = math.abs(c.xx) + math.abs(c.yy) + math.abs(c.zz);

            for (int sweep = 0; sweep < 32; sweep++)
            {
                if (math.abs(a[0, 1]) + math.abs(a[0, 2]) + math.abs(a[1, 2]) <= trace * 1e-16)
                    break;
                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (a[p, q] == 0)
                        continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = (theta >= 0 ? 1 : -1) / (math.abs(theta) + math.sqrt(theta * theta + 1));
                    double cs = 1 / math.sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double kp = a[k, p], kq = a[k, q];
                        a[k, p] = cs * kp - sn * kq;
                        a[k, q] = sn * kp + cs * kq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double pk = a[p, k], qk = a[q, k];
                        a[p, k] = cs * pk - sn * qk;
                        a[q, k] = sn * pk + cs * qk;
                        double vp = v[k, p], vq = v[k, q];
                        v[k, p] = cs * vp - sn * vq;
                        v[k, q] = sn * vp + cs * vq;
                    }
                }
            }

            var m = new float3x3(
                (float)v[0, 0], (float)v[0, 1], (float)v[0, 2],
                (float)v[1, 0], (float)v[1, 1], (float)v[1, 2],
                (float)v[2, 0], (float)v[2, 1], (float)v[2, 2]);
            if (math.determinant(m) < 0)
                m.c2 = -m.c2;
            rot = math.normalize(new quaternion(m)).value;
            scale = new float3(
                (float)math.sqrt(math.max(a[0, 0], 1e-16)),
                (float)math.sqrt(math.max(a[1, 1], 1e-16)),
                (float)math.sqrt(math.max(a[2, 2], 1e-16)));
        }

        static bool IsChunkRelative(SHFormat format) => format > SHFormat.Float32 && format <= SHFormat.Norm6;

        static int NextMultipleOf8(int size) => (size + 7) / 8 * 8;

        static int GetSHStride(SHFormat format) => format switch
        {
            SHFormat.Float32 => 192,
            SHFormat.Norm11 => 60,
            SHFormat.Norm6 => 32,
            _ => 96
        };

        static int SplatIndexToTextureIndex(uint idx)
        {
            uint2 xy = GaussianUtils.DecodeMorton2D_16x16(idx);
            uint width = GaussianSplatAsset.kTextureWidth / 16;
            idx >>= 8;
            return (int)(((idx / width) * 16 + xy.y) * GaussianSplatAsset.kTextureWidth + (idx % width) * 16 + xy.x);
        }

        static uint ReadU16(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8));
        static uint ReadU32(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        static float ReadF32(byte[] d, int o) => math.asfloat(ReadU32(d, o));
        static float ReadF16(byte[] d, int o) => math.f16tof32(ReadU16(d, o));

        static void WriteU16(byte[] d, int o, uint v)
        {
            d[o] = (byte)v;
            d[o + 1] = (byte)(v >> 8);
        }

        static void WriteU32(byte[] d, int o, uint v)
        {
            WriteU16(d, o, v & 0xFFFF);
            WriteU16(d, o + 2, v >> 16);
        }

        static void WriteF32(byte[] d, int o, float v) => WriteU32(d, o, math.asuint(v));
        static void WriteF16(byte[] d, int o, float v) => WriteU16(d, o, math.f32tof16(v));

        static float3 DecodeNorm11(uint v) => new float3((v & 2047) / 2047f, ((v >> 11) & 1023) / 1023f, ((v >> 21) & 2047) / 2047f);

        static uint EncodeNorm11(float3 v)
        {
            v = math.saturate(v);
            return (uint)(v.x * 2047.5f) | ((uint)(v.y * 1023.5f) << 11) | ((uint)(v.z * 2047.5f) << 21);
        }

        static float3 ReadVector(byte[] d, int o, VectorFormat format)
        {
            switch (format)
            {
                case VectorFormat.Float32:
                    return new float3(ReadF32(d, o), ReadF32(d, o + 4), ReadF32(d, o + 8));
                case VectorFormat.Norm16:
                    return new float3(ReadU16(d, o), ReadU16(d, o + 2), ReadU16(d, o + 4)) / 65535f;
                case VectorFormat.Norm11:
                    return DecodeNorm11(ReadU32(d, o));
                default:
                    uint v = ReadU16(d, o);
                    return new float3((v & 63) / 63f, ((v >> 6) & 31) / 31f, ((v >> 11) & 31) / 31f);
            }
        }

        static void WriteVector(byte[] d, int o, float3 v, VectorFormat format)
        {
            if (format == VectorFormat.Float32)
            {
                WriteF32(d, o, v.x);
                WriteF32(d, o + 4, v.y);
                WriteF32(d, o + 8, v.z);
                return;
            }
            v = math.saturate(v);
            switch (format)
            {
                case VectorFormat.Norm16:
                    WriteU16(d, o, (uint)(v.x * 65535f + 0.5f));
                    WriteU16(d, o + 2, (uint)(v.y * 65535f + 0.5f));
                    WriteU16(d, o + 4, (uint)(v.z * 65535f + 0.5f));
                    break;
                case VectorFormat.Norm11:
                    WriteU32(d, o, EncodeNorm11(v));
                    break;
                default:
                    WriteU16(d, o, (uint)(v.x * 63.5f) | ((uint)(v.y * 31.5f) << 6) | ((uint)(v.z * 31.5f) << 11));
                    break;
            }
        }

        static float4 ReadColor(byte[] d, int pixel, ColorFormat format)
        {
            int o = pixel * GaussianSplatAsset.GetColorSize(format);
            return format switch
            {
                ColorFormat.Float32x4 => new float4(ReadF32(d, o), ReadF32(d, o + 4), ReadF32(d, o + 8), ReadF32(d, o + 12)),
                ColorFormat.Float16x4 => new float4(ReadF16(d, o), ReadF16(d, o + 2), ReadF16(d, o + 4), ReadF16(d, o + 6)),
                _ => new float4(d[o], d[o + 1], d[o + 2], d[o + 3]) / 255f
            };
        }

        static void WriteColor(byte[] d, int pixel, float4 v, ColorFormat format)
        {
            int o = pixel * GaussianSplatAsset.GetColorSize(format);
            for (int i = 0; i < 4; i++)
            {
                if (format == ColorFormat.Float32x4) WriteF32(d, o + i * 4, v[i]);
                else if (format == ColorFormat.Float16x4) WriteF16(d, o + i * 2, v[i]);
                else d[o + i] = (byte)(math.saturate(v[i]) * 255.5f);
            }
        }

        static float3 ReadSH(byte[] d, int index, int k, SHFormat format)
        {
            int o = index * GetSHStride(format);
            switch (format)
            {
                case SHFormat.Float32:
                    o += k * 12;
                    return new float3(ReadF32(d, o), ReadF32(d, o + 4), ReadF32(d, o + 8));
                case SHFormat.Norm11:
                    return DecodeNorm11(ReadU32(d, o + k * 4));
                case SHFormat.Norm6:
                    uint v = ReadU16(d, o + k * 2);
                    return new float3((v & 31) / 31f, ((v >> 5) & 63) / 63f, ((v >> 11) & 31) / 31f);
                default:
                    o += k * 6;
                    return new float3(ReadF16(d, o), ReadF16(d, o + 2), ReadF16(d, o + 4));
            }
        }

        static void WriteSH(byte[] d, int index, int k, float3 v, SHFormat format)
        {
            int o = index * GetSHStride(format);
            switch (format)
            {
                case SHFormat.Float32:
                    WriteVector(d, o + k * 12, v, VectorFormat.Float32);
                    break;
                case SHFormat.Norm11:
                    WriteU32(d, o + k * 4, EncodeNorm11(v));
                    break;
                case SHFormat.Norm6:
                    v = math.saturate(v);
                    WriteU16(d, o + k * 2, (uint)(v.x * 31.5f) | ((uint)(v.y * 63.5f) << 5) | ((uint)(v.z * 31.5f) << 11));
                    break;
                default:
                    o += k * 6;
                    WriteF16(d, o, v.x);
                    WriteF16(d, o + 2, v.y);
                    WriteF16(d, o + 4, v.z);
                    break;
            }
        }

        static float4 DecodeRotation(uint v)
        {
            float3 q = new float3(v & 1023, (v >> 10) & 1023, (v >> 20) & 1023) / 1023f;
            q = q * math.SQRT2 - 1.0f / math.SQRT2;
            float w = math.sqrt(1.0f - math.saturate(math.dot(q, q)));
            return (v >> 30) switch
            {
                0 => new float4(w, q.x, q.y, q.z),
                1 => new float4(q.x, w, q.y, q.z),
                2 => new float4(q.x, q.y, w, q.z),
                _ => new float4(q, w)
            };
        }

        static uint EncodeRotation(float4 rot)
        {
            float4 v = math.saturate(GaussianUtils.PackSmallest3Rotation(rot));
            return (uint)(v.x * 1023.5f) | ((uint)(v.y * 1023.5f) << 10) | ((uint)(v.z * 1023.5f) << 20) | ((uint)(v.w * 3.5f) << 30);
        }
    }
}
