Shader "Custom/FlashlightBeam"
{
    Properties
    {
        [HDR]_Color("Color", Color) = (1, 0.95, 0.8, 1)
        _Intensity("Intensity", Range(0, 8)) = 1.5
        _RimPower("Edge softness", Range(0.5, 8)) = 3
        _LengthFalloff("Length falloff", Range(0.2, 6)) = 2
        _TipSoftness("Tip fade", Range(0.001, 0.5)) = 0.08
        _SoftDepth("Soft depth fade", Range(0.05, 6)) = 1.5
        _SideVisibility("Side visibility", Range(0, 1)) = 0.2
        _AxisSharpness("Axis sharpness", Range(0.5, 8)) = 2
        _NoiseTex("Dust noise", 2D) = "white" {}
        _NoiseStrength("Dust strength", Range(0, 1)) = 0.35
        _NoiseScroll("Dust scroll (xy)", Vector) = (0.05, 0.15, 0, 0)
        _ConeLength("Cone length (set by script)", Float) = 20
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
                float  coneT      : TEXCOORD3;   // 0 at apex, 1 at far end
                float4 screenPos  : TEXCOORD4;
            };

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            float4 _Color, _NoiseScroll;
            float _Intensity, _RimPower, _LengthFalloff, _TipSoftness, _SoftDepth, _NoiseStrength, _ConeLength;
            float _SideVisibility, _AxisSharpness;

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.coneT = saturate(v.positionOS.z / max(_ConeLength, 1e-3));
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Soft translucent shell — fades toward the silhouette.
                float rim = pow(saturate(1.0 - abs(dot(normalize(i.normalWS), viewDir))), _RimPower);

                // Bright at the source, fading down the beam; fade in from the tip.
                float lengthFade = pow(1.0 - i.coneT, _LengthFalloff);
                float tipFade = smoothstep(0.0, _TipSoftness, i.coneT);

                // Soft-depth: dissolve where the beam meets geometry instead of hard-clipping.
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float softDepth = saturate((sceneEye - i.screenPos.w) / _SoftDepth);

                // Dim the beam when viewed across its axis — a shell viewed broadside
                // otherwise reads as a solid bright cone. Full brightness looking along it.
                float3 beamDir = normalize(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
                float axis = lerp(_SideVisibility, 1.0, pow(abs(dot(viewDir, beamDir)), _AxisSharpness));

                // Drifting dust.
                float2 nuv = i.uv + _NoiseScroll.xy * _Time.y;
                float dust = lerp(1.0, SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r, _NoiseStrength);

                float a = _Intensity * rim * lengthFade * tipFade * softDepth * dust * axis;
                return half4(_Color.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }
}
