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
                float3 albedo: TEXCOORD2;
                float4 preScreenPosition: TEXCOORD3;
                float4 nowScreenPosition: TEXCOORD4;
                float4 vertex : SV_POSITION;
            };
            struct f2o
            {
                float4 position: COLOR0; 
                float4 normal: COLOR1;
                float4 albedo: COLOR2;
                float4 motion: COLOR3;

            };

            float3 _Albedo;
            float4x4 _LastVP;
            float2 _Jitter;
            int matid;
           // int _CurrentIndex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.vertex.xy += _Jitter;

                o.worldpos = mul(unity_ObjectToWorld, v.vertex);
                o.normal = float4(UnityObjectToWorldNormal(v.normal), 1.0);
                o.albedo = _Albedo;

                o.nowScreenPosition = UnityObjectToClipPos(v.vertex);
                o.preScreenPosition = mul(mul(_LastVP , unity_ObjectToWorld) , v.vertex);

                return o;
            }

            f2o frag (v2f i)
            {
                // apply fog
                //UNITY_APPLY_FOG(i.fogCoord, col);
                f2o output;
                output.position = float4((i.worldpos.xyz) , 1.0);
                output.normal = float4(i.normal.xyz, matid);
                output.albedo = float4(i.albedo, 1.0);

                float2 newPos = ((i.nowScreenPosition.xy / i.nowScreenPosition.w) * 0.5 + 0.5);
	            float2 prePos = ((i.preScreenPosition.xy / i.preScreenPosition.w) * 0.5 + 0.5);
                output.motion = float4(prePos, newPos);
                return output;
            }
            ENDCG
        }
    }
}
