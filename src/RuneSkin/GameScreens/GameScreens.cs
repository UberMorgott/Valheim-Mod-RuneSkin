using System;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.PreWorld;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Compat;

namespace RuneSkin.GameScreens
{
    // M6 batch 2 (art\README.md "M6 sprites", mockups m6-load-b, m6-text-a + raven B, m6-dialogs-a): vanilla in-game
    // screens restyled in place through the role pass (ScreenRegistry), as the M5 forms: vanilla panels -> panel stack
    // (PreWorldKit.Panels), controls by their component wiring (Classifier.ControlFamilies), adapter roles from the
    // vanilla fields, RuneSkin decor as internal SkinMark images (theme roles, reverted with the root).
    // Families: LoadScreen (Hud.m_loadingScreen), Chat (Chat window + world / NPC text templates), Popups (achievement,
    // join code, session player list, closed captions), TextDialogs (TextInput, TextViewer), Compendium (TextsDialog),
    // SplitDialog (InventoryGui.m_splitDialog).
    public static class GameScreenKit
    {
        public static bool Owned(string family) => OwnershipRegistry.IsOwned(family);

        // Registers a vanilla root (or row / bubble template) with the role pass and restyles it once.
        public static void Form(Transform root, string id, string family, Action<ScreenContext, Transform> roles = null, bool panels = true, string pad = "size.panel.pad")
        {
            if (!root) return;
            if (panels) PreWorldKit.Panels(root, pad);
            ScreenRegistry.Register(root, () => Context(root, id, family, roles));
            ScreenRegistry.Restyle(root);
        }

        private static ScreenContext Context(Transform root, string id, string family, Action<ScreenContext, Transform> roles)
        {
            var ctx = new ScreenContext(id, family);
            roles?.Invoke(ctx, root);
            PreWorldKit.Common(ctx, root);
            return ctx;
        }

        public static void Restyle(Component c)
        {
            if (c) ScreenRegistry.Restyle(c.transform);
        }

        // A RuneSkin image with a fixed theme role (internal mark: never re-classified, destroyed on revert).
        public static Image Decor(Transform parent, string name, string role, string reason)
        {
            var existing = parent ? parent.Find(name) : null;
            if (existing && existing.TryGetComponent<Image>(out var had)) return had;
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            SkinMark.Own(go, role, reason).ThemeRevision = -1;
            return img;
        }

        public static RectTransform Centre(RectTransform rt, Vector2 position, float side)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(side, side);
            return rt;
        }

        // Every vanilla image under t switched off (journaled through PanelSwap; role pass reports it skipped).
        public static void HideImages(Transform t)
        {
            if (!t) return;
            foreach (var img in t.GetComponentsInChildren<Image>(true))
            {
                if (img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                PanelSwap.Apply(img, img.sprite ? img.sprite.name : null);
            }
        }

        // The confirm button of a dialog reads as the primary (art: OK = button_selected).
        public static void Primary(ScreenContext ctx, Button b, string reason)
        {
            if (b && b.targetGraphic is Image img) ctx.Role(img, "button.primary", reason);
        }

        public static T Field<T>(object o, string name) where T : class =>
            o == null ? null : AccessTools.Field(o.GetType(), name)?.GetValue(o) as T;
    }

    // RuneSkin decor that must fade with a vanilla graphic the screen's Animator drives (TextViewer animates its texts'
    // and backdrop colours; our bands / panel stack are not part of that clip): CanvasGroup alpha = the source's
    // colour alpha x its CanvasRenderer alpha, every frame (run 20261022-122034: the rune bands stayed on screen
    // while the viewer was hidden).
    public sealed class AlphaFollow : MonoBehaviour
    {
        public Graphic Source;
        private CanvasGroup _group;

        public static void Ensure(GameObject go, Graphic source)
        {
            if (!go || !source) return;
            if (!go.TryGetComponent<AlphaFollow>(out var f)) f = go.AddComponent<AlphaFollow>();
            f.Source = source;
        }

        private void LateUpdate()
        {
            if (!_group)
            {
                // Decor only: never hit or focused. Set once (the per-frame writes re-dirtied the group, perf audit P2).
                if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            var a = Source && Source.gameObject.activeInHierarchy ? Source.color.a * Source.canvasRenderer.GetAlpha() : 0f;
            if (RuneSkin.Restyle.RenderGateMath.Concealed(a)) a = 0f;
            if (!Mathf.Approximately(_group.alpha, a)) _group.alpha = a;
            RuneSkin.Restyle.RenderGate.Set(_group, "fade", a == 0f); // the source faded out: not drawn
        }
    }

    // Theme colour on a text whose colour a vanilla Animator drives (TextViewer clips tint the rune text red / the topic):
    // keeps the clip's alpha (the fade), replaces the rgb every frame.
    public sealed class TintKeep : MonoBehaviour
    {
        public Color Rgb;
        private TMP_Text _text;

        public static void Ensure(TMP_Text t, string token)
        {
            if (!t) return;
            if (!t.TryGetComponent<TintKeep>(out var k)) k = t.gameObject.AddComponent<TintKeep>();
            k.Rgb = ThemeTokens.GetColor(token, t.color);
        }

