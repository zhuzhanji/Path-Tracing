Shader "TaaRP/taapass"
{
//Properties
//    {
//        _MainTex ("_MainTexx", 2D) = "white" {}
//}
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

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

            sampler2D currentTex;
            sampler2D preTex;
            sampler2D velocityTexture;
            sampler2D currentDepth;
            float2 _ScreenSize;
            int firstFrame;

			float3 RGB2YCoCgR(float3 rgbColor)
			{
				float3 YCoCgRColor;
			
				YCoCgRColor.y = rgbColor.r - rgbColor.b;
				float temp = rgbColor.b + YCoCgRColor.y / 2;
				YCoCgRColor.z = rgbColor.g - temp;
				YCoCgRColor.x = temp + YCoCgRColor.z / 2;
			
				return YCoCgRColor;
			}
			
			float3 YCoCgR2RGB(float3 YCoCgRColor)
			{
				float3 rgbColor;
			
				float temp = YCoCgRColor.x - YCoCgRColor.z / 2;
				rgbColor.g = YCoCgRColor.z + temp;
				rgbColor.b = temp - YCoCgRColor.y / 2;
				rgbColor.r = rgbColor.b + YCoCgRColor.y;
			
				return rgbColor;
			}
			
			float Luminance2(float3 color)
			{
				return 0.25 * color.r + 0.5 * color.g + 0.25 * color.b;
			}
			
			float3 ToneMap(float3 color)
			{
				return color / (1 + Luminance2(color));
			}
			
			float3 UnToneMap(float3 color)
			{
				return color / (1 - Luminance2(color));
			}
			
			float2 getClosestOffset(float2 screenPosition)
			{
				float2 deltaRes = float2(1.0 / _ScreenSize.x, 1.0 / _ScreenSize.y);
				float closestDepth = 1.0f;
				float2 closestUV = screenPosition;
			
				for(int i=-1;i<=1;++i)
				{
					for(int j=-1;j<=1;++j)
					{
						float2 newUV = screenPosition + deltaRes * float2(i, j);
			
						float depth = tex2D(currentDepth, newUV).x;
			
						if(depth < closestDepth && depth > 0.0001)
						{
							closestDepth = depth;
							closestUV = newUV;
						}
					}
				}
			
				return closestUV;
			}
			
			float3 clipAABB(float3 nowColor, float3 preColor, float2 screenPosition)
			{
				float3 aabbMin = nowColor, aabbMax = nowColor;
				float2 deltaRes = float2(1.0 / _ScreenSize.x, 1.0 / _ScreenSize.y);
				float3 m1 = float3(0.0, 0.0, 0.0), m2 = float3(0.0, 0.0, 0.0);
			
				for(int i=-1;i<=1;++i)
				{
					for(int j=-1;j<=1;++j)
					{
						float2 newUV = screenPosition + deltaRes * float2(i, j);
						float3 C = RGB2YCoCgR(ToneMap(tex2D(currentTex, newUV).rgb));
						m1 += C;
						m2 += C * C;
					}
				}
			
				// Variance clip
				const int N = 9;
				const float VarianceClipGamma = 1.0f;
				float3 mu = m1 / N;
				float3 sigma = sqrt(abs(m2 / N - mu * mu));
				aabbMin = mu - VarianceClipGamma * sigma;
				aabbMax = mu + VarianceClipGamma * sigma;
			
				// clip to center
				float3 p_clip = 0.5 * (aabbMax + aabbMin);
				float3 e_clip = 0.5 * (aabbMax - aabbMin);
			
				float3 v_clip = preColor - p_clip;
				float3 v_unit = v_clip.xyz / e_clip;
				float3 a_unit = abs(v_unit);
				float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));
			
				if (ma_unit > 1.0)
					return p_clip + v_clip / ma_unit;
				else
					return preColor;
			}


            float4 frag (v2f i) : SV_Target
            {
				float3 nowColor = tex2D(currentTex, i.uv).rgb;
				
				if(firstFrame == 1 /*|| tex2D(currentDepth, i.uv).x < 0.0001*/)
				{
					float3 outColor = nowColor;
					return float4(outColor, 1.0);
				}
			
				// 3x3、foremost、velocity vector
				float2 closestUV = getClosestOffset(i.uv);
				float2 lastUV = tex2D(velocityTexture, closestUV).rg;


				float2 offsetUV = clamp(lastUV, 0, 1);
				float3 preColor = tex2D(preTex, offsetUV).rgb;
			
				nowColor = RGB2YCoCgR(ToneMap(nowColor));
				preColor = RGB2YCoCgR(ToneMap(preColor));
			
				preColor = clipAABB(nowColor, preColor, i.uv);
			
				preColor = UnToneMap(YCoCgR2RGB(preColor));
				nowColor = UnToneMap(YCoCgR2RGB(nowColor));
			
				float c = 0.05;
				float4 outColor = float4(c * nowColor + (1-c) * preColor, 1.0);
				return float4(outColor.xyz, 1.0);
            }
            ENDCG
        }
    }
}
