Shader "Custom/VolumetricFog"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        _StepSize("Step size", Range(0.1, 20)) = 1
        _DensityMultiplier("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset("Noise offset", float) = 0

        _FogNoise("Fog noise", 3D) = "white" {}
        _NoiseTiling("Noise tiling", float) = 1
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1

        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 1)) = 0.2

        [Header(TV Static)]
        _StaticTex("TV static", 2D) = "gray" {}
        _StaticColor("Static tint", Color) = (1, 1, 1, 1)
        _StaticStrength("Static strength", Range(0, 1)) = 1
        _StaticTiling("Static tiling", float) = 1
        _StaticSpeed("Static refresh (Hz)", Range(1, 60)) = 12
        _StaticFadeStart("Static fade start (fog amount)", Range(0, 1)) = 0.35
        _StaticFadeEnd("Static fade end (fog amount)", Range(0, 1)) = 0.85

        [Header(VHS)]
        _VHSJitter("Horizontal jitter", Range(0, 0.2)) = 0.02
        _VHSBandCount("Glitch band count", float) = 30
        _VHSSpeed("Glitch speed", float) = 12
        _VHSScanline("Scanline strength", Range(0, 1)) = 0.25
        _VHSChroma("Chromatic aberration (px)", Range(0, 10)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color;
            float _MaxDistance;
            float _DensityMultiplier;
            float _StepSize;
            float _NoiseOffset;
            TEXTURE3D(_FogNoise);
            float _DensityThreshold;
            float _NoiseTiling;
            float4 _LightContribution;
            float _LightScattering;

            // TV static / VHS
            TEXTURE2D(_StaticTex); SAMPLER(sampler_StaticTex);
            float4 _StaticColor;
            float _StaticStrength, _StaticTiling, _StaticFadeStart, _StaticFadeEnd, _StaticSpeed;
            float _VHSJitter, _VHSBandCount, _VHSSpeed, _VHSScanline, _VHSChroma;

            // Clearing volume — driven as globals by FogClearingZone.cs
            float4x4 _FogClearMatrix;   // world -> zone local space
            float _FogClearEnabled;     // 0 / 1
            float _FogClearShape;       // 0 = sphere, 1 = box
            float _FogClearEdge;        // shell thickness (local units, ~0.05-0.2)
            float _FogClearEdgeDensity; // how solid the boundary wall is
            float4 _FogClearEdgeColor;  // boundary glow (HDR)

            float henyey_greenstein(float angle, float scattering)
            {
                return (1.0 - angle * angle) / (4.0 * PI * pow(1.0 + scattering * scattering - (2.0 * scattering) * angle, 1.5f));
            }

            float get_density(float3 worldPos)
            {
                float4 noise = _FogNoise.SampleLevel(sampler_TrilinearRepeat, worldPos * 0.01 * _NoiseTiling, 0);
                float density = dot(noise, noise);
                density = saturate(density - _DensityThreshold) * _DensityMultiplier;
                return density;
            }

            float sd_unit_box(float3 p)
            {
                float3 q = abs(p) - 0.5;
                return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
            }

            // insideAmount: 1 inside the clearing (kills fog). edge: 0..1 shell near the surface.
            void sample_clearing(float3 worldPos, out float inside, out float edge)
            {
                inside = 0; edge = 0;
                if (_FogClearEnabled < 0.5) return;

                float3 lp = mul(_FogClearMatrix, float4(worldPos, 1.0)).xyz;
                float sd = (_FogClearShape < 0.5) ? (length(lp) - 0.5) : sd_unit_box(lp);

                inside = (sd < 0.0) ? 1.0 : 0.0;
                edge = 1.0 - saturate(abs(sd) / max(_FogClearEdge, 1e-4));
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float3 sample_tv_static(float2 uv)
            {
                float2 res = _BlitTexture_TexelSize.zw;
                float frame = floor(_Time.y * _StaticSpeed);

                // VHS horizontal tearing: rows jump sideways, changing per frame
                float band = floor(uv.y * _VHSBandCount);
                float jitter = (hash21(float2(band, floor(_Time.y * _VHSSpeed))) - 0.5) * _VHSJitter;
                float2 suv = uv + float2(jitter, 0);

                // Boiling static: grain texture with a per-frame random offset
                float2 grainUV = suv * _StaticTiling + float2(hash21(float2(frame, 1)), hash21(float2(2, frame)));
                float ca = _VHSChroma / max(res.x, 1);
                float r = SAMPLE_TEXTURE2D(_StaticTex, sampler_StaticTex, grainUV + float2(ca, 0)).r;
                float g = SAMPLE_TEXTURE2D(_StaticTex, sampler_StaticTex, grainUV).r;
                float b = SAMPLE_TEXTURE2D(_StaticTex, sampler_StaticTex, grainUV - float2(ca, 0)).r;
                float3 col = float3(r, g, b);

                // Scanlines
                col *= 1.0 - _VHSScanline * (0.5 + 0.5 * sin(uv.y * res.y * PI));

                return col * _StaticColor.rgb;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                float depth = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir = worldPos - _WorldSpaceCameraPos;
                float viewLength = length(viewDir);
                float3 rayDir = normalize(viewDir);

                float2 pixelCoords = IN.texcoord * _BlitTexture_TexelSize.zw;
                float distLimit = min(viewLength, _MaxDistance);
                float distTravelled = InterleavedGradientNoise(pixelCoords, (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x))) * _NoiseOffset;
                float transmittance = 1;
                float4 fogCol = _Color;
                float3 edgeAccum = 0;

                while(distTravelled < distLimit)
                {
                    float3 rayPos = entryPoint + rayDir * distTravelled;
                    float density = get_density(rayPos);

                    float clearInside, clearEdge;
                    sample_clearing(rayPos, clearInside, clearEdge);
                    density *= (1.0 - clearInside);            // hollow out the bubble
                    float wallDensity = clearEdge * _FogClearEdgeDensity;

                    if (density > 0 || wallDensity > 0)
                    {
                        Light mainLight = GetMainLight(TransformWorldToShadowCoord(rayPos));
                        fogCol.rgb += mainLight.color.rgb * _LightContribution.rgb * henyey_greenstein(dot(rayDir, mainLight.direction), _LightScattering) * density * mainLight.shadowAttenuation * _StepSize;
                        edgeAccum += _FogClearEdgeColor.rgb * clearEdge * _StepSize;
                        transmittance *= exp(-(density + wallDensity) * _StepSize);
                    }
                    distTravelled += _StepSize;
                }

                float fogAmount = 1.0 - saturate(transmittance);

                // Thick fog (distance / sky) turns into VHS static.
                float staticMask = smoothstep(_StaticFadeStart, _StaticFadeEnd, fogAmount) * _StaticStrength;
                if (staticMask > 0.001)
                    fogCol.rgb = lerp(fogCol.rgb, sample_tv_static(IN.texcoord), staticMask);

                fogCol.rgb += edgeAccum;   // clearing boundary sits on top, un-staticked

                return lerp(col, fogCol, fogAmount);
            }
            ENDHLSL
        }
    }
}
