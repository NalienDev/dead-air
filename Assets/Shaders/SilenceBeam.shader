Shader "Custom/SilenceBeam"
{
    // A soft, additive vertical light pillar for the dampener's "silence zone".
    // Put this on a material, drop it on a cylinder (or any tube mesh), then move and
    // scale that object freely in the scene. Everything below is tweakable per material.
    Properties
    {
        [HDR]_Color("Color", Color) = (0.4, 0.9, 1.0, 1.0)
        _Intensity("Intensity", Range(0, 8)) = 2.0
        _FresnelPower("Edge glow (fresnel)", Range(0.5, 8)) = 2.5
        _BottomFade("Bottom fade", Range(0.001, 0.6)) = 0.15
        _TopFade("Top fade", Range(0.001, 0.6)) = 0.35
        _NoiseTex("Noise", 2D) = "white" {}
        _NoiseStrength("Noise strength", Range(0, 1)) = 0.35
        _ScrollSpeed("Scroll speed (up)", Range(-4, 4)) = 1.0
        _Pulse("Pulse amount", Range(0, 1)) = 0.15
        _PulseSpeed("Pulse speed", Range(0, 10)) = 2.0
        _SoftDepth("Soft depth fade", Range(0.05, 6)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend One One   // additive
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            float4 _NoiseTex_ST;
            float4 _Color;
            float _Intensity, _FresnelPower, _BottomFade, _TopFade;
            float _NoiseStrength, _ScrollSpeed, _Pulse, _PulseSpeed, _SoftDepth;

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _NoiseTex);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Glow strongest at the silhouette so the tube reads as a soft column.
                float fresnel = pow(saturate(1.0 - abs(dot(normalize(i.normalWS), viewDir))), _FresnelPower);

                // Fade the ends. Uses the mesh's V coord (0 at bottom, 1 at top on a
                // standard cylinder).
                float vfade = smoothstep(0.0, _BottomFade, i.uv.y) *
                              smoothstep(0.0, _TopFade, 1.0 - i.uv.y);

                // Drifting noise rising up the beam.
                float2 nuv = i.uv + float2(0.0, -_ScrollSpeed * _Time.y);
                float noise = lerp(1.0, SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r, _NoiseStrength);

                // Gentle breathing pulse.
                float pulse = 1.0 + _Pulse * sin(_Time.y * _PulseSpeed);

                // Dissolve where it meets geometry instead of hard-clipping.
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float softDepth = saturate((sceneEye - i.screenPos.w) / _SoftDepth);

                float a = _Intensity * fresnel * vfade * noise * pulse * softDepth;
                return half4(_Color.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }
}
