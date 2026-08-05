// Flat two-band gradient sky: horizon colour blending up to _TopColor and down
// to _BottomColor. No texture, no sun disc - one draw of a few instructions, so
// it costs about the same as the flat Unlit sky Seattle uses.
Shader "Ringsport/Gradient Skybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.11, 0.43, 0.64, 1)
        _HorizonColor ("Horizon Color", Color) = (0.61, 0.82, 0.89, 1)
        _BottomColor ("Bottom Color", Color) = (0.38, 0.67, 0.77, 1)
        _UpExponent ("Up Falloff", Range(0.1, 8)) = 1.4
        _DownExponent ("Down Falloff", Range(0.1, 8)) = 1.6
        _Exposure ("Exposure", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            half4 _TopColor;
            half4 _HorizonColor;
            half4 _BottomColor;
            half _UpExponent;
            half _DownExponent;
            half _Exposure;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // Skybox mesh is centred on the camera, so object space position
                // is the view direction.
                o.dir = v.vertex.xyz;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half y = normalize(i.dir).y;
                half up = pow(saturate(y), _UpExponent);
                half down = pow(saturate(-y), _DownExponent);
                half3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, up);
                col = lerp(col, _BottomColor.rgb, down);
                return half4(col * _Exposure, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
