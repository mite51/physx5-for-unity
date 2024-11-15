Shader "PhysX 5 for Unity/ScreenSpaceFluidProcessing"
{
    Properties
    {
        _Color ("Albedo", Color) = (0,0,0,1)
        _Glossiness ("Smoothness", Range(0,1)) = 1.0
        _Specular ("Specular", Color) = (0,0,0,1)
        _Fresnel ("Fresnel", Range(0,1)) = 1.0
        _WorldFilterSize ("World Filter Size", Range(0, 5)) = 3
        _ThresholdRatio ("Threshold Ratio", Range(0, 15)) = 1
        _ClampRatio ("Clamp Ratio", Range(0, 5)) = 1
    }
    SubShader
    {
        Pass
        {
            Name "FluidDepth"

            Cull Off
            ZTest Always
            Blend One One
            BlendOp Min

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "ScreenSpaceFluidDepth.cginc"

            ENDCG
        }

        Pass
        {
            Name "FluidColor"

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            uint _IndexCount;
            StructuredBuffer<uint> _Indices;
            StructuredBuffer<float4> _Points;
            StructuredBuffer<float4> _Colors;
            float _ParticleDiameter;
            float4x4 _ViewMatrix;
            float4x4 _ProjMatrix;
            sampler2D _CameraDepthTexture;
            sampler2D _DepthTex;
            float2 _ScreenSize;
            float FLIP_Y = 0;

            struct v2g {};

            struct g2f
            {
                float4 color : SV_Target;
                float4 pos : SV_Position;
                float3 vpos : VIEWPOS;
                float2 tex0 : TEXCOORD0;
            };

            struct f2o
            {
                float4 color : SV_Target;
            };

            v2g vert(uint id : SV_VertexId)
            {
                return (v2g)0;
            }

            [maxvertexcount(4)]
            void geom(point v2g i[1], uint id : SV_PrimitiveId, inout TriangleStream<g2f> _stream)
            {
                if (id >= _IndexCount) return;
                uint index = _Indices[id];
                float3 mpos = _Points[index].xyz;
                float4 color = _Colors[index];

                half scale = 0.15;

                float4x4 viewMatrix = _ViewMatrix;
                float3 pos = mul(viewMatrix, float4(mpos, 1));
                
                float4 clipPos = mul(_ProjMatrix, float4(pos, 1));
                float2 uv = clipPos.xy / clipPos.w * 0.5 + 0.5;
                float depth = clipPos.z / clipPos.w;
                float sceneDepth = (tex2Dlod(_CameraDepthTexture, float4(uv.x, FLIP_Y ? 1 - uv.y : uv.y, 0, 0)).r);

                if (depth >= sceneDepth - 0.15)
                {
                    float3 up = float3(0, 1, 0), right = float3(1, 0, 0);
                    float3 vpos[4] = { pos + (right - up) * scale, pos + (right + up) * scale,
                                        pos + (-right - up) * scale, pos + (-right + up) * scale };
                    float2 tex0[4] = { float2(1, 0), float2(1, 1),
                                        float2(0, 0), float2(0, 1) };
    
                    g2f o = (g2f)0;
    
                    for (int i = 0; i < 4; ++i)
                    {
                        o.pos = mul(_ProjMatrix, float4(vpos[i], 1));
                        o.vpos = vpos[i];
                        o.color = color;
                        o.tex0 = tex0[i];
                        _stream.Append(o);
                    }
                }
            }

            f2o frag(g2f i)
            {
                float2 uv = i.pos.xy / _ScreenSize.xy;           
                float depth = -i.vpos.z;
                float fluidDepth = tex2D(_DepthTex, float2(uv.x, uv.y)).x;
                
                if (fluidDepth - _ParticleDiameter > depth) discard;

                fixed2 distVec = i.tex0 - fixed2(0.5, 0.5);
                fixed dist = dot(distVec, distVec);

                f2o o = (f2o)0;
                if (dist > 0.25) discard;
                o.color.rgb = i.color.rgb;
                o.color.a = i.color.a;
                
                return o;
            }

            ENDCG
        }

        // -----------------------------
        //  Brute force bilateral filter
        // -----------------------------
        Pass
        {
            Name "FluidDepthBilateralFilter"

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct i2v
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct f2o
            {
                float depth : SV_Target;
            };

            float2 _InvScreen;
            float _FarPlane;
            sampler2D _DepthTex;
            float _WorldFilterSize;
            float _ParticleRadius;
            int _MaxFilterSize;
            float _FOVRatio;
            int _FixedFilterSize;

            v2f vert(i2v i)
            {
                v2f o = (v2f)0;
                o.pos = UnityObjectToClipPos(i.pos);
                o.uv = i.uv;
                return o;
            }

            f2o frag(v2f i)
            {
                f2o o = (f2o)0;

                float depth = tex2D(_DepthTex, i.uv).x;
                if (depth == _FarPlane) discard;

                float blurScale = _FOVRatio * _ParticleRadius * 0.1f;

                float K = _WorldFilterSize * blurScale;
                float filterHalfSize = (_FixedFilterSize > 0) ? _FixedFilterSize : min(_MaxFilterSize, int(ceil(K / depth)));
                float2 pixelStep = _InvScreen;

                float sum = 0.0;
                float wsum = 0.0;

                for (float x = -filterHalfSize; x < filterHalfSize; x += 1)
                {
                    for (float y = -filterHalfSize; y < filterHalfSize; y += 1)
                    {
                        float4 uv = float4(i.uv + float2(x, y) * pixelStep, 0, 0);
                        float d1 = tex2Dlod(_DepthTex, uv).x;

                        float r1 = length(float2(x, y)) / (0.5f * filterHalfSize); // Spatial
                        float r2 = abs(d1 - depth) / 0.2f; // Range
                        float w = exp(-r1 * r1 - r2 * r2);

                        if (depth < _FarPlane && d1 < _FarPlane)
                        {
                            sum += d1 * w;
                            wsum += w;
                        }
                    }
                }

                if (wsum > 0.0) o.depth = sum / wsum;
                return o;
            }

            ENDCG
        }

        // -----------------------------
        //  Approx. bilateral filter
        // -----------------------------
        Pass
        {
            Name "FluidDepthBilateralFilterApprox"

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct i2v
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct f2o
            {
                float depth : SV_Target;
            };

            float2 _InvScreen;
            float _FarPlane;
            sampler2D _DepthTex;
            float _WorldFilterSize;
            float _ParticleRadius;
            int _MaxFilterSize;
            float _FOVRatio;
            int _FixedFilterSize;

            v2f vert(i2v i)
            {
                v2f o = (v2f)0;
                o.pos = UnityObjectToClipPos(i.pos);
                o.uv = i.uv;
                return o;
            }

            f2o frag(v2f i)
            {
                f2o o = (f2o)0;

                float depth = tex2D(_DepthTex, i.uv).x;
                if (depth == _FarPlane) discard;

                float blurScale = _FOVRatio * _ParticleRadius * 0.1f;

                float K = _WorldFilterSize * blurScale;
                float filterHalfSize = (_FixedFilterSize > 0) ? _FixedFilterSize : min(_MaxFilterSize, int(ceil(K / depth)));
                float2 pixelStep = _InvScreen;

                float sum = 0.0;
                float wsum = 0.0;
                float count = 0.0;

                for (float x = -filterHalfSize; x < filterHalfSize; x += 1)
                {
                    for (float y = -filterHalfSize; y < filterHalfSize; y += 1)
                    {
                        float4 uv = float4(i.uv + float2(x, y) * pixelStep, 0, 0);
                        float d1 = tex2Dlod(_DepthTex, uv).x;

                        float r1 = length(float2(x, y)) / (0.5f * filterHalfSize);
                        float w = exp(-r1 * r1);

                        if (depth < _FarPlane && d1 < _FarPlane && abs(d1 - depth) < 0.3)
                        {
                            sum += d1 * w;
                            wsum += w;
                            count += 1;
                        }
                    }
                }

                if (wsum > 0.0) sum /= wsum;

                float kernelSize = 2.0 * filterHalfSize + 1.0;
                float blend = count / (kernelSize * kernelSize);
                o.depth = lerp(depth, sum, blend);

                return o;
            }

            ENDCG
        }

        Pass
        {
            Name "FluidDepthNarrowRangeFilter2D"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define FIX_OTHER_WEIGHT
            #define RANGE_EXTENSION
            #define PI_OVER_8 0.392699082f

            struct i2v
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct f2o
            {
                float depth : SV_Target;
            };

            float2 _InvScreen;
            float _FarPlane;
            sampler2D _DepthTex;
            float _WorldFilterSize;
            float _ParticleRadius;
            int _MaxFilterSize;
            float _ThresholdRatio;
            float _ClampRatio;
            float _FOVRatio;
            int _FixedFilterSize;

            void ModifiedGaussianFilter2D(inout float sampleDepth, inout float weight, inout float weight_other, inout float upper, inout float lower, float lower_clamp, float threshold)
            {
                if(sampleDepth > upper)
                {
                    weight = 0;
            #ifdef FIX_OTHER_WEIGHT
                    weight_other = 0;
            #endif
                }
                else
                {
                    if(sampleDepth < lower)
                    {
                        sampleDepth = lower_clamp;
                    }
            #ifdef RANGE_EXTENSION
                    else 
                    {
                        upper = max(upper, sampleDepth + threshold);
                        lower = min(lower, sampleDepth - threshold);
                    }
            #endif
                }
            }

            float ComputeWeight2D(float2 r, float two_sigma2)
            {
                return exp(-dot(r, r) / two_sigma2);
            }

            float Filter2D(float pixelDepth, float2 uv)
            {
                if(_WorldFilterSize == 0) {
                    return pixelDepth;
                }
            
                float2 pixelStep = _InvScreen;
                float threshold = _ParticleRadius * _ThresholdRatio;
                float K = -_WorldFilterSize * _FOVRatio * _ParticleRadius * 0.1f;
                int filterHalfSize = (_FixedFilterSize > 0) ? _FixedFilterSize : min(_MaxFilterSize, int(ceil(K / pixelDepth)));
            
                float upper = pixelDepth + threshold;
                float lower = pixelDepth - threshold;
                float lower_clamp = pixelDepth - _ParticleRadius * _ClampRatio;
            
                float sigma = filterHalfSize / 3.0f;
                float two_sigma2 = 2.0f * sigma * sigma;
            
                float4 f_tex = float4(uv.xy, uv.xy);
            
                float2 r = float2(0, 0);
                float4 sum4 = float4(pixelDepth, 0, 0, 0);
                float4 wsum4 = float4(1, 0, 0, 0);
                float4 sampleDepth;
                float4 w4;
            
                for (int x = 1; x <= filterHalfSize; x++)
                {
                    r.x += pixelStep.x;
                    r.y = 0;
                    f_tex.x += pixelStep.x;
                    f_tex.z -= pixelStep.x;
                    float4 f_tex1 = f_tex.xyxy;
                    float4 f_tex2 = f_tex.zwzw;
            
                    for (int y = 1; y <= filterHalfSize; y++)
                    {
                        f_tex1.y += pixelStep.y;
                        f_tex1.w -= pixelStep.y;
                        f_tex2.y += pixelStep.y;
                        f_tex2.w -= pixelStep.y;

                        float4 uv1 = float4(f_tex1.xy, 0, 0);
                        float4 uv2 = float4(f_tex1.zw, 0, 0);
                        float4 uv3 = float4(f_tex2.xy, 0, 0);
                        float4 uv4 = float4(f_tex2.zw, 0, 0);
                        sampleDepth.x = -tex2Dlod(_DepthTex, uv1).x;
                        sampleDepth.y = -tex2Dlod(_DepthTex, uv2).x;
                        sampleDepth.z = -tex2Dlod(_DepthTex, uv3).x;
                        sampleDepth.w = -tex2Dlod(_DepthTex, uv4).x;
            
                        r.y += pixelStep.y;
                        float w = ComputeWeight2D(pixelStep * r, two_sigma2);
                        w4 = float4(w, w, w, w);
            
                        ModifiedGaussianFilter2D(sampleDepth.x, w4.x, w4.w, upper, lower, lower_clamp, threshold);
                        ModifiedGaussianFilter2D(sampleDepth.y, w4.y, w4.z, upper, lower, lower_clamp, threshold);
                        ModifiedGaussianFilter2D(sampleDepth.z, w4.z, w4.y, upper, lower, lower_clamp, threshold);
                        ModifiedGaussianFilter2D(sampleDepth.w, w4.w, w4.x, upper, lower, lower_clamp, threshold);
            
                        sum4 += sampleDepth * w4;
                        wsum4 += w4;
                    }
                }
            
                float2 filterVal;
                filterVal.x = dot(sum4, float4(1, 1, 1, 1));
                filterVal.y = dot(wsum4, float4(1, 1, 1, 1));
                return filterVal.x / filterVal.y;
            }
            
            v2f vert(i2v i)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o)
                o.pos = UnityObjectToClipPos(i.pos);
                o.uv = i.uv;
                return o;
            }
            
            f2o frag(v2f i)
            {
                float depth = -tex2D(_DepthTex, i.uv).x;
                if (-depth == _FarPlane) discard;
                f2o o;
                UNITY_INITIALIZE_OUTPUT(f2o, o)
                float d = -Filter2D(depth, i.uv);
                o.depth = d;
                return o;
            }

            ENDCG
        }

        Pass
        {
            Name "FluidDepthNarrowRangeFilter1D"

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend Off

            CGPROGRAM

            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define FIX_OTHER_WEIGHT
            #define RANGE_EXTENSION
            #define PI_OVER_8 0.392699082f

            struct i2v
            {
                float4 pos : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct f2o
            {
                float depth : SV_Target;
            };

            float2 _InvScreen;
            float _FarPlane;
            sampler2D _DepthTex;
            float _WorldFilterSize;
            float _ParticleRadius;
            int _MaxFilterSize;
            float _ThresholdRatio;
            float _ClampRatio;
            float _FOVRatio;
            float _FilterDirection;

            void ModifiedGaussianFilter1D(inout float sampleDepth, inout float weight, inout float weight_other, inout float upper, inout float lower, float lower_clamp, float threshold)
            {
                if(sampleDepth > upper) {
                    weight = 0;
            #ifdef FIX_OTHER_WEIGHT
                    weight_other = 0;
            #endif
                } else {
                    if(sampleDepth < lower) {
                        sampleDepth = lower_clamp;
                    }
            #ifdef RANGE_EXTENSION
                    else {
                        upper = max(upper, sampleDepth + threshold);
                        lower = min(lower, sampleDepth - threshold);
                    }
            #endif
                }
            }

            float ComputeWeight1D(float r, float two_sigma2)
            {
                return exp(-r * r / two_sigma2);
            }
            
            float Filter1D(float pixelDepth, float2 uv)
            {
                if (_WorldFilterSize == 0) {
                    return pixelDepth;
                }

                float2 pixelStep = _InvScreen;
                float threshold = _ParticleRadius * _ThresholdRatio;
                float K = -_WorldFilterSize * _FOVRatio * _ParticleRadius * 0.1;
                int filterHalfSize = min(_MaxFilterSize, int(ceil(K / pixelDepth)));

                float upper = pixelDepth + threshold;
                float lower = pixelDepth - threshold;
                float lower_clamp = pixelDepth - _ParticleRadius * _ClampRatio;

                float sigma = filterHalfSize / 3.0;
                float two_sigma2 = 2.0 * sigma * sigma;

                float2 sum2 = float2(pixelDepth, 0);
                float2 wsum2 = float2(1, 0);
                float4 direction = (_FilterDirection == 0) ? float4(pixelStep.x, 0, -pixelStep.x, 0) : float4(0, pixelStep.y, 0, -pixelStep.y);

                float4 f_tex = uv.xyxy;
                float r = 0;
                float dr = direction.x + direction.y;

                float upper1 = upper;
                float upper2 = upper;
                float lower1 = lower;
                float lower2 = lower;
                float2 sampleDepth;
                float w;
                float2 w2;

                for (int x = 1; x <= filterHalfSize; ++x) {
                    f_tex += direction;
                    r += dr;

                    float4 uv1 = float4(f_tex.xy, 0, 0);
                    float4 uv2 = float4(f_tex.zw, 0, 0);
                    sampleDepth.x = -tex2Dlod(_DepthTex, uv1).x;
                    sampleDepth.y = -tex2Dlod(_DepthTex, uv2).x;

                    w = ComputeWeight1D(r, two_sigma2);
                    w2 = float2(w, w);
                    ModifiedGaussianFilter1D(sampleDepth.x, w2.x, w2.y, upper1, lower1, lower_clamp, threshold);
                    ModifiedGaussianFilter1D(sampleDepth.y, w2.y, w2.x, upper2, lower2, lower_clamp, threshold);

                    sum2 += sampleDepth * w2;
                    wsum2 += w2;
                }

                float2 filterVal = float2(sum2.x, wsum2.x) + float2(sum2.y, wsum2.y);
                return filterVal.x / filterVal.y;
            }

            v2f vert(i2v i)
            {
                v2f o;
                UNITY_INITIALIZE_OUTPUT(v2f, o)
                o.pos = UnityObjectToClipPos(i.pos);
                o.uv = i.uv;
                return o;
            }
            
            f2o frag(v2f i)
            {
                float depth = -tex2D(_DepthTex, i.uv).x;
                if (-depth == _FarPlane) discard;
                f2o o;
                UNITY_INITIALIZE_OUTPUT(f2o, o)
                float d = -Filter1D(depth, i.uv);
                o.depth = d;
                return o;
            }

            ENDCG
        }

    }
}
