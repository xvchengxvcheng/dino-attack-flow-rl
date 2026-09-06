// Adapted from aniruddhahar/URP-WaterShaders (MIT).
// Copyright (c) 2020 aniruddhahar.
// See Assets/ThirdParty/URP-WaterShaders-LICENSE.txt.
// This version omits scene color, depth textures, renderer features, caustics, and displacement.
Shader "Dino Attack/Environment/Shallow Water"
{
    Properties
    {
        [MainColor] _BaseColor("Shallow Color", Color) = (0.10, 0.56, 0.58, 0.76)
        _DeepColor("Deep Color", Color) = (0.055, 0.34, 0.40, 0.80)
        _FoamColor("Shore Color", Color) = (0.65, 0.90, 0.82, 0.75)
        [Normal] _FlowNormal("Flow Normal", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0, 1)) = 0.22
        _TilingA("Primary Tiling", Float) = 3.2
        _TilingB("Secondary Tiling", Float) = 4.7
        _PanA("Primary Flow", Vector) = (0.018, 0.010, 0, 0)
        _PanB("Secondary Flow", Vector) = (-0.012, 0.016, 0, 0)
        _FoamWidth("Shore Width", Range(0.001, 0.25)) = 0.065
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3.5
        _Smoothness("Smoothness", Range(0, 1)) = 0.78
        [HideInInspector] _ZWrite("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ShallowWaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_FlowNormal);
            SAMPLER(sampler_FlowNormal);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _FoamColor;
                float4 _FlowNormal_ST;
                half _NormalStrength;
                half _TilingA;
                half _TilingB;
                half4 _PanA;
                half4 _PanB;
                half _FoamWidth;
                half _FresnelPower;
                half _Smoothness;
                half _ZWrite;
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
                output.uv = TRANSFORM_TEX(input.uv, _FlowNormal);
                return output;
            }

            half3 DecodeNormal(half4 packedNormal)
            {
                half2 xy = (packedNormal.xy * 2.0h - 1.0h) * _NormalStrength;
                return half3(xy, sqrt(saturate(1.0h - dot(xy, xy))));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uvA = input.uv * _TilingA + _Time.y * _PanA.xy;
                float2 uvB = input.uv * _TilingB + _Time.y * _PanB.xy;
                half3 normalA = DecodeNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvA));
                half3 normalB = DecodeNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvB));
                half3 normalTS = normalize(half3(normalA.xy + normalB.xy, normalA.z * normalB.z));

                half3 normalWS = normalize(input.normalWS);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
                half3 waterNormalWS = normalize(mul(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));

                half edgeDistance = min(min(input.uv.x, 1.0h - input.uv.x), min(input.uv.y, 1.0h - input.uv.y));
                half shore = 1.0h - smoothstep(0.0h, _FoamWidth, edgeDistance);
                half depthBlend = smoothstep(_FoamWidth, 0.42h, edgeDistance);
                half3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0h - saturate(dot(viewDirectionWS, waterNormalWS)), _FresnelPower);
                Light mainLight = GetMainLight();
                half diffuse = 0.75h + 0.25h * saturate(dot(waterNormalWS, mainLight.direction));
                half specular = pow(saturate(dot(reflect(-mainLight.direction, waterNormalWS), viewDirectionWS)), lerp(8.0h, 96.0h, _Smoothness));

                half3 waterColor = lerp(_BaseColor.rgb, _DeepColor.rgb, depthBlend);
                waterColor = waterColor * diffuse * mainLight.color + fresnel * 0.10h + specular * 0.18h;
                waterColor = lerp(waterColor, _FoamColor.rgb, shore * _FoamColor.a);
                half alpha = saturate(lerp(_BaseColor.a, _DeepColor.a, depthBlend) + fresnel * 0.06h + shore * 0.08h);
                return half4(waterColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
