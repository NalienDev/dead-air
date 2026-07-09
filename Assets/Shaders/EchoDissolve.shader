Shader "Custom/EchoDissolve"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _DissolveThreshold("Dissolve", Range(0, 1)) = 0
        _NoiseScale("Noise scale", Range(0.5, 64)) = 14
        _EdgeWidth("Edge width", Range(0.005, 0.3)) = 0.06
        [HDR]_EdgeColor("Edge color", Color) = (0.5, 2.2, 2.6, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EdgeColor;
            float _DissolveThreshold;
            float _NoiseScale;
            float _EdgeWidth;
        CBUFFER_END

        // Procedural value noise — no texture to assign, and object-space
        // sampling keeps the pattern glued to the mesh while it moves.
        float Hash3(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            return lerp(
                lerp(lerp(Hash3(i),                    Hash3(i + float3(1, 0, 0)), f.x),
                     lerp(Hash3(i + float3(0, 1, 0)),  Hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash3(i + float3(0, 0, 1)),  Hash3(i + float3(1, 0, 1)), f.x),
                     lerp(Hash3(i + float3(0, 1, 1)),  Hash3(i + float3(1, 1, 1)), f.x), f.y),
                f.z);
        }

        // Signed distance to the dissolve front; negative = burned away.
        // The cutoff overshoots [0,1] slightly so threshold 0/1 is fully
        // solid/fully gone even where the noise never reaches the extremes.
        float DissolveDistance(float3 positionOS)
        {
            float3 p = positionOS * _NoiseScale;
            float noise = ValueNoise(p) * 0.65 + ValueNoise(p * 2.63) * 0.35;
            float cutoff = lerp(-0.05, 1.05, _DissolveThreshold);
            return noise - cutoff;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = DissolveDistance(i.positionOS);
                clip(d);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 normalWS = normalize(i.normalWS);

                // The light-loop macros read from an InputData called exactly
                // this in Forward+/clustered mode.
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(i.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // Ambient + main light.
                half3 color = albedo * SampleSH(normalWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                color += albedo * mainLight.color * mainLight.shadowAttenuation
                         * saturate(dot(normalWS, mainLight.direction));

                // Point/spot lights — this is what the flashlight beam hits.
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    color += albedo * light.color * light.distanceAttenuation
                             * light.shadowAttenuation * saturate(dot(normalWS, light.direction));
                LIGHT_LOOP_END

                // Burning front — suppressed until the dissolve actually starts,
                // otherwise low-noise patches glow on a fully solid model.
                float edge = (1.0 - smoothstep(0.0, _EdgeWidth, d)) * saturate(_DissolveThreshold * 12.0);
                color += _EdgeColor.rgb * edge;

                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings  { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o;
                o.positionOS = v.positionOS.xyz;

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                clip(DissolveDistance(i.positionOS));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings DepthVert(Attributes v)
            {
                Varyings o;
                o.positionOS = v.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(Varyings i) : SV_Target
            {
                clip(DissolveDistance(i.positionOS));
                return 0;
            }
            ENDHLSL
        }
    }
}