        private void LateUpdate()
        {
            if (!_text && !TryGetComponent(out _text)) return;
            var c = _text.color;
            if (Mathf.Approximately(c.r, Rgb.r) && Mathf.Approximately(c.g, Rgb.g) && Mathf.Approximately(c.b, Rgb.b)) return;
            _text.color = new Color(Rgb.r, Rgb.g, Rgb.b, c.a);
        }
    }

    // Slow continuous turn (teleport swirl), unscaled time so it runs while the world is paused / loading.
    public sealed class SwirlSpin : MonoBehaviour
    {
        public float DegPerSecond = 40f;
        private float _t;

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            transform.localRotation = Quaternion.Euler(0f, 0f, GameScreenMath.SpinAngle(_t, DegPerSecond));
        }
    }

    // Sleep medallion slot (socket + rune spinner + moon) laid out from the measured sleep texts (SleepText "Zzz"
    // m_textField + dream m_dreamField, SleepText.cs; dream texts run to 4+ lines): the medallion sits gap above the
    // texts' rendered block (below when it would leave the screen top), never on them; the texts wrap at
    // size.load.text.maxw (TMP margins, centred). Re-measured only when a text changes. Lives on the internal socket
    // (destroyed on revert: the margins it set go back).
    public sealed class SleepLayout : MonoBehaviour
    {
        private RectTransform _host;
        private RectTransform[] _parts;
        private TMP_Text[] _texts;
        private Vector4[] _margins;
        private int _key = int.MinValue;

        public static void Ensure(Image socket, RectTransform host, params RectTransform[] others)
        {
            if (!socket || !host || socket.TryGetComponent<SleepLayout>(out _)) return;
            var l = socket.gameObject.AddComponent<SleepLayout>();
            l._host = host;
            l._parts = new RectTransform[others.Length + 1];
            l._parts[0] = socket.rectTransform;
            for (var i = 0; i < others.Length; i++) l._parts[i + 1] = others[i];
            var texts = new System.Collections.Generic.List<TMP_Text>();
            foreach (var t in host.GetComponentsInChildren<TMP_Text>(true))
                if (!(t.GetComponent<SkinMark>() is SkinMark m && m.Internal)) texts.Add(t);
            l._texts = texts.ToArray();
            l._margins = new Vector4[l._texts.Length];
            for (var i = 0; i < l._texts.Length; i++) l._margins[i] = l._texts[i].margin;
        }

        private void LateUpdate()
        {
            if (!_host || _texts == null) return;
            var key = 17;
            foreach (var t in _texts)
                key = key * 31 + (t && t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text) ? StringComparer.Ordinal.GetHashCode(t.text) ^ (int)t.rectTransform.rect.width : 0);
            key = key * 31 + (int)_host.rect.height;
            if (key == _key) return;
            _key = key;
            Layout();
        }

        private void Layout()
        {
            var maxw = ThemeTokens.GetFloat("size.load.text.maxw", 1100f);
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            for (var i = 0; i < _texts.Length; i++)
            {
                var t = _texts[i];
                if (!t) continue;
                var inset = GameScreenMath.WrapInset(t.rectTransform.rect.width - _margins[i].x - _margins[i].z, maxw);
                t.margin = new Vector4(_margins[i].x + inset, _margins[i].y, _margins[i].z + inset, _margins[i].w);
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                t.ForceMeshUpdate();
                var b = t.textBounds;
                if (b.size.y <= 0f) continue;
                foreach (var y in new[] { b.max.y, b.min.y })
                {
                    var local = _host.InverseTransformPoint(t.rectTransform.TransformPoint(new Vector3(b.center.x, y, 0f))).y;
                    top = Mathf.Max(top, local);
                    bottom = Mathf.Min(bottom, local);
                }
            }
            if (float.IsNegativeInfinity(top)) return; // no text: the start slot stays
            var side = Mathf.Max(_parts[0].sizeDelta.x, _parts[0].sizeDelta.y);
            var r = _host.rect;
            var y0 = GameScreenMath.MedallionY(top, bottom, side, ThemeTokens.GetFloat("size.load.socket.gap", 24f), r.yMax, r.yMin,
                ThemeTokens.GetFloat("size.load.socket.margin", 24f));
            // Centre-anchored parts: anchoredPosition is relative to the host's centre.
            foreach (var p in _parts)
                if (p) p.anchoredPosition = new Vector2(p.anchoredPosition.x, y0 - r.center.y);
        }

