Shader "Terraformation/PlanetAtmosphere"
{
    Properties
    {
        _AtmosphereColor ("Atmosphere Color",        Color) = (0.3, 0.6, 1.0, 1.0)
        _RimPower        ("Rim Power (sharpness)",   Range(0.5, 8.0)) = 2.5
        _RimIntensity    ("Rim Intensity",           Range(0.0, 3.0)) = 1.2
        _PulsePeriod     ("Pulse Period (seconds)",  Float) = 4.0
        _PulseAmplitude  ("Pulse Amplitude",         Range(0.0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
        }

        LOD 100
        Cull Front          // rendu depuis l'intérieur = halo visible autour de la sphère GP
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _AtmosphereColor;
                float  _RimPower;
                float  _RimIntensity;
                float  _PulsePeriod;
                float  _PulseAmplitude;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N   = normalize(IN.normalWS);
                float3 V   = normalize(IN.viewDirWS);

                // Rim = 1 à la silhouette, 0 au centre
                float rim  = 1.0 - saturate(dot(N, V));
                float glow = pow(rim, _RimPower) * _RimIntensity;

                // Légère pulsation lente
                float pulse = 1.0 + _PulseAmplitude * sin(_Time.y * (6.2832 / _PulsePeriod));
                glow *= pulse;

                half4 col  = _AtmosphereColor;
                col.a      = saturate(glow * col.a);
                return col;
            }
            ENDHLSL
        }
    }
}
