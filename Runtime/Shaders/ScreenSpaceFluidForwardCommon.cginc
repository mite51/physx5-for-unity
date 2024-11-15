//  Forward pass common

#ifndef SCREEN_SPACE_FLUID_FORWARD_COMMON_INCLUDED
#define SCREEN_SPACE_FLUID_FORWARD_COMMON_INCLUDED

#include "UnityCG.cginc"
#include "UnityLightingCommon.cginc"
#include "UnityPBSLighting.cginc"
#include "AutoLight.cginc"


// -----------------------------
//  Shader properties
// -----------------------------
sampler2D _DepthTex;

sampler2D _FluidBackground;
float4 _FluidBackground_ST;
sampler2D _ColorTex;
float FLIP_Y = 0;
float LERP_BLEND = 0; // Blend fluid with background using multiplication

fixed4 _Color;
half _Glossiness;
fixed4 _Specular;
half _Fresnel;

// -----------------------------
//  View space position
// -----------------------------
inline float3 getFluidPixelViewPos(float2 uv)
{
    float3 vdir = mul(unity_CameraInvProjection, float4(uv * 2.0 - 1.0, 0, 1));
    #ifdef SHADER_API_GLCORE
        float dist = -tex2D(_DepthTex, uv).x;
    #else
        float dist = -tex2D(_DepthTex, FLIP_Y ? uv : float2(uv.x, 1 - uv.y)).x;
    #endif
    return (vdir / vdir.z) * dist;
}

// -----------------------------
//  View space normal
// -----------------------------
inline float3 getFluidPixelViewNormal(float2 uv, float3 vpos)
{
    float3 pixelSize = float3(1 / _ScreenParams.x, 1 / _ScreenParams.y, 0);
    float3 ddx1 = getFluidPixelViewPos(uv + pixelSize.xz);
    float3 ddx2 = getFluidPixelViewPos(uv - pixelSize.xz);
    float3 ddy1 = getFluidPixelViewPos(uv + pixelSize.zy);
    float3 ddy2 = getFluidPixelViewPos(uv - pixelSize.zy);
    
    if (abs(ddx1.z - vpos.z) > 0.5) ddx1 = vpos;
    if (abs(ddx2.z - vpos.z) > 0.5) ddx2 = vpos;
    if (abs(ddy1.z - vpos.z) > 0.5) ddy1 = vpos;
    if (abs(ddy2.z - vpos.z) > 0.5) ddy2 = vpos;
    return normalize(cross(ddx1 - ddx2, ddy1 - ddy2));
}

// -----------------------------
//  Light helpers without shadow
// -----------------------------
#ifdef DIRECTIONAL
    #define FLUID_ATTENUATION_NO_SHADOW(dest, worldPos) fixed dest = 1;
#endif

#ifdef POINT
    #define FLUID_ATTENUATION_NO_SHADOW(dest, worldPos) \
        unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz; \
        fixed dest = tex2D(_LightTexture0, dot(lightCoord, lightCoord).rr).r;
#endif

#ifdef SPOT
    #define FLUID_ATTENUATION_NO_SHADOW(dest, worldPos) \
        unityShadowCoord4 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)); \
        fixed dest = (lightCoord.z > 0) * UnitySpotCookie(lightCoord) * UnitySpotAttenuate(lightCoord.xyz);
#endif

#ifdef POINT_COOKIE
    #define FLUID_ATTENUATION_NO_SHADOW(dest, worldPos) \
        unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz; \
        fixed dest = tex2D(_LightTextureB0, dot(lightCoord, lightCoord).rr).r * texCUBE(_LightTexture0, lightCoord).w;
#endif

#ifdef DIRECTIONAL_COOKIE
    #define FLUID_ATTENUATION_NO_SHADOW(dest, worldPos) \
        unityShadowCoord2 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xy; \
        fixed dest = tex2D(_LightTexture0, lightCoord).w;
#endif

struct v2g { };

struct g2f {
    float4 pos : SV_POSITION;
    float2 screenCoords : TEXCOORD0;
};

struct f2o {
    float4 c : SV_Target;
    float d : SV_Depth;
};

// -----------------------------
//  Vertex shader
// -----------------------------
v2g vert(appdata_full i) {
    v2g o;
    UNITY_INITIALIZE_OUTPUT(v2g, o)
    return o;
}

// -----------------------------
//  Geometry shader
// -----------------------------
[maxvertexcount(4)]
void geom(point v2g i[1], uint id : SV_PrimitiveId, inout TriangleStream<g2f> triStream) {
    if (id != 0) return;

    float4 pos[4] = {
        float4( 1, 1, 0, 1),
        float4( 1, -1, 0, 1),
        float4(-1, 1, 0, 1),
        float4(-1, -1, 0, 1),
    };

    float2 screenCoords[4] = {
        float2(1.0f, 0.0f),
        float2(1.0f, 1.0f),
        float2(0.0f, 0.0f),
        float2(0.0f, 1.0f)
    };

    g2f o = (g2f)0;

    for (int idx = 0; idx < 4; ++idx)
    {
        o.pos = pos[idx];
        o.screenCoords = screenCoords[idx];
        triStream.Append(o);
    }
}

