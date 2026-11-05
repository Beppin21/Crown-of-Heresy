// Para los indicadores flotantes (FloatingMarker): un color plano, sin luz y sin niebla, dibujado
// encima de todo (ZTest Always), así se ve detrás de las casas y en la niebla de la noche. El canal
// rojo del color de los vértices elige entre el borde (0) y el relleno (1).
Shader "Crown/Indicador (sin niebla)"
{
    Properties
    {
        [HDR] _Color ("Relleno", Color) = (1, 0.82, 0.1, 1)
        _OutlineColor ("Borde", Color) = (0.05, 0.04, 0.03, 0.85)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Indicador"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fill : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.fill = input.color.r;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return lerp(_OutlineColor, _Color, input.fill);
            }
            ENDHLSL
        }
    }
}
