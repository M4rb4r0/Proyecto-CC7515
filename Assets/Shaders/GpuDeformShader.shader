Shader "Custom/GPULitAndShadows"
{
    Properties
    {
        _Color ("Color Base", Color) = (1, 1, 1, 1)
        _MainTex ("Textura", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off

        // ==========================================
        // PASE 1: ILUMINACIÓN Y COLOR
        // ==========================================
        Pass
        {
            Tags { "LightMode"="ForwardBase" }


            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;

            struct VertexData {
                float3 position;
                float3 normal;
            };

            StructuredBuffer<VertexData> vertexBuffer;

            // recibir texturas
            struct appdata
            {
                float2 uv : TEXCOORD0;
                uint id : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 gpuPosition =
                    vertexBuffer[v.id].position;

                float3 gpuNormal =
                    vertexBuffer[v.id].normal;

                o.pos = UnityObjectToClipPos(
                    float4(gpuPosition, 1.0)
                );

                o.worldNormal =
                    UnityObjectToWorldNormal(gpuNormal);

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0.0, dot(normal, lightDir));
                
                float3 ambient = ShadeSH9(half4(normal, 1.0));
                float3 lighting = ambient + (_LightColor0.rgb * NdotL);

                fixed4 textureColor = tex2D(_MainTex, i.uv);
                fixed4 baseColor = textureColor * _Color;
                
                return fixed4(baseColor.rgb * lighting, baseColor.a);
            }
            ENDCG
        }

        // ==========================================
        // PASE 2: PROYECCIÓN DE SOMBRAS
        // ==========================================
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"

            struct VertexData {
                float3 position;
                float3 normal;
            };

            // Recibimos los datos de la VRAM
            StructuredBuffer<VertexData> vertexBuffer;

            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float3 gpuPosition = vertexBuffer[id].position;
                float3 gpuNormal = vertexBuffer[id].normal;

                // 1. Calculamos la posición de la sombra basados en la luz de forma manual
                float4 clipPos = UnityClipSpaceShadowCasterPos(float4(gpuPosition, 1.0), gpuNormal);
                
                // 2. Aplicamos el sesgo para evitar errores visuales
                o.pos = UnityApplyLinearShadowBias(clipPos);
                
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // El pase de sombras no pinta colores, solo recorta la silueta, por lo que devolvemos 0
                return 0; 
            }
            ENDCG
        }
    }
}