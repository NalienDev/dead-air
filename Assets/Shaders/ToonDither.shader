Shader "Custom/ToonDither"
{
    Properties
    {
        _ToonLevels("Toon color levels", Range(2, 32)) = 5
        _DitherStrength("Dither strength", Range(0, 1)) = 0.7
        _DitherScale("Dither pixel scale", Range(1, 8)) = 1
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

            float _ToonLevels, _DitherStrength, _DitherScale;

            // 4x4 ordered Bayer threshold, 0..1
            float bayer4x4(uint2 p)
            {
                const float m[16] =
                {
                    0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                    12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                    3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                    15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
                };
                return m[(p.y & 3u) * 4u + (p.x & 3u)];
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord).rgb;

                // Nudge each pixel by its Bayer threshold (within one band) before
                // quantizing, so hard bands become an ordered dither instead.
                uint2 px = (uint2)floor(IN.texcoord * _BlitTexture_TexelSize.zw / max(_DitherScale, 1.0));
                float dither = (bayer4x4(px) - 0.5) * _DitherStrength / _ToonLevels;

                col += dither;
                col = floor(col * _ToonLevels + 0.5) / _ToonLevels;   // toon posterize

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
