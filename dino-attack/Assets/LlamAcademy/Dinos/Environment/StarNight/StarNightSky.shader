Shader "DinoAttack/Skybox/StarNight"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.012, 0.018, 0.055, 1)
        _HorizonColor ("Horizon", Color) = (0.065, 0.085, 0.15, 1)
        _NebulaColor ("Nebula", Color) = (0.035, 0.038, 0.075, 1)
        _StarIntensity ("Star Intensity", Range(0, 2)) = 0.85
        _Rotation ("Rotation", Range(0, 360)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            float4 _ZenithColor, _HorizonColor, _NebulaColor;
            float _StarIntensity, _Rotation;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float angle = radians(_Rotation);
                d.xz = float2(d.x*cos(angle)-d.z*sin(angle), d.x*sin(angle)+d.z*cos(angle));
                float altitude = saturate(abs(d.y));
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(altitude, 0.45));
                float band = exp(-pow(dot(d, normalize(float3(0.6, 0.35, 0.7))) * 6.0, 2.0));
                float clouds = 0.55 + 0.25*sin(d.x*13.0+d.z*9.0) * sin(d.y*17.0-d.z*7.0);
                sky += _NebulaColor.rgb * band * clouds * smoothstep(0.05, 0.45, altitude);
                float2 uv = float2(atan2(d.z, d.x) / (2.0*UNITY_PI) + 0.5, asin(clamp(d.y,-1.0,1.0)) / UNITY_PI + 0.5);
                float2 grid = uv * float2(1000.0, 500.0);
                float2 cell = floor(grid);
                float seed = hash(cell);
                float2 offset = float2(hash(cell + 9.3), hash(cell + 21.7)) * 0.5 + 0.25;
                float2 delta = frac(grid) - offset;
                float radius = max(0.08, min(0.30, max(fwidth(grid.x), fwidth(grid.y)) * 0.45));
                float star = exp(-dot(delta,delta)/(radius*radius)) * step(0.996,seed);
                float brightness = 0.35 + hash(cell + 47.0) * 0.9;
                sky += lerp(float3(0.66,0.78,1.0),float3(1.0,0.94,0.85),hash(cell+62.0)) * star * brightness * _StarIntensity * smoothstep(0.02,0.20,altitude) * (d.y >= 0.0 ? 1.0 : 0.4);
                // A small moon and restrained halo; no animated or oversized star motifs.
                float moonDistance = length(d - normalize(float3(-0.35, 0.55, 0.75)));
                sky += float3(0.68,0.79,1.0) * (1.0-smoothstep(0.011,0.013,moonDistance))*0.75;
                sky += float3(0.10,0.13,0.20) * exp(-moonDistance*moonDistance/0.002)*0.15;
                return half4(sky,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
