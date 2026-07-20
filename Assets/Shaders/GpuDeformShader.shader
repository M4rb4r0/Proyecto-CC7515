Shader "Custom/GPULitAndShadows"
{
    Properties
    {
        _Color ("Color Base", Color) = (1, 1, 1, 1)
        _MainTex ("Textura", 2D) = "white" {}
        _AOTexture ("Ambient Occlusion", 2D) = "white" {}
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
            sampler2D _AOTexture;
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
                float4 screenPosition : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 gpuPosition = vertexBuffer[v.id].position;
                float3 gpuNormal = vertexBuffer[v.id].normal;

                o.pos = UnityObjectToClipPos(float4(gpuPosition, 1.0));
                o.screenPosition = ComputeScreenPos(o.pos);
                o.worldNormal = UnityObjectToWorldNormal(gpuNormal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0.0, dot(normal, lightDir));

                float2 screenUV = i.screenPosition.xy / i.screenPosition.w;
                float ao = tex2D(_AOTexture, screenUV).r;
                
                float3 ambient = ShadeSH9(half4(normal, 1.0));
                ambient *= ao;

                float3 lighting = ambient + (_LightColor0.rgb * NdotL);

                fixed4 textureColor = tex2D(_MainTex, i.uv);
                fixed4 baseColor = textureColor * _Color;
                
                return fixed4(baseColor.rgb * lighting, baseColor.a);
            }
            ENDCG
        }

        // ===========================================
        // PASE 1.5: DATOS PARA SSAO
        // Calcula posición y normal en view space.
        // ===========================================
        Pass
        {
            Name "SSAOGBuffer"

            Tags
            {
                "LightMode" = "SSAOGBuffer"
            }

            CGPROGRAM

            #pragma vertex vertGBuffer
            #pragma fragment fragGBuffer
            #pragma target 5.0

            #include "UnityCG.cginc"

            struct VertexData
            {
                float3 position;
                float3 normal;
            };

            StructuredBuffer<VertexData> vertexBuffer;

            // Datos enviados desde el vertex shader
            // hacia el fragment shader.
            struct v2fGBuffer
            {
                // Posición usada por el rasterizador.
                float4 clipPosition : SV_POSITION;

                // Datos geométricos interpolados por píxel.
                float3 viewPosition : TEXCOORD0;
                float3 viewNormal   : TEXCOORD1;
            };

            v2fGBuffer vertGBuffer(uint id : SV_VertexID)
            {
                v2fGBuffer output;

                float3 objectPosition = vertexBuffer[id].position;

                float3 objectNormal = vertexBuffer[id].normal;

                float4 objectPosition4 = float4(objectPosition, 1.0);

                // Posición en clip space:
                // permite rasterizar el triángulo.
                output.clipPosition = UnityObjectToClipPos(objectPosition4);

                // Posición respecto de la cámara.
                output.viewPosition = mul(UNITY_MATRIX_MV, objectPosition4).xyz;

                // Primero transformamos la normal desde
                // object space hacia world space.
                float3 worldNormal = UnityObjectToWorldNormal(objectNormal);

                // Luego desde world space hacia view space.
                output.viewNormal = normalize(mul((float3x3)UNITY_MATRIX_V, worldNormal));
                return output;
            }

            // El fragment shader escribirá en dos texturas.
            struct GBufferOutput
            {
                float4 position : SV_Target0;
                float4 normal   : SV_Target1;
            };

            GBufferOutput fragGBuffer(v2fGBuffer input)
            {
                GBufferOutput output;

                output.position = float4(input.viewPosition, 1.0);

                output.normal = float4(normalize(input.viewNormal), 1.0);

                return output;
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