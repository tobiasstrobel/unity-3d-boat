// Near-water tiles only. Same shading as FX/Water (Basic), plus a false-color
// tint of wave height. Red is a crest at +_HeightMax, blue a trough at
// -_HeightMax. Fragments outside the circle around the boat are discarded so
// the distant plane shows through. That plane stays on the stock shader.
Shader "Boatware/WaterHeight" {
Properties {
	_horizonColor ("Horizon color", COLOR)  = ( .172 , .463 , .435 , 0)
	_WaveScale ("Wave scale", Range (0.02,0.15)) = .07
	[NoScaleOffset] _ColorControl ("Reflective color (RGB) fresnel (A) ", 2D) = "" { }
	[NoScaleOffset] _BumpMap ("Waves Normalmap ", 2D) = "" { }
	WaveSpeed ("Wave speed (map1 x,y; map2 x,y)", Vector) = (19,9,-16,-7)
	_HeightMax ("Full red at height (m)", Float) = 0.45
	_TintStrength ("Max tint", Range(0,1)) = 0.25
	}

CGINCLUDE

#include "UnityCG.cginc"

uniform float4 _horizonColor;

uniform float4 WaveSpeed;
uniform float _WaveScale;
uniform float4 _WaveOffset;
uniform float _HeightMax;
uniform float _TintStrength;
uniform float _WaveRadius;
uniform float4 _WaveCenter;

struct appdata {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
};

struct v2f {
	float4 pos : SV_POSITION;
	float2 bumpuv[2] : TEXCOORD0;
	float3 viewDir : TEXCOORD2;
	float height : TEXCOORD4;
	float2 worldXZ : TEXCOORD5;
	UNITY_FOG_COORDS(3)
};

v2f vert(appdata v)
{
	v2f o;
	o.pos = UnityObjectToClipPos(v.vertex);

	float4 wpos = mul(unity_ObjectToWorld, v.vertex);
	float4 temp;
	temp.xyzw = wpos.xzxz * _WaveScale + _WaveOffset;
	o.bumpuv[0] = temp.xy * float2(.4, .45);
	o.bumpuv[1] = temp.wz;

	o.viewDir.xzy = normalize( WorldSpaceViewDir(v.vertex) );
	o.height = wpos.y;
	o.worldXZ = wpos.xz;

	UNITY_TRANSFER_FOG(o,o.pos);
	return o;
}

ENDCG


Subshader {
	Tags { "RenderType"="Opaque" }
	Pass {

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_fog

sampler2D _BumpMap;
sampler2D _ColorControl;

half4 frag( v2f i ) : COLOR
{
	half3 bump1 = UnpackNormal(tex2D( _BumpMap, i.bumpuv[0] )).rgb;
	half3 bump2 = UnpackNormal(tex2D( _BumpMap, i.bumpuv[1] )).rgb;
	half3 bump = (bump1 + bump2) * 0.5;

	half fresnel = dot( i.viewDir, bump );
	half4 water = tex2D( _ColorControl, float2(fresnel,fresnel) );

	if (_WaveRadius > 0.0)
	{
		float2 delta = i.worldXZ - _WaveCenter.xz;
		if (dot(delta, delta) > _WaveRadius * _WaveRadius)
			discard;
	}

	half4 col;
	col.rgb = lerp( water.rgb, _horizonColor.rgb, water.a );
	col.a = _horizonColor.a;

	// Blue in the troughs, red on the crests. Tint strength 0.25 leaves the
	// water about 75% visible.
	half t = saturate((i.height / max(_HeightMax, 0.001) + 1.0) * 0.5);
	half3 deep = half3(0.08, 0.28, 1.0);
	half3 high = half3(1.0, 0.06, 0.02);
	col.rgb = lerp(col.rgb, lerp(deep, high, t), _TintStrength);

	UNITY_APPLY_FOG(i.fogCoord, col);
	return col;
}
ENDCG
	}
}

}
