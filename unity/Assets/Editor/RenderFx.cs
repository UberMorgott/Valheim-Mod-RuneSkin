// Offline preview of the boss-c bar and the enemy-b tier bars as the game draws them, for same-scale side-by-sides
// against the mockups before any game launch. Sprites come from the BUILT bundle's atlas (mipmapped, sharp bias, like the
// game), fills from the BossAwakening strips (mipmapped like BossBarAssets), effects from the RuneSkin/UI Boss FX shader.
// Layout = Vaettir's themed world bar (BossAwakening UI\BossBarHud.cs: LoadTheme / ThemeFrame / BuildCap / BuildMarker /
// BuildPlate / BuildFx / BuildFlare maths) and RuneSkin's enemy frame fit (EnemyHudMath.Fit).
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderFx.Render -logFile <log>
// env: RSFX_OUT (output dir), RSFX_STRIPS (the BossAwakening UI\Resources dir with the fill strips).
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

public static class RenderFx
{
    const float BaseU = 0.09f, BaseV = 0.325f, WindowU = 0.82f, WindowV = 0.35f;
    static SpriteAtlas _atlas;

    public static void Render()
    {
        AssetBundle bundle = null;
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RSFX_OUT") ?? throw new Exception("RSFX_OUT not set");
            var stripDir = Environment.GetEnvironmentVariable("RSFX_STRIPS") ?? throw new Exception("RSFX_STRIPS not set");
            Directory.CreateDirectory(outDir);
            AssetBundle.UnloadAllAssetBundles(true);
            bundle = AssetBundle.LoadFromFile(Path.GetFullPath("Build/runeskin")) ?? throw new Exception("bundle missing (run BuildBundles first)");
            _atlas = bundle.LoadAllAssets<SpriteAtlas>()[0];
            var shader = bundle.LoadAsset<Shader>("RuneSkinBossFx") ?? throw new Exception("shader missing in bundle");
            var blood = Png(Path.Combine(stripDir, "bar_blood_c.png"));
            var gasTex = Png(Path.Combine(stripDir, "bar_gas_c.png"));
            var empty = Png(Path.Combine(stripDir, "bar_empty_c.png"));
            // body widths of Vaettir's bar at 1080p / 1440p (span 0.64 of the screen width)
            foreach (var (res, body) in new[] { ("1080", 960f), ("1440", 1280f) })
            {
                foreach (var bg in new[] { "bright", "dark" })
                {
                    Boss(outDir, $"boss-red-{res}-{bg}", shader, blood, empty, bg, body, false, 0.7f, 0.7f, 0f, 0f);
                    Boss(outDir, $"boss-full-{res}-{bg}", shader, blood, empty, bg, body, false, 1f, 1f, 0f, 0f);
                    Boss(outDir, $"boss-hit-{res}-{bg}", shader, blood, empty, bg, body, false, 0.55f, 0.7f, 1f, 0.05f);
                    Boss(outDir, $"boss-gas-{res}-{bg}", shader, gasTex, empty, bg, body, true, 0.62f, 0.62f, 0f, 0.5f);
                }
                Enemies(outDir, $"enemy-tiers-{res}", res == "1080" ? 1f : 4f / 3f);
            }
            Debug.Log("RenderFx: OK " + outDir);
        }
        catch (Exception e)
        {
            Debug.LogError("RenderFx: FAILED " + e);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (bundle != null) bundle.Unload(true);
        }
    }

    static Texture2D Png(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, mipMapBias = -0.5f };
        if (!t.LoadImage(File.ReadAllBytes(path))) throw new Exception("cannot load " + path);
        return t;
    }

    static Sprite S(string name)
    {
        // the atlas in an editor-loaded bundle has no packed texture: use the imported source sprite (same mips / bias)
        var s = _atlas.GetSprite(name);
        if (s == null || s.texture == null)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"Assets/RuneSkin/Sprites/{name}.png"))
                if (o is Sprite src) s = src;
        if (s == null || s.texture == null) throw new Exception("sprite missing " + name);
        return s;
    }

    static (Camera cam, RenderTexture rt, GameObject canvasGo, GameObject camGo) Stage(int w, int h, string bg)
    {
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg == "bright" ? new Color(0.86f, 0.9f, 0.95f) : new Color(0.12f, 0.14f, 0.18f);
        cam.orthographic = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt;
        var canvasGo = new GameObject("canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        canvas.referencePixelsPerUnit = 50f; // Valheim's canvases
        return (cam, rt, canvasGo, camGo);
    }

    static void Shoot(string outDir, string name, Camera cam, RenderTexture rt, int w, int h)
    {
        Canvas.ForceUpdateCanvases();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void Boss(string outDir, string name, Shader shader, Texture2D fillTex, Texture2D emptyTex, string bg, float body, bool gas,
        float hp, float trail, float flash, float time)
    {
        var w = Mathf.RoundToInt(body * 1.45f);
        var h = Mathf.RoundToInt(body * 0.22f);
        var (cam, rt, canvasGo, camGo) = Stage(w, h, bg);
        const float refPpu = 50f;
        var mats = new System.Collections.Generic.List<Material>();
        try
        {
            var root = Rect("bar", canvasGo.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.4f);

            var frameSprite = S("boss_frame");
            var cap = S("boss_cap");
            var plaque = S("boss_plaque");
            var marker = S("boss_marker");
            var flareSprite = S("boss_flare");
            var barH = body / 29.4f; // theme share.boss.bar.aspect
            var b = frameSprite.border;
            var toRef = 100f / frameSprite.pixelsPerUnit;
            var window = Mathf.Max(1f, (frameSprite.rect.height - b.y - b.w) * toRef);
            var k = barH / window;
            var capOffset = b.w * toRef * k;
            var capH = barH * 2.9f; // theme size.boss.cap.h
            var capW = capH * cap.rect.width / cap.rect.height;
            var inset = Mathf.Max(0f, capOffset - (1f - 0.9f) * capW); // theme share.boss.cap.inner
            root.sizeDelta = new Vector2(body, barH);

            var back = Img(root, "back", null, new Color(0.05f, 0.02f, 0.02f, 0.92f));
            back.rectTransform.offsetMin = new Vector2(-2f, -2f);
            back.rectTransform.offsetMax = new Vector2(2f, 2f);
            var channel = Rect("channel", root);
            channel.offsetMin = new Vector2(inset, 0f);
            channel.offsetMax = new Vector2(-inset, 0f);
            Raw(channel, "empty", emptyTex, 0f, 1f, 1f);
            if (!gas && trail > hp)
            {
                var chip = Img(channel, "chip", null, new Color(0.55f, 0.07f, 0.07f, 0.6f)); // boss-c ghost
                chip.rectTransform.anchorMin = new Vector2(hp, 0f);
                chip.rectTransform.anchorMax = new Vector2(trail, 1f);
            }
            var fillMask = Rect("fillMask", channel);
            fillMask.anchorMax = new Vector2(hp, 1f);
            var rm = fillMask.gameObject.AddComponent<RectMask2D>();
            if (gas) { var soft = Mathf.RoundToInt(barH * 0.5f); rm.softness = new Vector2Int(soft, 0); rm.padding = new Vector4(-soft, 0f, 0f, 0f); }
            Raw(fillMask, "fill", fillTex, 0f, 1f, hp);

            var frame = Img(root, "frame", frameSprite, Color.white);
            frame.type = Image.Type.Tiled;
            frame.fillCenter = false;
            frame.pixelsPerUnitMultiplier = refPpu / 100f / k;
            frame.rectTransform.offsetMin = new Vector2(-b.x * toRef * k, -b.y * toRef * k);
            frame.rectTransform.offsetMax = new Vector2(b.z * toRef * k, b.w * toRef * k);

            // FX over the frame: theme fx.room 1, fx.gas.* / fx.weld.* defaults
            const float room = 1.6f;
            var fxRoot = Rect("fxRoot", root);
            fxRoot.offsetMin = new Vector2(inset, 0f);
            fxRoot.offsetMax = new Vector2(-inset, 0f);
            var fx = Img(fxRoot, "fx", null, Color.white);
            fx.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fx.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            fx.rectTransform.sizeDelta = new Vector2(0f, barH * (1f + 2f * room));
            var mat = new Material(shader);
            mats.Add(mat);
            mat.SetFloat("_Mode", gas ? 0f : 1f);
            mat.SetFloat("_Fill", hp);
            mat.SetFloat("_BandMin", room / (1f + 2f * room));
            mat.SetFloat("_BandMax", (1f + room) / (1f + 2f * room));
            mat.SetFloat("_Aspect", (body - 2f * inset) / (barH * (1f + 2f * room)));
            mat.SetFloat("_Flash", flash);
            mat.SetFloat("_Feather", 0.14f);
            mat.SetFloat("_GlowOut", 0.36f);
            mat.SetFloat("_WeldWidth", 0.2f);
            mat.SetFloat("_Intensity", 1.4f);
            mat.SetFloat("_Speed", 0.35f);
            mat.SetFloat("_Scale", 1.6f);
            mat.SetFloat("_TimeOffset", time);
            fx.material = mat;

            // the Codex flipbooks (frame 0) on the fill edge, as BossAwakening plays them
            fx.enabled = !gas && hp < 0.995f; // gas: the flipbook replaces the shader smoke
            if (!gas && hp < 0.995f)
            {
                var wf = S("fx_weld_0");
                var fl = Img(fxRoot, "weld", wf, Color.white);
                var add = Shader.Find("Legacy Shaders/Particles/Additive");
                if (add != null) { var am = new Material(add); am.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.8f)); mats.Add(am); fl.material = am; }
                var ch = barH * 1.3f * 512f / 320f * (1f + 0.25f * flash); // theme fx.weld.h
                var r = fl.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(hp, 0.5f);
                r.pivot = new Vector2(150f / 384f, 0.5f);
                r.sizeDelta = new Vector2(ch * 384f / 512f * 1.6f, ch); // theme fx.weld.w: the spray reaches further right
            }
            if (gas)
            {
                var gf = S("fx_gasend_0");
                var gi = Img(fxRoot, "gasend", gf, Color.white);
                var gh = barH * 4.5f;
                var r = gi.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(hp, 0.5f);
                r.pivot = new Vector2(0.28f, 0.32f);
                r.sizeDelta = new Vector2(gh * 0.75f, gh); // cell 384x512, theme fx.gasend.w 1
                gi.color = new Color(1f, 1f, 1f, 1f);
                var add3 = Shader.Find("Legacy Shaders/Particles/Additive");
                if (add3 != null) { var am3 = new Material(add3); am3.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.8f)); mats.Add(am3); gi.material = am3; }

                // the bright lilac end of the gas fill: the same shader ramp in lilac (Vaettir's gas edge fx)
                var ge = Img(fxRoot, "gasedge", null, Color.white);
                ge.rectTransform.anchorMin = new Vector2(0f, 0.5f); ge.rectTransform.anchorMax = new Vector2(1f, 0.5f);
                ge.rectTransform.sizeDelta = new Vector2(0f, barH * (1f + 2f * room));
                var gm = new Material(mat); mats.Add(gm);
                gm.SetFloat("_Mode", 1f); gm.SetFloat("_WeldWidth", 0.05f); gm.SetFloat("_Intensity", 1.4f);
                gm.SetColor("_WeldGlow", new Color(0.62f, 0.3f, 1f)); gm.SetColor("_WeldCore", new Color(0.82f, 0.62f, 1f));
                ge.material = gm;
            }

            // fangs over the rails (Vaettir builds its marks group after the frame)
            var marks = Rect("marks", root);
            marks.offsetMin = new Vector2(inset, 0f);
            marks.offsetMax = new Vector2(-inset, 0f);
            for (var m = 1; m < 3; m++)
            {
                var mk = Img(marks, "marker" + m, marker, Color.white);
                var r = mk.rectTransform;
                var mh = barH * 1.6f; // theme size.boss.marker.h
                r.anchorMin = r.anchorMax = new Vector2(m / 3f, 1f);
                r.pivot = new Vector2(0.5f, 0.78f);
                r.sizeDelta = new Vector2(mh * 0.27f, mh);
            }
            for (var side = 0; side < 2; side++)
            {
                var c = Img(root, "cap" + side, cap, Color.white);
                c.preserveAspect = true;
                var r = c.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(side, 0.5f);
                r.pivot = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(capW, capH);
                r.anchoredPosition = new Vector2(side == 0 ? capOffset : -capOffset, 0f);
                if (side == 1) r.localScale = new Vector3(-1f, 1f, 1f);
            }
            var ph = barH * 2.7f; // theme share.boss.plaque.h
            var pl = Img(root, "plaque", plaque, Color.white);
            pl.preserveAspect = true;
            var pr = pl.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 0f);
            pr.sizeDelta = new Vector2(ph * plaque.rect.width / plaque.rect.height, ph);
            var nameRect = Rect("name", pr);
            nameRect.anchorMin = new Vector2(0.2f, 0.18f);
            nameRect.anchorMax = new Vector2(0.79f, 0.72f);
            var text = nameRect.gameObject.AddComponent<Text>();
            // stand-in serif (the game draws Valheim-Norsebold, not available to the editor) with the engraving look
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/RuneSkin/Fonts/Source/Cormorant-Bold.otf") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var sh = nameRect.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(0f, -2f);
            var ol = nameRect.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.04f, 0.05f, 0.06f, 1f);
            ol.effectDistance = new Vector2(1f, -1f);
            text.text = "THE ELDER";
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = 200;
            text.color = new Color(0.8f, 0.82f, 0.84f);
            fxRoot.SetAsLastSibling(); // effects over the plaque / caps, like Vaettir's effect books
            Shoot(outDir, name, cam, rt, w, h);
        }
        finally
        {
            foreach (var m in mats) UnityEngine.Object.DestroyImmediate(m);
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(canvasGo);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    // The four enemy-b tiers at their in-game size (Health 150 x 15 canvas units, x scale for 1440p), fill 70 %, lag 10 %.
    static void Enemies(string outDir, string name, float s)
    {
        var w = Mathf.RoundToInt(420 * s);
        var h = Mathf.RoundToInt(260 * s);
        var (cam, rt, canvasGo, camGo) = Stage(w, h, "dark");
        const float refPpu = 50f;
        try
        {
            var tiers = new[] { "normal", "magic", "rare", "unique" };
            for (var i = 0; i < 4; i++)
            {
                var health = Rect("health" + i, canvasGo.transform);
                health.anchorMin = health.anchorMax = new Vector2(0.5f, 1f - (i + 0.5f) / 4f);
                health.sizeDelta = new Vector2(150f * s, 15f * s);
                var trough = Img(health, "trough", S("bar_enemy_trough_b"), Color.white);
                var lag = Img(health, "lag", S("bar_enemy_lag_b"), Color.white);
                lag.rectTransform.anchorMax = new Vector2(0.8f, 1f);
                var fill = Img(health, "fill", S("bar_enemy_fill_b"), Color.white);
                fill.rectTransform.anchorMax = new Vector2(0.7f, 1f);
                var sp = S("frame_enemy_" + tiers[i]);
                var b = sp.border;
                var toRef = 100f / sp.pixelsPerUnit;
                var k = health.sizeDelta.y / Mathf.Max(1f, (sp.rect.height - b.y - b.w) * toRef);
                var frame = Img(health, "frame", sp, Color.white);
                frame.type = Image.Type.Sliced;
                frame.fillCenter = false;
                frame.pixelsPerUnitMultiplier = refPpu / 100f / k;
                frame.rectTransform.offsetMin = new Vector2(-b.x * toRef * k, -b.y * toRef * k);
                frame.rectTransform.offsetMax = new Vector2(b.z * toRef * k, b.w * toRef * k);
            }
            Shoot(outDir, name, cam, rt, w, h);
        }
        finally
        {
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(canvasGo);
            UnityEngine.Object.DestroyImmediate(camGo);
        }
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        return r;
    }

    static Image Img(RectTransform parent, string name, Sprite sprite, Color color)
    {
        var r = Rect(name, parent);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    // A fill strip over [start, start + width] of its parent, its window sampled like Vaettir's Place (no drift).
    static void Raw(RectTransform parent, string name, Texture2D tex, float start, float width, float uvShare)
    {
        var r = Rect(name, parent);
        r.anchorMin = new Vector2(start, 0f);
        r.anchorMax = new Vector2(start + width, 1f);
        var raw = r.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.uvRect = new Rect(BaseU, BaseV, WindowU * uvShare, WindowV);
        raw.raycastTarget = false;
    }
}
