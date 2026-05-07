Shader "Terraformation/WaterVertexColor"
{
    // Shader eau pour les WaterCaps du polyèdre Goldberg.
    // Lit les vertex colors (gradient profondeur→côtier calculé par WaterCapsBuilder).
    // Transparent + ZWrite Off + culling double-face pour les plans d'eau.
    Properties
    {
        _BaseColor    ("Base Color (tint global)", Color) = (1,1,1,1)
        _Opacity      ("Opacity globale",          Range(0,1))  = 0.88
        // Tide alpha : phase = sin(t) ∈ [-1,+1], AlphaMin = opacité au reflux.
        _TidePhase    ("Tide Phase",               Range(-1,1)) = 0.0
        _TideAlphaMin ("Tide Alpha Min (reflux)",  Range(0,1))  = 0.65
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
        // ── Forward transparent pass ───────────────────────────────────────
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Opacity;
                float  _TidePhase;
                float  _TideAlphaMin;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color * _BaseColor;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir  = normalize(GetCameraPositionWS() - IN.positionWS);
                float  fresnel  = 1.0 - saturate(dot(normalize(IN.normalWS), viewDir));
                float  specular = pow(fresnel, 4.0) * 0.20;

                half4 col = IN.color;
                col.rgb += specular;
                // Tide : modulation alpha via phase sinusoïdale.
                // Phase=+1 (pleine mer) → alpha = _Opacity ; Phase=-1 (reflux) → alpha = _Opacity * _TideAlphaMin.
                float tideFactor = lerp(_TideAlphaMin, 1.0, (_TidePhase + 1.0) * 0.5);
                col.a   *= _Opacity * tideFactor;
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
