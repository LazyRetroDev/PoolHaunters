Shader "PoolHaunters/DirtDissolve"
{
    Properties
    {
        [MainTexture] _MainTex("Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.03, 0.03, 0.03, 1)
        _EdgeColor("Edge Color", Color) = (0.45, 0.8, 1, 1)
        _DissolveAmount("Dissolve Amount", Range(0, 1)) = 0
        _EdgeWidth("Edge Width", Range(0.001, 0.25)) = 0.08
        _EdgeGlow("Edge Glow", Range(0, 4)) = 0.6
        _NoiseScale("Noise Scale", Range(0.5, 20)) = 7
        _BrushSoftness("Brush Softness", Range(0.01, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            Blend One Zero
            AlphaToMask Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; 
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float2 uv : TEXCOORD1; 
            };

            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; 
                float4 _BaseColor;
                float4 _EdgeColor;
                float _DissolveAmount;
                float _EdgeWidth;
                float _EdgeGlow;
                float _NoiseScale;
                float _BrushSoftness;
                float _CleanPointCount;
            CBUFFER_END

            #define MAX_CLEAN_POINTS 512
            float4 _CleanPoints[MAX_CLEAN_POINTS];

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash31(i + float3(0, 0, 0));
                float n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0));
                float n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1));
                float n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1));
                float n111 = hash31(i + float3(1, 1, 1));

                float n00 = lerp(n000, n100, f.x);
                float n10 = lerp(n010, n110, f.x);
                float n01 = lerp(n001, n101, f.x);
                float n11 = lerp(n011, n111, f.x);
                float n0 = lerp(n00, n10, f.y);
                float n1 = lerp(n01, n11, f.y);
                return lerp(n0, n1, f.z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                
                output.uv = TRANSFORM_TEX(input.uv, _MainTex); 
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float localDissolve = _DissolveAmount;

                [unroll]
                for (int i = 0; i < MAX_CLEAN_POINTS; i++)
                {
                    if (i >= (int)_CleanPointCount)
                        break;

                    float3 cleanPoint = _CleanPoints[i].xyz;
                    float cleanRadius = _CleanPoints[i].w;
                    float distanceToBrush = distance(input.positionOS, cleanPoint);
                    float innerRadius = cleanRadius * saturate(1.0 - _BrushSoftness);
                    float brushMask = 1.0 - smoothstep(innerRadius, cleanRadius, distanceToBrush);
                    localDissolve = max(localDissolve, brushMask);
                }

                float noise = valueNoise(input.positionOS * _NoiseScale);
                clip(noise - localDissolve);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half3 finalBaseColor = texColor.rgb * _BaseColor.rgb;

                float edgeMask = 1.0 - smoothstep(localDissolve, localDissolve + _EdgeWidth, noise);
                
                float3 color = finalBaseColor + (_EdgeColor.rgb * edgeMask * _EdgeGlow);
                
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}