// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace GaussianSplatting.Runtime
{
    // Sorts and projects the splats before the camera's main render pass, then draws them straight into the camera
    // target. Keeping compute out of the main pass and skipping an intermediate texture lets URP merge everything
    // into one native render pass, which is what tile-based mobile XR GPUs need.
    // ReSharper disable once InconsistentNaming
    public class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        const int kDirectShaderPass = 1;
        const int kDepthShaderPass = 2;

        [Range(0f, 0.9f), Tooltip("Splats write depth where they are more opaque than this, so that particles and other transparents behind them are hidden. 0 turns depth writing off.")]
        public float m_DepthAlphaCutoff = 0.3f;

        class PreparePass : ScriptableRenderPass
        {
            class PassData
            {
                internal Camera Camera;
                internal SplatCameraViews Views;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                using var builder = renderGraph.AddUnsafePass("GaussianSplat.Prepare", out PassData passData);
                passData.Camera = cameraData.camera;
                passData.Views = CaptureViews(cameraData);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    GaussianSplatRenderSystem.instance.PrepareSplats(data.Camera, data.Views, cmd);
                });
            }

            static SplatCameraViews CaptureViews(UniversalCameraData camera)
            {
                bool xr = camera.xr != null && camera.xr.enabled;
                int count = xr ? camera.xr.viewCount : 1;
                Vector2 size = xr
                    ? camera.xr.GetViewport(0).size
                    : new Vector2(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
                int rightEye = count > 1 ? 1 : 0;
                return new SplatCameraViews
                {
                    count = count,
                    viewLeft = camera.GetViewMatrix(0),
                    viewRight = camera.GetViewMatrix(rightEye),
                    gpuProjectionLeft = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(0), true),
                    gpuProjectionRight = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(rightEye), true),
                    screenParams = new Vector4(size.x, size.y, 0, 0),
                    cameraPosition = camera.camera.transform.position,
                };
            }
        }

        class DrawPass : ScriptableRenderPass
        {
            internal float DepthAlphaCutoff;

            class PassData
            {
                internal List<SplatDraw> Draws;
                internal float DepthAlphaCutoff;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                using var builder = renderGraph.AddRasterRenderPass("GaussianSplat.Draw", out PassData passData);
                passData.Draws = GaussianSplatRenderSystem.instance.preparedDraws;
                passData.DepthAlphaCutoff = DepthAlphaCutoff;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                ConfigureFoveation(builder, cameraData, resourceData.isActiveTargetBackBuffer);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    foreach (var draw in data.Draws)
                    {
                        // debug shaders only have the one pass
                        bool splats = draw.material == draw.splat.m_MatSplats;
                        int pass = splats ? kDirectShaderPass : 0;
                        context.cmd.DrawProcedural(draw.splat.m_GpuIndexBuffer, draw.matrix, draw.material, pass,
                            MeshTopology.Triangles, draw.indexCount, draw.instanceCount, draw.properties);

                        if (!splats || data.DepthAlphaCutoff <= 0f) continue;
                        draw.properties.SetFloat(GaussianSplatRenderer.Props.DepthAlphaCutoff, data.DepthAlphaCutoff);
                        context.cmd.DrawProcedural(draw.splat.m_GpuIndexBuffer, draw.matrix, draw.material, kDepthShaderPass,
                            MeshTopology.Triangles, draw.indexCount, draw.instanceCount, draw.properties);
                    }
                });
            }

            // Must match URP's opaque and transparent passes, otherwise they cannot merge with this one.
            static void ConfigureFoveation(IRasterRenderGraphBuilder builder, UniversalCameraData camera, bool backBuffer)
            {
                if (camera.xr == null || !camera.xr.enabled || !camera.xr.supportsFoveatedRendering)
                {
                    builder.EnableFoveatedRasterization(false);
                    return;
                }
                bool mobileXR = Application.platform == RuntimePlatform.Android;
                builder.EnableFoveatedRasterization(backBuffer || !mobileXR || XRSystem.GetRenderViewportScale() == 1.0f);
            }
        }

        PreparePass m_PreparePass;
        DrawPass m_DrawPass;

        public override void Create()
        {
            m_PreparePass = new PreparePass { renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses };
            m_DrawPass = new DrawPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!GaussianSplatRenderSystem.instance.GatherSplatsForCamera(renderingData.cameraData.camera, GetCullingViews(ref renderingData)))
                return;
            m_DrawPass.DepthAlphaCutoff = m_DepthAlphaCutoff;
            renderer.EnqueuePass(m_PreparePass);
            renderer.EnqueuePass(m_DrawPass);
        }

        static SplatCullingViews GetCullingViews(ref RenderingData renderingData)
        {
            ref var camera = ref renderingData.cameraData;
            int count = camera.xr != null && camera.xr.enabled ? camera.xr.viewCount : 1;
            int rightEye = count > 1 ? 1 : 0;
            return new SplatCullingViews
            {
                count = Mathf.Min(count, GaussianSplatRenderer.kMaxViews),
                viewProjectionLeft = camera.GetProjectionMatrix(0) * camera.GetViewMatrix(0),
                viewProjectionRight = camera.GetProjectionMatrix(rightEye) * camera.GetViewMatrix(rightEye),
            };
        }

        protected override void Dispose(bool disposing)
        {
            m_PreparePass = null;
            m_DrawPass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
