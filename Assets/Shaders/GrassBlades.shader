Shader "Custom/Mobile/GrassBlades"
{
    Properties
    {
        [Header(Colors)]
        _BaseColor ("Root Color", Color) = (0.30, 0.34, 0.09, 1)
        _TipColor ("Tip Color", Color) = (0.52, 0.57, 0.18, 1)
        _ColorVariation ("Per Blade Color Variation", Range(0, 1)) = 0.25
        _RootShade ("Root Shading", Range(0, 1)) = 0.75

        [Header(Wind)]
        _WindDirection ("Wind Direction (XZ)", Vector) = (0.75, 0, 0.65, 0)
        _WindStrength ("Wind Strength", Range(0, 1)) = 0.12
        _WindSpeed ("Wind Speed", Range(0, 8)) = 1.6
        _WindScale ("Wind Spatial Scale", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        LOD 100

        // Blades are single-sided cards spun to random yaws - both faces must draw
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Mobile optimization: prefer half precision
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            // Unity defined keywords
            #pragma multi_compile_fog

            // Deliberately no shadow/lightmap keywords and no per-pixel work:
            // blades are lit per-vertex with an up normal so they take the same
            // tone as the ground plane beneath them, and thousands of blades
            // stay cheap on WebGL2/WebGPU integrated GPUs. No textures, no
            // clip() - early-Z stays enabled on tile-based GPUs.

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                // x = normalized height along blade (0 root, 1 tip)
                // y = per-blade random 0..1 (tint + phase jitter)
                half2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR0;
                half fogFactor : TEXCOORD0;
            };

            // Global arc effect parameters (controlled by ArcEffectController)
            float _ArcStrength;
            float _ArcDistance;
            float3 _PlayerPosition;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                half _ColorVariation;
                half _RootShade;
                half4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindScale;
            CBUFFER_END

            // Same curve as Custom/Mobile/ArcEffect so blades stay glued to the
            // floor tiles they sit on
            void ApplyArcEffect(inout float3 positionWS)
            {
                float2 offset = positionWS.xz - _PlayerPosition.xz;
                half dist = length(offset);
                half arcBlend = saturate(dist / _ArcDistance);
                half arcAmount = arcBlend * arcBlend * _ArcStrength;
                positionWS.y -= arcAmount;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                half t = input.uv.x;
                half rand = input.uv.y;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Wind: two layered sines travelling along the wind direction,
                // phase-jittered per blade. Weighted by t^2 so roots stay
                // planted while tips swing.
                half2 windDir = normalize(_WindDirection.xz + half2(0.0001h, 0));
                half phase = _Time.y * _WindSpeed
                           + dot(positionWS.xz, windDir) * _WindScale
                           + rand * 2.4h;
                half sway = sin(phase) + 0.35h * sin(phase * 2.17h + 1.3h) + 0.4h;
                half swayWeight = t * t * _WindStrength;
                positionWS.xz += windDir * (sway * swayWeight);

                // Arc after wind so the dip matches the displaced position
                ApplyArcEffect(positionWS);

                output.positionCS = TransformWorldToHClip(positionWS);

                // Per-vertex lighting with a fixed up normal: same main light +
                // 0.45 * SH ambient recipe as the ground shader, so grass and
                // ground read as one surface
                Light mainLight = GetMainLight();
                half3 lighting = SampleSH(half3(0, 1, 0)) * 0.45h
                               + mainLight.color * saturate(mainLight.direction.y);

                half3 albedo = lerp(_BaseColor.rgb, _TipColor.rgb, t);
                albedo *= 1.0h + _ColorVariation * (rand * 2.0h - 1.0h);
                albedo *= lerp(_RootShade, 1.0h, t);

                output.color = albedo * lighting;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = MixFog(input.color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Depth only pass (PC pipeline asset renders a depth texture)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Global arc effect parameters
            float _ArcStrength;
            float _ArcDistance;
            float3 _PlayerPosition;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                half _ColorVariation;
                half _RootShade;
                half4 _WindDirection;
                half _WindStrength;
                half _WindSpeed;
                half _WindScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            void ApplyArcEffect(inout float3 positionWS)
            {
                float2 offset = positionWS.xz - _PlayerPosition.xz;
                half dist = length(offset);
                half arcBlend = saturate(dist / _ArcDistance);
                half arcAmount = arcBlend * arcBlend * _ArcStrength;
                positionWS.y -= arcAmount;
            }

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                half t = input.uv.x;
                half rand = input.uv.y;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Must match the forward pass displacement exactly or the
                // depth texture ghosts against the color pass
                half2 windDir = normalize(_WindDirection.xz + half2(0.0001h, 0));
                half phase = _Time.y * _WindSpeed
                           + dot(positionWS.xz, windDir) * _WindScale
                           + rand * 2.4h;
                half sway = sin(phase) + 0.35h * sin(phase * 2.17h + 1.3h) + 0.4h;
                half swayWeight = t * t * _WindStrength;
                positionWS.xz += windDir * (sway * swayWeight);

                ApplyArcEffect(positionWS);

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // No ShadowCaster pass on purpose: thousands of blades re-rendered
        // into the shadow map would double vertex cost for barely visible
        // self-shadowing. Grass renderers also disable shadow casting.
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
