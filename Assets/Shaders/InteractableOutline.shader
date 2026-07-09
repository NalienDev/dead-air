// White hover outline for interactables. Inverted-hull: the mesh is redrawn
// slightly inflated along its normals with front faces culled, so only a thin
// shell shows around the silhouette. HoverOutline creates/toggles the shell
// meshes; the Interactor drives it from its look-at raycast.
Shader "Custom/InteractableOutline"
{
    Properties
    {
        _OutlineColor("Outline color", Color) = (1, 1, 1, 1)
        _Thickness("Thickness (m)", Range(0.001, 0.1)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "InvertedHullOutline"
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _OutlineColor;
            float _Thickness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));

                // Inflate along world-space normals so the shell thickness is
                // consistent regardless of the object's scale.
                posWS += nrmWS * _Thickness;

                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
