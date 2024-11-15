#ifndef SCREEN_SPACE_FLUID_DEPTH_INCLUDED
#define SCREEN_SPACE_FLUID_DEPTH_INCLUDED

#include "UnityCG.cginc"

uint _IndexCount;
StructuredBuffer<uint> _Indices;
StructuredBuffer<float4> _Points;
StructuredBuffer<float4> _Anisotropy1;
StructuredBuffer<float4> _Anisotropy2;
StructuredBuffer<float4> _Anisotropy3;
float4x4 _ViewMatrix;
float4x4 _ProjMatrix;

struct v2g {};

struct g2f
{
    float4 pos : SV_Position;
    float3 viewPos : VIEWPOS;
    float2 uv : TEXCOORD0;
    float4 ani1 : ANISOTROPY1;
    float4 ani2 : ANISOTROPY2;
    float4 ani3 : ANISOTROPY3;
    float aniScale : ANI_SCALE;
};

struct f2o
{
    float depth : SV_Target;
};

v2g vert() {
    v2g o;
    UNITY_INITIALIZE_OUTPUT(v2g, o)
    return o;
}

[maxvertexcount(4)]
void geom(point v2g i[1], uint id : SV_PrimitiveId, inout TriangleStream<g2f> triStream)
{
    if (id >= _IndexCount) return;
    uint index = _Indices[id];
    float3 particleWorldPos = _Points[index].xyz;

    #ifdef UNITY_PASS_SHADOWCASTER
        #define MATRIX_V UNITY_MATRIX_V
        #define MATRIX_P UNITY_MATRIX_P
    #else
        #define MATRIX_V _ViewMatrix
        #define MATRIX_P _ProjMatrix
    #endif

    float4 ani01 = _Anisotropy1[index];
    float4 ani02 = _Anisotropy2[index];
    float4 ani03 = _Anisotropy3[index];
    float3 particleViewPos = mul(MATRIX_V, float4(particleWorldPos, 1));

    float4 ani1 = float4(mul(MATRIX_V, ani01.xyz).xyz, ani01.w + 0.001);
    float4 ani2 = float4(mul(MATRIX_V, ani02.xyz).xyz, ani02.w + 0.001);
    float4 ani3 = float4(mul(MATRIX_V, ani03.xyz).xyz, ani03.w + 0.001);

    // Quad size
    float scale = max(0.01, max(ani1.w, max(ani2.w, ani3.w))) * 1.5;
    
    // Scale the ani scale to the quad coordinate
    ani1.w /= scale;
    ani2.w /= scale;
    ani3.w /= scale;

    float3 up = float3(0, 1, 0), right = float3(1, 0, 0);
    float3 viewPos[4] = {
        particleViewPos + (right - up) * scale,
        particleViewPos + (right + up) * scale,
        particleViewPos + (-right - up) * scale,
        particleViewPos + (-right + up) * scale
    };

    float2 uv[4] = {
        float2(1, 0),
        float2(1, 1),
        float2(0, 0),
        float2(0, 1)
    };

    g2f o;
    UNITY_INITIALIZE_OUTPUT(g2f, o)
    o.ani1 = ani1;
    o.ani2 = ani2;
    o.ani3 = ani3;
    o.aniScale = scale;

    for (int idx = 0; idx < 4; ++idx)
    {
        float4 pos = mul(MATRIX_P, float4(viewPos[idx], 1));
        #ifdef UNITY_PASS_SHADOWCASTER
            // This is ugly. Just don't use large shadow bias.
            // Receiving shadow doesn't work for transparent anyways.
            pos = UnityApplyLinearShadowBias(pos);
        #endif
        o.pos = pos;
        o.viewPos = viewPos[idx];
        o.uv = uv[idx];
        triStream.Append(o);
    }
}

f2o frag(g2f i)
{
    f2o o;
    UNITY_INITIALIZE_OUTPUT(f2o, o)

    // Convert to a sphere, then calculate the surface distance from the quad
    float3x3 aniTrans = transpose(float3x3(i.ani1.xyz * i.ani1.w, i.ani2.xyz * i.ani2.w, i.ani3.xyz * i.ani3.w));
    float3x3 invAniTrans = float3x3(i.ani1.xyz / i.ani1.w, i.ani2.xyz / i.ani2.w, i.ani3.xyz / i.ani3.w);

    float3 quadCoord = float3(i.uv * float2(2.0, 2.0) - float2(1.0, 1.0), 0);
    float3 rayDirection = normalize(i.viewPos);

    quadCoord = mul(invAniTrans, quadCoord);
    rayDirection = mul(invAniTrans, rayDirection);

    // (quadCoord + rayDirection * t)^2 == R^2
    float a = dot(rayDirection, rayDirection);
    float halfB = dot(rayDirection, quadCoord);
    float c = dot(quadCoord, quadCoord) - 1;

    float t = 0;
    if (a == 0 || halfB == 0) discard;
    float delta = halfB * halfB - a * c;
    if (delta < 0) discard;
    t = (-halfB - sqrt(delta)) / a;

    float3 viewPos = i.viewPos + mul(aniTrans, rayDirection * t) * i.aniScale;
    #ifdef UNITY_PASS_SHADOWCASTER
        float4 pos = mul(UNITY_MATRIX_P, viewPos);
        #ifdef SHADER_API_GLCORE
            o.depth = (pos.z / pos.w + 1.0) * 0.5;
        #else
            o.depth = pos.z / pos.w;
        #endif
    #else
        o.depth = -viewPos.z;
    #endif

    return o;
}

#endif