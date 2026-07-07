Shader "Custom/Outline"
{
    Properties
    {
        _OutlineColor("Outline color", Color) = (0, 0, 0, 1)
        _Thickness("Thickness (px)", Range(0.5, 8)) = 1
        _DepthThreshold("Depth sensitivity", Range(0.001, 0.5)) = 0.03
        _NormalThreshold("Normal sensitivity", Range(0.05, 2)) = 0.4
        _GrazingBias("Grazing angle bias", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float _Thickness, _DepthThreshold, _NormalThreshold, _GrazingBias;

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 o = _BlitTexture_TexelSize.xy * _Thickness;

                // Roberts cross — 4 diagonal neighbours.
                float2 uvBL = uv + float2(-o.x, -o.y);
                float2 uvTR = uv + float2( o.x,  o.y);
                float2 uvTL = uv + float2(-o.x,  o.y);
                float2 uvBR = uv + float2( o.x, -o.y);

                // ── Depth edges ──────────────────────────────────────────────
                float dC  = LinearEyeDepth(SampleSceneDepth(uv),   _ZBufferParams);
                float dBL = LinearEyeDepth(SampleSceneDepth(uvBL), _ZBufferParams);
                float dTR = LinearEyeDepth(SampleSceneDepth(uvTR), _ZBufferParams);
                float dTL = LinearEyeDepth(SampleSceneDepth(uvTL), _ZBufferParams);
                float dBR = LinearEyeDepth(SampleSceneDepth(uvBR), _ZBufferParams);

                float depthDiff = sqrt((dTR - dBL) * (dTR - dBL) + (dBR - dTL) * (dBR - dTL));

                // Threshold relative to distance so it's stable near & far, and
                // loosened on surfaces seen at a grazing angle (steep depth slope)
                // so floors/walls don't outline all over.
                float3 nC = SampleSceneNormals(uv);
                float3 viewDir = normalize(GetWorldSpaceViewDir(ComputeWorldSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_VP)));
                float grazing = 1.0 - saturate(abs(dot(nC, viewDir)));
                float depthThresh = _DepthThreshold * dC * (1.0 + grazing * grazing * 25.0 * _GrazingBias);
                float depthEdge = step(depthThresh, depthDiff);

                // ── Normal edges (creases) ───────────────────────────────────
                float3 nBL = SampleSceneNormals(uvBL);
                float3 nTR = SampleSceneNormals(uvTR);
                float3 nTL = SampleSceneNormals(uvTL);
                float3 nBR = SampleSceneNormals(uvBR);

                float3 nd1 = nTR - nBL;
                float3 nd2 = nBR - nTL;
                float normalDiff = sqrt(dot(nd1, nd1) + dot(nd2, nd2));
                float normalEdge = step(_NormalThreshold, normalDiff);

                float edge = max(depthEdge, normalEdge);

                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(col, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
