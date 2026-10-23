// Offline mockup of the HP bar with a shield (neon tube glow, RuneSkin/UI Bar mode 1 _Neon 1) composited into a real
// RuneSkin hud2 capture, with three shield-number layouts (A inline "+N", B glyph badge at the bar end, C stacked above).
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderShieldMock.Render -logFile <log>
// env: RSSHIELD_OUT (dir), RSSHIELD_BG (hud2 capture at the target resolution), RSSHIELD_SCALE (1 = 1080p, 1.3333 = 1440p),
//      RSSHIELD_ANIM (A/B/C = also render the 6 s pulse + absorb timeline frames for that layout, empty = stills only)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class RenderShieldMock
{
    // 1080p coordinates (top-left origin): crop of the HUD panel and the HP window, eyedropped from run 20261023-204838
    static readonly RectInt Crop = new RectInt(420, 885, 1080, 195);
    static readonly RectInt HpWin = new RectInt(657, 1003, 637, 21);
    const float HpMax = 200f, Outset = 18f, Caption = 26f;
    static readonly Color HpColor = Hex("#D81C22"), ShieldText = Hex("#A6F2FF"), Glow = Hex("#5BE8F2");

    static Material _fill, _glow;
    static Text _hp, _sh, _caption;
    static Image _glyph, _glowImg;
    static RectTransform _win;
    static float _s;

    public static void Render()
    {
        var mats = new List<Material>();
        var prevSpace = PlayerSettings.colorSpace;
        GameObject canvasGo = null, camGo = null;
        RenderTexture rt = null;
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RSSHIELD_OUT") ?? throw new Exception("RSSHIELD_OUT not set");
            var bgPath = Environment.GetEnvironmentVariable("RSSHIELD_BG") ?? throw new Exception("RSSHIELD_BG not set");
            _s = float.Parse(Environment.GetEnvironmentVariable("RSSHIELD_SCALE") ?? "1", CultureInfo.InvariantCulture);
            var anim = Environment.GetEnvironmentVariable("RSSHIELD_ANIM") ?? "";
            Directory.CreateDirectory(outDir);
            // render like the game: linear colour space with the bar's gamma-math fix
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Shader.SetGlobalFloat("_RuneSkinLinear", 1f);
            var shader = ImportShader();
            var font = ImportFont();
            var s = _s;

            var w = Even(Crop.width * s);
            var capH = Even(Caption * s);
            var h = Even(Crop.height * s) + capH;
            camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 1f);
            cam.orthographic = true;
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            cam.targetTexture = rt;
            canvasGo = new GameObject("canvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var root = (RectTransform)canvasGo.transform;

            var bgTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!bgTex.LoadImage(File.ReadAllBytes(bgPath))) throw new Exception("cannot load " + bgPath);
            var bgR = Child(root, "bg");
            bgR.offsetMax = new Vector2(0f, -capH);
            var bg = bgR.gameObject.AddComponent<RawImage>();
            bg.texture = bgTex;
            // the capture is at the target resolution: crop in its own pixels
            float cx = Mathf.Round(Crop.x * s), cy = Mathf.Round(Crop.y * s);
            bg.uvRect = new Rect(cx / bgTex.width, 1f - (cy + (h - capH)) / bgTex.height, (float)w / bgTex.width, (float)(h - capH) / bgTex.height);

            var cap = Child(root, "caption");
            cap.anchorMin = new Vector2(0f, 1f);
            cap.offsetMin = new Vector2(10f * s, -capH);
            _caption = MakeText(cap, font, Mathf.RoundToInt(16 * s), TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.85f));

            // HP window (px of the capture), the bar drawn by the shader over the captured one
            float wx = Mathf.Round(HpWin.x * s) - cx, wy = Mathf.Round(HpWin.y * s) - cy;
            float ww = Mathf.Round(HpWin.width * s), wh = Mathf.Round(HpWin.height * s);
            _win = Child(bgR, "hp");
            _win.anchorMin = _win.anchorMax = new Vector2(0f, 1f);
            _win.pivot = new Vector2(0f, 1f);
            _win.anchoredPosition = new Vector2(wx, -wy);
            _win.sizeDelta = new Vector2(ww, wh);
            var back = _win.gameObject.AddComponent<Image>();
            back.color = new Color(0.02f, 0.025f, 0.03f, 1f);
            var fillImg = Child(_win, "fill").gameObject.AddComponent<Image>();
            _fill = new Material(shader);
            mats.Add(_fill);
            _fill.SetFloat("_Mode", 0f);
            _fill.SetFloat("_Aspect", ww / wh);
            _fill.SetColor("_FillColor", HpColor);
            _fill.SetColor("_LagColor", Hex("#E9A79B"));
            _fill.SetColor("_MissingColor", Hex("#5A5A5A"));
            _fill.SetFloat("_HiPos", 0.72f);
            _fill.SetFloat("_HiWidth", 0.2f);
            _fill.SetFloat("_HiAmount", 0.3f);
            _fill.SetFloat("_Bottom", 0.55f);
            _fill.SetFloat("_Depth", 0.45f);
            _fill.SetFloat("_Rim", 1f);
            _fill.SetFloat("_Shield", 0f); // neon: the halo is the only light over the blood
            _fill.SetFloat("_Pool", 0f);
            _fill.SetFloat("_FxOn", 1f);
            _fill.SetFloat("_FxIntensity", 1f);
            _fill.SetFloat("_FxSpeed", 1f);
            fillImg.material = _fill;

            var o = Mathf.Round(Outset * s);
            var gr = Child(_win, "neon");
            gr.offsetMin = new Vector2(-o, -o);
            gr.offsetMax = new Vector2(o, o);
            _glowImg = gr.gameObject.AddComponent<Image>();
            _glow = new Material(shader);
            mats.Add(_glow);
            _glow.SetFloat("_Mode", 1f);
            _glow.SetFloat("_Neon", 1f);
            _glow.SetFloat("_Aspect", (ww + 2f * o) / (wh + 2f * o));
            _glow.SetFloat("_GlowWidth", o / (wh + 2f * o));
            _glow.SetColor("_GlowColor", Glow);
            _glowImg.material = _glow;

            _hp = MakeText(Child(_win, "hpValue"), font, Mathf.RoundToInt(21 * s), TextAnchor.MiddleCenter, Color.white);
            _hp.supportRichText = true;
            var shR = Child(_win, "shieldValue");
            _sh = MakeText(shR, font, Mathf.RoundToInt(17 * s), TextAnchor.MiddleRight, ShieldText);
            var glyphTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            glyphTex.LoadImage(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/sprites/glyph_sub_shield.png"))));
            var gR = Child(_win, "glyph");
            _glyph = gR.gameObject.AddComponent<Image>();
            _glyph.sprite = Sprite.Create(glyphTex, new Rect(0, 0, glyphTex.width, glyphTex.height), new Vector2(0.5f, 0.5f));
            _glyph.color = ShieldText;
            _glyph.preserveAspect = true;
            var gol = gR.gameObject.AddComponent<Outline>();
            gol.effectColor = new Color(0.03f, 0.02f, 0.02f, 0.9f);
            gol.effectDistance = new Vector2(1f * s, -1f * s);

            var states = new (string id, string label, float hp, float shield)[]
            {
                ("1-none", "no shield  (HP 142/200)", 142f, 0f),
                ("2-small", "small shield 20  (10 % of max HP)", 142f, 20f),
                ("3-main", "shield 80  (40 % of max HP)  - main state", 142f, 80f),
                ("4-over", "shield 220  (>= max HP: whole outline lit)", 142f, 220f),
                ("5-absorb", "absorbing: shield 80 -> 34 mid-hit (outline shrinking)", 142f, 34f),
            };
            foreach (var v in new[] { "A", "B", "C" })
                foreach (var st in states)
                {
                    Apply(v, st.hp, st.shield, 1.35f, -100f);
                    _caption.text = $"{v} - {st.label}";
                    Shoot(cam, rt, w, h, Path.Combine(outDir, $"{v}-{st.id}.png"));
                }
            if (anim != "")
            {
                var frameDir = Path.Combine(outDir, "frames-" + anim);
                Directory.CreateDirectory(frameDir);
                const float fps = 25f, dur = 7f;
                for (var i = 0; i < Mathf.RoundToInt(dur * fps); i++)
                {
                    var t = i / fps;
                    // shield 80 idle (pulse) -> hit at 2.5 s absorbed (80 -> 34) -> hit at 5.0 s breaks it (34 -> 0, HP 142 -> 120)
                    float shield = 80f, hp = 142f, hitAt = -100f;
                    if (t >= 2.5f) shield = Mathf.Lerp(80f, 34f, Smooth((t - 2.5f) / 0.35f));
                    if (t >= 5f) { shield = Mathf.Lerp(34f, 0f, Smooth((t - 5f) / 0.25f)); hp = Mathf.Lerp(142f, 120f, Smooth((t - 5.1f) / 0.1f)); hitAt = 5.1f; }
                    Apply(anim, hp, shield, t, hitAt);
                    _caption.text = $"{anim} - neon pulse + absorb  t={t:0.00}s";
                    Shoot(cam, rt, w, h, Path.Combine(frameDir, $"f{i:0000}.png"));
                }
            }
            Debug.Log($"RenderShieldMock: OK {outDir} {w}x{h}");
        }
        catch (Exception e)
        {
            Debug.LogError("RenderShieldMock: FAILED " + e);
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

    static void Apply(string variant, float hp, float shield, float t, float hitAt)
    {
        var s = _s;
        var share = Mathf.Clamp01(shield / HpMax);
        _fill.SetFloat("_FxClock", t);
        _fill.SetFloat("_Fill", hp / HpMax);
        _fill.SetFloat("_Lag", hp / HpMax);
        _fill.SetFloat("_HitAt", hitAt);
        _glow.SetFloat("_FxClock", t);
        _glow.SetFloat("_Shield", share);
        _glowImg.enabled = shield > 0.01f;
        var hpS = Mathf.CeilToInt(hp).ToString(CultureInfo.InvariantCulture);
        var shS = Mathf.CeilToInt(shield).ToString(CultureInfo.InvariantCulture);
        var on = shield > 0.01f;
        var hex = ColorUtility.ToHtmlStringRGB(ShieldText);
        var hpR = (RectTransform)_hp.transform;
        var shR = (RectTransform)_sh.transform;
        var gR = (RectTransform)_glyph.transform;
        hpR.anchorMin = Vector2.zero; hpR.anchorMax = Vector2.one; hpR.offsetMin = hpR.offsetMax = Vector2.zero;
        _sh.enabled = false;
        _glyph.enabled = false;
        var wh = _win.sizeDelta.y;
        switch (variant)
        {
            case "A": // inline after the HP value
                _hp.text = on ? $"{hpS} / {HpMax:0}  <color=#{hex}>+{shS}</color>" : $"{hpS} / {HpMax:0}";
                break;
            case "B": // glyph badge right-aligned at the bar's end, inside the window
            {
                _hp.text = hpS;
                if (!on) break;
                _sh.enabled = _glyph.enabled = true;
                _sh.fontSize = Mathf.RoundToInt(17 * s);
                _sh.alignment = TextAnchor.MiddleRight;
                var pad = 0.75f * wh;
                shR.anchorMin = Vector2.zero; shR.anchorMax = Vector2.one;
                shR.offsetMin = Vector2.zero; shR.offsetMax = new Vector2(-pad, 0f);
                _sh.text = shS;
                var tw = _sh.preferredWidth;
                var g = 0.9f * wh;
                gR.anchorMin = gR.anchorMax = new Vector2(1f, 0.5f);
                gR.pivot = new Vector2(1f, 0.5f);
                gR.sizeDelta = new Vector2(g, g);
                gR.anchoredPosition = new Vector2(-pad - tw - 3f * s, 0f);
                break;
            }
            case "C": // stacked: shield above the HP number, smaller, over the top rim
            {
                _hp.text = hpS;
                if (!on) break;
                _sh.enabled = true;
                _sh.fontSize = Mathf.RoundToInt(15 * s);
                _sh.alignment = TextAnchor.MiddleCenter;
                shR.anchorMin = new Vector2(0f, 1f); shR.anchorMax = Vector2.one;
                shR.offsetMin = new Vector2(0f, -2f * s); shR.offsetMax = new Vector2(0f, 14f * s);
                _sh.text = "+" + shS;
                break;
            }
        }
    }

    static Text MakeText(RectTransform r, Font font, int size, TextAnchor anchor, Color color)
    {
        var text = r.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var ol = text.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.03f, 0.02f, 0.02f, 0.95f);
        ol.effectDistance = new Vector2(1.4f * _s, -1.4f * _s);
        return text;
    }

    static float Smooth(float a) { a = Mathf.Clamp01(a); return a * a * (3f - 2f * a); }

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
