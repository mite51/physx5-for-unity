/*
Screen space fluid rendering

Known issues:
 - receiving shadows does not work for transparent queue; attenuation is calculated directly without considering shadow
 - lightmaps will not affect a transparent object
*/

Shader "PhysX 5 for Unity/Screen Space Fluid"
{
    Properties
    {
        _Color ("Albedo", Color) = (0,0,0,1)
        _Glossiness ("Smoothness", Range(0,1)) = 1.0
        _Specular ("Specular", Color) = (0,0,0,1)
        _Fresnel ("Fresnel", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            LOD 200
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            // Transparent shader should not use lightmaps; Vertex light does not make sense
            // TODO: fog is not supported
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nofog novertexlights
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"

            #include "ScreenSpaceFluidForwardCommon.cginc"

            ENDCG
        }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardAdd" }
            Cull Off
            ZTest LEqual
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            // Transparent shader should not use lightmaps; Vertex light does not make sense
            // TODO: fog is not supported
            #pragma multi_compile_fwdadd_fullshadows nolightmap nodynlightmap nofog novertexlights
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"

            #include "ScreenSpaceFluidForwardCommon.cginc"

            ENDCG
        }

        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual
            Cull Off
            
            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma multi_compile_shadowcaster nofog

            #include "ScreenSpaceFluidDepth.cginc"

            ENDCG
        }
    }
    FallBack Off
}
