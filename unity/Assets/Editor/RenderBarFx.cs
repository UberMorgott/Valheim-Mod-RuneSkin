// Animated offline mockup of the living bar fills (RuneSkin/UI Bar, _Pool blood / energy / arcane) inside the hud-4M3
// strip: the mockup crop is the backdrop, the three bar windows (HP 944 x 31, stamina / eitr 461 x 27 at 1080p, eyedropped
// from hud-4M3) are redrawn by the shader, the values in PT Sans Bold with a dark outline on top. A scripted 12 s timeline
// (25 fps) walks full -> half -> low with the events: HP hit (splash, clotted lost segment draining), HP heal (wave),
// stamina drained to exhaustion (flicker) then regenerating (sparkles), eitr spend pulses. Frames are rendered with an
// explicit _FxClock, so the result is deterministic.
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderBarFx.Render -logFile <log>
// env: RSBARFX_OUT (dir), RSBARFX_BG (hud-4M3.png, 1920 x 1080), RSBARFX_SCALE (1 = 1080p, 1.3333 = 1440p),
//      RSBARFX_FRAMES (optional, 0 = stills only), RSBARFX_NOSHIELD (1 = shield forced off, A/B tone check),
//      RSBARFX_LINEAR (1 = linear colour space like the game with the gamma-math fix, 2 = linear without it)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class RenderBarFx
{
    // crop of hud-4M3 (top-left origin, 1080p px) and the bar windows inside the full mockup
    static readonly RectInt Crop = new RectInt(440, 866, 1040, 140);
    static readonly RectInt HpWin = new RectInt(488, 898, 944, 31);
    static readonly RectInt StWin = new RectInt(488, 962, 461, 27);
    static readonly RectInt EiWin = new RectInt(971, 962, 461, 27);
    const float Fps = 25f, Duration = 12f;
    const float HpMax = 157f, StMax = 112f, EiMax = 100f;

    // theme tokens (default-theme.json, stripscale 0.19.x)
    static readonly Color HpColor = Hex("#D81C22"), StColor = Hex("#EEA510"), EiColor = Hex("#8E3CF0");

    class Bar
    {
        public Material Mat;
        public Text Value;
        public float Max;
        public Material Glow;
        public Image GlowImage;
    }

    public static void Render()
    {
        var mats = new List<Material>();
        var prevSpace = PlayerSettings.colorSpace;
        GameObject canvasGo = null, camGo = null;
        RenderTexture rt = null;
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RSBARFX_OUT") ?? throw new Exception("RSBARFX_OUT not set");
            var bgPath = Environment.GetEnvironmentVariable("RSBARFX_BG") ?? throw new Exception("RSBARFX_BG not set");
            var s = float.Parse(Environment.GetEnvironmentVariable("RSBARFX_SCALE") ?? "1", CultureInfo.InvariantCulture);
            var framesEnv = Environment.GetEnvironmentVariable("RSBARFX_FRAMES");
            var animate = framesEnv != "0";
            Directory.CreateDirectory(outDir);
            // RSBARFX_LINEAR=1 / 2: render as Valheim does (linear colour space); 1 = the gamma-math fix on, 2 = off
            // (reproduces the washed in-game look of run 20261023-161531). The project setting is restored after.
            var linearMode = Environment.GetEnvironmentVariable("RSBARFX_LINEAR") ?? "0";
            prevSpace = PlayerSettings.colorSpace;
            if (linearMode != "0") PlayerSettings.colorSpace = ColorSpace.Linear;
            Shader.SetGlobalFloat("_RuneSkinLinear", linearMode == "1" ? 1f : 0f);
            var shader = ImportShader();
            var font = ImportFont();

            var w = Even(Crop.width * s);
            var h = Even(Crop.height * s);
            camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            cam.targetTexture = rt;
            canvasGo = new GameObject("canvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvas.referencePixelsPerUnit = 50f;
            var root = (RectTransform)canvasGo.transform;

            var bgTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!bgTex.LoadImage(File.ReadAllBytes(bgPath))) throw new Exception("cannot load " + bgPath);
            var bg = Child(root, "bg").gameObject.AddComponent<RawImage>();
            CleanShield(bgTex);
            bg.texture = bgTex;
            bg.uvRect = new Rect((float)Crop.x / bgTex.width, 1f - (float)(Crop.y + Crop.height) / bgTex.height,
                (float)Crop.width / bgTex.width, (float)Crop.height / bgTex.height);

            var hp = MakeBar(root, shader, font, mats, HpWin, s, HpColor, 0f, HpMax, 25);
            hp.Glow = MakeGlow(hp, shader, mats, HpWin, s);
            var st = MakeBar(root, shader, font, mats, StWin, s, StColor, 1f, StMax, 20);
            var ei = MakeBar(root, shader, font, mats, EiWin, s, EiColor, 2f, EiMax, 20);

            var stills = new (string name, float t)[]
            {
                ("s1-full-idle", 1.0f), ("s2-hp-hit-splash", 2.12f), ("s3-hp-clot-draining", 3.0f), ("s4-eitr-pulse-half", 4.12f),
                ("s5-stamina-exhausted", 5.6f), ("s6-hp-heal-wave", 6.85f), ("s7-stamina-regen-sparkle", 8.6f),
                ("s8-hp-hit-low", 9.15f), ("s9-low-states", 11.6f),
            };
            foreach (var (name, t) in stills)
            {
                Apply(t, hp, st, ei);
                Shoot(cam, rt, w, h, Path.Combine(outDir, name + ".png"));
            }
            if (animate)
            {
                var frameDir = Path.Combine(outDir, "frames");
                Directory.CreateDirectory(frameDir);
                var n = Mathf.RoundToInt(Duration * Fps);
                for (var i = 0; i < n; i++)
                {
                    Apply(i / Fps, hp, st, ei);
                    Shoot(cam, rt, w, h, Path.Combine(frameDir, $"f{i:0000}.png"));
                }
            }
            Debug.Log($"RenderBarFx: OK {outDir} {w}x{h}");
        }
        catch (Exception e)
        {
            Debug.LogError("RenderBarFx: FAILED " + e);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (PlayerSettings.colorSpace != prevSpace) { PlayerSettings.colorSpace = prevSpace; AssetDatabase.SaveAssets(); }
            foreach (var m in mats) UnityEngine.Object.DestroyImmediate(m);
            if (camGo != null) camGo.GetComponent<Camera>().targetTexture = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (canvasGo != null) UnityEngine.Object.DestroyImmediate(canvasGo);
            if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    // The scripted timeline: values + event clocks at time t (s).
    static void Apply(float t, Bar hp, Bar st, Bar ei)
    {
        // HP: 0.9; hit at 2.0 -> 0.62 (lag holds 0.6 s, drains by 3.6); heal from 6.5 -> 0.8 in 0.6 s; hit at 9.0 -> 0.2
        float hpFill, hpLag, hitAt = -100f, healAt = -100f;
        if (t < 2f) { hpFill = 0.9f; hpLag = 0.9f; }
        else if (t < 6.5f) { hpFill = 0.62f; hpLag = Drain(0.9f, 0.62f, t - 2f); hitAt = 2f; }
        else if (t < 9f) { hpFill = Mathf.Lerp(0.62f, 0.8f, Smooth((t - 6.5f) / 0.6f)); hpLag = hpFill; hitAt = 2f; healAt = 6.5f; }
        else { hpFill = 0.2f; hpLag = Drain(0.8f, 0.2f, t - 9f); hitAt = 9f; healAt = 6.5f; }
        Set(hp, t, hpFill, hpLag);
        hp.Mat.SetFloat("_HitAt", hitAt);
        hp.Mat.SetFloat("_HealAt", healAt);
        // shield (BarView ShieldRun share): absorbs part of each hit, gone after the second
        var shield = t < 2f ? 0.55f : t < 9f ? 0.25f : 0f;
        if (Environment.GetEnvironmentVariable("RSBARFX_NOSHIELD") == "1") shield = 0f; // A/B for the under-shield tone check
        hp.Mat.SetFloat("_Shield", shield);
        hp.Glow.SetFloat("_Shield", shield);
        hp.GlowImage.enabled = shield > 0f;

        // stamina: full, sprint drain 3..5 s, exhausted 5..6.8 s, regenerating from 6.8 s
        float stFill, exhaust = 0f, regen = 0f;
        if (t < 3f) stFill = 1f;
        else if (t < 5f) stFill = 1f - (t - 3f) / 2f;
        else if (t < 6.2f) { stFill = 0.02f + (t - 5f) * 0.1f; exhaust = 1f; } // refills dimmed + flickering while exhausted
        else { stFill = Mathf.Min(0.65f, 0.14f + (t - 6.2f) * 0.09f); regen = 1f; exhaust = Mathf.Clamp01(1f - (t - 6.2f) / 0.3f); }
        Set(st, t, stFill, stFill);
        st.Mat.SetFloat("_Exhaust", exhaust);
        st.Mat.SetFloat("_Regen", regen);

        // eitr: 0.85, spend at 4.0 -> 0.5 (pulse), spend at 8.0 -> 0.12 (pulse)
        float eiFill, pulseAt = -100f;
        if (t < 4f) eiFill = 0.85f;
        else if (t < 8f) { eiFill = Mathf.Lerp(0.85f, 0.5f, Smooth((t - 4f) / 0.25f)); pulseAt = 4f; }
        else { eiFill = Mathf.Lerp(0.5f, 0.12f, Smooth((t - 8f) / 0.25f)); pulseAt = 8f; }
        Set(ei, t, eiFill, eiFill);
        ei.Mat.SetFloat("_PulseAt", pulseAt);
    }

    // lag ghost like BarView: holds 0.6 s, then eases down over ~1 s
    static float Drain(float from, float to, float age) => age < 0.6f ? from : Mathf.Lerp(from, to, Smooth((age - 0.6f) / 1.0f));

    static float Smooth(float a) { a = Mathf.Clamp01(a); return a * a * (3f - 2f * a); }

    static void Set(Bar b, float t, float fill, float lag)
    {
        b.Mat.SetFloat("_FxClock", t);
        b.Mat.SetFloat("_Fill", fill);
        b.Mat.SetFloat("_Lag", Mathf.Max(fill, lag));
        b.Value.text = Mathf.RoundToInt(fill * b.Max).ToString(CultureInfo.InvariantCulture);
    }

    static Bar MakeBar(RectTransform root, Shader shader, Font font, List<Material> mats, RectInt win, float s, Color color, float pool, float max, int fontPx)
    {
        var r = Child(root, "bar");
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2((win.x - Crop.x) * s, -(win.y - Crop.y) * s);
        r.sizeDelta = new Vector2(win.width * s, win.height * s);
        var back = r.gameObject.AddComponent<Image>(); // covers the painted fill outside the capsule corners
        back.color = new Color(0.02f, 0.025f, 0.03f, 1f);
        var img = Child(r, "fill").gameObject.AddComponent<Image>();
        var m = new Material(shader);
        mats.Add(m);
        // BarView.UseShader values (theme share.hud.bar.fx.*)
        m.SetFloat("_Mode", 0f);
        m.SetFloat("_Aspect", (float)win.width / win.height);
        m.SetColor("_FillColor", color);
        m.SetColor("_LagColor", Hex("#E9A79B"));
        m.SetColor("_MissingColor", Hex("#5A5A5A"));
        m.SetFloat("_HiPos", 0.72f);
        m.SetFloat("_HiWidth", 0.2f);
        m.SetFloat("_HiAmount", 0.3f);
        m.SetFloat("_Bottom", 0.55f);
        m.SetFloat("_Depth", 0.45f);
        m.SetFloat("_Rim", 1f);
        m.SetFloat("_Shield", 0f);
        m.SetColor("_SheenColor", Hex("#5BE8F238"));
        m.SetFloat("_Pool", pool);
        m.SetFloat("_FxOn", 1f);
        m.SetFloat("_FxIntensity", 1f);
        m.SetFloat("_FxSpeed", 1f);
        img.material = m;
        img.raycastTarget = false;
        var text = Child(r, "value").gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = Mathf.RoundToInt(fontPx * s);
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var ol = text.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.03f, 0.02f, 0.02f, 0.95f);
        ol.effectDistance = new Vector2(1.4f * s, -1.4f * s);
        return new Bar { Mat = m, Value = text, Max = max };
    }

    // The shield perimeter glow exactly as BarView.FxLayout builds it: mode 1 of the same shader on a quad = the bar
    // grown by size.hud.strip4.shield.out (3) on every side, drawn over the frame, under the value.
    static Material MakeGlow(Bar bar, Shader shader, List<Material> mats, RectInt win, float s)
    {
        var parent = (RectTransform)bar.Value.transform.parent;
        var r = Child(parent, "shieldGlow");
        var o = 3f * s;
        r.offsetMin = new Vector2(-o, -o);
        r.offsetMax = new Vector2(o, o);
        r.SetSiblingIndex(bar.Value.transform.GetSiblingIndex());
        var img = r.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        var m = new Material(shader);
        mats.Add(m);
        m.SetFloat("_Mode", 1f);
        m.SetFloat("_Aspect", (win.width * s + 2f * o) / (win.height * s + 2f * o));
        m.SetColor("_GlowColor", Hex("#5BE8F2"));
        m.SetFloat("_GlowWidth", 0.16f);
        img.material = m;
        bar.GlowImage = img;
        return m;
    }

    // The mockup has the shield line painted around the HP well: turn its cyan pixels into the steel rail colour so
    // the only shield in the render is the shader's (follows the HP shield value).
    static void CleanShield(Texture2D tex)
    {
        var px = tex.GetPixels32();
        for (var y = 876; y <= 948; y++)
        for (var x = 466; x <= 1448; x++)
        {
            var i = (tex.height - 1 - y) * tex.width + x;
            var c = px[i];
            if ((c.g + c.b) / 2 - c.r < 30 || c.b < 40) continue;
            var lum = Mathf.Max(c.g, c.b) / 255f;
            px[i] = new Color32((byte)(13 + lum * 26), (byte)(21 + lum * 34), (byte)(25 + lum * 40), 255);
        }
        tex.SetPixels32(px);
        tex.Apply();
    }

    static Shader ImportShader()
    {
        const string dst = "Assets/RuneSkin/Shaders/RuneSkinBar.shader";
        var src = Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/shaders/RuneSkinBar.shader"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst)));
        File.Copy(src, Path.GetFullPath(dst), true);
        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(dst) ?? throw new Exception("shader import failed");
        if (ShaderUtil.ShaderHasError(sh))
        {
            foreach (var m in ShaderUtil.GetShaderMessages(sh)) Debug.LogError($"shader: {m.message} line {m.line}");
            throw new Exception("RuneSkinBar.shader has errors");
        }
        return sh;
    }

    static Font ImportFont()
    {
        const string dst = "Assets/RuneSkin/Fonts/Source/PT_Sans-Web-Bold.ttf";
        if (!File.Exists(Path.GetFullPath(dst)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst)));
            File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/fonts/PT_Sans-Web-Bold.ttf")), Path.GetFullPath(dst), true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
        }
        return AssetDatabase.LoadAssetAtPath<Font>(dst) ?? throw new Exception("font import failed");
    }

    static void Shoot(Camera cam, RenderTexture rt, int w, int h, string path)
    {
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static RectTransform Child(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
    }

    static int Even(float v) => Mathf.RoundToInt(v / 2f) * 2;

    static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}
