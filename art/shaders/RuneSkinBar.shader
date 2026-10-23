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
        // Neon shield (mode 1, _Neon 1): _GlowWidth is then the outline's inset from the quad edge (share of the quad
        // height) = the quad outset around the bar window, so the bloom has room outside the window.
        _Neon ("Neon tube glow 0/1", Float) = 0
        _NeonCore ("Neon core half width, share of window height", Float) = 0.05
        _NeonBloom ("Neon widest bloom, share of window height", Float) = 0.6
        _NeonPower ("Neon brightness", Float) = 1
        // Living fills (barfx): procedural shader-math only (no textures), all off at _FxOn 0 / _Pool < 0.
        _Pool ("Pool: -1 plain, 0 blood, 1 energy, 2 arcane", Float) = -1
        _FxOn ("Fx on", Float) = 1
        _FxIntensity ("Fx intensity", Float) = 1
        _FxSpeed ("Fx speed", Float) = 1
        _FxClock ("Clock override (<0 = global _RuneSkinFxClock)", Float) = -1
        _HitAt ("Damage event clock", Float) = -100
        _HitSize ("Damage event size share", Float) = 0.2
        _HealAt ("Heal event clock", Float) = -100
        _PulseAt ("Pulse event clock", Float) = -100
        _Regen ("Regenerating 0..1", Float) = 0
        _Exhaust ("Exhausted 0..1", Float) = 0
        _ClotColor ("Clotted lost segment", Color) = (0.30, 0.035, 0.045, 1)

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
            fixed4 _FillColor, _LagColor, _MissingColor, _SheenColor, _GlowColor, _ClotColor;
            float _RuneSkinFxClock;   // global shader clock, written once per frame by BarFxConfig.TickClock
            float _Pool, _FxOn, _FxIntensity, _FxSpeed, _FxClock, _HitAt, _HitSize, _HealAt, _PulseAt, _Regen, _Exhaust;
            float _RuneSkinLinear;    // global, 1 when the game renders in linear colour space (BarFxConfig.TickClock)
            float _Neon, _NeonCore, _NeonBloom, _NeonPower;

            // The living fills were designed and approved (v3) in gamma space. In a linear-space game (Valheim) their
            // colour math runs on gamma-encoded inputs and frag linearises the result, so in-game matches the renders
            // (in-game run 20261023-161531: without this every darkening factor read lighter -> washed, flat flow).
            // The plain fill (effects off, adrenaline) and the shield glow keep their tuned behaviour.
            bool GammaMath() { return _RuneSkinLinear > 0.5 && _FxOn * _FxIntensity > 0.0 && _Pool > -0.5 && _Mode < 0.5; }
            float3 G(float3 c) { return GammaMath() ? LinearToGammaSpace(c) : c; }

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

            // ---- living fills (barfx): value noise / fbm in shader math, no textures, no grab pass ----
            // P = position in bar-height units (x * aspect, v) so every feature keeps its shape at any bar size.
            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float s = 0.5 * vnoise(p);
                p = p * 2.03 + float2(17.1, 9.2);
                s += 0.25 * vnoise(p);
                p = p * 2.01 + float2(5.3, 31.7);
                s += 0.125 * vnoise(p);
                return s / 0.875;
            }

            // Twinkling star points on a jittered grid (cell in height units); share = lit share of the cells.
            float sparkle(float2 P, float cell, float share, float t, float rate)
            {
                float2 g = P / cell;
                float2 id = floor(g);
                float h = hash21(id + 7.7);
                float2 c = float2(hash21(id + 3.1), hash21(id + 5.9)) * 0.6 + 0.2;
                float2 d = (frac(g) - c) * cell;
                float life = frac(t * rate + h * 13.0);
                float tw = smoothstep(0.0, 0.12, life) * (1.0 - smoothstep(0.2, 0.5, life));
                float star = exp(-length(d) / 0.05)
                    + 0.6 * exp(-abs(d.x) / 0.03 - abs(d.y) / 0.16)
                    + 0.6 * exp(-abs(d.y) / 0.03 - abs(d.x) / 0.16);
                return star * tw * step(h, share);
            }

            // Arcane crackle: a jagged filament re-drawn ~11 times a second, present in about two of three draws.
            float arcFilament(float2 P, float v, float tf)
            {
                float seg = floor(tf * 11.0);
                float y = 0.5 + 0.9 * (fbm(float2(P.x * 2.8, seg * 1.7)) - 0.5);
                return exp(-abs(v - y) / 0.035) * step(0.35, hash21(float2(seg, 2.0)));
            }

            // Rune glints: rare short angular strokes (one of four rune angles, 0.10-0.16 bar heights) with a bright
            // point at their centre, flashing briefly on a coarse grid.
            float runeGlint(float2 P, float t)
            {
                const float cell = 0.9;
                float2 g = P / cell;
                float2 id = floor(g);
                float h = hash21(id + 7.7);
                float2 c = float2(hash21(id + 3.1), hash21(id + 5.9)) * 0.6 + 0.2;
                float2 d = (frac(g) - c) * cell;
                float a = (floor(hash21(id + 1.9) * 4.0) + 0.5) * 0.785398 + 0.35;
                float2 dir = float2(cos(a), sin(a));
                float len = 0.05 + 0.03 * hash21(id + 9.4);
                float along = dot(d, dir);
                float across = dot(d, float2(-dir.y, dir.x));
                float stroke = exp(-abs(across) / 0.022) * (1.0 - smoothstep(len - 0.02, len, abs(along)));
                float life = frac(t * 0.45 + h * 13.0);
                float tw = smoothstep(0.0, 0.08, life) * (1.0 - smoothstep(0.15, 0.35, life));
                return (stroke + 0.8 * exp(-length(d) / 0.03)) * tw * step(h, 0.22);
            }

            float3 arcGlow() { return lerp(G(_FillColor.rgb), float3(0.92, 0.84, 1.0), 0.2); }

            // Slow drips running down from the top lip (one candidate per 1.7 bar heights, ~35 % used, 10-20 s each):
            // a tapering trail above a rounded head. Returns 0..1 coverage.
            float drip(float2 P, float v, float tf)
            {
                const float cell = 1.7;
                float id = floor(P.x / cell);
                float h = hash21(float2(id, 4.2));
                float cx = (id + 0.25 + 0.5 * h) * cell;
                float life = frac(tf * (0.05 + 0.05 * h) + h * 7.0);
                float head = 1.02 - life * 1.25;
                float wdt = 0.05 + 0.03 * h;
                float dx = abs(P.x - cx);
                float above = saturate((v - head) * 3.0);
                float trail = exp(-dx / (wdt * (0.5 + 0.5 * saturate(1.0 - (v - head))))) * step(head, v) * (0.35 + 0.65 * above) * exp(-(v - head) / 0.3);
                float bulb = exp(-pow(length(float2(dx, (v - head) * 0.8)) / (wdt * 1.6), 2.0));
                return saturate(max(trail * 0.8, bulb)) * step(h, 0.35) * (1.0 - smoothstep(0.85, 1.0, life));
            }

            // Tiny darker particles suspended in the blood (sparse jittered grid, ~1 px at 1080p).
            float motes(float2 P)
            {
                const float cell = 0.22;
                float2 g = P / cell;
                float2 id = floor(g);
                float h = hash21(id + 2.3);
                float2 c = float2(hash21(id + 8.1), hash21(id + 6.6)) * 0.6 + 0.2;
                float d = length((frac(g) - c) * cell);
                return exp(-pow(d / (0.03 + 0.02 * hash21(id + 4.7)), 2.0)) * step(h, 0.065);
            }

            // HP = blood: deep desaturated crimson over a near-black base (no glossy cylinder), slow domain-warped flow,
            // dark coagulated streaks, slow drips, suspended particles, translucent red under the top lip, a soft wet
            // sheen, a coagulated band at the fill end with a thin translucent meniscus rim, a flash + slosh on damage,
            // a warm wave on heal. Intensity blends from the plain cylinder (0) to the full look (1).
            float3 bloodBody(float2 P, float v, float x, float dF, float tf, float hitAge, float healAge, float I)
            {
                float Is = saturate(I);
                float slosh = exp(-max(hitAge, 0.0) * 2.2) * step(0.0, hitAge);
                float wave = (0.03 * sin(P.x * 0.9 - tf * 0.6) + 0.02 * sin(P.x * 2.1 + tf * 0.4))
                    + slosh * 0.10 * sin(P.x * 2.6 - hitAge * 9.0) * exp(-max(dF, 0.0) * 0.15);
                float vs = saturate(v - wave * Is);
                float lumC = dot(G(_FillColor.rgb), float3(0.3, 0.59, 0.11));
                float3 crimson = lerp(G(_FillColor.rgb), lumC.xxx, 0.22) * float3(0.70, 0.52, 0.56);
                float3 black = float3(0.075, 0.008, 0.013);
                float2 q = P * float2(0.30, 1.0);
                // two-axis domain warp + a finer, differently stretched layer: irregular, non-repeating flow
                float2 wv = float2(fbm(q * 0.9 + float2(-tf * 0.05, 1.7)), fbm(q * 1.3 + float2(4.1, tf * 0.03))) - 0.5;
                float n = fbm(q * 1.7 + wv * 2.2 + float2(-tf * 0.07, 0.0));
                n = lerp(n, vnoise(P * float2(0.9, 2.2) + wv * 1.5 + float2(-tf * 0.11, 0.0)), 0.35);
                float depth = (0.12 + 0.88 * smoothstep(0.0, 0.78, vs)) * (0.72 + 0.5 * n);
                float3 c = lerp(black, crimson, saturate(depth));
                float st = fbm(P * float2(0.6, 3.4) + float2(-tf * 0.04, 5.0));
                c *= 1.0 - 0.27 * smoothstep(0.58, 0.78, st) * smoothstep(0.45, 0.7, vnoise(P * float2(0.8, 1.2) + float2(-tf * 0.03, 9.0)));
                c *= 1.0 - 0.35 * drip(P, v, tf);
                c *= 1.0 - 0.15 * motes(P + float2(-tf * 0.06, sin(tf * 0.3) * 0.05));
                c += G(_FillColor.rgb) * 0.30 * exp(-pow((vs - 0.88) / 0.08, 2.0)) * (0.7 + 0.6 * n);
                float gl = vnoise(float2(P.x * 0.5 - tf * 0.2, 1.7));
                c += float3(1.0, 0.45, 0.40) * exp(-pow((vs - _HiPos - 0.06) / 0.12, 2.0)) * (0.06 + 0.10 * gl);
                float clot = 1.0 - smoothstep(0.0, 1.8, dF);
                float grain = fbm(P * float2(3.0, 4.0) + 11.0);
                c = lerp(c, G(_ClotColor.rgb) * (0.55 + 0.5 * grain) * (0.35 + 0.65 * smoothstep(0.0, 0.8, vs)), clot * clot * 0.9);
                c += G(_FillColor.rgb) * 0.7 * exp(-max(dF, 0.0) / 0.05) * (0.7 + 0.8 * slosh) * (0.4 + 0.6 * smoothstep(0.0, 0.6, vs));
                c += float3(0.32, 0.06, 0.04) * exp(-max(hitAge, 0.0) * 7.0) * step(0.0, hitAge) * exp(-max(dF, 0.0) / 0.5);
                float heal = exp(-pow((x - healAge * 0.9) * _Aspect / 1.6, 2.0)) * step(0.0, healAge) * (1.0 - smoothstep(1.1, 1.5, healAge));
                c += float3(0.95, 0.30, 0.22) * heal * 0.5;
                c *= 1.0 + 0.25 * step(0.0, healAge) * exp(-max(healAge, 0.0) * 2.0);
                return lerp(cylinder(G(_FillColor.rgb), v, _HiAmount), c, Is);
            }

            // The lost-HP (lag) segment of the blood bar: dark clotted blood that drains as _Lag eases down.
            float3 clotBody(float2 P, float v, float tf)
            {
                float g = fbm(P * float2(2.2, 3.0) + float2(-tf * 0.05, 0.0));
                return G(_ClotColor.rgb) * float3(0.62, 0.55, 0.5) * (0.6 + 0.7 * g) * (0.3 + 0.7 * smoothstep(0.0, 0.8, v)) + G(_ClotColor.rgb) * 0.5 * exp(-pow((v - _HiPos - 0.06) / 0.10, 2.0));
            }

            // Damage splash: four droplets thrown from the new fill end into the missing part, gone within ~0.55 s.
            float splash(float2 P, float frontX, float hitAge)
            {
                if (hitAge < 0.0 || hitAge > 0.6) return 0.0;
                float acc = 0.0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float h1 = hash21(float2(k, 1.3)), h2 = hash21(float2(k, 7.9)), h3 = hash21(float2(k, 4.4));
                    float vx = 1.0 + 3.0 * h1;
                    float vy = (h2 - 0.5) * 2.6;
                    float2 pos = float2(frontX + vx * hitAge - 0.6 * vx * hitAge * hitAge,
                        0.5 + (h3 - 0.5) * 0.5 + vy * hitAge - 1.6 * hitAge * hitAge);
                    float r = 0.05 + 0.06 * h3;
                    acc += exp(-pow(length((P - pos) * float2(0.8, 1.0)) / r, 2.0));
                }
                return acc * (1.0 - smoothstep(0.15, 0.55, hitAge));
            }

            // Stamina = liquid energy: amber/gold fluid, fast flow + ripples, caustic streaks, a lit front, sparkles
            // while regenerating; exhausted = dimmed, desaturated, flickering.
            float3 energyBody(float2 P, float v, float dF, float tf, float I)
            {
                // fast flow towards the end: streaks stretched along the bar, soft ripple fronts running with them
                float2 q = P * float2(0.38, 2.0);
                float n = fbm(q * 1.4 + float2(-tf * 1.2, sin(tf * 0.7) * 0.3));
                float n2 = fbm(q * 2.2 + float2(-tf * 2.0 + n * 0.9, tf * 0.3));
                float rip = 0.5 + 0.5 * sin(P.x * 2.4 - tf * 6.0 + n * 3.0);
                float3 amber = G(_FillColor.rgb) * float3(0.95, 0.70, 0.30);
                float3 gold = lerp(G(_FillColor.rgb), float3(1.0, 0.95, 0.70), 0.22);
                float3 base = lerp(G(_FillColor.rgb), lerp(amber, gold, saturate(n * 1.2 + rip * 0.25 - 0.3)), 0.7 * saturate(I));
                float wave = 0.04 * sin(P.x * 2.2 - tf * 4.0) * I;
                float3 c = cylinder(base, saturate(v - wave), _HiAmount);
                c += gold * smoothstep(0.58, 0.85, n2) * 0.18 * I;
                c += float3(1.0, 0.97, 0.85) * exp(-pow((v - _HiPos - wave) / 0.05, 2.0)) * smoothstep(0.4, 0.8, rip) * 0.30 * I;
                c += gold * exp(-max(dF, 0.0) / 0.35) * 0.35 * I;
                float sp = sparkle(P + float2(-tf * 0.6, 0.0), 0.42, 0.35, tf, 1.3);
                c += float3(1.0, 0.95, 0.75) * sp * saturate(_Regen) * (0.35 + 0.65 * exp(-max(dF, 0.0) / 3.0)) * 1.2 * I;
                float lum = dot(c, float3(0.3, 0.59, 0.11));
                float flick = 0.78 + 0.22 * step(0.45, vnoise(float2(tf * 9.0, 2.0)));
                c = lerp(c, lum * float3(0.80, 0.62, 0.42) * 0.55 * flick, saturate(_Exhaust) * step(0.001, I));
                return c;
            }

            // Eitr = concentrated energy: dense glowing violet with a bright core line, pulsing veins, rare rune
            // glints, crackle at the fill end; glows rather than flows. A pulse event sends a ring back from the end.
            float3 arcaneBody(float2 P, float v, float dF, float tf, float pulseAge, float I)
            {
                float breathe = 0.5 + 0.5 * sin(tf * 2.1);
                float n = fbm(P * float2(0.8, 1.6) + float2(tf * 0.07, -tf * 0.04));
                float vn = fbm(P * float2(1.3, 2.4) + float2(n * 1.8, tf * 0.10));
                // veins only in slowly wandering patches, so the bar reads as dense glow, not a lightning net
                float patch = smoothstep(0.5, 0.72, vnoise(P * float2(0.35, 0.8) + float2(tf * 0.05, 3.0)));
                float veins = pow(saturate(1.0 - abs(vn * 2.0 - 1.0)), 14.0) * patch;
                float3 deep = G(_FillColor.rgb) * float3(0.30, 0.22, 0.48);
                float3 base = lerp(G(_FillColor.rgb), lerp(deep, G(_FillColor.rgb), 0.35 + 0.45 * n), 0.8 * saturate(I));
                float3 c = cylinder(base, v, _HiAmount * lerp(1.0, 0.45, saturate(I)));
                float3 glowC = arcGlow();
                float core = exp(-pow((v - 0.5) / 0.12, 2.0));
                float halo = exp(-pow((v - 0.5) / 0.38, 2.0));
                c += G(_FillColor.rgb) * halo * (0.22 + 0.12 * breathe) * I;
                c += glowC * core * (0.22 + 0.12 * breathe) * (0.8 + 0.4 * n) * I;
                c += glowC * veins * (0.35 + 0.65 * breathe) * 0.45 * I;
                c += float3(0.9, 0.8, 1.0) * runeGlint(P, tf) * 0.9 * I;
                c += glowC * arcFilament(P, v, tf) * (1.0 - smoothstep(0.0, 1.4, dF)) * 0.65 * I;
                float pa = max(pulseAge, 0.0);
                float on = step(0.0, pulseAge);
                c += glowC * exp(-pow((dF - pa * 9.0) / 0.6, 2.0)) * exp(-pa * 1.6) * on * 0.35 * I;
                c *= 1.0 + 0.15 * exp(-pa * 3.0) * on * I;
                return c;
            }

            fixed4 fill(float2 uv)
            {
                float x = uv.x;
                float v = uv.y;
                float px = max(fwidth(x), 1e-5);
                // shared clock (BarFxConfig.TickClock: seconds since the HUD started, unwrapped, events use the same
                // clock); _FxClock >= 0 overrides it for offline renders
                float tr = _FxClock >= 0.0 ? _FxClock : _RuneSkinFxClock;
                float tf = tr * _FxSpeed;
                float I = _FxOn * _FxIntensity * step(-0.5, _Pool);
                float hitAge = tr - _HitAt, healAge = tr - _HealAt, pulseAge = tr - _PulseAt;
                float2 P = float2(x * _Aspect, v);
                float d0 = pill(float2((x - 0.5) * _Aspect, v - 0.5), 0.5 * _Aspect, 0.5);
                float live = step(0.001, _Fill) * step(_Fill, 0.999);
                // living end: blood meniscus wobbles (sloshes after a hit), energy ripples, arcane stays straight
                float slosh = exp(-max(hitAge, 0.0) * 2.2) * step(0.0, hitAge);
                float wob = 0.0;
                if (_Pool < 0.5) wob = 0.22 * (1.0 - (2.0 * v - 1.0) * (2.0 * v - 1.0)) - 0.11 + (0.04 * sin(v * 4.0 + tf * 1.5) + 0.03 * sin(tf * 1.1)) + 0.12 * slosh * sin(v * 6.0 - hitAge * 14.0);
                else if (_Pool < 1.5) wob = 0.05 * sin(v * 9.0 - tf * 8.0);
                wob *= saturate(I) * live;
                float fillE = _Fill + wob / _Aspect;
                float inFill = 1.0 - smoothstep(fillE - px, fillE + px, x);
                float inLag = (1.0 - smoothstep(_Lag - px, _Lag + px, x)) * (1.0 - inFill);
                float dF = (fillE - x) * _Aspect;
                // the noisy bodies run only where they are seen (the fill / lag share); the grey part stays cheap
                float3 body = G(_FillColor.rgb);
                [branch] if (inFill <= 0.0) { }
                else if (I <= 0.0)
                {
                    // plain glossy cylinder (adrenaline, fx off): straight-cut end, the last ~14 px a little lighter
                    body = cylinder(G(_FillColor.rgb), v, _HiAmount);
                    float endGlow = smoothstep(_Fill - 14.0 * px, _Fill - px, x) * inFill;
                    body = lerp(body, saturate(body * 1.22 + 0.03), endGlow * _Rim);
                }
                else if (_Pool < 0.5) body = bloodBody(P, v, x, dF, tf, hitAge, healAge, I);
                else if (_Pool < 1.5) body = energyBody(P, v, dF, tf, I);
                else body = arcaneBody(P, v, dF, tf, pulseAge, I);
                // shield sheen over the fill, from the left, shield share long
                float sh = (1.0 - smoothstep(_Shield - px, _Shield + px, x)) * step(0.0001, _Shield);
                // blood: the sheen narrows to a thin line under the top lip (a broad cyan wash greys dark blood)
                float bloodSheen = step(_Pool, 0.5) * step(0.0001, I);
                float sheenProfile = lerp(0.6 + 0.8 * exp(-pow((v - _HiPos) / max(0.01, _HiWidth * 1.4), 2.0)),
                    0.5 * exp(-pow((v - 0.88) / 0.035, 2.0)), bloodSheen);
                body += G(_SheenColor.rgb) * _SheenColor.a * sh * sheenProfile;
                float3 lag = G(_LagColor.rgb);
                [branch] if (inLag > 0.0)
                {
                    lag = cylinder(G(_LagColor.rgb), v, _HiAmount * 0.5);
                    if (I > 0.0 && _Pool < 0.5) lag = lerp(lag, clotBody(P, v, tf), saturate(I));
                }
                // missing part: a dark hollow (Codex review: shadow under the top lip, faint bottom reflex), no cylinder
                float3 miss = G(_MissingColor.rgb) * (1.0 - 0.45 * smoothstep(0.6, 1.0, v)) * (1.0 + 0.18 * (1.0 - smoothstep(0.0, 0.25, v)));
                float3 c = body * inFill + lag * inLag + miss * (1.0 - inFill - inLag);
                [branch] if (I > 0.0 && live > 0.0)
                {
                    float outside = 1.0 - inFill;
                    if (_Pool < 0.5)
                    {
                        float drop = saturate(splash(P, fillE * _Aspect, hitAge) * saturate(I));
                        c = lerp(c, cylinder(G(_FillColor.rgb) * 1.1, v, _HiAmount * 1.5), drop * outside);
                    }
                    else if (_Pool > 1.5 && dF > -1.5)
                    {
                        float o = max(-dF, 0.0);
                        c += arcGlow() * (0.35 * exp(-o / 0.25) + 0.9 * arcFilament(P, v, tf) * exp(-o / 0.3)) * I * outside;
                    }
                }
                float seam = (1.0 - smoothstep(fillE, fillE + 2.0 * px, x)) * (1.0 - inFill) * live;
                c *= 1.0 - 0.6 * seam;
                // well depth: a narrow inner shadow under the top rail and over the bottom one
                c *= 1.0 - _Depth * smoothstep(0.9, 1.0, v);
                c *= 1.0 - 0.6 * _Depth * (1.0 - smoothstep(0.0, 0.08, v));
                // capsule window (radius = half height): the fill ends round under the well's rounded ends, never as
                // rectangle corners poking past them (Codex review)
                float pxV = max(fwidth(d0), 1e-5);
                float d = d0;
                float mask = 1.0 - smoothstep(-pxV, pxV, d);
                return fixed4(c * mask, mask);
            }

            fixed4 glow(float2 uv)
            {
                float2 p = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5);
                float w = max(0.01, _GlowWidth);
                float d = pill(p, 0.5 * _Aspect - w, 0.5 - w);
                // outline: soft halo outwards, a sharp falloff inwards (the fill under the shield keeps its own tone)
                float g = exp(-abs(d) / ((d > 0.0 ? 0.45 : 0.12) * w));
                float px = max(fwidth(uv.x), 1e-5);
                float lit = 1.0 - smoothstep(_Shield - 2.0 * px, _Shield + 2.0 * px, uv.x);
                lit = max(lit, step(0.995, _Shield));
                float a = g * lit * _GlowColor.a;
                return fixed4(_GlowColor.rgb * a, d > 0.0 ? a * 0.85 : 0.0);   // inside: additive only, never darkens
            }

            // Neon tube shield (user call 2026-10-23 "like neon light in the dark"): a thin near-white core line on the
            // window's pill outline + three gaussian bloom layers of saturated shield colour (wide outside over the
            // metal, narrower inside over the fill), additive (alpha only under the core, so the line stays crisp over
            // bright blood). Lit from the left up to _Shield of the WINDOW width (top and bottom together), the right
            // cap at full; the last ~1.5 window heights before the shield end burn brighter, the tip blooms past it.
            // Slow pulse + rare buzz flicker + a faint highlight travelling along the lit run. Single pass, no grab
            // pass, no texture: 1 pill SDF + ~8 exp + 1 value noise per pixel of the quad.
            fixed4 neon(float2 uv)
            {
                float w = clamp(_GlowWidth, 0.0, 0.45);
                float hw = 0.5 * _Aspect - w;               // window half width, quad-height units
                float winH = 1.0 - 2.0 * w;                 // window height, quad-height units
                float2 p = float2((uv.x - 0.5) * _Aspect, uv.y - 0.5);
                float d = pill(p, hw, 0.5 * winH);          // signed distance to the window outline
                float px = max(fwidth(p.y), 1e-5);
                float tr = _FxClock >= 0.0 ? _FxClock : _RuneSkinFxClock;
                // along the window: share 0..1, distance past the shield end in quad-height units
                float xw = saturate((p.x + hw) / (2.0 * hw));
                float full = step(0.995, _Shield);
                float e = (xw - _Shield) * 2.0 * hw * (1.0 - full) - full * 10.0;
                float on = step(0.0001, _Shield);
                float lineLit = (1.0 - smoothstep(-px, px, e)) * on;
                float tipLit = exp(-pow(max(e, 0.0) / (0.5 * winH), 2.0)) * on;
                float hot = exp(-max(-e, 0.0) / (1.5 * winH)) * (1.0 - full);
                // time: slow breathing + a rare short buzz dip (neon flicker) + travelling highlight
                float pulse = 0.88 + 0.12 * sin(tr * 2.3);
                float buzz = 1.0 - 0.22 * step(0.82, vnoise(float2(tr * 13.0, 3.7))) * step(0.6, vnoise(float2(tr * 0.7, 8.1)));
                float runLen = max(_Shield, 0.02) * 2.0 * hw;
                float travelPos = frac(tr * 0.22) * (runLen + 2.0) - 1.0;
                float trav = exp(-pow((xw * 2.0 * hw - travelPos) / (0.9 * winH), 2.0));
                // profile across the tube
                float core = exp(-pow(d / max(_NeonCore * winH, 0.8 * px), 2.0));
                float b = _NeonBloom * winH;
                float k = d > 0.0 ? 1.0 : 0.45;             // inward (over the fill) blooms narrower
                float bl1 = exp(-pow(d / (0.18 * b * k), 2.0));
                float bl2 = exp(-pow(d / (0.45 * b * k), 2.0));
                float bl3 = exp(-pow(d / (b * k), 2.0));
                float inner = d > 0.0 ? 1.0 : 0.6;
                float3 sat = normalize(max(_GlowColor.rgb, 1e-3)) * 1.25;                 // saturated tube colour
                sat = lerp(_GlowColor.rgb, sat, 0.6);
                float3 white = lerp(_GlowColor.rgb, float3(1, 1, 1), 0.72);
                float flick = pulse * buzz;
                float lit = lineLit * (1.0 + 1.3 * hot + 0.45 * trav);
                float3 c = white * core * lit * (0.9 + 0.1 * flick)
                    + sat * (1.0 * bl1 + 0.6 * bl2 * inner + 0.32 * bl3 * inner) * flick * (1.0 + 1.1 * hot + 0.35 * trav * lineLit) * (lineLit + tipLit * (1.0 - lineLit));
                c *= _NeonPower * _GlowColor.a;
                float a = saturate(core * lineLit * 0.55);
                return fixed4(c, a);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = _Mode < 0.5 ? fill(IN.uv) : (_Neon > 0.5 ? neon(IN.uv) : glow(IN.uv));
                if (GammaMath()) { float pa = max(c.a, 1e-4); c.rgb = GammaToLinearSpace(saturate(c.rgb / pa)) * pa; }
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