// -----------------------------
//  Fragment shader
// -----------------------------
f2o frag(g2f i)
{
    // PhysX 5 only supports Windows and Linux, so there are only GL, D3D or Vulkan
    #ifdef SHADER_API_GLCORE
        i.screenCoords.y = 1.0 - i.screenCoords.y;
    #endif

    // Depth
    float3 vpos = getFluidPixelViewPos(i.screenCoords);
    if (-vpos.z == _ProjectionParams.z) discard;

    f2o o;
    UNITY_INITIALIZE_OUTPUT(f2o, o)

    float4 pos = mul(UNITY_MATRIX_P, float4(vpos, 1));
    #ifdef SHADER_API_GLCORE
        o.d = (pos.z / pos.w + 1.0) * 0.5;
    #else
        o.d = pos.z / pos.w;
    #endif

    // Color
    float3 vnrm = getFluidPixelViewNormal(i.screenCoords, vpos);
    float3 worldPos = mul(UNITY_MATRIX_I_V, float4(vpos, 1));
    float3 worldNormal = mul(UNITY_MATRIX_I_V, vnrm);
    float3 lightDir = normalize(UnityWorldSpaceLightDir(worldPos));
    float3 viewDir = normalize(UnityWorldSpaceViewDir(worldPos));
    
    float2 uv_FluidBackground = i.screenCoords + vnrm.xy * -0.025;
    float2 uv_ColorTex = i.screenCoords.xy;
    fixed4 colorBackground = tex2D(_FluidBackground, uv_FluidBackground);
    #ifdef SHADER_API_GLCORE
        fixed4 colorTex = tex2D(_ColorTex, uv_ColorTex);
    #else
        // TODO: This is an ugly hack.
        // Rendered texture should be flipped during preprossing not here
        fixed4 colorTex = tex2D(_ColorTex, FLIP_Y ? uv_ColorTex : float2(uv_ColorTex.x, 1 - uv_ColorTex.y));
    #endif
    fixed4 blendedColor = LERP_BLEND ? lerp(colorBackground, colorTex, colorTex.a) : colorBackground * colorTex;

    // Surface data
    SurfaceOutputStandardSpecular surfaceData = (SurfaceOutputStandardSpecular)0;
    surfaceData.Normal = worldNormal;
    float3 vdir = normalize(mul(unity_CameraInvProjection, float4(uv_FluidBackground * 2.0 - 1.0, 1, 1)));
    float fren = saturate(1 - dot(vdir, mul(unity_WorldToCamera, worldNormal)));
    fren = _Fresnel + (1 - _Fresnel) * (fren * fren * fren * fren * fren);
    fren = 0.6 * fren;

    // Albedo should not be pure black for specular to work
    surfaceData.Albedo = _Color;
    surfaceData.Emission = blendedColor.rgb * (1 - fren);
    surfaceData.Specular = _Specular * fren;
    surfaceData.Smoothness = _Glossiness;
    surfaceData.Occlusion = 1.0;
    surfaceData.Alpha = 0.0;

    // UnityGI data
    UnityGI gi = (UnityGI)0;
    gi.indirect.diffuse = 0;
    gi.indirect.specular = 0;
    gi.light.color = _LightColor0.rgb;
    gi.light.dir = lightDir;

    UnityGIInput giInput = (UnityGIInput)0;
    giInput.light = gi.light;
    giInput.worldPos = worldPos;
    giInput.worldViewDir = viewDir;
    FLUID_ATTENUATION_NO_SHADOW(atten, worldPos)
    giInput.atten = atten;

    float3 sh = 0;
    #if UNITY_SHOULD_SAMPLE_SH
        sh = ShadeSHPerPixel(worldNormal, 0, worldPos);
    #endif
    giInput.ambient = sh;

    #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
        giInput.boxMin[0] = unity_SpecCube0_BoxMin;
        giInput.boxMin[1] = unity_SpecCube1_BoxMin;
    #endif
    
    #ifdef UNITY_SPECCUBE_BOX_PROJECTION
        giInput.boxMax[0] = unity_SpecCube0_BoxMax;
        giInput.boxMax[1] = unity_SpecCube1_BoxMax;
        giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
        giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
        giInput.probeHDR[0] = unity_SpecCube0_HDR;
        giInput.probeHDR[1] = unity_SpecCube1_HDR;
    #endif
    LightingStandardSpecular_GI(surfaceData, giInput, gi);

    o.c += LightingStandardSpecular(surfaceData, viewDir, gi);
    o.c.rgb += surfaceData.Emission;
    #ifdef UNITY_PASS_FORWARDBASE
        o.c.a = 1;
    #elif defined(UNITY_PASS_FORWARDADD)
        o.c.a = atten;
    #endif

    return o;
}

#endif