Shader "Custom/Mobile/BlobShadow"
{
    Properties
    {
        [Header(Base Properties)]
        _BaseMap ("Blob Texture", 2D) = "white" {}
        _BaseColor ("Tint (alpha drives strength)", Color) = (0, 0, 0, 1)

        // Arc parameters are globals driven by ArcEffectController, exactly as
        // in Custom/Mobile/ArcEffect - declaring them per-material would let a
        // serialized value override the global and lift the blob off the floor.
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        LOD 100

        // Unlit, alpha-blended ground decal. There is deliberately no
        // ShadowCaster or DepthOnly pass: the blob stands in for a real shadow,
        // so it must never cast one or write depth over the floor behind it.
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            // Bias toward the camera so the decal never z-fights the floor it
            // is lying on, whatever the arc offset does to both surfaces.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Global arc effect parameters (controlled by ArcEffectController)
            float _ArcStrength;
            float _ArcDistance;
            float3 _PlayerPosition;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            void ApplyArcEffect(inout float3 positionWS)
            {
                float2 offset = positionWS.xz - _PlayerPosition.xz;
                half distance = length(offset);
                half arcBlend = saturate(distance / _ArcDistance);
                half arcAmount = arcBlend * arcBlend * _ArcStrength;
                positionWS.y -= arcAmount;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                ApplyArcEffect(positionWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 blob = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = blob * _BaseColor;

                // Fade the decal out with fog rather than tinting it toward the
                // fog colour - a dark blob lightened by fog reads as a stain.
                color.a *= ComputeFogIntensity(input.fogFactor);

                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
