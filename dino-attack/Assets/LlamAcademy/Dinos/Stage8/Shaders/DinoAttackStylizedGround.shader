Shader "Dino Attack/Environment/Stylized Ground"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _SurfaceMap("Roughness or Specular Map", 2D) = "white" {}
        _DetailScale("Detail World Scale", Range(0.25, 16)) = 3
        _MacroScale("Macro World Scale", Range(4, 128)) = 36
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.35
        _SurfaceMapIsSpecular("Surface Map Is Specular", Range(0, 1)) = 0
        _SmoothnessMin("Minimum Smoothness", Range(0, 1)) = 0.04
        _SmoothnessMax("Maximum Smoothness", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
        }

        Pass
        {
            Name "StylizedGroundForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SurfaceMap);
            SAMPLER(sampler_SurfaceMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _NormalMap_ST;
                float4 _SurfaceMap_ST;
                half _DetailScale;
                half _MacroScale;
                half _NormalStrength;
                half _SurfaceMapIsSpecular;
                half _SmoothnessMin;
                half _SmoothnessMax;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 detailUV = input.positionWS.xz / max(_DetailScale, 0.001h);
                float2 macroUV = input.positionWS.xz / max(_MacroScale, 0.001h);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, detailUV).rgb * _BaseColor.rgb;
                half macro = 0.90h + 0.10h * sin((macroUV.x + macroUV.y) * 6.28318h);
                albedo *= macro;

                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, detailUV), _NormalStrength);
                half3 normalWS = normalize(half3(normalTS.x, normalTS.z, normalTS.y));

                half surfaceSample = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, detailUV).r;
                half smoothnessSource = lerp(1.0h - surfaceSample, surfaceSample, _SurfaceMapIsSpecular);
                half smoothness = lerp(_SmoothnessMin, _SmoothnessMax, saturate(smoothnessSource));

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirection = SafeNormalize(mainLight.direction + viewDirectionWS);
                half specular = pow(saturate(dot(normalWS, halfDirection)), lerp(8.0h, 64.0h, smoothness)) * smoothness;

                half3 color = albedo * (ambient + mainLight.color * (0.35h + 0.65h * ndotl));
                color += mainLight.color * specular * 0.16h;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
