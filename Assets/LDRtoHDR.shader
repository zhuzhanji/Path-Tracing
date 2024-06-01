Shader "Hidden/LDRtoHDR"
{
 //   Properties
 //   {
 //       _MainTex ("Texture", 2D) = "white" {}
 //   }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha  // enable alpha blending

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _DirectIllumination;
            sampler2D _IndirectIllumination;

            fixed4 frag (v2f i) : SV_Target
            {
                float3 direct = tex2D(_DirectIllumination, i.uv).rgb;
                direct = direct / (1.f - direct + 1e-4f);
                float3 indirect = tex2D(_IndirectIllumination, i.uv).rgb;
                indirect = indirect / (1.f - indirect + 1e-4f);
                float3 color = direct + indirect;
                return float4(color, 1.0);               
            }
            ENDCG
        }
    }
}
