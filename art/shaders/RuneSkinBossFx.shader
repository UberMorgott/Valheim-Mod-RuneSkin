// RuneSkin boss bar effects (hud-design boss-c): one UI pass, no GrabPass, UI/Default compatible (stencil masks,
// RectMask2D clip rect, alpha clip). Drawn on a plain Image whose rect spans the bar channel plus a little glow room
// above and below (_BandMin.._BandMax = the channel in the rect's v). Premultiplied output (Blend One OneMinusSrcAlpha):
// colour adds light, alpha only darkens what it covers, so hot glow never blows the fill out to a flat wash.
//   _Mode 0 = gas haze: 2-octave domain-warped fBm smoke scrolling inside the drained fill, soft feathered end at
//             _Fill (smoothstep over _Feather of the width), soft top / bottom falloff with a thin glow past the
//             rails (_GlowOut), never a box.
//   _Mode 1 = weld: a narrow molten band at _Fill (_WeldWidth of the width, soft falloff into the fill, a sharper one
//             into the trough), flickering along v; _Flash (a hit, decays on the CPU) adds short-lived sparks.
// Colours are eyedropped from boss-c.png (gas row / red fill end); speed / intensity / widths come from theme tokens.
Shader "RuneSkin/UI Boss FX"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Mode ("Mode 0 gas, 1 weld", Float) = 0
        _Fill ("Fill", Range(0,1)) = 1
        _Feather ("Fill end feather share of width", Float) = 0.14
        _BandMin ("Channel v min", Float) = 0.3
        _BandMax ("Channel v max", Float) = 0.7
        _GlowOut ("Glow past the channel v", Float) = 0.45
        _Aspect ("Rect width / height", Float) = 20
        _Scale ("Noise scale cells per height", Float) = 1.6
        _Speed ("Scroll speed", Float) = 0.35
        _Intensity ("Intensity", Float) = 1.4
        _GasDeep ("Gas deep", Color) = (0.20, 0.05, 0.42, 1)
        _GasMid ("Gas mid", Color) = (0.55, 0.24, 0.98, 1)
        _GasHot ("Gas hot", Color) = (0.88, 0.70, 1.0, 1)
        _WeldWidth ("Weld bloom share of width", Float) = 0.15
        _WeldCore ("Weld core", Color) = (1.0, 0.78, 0.76, 1)
        _WeldGlow ("Weld glow", Color) = (1.0, 0.4, 0.4, 1)
        _Flash ("Hit flash 0 to 1", Float) = 0
        _TimeOffset ("Time offset offline preview frames", Float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _Mode, _Fill, _Feather, _BandMin, _BandMax, _GlowOut, _Aspect, _Scale, _Speed, _Intensity;
            fixed4 _GasDeep, _GasMid, _GasHot, _WeldCore, _WeldGlow;
            float _WeldWidth, _Flash, _TimeOffset;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 2 octaves
            float fbm(float2 p)
            {
                return 0.65 * vnoise(p) + 0.35 * vnoise(p * 2.03 + 17.1);
            }

            // 0 inside the channel band, distance (in v) outside it
            float bandDist(float v)
            {
                return max(max(_BandMin - v, v - _BandMax), 0.0);
            }

            fixed4 gas(float2 uv, float t)
            {
                // billowing clouds: isotropic, large-scale, twice domain-warped fBm (puffs, not bands), slowly rolling
                float2 p = float2(uv.x * _Aspect, uv.y) * _Scale;
                float2 q = float2(fbm(p * 0.7 + float2(t * 0.35, -t * 0.12)), fbm(p * 0.7 + float2(3.7, 1.9) - float2(t * 0.2, t * 0.3)));
                float2 r = float2(fbm(p + 3.0 * q + float2(1.7, 9.2) + t * 0.15), fbm(p + 3.0 * q + float2(8.3, 2.8) - t * 0.12));
                float n = fbm(p + 2.5 * r);
                // the same field a little lower: its difference lights the puffs' upper rims (soft rolling edges)
                float nLow = fbm(p + 2.5 * r + float2(0.0, -0.22));
                float puff = smoothstep(0.38, 0.78, n);
                float rim = saturate((n - nLow) * 5.0 + 0.35);
                // the fill end dissolves into the puffs: a noise-shifted edge, feathered past the fill into the trough
                float edge = _Fill + (n - 0.5) * _Feather;
                float h = smoothstep(0.0, 0.015, uv.x) * (1.0 - smoothstep(edge - _Feather * 0.3, edge + _Feather, uv.x));
                float past = smoothstep(_Fill - _Feather * 0.03, _Fill + _Feather * 0.12, uv.x); // the smoke starts AT the end: the fill stays bright and crisp
                float d = bandDist(uv.y);
                float inBand = d <= 0.0 ? 1.0 : 0.0;
                float endZone = smoothstep(_Fill - 0.06, _Fill, uv.x) * (1.0 - smoothstep(_Fill + _Feather * 0.5, _Fill + _Feather * 1.1, uv.x));
                // inside the channel: soft light over the fill, real cloud past its end
                float lightA = saturate(puff * h * _Intensity) * 0.04 * inBand * (1.0 - past);
                // past the end a denser smoke body (the mockup's dark violet cloud filling the drained trough)
                float smokeA = saturate(smoothstep(0.22, 0.68, n) * 1.4 * h * past * _Intensity) * inBand;
                // past the rails: puffs rolling off the fill end, a faint haze along the top of the drained bar
                float fall = (1.0 - inBand) * (1.0 - smoothstep(0.0, _GlowOut, d));
                // a thick cloud rolling up / down off the fill end (may soften past the rails, as in boss-c)
                float fallC = (1.0 - inBand) * (1.0 - smoothstep(0.0, _GlowOut * 0.7, d));
                float cloudA = saturate(smoothstep(0.3, 0.72, n) * fallC * (0.03 + 1.1 * endZone) * _Intensity);
                float3 deep = _GasDeep.rgb;
                float3 lit = lerp(_GasMid.rgb, _GasHot.rgb, rim * rim);
                // tiny violet sparkles drifting in the smoke past the end (the mockup's particles)
                float2 sg = float2(uv.x * _Aspect * 14.0, uv.y * 14.0 - t * 0.8);
                float2 sc = floor(sg);
                float sp = step(0.9, hash21(sc)) * smoothstep(0.16, 0.0, length(frac(sg) - (float2(hash21(sc + 2.3), hash21(sc + 5.9)) * 0.7 + 0.15))) * past * (inBand + fall) * h;
                float3 col = lerp(_GasMid.rgb, _GasHot.rgb, rim * 0.5) * lightA
                           + lerp(deep * 0.9, _GasMid.rgb, 0.2 + 0.35 * rim) * (smokeA + cloudA) + _GasHot.rgb * sp * 1.4;
                float alpha = lightA * 0.2 + smokeA * 0.85 + cloudA * 0.7 + sp * 0.3;
                return fixed4(col, alpha);
            }
            fixed4 weld(float2 uv, float t)
            {
                // Only the fill's warming toward the hot edge (pink, last ~_WeldWidth of the bar); the crackling edge and
                // the spark spray are the Codex flipbook (sprite.fx.weld.*) played on the edge.
                float w = max(_WeldWidth, 1e-4);
                // a jagged, crackling end: the edge wanders along v (two frequencies), the ramp stops hard at it
                float jag = (vnoise(float2(uv.y * 24.0, t * 3.0)) - 0.5) * 0.012 + (vnoise(float2(uv.y * 61.0, t * 7.0)) - 0.5) * 0.006;
                float x = uv.x - _Fill - jag;
                float inBand = bandDist(uv.y) <= 0.0 ? 1.0 : 0.0;
                float ramp = x <= 0.0 ? pow(saturate(1.0 + x / (w * 1.35)), 1.6) : 0.0;
                float3 warm = lerp(_WeldGlow.rgb, _WeldCore.rgb, pow(ramp, 3.0));
                // keep the glossy cylinder through the warming: strongest in the highlight band, the top / bottom stay dark
                float tb = saturate((uv.y - _BandMin) / max(1e-4, _BandMax - _BandMin));
                float cyl = 0.35 + 0.65 * pow(sin(3.14159 * saturate(tb * 1.1 - 0.02)), 1.5);
                ramp *= cyl;
                return fixed4(warm * ramp * 1.2 * inBand * _Intensity, ramp * 0.55 * inBand * _Intensity);
            }
            fixed4 frag(v2f IN) : SV_Target
            {
                float now = _Time.y + _TimeOffset;
                fixed4 c = _Mode < 0.5 ? gas(IN.uv, now * _Speed) : weld(IN.uv, now);
                c *= IN.color.a;
                c.rgb *= IN.color.rgb;
                #ifdef UNITY_UI_CLIP_RECT
                c *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
