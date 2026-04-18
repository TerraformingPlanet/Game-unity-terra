Shader "Terraformation/PlanetClouds"
{
    Properties
    {
        _CloudColor ("Cloud Color", Color) = (1,1,1,1)
        _Coverage ("Coverage", Range(0,1)) = 0.58
        _Opacity ("Opacity", Range(0,1)) = 0.8
        _Softness ("Softness", Range(0.01,0.6)) = 0.16
        _FresnelStrength ("Fresnel Strength", Range(0,1.5)) = 0.35
        _PrimaryTiling ("Primary Tiling", Range(0.5,12)) = 2.1
        _SecondaryTiling ("Secondary Tiling", Range(0.5,16)) = 5.8
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        LOD 200
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _CloudColor;
                float _Coverage;
                float _Opacity;
                float _Softness;
                float _FresnelStrength;
                float _PrimaryTiling;
                float _SecondaryTiling;
                float _DetailStrength;
            CBUFFER_END

            float2 SphericalUV(float3 normalWS)
            {
                float3 n = normalize(normalWS);
                float u = atan2(n.z, n.x) * (0.15915494) + 0.5;
                float v = asin(saturate(n.y) * 2.0 - 1.0);
                v = asin(clamp(n.y, -1.0, 1.0)) * 0.31830989 + 0.5;
                return float2(u, v);
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    value += amplitude * Noise(p);
                    p = p * 2.03 + float2(17.1, 9.2);
                    amplitude *= 0.5;
                }

                return value;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                OUT.positionWS = posInputs.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = SphericalUV(IN.normalWS);
                float time = _Time.y;

                float layerA = Fbm(uv * _PrimaryTiling + float2(time * 0.02, time * 0.01));
                float layerB = Fbm(uv * _SecondaryTiling + float2(-time * 0.018, time * 0.014));
                float detail = Fbm(uv * (_SecondaryTiling * 1.9) + float2(time * 0.028, -time * 0.017));

                float cloudMask = lerp(layerA, layerB, 0.45) + (detail - 0.5) * _DetailStrength;
                float alpha = smoothstep(_Coverage - _Softness, _Coverage + _Softness, cloudMask);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);
                float fresnel = pow(1.0 - saturate(dot(N, V)), 2.4) * _FresnelStrength;

                half4 col = _CloudColor;
                col.rgb = lerp(col.rgb, col.rgb * 1.15, fresnel);
                col.a = saturate(alpha * _Opacity + fresnel * 0.18);
                return col;
            }
            ENDHLSL
        }
    }
}