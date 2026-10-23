// RuneSkin HUD strip bar (mockup hud-4M3 bars, user call 2026-10-23 "do it on shaders"): one resolution-independent UI
// pass on a plain Image (no sprite: uv 0..1 over the rect), crisp at any bar height. UI/Default compatible (stencil
// masks, RectMask2D clip rect, alpha clip). Premultiplied output (Blend One OneMinusSrcAlpha).
//   _Mode 0 = the bar window under the well frame: glossy cylinder fill (_Fill share of the width, a straight cut end
//             with a thin lit rim, profile eyedropped from hud-4M3: bright highlight band near the top, saturated
//             body, darker towards the bottom), the damage-lag ghost (_Lag), the missing part as a grey cylinder,
//             the well's depth (inner shadow at the top and bottom) and the shield sheen over the fill (_Shield).
//   _Mode 1 = shield perimeter glow over the frame: a soft line along the rounded rect's outline (pill ends), lit
//             from the left up to _Shield of the width (top and bottom together), the right end at full.
// Every value is a material property set by BarView on change only (one material instance per bar); shape numbers
// come from theme tokens (share.hud.bar.fx.*).
Shader "RuneSkin/UI Bar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Mode ("Mode 0 fill, 1 glow", Float) = 0
        _Fill ("Fill share", Range(0,1)) = 1
        _Lag ("Lag share", Range(0,1)) = 1
        _Shield ("Shield share", Range(0,1)) = 0
        _Aspect ("Rect width / height", Float) = 20
        _FillColor ("Fill body", Color) = (0.86, 0.11, 0.13, 1)
        _LagColor ("Lag ghost", Color) = (0.95, 0.88, 0.82, 1)
        _MissingColor ("Missing (grey)", Color) = (0.42, 0.42, 0.42, 1)
        _SheenColor ("Shield sheen", Color) = (0.36, 0.91, 0.95, 0.22)
        _GlowColor ("Shield glow", Color) = (0.36, 0.91, 0.95, 1)
        _HiPos ("Highlight centre v", Float) = 0.78
        _HiWidth ("Highlight half width v", Float) = 0.12
        _HiAmount ("Highlight mix to white", Float) = 0.38
        _Bottom ("Bottom brightness", Float) = 0.55
        _Depth ("Well inner shadow", Float) = 0.45
        _Rim ("End rim brightness", Float) = 0.35
        _GlowWidth ("Glow width share of height", Float) = 0.16

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
            #pragma target 3.0
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
            float _Mode, _Fill, _Lag, _Shield, _Aspect, _HiPos, _HiWidth, _HiAmount, _Bottom, _Depth, _Rim, _GlowWidth;
            fixed4 _FillColor, _LagColor, _MissingColor, _SheenColor, _GlowColor;

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

            // Cylinder shading of a base colour at v (0 bottom .. 1 top): darker towards the bottom, a highlight band
            // mixed towards white near the top, a slightly darker top lip.
            float3 cylinder(float3 base, float v, float hiAmount)
            {
                float body = lerp(_Bottom, 1.0, smoothstep(0.0, 0.62, v));
                float3 c = base * body;
                float hi = exp(-pow((v - _HiPos) / max(0.01, _HiWidth), 2.0));
                c = lerp(c, lerp(base, float3(1, 1, 1), 0.55), hi * hiAmount);
                c *= lerp(1.0, 0.9, smoothstep(0.92, 1.0, v));
                return c;
            }

            // Signed distance to a pill (rounded rect, radius = half height) in height units.
            float pill(float2 p, float halfW, float halfH)
            {
                float r = halfH;
                float2 q = abs(p) - float2(halfW - r, halfH - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 fill(float2 uv)
            {
                float x = uv.x;
                float v = uv.y;
                float px = max(fwidth(x), 1e-5);
                float inFill = 1.0 - smoothstep(_Fill - px, _Fill + px, x);
                float inLag = (1.0 - smoothstep(_Lag - px, _Lag + px, x)) * (1.0 - inFill);
                float3 body = cylinder(_FillColor.rgb, v, _HiAmount);
                // straight-cut end (hud-4M3): the last ~14 px of the fill glow a little lighter, a thin dark seam at the
                // cut against the missing part
                float pxW = px;                          // one px in x
                float glowLen = 14.0 * pxW;
                float endGlow = smoothstep(_Fill - glowLen, _Fill - pxW, x) * inFill;
                body = lerp(body, saturate(body * 1.22 + 0.03), endGlow * _Rim);
                // shield sheen over the fill, from the left, shield share long
                float sh = (1.0 - smoothstep(_Shield - px, _Shield + px, x)) * step(0.0001, _Shield);
                body += _SheenColor.rgb * _SheenColor.a * sh * (0.6 + 0.8 * exp(-pow((v - _HiPos) / max(0.01, _HiWidth * 1.4), 2.0)));
                float3 lag = cylinder(_LagColor.rgb, v, _HiAmount * 0.5);
                // missing part: a dark hollow (Codex review: shadow under the top lip, faint bottom reflex), no cylinder
                float3 miss = _MissingColor.rgb * (1.0 - 0.45 * smoothstep(0.6, 1.0, v)) * (1.0 + 0.18 * (1.0 - smoothstep(0.0, 0.25, v)));
                float3 c = body * inFill + lag * inLag + miss * (1.0 - inFill - inLag);
                float seam = smoothstep(_Fill + 2.0 * pxW, _Fill, x) * (1.0 - inFill) * step(0.0005, _Fill) * step(_Fill, 0.9995);
                c *= 1.0 - 0.6 * seam;
                // well depth: a narrow inner shadow under the top rail and over the bottom one
                c *= 1.0 - _Depth * smoothstep(0.9, 1.0, v);
                c *= 1.0 - 0.6 * _Depth * (1.0 - smoothstep(0.0, 0.08, v));
                // capsule window (radius = half height): the fill ends round under the well's rounded ends, never as
                // rectangle corners poking past them (Codex review)
                float pxV = max(fwidth(v), 1e-5);
                float d = pill(float2((x - 0.5) * _Aspect, v - 0.5), 0.5 * _Aspect, 0.5);
                float mask = 1.0 - smoothstep(-pxV, pxV, d);
                return fixed4(c * mask, mask);
            }

            fixed4 glow(float2 uv)
            {
                float2 p = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5);
                float w = max(0.01, _GlowWidth);
                float d = pill(p, 0.5 * _Aspect - w, 0.5 - w);
                float g = exp(-abs(d) / (0.45 * w));
                float px = max(fwidth(uv.x), 1e-5);
                float lit = 1.0 - smoothstep(_Shield - 2.0 * px, _Shield + 2.0 * px, uv.x);
                lit = max(lit, step(0.995, _Shield));
                float a = g * lit * _GlowColor.a;
                return fixed4(_GlowColor.rgb * a, a * 0.85);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = _Mode < 0.5 ? fill(IN.uv) : glow(IN.uv);
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
