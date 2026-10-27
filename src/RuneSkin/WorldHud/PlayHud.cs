using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.Ownership;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.WorldHud
{
    // M6 in-game HUD families (art\README.md "M6 sprites", mockups m6-build-a / m6-ship-b), restyled in place on the
    // live vanilla Hud objects (Hud.cs fields; hierarchy from the hudroot dump):
    // - Crosshair (CrosshairSkin): hudroot/crosshair: crosshair glyph, hover text, piece health bar, stealth eye + bar;
    // - ActionHud (ActionHudSkin): hudroot/action_progress, staggerpanel, EventBar, MountHud/MountPanel;
    // - ShipHud (ShipHudSkin): hudroot/ShipHud: own speed ladder + rudder socket, vanilla icons hidden;
    // - KeyHints (KeyHintsSkin): hudroot/KeyHints: key plates + labels, gamepad glyph art kept;
    // - BuildHud (BuildHudSkin): BuildUi (hudroot/BuildUIV2) + Hud.m_buildHud (selected-piece strip, author window)
    //   through the role pass (ScreenRegistry), as the M5 forms.
    public static class PlayHud
    {
        internal static readonly List<Func<ScreenReport>> Reports = new List<Func<ScreenReport>>();

        public static void Record()
        {
            foreach (var r in Reports)
            {
                try
                {
                    var report = r();
                    if (report != null) SkinDiagnostics.Record(report);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] play HUD diagnostics failed: {e.Message}"); }
            }
        }

        internal static bool Owned(string family) => OwnershipRegistry.IsOwned(family);

        internal static void SyncPpu(HudArt art, Component c)
        {
            var canvas = c ? c.GetComponentInParent<Canvas>(true) : null;
            if (canvas) art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
        }

        // Sprite size in reference px (sprite px x 100 / PPU).
        internal static Vector2 RefSize(string token, Vector2 fallback)
        {
            var s = ThemeTokens.GetSprite(token);
            return s ? s.rect.size * (100f / Mathf.Max(1f, s.pixelsPerUnit)) : fallback;
        }

        // Ref px per side of a sliced sprite's border (left, bottom, right, top).
        internal static Vector4 RefBorder(string token)
        {
            var s = ThemeTokens.GetSprite(token);
            return s ? s.border * (100f / Mathf.Max(1f, s.pixelsPerUnit)) : Vector4.zero;
        }

        // A GuiBar fill restyled: sprite + colour, and the colour GuiBar restores on ResetColor (GuiBar.m_originalColor,
        // cached in Awake).
        internal static void Fill(HudArt art, GuiBar bar, string spriteToken, string colorToken, string reason)
        {
            if (!bar || !bar.m_bar || !bar.m_bar.TryGetComponent<Image>(out var img)) return;
            if (!art.Reskin(img, spriteToken, colorToken, reason)) return;
            img.type = Image.Type.Sliced;
            if (colorToken != null) bar.m_originalColor = img.color;
        }

        // Crisp fill-end rim (bar_edge, M6 edge pass) as a child of a GuiBar fill, anchored on its right edge: it follows
        // GuiBar's width changes without per-frame code. Theme mode.hud.bar.edge 2 only.
        internal static void EdgeRim(HudArt art, GuiBar bar, string colorToken)
        {
            if (!bar || !bar.m_bar || ThemeTokens.GetFloat("mode.hud.bar.edge", 2f) < 2f || !ThemeTokens.GetSprite("sprite.hud.bar.edge")) return;
            var fill = bar.m_bar;
            if (fill.Find("RuneSkin_Edge")) return;
            var img = art.Image("RuneSkin_Edge", fill, "sprite.hud.bar.edge", colorToken, Image.Type.Simple);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var s = img.sprite;
            var h = fill.rect.height > 0f ? fill.rect.height : 20f;
            rt.sizeDelta = new Vector2(PlayerHud.HudMath.EdgeWidth(s ? s.rect.width : 0f, s ? s.rect.height : 0f, h), 0f);
        }

        // An Image named `name` directly under `parent`.
        internal static Image Child(Transform parent, string name) =>
            parent && parent.Find(name) is Transform t && t.TryGetComponent<Image>(out var img) ? img : null;

        internal static void Hide(HudArt art, Image img, string reason)
        {
            if (!img) return;
            img.enabled = false;
            art.Suppressed(img.transform, reason);
        }

        internal static void Text(HudArt art, TMP_Text t, string font, string look, string color, string role, string reason, float size = 0f)
        {
            if (!t) return;
            art.Note(t, role, TextLook.ApplyNameplate(t, font, look, 0f), reason); // outline + soft shadow (user: readable on white)
            if (color != null) t.color = ThemeTokens.GetColor(color, t.color);
            // Every themed HUD text reads at >= size.text.hud.min (px at 1080p, the HUD canvas scales with the screen).
            if (size > 0f)
            {
                t.enableAutoSizing = false;
                t.fontSize = MessageSkin.HudReadable(size);
            }
            else if (t.enableAutoSizing) t.fontSizeMax = MessageSkin.HudReadable(t.fontSizeMax);
            else t.fontSize = MessageSkin.HudReadable(t.fontSize);
        }

        // A frame image (sibling drawn after `after`) around `target` by the sprite's border insets.
        internal static Image FrameAround(HudArt art, string name, RectTransform target, Transform after, string token, float scale = 1f)
        {
            if (!target || !target.parent) return null;
            var frame = art.Image(name, target.parent, token, "color.frame", Image.Type.Sliced);
            frame.fillCenter = false; // the window shows the bar under it
            var rt = frame.rectTransform;
            rt.SetSiblingIndex((after ? after : target).GetSiblingIndex() + 1);
            var b = RefBorder(token) * scale;
            rt.anchorMin = target.anchorMin;
            rt.anchorMax = target.anchorMax;
            rt.pivot = target.pivot;
            rt.sizeDelta = target.sizeDelta + new Vector2(b.x + b.z, b.y + b.w);
            rt.anchoredPosition = new Vector2(
                PlayHudMath.GrowPosition(target.anchoredPosition.x, target.pivot.x, b.x, b.z),
                PlayHudMath.GrowPosition(target.anchoredPosition.y, target.pivot.y, b.y, b.w));
            art.Track(frame, 1f / Mathf.Max(0.1f, scale));
            return frame;
        }
    }

    // ---- Crosshair -------------------------------------------------------------------------------------------------

    // Crosshair: glyph_crosshair (tint text_body, gold over something interactable: vanilla writes Color.yellow /
    // half white every frame, Hud.UpdateCrosshair :839-843, re-tinted in its postfix); bow crosshair stays vanilla;
    // hover text in the outlined body sans, the "[<color=yellow>KEY</color>]" tags in text_active; piece health =
    // bar_skill trough / fill (text_ok); stealth = glyph_stealth_eye (hidden text_body, targeted text_active, alert
    // ember) over a slim bar_skill fill in the rune glow (Hud.UpdateStealth :947-980 toggles the objects).
    public sealed class CrosshairSkin : MonoBehaviour
    {
        public static CrosshairSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.Crosshair);
        private Hud _hud;
        private Color _gold, _rest;
        private string _goldTag;

        public static void Attach(Hud hud)
        {
            if (!hud || !hud.m_crosshair || hud.GetComponent<CrosshairSkin>()) return;
            hud.gameObject.AddComponent<CrosshairSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            PlayHud.SyncPpu(_art, hud.m_crosshair);
            _gold = ThemeTokens.GetColor("color.text.active", new Color(0.91f, 0.71f, 0.29f));
            _rest = ThemeTokens.GetColor("color.text.body", Color.white);
            _rest.a = ThemeTokens.GetFloat("share.crosshair.alpha", 0.85f);
            _goldTag = "<color=#" + ColorUtility.ToHtmlStringRGB(_gold) + ">";
            var root = hud.m_crosshair.transform.parent;
            if (root && root.TryGetComponent<Image>(out var anchor)) _art.Content(anchor, "crosshair anchor (vanilla 5 px frame)");
            if (_art.Reskin(hud.m_crosshair, "sprite.glyph.crosshair", null, "crosshair glyph"))
            {
                hud.m_crosshair.type = Image.Type.Simple;
                hud.m_crosshair.preserveAspect = true;
                var s = ThemeTokens.GetFloat("size.crosshair", 22f);
                hud.m_crosshair.rectTransform.sizeDelta = new Vector2(s, s);
            }
            _art.Content(hud.m_crosshairBow, "bow draw crosshair (vanilla, art notes: stays)");
            PlayHud.Text(_art, hud.m_hoverName, "font.body", "hover", "color.text.body", "hover", "hover text (outlined body sans)", ThemeTokens.GetFloat("size.hover.font", 19f));
            if (hud.m_hoverName) hud.m_hoverName.richText = true;
            PieceHealth(hud);
            Stealth(hud);
            Plugin.Log.LogInfo("[RuneSkin] crosshair attached");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        private void PieceHealth(Hud hud)
        {
            var root = hud.m_pieceHealthRoot;
            if (!root) return;
            var darken = PlayHud.Child(root, "darken");
            if (darken && _art.Reskin(darken, "sprite.bar.skill.trough", "color.frame", "piece health trough")) darken.type = Image.Type.Sliced;
            PlayHud.Hide(_art, PlayHud.Child(root, "bkg"), "vanilla bar background (the trough replaces it)");
            PlayHud.Fill(_art, hud.m_pieceHealthBar, "sprite.bar.skill.fill", "color.text.ok", "piece health fill");
        }

        private void Stealth(Hud hud)
        {
            Eye(hud.m_hidden, "color.text.body", "stealth: hidden");
            Eye(hud.m_targeted, "color.text.active", "stealth: targeted");
            Eye(hud.m_targetedAlert, "color.text.ember", "stealth: alerted");
            var bar = hud.m_stealthBar;
            if (!bar) return;
            PlayHud.Hide(_art, PlayHud.Child(bar.transform, "darken"), "vanilla bar shadow");
            var bkg = PlayHud.Child(bar.transform, "bkg");
            if (bkg && _art.Reskin(bkg, "sprite.bar.skill.trough", "color.frame", "stealth trough")) bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, bar, "sprite.bar.skill.fill", "color.hud.glyph", "stealth fill (rune glow)");
        }

        private void Eye(GameObject go, string color, string reason)
        {
            if (!go || !go.TryGetComponent<Image>(out var img)) return;
            if (!_art.Reskin(img, "sprite.glyph.stealth.eye", color, reason)) return;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = PlayHud.RefSize("sprite.glyph.stealth.eye", new Vector2(48f, 28f)) * ThemeTokens.GetFloat("scale.stealth.eye", 0.9f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Hud.UpdateCrosshair postfix: vanilla colours -> theme colours, hover key tags gold.
        internal void AfterCrosshair()
        {
            var c = _hud.m_crosshair;
            if (c)
            {
                var want = c.color == Color.yellow ? _gold : _rest;
                if (c.color != want) c.color = want;
            }
            var t = _hud.m_hoverName;
            if (t && t.text.IndexOf("<color=yellow>", StringComparison.Ordinal) >= 0)
                t.text = t.text.Replace("<color=yellow>", _goldTag);
        }
    }

    // ---- Action progress, stagger, event line, mount -----------------------------------------------------------------

    // Action progress / stagger (art notes): bar_frame_action around the bar (window inset by its border), bar_trough
    // + bar_fill (action in the rune glow, stagger keeps vanilla's colour), serif label. Event / raid line:
    // band_message behind the ember serif title + small mirrored banner_rule at both ends (UpdateEvent writes the
    // name). Mount panel: popup frame, portrait in a button_round socket, bar_skill trough / fill (health red, lag
    // ghost, stamina yellow), outlined texts.
    public sealed class ActionHudSkin : MonoBehaviour
    {
        public static ActionHudSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.ActionHud);
        private Hud _hud;
        private Image _band;
        private RectTransform _ruleL, _ruleR;
        private string _eventText;

        public static void Attach(Hud hud)
        {
            if (!hud || hud.GetComponent<ActionHudSkin>()) return;
            hud.gameObject.AddComponent<ActionHudSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            PlayHud.SyncPpu(_art, hud.m_rootObject.transform);
            if (hud.m_actionBarRoot) Bar(hud.m_actionBarRoot.transform, hud.m_actionProgress, "color.hud.glyph", hud.m_actionName, "action progress");
            if (hud.m_staggerProgress) Bar(hud.m_staggerProgress.transform, hud.m_staggerProgress, null, null, "stagger");
            if (hud.m_staggerProgress && PlayHud.Child(hud.m_staggerProgress.transform, "StaggerIcon") is Image icon) _art.Content(icon, "stagger icon (vanilla)");
            Event(hud);
            Mount(hud);
            Plugin.Log.LogInfo("[RuneSkin] action HUD attached");
            var root = hud.m_rootObject.transform;
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        private void Bar(Transform root, GuiBar bar, string fillColor, TMP_Text label, string what)
        {
            PlayHud.Hide(_art, PlayHud.Child(root, "darken"), $"{what}: vanilla shadow (frame replaces it)");
            var bkg = PlayHud.Child(root, "bkg");
            if (bkg && _art.Reskin(bkg, "sprite.hud.bar.trough", "color.hud.trough", $"{what} trough")) bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, bar, "sprite.hud.bar.fill", fillColor, $"{what} fill");
            PlayHud.EdgeRim(_art, bar, "color.hud.edge");
            if (bkg)
            {
                var frame = PlayHud.FrameAround(_art, "RuneSkin_ActionFrame", bkg.rectTransform, bar && bar.m_bar ? bar.m_bar : null, "sprite.bar.frame.action",
                    ThemeTokens.GetFloat("scale.action.frame", 0.5f));
                if (frame && label) frame.rectTransform.SetSiblingIndex(label.transform.GetSiblingIndex());
            }
            PlayHud.Text(_art, label, "font.body", "action", "color.text.body", what + ".label", $"{what} label (outlined serif)", ThemeTokens.GetFloat("size.action.font", 19f));
            // Label above the framed bar (vanilla centres a 16 px auto-sized line inside the 20 px bar; the frame and the
            // serif would cut it): one line, overflowing sideways, its bottom on the frame top.
            if (label && bkg)
            {
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.alignment = TextAlignmentOptions.Bottom;
                var lr = label.rectTransform;
                var br = bkg.rectTransform;
                var lift = br.rect.height * 0.5f + PlayHud.RefBorder("sprite.bar.frame.action").w * ThemeTokens.GetFloat("scale.action.frame", 0.5f) + ThemeTokens.GetFloat("size.action.label.gap", 4f);
                lr.anchorMin = lr.anchorMax = br.anchorMin == br.anchorMax ? br.anchorMin : new Vector2(0.5f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0f);
                lr.sizeDelta = new Vector2(Mathf.Max(br.rect.width, 200f), label.fontSize * 1.4f);
                lr.anchoredPosition = br.anchoredPosition + new Vector2((0.5f - br.pivot.x) * br.rect.width, (0.5f - br.pivot.y) * br.rect.height + lift);
            }
        }

        private void Event(Hud hud)
        {
            if (!hud.m_eventBar) return;
            var root = hud.m_eventBar.transform;
            _band = PlayHud.Child(root, "Image");
            if (_band && _art.Reskin(_band, "sprite.band.message", "color.frame", "event line band")) _band.type = Image.Type.Sliced;
            PlayHud.Text(_art, hud.m_eventName, "font.display", "event", "color.text.ember", "event.title", "event / raid title (ember serif)", ThemeTokens.GetFloat("size.event.font", 30f));
            if (!hud.m_eventName) return;
            var size = PlayHud.RefSize("sprite.banner.rule", new Vector2(156f, 29f)) * ThemeTokens.GetFloat("scale.event.rule", 0.6f);
            _ruleL = Rule("RuneSkin_RuleLeft", hud.m_eventName.rectTransform, size, true);
            _ruleR = Rule("RuneSkin_RuleRight", hud.m_eventName.rectTransform, size, false);
        }

        private RectTransform Rule(string name, RectTransform title, Vector2 size, bool mirrored)
        {
            var img = _art.Image(name, title.parent, "sprite.banner.rule", "color.frame", Image.Type.Simple);
            var rt = img.rectTransform;
            rt.SetSiblingIndex(title.GetSiblingIndex());
            rt.anchorMin = rt.anchorMax = title.anchorMin == title.anchorMax ? title.anchorMin : new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            if (mirrored) rt.localScale = new Vector3(-1f, 1f, 1f);
            return rt;
        }

        private void Mount(Hud hud)
        {
            var panel = hud.m_mountPanel ? hud.m_mountPanel.transform : null;
            if (!panel) return;
            var darken = PlayHud.Child(panel, "darken");
            if (darken && _art.Reskin(darken, "sprite.frame.tooltip", "color.frame", "mount panel popup")) darken.type = Image.Type.Sliced;
            if (hud.m_mountIcon)
            {
                _art.Content(hud.m_mountIcon, "mount portrait (creature trophy icon)");
                var ir = hud.m_mountIcon.rectTransform;
                var socket = _art.Image("RuneSkin_PortraitSocket", ir.parent, "sprite.button.round", "color.frame", Image.Type.Simple);
                var sr = socket.rectTransform;
                sr.SetSiblingIndex(ir.GetSiblingIndex());
                sr.anchorMin = ir.anchorMin;
                sr.anchorMax = ir.anchorMax;
                sr.pivot = ir.pivot;
                sr.anchoredPosition = ir.anchoredPosition;
                sr.sizeDelta = ir.sizeDelta * ThemeTokens.GetFloat("scale.mount.socket", 1.6f);
                socket.preserveAspect = true;
            }
            foreach (var group in new[] { "Health", "Stamina" })
                if (panel.Find(group) is Transform g && PlayHud.Child(g, "bkg") is Image bkg && _art.Reskin(bkg, "sprite.bar.skill.trough", "color.frame", $"mount {group} trough"))
                    bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, hud.m_mountHealthBarFast, "sprite.bar.skill.fill", "color.hud.health", "mount health");
            PlayHud.Fill(_art, hud.m_mountHealthBarSlow, "sprite.bar.skill.fill", "color.hud.lag", "mount health lag");
            PlayHud.Fill(_art, hud.m_mountStaminaBar, "sprite.bar.skill.fill", "color.hud.stamina", "mount stamina");
            PlayHud.Text(_art, hud.m_mountNameText, "font.display", "mount", "color.text.header", "mount.name", "mount name (serif)");
            PlayHud.Text(_art, hud.m_mountHealthText, "font.body", "hudvalue", "color.hud.text", "mount.value", "mount health value");
            PlayHud.Text(_art, hud.m_mountStaminaText, "font.body", "hudvalue", "color.hud.text", "mount.value", "mount stamina value");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var t = _hud ? _hud.m_eventName : null;
            if (!t || !_ruleL || !_ruleR || t.text == _eventText || !t.gameObject.activeInHierarchy) return;
            _eventText = t.text;
            t.ForceMeshUpdate();
            var b = t.textBounds;
            var gap = ThemeTokens.GetFloat("size.event.rule.gap", 16f);
            var x = PlayHudMath.RuleOffset(b.size.x, gap);
            var tr = t.rectTransform;
            var centre = tr.anchoredPosition + new Vector2(b.center.x, b.center.y);
            _ruleR.anchoredPosition = centre + new Vector2(x, 0f);
            _ruleL.anchoredPosition = centre - new Vector2(x, 0f);
            if (_band)
            {
                var br = _band.rectTransform;
                var pad = ThemeTokens.GetFloat("size.message.band.pad", 70f);
                br.sizeDelta = new Vector2(2f * (x + _ruleR.sizeDelta.x) + pad, Mathf.Max(br.sizeDelta.y, b.size.y + 2f * ThemeTokens.GetFloat("size.message.band.vpad", 8f)));
            }
        }
    }

    // ---- Ship HUD ------------------------------------------------------------------------------------------------

    // Ship HUD (mockup m6-ship-b): a vertical popup column, top-down sail full / sail half / oar / stop / reverse,
    // the active speed in slot_cell_selected with its glyph in the rune glow and its label lit; each row carries a
    // vanilla-token label (PlayHudMath.LadderTokens) left of its cell. Under it the rudder row: arrow_left | rudder in a
    // button_round socket (glyph turned by Ship.GetRudderValue) | arrow_right, the turned side lit
    // (PlayHudMath.RudderSideAlpha). Wind stays on the minimap ring (M4). Vanilla's icons (PowerIcon, Controls
    // following the tiller, WindIndicator) stay alive for Hud.UpdateShipHud but are drawn at alpha 0.
    // Placement (tester 2026-10-26: the ladder sat on the vanilla PowerIcon spot, under the RuneSkin minimap ring):
    // screen px from the minimap footprint (Minimap.m_smallRoot + its plaque / key tip children, reserved whether the
    // small map shows or not) and the HUD strip top, PlayHudMath.ShipPlacement; hidden while the large map is open.
    public sealed class ShipHudSkin : MonoBehaviour
    {
        public static ShipHudSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.ShipHud);
        private static readonly string[] Glyphs = { "sprite.glyph.ship.sail.full", "sprite.glyph.ship.sail.half", "sprite.glyph.ship.oar", "sprite.glyph.stop", "sprite.glyph.chevron.down" };
        private Hud _hud;
        private RectTransform _column;
        private CanvasGroup _group;
        private readonly Image[] _cells = new Image[PlayHudMath.LadderCells];
        private readonly Image[] _glyphs = new Image[PlayHudMath.LadderCells];
        private readonly TMP_Text[] _labels = new TMP_Text[PlayHudMath.LadderCells];
        private Image _left, _right;
        private RectTransform _rudder;
        private int _shown = -1;
        private float _rudderShown = float.NaN;
        private bool _hiddenForMap;
        private Vector2 _blockUnits, _placedAt = new Vector2(float.NaN, float.NaN);
        private readonly Vector3[] _corners = new Vector3[4];
        public int ActiveIndex => _shown;
        // Harness read side: the last placement (screen px top-right corner) and whether it went beside the minimap.
        public Vector2 PlacedTopRight => _placedAt;
        public bool PlacedBeside { get; private set; }

        public static void Attach(Hud hud)
        {
            if (!hud || !hud.m_shipHudRoot || hud.GetComponent<ShipHudSkin>()) return;
            hud.gameObject.AddComponent<ShipHudSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            var root = hud.m_shipHudRoot.transform;
            PlayHud.SyncPpu(_art, root);
            foreach (var t in new[] { hud.m_fullSail ? hud.m_fullSail.transform.parent?.parent : null, hud.m_shipControlsRoot ? hud.m_shipControlsRoot.transform : null, hud.m_shipWindIndicatorRoot })
            {
                if (!t) continue;
                var g = t.GetComponent<CanvasGroup>() ?? t.gameObject.AddComponent<CanvasGroup>();
                g.alpha = 0f;
                g.blocksRaycasts = false;
                RuneSkin.Restyle.RenderGate.Set(t, "shiphud", true); // not drawn (vanilla code keeps writing it)
                _art.Suppressed(t, $"vanilla ship HUD '{t.name}' (RuneSkin ladder replaces it; wind on the minimap ring)");
            }
            var cell = ThemeTokens.GetFloat("size.ship.cell", 46f);
            var gap = ThemeTokens.GetFloat("size.ship.gap", 6f);
            var pad = ThemeTokens.GetFloat("size.ship.pad", 12f);
            var labelGap = ThemeTokens.GetFloat("size.ship.label.gap", 8f);
            var rs = ThemeTokens.GetFloat("size.ship.rudder", 64f);
            var arrow = ThemeTokens.GetFloat("size.ship.arrow", 28f);
            var ladderH = PlayHudMath.LadderHeight(cell, gap, pad);
            _column = HudArt.Rect("RuneSkin_ShipLadder", root);
            _column.anchorMin = _column.anchorMax = new Vector2(1f, 1f);
            _column.pivot = new Vector2(1f, 1f);
            _group = _column.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var popup = _art.Image("Popup", _column, "sprite.frame.tooltip", "color.frame", Image.Type.Sliced);

            // Row labels first (their widest one sizes the column).
            var labelSize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.ship.label", 18f));
            var labelW = 0f;
            for (var i = 0; i < PlayHudMath.LadderCells; i++)
            {
                var token = PlayHudMath.LadderTokens[i];
                var text = Localization.instance != null ? Localization.instance.Localize(token) : token;
                var label = _art.Text("Label" + i, _column, "font.body", "color.text.body", labelSize, TextAlignmentOptions.MidlineRight);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.text = text;
                labelW = Mathf.Max(labelW, label.GetPreferredValues(text).x);
                _labels[i] = label;
            }
            labelW = Mathf.Ceil(labelW);
            var block = PlayHudMath.ShipBlock(cell, gap, pad, labelW, labelGap, rs, arrow);
            _blockUnits = new Vector2(block.width, block.height);
            _column.sizeDelta = _blockUnits;
            var ladderW = 2f * pad + labelW + (labelW > 0f ? labelGap : 0f) + cell;
            var pr = popup.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1f, 1f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(ladderW, ladderH);

            for (var i = 0; i < PlayHudMath.LadderCells; i++)
            {
                var top = PlayHudMath.LadderCellTop(i, cell, gap, pad);
                var c = _art.Image("Cell" + i, _column, "sprite.slot.cell.selected", "color.frame", Image.Type.Sliced);
                var cr = c.rectTransform;
                cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 1f);
                cr.anchoredPosition = new Vector2(-pad, -top);
                cr.sizeDelta = new Vector2(cell, cell);
                c.gameObject.SetActive(false);
                _cells[i] = c;
                var g = _art.Image("Glyph" + i, _column, Glyphs[i], "color.text.body", Image.Type.Simple);
                g.preserveAspect = true;
                var gr = g.rectTransform;
                gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(1f, 1f);
                var inset = cell * ThemeTokens.GetFloat("share.ship.glyph.inset", 0.18f);
                gr.anchoredPosition = new Vector2(-pad - inset, -top - inset);
                gr.sizeDelta = new Vector2(cell - 2f * inset, cell - 2f * inset);
                _glyphs[i] = g;
                var lr = _labels[i].rectTransform;
                lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(1f, 1f);
                lr.anchoredPosition = new Vector2(-pad - cell - labelGap, -top);
                lr.sizeDelta = new Vector2(labelW, cell);
            }

            // Rudder row, centred under the block: arrow_left | socket + rudder glyph | arrow_right.
            var rowY = -ladderH - gap;
            var socket = _art.Image("RudderSocket", _column, "sprite.button.round", "color.frame", Image.Type.Simple);
            socket.preserveAspect = true;
            var sr = socket.rectTransform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0.5f, 1f);
            sr.anchoredPosition = new Vector2(0f, rowY);
            sr.sizeDelta = new Vector2(rs, rs);
            var rudder = _art.Image("Rudder", socket.rectTransform, "sprite.glyph.rudder", "color.text.active", Image.Type.Simple);
            rudder.preserveAspect = true;
            _rudder = rudder.rectTransform;
            _rudder.anchorMin = _rudder.anchorMax = _rudder.pivot = new Vector2(0.5f, 0.5f);
            _rudder.anchoredPosition = Vector2.zero;
            _rudder.sizeDelta = new Vector2(rs, rs) * ThemeTokens.GetFloat("share.ship.rudder.glyph", 0.72f);
            _left = Arrow("RudderLeft", "sprite.glyph.arrow.left", -(rs * 0.5f + gap + arrow * 0.5f), rowY - (rs - arrow) * 0.5f, arrow);
            _right = Arrow("RudderRight", "sprite.glyph.arrow.right", rs * 0.5f + gap + arrow * 0.5f, rowY - (rs - arrow) * 0.5f, arrow);
            Plugin.Log.LogInfo($"[RuneSkin] ship HUD attached (block {block.width:0}x{block.height:0} units, labels {labelW:0} units)");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        private Image Arrow(string name, string sprite, float x, float y, float size)
        {
            var a = _art.Image(name, _column, sprite, "color.text.body", Image.Type.Simple);
            a.preserveAspect = true;
            var r = a.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(size, size);
            return a;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var player = Player.m_localPlayer;
            var ship = player ? player.GetControlledShip() : null;
            if (!ship || !_column) return;
            HideForLargeMap();
            Place();
            var index = PlayHudMath.LadderIndex((int)ship.GetSpeedSetting());
            if (index != _shown)
            {
                _shown = index;
                var on = ThemeTokens.GetColor("color.hud.glyph", Color.cyan);
                var off = ThemeTokens.GetColor("color.text.body", Color.white);
                off.a = ThemeTokens.GetFloat("share.ship.idle.alpha", 0.55f);
                var labelOff = ThemeTokens.GetColor("color.text.body", Color.white);
                labelOff.a = ThemeTokens.GetFloat("share.ship.label.idle.alpha", 0.75f);
                for (var i = 0; i < PlayHudMath.LadderCells; i++)
                {
                    if (_cells[i].gameObject.activeSelf != (i == index)) _cells[i].gameObject.SetActive(i == index);
                    _glyphs[i].color = i == index ? on : off;
                    if (_labels[i]) _labels[i].color = i == index ? on : labelOff;
                }
            }
            var value = ship.GetRudderValue();
            var angle = PlayHudMath.RudderAngle(value, ThemeTokens.GetFloat("size.ship.rudder.deg", 60f));
            if (Mathf.Abs(_rudder.localEulerAngles.z - Mathf.Repeat(angle, 360f)) > 0.1f) _rudder.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (!(Mathf.Abs(value - _rudderShown) < 0.005f))
            {
                _rudderShown = value;
                var idle = ThemeTokens.GetFloat("share.ship.idle.alpha", 0.55f) * 0.6f;
                var minLit = ThemeTokens.GetFloat("share.ship.rudder.lit.min", 0.65f);
                Side(_left, PlayHudMath.RudderSideAlpha(value, -1, idle, minLit), idle);
                Side(_right, PlayHudMath.RudderSideAlpha(value, 1, idle, minLit), idle);
            }
        }

        private static void Side(Image arrow, float alpha, float idle)
        {
            if (!arrow) return;
            var c = alpha > idle ? ThemeTokens.GetColor("color.hud.glyph", Color.cyan) : ThemeTokens.GetColor("color.text.body", Color.white);
            c.a = alpha;
            arrow.color = c;
        }

        // The large map covers the screen (the HUD cluster hides with it, LargeMap.HideHudCluster): the ladder too.
        private void HideForLargeMap()
        {
            var map = Minimap.instance;
            var hide = map && map.m_largeRoot && map.m_largeRoot.activeInHierarchy;
            if (hide == _hiddenForMap) return;
            _hiddenForMap = hide;
            _group.alpha = hide ? 0f : 1f;
            RuneSkin.Restyle.RenderGate.Set(_column, "shiphud-map", hide);
        }

        // Screen placement (PlayHudMath.ShipPlacement) from the live minimap footprint and strip top; moved only when
        // the result changes by half a pixel or more (StripClear re-homes an element its owner moves).
        private void Place()
        {
            var parent = _column.parent as RectTransform;
            var canvas = parent ? parent.GetComponentInParent<Canvas>() : null;
            if (!canvas) return;
            var cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            var origin = RectTransformUtility.WorldToScreenPoint(cam, parent.position);
            var unit = RectTransformUtility.WorldToScreenPoint(cam, parent.position + parent.TransformVector(Vector3.up)).y - origin.y;
            if (!(unit > 0f)) return;
            var safe = Responsive.ResponsiveContext.SafeArea;
            HudMath.HudBox(safe.x, safe.width, safe.height, Plugin.UltrawideBox?.Value ?? true, ThemeTokens.GetFloat("ratio.hud.ultrawide", 2.2f), out var boxX, out var boxW);
            var s = HudMath.FrameScale(Responsive.ResponsiveContext.GuiFactor, Plugin.HudScale?.Value ?? 1f); // = the strip's gaps
            var margin = ThemeTokens.GetFloat("size.hud.margin", 24f) * s;
            if (!Footprint(out var fp)) fp = new Rect(boxX + boxW - margin, safe.yMax - margin, 0f, 0f);
            var strip = HudCluster.Instance ? HudCluster.Instance.StripTopPx : 0f;
            var floor = strip > 0f ? strip + ThemeTokens.GetFloat("size.hud.strip.clear.gap", 12f) * s : safe.y + margin;
            var gapPx = ThemeTokens.GetFloat("size.ship.screen.gap", 12f) * s;
            var p = PlayHudMath.ShipPlacement(fp.xMin, fp.xMax, fp.yMin, fp.yMax, boxX + margin, floor, _blockUnits.x * unit, _blockUnits.y * unit, gapPx);
            var want = new Vector2(p.right, p.top);
            if (Mathf.Abs(want.x - _placedAt.x) < 0.5f && Mathf.Abs(want.y - _placedAt.y) < 0.5f) return;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, want, cam, out var world)) return;
            _column.position = world;
            _placedAt = want;
            PlacedBeside = p.beside;
            Plugin.Log.LogInfo($"[RuneSkin] ship HUD placed top-right ({want.x:0},{want.y:0}) px {(p.beside ? "beside" : "under")} the minimap footprint [{fp.xMin:0},{fp.yMin:0}]-[{fp.xMax:0},{fp.yMax:0}] (floor {floor:0} px)");
        }

        // Minimap footprint, screen px: the small root plus its direct children (ring art, biome plaque, key tip; the
        // RuneSkin ones always, others while active), from their rects - valid while the small map is hidden too.
        private bool Footprint(out Rect r)
        {
            r = default;
            var map = Minimap.instance;
            var small = map && map.m_smallRoot ? map.m_smallRoot.transform as RectTransform : null;
            if (!small) return false;
            var canvas = small.GetComponentInParent<Canvas>(true);
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            void Add(RectTransform rt)
            {
                if (rt.rect.width < 1f || rt.rect.height < 1f) return;
                rt.GetWorldCorners(_corners);
                for (var k = 0; k < 4; k++)
                {
                    var sp = RectTransformUtility.WorldToScreenPoint(cam, _corners[k]);
                    x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                    y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
                }
            }
            Add(small);
            for (var i = 0; i < small.childCount; i++)
            {
                var c = small.GetChild(i) as RectTransform;
                if (!c) continue;
                if (c.gameObject.activeSelf || c.name.StartsWith("RuneSkin_", StringComparison.Ordinal) || c.name == "Key tip") Add(c);
            }
            if (x0 == float.MaxValue) return false;
            r = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }
    }

    // ---- Key hints -------------------------------------------------------------------------------------------------

    // Key hints (hudroot/KeyHints: combat, build, inventory, fishing, barber, radial groups; KeyHints.UpdateHints
    // toggles them): keyboard key plates ("key_bkg") = plate_key, key letters + labels in the outlined body sans;
    // controller / mouse glyph art kept. Template-free: every group exists at Awake.
    public sealed class KeyHintsSkin : MonoBehaviour
    {
        public static KeyHintsSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.KeyHints);

        public static void Attach(Hud hud)
        {
            var hints = hud ? hud.GetComponentInChildren<KeyHints>(true) : null;
            if (!hints || hints.GetComponent<KeyHintsSkin>()) return;
            hints.gameObject.AddComponent<KeyHintsSkin>().Init(hints);
        }

        private void Init(KeyHints hints)
        {
            Instance = this;
            var root = hints.transform;
            PlayHud.SyncPpu(_art, root);
            Restyle(root);
            Plugin.Log.LogInfo("[RuneSkin] key hints attached");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        // Also used by BuildHud for the build menu's own keyboard hints.
        internal void Restyle(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "key_bkg")
                {
                    if (_art.Reskin(img, "sprite.hud.plate.key", "color.frame", "key plate")) img.type = Image.Type.Sliced;
                }
                else _art.Content(img, img.sprite ? $"key hint glyph art '{img.sprite.name}' (controller / mouse)" : "key hint layout image");
            }
            var keySize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.keyhint.key", 17f));
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var key = t.transform.parent && t.transform.parent.name == "key_bkg";
                PlayHud.Text(_art, t, "font.body", key ? "key" : "hint", key ? "color.text.active" : "color.text.body", key ? "keyhint.key" : "keyhint.label",
                    key ? "key letter on its plate" : "key hint label (outlined body sans)");
                if (key && t.enableAutoSizing) t.fontSizeMax = Mathf.Max(t.fontSizeMax, keySize);
            }
            foreach (var s in root.GetComponentsInChildren<TMP_SubMeshUI>(true)) _art.Content(s, "TMP sub-mesh");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }

    // ---- Harmony -------------------------------------------------------------------------------------------------

    [HarmonyPatch]
    public static class PlayHudAdapter
    {
        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(Hud __instance)
        {
            PlayHud.Reports.Clear();
            Try("crosshair", OwnershipRegistry.Crosshair, () => CrosshairSkin.Attach(__instance));
            Try("action HUD", OwnershipRegistry.ActionHud, () => ActionHudSkin.Attach(__instance));
            Try("ship HUD", OwnershipRegistry.ShipHud, () => ShipHudSkin.Attach(__instance));
            Try("key hints", OwnershipRegistry.KeyHints, () => KeyHintsSkin.Attach(__instance));
            Try("build HUD", OwnershipRegistry.BuildHud, () => BuildHudSkin.Attach(__instance));
        }

        private static void Try(string what, string family, Action attach)
        {
            if (!PlayHud.Owned(family)) return;
            try { attach(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] {what} restyle failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
        [HarmonyPostfix]
        private static void UpdateCrosshair_Postfix()
        {
            var c = CrosshairSkin.Instance;
            if (c) c.AfterCrosshair();
        }

        // Piece / tag button templates are cloned from BuildUi.Awake on (BuildUi.cs:154 the first tag button): styled
        // before it runs, every clone copies the look.
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.Awake))]
        [HarmonyPrefix]
        private static void BuildUiAwake_Prefix(BuildUi __instance)
        {
            if (!PlayHud.Owned(OwnershipRegistry.BuildHud)) return;
            try { BuildHudSkin.Templates(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] build menu templates restyle failed: {e}"); }
        }

        // BuildUi lays its tag list / piece grid out on open (OpenBuildMenu); restyle again so pooled clones made after
        // Awake that missed the template (mod tags) are covered.
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OpenBuildMenu))]
        [HarmonyPostfix]
        private static void BuildUiOpen_Postfix(BuildUi __instance)
        {
            if (!PlayHud.Owned(OwnershipRegistry.BuildHud)) return;
            try { BuildHudSkin.Refresh(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] build menu refresh failed: {e}"); }
        }
    }
}
