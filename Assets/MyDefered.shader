Shader "Unlit/MyDefered"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZWrite On ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 normal : NORMAL; 
            };

            struct v2f
            {
                float4 worldpos: TEXCOORD0;
                float4 normal: TEXCOORD1;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };
            struct f2o
            {
                half4 color0: COLOR0;
                half4 color1: COLOR1;

            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldpos = mul(unity_ObjectToWorld, v.vertex);
                o.normal = float4(UnityObjectToWorldNormal(v.normal), 1.0);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            f2o frag (v2f i)
            {
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                f2o output;
                output.color0 = float4((i.worldpos.xyz) , 1.0);
                output.color1 = float4(((i.normal.xyz) + 1.0) * 0.5, 1.0);
                return output;
            }
            ENDCG
        }
    }
}
