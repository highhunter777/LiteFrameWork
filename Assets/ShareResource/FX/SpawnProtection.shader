Shader "LiteGame/FX/SpawnProtection"
{
    Properties
    {
        [HDR] _Color("Core Color", Color) = (1, 0.78, 0.25, 0.08)
        [HDR] _RimColor("Rim Color", Color) = (1, 0.85, 0.35, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity("Rim Intensity", Range(0, 8)) = 2.5
        _PulseSpeed("Pulse Speed", Range(0, 8)) = 2
        _PulseAmount("Pulse Amount", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SpawnProtection"
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _RimIntensity;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float rim = pow(saturate(1.0 - dot(normalize(input.normalWS), viewDir)), _RimPower);
                float pulse = 1.0 - _PulseAmount * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
                half3 rgb = _Color.rgb + _RimColor.rgb * rim * _RimIntensity;
                half alpha = saturate((_Color.a + rim * 0.55) * pulse);
                return half4(rgb * pulse, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
