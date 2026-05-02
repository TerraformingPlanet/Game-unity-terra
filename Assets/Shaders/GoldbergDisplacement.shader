Shader "Terraformation/GoldbergDisplacement"
{
    // Vertex displacement GPU depuis une heightmap lat/lon (équirectangulaire).
    // UV0 du mesh = (longitude, latitude) normalisés [0,1].
    // Canal R de _HeightMap = altitude remappée [0,1] (0.5 = niveau de la mer).
    // Vertex color = couleur biome H3 (inchangée).

    Properties
    {
        _HeightMap         ("Height Map (R=altitude)", 2D) = "gray" {}
        _DisplacementScale ("Displacement Scale",  Range(0, 3)) = 0.5
        _SeaLevel          ("Sea Level (abs altitude)", Range(-1, 1)) = 0.0
        _BaseColor         ("Base Color Tint",     Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 200
        Cull Off
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // Nécessaire pour que le sampler fonctionne en vertex stage
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HeightMap_ST;
                float  _DisplacementScale;
                float  _SeaLevel;
                float4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_HeightMap);
            SAMPLER(sampler_HeightMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;  // UV centroïde (même pour toute la tuile)
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Utiliser UV1 (centroïde de la tuile) pour échantillonner le heightmap.
                // Toutes les vertices d'une même tuile obtiennent la même altitude
                // → displacement UNIFORME → tuile plate, zéro effet mesa/losange.
                float rawH     = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, IN.uv1, 0).r;
                float altitude = rawH * 2.0 - 1.0;   // [0,1] → [-1,1]

                // Clamp ocean : tout vertex sous le sea level est ramené à exactement sea level.
                // Garantit qu'aucune face ocean ne perce la water sphere (qui est à seaLevel + epsilon).
                // Les faces land (altitude >= _SeaLevel) sont inchangées.
                altitude = max(altitude, _SeaLevel);

                // Déplacement radial : on utilise la direction sphérique (normalize(positionOS))
                // et NON pas normalOS. Raison : les coins entre 3 tuiles ont des copies
                // indépendantes (normales différentes) → normalOS donnerait des gaps.
                // La direction radiale est identique pour toutes les copies d'un même coin.
                float3 radial    = normalize(IN.positionOS.xyz);
                float3 displaced = IN.positionOS.xyz + radial * (altitude * _DisplacementScale);

                OUT.positionHCS = TransformObjectToHClip(displaced);
                OUT.positionWS  = TransformObjectToWorld(displaced);
                OUT.normalWS    = TransformObjectToWorldNormal(radial);
                OUT.color       = IN.color * _BaseColor;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Éclairage Lambert simple (directional light principale)
                InputData   inputData  = (InputData)0;
                inputData.normalWS     = normalize(IN.normalWS);
                inputData.positionWS   = IN.positionWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord  = float4(0, 0, 0, 0);
                inputData.fogCoord     = 0;
                inputData.bakedGI      = SampleSH(inputData.normalWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = IN.color.rgb;
                surfaceData.alpha       = 1.0;
                surfaceData.smoothness  = 0.15;
                surfaceData.metallic    = 0.0;
                surfaceData.occlusion   = 1.0;
                surfaceData.normalTS    = float3(0, 0, 1);

                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        // Shadow caster pass — nécessaire pour que la planète reçoive/projette des ombres
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _HeightMap_ST;
                float  _DisplacementScale;
                float  _SeaLevel;
                float4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_HeightMap);
            SAMPLER(sampler_HeightMap);

            struct AttrShadow { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct VaryShadow { float4 positionHCS : SV_POSITION; };

            VaryShadow vertShadow(AttrShadow IN)
            {
                VaryShadow OUT;
                float rawH     = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, IN.uv1, 0).r;
                float altitude = rawH * 2.0 - 1.0;
                altitude = max(altitude, _SeaLevel);
                float3 radial    = normalize(IN.positionOS.xyz);
                float3 displaced = IN.positionOS.xyz + radial * (altitude * _DisplacementScale);
                OUT.positionHCS = TransformObjectToHClip(displaced);
                return OUT;
            }

            half4 fragShadow(VaryShadow IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
