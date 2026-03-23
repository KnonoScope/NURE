Shader "NURE/RuntimeSkyDomeUnlit"
{
    Properties
    {
        [MainTexture] _MainTex("Panorama", 2D) = "white" {}
        [NoScaleOffset] _CubeTex("Cubemap", Cube) = "" {}
        [MainColor] _Tint("Tint", Color) = (1,1,1,1)
        _Exposure("Exposure", Float) = 1
        _UseCube("Use Cube", Float) = 0
        _PanoramaMipLevel("Panorama Mip Level", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Background"
            "IgnoreProjector" = "True"
        }

        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "RuntimeSkyDome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 skyDirWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURECUBE(_CubeTex);
            SAMPLER(sampler_CubeTex);
            float4 _MainTex_HDR;

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                half _UseCube;
                half _PanoramaMipLevel;
            CBUFFER_END

            half3 DecodeSkyHDR(half4 encoded)
            {
                half decodeMagnitude = abs(_MainTex_HDR.x) + abs(_MainTex_HDR.y) + abs(_MainTex_HDR.z) + abs(_MainTex_HDR.w);
                if (decodeMagnitude <= 0.0001h)
                    return encoded.rgb;

                half alpha = _MainTex_HDR.w * (encoded.a - 1.0h) + 1.0h;
#if defined(UNITY_COLORSPACE_GAMMA)
                return (_MainTex_HDR.x * alpha) * encoded.rgb;
#else
                return (_MainTex_HDR.x * PositivePow(alpha, _MainTex_HDR.y)) * encoded.rgb;
#endif
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.skyDirWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color;
                float3 skyDirWS = normalize(input.skyDirWS);

                if (_UseCube > 0.5h)
                {
                    color = SAMPLE_TEXTURECUBE(_CubeTex, sampler_CubeTex, skyDirWS).rgb;
                }
                else
                {
                    float2 panoUv = DirectionToLatLongCoordinate(skyDirWS);
                    color = DecodeSkyHDR(SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, panoUv, _PanoramaMipLevel));
                }

                color *= _Tint.rgb * _Exposure;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
