// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    // The eyes rendered by one camera pass: two in single-pass stereo, otherwise one.
    struct SplatCameraViews
    {
        public int count;
        public Matrix4x4 viewLeft, viewRight;
        public Matrix4x4 gpuProjectionLeft, gpuProjectionRight;
        public Vector4 screenParams;
        public Vector3 cameraPosition;

        public static SplatCameraViews FromCamera(Camera cam)
        {
            int eyeW = XRSettings.eyeTextureWidth, eyeH = XRSettings.eyeTextureHeight;
            var projection = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
            return new SplatCameraViews
            {
                count = 1,
                viewLeft = cam.worldToCameraMatrix,
                viewRight = cam.worldToCameraMatrix,
                gpuProjectionLeft = projection,
                gpuProjectionRight = projection,
                screenParams = new Vector4(eyeW != 0 ? eyeW : cam.pixelWidth, eyeH != 0 ? eyeH : cam.pixelHeight, 0, 0),
                cameraPosition = cam.transform.position,
            };
        }
    }

    // World to clip space matrices of the eyes; objects outside all of them are not drawn. The default draws everything.
    struct SplatCullingViews
    {
        public int count;
        public Matrix4x4 viewProjectionLeft, viewProjectionRight;
    }

    struct SplatDraw
    {
        public GaussianSplatRenderer splat;
        public Material material;
        public MaterialPropertyBlock properties;
        public Matrix4x4 matrix;
        public int indexCount;
        public int instanceCount;
    }

    class GaussianSplatRenderSystem
    {
        // ReSharper disable MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        internal static readonly ProfilerMarker s_ProfDraw = new(ProfilerCategory.Render, "GaussianSplat.Draw", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCompose = new(ProfilerCategory.Render, "GaussianSplat.Compose", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCalcView = new(ProfilerCategory.Render, "GaussianSplat.CalcView", MarkerFlags.SampleGPU);
        // ReSharper restore MemberCanBePrivate.Global

        public static GaussianSplatRenderSystem instance => ms_Instance ??= new GaussianSplatRenderSystem();
        static GaussianSplatRenderSystem ms_Instance;

        readonly Dictionary<GaussianSplatRenderer, MaterialPropertyBlock> m_Splats = new();
        readonly HashSet<Camera> m_CameraCommandBuffersDone = new();
        readonly List<GaussianSplatRenderer> m_ActiveSplats = new();
        readonly List<bool> m_ActiveVisible = new(); // for every active splat object
        readonly Plane[][] m_CullingPlanes = { new Plane[6], new Plane[6] };
        readonly List<GaussianSplatRenderer> m_DebugSplats = new();
        readonly List<SplatDraw> m_PreparedDraws = new();

        // Splats of all objects share these buffers, so that they can be sorted and blended as one set.
        const int kCapacityStep = 1 << 18; // objects spawn during play, grow in steps instead of on every change
        GraphicsBuffer m_GpuView;
        GraphicsBuffer m_GpuColors;
        GraphicsBuffer m_GpuSortDistances;
        GraphicsBuffer m_GpuSortKeys;
        GpuSorting m_Sorter;
        GpuSorting.Args m_SorterArgs;
        ComputeShader m_SorterShader;
        readonly MaterialPropertyBlock m_DrawProperties = new();
        int m_SplatCount;
        int m_VisibleSplatCount;
        int m_LayoutHash;
        int m_FrameCounter;
        bool m_SortDirty = true;
        bool m_LimitReported;

        long m_MaxSplatCount;
        long maxSplatCount => m_MaxSplatCount != 0 ? m_MaxSplatCount : m_MaxSplatCount =
            Math.Min(int.MaxValue, SystemInfo.maxGraphicsBufferSize / GaussianSplatRenderer.kGpuViewDataSize) / GaussianSplatRenderer.kMaxViews;

        CommandBuffer m_CommandBuffer;

        public List<SplatDraw> preparedDraws => m_PreparedDraws;

        public void RegisterSplat(GaussianSplatRenderer r)
        {
            if (m_Splats.Count == 0)
            {
                if (GraphicsSettings.currentRenderPipeline == null)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_Splats.Add(r, new MaterialPropertyBlock());
        }

        public void UnregisterSplat(GaussianSplatRenderer r)
        {
            if (!m_Splats.ContainsKey(r))
                return;
            m_Splats.Remove(r);
            if (m_Splats.Count == 0)
            {
                if (m_CameraCommandBuffersDone != null)
                {
                    if (m_CommandBuffer != null)
                    {
                        foreach (var cam in m_CameraCommandBuffersDone)
                        {
                            if (cam)
                                cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                        }
                    }
                    m_CameraCommandBuffersDone.Clear();
                }

                m_ActiveSplats.Clear();
                m_ActiveVisible.Clear();
                m_DebugSplats.Clear();
                m_PreparedDraws.Clear();
                DisposeSplatBuffers();
                m_Sorter = null;
                m_SorterShader = null;
                GaussianSplatGpuData.FreeUnused();
                m_CommandBuffer?.Dispose();
                m_CommandBuffer = null;
                Camera.onPreCull -= OnPreCullCamera;
            }
        }

        void DisposeSplatBuffers()
        {
            m_GpuView?.Dispose();
            m_GpuColors?.Dispose();
            m_GpuSortDistances?.Dispose();
            m_GpuSortKeys?.Dispose();
            m_GpuView = null;
            m_GpuColors = null;
            m_GpuSortDistances = null;
            m_GpuSortKeys = null;
            m_SorterArgs.resources.Dispose();
            m_SorterArgs = default;
        }

        void EnsureSplatBuffers(ComputeShader cs)
        {
            if (m_Sorter == null || m_SorterShader != cs)
            {
                DisposeSplatBuffers();
                m_Sorter = new GpuSorting(cs);
                m_SorterShader = cs;
            }
            if (m_GpuSortKeys != null && m_GpuSortKeys.count >= m_SplatCount)
                return;

            DisposeSplatBuffers();
            m_SortDirty = true;
            int capacity = (int)Math.Min(((long)m_SplatCount + kCapacityStep - 1) / kCapacityStep * kCapacityStep, maxSplatCount);
            m_GpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * GaussianSplatRenderer.kMaxViews, GaussianSplatRenderer.kGpuViewDataSize) { name = "GaussianSplatViewData" };
            m_GpuColors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 8) { name = "GaussianSplatColors" };
            m_GpuSortDistances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4) { name = "GaussianSplatSortDistances" };
            m_GpuSortKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 4) { name = "GaussianSplatSortIndices" };
            m_SorterArgs.inputKeys = m_GpuSortDistances;
            m_SorterArgs.inputValues = m_GpuSortKeys;
            if (m_Sorter.Valid)
                m_SorterArgs.resources = GpuSorting.SupportResources.Load((uint)capacity);
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        public bool GatherSplatsForCamera(Camera cam, in SplatCullingViews culling = default)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;
            if (culling.count > 0)
                GeometryUtility.CalculateFrustumPlanes(culling.viewProjectionLeft, m_CullingPlanes[0]);
            if (culling.count > 1)
                GeometryUtility.CalculateFrustumPlanes(culling.viewProjectionRight, m_CullingPlanes[1]);

            // gather all active & valid splat objects
            m_ActiveSplats.Clear();
            m_ActiveVisible.Clear();
            m_DebugSplats.Clear();
            m_SplatCount = 0;
            m_VisibleSplatCount = 0;
            long limit = maxSplatCount;
            bool overLimit = false;
            var layout = new HashCode();
            foreach (var gs in m_Splats.Keys)
            {
                if (gs == null || !gs.isActiveAndEnabled || !gs.HasValidAsset || !gs.HasValidRenderSetup)
                    continue;
                if (gs.m_RenderMode != GaussianSplatRenderer.RenderMode.Splats)
                {
                    m_DebugSplats.Add(gs);
                    continue;
                }
                if (m_SplatCount + (long)gs.splatCount > limit)
                {
                    overLimit = true;
                    continue;
                }
                // Culled objects keep their place in the view and color buffers, so that turning the head
                // does not move the others around; they are only left out of the sort and the draw.
                bool visible = IsVisible(gs, culling.count);
                if (visible)
                    m_VisibleSplatCount += gs.splatCount;
                else
                    gs.OnCulled(m_SplatCount);
                m_ActiveSplats.Add(gs);
                m_ActiveVisible.Add(visible);
                m_SplatCount += gs.splatCount;
                layout.Add(gs.GetHashCode());
                layout.Add(gs.splatCount);
                layout.Add(visible);
            }

            if (overLimit && !m_LimitReported)
                Debug.LogError($"Gaussian splats: more than {limit:N0} splats are active, which is the most this GPU can hold in one buffer. Some objects are not drawn.");
            m_LimitReported = overLimit;

            int layoutHash = layout.ToHashCode();
            if (layoutHash != m_LayoutHash)
                m_SortDirty = true;
            m_LayoutHash = layoutHash;

            return m_VisibleSplatCount != 0 || m_DebugSplats.Count != 0;
        }

        bool IsVisible(GaussianSplatRenderer gs, int viewCount)
        {
            if (viewCount == 0 || !gs.TryGetCullingBounds(out var bounds))
                return true;
            for (int i = 0; i < viewCount; i++)
            {
                if (GeometryUtility.TestPlanesAABB(m_CullingPlanes[i], bounds))
                    return true;
            }
            return false;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        public Material SortAndRenderSplats(Camera cam, CommandBuffer cmb)
        {
            Material matComposite = PrepareSplats(cam, SplatCameraViews.FromCamera(cam), cmb);
            foreach (var draw in m_PreparedDraws)
            {
                cmb.BeginSample(s_ProfDraw);
                cmb.DrawProcedural(draw.splat.m_GpuIndexBuffer, draw.matrix, draw.material, 0, MeshTopology.Triangles, draw.indexCount, draw.instanceCount, draw.properties);
                cmb.EndSample(s_ProfDraw);
            }
            return matComposite;
        }

        // Records view data calculation and sorting; the draws to issue afterwards end up in preparedDraws.
        public Material PrepareSplats(Camera cam, in SplatCameraViews views, CommandBuffer cmb)
        {
            Material matComposite = null;
            m_PreparedDraws.Clear();
            if (m_VisibleSplatCount != 0)
                matComposite = PrepareSharedDraw(views, cmb);
            if (m_DebugSplats.Count != 0)
                matComposite = PrepareDebugDraws(cam, cmb);
            return matComposite;
        }

        // Every object writes its range of the shared buffers, then all ranges are sorted together into one draw.
        Material PrepareSharedDraw(in SplatCameraViews views, CommandBuffer cmb)
        {
            var host = m_ActiveSplats[0]; // shaders and the quad index buffer are the same for every object
            host.EnsureMaterials();
            EnsureSplatBuffers(host.m_CSSplatUtilities);

            int sortNthFrame = int.MaxValue;
            foreach (var gs in m_ActiveSplats)
                sortNthFrame = Math.Min(sortNthFrame, Math.Max(gs.m_SortNthFrame, 1));
            bool sort = m_SortDirty;
            bool mayResort = m_FrameCounter % sortNthFrame == 0;
            for (int i = 0; i < m_ActiveSplats.Count; i++)
            {
                if (m_ActiveVisible[i])
                    sort |= m_ActiveSplats[i].DispatchDeform(cmb) || (mayResort && m_ActiveSplats[i].IsSortOutdated(views.cameraPosition));
            }
            ++m_FrameCounter;

            GaussianSplatRenderer.SetViewParams(cmb, host.m_CSSplatUtilities, views);

            cmb.BeginSample(s_ProfCalcView);
            int offset = 0, sortOffset = 0;
            for (int i = 0; i < m_ActiveSplats.Count; i++)
            {
                var gs = m_ActiveSplats[i];
                if (m_ActiveVisible[i])
                {
                    gs.CalcViewData(cmb, host.m_CSSplatUtilities, views, m_GpuView, m_GpuColors, m_GpuSortDistances, m_GpuSortKeys, offset, sortOffset, m_SplatCount, sort);
                    sortOffset += gs.splatCount;
                }
                offset += gs.splatCount;
            }
            cmb.EndSample(s_ProfCalcView);

            if (sort)
            {
                cmb.BeginSample(GaussianSplatRenderer.s_ProfSort);
                m_SorterArgs.count = (uint)m_VisibleSplatCount;
                if (m_Sorter.Valid)
                    m_Sorter.Dispatch(cmb, m_SorterArgs);
                cmb.EndSample(GaussianSplatRenderer.s_ProfSort);
                m_SortDirty = false;
            }

            m_DrawProperties.Clear();
            m_DrawProperties.SetBuffer(GaussianSplatRenderer.Props.SplatViewData, m_GpuView);
            m_DrawProperties.SetBuffer(GaussianSplatRenderer.Props.SplatColors, m_GpuColors);
            m_DrawProperties.SetBuffer(GaussianSplatRenderer.Props.OrderBuffer, m_GpuSortKeys);
            m_DrawProperties.SetInteger(GaussianSplatRenderer.Props.SplatCount, m_SplatCount);
            m_DrawProperties.SetInteger(GaussianSplatRenderer.Props.SplatDrawCount, m_VisibleSplatCount);
            m_DrawProperties.SetVector(GaussianSplatRenderer.Props.VecScreenParams, views.screenParams);
            if (host.m_MatSplats != null)
            {
                m_PreparedDraws.Add(new SplatDraw
                {
                    splat = host,
                    material = host.m_MatSplats,
                    properties = m_DrawProperties,
                    matrix = Matrix4x4.identity,
                    indexCount = 6,
                    instanceCount = m_VisibleSplatCount,
                });
            }
            return host.m_MatComposite;
        }

        // Debug modes read the source data of each object directly, so they stay one draw per object.
        Material PrepareDebugDraws(Camera cam, CommandBuffer cmb)
        {
            var camTr = cam.transform;
            m_DebugSplats.Sort((a, b) =>
                camTr.InverseTransformPoint(a.transform.position).z.CompareTo(camTr.InverseTransformPoint(b.transform.position).z));

            Material matComposite = null;
            foreach (var gs in m_DebugSplats)
            {
                gs.EnsureMaterials();
                gs.DispatchDeform(cmb);
                matComposite = gs.m_MatComposite;
                bool chunks = gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugChunkBounds;
                bool boxes = chunks || gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugBoxes;
                var displayMat = boxes ? gs.m_MatDebugBoxes : gs.m_MatDebugPoints;
                if (displayMat == null)
                    continue;

                var matrix = gs.transform.localToWorldMatrix;
                var mpb = m_Splats[gs];
                mpb.Clear();
                gs.SetAssetDataOnMaterial(mpb);
                if (boxes)
                {
                    gs.SortPoints(cmb, cam, matrix);
                    mpb.SetBuffer(GaussianSplatRenderer.Props.OrderBuffer, gs.m_GpuSortKeys);
                }
                mpb.SetBuffer(GaussianSplatRenderer.Props.SplatChunks, gs.m_GpuChunks);
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatScale, gs.m_SplatScale);
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatOpacityScale, gs.m_OpacityScale);
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatSize, gs.m_PointDisplaySize);
                mpb.SetInteger(GaussianSplatRenderer.Props.DisplayIndex, gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugPointIndices ? 1 : 0);
                mpb.SetInteger(GaussianSplatRenderer.Props.DisplayChunks, chunks ? 1 : 0);

                int instanceCount = gs.splatCount;
                if (chunks)
                    instanceCount = gs.m_GpuChunksValid ? gs.m_GpuChunks.count : 0;

                m_PreparedDraws.Add(new SplatDraw
                {
                    splat = gs,
                    material = displayMat,
                    properties = mpb,
                    matrix = matrix,
                    indexCount = boxes ? 36 : 6,
                    instanceCount = instanceCount,
                });
            }
            return matComposite;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        // ReSharper disable once UnusedMethodReturnValue.Global - used by HDRP/URP features that are not always compiled
        public CommandBuffer InitialClearCmdBuffer(Camera cam)
        {
            m_CommandBuffer ??= new CommandBuffer { name = "RenderGaussianSplats" };
            if (GraphicsSettings.currentRenderPipeline == null && cam != null && !m_CameraCommandBuffersDone.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                m_CameraCommandBuffersDone.Add(cam);
            }

            // get render target for all splats
            m_CommandBuffer.Clear();
            return m_CommandBuffer;
        }

        void OnPreCullCamera(Camera cam)
        {
            if (!GatherSplatsForCamera(cam))
                return;

            InitialClearCmdBuffer(cam);

            m_CommandBuffer.GetTemporaryRT(GaussianSplatRenderer.Props.GaussianSplatRT, -1, -1, 0, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat);
            m_CommandBuffer.SetRenderTarget(GaussianSplatRenderer.Props.GaussianSplatRT, BuiltinRenderTextureType.CurrentActive);
            m_CommandBuffer.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

            // We only need this to determine whether we're rendering into backbuffer or not. However, detection this
            // way only works in BiRP so only do it here.
            m_CommandBuffer.SetGlobalTexture(GaussianSplatRenderer.Props.CameraTargetTexture, BuiltinRenderTextureType.CameraTarget);

            // add sorting, view calc and drawing commands for each splat object
            Material matComposite = SortAndRenderSplats(cam, m_CommandBuffer);

            // compose
            m_CommandBuffer.BeginSample(s_ProfCompose);
            m_CommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_CommandBuffer.DrawProcedural(Matrix4x4.identity, matComposite, 0, MeshTopology.Triangles, 3, 1);
            m_CommandBuffer.EndSample(s_ProfCompose);
            m_CommandBuffer.ReleaseTemporaryRT(GaussianSplatRenderer.Props.GaussianSplatRT);
        }
    }

    [ExecuteInEditMode]
    public class GaussianSplatRenderer : MonoBehaviour
    {
        public enum RenderMode
        {
            Splats,
            DebugPoints,
            DebugPointIndices,
            DebugBoxes,
            DebugChunkBounds,
        }
        public GaussianSplatAsset m_Asset;

        [Range(0.1f, 2.0f)]
        [Tooltip("Additional scaling factor for the splats")]
        public float m_SplatScale = 1.0f;
        [Range(0.05f, 20.0f)]
        [Tooltip("Additional scaling factor for opacity")]
        public float m_OpacityScale = 1.0f;
        [Range(0, 3)]
        [Tooltip("Spherical Harmonics order to use")]
        public int m_SHOrder = 3;
        [Tooltip("Show only Spherical Harmonics contribution, using gray color")]
        public bool m_SHOnly;
        [Range(1, 30)]
        [Tooltip("Sort splats only every N frames")]
        public int m_SortNthFrame = 1;
        [Range(0f, 2f)]
        [Tooltip("Keep the sort order until the direction towards the camera changes by this many degrees. 0 sorts every time.")]
        public float m_SortAngle = 0.25f;
        [Range(0f, 5f)]
        [Tooltip("Keep view dependent colors (SH) until the direction towards the camera changes by this many degrees. 0 updates them every frame.")]
        public float m_SHRefreshAngle = 1f;

        // 0 = intact, towards 1 the splats drift apart, fall and shrink away
        [NonSerialized] public float disintegration;
        // how far the splats fly apart while disintegrating
        [NonSerialized] public float disintegrationSpread = 1f;
        // 0 = intact, towards 1 the splats run down and drip off, the lowest ones first
        [NonSerialized] public float melt;
        // blended over the colors, alpha is the amount
        [NonSerialized] public Color tint = Color.clear;

        public RenderMode m_RenderMode = RenderMode.Splats;
        [Range(1.0f, 15.0f)] public float m_PointDisplaySize = 3.0f;

        public GaussianCutout[] m_Cutouts;

        public Shader m_ShaderSplats;
        public Shader m_ShaderComposite;
        public Shader m_ShaderDebugPoints;
        public Shader m_ShaderDebugBoxes;
        [Tooltip("Gaussian splatting compute shader")]
        public ComputeShader m_CSSplatUtilities;

        int m_SplatCount; // initially same as asset splat count, but editing can change this
        // only created for debug boxes, normal rendering sorts all objects together in GaussianSplatRenderSystem
        GraphicsBuffer m_GpuSortDistances;
        internal GraphicsBuffer m_GpuSortKeys;
        GaussianSplatGpuData m_GpuData;
        // what is currently drawn: the asset data, with geometry replaced while deformed
        GraphicsBuffer m_GpuPosData;
        GraphicsBuffer m_GpuOtherData;
        GraphicsBuffer m_GpuSHData;
        Texture m_GpuColorData;
        internal GraphicsBuffer m_GpuChunks;
        internal bool m_GpuChunksValid;
        internal GraphicsBuffer m_GpuIndexBuffer;

        GraphicsBuffer m_GpuDeformedPos;
        GraphicsBuffer m_GpuDeformedOther;
        GraphicsBuffer m_GpuDeformVertices;
        bool m_DeformDirty;

        Vector3 m_SortedFrom;
        bool m_SortedFromValid;
        Vector3 m_ColorsFrom;
        GraphicsBuffer m_ColorsBuffer;
        int m_ColorsOffset;
        int m_ColorsHash;

        // these buffers are only for splat editing, and are lazily created
        GraphicsBuffer m_GpuEditCutouts;
        GraphicsBuffer m_GpuEditCountsBounds;
        GraphicsBuffer m_GpuEditSelected;
        GraphicsBuffer m_GpuEditDeleted;
        GraphicsBuffer m_GpuEditSelectedMouseDown; // selection state at start of operation
        GraphicsBuffer m_GpuEditPosMouseDown; // position state at start of operation
        GraphicsBuffer m_GpuEditOtherMouseDown; // rotation/scale state at start of operation

        GpuSorting m_Sorter;
        GpuSorting.Args m_SorterArgs;

        internal Material m_MatSplats;
        internal Material m_MatComposite;
        internal Material m_MatDebugPoints;
        internal Material m_MatDebugBoxes;

        IGaussianSplatAsset m_PrevAsset;
        Hash128 m_PrevHash;
        bool m_Registered;

        internal static readonly ProfilerMarker s_ProfSort = new(ProfilerCategory.Render, "GaussianSplat.Sort", MarkerFlags.SampleGPU);

        internal static class Props
        {
            public static readonly int SplatPos = Shader.PropertyToID("_SplatPos");
            public static readonly int SplatOther = Shader.PropertyToID("_SplatOther");
            public static readonly int SplatSH = Shader.PropertyToID("_SplatSH");
            public static readonly int SplatColor = Shader.PropertyToID("_SplatColor");
            public static readonly int SplatSelectedBits = Shader.PropertyToID("_SplatSelectedBits");
            public static readonly int SplatSelectedBitsRead = Shader.PropertyToID("_SplatSelectedBitsRead");
            public static readonly int SplatDeletedBits = Shader.PropertyToID("_SplatDeletedBits");
            public static readonly int SplatBitsValid = Shader.PropertyToID("_SplatBitsValid");
            public static readonly int SplatFormat = Shader.PropertyToID("_SplatFormat");
            public static readonly int SplatChunks = Shader.PropertyToID("_SplatChunks");
            public static readonly int SplatChunkCount = Shader.PropertyToID("_SplatChunkCount");
            public static readonly int SplatViewData = Shader.PropertyToID("_SplatViewData");
            public static readonly int SplatColors = Shader.PropertyToID("_SplatColors");
            public static readonly int WriteColors = Shader.PropertyToID("_WriteColors");
            public static readonly int DeformSplats = Shader.PropertyToID("_DeformSplats");
            public static readonly int DeformVertices = Shader.PropertyToID("_DeformVertices");
            public static readonly int SplatOffset = Shader.PropertyToID("_SplatOffset");
            public static readonly int SplatSortOffset = Shader.PropertyToID("_SplatSortOffset");
            public static readonly int SplatTotalCount = Shader.PropertyToID("_SplatTotalCount");
            public static readonly int SplatDrawCount = Shader.PropertyToID("_SplatDrawCount");
            public static readonly int WriteSortKeys = Shader.PropertyToID("_WriteSortKeys");
            public static readonly int OrderBuffer = Shader.PropertyToID("_OrderBuffer");
            public static readonly int Disintegration = Shader.PropertyToID("_Disintegration");
            public static readonly int Melt = Shader.PropertyToID("_Melt");
            public static readonly int MeltHeight = Shader.PropertyToID("_MeltHeight");
            public static readonly int SplatTint = Shader.PropertyToID("_SplatTint");
            public static readonly int DisintegrationCenter = Shader.PropertyToID("_DisintegrationCenter");
            public static readonly int SplatScale = Shader.PropertyToID("_SplatScale");
            public static readonly int SplatOpacityScale = Shader.PropertyToID("_SplatOpacityScale");
            public static readonly int SplatSize = Shader.PropertyToID("_SplatSize");
            public static readonly int SplatCount = Shader.PropertyToID("_SplatCount");
            public static readonly int DepthAlphaCutoff = Shader.PropertyToID("_DepthAlphaCutoff");
            public static readonly int SHOrder = Shader.PropertyToID("_SHOrder");
            public static readonly int SHOnly = Shader.PropertyToID("_SHOnly");
            public static readonly int DisplayIndex = Shader.PropertyToID("_DisplayIndex");
            public static readonly int DisplayChunks = Shader.PropertyToID("_DisplayChunks");
            public static readonly int GaussianSplatRT = Shader.PropertyToID("_GaussianSplatRT");
            public static readonly int SplatSortKeys = Shader.PropertyToID("_SplatSortKeys");
            public static readonly int SplatSortDistances = Shader.PropertyToID("_SplatSortDistances");
            public static readonly int SrcBuffer = Shader.PropertyToID("_SrcBuffer");
            public static readonly int DstBuffer = Shader.PropertyToID("_DstBuffer");
            public static readonly int BufferSize = Shader.PropertyToID("_BufferSize");
            public static readonly int MatrixMV = Shader.PropertyToID("_MatrixMV");
            public static readonly int MatrixObjectToWorld = Shader.PropertyToID("_MatrixObjectToWorld");
            public static readonly int MatrixWorldToObject = Shader.PropertyToID("_MatrixWorldToObject");
            public static readonly int VecScreenParams = Shader.PropertyToID("_VecScreenParams");
            public static readonly int VecWorldSpaceCameraPos = Shader.PropertyToID("_VecWorldSpaceCameraPos");
            public static readonly int SplatViewCount = Shader.PropertyToID("_SplatViewCount");
            public static readonly int EyeMatrixMV = Shader.PropertyToID("_EyeMatrixMV");
            public static readonly int EyeMatrixVP = Shader.PropertyToID("_EyeMatrixVP");
            public static readonly int EyeMatrixP = Shader.PropertyToID("_EyeMatrixP");
            public static readonly int CameraTargetTexture = Shader.PropertyToID("_CameraTargetTexture");
            public static readonly int SelectionCenter = Shader.PropertyToID("_SelectionCenter");
            public static readonly int SelectionDelta = Shader.PropertyToID("_SelectionDelta");
            public static readonly int SelectionDeltaRot = Shader.PropertyToID("_SelectionDeltaRot");
            public static readonly int SplatCutoutsCount = Shader.PropertyToID("_SplatCutoutsCount");
            public static readonly int SplatCutouts = Shader.PropertyToID("_SplatCutouts");
            public static readonly int SelectionMode = Shader.PropertyToID("_SelectionMode");
            public static readonly int SplatPosMouseDown = Shader.PropertyToID("_SplatPosMouseDown");
            public static readonly int SplatOtherMouseDown = Shader.PropertyToID("_SplatOtherMouseDown");
        }

        [field: NonSerialized] public bool editModified { get; private set; }
        [field: NonSerialized] public uint editSelectedSplats { get; private set; }
        [field: NonSerialized] public uint editDeletedSplats { get; private set; }
        [field: NonSerialized] public uint editCutSplats { get; private set; }
        [field: NonSerialized] public Bounds editSelectedBounds { get; private set; }

        private IGaussianSplatAsset m_injectedAsset;

        public IGaussianSplatAsset asset
        {
            get
            {
                if (m_injectedAsset != null)
                    return m_injectedAsset;
                return m_Asset;
            }
        }
        public int splatCount => m_SplatCount;

        enum KernelIndices
        {
            SetIndices,
            CalcDistances,
            CalcViewData,
            UpdateEditData,
            InitEditData,
            ClearBuffer,
            InvertSelection,
            SelectAll,
            OrBuffers,
            SelectionUpdate,
            TranslateSelection,
            RotateSelection,
            ScaleSelection,
            ExportData,
            CopySplats,
            DeformSplats,
        }

        public bool HasValidAsset =>
            asset != null &&
            asset.splatCount > 0 &&
            asset.formatVersion == GaussianSplatAsset.kCurrentVersion &&
            asset.posData != null &&
            asset.otherData != null &&
            asset.shData != null &&
            asset.colorData != null;
        public bool HasValidRenderSetup => m_GpuPosData != null && m_GpuOtherData != null && m_GpuChunks != null;

        internal const int kGpuViewDataSize = 32;
        internal const int kMaxViews = 2; // single-pass stereo keeps one view record per eye

        // asset bounds only contain splat centers
        const float kCullingMarginOfSize = 0.25f;
        const float kCullingMargin = 0.1f; // world units

        static readonly Matrix4x4[] s_EyeMatrixMV = new Matrix4x4[kMaxViews];
        static readonly Matrix4x4[] s_EyeMatrixVP = new Matrix4x4[kMaxViews];
        static readonly Matrix4x4[] s_EyeMatrixP = new Matrix4x4[kMaxViews];

        public void InjectAsset(IGaussianSplatAsset injectedAsset)
        {
            if (asset != null) asset.Dispose();
            m_injectedAsset = injectedAsset;
        }

        void CreateResourcesForAsset()
        {
            if (!HasValidAsset)
                return;

            m_SplatCount = asset.splatCount;
            m_GpuData = GaussianSplatGpuData.Acquire(asset);
            BindGpuData();

            m_GpuIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, 36, 2);
            // cube indices, most often we use only the first quad
            m_GpuIndexBuffer.SetData(new ushort[]
            {
                0, 1, 2, 1, 3, 2,
                4, 6, 5, 5, 6, 7,
                0, 2, 4, 4, 2, 6,
                1, 5, 3, 5, 7, 3,
                0, 4, 1, 4, 5, 1,
                2, 3, 6, 3, 7, 6
            });

            EnsureRegistered();
        }

        void BindGpuData()
        {
            bool deformed = m_GpuDeformedPos != null;
            m_GpuPosData = deformed ? m_GpuDeformedPos : m_GpuData.pos;
            m_GpuOtherData = deformed ? m_GpuDeformedOther : m_GpuData.other;
            m_GpuSHData = m_GpuData.sh;
            m_GpuColorData = m_GpuData.color;
            m_GpuChunks = deformed ? m_GpuData.deformChunks : m_GpuData.chunks;
            m_GpuChunksValid = m_GpuData.chunksValid;
        }

        int splatFormat
        {
            get
            {
                // deformed geometry is always Float32, which is 0
                int geometry = m_GpuDeformedPos != null ? 0 : (int)asset.posFormat | ((int)asset.scaleFormat << 8);
                return geometry | ((int)asset.shFormat << 16);
            }
        }

        // Clustered SH keeps 16 bit indices next to the scales, which CSDeformSplats does not write.
        public bool SupportsDeform => asset != null && asset.shFormat <= GaussianSplatAsset.SHFormat.Norm6;

        // Moves the splats along with mesh vertices. Only the vertices are uploaded, splat geometry is rebuilt on the GPU.
        // createSplats is called once per asset, the result stays on the GPU.
        public void Deform(NativeArray<float3> vertices, Func<NativeArray<DeformSplat>> createSplats)
        {
            if (m_GpuData == null || m_GpuEditSelected != null)
                return;

            m_GpuData.EnsureDeformData(asset, createSplats);
            if (m_GpuDeformedPos == null)
            {
                m_GpuDeformedPos = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, m_SplatCount * 3, 4) { name = "GaussianDeformedPos" };
                m_GpuDeformedOther = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, m_SplatCount * 4, 4) { name = "GaussianDeformedOther" };
                BindGpuData();
            }
            if (m_GpuDeformVertices == null || m_GpuDeformVertices.count != vertices.Length * 3)
            {
                m_GpuDeformVertices?.Dispose();
                m_GpuDeformVertices = new GraphicsBuffer(GraphicsBuffer.Target.Raw, vertices.Length * 3, 4) { name = "GaussianDeformVertices" };
            }
            m_GpuDeformVertices.SetData(vertices.Reinterpret<float>(12));
            m_DeformDirty = true;
        }

        internal bool DispatchDeform(CommandBuffer cmb)
        {
            if (!m_DeformDirty)
                return false;
            m_DeformDirty = false;

            const int kernel = (int)KernelIndices.DeformSplats;
            cmb.SetComputeBufferParam(m_CSSplatUtilities, kernel, Props.DeformSplats, m_GpuData.deformSplats);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, kernel, Props.DeformVertices, m_GpuDeformVertices);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, kernel, Props.SplatPos, m_GpuDeformedPos);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, kernel, Props.SplatOther, m_GpuDeformedOther);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SplatCount, m_SplatCount);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
            m_CSSplatUtilities.GetKernelThreadGroupSizes(kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, kernel, (m_SplatCount + (int)gsX - 1) / (int)gsX, 1, 1);
            return true;
        }

        void DisposeDeformBuffers()
        {
            DisposeBuffer(ref m_GpuDeformedPos);
            DisposeBuffer(ref m_GpuDeformedOther);
            DisposeBuffer(ref m_GpuDeformVertices);
            m_DeformDirty = false;
        }

        // In the object's own space the camera moves for head motion as well as for motion of the object.
        bool HasViewChanged(Vector3 from, Vector3 to, float degrees)
        {
            var closest = new Bounds((asset.boundsMin + asset.boundsMax) * 0.5f, asset.boundsMax - asset.boundsMin).ClosestPoint(to);
            float tolerance = (to - closest).magnitude * degrees * Mathf.Deg2Rad;
            return (to - from).sqrMagnitude > tolerance * tolerance;
        }

        // False when the bounds are not known, because the splats were moved since import.
        internal bool TryGetCullingBounds(out Bounds bounds)
        {
            bounds = default;
            if (m_injectedAsset != null || m_GpuDeformedPos != null || m_GpuEditSelected != null || disintegration > 0f || melt > 0f)
                return false;

            var matrix = transform.localToWorldMatrix;
            Vector3 extents = (asset.boundsMax - asset.boundsMin) * 0.5f;
            Vector3 x = matrix.GetColumn(0) * extents.x, y = matrix.GetColumn(1) * extents.y, z = matrix.GetColumn(2) * extents.z;
            var world = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            float margin = kCullingMargin + kCullingMarginOfSize * Mathf.Max(world.x, world.y, world.z);
            bounds.center = matrix.MultiplyPoint3x4((asset.boundsMin + asset.boundsMax) * 0.5f);
            bounds.extents = world + Vector3.one * margin;
            return true;
        }

        // A culled object does not rewrite its colors, so they are lost once its range moves.
        internal void OnCulled(int offset)
        {
            if (m_ColorsOffset != offset)
                m_ColorsBuffer = null;
        }

        internal bool IsSortOutdated(Vector3 cameraPosition)
        {
            return m_GpuEditSelected != null || m_SortAngle <= 0f || !m_SortedFromValid || disintegration > 0f || melt > 0f ||
                   HasViewChanged(m_SortedFrom, transform.InverseTransformPoint(cameraPosition), m_SortAngle);
        }

        bool AreColorsOutdated(Vector3 cameraLocal, GraphicsBuffer colors, int offset, int hash)
        {
            if (m_GpuEditSelected != null || m_ColorsBuffer != colors || m_ColorsOffset != offset || m_ColorsHash != hash)
                return true;
            if (m_SHOrder == 0)
                return false;
            return m_SHRefreshAngle <= 0f || HasViewChanged(m_ColorsFrom, cameraLocal, m_SHRefreshAngle);
        }

        void InitSortBuffers(int count)
        {
            m_GpuSortDistances?.Dispose();
            m_GpuSortKeys?.Dispose();
            m_SorterArgs.resources.Dispose();

            m_Sorter ??= new GpuSorting(m_CSSplatUtilities);

            m_GpuSortDistances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "GaussianSplatSortDistances" };
            m_GpuSortKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "GaussianSplatSortIndices" };

            // init keys buffer to splat indices
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.SetIndices, Props.SplatSortKeys, m_GpuSortKeys);
            m_CSSplatUtilities.SetInt(Props.SplatCount, m_GpuSortDistances.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.SetIndices, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.SetIndices, (m_GpuSortDistances.count + (int)gsX - 1) / (int)gsX, 1, 1);

            m_SorterArgs.inputKeys = m_GpuSortDistances;
            m_SorterArgs.inputValues = m_GpuSortKeys;
            m_SorterArgs.count = (uint)count;
            if (m_Sorter.Valid)
                m_SorterArgs.resources = GpuSorting.SupportResources.Load((uint)count);
        }

        bool resourcesAreSetUp => m_ShaderSplats != null && m_ShaderComposite != null && m_ShaderDebugPoints != null &&
                                  m_ShaderDebugBoxes != null && m_CSSplatUtilities != null && SystemInfo.supportsComputeShaders;

        public void EnsureMaterials()
        {
            if (m_MatSplats == null && resourcesAreSetUp)
            {
                m_MatSplats = new Material(m_ShaderSplats) { name = "GaussianSplats" };
                m_MatComposite = new Material(m_ShaderComposite) { name = "GaussianClearDstAlpha" };
                m_MatDebugPoints = new Material(m_ShaderDebugPoints) { name = "GaussianDebugPoints" };
                m_MatDebugBoxes = new Material(m_ShaderDebugBoxes) { name = "GaussianDebugBoxes" };
            }
        }

        public void EnsureRegistered()
        {
            if (!m_Registered && resourcesAreSetUp)
            {
                GaussianSplatRenderSystem.instance.RegisterSplat(this);
                m_Registered = true;
            }
        }

        public void OnEnable()
        {
            if (!resourcesAreSetUp)
                return;

            EnsureRegistered();

            CreateResourcesForAsset();
            m_PrevAsset = asset;
            m_PrevHash = asset != null ? asset.dataHash : new Hash128();
        }

        void SetAssetDataOnCS(CommandBuffer cmb, KernelIndices kernel)
        {
            ComputeShader cs = m_CSSplatUtilities;
            int kernelIndex = (int)kernel;
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatPos, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatChunks, m_GpuChunks);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatOther, m_GpuOtherData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSH, m_GpuSHData);
            cmb.SetComputeTextureParam(cs, kernelIndex, Props.SplatColor, m_GpuColorData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);

            cmb.SetComputeIntParam(cs, Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            cmb.SetComputeIntParam(cs, Props.SplatFormat, splatFormat);
            cmb.SetComputeIntParam(cs, Props.SplatCount, m_SplatCount);
            cmb.SetComputeIntParam(cs, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);

            UpdateCutoutsBuffer();
            cmb.SetComputeIntParam(cs, Props.SplatCutoutsCount, m_Cutouts?.Length ?? 0);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatCutouts, m_GpuEditCutouts);
        }

        internal void SetAssetDataOnMaterial(MaterialPropertyBlock mat)
        {
            mat.SetBuffer(Props.SplatPos, m_GpuPosData);
            mat.SetBuffer(Props.SplatOther, m_GpuOtherData);
            mat.SetBuffer(Props.SplatSH, m_GpuSHData);
            mat.SetTexture(Props.SplatColor, m_GpuColorData);
            mat.SetBuffer(Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            mat.SetBuffer(Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);
            mat.SetInt(Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            mat.SetInteger(Props.SplatFormat, splatFormat);
            mat.SetInteger(Props.SplatCount, m_SplatCount);
            mat.SetInteger(Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
        }

        static void DisposeBuffer(ref GraphicsBuffer buf)
        {
            buf?.Dispose();
            buf = null;
        }

        void DisposeResourcesForAsset()
        {
            DisposeDeformBuffers();
            GaussianSplatGpuData.Release(ref m_GpuData);
            m_GpuPosData = null;
            m_GpuOtherData = null;
            m_GpuSHData = null;
            m_GpuColorData = null;
            m_GpuChunks = null;
            m_ColorsBuffer = null;
            m_SortedFromValid = false;

            DisposeBuffer(ref m_GpuIndexBuffer);
            DisposeBuffer(ref m_GpuSortDistances);
            DisposeBuffer(ref m_GpuSortKeys);

            DisposeBuffer(ref m_GpuEditSelectedMouseDown);
            DisposeBuffer(ref m_GpuEditPosMouseDown);
            DisposeBuffer(ref m_GpuEditOtherMouseDown);
            DisposeBuffer(ref m_GpuEditSelected);
            DisposeBuffer(ref m_GpuEditDeleted);
            DisposeBuffer(ref m_GpuEditCountsBounds);
            DisposeBuffer(ref m_GpuEditCutouts);

            m_SorterArgs.resources.Dispose();

            m_SplatCount = 0;
            m_GpuChunksValid = false;

            editSelectedSplats = 0;
            editDeletedSplats = 0;
            editCutSplats = 0;
            editModified = false;
            editSelectedBounds = default;
        }

        public void OnDestroy()
        {
            // runtime-built (injected) assets own native arrays; nothing else would free the last one
            m_injectedAsset?.Dispose();
            m_injectedAsset = null;
        }

        public void OnDisable()
        {
            DisposeResourcesForAsset();
            GaussianSplatRenderSystem.instance.UnregisterSplat(this);
            m_Registered = false;

            DestroyImmediate(m_MatSplats);
            DestroyImmediate(m_MatComposite);
            DestroyImmediate(m_MatDebugPoints);
            DestroyImmediate(m_MatDebugBoxes);
        }

        // Per-camera values, the same for every object.
        internal static void SetViewParams(CommandBuffer cmb, ComputeShader cs, in SplatCameraViews views)
        {
            s_EyeMatrixVP[0] = views.gpuProjectionLeft * views.viewLeft;
            s_EyeMatrixVP[1] = views.gpuProjectionRight * views.viewRight;
            s_EyeMatrixP[0] = views.gpuProjectionLeft;
            s_EyeMatrixP[1] = views.gpuProjectionRight;

            cmb.SetComputeIntParam(cs, Props.SplatViewCount, views.count);
            cmb.SetComputeMatrixArrayParam(cs, Props.EyeMatrixVP, s_EyeMatrixVP);
            cmb.SetComputeMatrixArrayParam(cs, Props.EyeMatrixP, s_EyeMatrixP);
            cmb.SetComputeVectorParam(cs, Props.VecScreenParams, views.screenParams);
            cmb.SetComputeVectorParam(cs, Props.VecWorldSpaceCameraPos, views.cameraPosition);
        }

        internal void CalcViewData(CommandBuffer cmb, ComputeShader cs, in SplatCameraViews views, GraphicsBuffer view, GraphicsBuffer colors,
            GraphicsBuffer sortDistances, GraphicsBuffer sortKeys, int offset, int sortOffset, int totalCount, bool writeSortKeys)
        {
            var tr = transform;
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            Vector3 cameraLocal = matW2O.MultiplyPoint3x4(views.cameraPosition);

            if (writeSortKeys)
            {
                m_SortedFrom = cameraLocal;
                m_SortedFromValid = true;
            }

            int colorsHash = HashCode.Combine(m_OpacityScale, m_SHOrder, m_SHOnly, tint);
            bool writeColors = AreColorsOutdated(cameraLocal, colors, offset, colorsHash);
            if (writeColors)
            {
                m_ColorsFrom = cameraLocal;
                m_ColorsBuffer = colors;
                m_ColorsOffset = offset;
                m_ColorsHash = colorsHash;
            }

            // calculate view dependent data for each splat
            SetAssetDataOnCS(cmb, KernelIndices.CalcViewData);

            const int kernel = (int)KernelIndices.CalcViewData;
            cmb.SetComputeBufferParam(cs, kernel, Props.SplatSelectedBitsRead, m_GpuEditSelected ?? m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernel, Props.SplatViewData, view);
            cmb.SetComputeBufferParam(cs, kernel, Props.SplatColors, colors);
            cmb.SetComputeBufferParam(cs, kernel, Props.SplatSortDistances, sortDistances);
            cmb.SetComputeBufferParam(cs, kernel, Props.SplatSortKeys, sortKeys);
            cmb.SetComputeIntParam(cs, Props.SplatOffset, offset);
            cmb.SetComputeIntParam(cs, Props.SplatSortOffset, sortOffset);
            cmb.SetComputeIntParam(cs, Props.SplatTotalCount, totalCount);
            cmb.SetComputeIntParam(cs, Props.WriteSortKeys, writeSortKeys ? 1 : 0);
            cmb.SetComputeIntParam(cs, Props.WriteColors, writeColors ? 1 : 0);

            s_EyeMatrixMV[0] = views.viewLeft * matO2W;
            s_EyeMatrixMV[1] = views.viewRight * matO2W;
            cmb.SetComputeMatrixArrayParam(cs, Props.EyeMatrixMV, s_EyeMatrixMV);
            cmb.SetComputeMatrixParam(cs, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(cs, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeFloatParam(cs, Props.Disintegration, disintegration);
            Vector3 center = matO2W.MultiplyPoint3x4((asset.boundsMin + asset.boundsMax) * 0.5f);
            cmb.SetComputeVectorParam(cs, Props.DisintegrationCenter, new Vector4(center.x, center.y, center.z, disintegrationSpread));
            cmb.SetComputeFloatParam(cs, Props.Melt, melt);
            if (melt > 0f)
            {
                // world space height range of the asset bounds
                Vector3 extents = (asset.boundsMax - asset.boundsMin) * 0.5f;
                float halfHeight = Mathf.Abs(matO2W.m10) * extents.x + Mathf.Abs(matO2W.m11) * extents.y + Mathf.Abs(matO2W.m12) * extents.z;
                cmb.SetComputeVectorParam(cs, Props.MeltHeight, new Vector4(center.y - halfHeight, center.y + halfHeight));
            }
            // colors are still in gamma space here
            cmb.SetComputeVectorParam(cs, Props.SplatTint, tint);
            cmb.SetComputeFloatParam(cs, Props.SplatScale, m_SplatScale);
            cmb.SetComputeFloatParam(cs, Props.SplatOpacityScale, m_OpacityScale);
            cmb.SetComputeIntParam(cs, Props.SHOrder, m_SHOrder);
            cmb.SetComputeIntParam(cs, Props.SHOnly, m_SHOnly ? 1 : 0);

            cs.GetKernelThreadGroupSizes(kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(cs, kernel, (m_SplatCount + (int)gsX - 1) / (int)gsX, 1, 1);
        }

        internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            Matrix4x4 worldToCamMatrix = cam.worldToCameraMatrix;
            worldToCamMatrix.m20 *= -1;
            worldToCamMatrix.m21 *= -1;
            worldToCamMatrix.m22 *= -1;

            if (m_GpuSortKeys == null)
                InitSortBuffers(m_SplatCount);

            // calculate distance to the camera for each splat
            cmd.BeginSample(s_ProfSort);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortDistances, m_GpuSortDistances);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortKeys, m_GpuSortKeys);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatChunks, m_GpuChunks);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatPos, m_GpuPosData);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatFormat, splatFormat);
            cmd.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, worldToCamMatrix * matrix);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatCount, m_SplatCount);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.CalcDistances, out uint gsX, out _, out _);
            cmd.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, (m_GpuSortDistances.count + (int)gsX - 1) / (int)gsX, 1, 1);

            // sort the splats
            if (m_Sorter.Valid)
                m_Sorter.Dispatch(cmd, m_SorterArgs);
            cmd.EndSample(s_ProfSort);
        }

        public void Update()
        {
            var curHash = asset != null ? asset.dataHash : new Hash128();
            if (m_PrevAsset != asset || m_PrevHash != curHash)
            {
                m_PrevAsset?.Dispose();
                m_PrevAsset = asset;
                m_PrevHash = curHash;
                if (resourcesAreSetUp)
                {
                    DisposeResourcesForAsset();
                    CreateResourcesForAsset();
                }
                else
                {
                    Debug.LogError($"{nameof(GaussianSplatRenderer)} component is not set up correctly (Resource references are missing), or platform does not support compute shaders");
                }
            }
        }

        public void ActivateCamera(int index)
        {
            Camera mainCam = Camera.main;
            if (!mainCam)
                return;
            if (asset == null || asset.cameras == null)
                return;

            var selfTr = transform;
            var camTr = mainCam.transform;
            var prevParent = camTr.parent;
            var cam = asset.cameras[index];
            camTr.parent = selfTr;
            camTr.localPosition = cam.pos;
            camTr.localRotation = Quaternion.LookRotation(cam.axisZ, cam.axisY);
            camTr.parent = prevParent;
            camTr.localScale = Vector3.one;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(camTr);
#endif
        }

        void ClearGraphicsBuffer(GraphicsBuffer buf)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.ClearBuffer, Props.DstBuffer, buf);
            m_CSSplatUtilities.SetInt(Props.BufferSize, buf.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.ClearBuffer, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.ClearBuffer, (int)((buf.count + gsX - 1) / gsX), 1, 1);
        }

        void UnionGraphicsBuffers(GraphicsBuffer dst, GraphicsBuffer src)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.SrcBuffer, src);
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.DstBuffer, dst);
            m_CSSplatUtilities.SetInt(Props.BufferSize, dst.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.OrBuffers, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.OrBuffers, (int)((dst.count + gsX - 1) / gsX), 1, 1);
        }

        static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return math.asfloat(v ^ mask);
        }

        public void UpdateEditCountsAndBounds()
        {
            if (m_GpuEditSelected == null)
            {
                editSelectedSplats = 0;
                editDeletedSplats = 0;
                editCutSplats = 0;
                editModified = false;
                editSelectedBounds = default;
                return;
            }

            m_CSSplatUtilities.SetBuffer((int)KernelIndices.InitEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.InitEditData, 1, 1, 1);

            using CommandBuffer cmb = new CommandBuffer();
            SetAssetDataOnCS(cmb, KernelIndices.UpdateEditData);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.UpdateEditData, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, (int)((m_GpuEditSelected.count + gsX - 1) / gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);

            uint[] res = new uint[m_GpuEditCountsBounds.count];
            m_GpuEditCountsBounds.GetData(res);
            editSelectedSplats = res[0];
            editDeletedSplats = res[1];
            editCutSplats = res[2];
            Vector3 min = new Vector3(SortableUintToFloat(res[3]), SortableUintToFloat(res[4]), SortableUintToFloat(res[5]));
            Vector3 max = new Vector3(SortableUintToFloat(res[6]), SortableUintToFloat(res[7]), SortableUintToFloat(res[8]));
            Bounds bounds = default;
            bounds.SetMinMax(min, max);
            if (bounds.extents.sqrMagnitude < 0.01)
                bounds.extents = new Vector3(0.1f, 0.1f, 0.1f);
            editSelectedBounds = bounds;
        }

        void UpdateCutoutsBuffer()
        {
            int bufferSize = m_Cutouts?.Length ?? 0;
            if (bufferSize == 0)
                bufferSize = 1;
            if (m_GpuEditCutouts == null || m_GpuEditCutouts.count != bufferSize)
            {
                m_GpuEditCutouts?.Dispose();
                m_GpuEditCutouts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, UnsafeUtility.SizeOf<GaussianCutout.ShaderData>()) { name = "GaussianCutouts" };
            }

            // nothing reads the buffer without cutouts, it only has to be bound
            if (m_Cutouts == null || m_Cutouts.Length == 0)
                return;

            NativeArray<GaussianCutout.ShaderData> data = new(bufferSize, Allocator.Temp);
            {
                var matrix = transform.localToWorldMatrix;
                for (var i = 0; i < m_Cutouts.Length; ++i)
                {
                    data[i] = GaussianCutout.GetShaderData(m_Cutouts[i], matrix);
                }
            }

            m_GpuEditCutouts.SetData(data);
            data.Dispose();
        }

        bool EnsureEditingBuffers()
        {
            if (!HasValidAsset || !HasValidRenderSetup)
                return false;

            if (m_GpuEditSelected == null)
            {
                // edits write into the splat data, which other renderers of the asset share
                DisposeDeformBuffers();
                if (m_GpuData.cached)
                {
                    GaussianSplatGpuData.Release(ref m_GpuData);
                    m_GpuData = GaussianSplatGpuData.CreatePrivate(asset);
                }
                BindGpuData();

                var target = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource |
                             GraphicsBuffer.Target.CopyDestination;
                var size = (m_SplatCount + 31) / 32;
                m_GpuEditSelected = new GraphicsBuffer(target, size, 4) { name = "GaussianSplatSelected" };
                m_GpuEditSelectedMouseDown = new GraphicsBuffer(target, size, 4) { name = "GaussianSplatSelectedInit" };
                m_GpuEditDeleted = new GraphicsBuffer(target, size, 4) { name = "GaussianSplatDeleted" };
                m_GpuEditCountsBounds = new GraphicsBuffer(target, 3 + 6, 4) { name = "GaussianSplatEditData" }; // selected count, deleted bound, cut count, float3 min, float3 max
                ClearGraphicsBuffer(m_GpuEditSelected);
                ClearGraphicsBuffer(m_GpuEditSelectedMouseDown);
                ClearGraphicsBuffer(m_GpuEditDeleted);
            }
            return m_GpuEditSelected != null;
        }

        public void EditStoreSelectionMouseDown()
        {
            if (!EnsureEditingBuffers()) return;
            Graphics.CopyBuffer(m_GpuEditSelected, m_GpuEditSelectedMouseDown);
        }

        public void EditStorePosMouseDown()
        {
            if (m_GpuEditPosMouseDown == null)
            {
                m_GpuEditPosMouseDown = new GraphicsBuffer(m_GpuPosData.target | GraphicsBuffer.Target.CopyDestination, m_GpuPosData.count, m_GpuPosData.stride) { name = "GaussianSplatEditPosMouseDown" };
            }
            Graphics.CopyBuffer(m_GpuPosData, m_GpuEditPosMouseDown);
        }
        public void EditStoreOtherMouseDown()
        {
            if (m_GpuEditOtherMouseDown == null)
            {
                m_GpuEditOtherMouseDown = new GraphicsBuffer(m_GpuOtherData.target | GraphicsBuffer.Target.CopyDestination, m_GpuOtherData.count, m_GpuOtherData.stride) { name = "GaussianSplatEditOtherMouseDown" };
            }
            Graphics.CopyBuffer(m_GpuOtherData, m_GpuEditOtherMouseDown);
        }

        public void EditUpdateSelection(Vector2 rectMin, Vector2 rectMax, Camera cam, bool subtract)
        {
            if (!EnsureEditingBuffers()) return;

            Graphics.CopyBuffer(m_GpuEditSelectedMouseDown, m_GpuEditSelected);

            var tr = transform;
            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            Vector4 screenPar = new Vector4(screenW, screenH, 0, 0);
            Vector4 camPos = cam.transform.position;

            using var cmb = new CommandBuffer { name = "SplatSelectionUpdate" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectionUpdate);

            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, matView * matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecScreenParams, screenPar);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecWorldSpaceCameraPos, camPos);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_SelectionRect", new Vector4(rectMin.x, rectMax.y, rectMax.x, rectMin.y));
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SelectionMode, subtract ? 0 : 1);

            DispatchUtilsAndExecute(cmb, KernelIndices.SelectionUpdate, m_SplatCount);
            UpdateEditCountsAndBounds();
        }


        public void EditTranslateSelection(Vector3 localSpacePosDelta)
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatTranslateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.TranslateSelection);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, localSpacePosDelta);

            DispatchUtilsAndExecute(cmb, KernelIndices.TranslateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        public void EditRotateSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Quaternion rotation)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null || m_GpuEditOtherMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatRotateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.RotateSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatOtherMouseDown, m_GpuEditOtherMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDeltaRot, new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));

            DispatchUtilsAndExecute(cmb, KernelIndices.RotateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }


        public void EditScaleSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Vector3 scale)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatScaleSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.ScaleSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ScaleSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, scale);

            DispatchUtilsAndExecute(cmb, KernelIndices.ScaleSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        public void EditDeleteSelected()
        {
            if (!EnsureEditingBuffers()) return;
            UnionGraphicsBuffers(m_GpuEditDeleted, m_GpuEditSelected);
            EditDeselectAll();
            UpdateEditCountsAndBounds();
            if (editDeletedSplats != 0)
                editModified = true;
        }

        public void EditSelectAll()
        {
            if (!EnsureEditingBuffers()) return;
            using var cmb = new CommandBuffer { name = "SplatSelectAll" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectAll);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.SelectAll, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.SelectAll, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public void EditDeselectAll()
        {
            if (!EnsureEditingBuffers()) return;
            ClearGraphicsBuffer(m_GpuEditSelected);
            UpdateEditCountsAndBounds();
        }

        public void EditInvertSelection()
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatInvertSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.InvertSelection);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.InvertSelection, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.InvertSelection, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public bool EditExportData(GraphicsBuffer dstData, bool bakeTransform)
        {
            if (!EnsureEditingBuffers()) return false;

            int flags = 0;
            var tr = transform;
            Quaternion bakeRot = tr.localRotation;
            Vector3 bakeScale = tr.localScale;

            if (bakeTransform)
                flags = 1;

            using var cmb = new CommandBuffer { name = "SplatExportData" };
            SetAssetDataOnCS(cmb, KernelIndices.ExportData);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_ExportTransformFlags", flags);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformRotation", new Vector4(bakeRot.x, bakeRot.y, bakeRot.z, bakeRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformScale", bakeScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, tr.localToWorldMatrix);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ExportData, "_ExportBuffer", dstData);

            DispatchUtilsAndExecute(cmb, KernelIndices.ExportData, m_SplatCount);
            return true;
        }

        public void EditSetSplatCount(int newSplatCount)
        {
            if (newSplatCount <= 0 || newSplatCount > GaussianSplatAsset.kMaxSplats)
            {
                Debug.LogError($"Invalid new splat count: {newSplatCount}");
                return;
            }
            if (asset.chunkData != null)
            {
                Debug.LogError("Only splats with VeryHigh quality can be resized");
                return;
            }
            if (newSplatCount == splatCount)
                return;

            int posStride = (int)(asset.posData.dataSize / asset.splatCount);
            int otherStride = (int)(asset.otherData.dataSize / asset.splatCount);
            int shStride = (int)(asset.shData.dataSize / asset.splatCount);

            // create new GPU buffers
            var newPosData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * posStride / 4, 4) { name = "GaussianPosData" };
            var newOtherData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * otherStride / 4, 4) { name = "GaussianOtherData" };
            var newSHData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, newSplatCount * shStride / 4, 4) { name = "GaussianSHData" };

            // new texture is a RenderTexture so we can write to it from a compute shader
            var (texWidth, texHeight) = GaussianSplatAsset.CalcTextureSize(newSplatCount);
            var texFormat = GaussianSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var newColorData = new RenderTexture(texWidth, texHeight, texFormat, GraphicsFormat.None) { name = "GaussianColorData", enableRandomWrite = true };
            newColorData.Create();

            // selected/deleted buffers
            var selTarget = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.CopyDestination;
            var selSize = (newSplatCount + 31) / 32;
            var newEditSelected = new GraphicsBuffer(selTarget, selSize, 4) { name = "GaussianSplatSelected" };
            var newEditSelectedMouseDown = new GraphicsBuffer(selTarget, selSize, 4) { name = "GaussianSplatSelectedInit" };
            var newEditDeleted = new GraphicsBuffer(selTarget, selSize, 4) { name = "GaussianSplatDeleted" };
            ClearGraphicsBuffer(newEditSelected);
            ClearGraphicsBuffer(newEditSelectedMouseDown);
            ClearGraphicsBuffer(newEditDeleted);

            DisposeBuffer(ref m_GpuSortDistances);
            DisposeBuffer(ref m_GpuSortKeys);

            // copy existing data over into new buffers
            EditCopySplats(transform, newPosData, newOtherData, newSHData, newColorData, newEditDeleted, newSplatCount, 0, 0, m_SplatCount);

            // use the new buffers and the new splat count
            m_GpuData.Replace(newPosData, newOtherData, newSHData, newColorData);
            BindGpuData();

            m_GpuEditSelected?.Dispose();
            m_GpuEditSelectedMouseDown?.Dispose();
            m_GpuEditDeleted?.Dispose();

            m_GpuEditSelected = newEditSelected;
            m_GpuEditSelectedMouseDown = newEditSelectedMouseDown;
            m_GpuEditDeleted = newEditDeleted;

            DisposeBuffer(ref m_GpuEditPosMouseDown);
            DisposeBuffer(ref m_GpuEditOtherMouseDown);

            m_SplatCount = newSplatCount;
            editModified = true;
        }

        public void EditCopySplatsInto(GaussianSplatRenderer dst, int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            EditCopySplats(
                dst.transform,
                dst.m_GpuPosData, dst.m_GpuOtherData, dst.m_GpuSHData, dst.m_GpuColorData, dst.m_GpuEditDeleted,
                dst.splatCount,
                copySrcStartIndex, copyDstStartIndex, copyCount);
            dst.editModified = true;
        }

        public void EditCopySplats(
            Transform dstTransform,
            GraphicsBuffer dstPos, GraphicsBuffer dstOther, GraphicsBuffer dstSH, Texture dstColor,
            GraphicsBuffer dstEditDeleted,
            int dstSize,
            int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            if (!EnsureEditingBuffers()) return;

            Matrix4x4 copyMatrix = dstTransform.worldToLocalMatrix * transform.localToWorldMatrix;
            Quaternion copyRot = copyMatrix.rotation;
            Vector3 copyScale = copyMatrix.lossyScale;

            using var cmb = new CommandBuffer { name = "SplatCopy" };
            SetAssetDataOnCS(cmb, KernelIndices.CopySplats);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstPos", dstPos);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstOther", dstOther);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstSH", dstSH);
            cmb.SetComputeTextureParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstColor", dstColor);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstEditDeleted", dstEditDeleted);

            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstSize", dstSize);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopySrcStartIndex", copySrcStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstStartIndex", copyDstStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyCount", copyCount);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformRotation", new Vector4(copyRot.x, copyRot.y, copyRot.z, copyRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformScale", copyScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, "_CopyTransformMatrix", copyMatrix);

            DispatchUtilsAndExecute(cmb, KernelIndices.CopySplats, copyCount);
        }

        void DispatchUtilsAndExecute(CommandBuffer cmb, KernelIndices kernel, int count)
        {
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)kernel, (int)((count + gsX - 1) / gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);
        }

        public GraphicsBuffer GpuEditDeleted => m_GpuEditDeleted;
    }
}