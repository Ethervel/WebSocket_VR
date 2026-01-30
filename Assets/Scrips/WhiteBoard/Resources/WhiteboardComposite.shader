Shader "Hidden/WhiteboardComposite"
{
    Properties
    {
        _MainTex ("Base (Screen Share)", 2D) = "white" {}
        _OverlayTex ("Overlay (Annotations)", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _OverlayTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample both textures
                fixed4 base = tex2D(_MainTex, i.uv);
                fixed4 overlay = tex2D(_OverlayTex, i.uv);

                // Alpha blending: overlay on top of base
                fixed3 result = lerp(base.rgb, overlay.rgb, overlay.a);

                return fixed4(result, 1.0);
            }
            ENDCG
        }
    }
}
