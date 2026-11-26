// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

CGINCLUDE
#include "UnityCG.cginc"
#include "GaussianSplatting.hlsl"

StructuredBuffer<uint> _OrderBuffer;

struct appdata
{
    uint vtxID : SV_VertexID;
#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
    UNITY_VERTEX_INPUT_INSTANCE_ID
#else
    uint instanceID : SV_InstanceID;
#endif
};

struct v2f
{
    half4 col : COLOR0;
    float2 pos : TEXCOORD0;
    float4 vertex : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

StructuredBuffer<SplatViewData> _SplatViewData;
StructuredBuffer<uint2> _SplatColors; // 4xFP16
uint _SplatCount; // of all objects together
uint _SplatDrawCount; // without culled objects
float4 _VecScreenParams;
float _DepthAlphaCutoff;

v2f vertCommon (appdata v, bool direct, bool depthOnly)
{
    v2f o = (v2f)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    uint instID = v.instanceID;
#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
    instID = unity_InstanceID;
#endif

    uint eye = 0;
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    eye = min((uint)unity_StereoEyeIndex, 1u);
#endif

    // the sort order is front to back, blending straight into the camera target needs back to front
    instID = _OrderBuffer[direct && !depthOnly ? _SplatDrawCount - 1u - instID : instID];

    SplatViewData view = _SplatViewData[instID + eye * _SplatCount];
    uint2 color = _SplatColors[instID];
    float4 centerClipPos = view.pos;
    bool skip = centerClipPos.w <= 0;
    float radius = 2;
    if (depthOnly)
    {
        // cover only the part of the splat that is opaque enough to write depth
        float opacity = f16tof32(color.y);
        skip = skip || opacity <= _DepthAlphaCutoff;
        radius = min(radius, sqrt(max(log(opacity / _DepthAlphaCutoff), 0)));
    }
    if (skip)
    {
        o.vertex = asfloat(0x7fc00000); // NaN discards the primitive
    }
    else
    {
        o.col.r = f16tof32(color.x >> 16);
        o.col.g = f16tof32(color.x);
        o.col.b = f16tof32(color.y >> 16);
        o.col.a = f16tof32(color.y);
        // no composite pass converts the color later
        if (direct)
            o.col.rgb = GammaToLinearSpace(saturate(o.col.rgb));

        uint idx = v.vtxID;
        float2 quadPos = float2(idx&1, (idx>>1)&1) * 2.0 - 1.0;
        quadPos *= radius;

        o.pos = quadPos;

        float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _VecScreenParams.xy;
        o.vertex = centerClipPos;
        o.vertex.xy += deltaScreenPos * centerClipPos.w;
    }
    if (direct)
    {
#if UNITY_UV_STARTS_AT_TOP
        // view data uses the render-texture projection; match the target the pass actually draws to
        o.vertex.y *= -_ProjectionParams.x;
#endif
    }
    else
    {
        FlipProjectionIfBackbuffer(o.vertex);
    }
    return o;
}

v2f vert (appdata v) { return vertCommon(v, false, false); }
v2f vertDirect (appdata v) { return vertCommon(v, true, false); }
v2f vertDepth (appdata v) { return vertCommon(v, true, true); }

half4 fragDepth (v2f i) : SV_Target
{
    clip(exp(-dot(i.pos, i.pos)) * i.col.a - _DepthAlphaCutoff);
    return 0;
}

half4 frag (v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    float power = -dot(i.pos, i.pos);
    half alpha = exp(power);
    if (i.col.a >= 0)
    {
        alpha = saturate(alpha * i.col.a);
    }
    else
    {
        // "selected" splat: magenta outline, increase opacity, magenta tint
        half3 selectedColor = half3(1,0,1);
        if (alpha > 7.0/255.0)
        {
            if (alpha < 10.0/255.0)
            {
                alpha = 1;
                i.col.rgb = selectedColor;
            }
            alpha = saturate(alpha + 0.3);
        }
        i.col.rgb = lerp(i.col.rgb, selectedColor, 0.5);
    }

    if (alpha < 1.0/255.0)
        discard;

    half4 res = half4(i.col.rgb * alpha, alpha);
    return res;
}
ENDCG

        // Front to back into an intermediate texture, followed by the composite shader.
        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One
            Cull Off

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma multi_compile_instancing
ENDCG
        }

        // Back to front straight into the camera target: no intermediate texture or composite, cheap on tile-based GPUs.
        Pass
        {
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

CGPROGRAM
#pragma vertex vertDirect
#pragma fragment frag
#pragma require compute
#pragma multi_compile_instancing
ENDCG
        }

        // Depth of the mostly opaque splat cores, so that particles and other transparents are hidden behind splats.
        Pass
        {
            ZWrite On
            ColorMask 0
            Cull Off

CGPROGRAM
#pragma vertex vertDepth
#pragma fragment fragDepth
#pragma require compute
#pragma multi_compile_instancing
ENDCG
        }
    }
}
