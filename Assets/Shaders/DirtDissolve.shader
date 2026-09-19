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
        [Normal] _DirtNormalMap("Dirt Normal Map", 2D) = "bump" {}
        _DirtTextureScale("Dirt Texture Scale", Range(0.05, 4)) = 0.6
        _DirtNormalStrength("Dirt Relief", Range(0, 2)) = 0.8
        _DirtLightingInfluence("Relief Lighting", Range(0, 1)) = 0.75
        _DirtPatternStrength("Visible Pattern", Range(0, 1)) = 0.65
        _DirtWetness("Wet Shine", Range(0, 1)) = 0.45
        [HideInInspector] _CoverageMask("Coverage", 2D) = "black" {}
        [HideInInspector] _UseCoverageMask("Use Coverage", Float) = 0
        [HideInInspector] _CoverageWorldSpace("World Space Coverage", Float) = 0
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

            Cull Off
            ZWrite On
            Blend One Zero
            AlphaToMask Off

            HLSLPROGRAM
            #pragma target 4.5
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
                float3 positionWS : TEXCOORD2;
            };

            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CoverageMask);
            SAMPLER(sampler_CoverageMask);
            TEXTURE2D(_DirtNormalMap);
            SAMPLER(sampler_DirtNormalMap);
            float _UseCoverageMask;
            float _CoverageWorldSpace;
            float4 _CoverageMask_TexelSize;
            float4 _CoverageU, _CoverageV, _CoverageBounds;
            float4x4 _CoverageWorldToLocal;

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
                float _DirtTextureScale;
                float _DirtNormalStrength;
                float _DirtLightingInfluence;
                float _DirtPatternStrength;
                float _DirtWetness;
            CBUFFER_END

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

            half3 ApplyDirtSurfaceDetail(half3 baseColor, float2 dirtUV)
            {
                half3 detailNormal = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_DirtNormalMap, sampler_DirtNormalMap, dirtUV),
                    _DirtNormalStrength);
                half3 stylizedLightDirection = normalize(half3(-0.35h, 0.4h, 0.85h));
                half diffuse = saturate(dot(detailNormal, stylizedLightDirection));
                half reliefLighting = lerp(
                    1.0h,
                    0.68h + diffuse * 0.5h,
                    _DirtLightingInfluence);
                half pattern = saturate(
                    0.5h + detailNormal.x * 0.42h + detailNormal.y * 0.28h);
                half patternLighting = lerp(0.68h, 1.3h, pattern);
                half3 shineDirection = normalize(half3(0.25h, -0.2h, 0.95h));
                half wetHighlight = pow(
                    saturate(dot(detailNormal, shineDirection)),
                    18.0h) * _DirtWetness;

                return baseColor * reliefLighting *
                    lerp(1.0h, patternLighting, _DirtPatternStrength) +
                    half3(wetHighlight, wetHighlight, wetHighlight);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                
                output.uv = TRANSFORM_TEX(input.uv, _MainTex); 
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_UseCoverageMask > 0.5)
                {
                    float2 localSurfacePosition = float2(dot(input.positionOS, _CoverageU.xyz), dot(input.positionOS, _CoverageV.xyz));
                    float2 sharedSurfacePosition = mul(_CoverageWorldToLocal, float4(input.positionWS, 1)).xz;
                    float2 surfacePosition = lerp(localSurfacePosition, sharedSurfacePosition, saturate(_CoverageWorldSpace));
                    float2 maskUV = (surfacePosition - _CoverageBounds.xy) / _CoverageBounds.zw;
                    float coverage = SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, maskUV).r;
                    // The same binary texels count toward completion on the CPU.
                    clip(0.5 - max(coverage, _DissolveAmount));
                    float2 offset = _CoverageMask_TexelSize.xy * max(1.0, _EdgeWidth * 32.0);
                    float border = SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, maskUV + float2(offset.x, 0)).r;
                    border += SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, maskUV - float2(offset.x, 0)).r;
                    border += SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, maskUV + float2(0, offset.y)).r;
                    border += SAMPLE_TEXTURE2D(_CoverageMask, sampler_CoverageMask, maskUV - float2(0, offset.y)).r;
                    float edge = saturate(border) * lerp(0.6, 1.0, valueNoise(input.positionOS * _NoiseScale));
                    float2 dirtUV = input.positionWS.xz * _DirtTextureScale;
                    half3 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb *
                        _BaseColor.rgb;
                    half3 finalColor = ApplyDirtSurfaceDetail(baseColor, dirtUV) +
                        _EdgeColor.rgb * edge * _EdgeGlow;
                    return half4(finalColor, 1);
                }
                float localDissolve = _DissolveAmount;

                float noise = valueNoise(input.positionOS * _NoiseScale);
                clip(noise - localDissolve);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                half3 finalBaseColor = ApplyDirtSurfaceDetail(
                    texColor.rgb * _BaseColor.rgb,
                    input.positionWS.xz * _DirtTextureScale);

                float edgeMask = 1.0 - smoothstep(localDissolve, localDissolve + _EdgeWidth, noise);
                
                float3 color = finalBaseColor + (_EdgeColor.rgb * edgeMask * _EdgeGlow);
                
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
