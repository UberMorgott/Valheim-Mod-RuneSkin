using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // boss-c on vanilla's boss template (m_baseHudBoss): every boss-flagged creature that is not Vaettir's awakened world
    // boss (vanilla bosses without Vaettir, EpicLoot bounty targets, a second boss, other mods' bosses) gets the same bar
    // as Vaettir's world bar (UI\BossBarHud.cs): tiled rail frame, spear caps, plaque with the name in its field, fang
    // markers at the thirds, the mockup fill / trough strips, the weld on the HP edge, and the same screen share
    // (Hud.BossBarWidth, spear tip to spear tip of a 16:9 box). Applied per boss HUD clone (a creature with IsBoss(), the
    // only creatures vanilla builds from m_baseHudBoss), never on the template itself: Vaettir's Hybrid target panel is a
    // copy of that template (HudModes.BuildPanel) and drew an aimed Greydwarf as a boss-c bar when the template carried it.
    public static partial class EnemyHudSkin
    {
        public const string BossCapName = "RuneSkin_BossCap";
        public const string BossWeldName = "RuneSkin_BossWeld";
        public const string BossFlareName = "RuneSkin_BossFlare";

        private static bool _bossC; // every boss-c sprite is in the theme
        private static float _bossSpan;
        private static Sprite _bossWeld;

        private static readonly int FxMode = Shader.PropertyToID("_Mode"), FxFill = Shader.PropertyToID("_Fill"),
            FxBandMin = Shader.PropertyToID("_BandMin"), FxBandMax = Shader.PropertyToID("_BandMax"), FxAspect = Shader.PropertyToID("_Aspect"),
            FxGlowOut = Shader.PropertyToID("_GlowOut"), FxWeldWidth = Shader.PropertyToID("_WeldWidth"),
            FxIntensity = Shader.PropertyToID("_Intensity"), FxFlash = Shader.PropertyToID("_Flash");

        private static readonly Color BossChip = new Color(0.55f, 0.07f, 0.07f, 0.6f); // dark translucent red ghost (boss-c)

        private static void LoadBossC()
        {
            _bossC = ThemeTokens.GetSprite("sprite.boss.frame") && ThemeTokens.GetSprite("sprite.boss.cap") &&
                ThemeTokens.GetSprite("sprite.boss.plaque") && ThemeTokens.GetSprite("sprite.boss.marker") &&
                ThemeTokens.GetSprite("sprite.bar.boss.fill.c") && ThemeTokens.GetSprite("sprite.bar.boss.trough.c");
        }

        private static void ApplyBossC(State s)
        {
            s.BossCTried = true;
            if (!_bossC || !s.Health) return;
            var hud = EnemyHud.instance;
            if (!hud || !hud.m_baseHudBoss || !EnemyHudMath.BossBar(s.Boss, s.Data.m_gui.name, hud.m_baseHudBoss.name)) return;
            try { s.BossC = BossC(s.Data.m_gui.transform, s.Health); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] boss-c on '{s.Data.m_gui.name}' failed (legacy boss look stays): {e}"); }
        }

        private static bool BossC(Transform t, RectTransform health)
        {
            var frameSprite = ThemeTokens.GetSprite("sprite.boss.frame");
            var cap = ThemeTokens.GetSprite("sprite.boss.cap");
            var plaque = ThemeTokens.GetSprite("sprite.boss.plaque");
            var marker = ThemeTokens.GetSprite("sprite.boss.marker");
            var fillSprite = ThemeTokens.GetSprite("sprite.bar.boss.fill.c");
            var trough = ThemeTokens.GetSprite("sprite.bar.boss.trough.c");
            if (!frameSprite || !cap || !plaque || !marker || !fillSprite || !trough) return false;
            _bossWeld = ThemeTokens.GetSprite("sprite.boss.weld");

            // Height follows width like the world bar (share.boss.bar.aspect).
            var width = health.rect.width;
            Grow(t, health, width / ThemeTokens.GetFloat("share.boss.bar.aspect", 29.4f));
            var barH = health.rect.height;

            var bkg = health.Find("bkg") as RectTransform;
            if (bkg && bkg.TryGetComponent<Image>(out var troughImg) && MockupStrip(troughImg, "sprite.bar.boss.trough.c", "boss trough (cut from boss-c)"))
                bkg.offsetMin = bkg.offsetMax = Vector2.zero;
            for (var i = 0; i < health.childCount; i++)
            {
                var child = health.GetChild(i);
                if (!child.name.StartsWith("health_", System.StringComparison.Ordinal)) continue;
                var bar = child.Find("bar");
                if (!bar || !bar.TryGetComponent<Image>(out var img)) continue;
                if (child.name == "health_fast") MockupStrip(img, "sprite.bar.boss.fill.c", "boss fill (cut from boss-c)");
                else if (child.name == "health_slow")
                {
                    // The lag = Vaettir's chip: flat pale pink behind the fill.
                    img.sprite = null;
                    img.type = Image.Type.Simple;
                    img.color = BossChip;
                    Art.Note(img, "boss.chip", true, "boss lag ghost (dark translucent red)");
                }
            }

            // Rail frame: tiled, its window fitted to the bar height (Vaettir ThemeFrame); the legacy frame object is reused.
            var frame = Child(health, FrameName);
            if (!frame) frame = NewFrame(health);
            var b = frameSprite.border; // x = L, y = B, z = R, w = T (sprite px)
            var toRef = 100f / frameSprite.pixelsPerUnit;
            var window = Mathf.Max(1f, (frameSprite.rect.height - b.y - b.w) * toRef);
            var k = barH / window;
            frame.sprite = frameSprite;
            frame.type = Image.Type.Tiled;
            frame.fillCenter = false;
            frame.color = Color.white;
            frame.pixelsPerUnitMultiplier = _canvasFactor / k;
            var fr = frame.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(-b.x * toRef * k, -b.y * toRef * k);
            fr.offsetMax = new Vector2(b.z * toRef * k, b.w * toRef * k);
            Art.Note(frame, "boss.frame", true, "boss-c rail frame (tiled, window = bar height)");

            // Weld on the HP edge, under the caps / fangs: the RuneSkin/UI Boss FX shader (narrow molten band + hit sparks)
            // on a rect over the whole bar with glow room above / below, else the boss_weld sprite moved per frame.
            var fxShader = ThemeTokens.BossFxShader;
            if (fxShader && fxShader.isSupported)
            {
                var weld = NewChildImage(health, BossWeldName, null);
                var room = ThemeTokens.GetFloat("fx.room", 1.6f);
                var r = weld.rectTransform;
                r.anchorMin = new Vector2(0f, 0.5f);
                r.anchorMax = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(0f, barH * (1f + 2f * room));
                var mat = new Material(fxShader) { name = "RuneSkin BossFx weld" };
                mat.SetFloat(FxMode, 1f);
                mat.SetFloat(FxBandMin, room / (1f + 2f * room));
                mat.SetFloat(FxBandMax, (1f + room) / (1f + 2f * room));
                mat.SetFloat(FxAspect, width / (barH * (1f + 2f * room)));
                mat.SetFloat(FxGlowOut, ThemeTokens.GetFloat("fx.gas.glow", 0.36f));
                mat.SetFloat(FxWeldWidth, ThemeTokens.GetFloat("fx.weld.width", 0.2f));
                mat.SetFloat(FxIntensity, ThemeTokens.GetFloat("fx.weld.intensity", 1.4f));
                mat.SetFloat(FxFill, 1f);
                weld.material = mat;
                weld.enabled = false;
                Art.Note(weld, "boss.weld", true, "boss-c weld (RuneSkin/UI Boss FX shader) on the HP edge");
            }
            else if (_bossWeld)
            {
                var weld = NewChildImage(health, BossWeldName, _bossWeld);
                var r = weld.rectTransform;
                r.anchorMin = new Vector2(1f, 0f);
                r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(_bossWeld.pivot.x / Mathf.Max(1f, _bossWeld.rect.width), 0.5f);
                r.sizeDelta = new Vector2(barH * _bossWeld.rect.width / Mathf.Max(1f, _bossWeld.rect.height), 0f);
                weld.enabled = false;
                Art.Note(weld, "boss.weld", true, "boss-c weld sprite (shader fallback) on the HP edge");
            }
            // The hot edge on the HP fill end: the Codex weld flipbook (8 frames, cross-faded), else the flare sprite.
            if (WeldFrames[0] || LoadWeldFrames())
            {
                // cell 384x512: the edge at x 150, spanning y 96..416 (fx.weld.h bar heights incl. overshoot)
                var cellH = barH * ThemeTokens.GetFloat("fx.weld.h", 1.3f) * 512f / 320f;
                for (var k2 = 0; k2 < 2; k2++)
                {
                    var img = NewChildImage(health, BossFlareName + (k2 == 0 ? "" : "B"), WeldFrames[0]);
                    var r = img.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
                    r.pivot = new Vector2(150f / 384f, 0.5f);
                    r.sizeDelta = new Vector2(cellH * 384f / 512f * ThemeTokens.GetFloat("fx.weld.w", 1.6f), cellH);
                    img.enabled = false;
                    Art.Note(img, "boss.weld.book", true, "boss-c hot edge flipbook (Codex, 8 frames)");
                }
            }
            else
            {
                var flareSprite = ThemeTokens.GetSprite("sprite.boss.flare");
                if (flareSprite)
                {
                    var flare = NewChildImage(health, BossFlareName, flareSprite);
                    flare.preserveAspect = true;
                    var fs = barH * ThemeTokens.GetFloat("size.boss.flare", 2.2f);
                    var r = flare.rectTransform;
                    r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
                    r.sizeDelta = new Vector2(fs * flareSprite.rect.width / Mathf.Max(1f, flareSprite.rect.height) * ThemeTokens.GetFloat("share.boss.flare.w", 2.4f), fs);
                    flare.enabled = false;
                    Art.Note(flare, "boss.flare", true, "boss-c flare on the HP edge (no flipbook)");
                }
            }
            // Spear caps: right edge one rail thickness inside the bar end, the knot over the fill ends; the right one mirrored.
            var capH = barH * ThemeTokens.GetFloat("size.boss.cap.h", 2.6f);
            var capW = capH * cap.rect.width / Mathf.Max(1f, cap.rect.height);
            var capOffset = b.w * toRef * k; // the fill spans Health, so it already runs under the knot (no gap)
            for (var side = 0; side < 2; side++)
            {
                var img = NewChildImage(health, BossCapName + side, cap);
                img.preserveAspect = true;
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(side, 0.5f);
                r.pivot = new Vector2(1f, 0.5f);
                r.sizeDelta = new Vector2(capW, capH);
                r.anchoredPosition = new Vector2(side == 0 ? capOffset : -capOffset, 0f);
                if (side == 1) r.localScale = new Vector3(-1f, 1f, 1f);
                Art.Note(img, "boss.cap", true, "boss-c spear cap");
            }

            // Fangs at the thirds, hanging from the top edge (Vaettir BuildMarker, themed).
            var mh = barH * ThemeTokens.GetFloat("size.boss.marker.h", 1.85f);
            for (var m = 1; m < 3; m++)
            {
                var img = NewChildImage(health, "RuneSkin_BossMarker" + m, marker);
                var r = img.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(m / 3f, 1f);
                r.pivot = new Vector2(0.5f, ThemeTokens.GetFloat("share.boss.marker.pivot", 0.89f));
                r.sizeDelta = new Vector2(mh * ThemeTokens.GetFloat("share.boss.marker.aspect", 0.3f), mh);
                Art.Note(img, "boss.marker", true, "boss-c fang marker");
            }

            // Plaque on the bar top, the vanilla Name (a direct child, EnemyHud finds it by name) moved into its field.
            var ph = barH * ThemeTokens.GetFloat("share.boss.plaque.h", 2.45f);
            var pw = ph * plaque.rect.width / Mathf.Max(1f, plaque.rect.height);
            var legacyPlaque = t.Find("RuneSkin_BossPlaque");
            if (legacyPlaque) legacyPlaque.gameObject.SetActive(false);
            var plaqueImg = NewChildImage(health, "RuneSkin_BossPlaqueC", plaque);
            plaqueImg.preserveAspect = true;
            var pr = plaqueImg.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 0f);
            pr.sizeDelta = new Vector2(pw, ph);
            pr.anchoredPosition = Vector2.zero;
            Art.Note(plaqueImg, "boss.plaque", true, "boss-c name plaque");
            var name = t.Find("Name") as RectTransform;
            if (name)
            {
                var x0 = ThemeTokens.GetFloat("share.boss.plaque.field.x0", 0.236f);
                var x1 = ThemeTokens.GetFloat("share.boss.plaque.field.x1", 0.783f);
                var y0 = ThemeTokens.GetFloat("share.boss.plaque.field.y0", 0.235f);
                var y1 = ThemeTokens.GetFloat("share.boss.plaque.field.y1", 0.68f);
                var r = health.rect;
                var centre = new Vector3(r.center.x + ((x0 + x1) * 0.5f - 0.5f) * pw, r.yMax + (y0 + y1) * 0.5f * ph, 0f);
                var inHud = t.InverseTransformPoint(health.TransformPoint(centre));
                name.anchorMin = name.anchorMax = new Vector2(0.5f, 0.5f);
                name.pivot = new Vector2(0.5f, 0.5f);
                var parentRect = ((RectTransform)t).rect;
                name.anchoredPosition = new Vector2(inHud.x - parentRect.center.x, inHud.y - parentRect.center.y);
                name.sizeDelta = new Vector2((x1 - x0) * pw, (y1 - y0) * ph);
                name.SetAsLastSibling();
                if (name.TryGetComponent<TMP_Text>(out var text))
                {
                    var ok = TextLook.Apply(text, "font.rune", "boss");
                    text.color = ThemeTokens.GetColor("color.boss.name", new Color(0.851f, 0.863f, 0.878f));
                    text.alignment = TextAlignmentOptions.Center;
                    text.enableAutoSizing = true;
                    text.fontSizeMin = 10f;
                    text.fontSizeMax = 26f;
                    text.characterSpacing = 6f;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    // engraved metallic silver (top light to bottom grey, dark outline, soft dark underlay downward)
                    text.enableVertexGradient = true;
                    text.colorGradient = new VertexGradient(new Color(0.96f, 0.97f, 0.98f), new Color(0.96f, 0.97f, 0.98f),
                        new Color(0.62f, 0.65f, 0.68f), new Color(0.62f, 0.65f, 0.68f));
                    text.color = Color.white;
                    var fm = text.fontMaterial;
                    fm.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.12f);
                    fm.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.04f, 0.05f, 0.06f, 1f));
                    if (fm.HasProperty(ShaderUtilities.ID_UnderlayColor))
                    {
                        fm.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                        fm.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
                        fm.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
                        fm.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.25f);
                    }
                    Art.Note(text, "boss.name", ok, "boss name in the boss-c plaque field (rune face)");
                }
            }

            _bossSpan = EnemyHudMath.BossSpan(width, capW, capOffset);
            Plugin.Log.LogInfo($"[RuneSkin] boss HUD '{t.name}' drawn as boss-c: bar {width:0.#}x{barH:0.#}, span {_bossSpan:0.#}");
            return true;
        }

        private static Image NewChildImage(RectTransform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.color = Color.white;
            return img;
        }

        // Per frame for a boss HUD (boss-c): screen share and the weld on the HP edge. No allocation.
        private static void BossUpdate(State s, Character c)
        {
            if (!s.BossC) return;
            var gui = s.Data.m_gui.transform;
            if (_rootCanvas)
            {
                var r = _rootCanvas.rect;
                var fit = EnemyHudMath.BossScreenFit(_bossSpan, r.width, r.height, EnemyHudConfig.BossBarWidth.Value);
                if (Mathf.Abs(gui.localScale.x - fit) > 0.001f) gui.localScale = new Vector3(fit, fit, 1f);
                s.BossFit = fit;
            }
            var hp = c.GetHealthPercentage();
            var show = hp > 0.001f && hp < 0.995f;
            // a hit flashes the sparks / flare burst, decaying over 0.35 s
            if (s.LastHp >= 0f && hp < s.LastHp - 0.0005f) s.Flash = 1f;
            s.LastHp = hp;
            s.Flash = Mathf.Max(0f, s.Flash - Time.unscaledDeltaTime / 0.35f);
            BossFlare(s, hp, show);
            if (!s.Weld)
            {
                if (s.WeldLooked) return;
                s.WeldLooked = true;
                s.Weld = Child(s.Health, BossWeldName);
                if (!s.Weld) return;
            }
            if (s.Weld.enabled != show) s.Weld.enabled = show;
            if (!show) return;
            var m = s.Weld.material;
            if (m && m.shader == ThemeTokens.BossFxShader)
            {
                m.SetFloat(FxFill, hp);
                m.SetFloat(FxFlash, s.Flash);
                return;
            }
            var wr = s.Weld.rectTransform;
            if (Mathf.Abs(wr.anchorMin.x - hp) > 0.0005f)
            {
                wr.anchorMin = new Vector2(hp, 0f);
                wr.anchorMax = new Vector2(hp, 1f);
                wr.anchoredPosition = Vector2.zero;
            }
        }

        private static readonly Sprite[] WeldFrames = new Sprite[8];

        private static bool LoadWeldFrames()
        {
            for (var i = 0; i < WeldFrames.Length; i++)
                if (!(WeldFrames[i] = ThemeTokens.GetSprite("sprite.fx.weld." + i))) { WeldFrames[0] = null; return false; }
            return true;
        }

        private static void BossFlare(State s, float hp, bool show)
        {
            if (!s.Flare)
            {
                if (s.FlareLooked) return;
                s.FlareLooked = true;
                s.Flare = Child(s.Health, BossFlareName);
                s.FlareB = Child(s.Health, BossFlareName + "B");
                if (!s.Flare) return;
            }
            if (s.Flare.enabled != show) s.Flare.enabled = show;
            if (s.FlareB && s.FlareB.enabled != show) s.FlareB.enabled = show;
            if (!show) return;
            var t = Time.unscaledTime;
            if (s.FlareB && WeldFrames[0])
            {
                var i = EnemyHudMath.SheetFrame(t, 12f, WeldFrames.Length, out var blend);
                if (i != s.SparkFrame)
                {
                    s.SparkFrame = i;
                    s.Flare.sprite = WeldFrames[i];
                    s.FlareB.sprite = WeldFrames[(i + 1) % WeldFrames.Length];
                }
                var a = Mathf.Min(1f, 0.85f + s.Flash);
                var k = 1f + 0.25f * s.Flash;
                PlaceBook(s.Flare, hp, a * (1f - blend), k);
                PlaceBook(s.FlareB, hp, a * blend, k);
                return;
            }
            var r = s.Flare.rectTransform;
            if (Mathf.Abs(r.anchorMin.x - hp) > 0.0005f)
            {
                r.anchorMin = r.anchorMax = new Vector2(hp, 0.5f);
                r.anchoredPosition = Vector2.zero;
            }
            var ky = 1f + 0.06f * Mathf.Sin(t * 7.3f) + 0.2f * s.Flash;
            var kx = 1f + 0.12f * Mathf.PerlinNoise(t * 4f, 0.71f) + 0.5f * s.Flash;
            r.localScale = new Vector3(kx, ky, 1f);
            s.Flare.color = new Color(1f, 1f, 1f, Mathf.Min(1f, 0.85f + 0.15f * Mathf.PerlinNoise(t * 6f, 0.37f) + s.Flash));
        }

        private static void PlaceBook(Image img, float x, float alpha, float scale)
        {
            var r = img.rectTransform;
            if (Mathf.Abs(r.anchorMin.x - x) > 0.0005f)
            {
                r.anchorMin = r.anchorMax = new Vector2(x, 0.5f);
                r.anchoredPosition = Vector2.zero;
            }
            if (Mathf.Abs(r.localScale.x - scale) > 0.001f) r.localScale = new Vector3(scale, scale, 1f);
            img.color = new Color(1f, 1f, 1f, alpha);
        }
    }
}