        private void OnDestroy()
        {
            if (_texts == null) return;
            for (var i = 0; i < _texts.Length; i++)
                if (_texts[i]) _texts[i].margin = _margins[i];
        }
    }

    // ---- LoadScreen ------------------------------------------------------------------------------------------------

    // Black screens over the world (Hud.m_loadingScreen, Hud.UpdateBlackScreen :571-630, mockup m6-load-b): respawn tip
    // in a panel banner bottom-centre (vanilla tip text + braid kept, banner behind them), the LoadingIndicator spinner
    // (no real progress unless locations generate) = spinner_runes in a button_round socket; sleep = socket + rune
    // spinner + glyph_time_moon; teleport = minimap_ring + glyph_teleport_swirl in the rune glow, turning slowly.
    // Vanilla progress images of sleep / teleport are switched off; the loading picture stays content.
    public static class LoadScreenSkin
    {
        public const string ScreenId = "LoadScreen";
        private const string Banner = "RuneSkin_TipBanner";

        public static void Attach(Hud hud)
        {
            if (!hud.m_loadingScreen) return;
            var root = hud.m_loadingScreen.transform;
            TipBanner(hud);
            Respawn(hud);
            Sleep(hud);
            Teleport(hud);
            GameScreenKit.Form(root, ScreenId, OwnershipRegistry.LoadScreen, (ctx, r) => Roles(ctx, hud), panels: false);
            Plugin.Log.LogInfo("[RuneSkin] load screen attached");
        }

        private static void TipBanner(Hud hud)
        {
            var tip = hud.m_loadingTip ? hud.m_loadingTip.rectTransform : null;
            if (!tip || !tip.parent || tip.parent.Find(Banner)) return;
            var host = new GameObject(Banner, typeof(RectTransform));
            var rt = (RectTransform)host.transform;
            rt.SetParent(tip.parent, false);
            rt.SetSiblingIndex(tip.GetSiblingIndex());
            host.AddComponent<LayoutElement>().ignoreLayout = true;
            var pad = ThemeTokens.GetFloat("size.load.tip.pad", 18f);
            var size = tip.rect.size;
            rt.anchorMin = tip.anchorMin;
            rt.anchorMax = tip.anchorMax;
            rt.pivot = tip.pivot;
            rt.anchoredPosition = tip.anchoredPosition;
            var stretchX = !Mathf.Approximately(tip.anchorMin.x, tip.anchorMax.x);
            var w = GameScreenMath.BannerWidth(size.x, pad, ThemeTokens.GetFloat("size.load.tip.minw", 520f), ThemeTokens.GetFloat("size.load.tip.maxw", 1400f));
            var h = GameScreenMath.BannerHeight(size.y, 0f, pad);
            rt.sizeDelta = new Vector2(stretchX ? tip.sizeDelta.x + 2f * pad : w, Mathf.Approximately(tip.anchorMin.y, tip.anchorMax.y) ? h : tip.sizeDelta.y + 2f * pad);
            PanelDecor.Ensure(rt, "size.panel.pad.small", glass: true, plaque: false);
            hud.m_loadingTip.alignment = TextAlignmentOptions.Center;
        }

        private static void Respawn(Hud hud)
        {
            var ind = hud.m_loadingIndicator;
            var spinner = GameScreenKit.Field<Image>(ind, "m_spinner");
            if (!spinner || !spinner.transform.parent) return;
            var sr = spinner.rectTransform;
            var side = Mathf.Max(sr.rect.width, sr.rect.height) * ThemeTokens.GetFloat("scale.load.socket", 1.45f);
            var socket = GameScreenKit.Decor(sr.parent, "RuneSkin_SpinnerSocket", "socket.round", "respawn spinner socket (button_round)");
            var so = socket.rectTransform;
            so.SetSiblingIndex(sr.GetSiblingIndex());
            so.anchorMin = sr.anchorMin;
            so.anchorMax = sr.anchorMax;
            so.pivot = sr.pivot;
            so.anchoredPosition = sr.anchoredPosition;
            so.sizeDelta = new Vector2(side, side);
            // LoadingIndicator re-tints the spinner from the colour it cached in Awake (UpdateGUIVisibility): the cache
            // takes the rune glow, so its fades keep the theme colour.
            var glow = ThemeTokens.GetColor("color.minimap.rune", spinner.color);
            AccessTools.Field(typeof(LoadingIndicator), "m_spinnerOriginalColor")?.SetValue(ind, glow);
            // The vanilla spinner image turns (its animation): off; the RuneSkin rune spinner (fixed runes, chasing
            // glow, 0.10.2) takes its place and fades with it (AlphaFollow reads the vanilla colour fade).
            if (sr.parent.Find("RuneSkin_RespawnSpinner")) return;
            PanelSwap.Apply(spinner, spinner.sprite ? spinner.sprite.name : null);
            var rune = RuneSpinner.Add(sr.parent, "RuneSkin_RespawnSpinner", Mathf.Max(sr.rect.width, sr.rect.height));
            var ro = rune.rectTransform;
            ro.SetSiblingIndex(sr.GetSiblingIndex() + 1);
            ro.anchorMin = sr.anchorMin;
            ro.anchorMax = sr.anchorMax;
            ro.pivot = sr.pivot;
            ro.anchoredPosition = sr.anchoredPosition;
            AlphaFollow.Ensure(rune.gameObject, spinner);
        }

        private static void Sleep(Hud hud)
        {
            var host = hud.m_sleepingProgress ? hud.m_sleepingProgress.transform : null;
            if (!host || host.Find("RuneSkin_SleepSocket")) return;
            GameScreenKit.HideImages(host);
            var side = ThemeTokens.GetFloat("size.load.socket", 112f);
            // Start slot above the centre; SleepLayout moves the medallion into its own slot above the measured sleep
            // texts every time they change (the fixed 96 lift sat on multi-line dream texts, SleepText.m_dreamField,
            // user screenshot 2026-10-23).
            var at = new Vector2(0f, ThemeTokens.GetFloat("size.load.socket.lift", 96f));
            var socket = GameScreenKit.Decor(host, "RuneSkin_SleepSocket", "socket.round", "sleep socket (button_round)");
            GameScreenKit.Centre(socket.rectTransform, at, side);
            var spin = RuneSpinner.Add(host, "RuneSkin_SleepSpinner", side * ThemeTokens.GetFloat("share.load.spinner", 0.86f));
            GameScreenKit.Centre(spin.rectTransform, at, spin.rectTransform.sizeDelta.x);
            var moon = GameScreenKit.Decor(host, "RuneSkin_SleepMoon", "glyph.moon", "sleep glyph (glyph_time_moon)");
            GameScreenKit.Centre(moon.rectTransform, at, GameScreenMath.GlyphSide(side, ThemeTokens.GetFloat("share.load.glyph", 0.42f)));
            SleepLayout.Ensure(socket, (RectTransform)host, spin.rectTransform, moon.rectTransform);
        }

        private static void Teleport(Hud hud)
        {
            var host = hud.m_teleportingProgress ? hud.m_teleportingProgress.transform : null;
            if (!host || host.Find("RuneSkin_TeleportRing")) return;
            GameScreenKit.HideImages(host);
            var side = ThemeTokens.GetFloat("size.load.teleport", 220f);
            GameScreenKit.Centre(GameScreenKit.Decor(host, "RuneSkin_TeleportRing", "teleport.ring", "teleport ring (minimap_ring)").rectTransform, Vector2.zero, side);
            var swirl = GameScreenKit.Decor(host, "RuneSkin_TeleportSwirl", "teleport.swirl", "teleport swirl (glyph_teleport_swirl, rune glow)");
            GameScreenKit.Centre(swirl.rectTransform, Vector2.zero, GameScreenMath.GlyphSide(side, ThemeTokens.GetFloat("share.load.swirl", 0.62f)));
            swirl.gameObject.AddComponent<SwirlSpin>().DegPerSecond = ThemeTokens.GetFloat("speed.load.swirl", 40f);
        }

        private static void Roles(ScreenContext ctx, Hud hud)
        {
            var root = hud.m_loadingScreen.transform;
            ctx.Role(hud.m_loadingImage, "content", "loading picture (Hud.m_loadingImage)");
            ctx.Role(hud.m_loadingTip, "text.body", "respawn tip (Hud.m_loadingTip)");
            var ind = hud.m_loadingIndicator;
            ctx.Role(GameScreenKit.Field<Image>(ind, "m_spinner"), "spinner", "LoadingIndicator spinner (rune spinner art)");
            ctx.Role(GameScreenKit.Field<Image>(ind, "m_progressIndicator"), "content", "LoadingIndicator progress fill (location generation)");
            ctx.Role(GameScreenKit.Field<Image>(ind, "m_background"), "content", "LoadingIndicator progress backdrop");
            ctx.Role(GameScreenKit.Field<TMP_Text>(ind, "m_text"), "text.body", "LoadingIndicator text");
            // The full-screen black: the CanvasGroup's own image(s) stretched over the screen.
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(img) || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var r = img.rectTransform;
                if (r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one && (r.parent == root || r == root)) ctx.Role(img, "content", "black screen fill (vanilla)");
            }
        }
    }

    // ---- Chat ------------------------------------------------------------------------------------------------------

    // Chat (Terminal m_chatWindow / m_output / m_input, Chat.cs): the window backdrop = popup, output in the body sans,
    // input = field_search. In-world texts (shout / ping, m_worldTextBase) and NPC / raven speech bubbles
    // (m_npcTextBase, m_npcTextBaseLarge: topic + text, Chat.SetNpcText :679) are templates: restyled once by
    // SpeechBubbleSkin (world-anchored, sized to the text, outlined sans, soft backing; never a panel), every instance
    // copies the look.
    public static class ChatSkin
    {
        public const string ScreenId = "Chat";

        public static void Attach(Chat chat)
        {
            // The chat window (Terminal.m_chatWindow) is not under the Chat object (its children are the bubble
            // templates, run 20261022-122034): registered on its own.
            if (chat.m_chatWindow) GameScreenKit.Form(chat.m_chatWindow, ScreenId, OwnershipRegistry.Chat, (ctx, r) => Roles(ctx, chat), panels: false);
            // The log sat on the popup's left rim (run 20261022-122327): inset inside the frame.
            if (chat.m_output)
            {
                var pad = ThemeTokens.GetFloat("size.chat.pad", 14f);
                chat.m_output.margin = new Vector4(pad, pad * 0.5f, pad, pad * 0.5f);
            }
            WorldHud.SpeechBubbleSkin.StyleWorldTextTemplate(chat.m_worldTextBase);
            WorldHud.SpeechBubbleSkin.StyleNpcTemplate(chat.m_npcTextBase, false);
            WorldHud.SpeechBubbleSkin.StyleNpcTemplate(chat.m_npcTextBaseLarge, true);
            Template(chat.m_worldTextBase, ScreenId + ".WorldText");
            Template(chat.m_npcTextBase, ScreenId + ".NpcText");
            Template(chat.m_npcTextBaseLarge, ScreenId + ".NpcTextLarge");
            // Hidden while a modal (pause menu / its dialogs, UnifiedPopup) is open: never over a dialog.
            WorldHud.WorldTextGate.Ensure(chat);
            Plugin.Log.LogInfo("[RuneSkin] chat attached");
        }

        private static void Roles(ScreenContext ctx, Chat chat)
        {
            var window = chat.m_chatWindow;
            if (window)
                foreach (var img in window.GetComponentsInChildren<Image>(true))
                {
                    if (img.GetComponentInParent<Selectable>(true) || img.GetComponentInParent<Scrollbar>(true)) continue;
                    var sprite = img.sprite ? img.sprite.name : null;
                    if (img.GetComponent<SkinMark>() is SkinMark mk && !string.IsNullOrEmpty(mk.OriginalSprite)) sprite = mk.OriginalSprite;
                    // The log backdrop: the window's own image / a stretched child, vanilla sprite "gradient".
                    if (img.transform == window || sprite == "gradient" || (img.transform.parent == window && img.rectTransform.anchorMin == Vector2.zero && img.rectTransform.anchorMax == Vector2.one))
                        ctx.Role(img, "popup", "chat window backdrop (popup)");
                }
            ctx.Role(chat.m_output, "text.body", "chat log (Terminal.m_output)");
            ctx.Role(chat.m_search, "text.muted", "chat search hint (Terminal.m_search)");
            // Live instances of the bubble / world text templates are under the chat object; they carry the marks of
            // their styled template (registered separately).
            foreach (var t in chat.m_npcTexts) if (t?.m_gui) ctx.Exclude(t.m_gui.transform, "NPC speech bubble instance (styled template Chat.NpcText)");
            foreach (var t in chat.m_worldTexts) if (t?.m_gui) ctx.Exclude(t.m_gui.transform, "world text instance (styled template Chat.WorldText)");
        }

        // Registered for diagnostics only: every graphic is content, styled by SpeechBubbleSkin. Never a panel / popup
        // role here: the popup frame on the fixed 700x250 bubble rect drew a window over a third of the screen (user
        // 2026-10-23, Wizardry mage line).
        private static void Template(GameObject template, string id)
        {
            if (!template) return;
            GameScreenKit.Form(template.transform, id, OwnershipRegistry.Chat, (ctx, root) =>
            {
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                    ctx.Role(img, "content", "speech bubble backing (vanilla soft blob, color.speech.plate, sized to the text by SpeechBubbleFit)");
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                    ctx.Role(t, "content", "speech text (body sans, outline + underlay, SpeechBubbleSkin)");
            }, panels: false);
        }
    }

    // ---- Popups ----------------------------------------------------------------------------------------------------

    // Small popups (art: kit popup): achievement unlock (icon content, name serif), join code (button plate), session
    // player list (pause menu window, rows cloned from its own child), closed captions (caption template + direction
    // indicators). Panels via the panel stack, other backdrops through the role pass.
    public static class PopupSkin
    {
        public const string ScreenId = "Popups";

        public static void Achievement(AchievementUnlockPopup p)
        {
            var root = p.m_root ? p.m_root : p.transform;
            GameScreenKit.Form(root, ScreenId + ".Achievement", OwnershipRegistry.Popups, (ctx, r) =>
            {
                ctx.Role(p.m_achIcon, "content", "achievement icon");
                ctx.Role(p.m_achName, "text.display", "achievement name (display serif)");
                Backdrops(ctx, r);
            });
        }

        public static void JoinCode(JoinCode j)
        {
            var root = j.m_root ? j.m_root.transform : j.transform;
            GameScreenKit.Form(root, ScreenId + ".JoinCode", OwnershipRegistry.Popups, (ctx, r) =>
            {
                ctx.Role(j.m_text, "text.body", "join code text");
                Backdrops(ctx, r);
            });
        }

        public static void PlayerList(Valheim.UI.SessionPlayerList list) =>
            GameScreenKit.Form(list.transform, ScreenId + ".PlayerList", OwnershipRegistry.Popups, (ctx, r) => Backdrops(ctx, r));

        public static void Captions(ClosedCaptions c)
        {
            if (c.m_captionPrefab)
                GameScreenKit.Form(c.m_captionPrefab.transform, ScreenId + ".Caption", OwnershipRegistry.Popups, (ctx, r) => Backdrops(ctx, r), panels: false);
            if (c.m_directionalIndicatorPrefab)
                GameScreenKit.Form(c.m_directionalIndicatorPrefab.transform, ScreenId + ".CaptionIndicator", OwnershipRegistry.Popups, (ctx, r) =>
                {
                    foreach (var img in r.GetComponentsInChildren<Image>(true)) ctx.Role(img, "content", "caption direction indicator (vanilla arrow)");
                }, panels: false);
        }

        // Backdrops the panel pass leaves (non-panel sprites, sprite-less fills behind the content): popup frame.
        internal static void Backdrops(ScreenContext ctx, Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(img) || img.GetComponentInParent<Selectable>(true) || img.GetComponentInParent<Scrollbar>(true)) continue;
                if (img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (img.TryGetComponent<PanelSwap>(out _) || img.GetComponent<Mask>() || img.GetComponent<RectMask2D>()) continue;
                var sprite = img.sprite ? img.sprite.name : null;
                if (img.GetComponent<SkinMark>() is SkinMark mk && !string.IsNullOrEmpty(mk.OriginalSprite)) sprite = mk.OriginalSprite;
                if (sprite == null) continue;
                if (sprite == "TextBackdrop" || sprite.StartsWith("darken", StringComparison.Ordinal) || sprite.StartsWith("panel_", StringComparison.Ordinal) || sprite == "Background")
                    ctx.Role(img, "popup", $"popup backdrop '{sprite}'");
            }
        }
    }

    // ---- Text dialogs ----------------------------------------------------------------------------------------------

    // Text input (TextInput: m_panel, m_topic, m_inputField; mockup m6-text-a): panel stack, title serif, field
    // field_search, OK = button_selected, cancel = button_normal. Text viewer (TextViewer: rune m_root, intro
    // m_introRoot, raven m_ravenRoot): panel stack, band_runes (tiled, 22 ref high) above and below the rune text,
    // serif topic, body sans text, close hint in a button plate; raven = panel + big caps topic.
    public static class TextDialogSkin
    {
        public static void Input(TextInput input)
        {
            GameScreenKit.Form(input.transform, "TextDialogs.Input", OwnershipRegistry.TextDialogs, (ctx, r) =>
            {
                ctx.Role(input.m_topic, "text.header", "text input title (TextInput.m_topic)");
                foreach (var b in r.GetComponentsInChildren<Button>(true))
                    if (IsConfirm(b)) GameScreenKit.Primary(ctx, b, "text input OK (button_selected)");
                PopupSkin.Backdrops(ctx, r);
            });
        }

        // The confirm button by its vanilla wiring (OnEnter / OnInput listeners) or name.
        private static bool IsConfirm(Button b)
        {
            var e = b.onClick;
            for (var i = 0; i < e.GetPersistentEventCount(); i++)
            {
                var m = e.GetPersistentMethodName(i);
                if (m == nameof(TextInput.OnEnter) || m == nameof(TextInput.OnInput)) return true;
            }
            var n = b.name;
            return n.IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("accept", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Viewer(TextViewer v)
        {
            Bands(v.m_topic, v.m_text);
            TintKeep.Ensure(v.m_text, "color.text.body");
            TintKeep.Ensure(v.m_topic, "color.text.header");
            if (v.m_ravenTopic) v.m_ravenTopic.fontStyle |= FontStyles.UpperCase;
            GameScreenKit.Form(v.transform, "TextDialogs.Viewer", OwnershipRegistry.TextDialogs, (ctx, r) =>
            {
                ctx.Role(v.m_topic, "text.header", "rune topic (serif)");
                ctx.Role(v.m_text, "text.body", "rune text (body)");
                ctx.Role(v.m_runeText, "content", "runic transliteration (vanilla rune font: the runes are the content)");
                ctx.Role(v.m_introTopic, "text.header", "intro saga topic");
                ctx.Role(v.m_introText, "text.body", "intro saga text");
                ctx.Role(v.m_ravenTopic, "text.header", "raven topic (big caps)");
                ctx.Role(v.m_ravenText, "text.body", "raven text");
                if (v.m_closeText)
                    foreach (var t in v.m_closeText.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "text.muted", "close hint");
                PopupSkin.Backdrops(ctx, r);
            });
            // Panel stacks fade with the vanilla backdrop they replace (its colour stays Animator-driven while off).
            foreach (var swap in v.GetComponentsInChildren<PanelSwap>(true))
                if (swap.transform.Find(PanelDecor.ChildName) is Transform decor && swap.TryGetComponent<Image>(out var bg))
                {
                    AlphaFollow.Ensure(decor.gameObject, bg);
                    if (swap.transform.Find(PanelDecor.CapsName) is Transform caps) AlphaFollow.Ensure(caps.gameObject, bg);
                }
        }

        // Runic bands (art: band_runes above and below the text): the top one under the topic (it covered the topic when
        // centred on the text box, run 20261022-122327), the bottom one under the text box.
        private static void Bands(TMP_Text topic, TMP_Text text)
        {
            var tr = text ? text.rectTransform : null;
            if (!tr || !tr.parent || tr.parent.Find("RuneSkin_BandTop")) return;
            var h = ThemeTokens.GetFloat("size.text.band.h", 22f);
            var gap = ThemeTokens.GetFloat("size.text.band.gap", 10f);
            var off = GameScreenMath.BandOffset(tr.rect.height, h, gap);
            var centre = tr.anchoredPosition + new Vector2((0.5f - tr.pivot.x) * tr.rect.width, (0.5f - tr.pivot.y) * tr.rect.height);
            // Topic bottom in the band parent's space (world corners, any anchors); else the text-box rule.
            var top = centre.y + off;
            if (topic)
            {
                var pr = topic.rectTransform;
                var parent = (RectTransform)tr.parent;
                var local = parent.InverseTransformPoint(pr.TransformPoint(new Vector3(pr.rect.center.x, pr.rect.yMin, 0f)));
                // anchoredPosition of a centre-anchored band: offset from the parent's anchor reference point.
                var anchorRef = Vector2.Scale(parent.rect.size, (tr.anchorMin == tr.anchorMax ? tr.anchorMin : new Vector2(0.5f, 0.5f)) - parent.pivot);
                top = local.y - anchorRef.y - gap - h * 0.5f;
            }
            foreach (var (name, y) in new[] { ("RuneSkin_BandTop", top), ("RuneSkin_BandBottom", centre.y - off) })
            {
                var band = GameScreenKit.Decor(tr.parent, name, "band.runes", "runic band (band_runes, tiled)");
                var br = band.rectTransform;
                br.SetSiblingIndex(tr.GetSiblingIndex());
                br.anchorMin = br.anchorMax = tr.anchorMin == tr.anchorMax ? tr.anchorMin : new Vector2(0.5f, 0.5f);
                br.pivot = new Vector2(0.5f, 0.5f);
                br.sizeDelta = new Vector2(tr.rect.width, h);
                br.anchoredPosition = new Vector2(centre.x, y);
                AlphaFollow.Ensure(band.gameObject, text);
            }
        }
    }

    // ---- Compendium ------------------------------------------------------------------------------------------------

    // Compendium (TextsDialog, mockup m6-dialogs-a): panel stack, topic list rows = row_recipe_* (selected child =
    // row_recipe_selected), names body sans, reading pane topic serif + body text. The row template is a child of the
    // dialog (TextsDialog.FillTextList clones m_elementPrefab), so clones copy the look. The active effects page is
    // RuneSkin's own list (ActiveEffectsPage, own registration); Epic Loot's own page
    // parts are left to their owner.
    public static class CompendiumSkin
    {
        public const string ScreenId = "Compendium";

        public static void Attach(TextsDialog d)
        {
            GameScreenKit.Form(d.transform, ScreenId, OwnershipRegistry.Compendium, (ctx, r) =>
            {
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == ActiveEffectsPage.RootName) ctx.Exclude(t, "active effects list (own registration Compendium.ActiveEffects)");
                }
                // Search field focus glow (button_glow under searchField): vanilla highlight art, kept.
                foreach (var img in r.GetComponentsInChildren<Image>(true))
                    if (img.name == "glow" && img.sprite && img.sprite.name.StartsWith("button_glow", StringComparison.Ordinal)) ctx.Role(img, "content", "search field focus glow (vanilla)");
                // The topic list content (m_listRoot, sprite-less full-stretch Image) is a transparent hit area, not a
                // screen dim: as scrim it painted a dark plate that ShowText's FocusOnCurrentLevel -> SnapTo
                // (TextsDialog.cs:87-99,199) shifts and the left ScrollRect clamps back on every topic switch (blink).
                if (d.m_listRoot && d.m_listRoot.TryGetComponent<Image>(out var listHit))
                    ctx.Role(listHit, "skip", "topic list content hit area (vanilla, transparent; not a screen dim)");
                var row = d.m_elementPrefab ? d.m_elementPrefab.transform : null;
                foreach (var b in r.GetComponentsInChildren<Button>(true))
                {
                    if (!row || !(b.transform == row || b.name == row.name || b.name == row.name + "(Clone)")) continue;
                    if (b.targetGraphic is Image bg) ctx.Role(bg, "craft.row", "compendium topic row");
                    if (b.transform.Find("selected") is Transform sel && sel.TryGetComponent<Image>(out var si)) ctx.Role(si, "craft.row.selected", "compendium topic selected");
                    foreach (var t in b.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "text.body", "compendium topic name");
                }
                ctx.Role(d.m_textAreaTopic, "text.header", "reading pane topic (serif)");
                ctx.Role(d.m_textArea, "text.body", "reading pane text");
                ctx.Role(d.m_totalSkillText, "text.muted", "compendium footer text");
                PopupSkin.Backdrops(ctx, r);
            });
            // One window header (RuneWindowHeader) over its title band (Texts/Texts_frame, topic on top; vanilla places the
            // frame, the user offset rides on it).
            if (d.transform.Find("Texts_frame") is RectTransform frame)
            {
                MovableWindow.Ensure(frame, WindowMove.Compendium);
                RuneWindowHeader.TopBand(frame, WindowMove.Compendium, frame, "$runeskin_win_texts");
            }
        }
    }

    // ---- Split dialog ----------------------------------------------------------------------------------------------

    // Split stack (SplitDialog, mockup m6-dialogs-a): panel stack, the item icon in a slot_cell_recess socket, slider
    // (M5 art by its wiring), amount body sans, OK = button_selected.
    public static class SplitSkin
    {
        public const string ScreenId = "SplitDialog";

        public static void Attach(SplitDialog s)
        {
            GameScreenKit.Form(s.transform, ScreenId, OwnershipRegistry.SplitDialog, (ctx, r) =>
            {
                var icon = GameScreenKit.Field<Image>(s, "m_splitIcon");
                ctx.Role(icon, "content", "split item icon");
                if (icon && icon.transform.parent && icon.transform.parent.TryGetComponent<Image>(out var socket) && socket != icon && !socket.GetComponent<PanelSwap>())
                    ctx.Role(socket, "slot.recess", "split item socket (slot_cell_recess)");
                ctx.Role(GameScreenKit.Field<TMP_Text>(s, "m_splitIconName"), "text.display", "split item name (display serif)");
                ctx.Role(GameScreenKit.Field<TMP_Text>(s, "m_splitAmount"), "text.body", "split amount");
                GameScreenKit.Primary(ctx, GameScreenKit.Field<Button>(s, "m_splitOkButton"), "split OK (button_selected)");
                PopupSkin.Backdrops(ctx, r);
            });
        }
    }

    // ---- Harmony ---------------------------------------------------------------------------------------------------

    [HarmonyPatch]
    public static class GameScreensAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => AnyOwned();

        private static readonly string[] Families =
            { OwnershipRegistry.LoadScreen, OwnershipRegistry.Chat, OwnershipRegistry.Popups, OwnershipRegistry.TextDialogs, OwnershipRegistry.Compendium, OwnershipRegistry.SplitDialog };

        public static bool AnyOwned() => Array.Exists(Families, OwnershipRegistry.IsOwned);

        private static void Try(string what, string family, Action attach)
        {
            if (!GameScreenKit.Owned(family)) return;
            try { attach(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] {what} restyle failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.HudGameScreens)]
        private static void Hud_Postfix(Hud __instance) => Try("load screen", OwnershipRegistry.LoadScreen, () => LoadScreenSkin.Attach(__instance));

        [HarmonyPatch(typeof(Chat), nameof(Chat.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Chat_Postfix(Chat __instance) => Try("chat", OwnershipRegistry.Chat, () => ChatSkin.Attach(__instance));

        [HarmonyPatch(typeof(TextInput), nameof(TextInput.Awake))]
        [HarmonyPostfix]
        private static void TextInput_Postfix(TextInput __instance) => Try("text input", OwnershipRegistry.TextDialogs, () => TextDialogSkin.Input(__instance));

        [HarmonyPatch(typeof(TextViewer), nameof(TextViewer.Awake))]
        [HarmonyPostfix]
        private static void TextViewer_Postfix(TextViewer __instance) => Try("text viewer", OwnershipRegistry.TextDialogs, () => TextDialogSkin.Viewer(__instance));

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.InventoryGameScreens)]
        private static void InventoryGui_Postfix(InventoryGui __instance)
        {
            if (__instance.m_textsDialog) Try("compendium", OwnershipRegistry.Compendium, () => CompendiumSkin.Attach(__instance.m_textsDialog));
            if (__instance.m_splitDialog) Try("split dialog", OwnershipRegistry.SplitDialog, () => SplitSkin.Attach(__instance.m_splitDialog));
        }

        // Topic rows are cloned per Setup and mods (Epic Loot pages) add parts there: restyle after it.
        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.Setup))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void TextsSetup_Postfix(TextsDialog __instance) => Try("compendium refresh", OwnershipRegistry.Compendium, () => GameScreenKit.Restyle(__instance));

        // Active effects page (ActiveEffectsPage): the TextInfo vanilla inserts, the list swap on ShowText (after Epic
        // Loot's postfix, which turns the text area back on), hidden on close.
        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.AddActiveEffects))]
        [HarmonyPostfix]
        private static void AddActiveEffects_Postfix(TextsDialog __instance) => Try("active effects", OwnershipRegistry.Compendium, () => ActiveEffectsPage.Added(__instance));

        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.ShowText), typeof(TextsDialog.TextInfo))]
        [HarmonyPostfix]
        [HarmonyAfter(ModCompat.EpicLootGuid)]
        private static void ShowText_Postfix(TextsDialog __instance, TextsDialog.TextInfo text) => Try("active effects page", OwnershipRegistry.Compendium, () => ActiveEffectsPage.Shown(__instance, text));

        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.OnClose))]
        [HarmonyPostfix]
        private static void TextsClose_Postfix() => Try("active effects close", OwnershipRegistry.Compendium, ActiveEffectsPage.Closed);

        [HarmonyPatch(typeof(AchievementUnlockPopup), nameof(AchievementUnlockPopup.Start))]
        [HarmonyPostfix]
        private static void Achievement_Postfix(AchievementUnlockPopup __instance) => Try("achievement popup", OwnershipRegistry.Popups, () => PopupSkin.Achievement(__instance));

        [HarmonyPatch(typeof(JoinCode), nameof(JoinCode.Start))]
        [HarmonyPostfix]
        private static void JoinCode_Postfix(JoinCode __instance) => Try("join code", OwnershipRegistry.Popups, () => PopupSkin.JoinCode(__instance));

        [HarmonyPatch(typeof(Valheim.UI.SessionPlayerList), nameof(Valheim.UI.SessionPlayerList.Awake))]
        [HarmonyPostfix]
        private static void PlayerList_Postfix(Valheim.UI.SessionPlayerList __instance) => Try("player list", OwnershipRegistry.Popups, () => PopupSkin.PlayerList(__instance));

        [HarmonyPatch(typeof(ClosedCaptions), nameof(ClosedCaptions.Awake))]
        [HarmonyPostfix]
        private static void Captions_Postfix(ClosedCaptions __instance) => Try("closed captions", OwnershipRegistry.Popups, () => PopupSkin.Captions(__instance));
    }
}
