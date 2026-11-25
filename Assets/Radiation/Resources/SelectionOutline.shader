Shader "Radiation/Selection Outline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.15, 0.9, 1, 1)
        _OutlineWidth ("Outline Width (pixels)", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SelectionOutline"
            Tags { "LightMode" = "UniversalForward" }

            // Рисуем только задние грани: они выходят за силуэт модели и дают
            // контур по её реальной форме, а не по ограничивающему боксу.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _OutlineColor;
            float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // Проецируем НОРМАЛЬ, а не матрицу VP, приведённую к float3x3.
                // Предыдущий вариант искажал направление при перспективной камере:
                // поэтому контур мог съезжать от излучателя или выглядеть ломаным.
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 positionVS = TransformWorldToView(positionWS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float2 direction = mul(UNITY_MATRIX_P, float4(normalVS, 0.0)).xy;
                float directionLength = length(direction);
                if (directionLength > 1e-5)
                {
                    direction /= directionLength;
                    // Переводим толщину из пикселей в clip space.
                    float2 pixelToClip = 2.0 / _ScreenParams.xy;
                    positionCS.xy += direction * pixelToClip * _OutlineWidth * positionCS.w;
                }

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, _OutlineColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
