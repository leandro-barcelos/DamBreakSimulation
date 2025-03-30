Shader "Custom/InstancedIndirectColor" {
    Properties {
        // Add this to enable transparency
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination Blend", Float) = 10 // OneMinusSrcAlpha
    }
    
    SubShader {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        
        // Enable alpha blending
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
            };

            struct v2f {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
            };

            struct MeshProperties {
                float4x4 mat;
                float4 color;
            };

            StructuredBuffer<MeshProperties> _Properties;

            v2f vert(appdata_t i, uint instanceID: SV_InstanceID) {
                v2f o;

                float4 pos = mul(_Properties[instanceID].mat, i.vertex);
                o.vertex = UnityObjectToClipPos(pos);
                o.color = _Properties[instanceID].color;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                // Early discard if alpha is 0
                clip(i.color.a - 0.001); // Discard pixels with alpha less than 0.001
                return i.color;
            }

            ENDCG
        }
    }
}
