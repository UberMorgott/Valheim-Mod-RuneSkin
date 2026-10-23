using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // One resource bar (hud-resource-states §2-§3). Z-order inside the window: trough -> damage-lag ghost -> fill ->
    // DoT stripes -> reserved hatch (+ edge, lock rune); then the frame, the primary overlay and the numbers.
    // Geometry in reference px; the window (inner rect of the carved frame) is length L x height H, the frame extends
    // inset.x / inset.y around it (art: window insets per frame sprite, theme floats window.<asset>.*).
    // Bars have one fixed outer width (the max is a number, not a length); a real max step pulses the value and the
    // frame instead. Writes only when a value moved; lag ease and the pulse run on their own clocks.
    public sealed class BarView
    {
        public const int MaxStripes = 3;

        public RectTransform Root;
        private RectTransform _window;
        private Image _trough, _lag, _fill, _reserved, _reservedEdge, _lock, _frame, _overlay, _paused, _glyph;
        private Vector2 _glyphAt; private float _glyphSize;
        // Fill-end effect (mockup: HP glossy glow front, stamina sheen, eitr wisps): one Simple image riding the fill
        // end, its core at frontCore of its width; alpha breathes (canvasRenderer alpha: no mesh rebuild).
        private Image _front;
        // Fill-end rim (M6 edge pass, Codex pick B: crisp 1-2 px bright bevel, no glow): bar_edge mask tinted per bar,
        // its right edge on the fill end (and on the shield band's end). Theme float mode.hud.bar.edge: 0 = the
        // older breathing front FX, 1 = clean end (no FX), 2 = rim. EdgeModeOverride (>= 0) wins: harness variants.
        private Image _edge, _shieldEdge;
        public static int EdgeModeOverride = -1;
        // Right end cap (0.10.2, user: the brown leather ends were "убогие"): frames are open at the right (rails run to
        // the edge), one dark-metal cap per bar covers the rail band there - equal width size.hud.bar.cap on every bar,
        // height = the bar's rail band, so stacked caps form one column; the HP cap holds the rune knot. The window
        // ends at the cap's flat left edge and clips (RectMask2D): a full fill / the trough run under the cap and end
        // squarely there instead of showing their rounded end before it. CapVariantOverride ("a" / "b" / "c", null =
        // theme default) swaps every cap to token "<cap token>.<variant>" live: harness variant crops.
        private Image _cap;
        private readonly string _capToken;
        private string _shownCapVariant;
        private float _capW, _overFill, _overTrough, _overLag;
        public static string CapVariantOverride;
        // Plain (HUD strip, mockup hud-3): the bar shows its fill, lag, shield band and the plain value only - no DoT
        // stripes, reserve hatch / edge / lock, paused rune, "cur/avail" or "+ shield" texts.
        public bool Plain;
        // Shield as a thin line above the bar (older strip) instead of the band inside the window.
        public bool ShieldLine;
        // Shield as a glow around the bar's perimeter (mockup hud-4M3): lit from the left end cap along the top and the
        // bottom edges together, the right cap at full; plus a faint sheen over the fill, both shield / max long.
        public bool ShieldPerimeter;
        private Image _shCapL, _shMid, _shCapR, _sheen;
        private static int EdgeMode => EdgeModeOverride >= 0 ? EdgeModeOverride : (int)ThemeTokens.GetFloat("mode.hud.bar.edge", 2f);
        private float _frontCore, _frontClock;
        // Shield layer (health bar only): translucent shield-coloured strip over the fill, HudMath.ShieldOverlay wide.
        private Image _shield;
        private float _shieldValue, _shownShield = -1f;
        // Wet: a few single drip clusters at irregular spots along the frame bottom (a tiled row read as a zipper).
        private static readonly float[] WetSpots = { 0.07f, 0.38f, 0.79f };
        private readonly Image[] _wet = new Image[WetSpots.Length];
        private readonly Image[] _stripes = new Image[MaxStripes];
        private TMP_Text _number, _aux;
        private readonly HudArt _art;
        private readonly bool _rail;
        private readonly string _fillColor;
        private readonly bool _main;
        private readonly float _scale;

        private float _insetL, _insetR, _insetB, _insetT, _h;
        private float _length = -1f;
        private float _lagValue = -1f, _lagHold, _lastCur = -1f;
        private float _shownCur = -1f, _shownAvail = -1f, _shownExtra = -1f;
        private int _shownStripes = -1;
        private string _shownFill, _shownOverlaySprite;
        private bool _shownInert;
        private float _lastMax = -1f, _pulse;
        private Color _textColor, _frameColor;

        // scale: the frame drawn smaller than its sprite (borders follow through HudArt.Track, geometry through
        // the same factor). mainValue: the value is centred and large with the glow material (health); else small,
        // right-aligned.
        public BarView(HudArt art, Transform parent, string name, bool rail, string frameToken, string fillColorToken,
            float scale = 1f, bool mainValue = false, string glyphToken = null, string frontToken = null, string frontColorToken = null,
            float frontCore = 0.5f, string capToken = null, string fillToken = null, string troughColorToken = null, string capWidthToken = null, string shieldToken = null, string troughToken = null)
        {
            _art = art;
            _rail = rail;
            _fillColor = fillColorToken;
            _main = mainValue;
            Root = HudArt.Rect(name, parent);
            FrameGeometry(frameToken, out _insetL, out _insetR, out _insetB, out _insetT, out _h);
            scale = Mathf.Clamp(scale, 0.25f, 4f);
            _insetL *= scale;
            _insetR *= scale;
            _insetB *= scale;
            _insetT *= scale;
            _h *= scale;
            _scale = scale;
            FrameEdges(frameToken, out var edgeT, out var edgeB);
            EdgeTop = edgeT * scale;
            EdgeBottom = edgeB * scale;
            if (capToken != null && ThemeTokens.GetSprite(capToken))
            {
                _capToken = capToken;
                _capW = ThemeTokens.GetFloat(capWidthToken ?? "size.hud.bar.cap", 34f);
                _insetR = _capW;
            }
            _window = HudArt.Rect("Window", Root);
            _window.anchoredPosition = new Vector2(_insetL, _insetB);
            _trough = art.Image("Trough", _window, rail ? "sprite.hud.rail.trough" : troughToken ?? "sprite.hud.bar.trough", troughColorToken ?? "color.hud.trough", Image.Type.Sliced);
            _lag = art.Image("Lag", _window, rail ? "sprite.hud.rail.fill" : "sprite.hud.bar.lag", "color.hud.lag", Image.Type.Sliced);
            // fillToken: a fill texture of its own (HUD strip: cut from the mockup, untinted), else the shared grey mask tinted.
            _fill = art.Image("Fill", _window, rail ? "sprite.hud.rail.fill" : fillToken ?? "sprite.hud.bar.fill", fillColorToken, Image.Type.Sliced);
            if (!rail && ThemeTokens.GetSprite("sprite.hud.bar.edge"))
            {
                _edge = art.Image("Edge", _window, "sprite.hud.bar.edge", HudMath.EdgeColorToken(fillColorToken), Image.Type.Simple);
                _edge.gameObject.SetActive(false);
            }
            if (frontToken != null && ThemeTokens.GetSprite(frontToken))
            {
                _front = art.Image("Front", _window, frontToken, frontColorToken, Image.Type.Simple);
                _frontCore = Mathf.Clamp01(frontCore);
                _front.gameObject.SetActive(false);
            }
            if (!rail)
                for (var i = 0; i < MaxStripes; i++)
                {
                    _stripes[i] = art.Image("Stripe" + i, _window, "sprite.hud.dot.poison", "color.dot.other", Image.Type.Tiled);
                    _stripes[i].gameObject.SetActive(false);
                }
            if (mainValue)
            {
                _shield = art.Image("Shield", _window, shieldToken ?? "sprite.hud.bar.fill", "color.hud.shield", shieldToken != null ? Image.Type.Simple : Image.Type.Sliced);
                _shield.gameObject.SetActive(false);
                if (_edge)
                {
                    _shieldEdge = art.Image("ShieldEdge", _window, "sprite.hud.bar.edge", HudMath.EdgeColorToken("color.hud.shield"), Image.Type.Simple);
                    _shieldEdge.gameObject.SetActive(false);
                }
            }
            if (mainValue && ThemeTokens.GetSprite("sprite.hud.strip4.shield.mid"))
            {
                _sheen = art.Image("ShieldSheen", _window, "sprite.hud.strip4.fill", "color.hud.shield.sheen", Image.Type.Sliced);
                _sheen.gameObject.SetActive(false);
            }
            _reserved = art.Image("Reserved", _window, "sprite.hud.reserved", "color.hud.reserve", Image.Type.Tiled);
            _reservedEdge = art.Image("ReservedEdge", _window, "sprite.hud.reserved.edge", "color.hud.reserve.edge", Image.Type.Simple);
            _lock = art.Image("Lock", _window, "sprite.hud.glyph.lock", "color.hud.glyph", Image.Type.Simple);
            _reserved.gameObject.SetActive(false);
            _reservedEdge.gameObject.SetActive(false);
            _lock.gameObject.SetActive(false);
            _frame = art.Image("Frame", Root, frameToken, "color.hud.frame", Image.Type.Tiled);
            _frameColor = _frame.color;
            if (_capToken != null)
            {
                _cap = art.Image("Cap", Root, _capToken, "color.hud.frame", Image.Type.Simple);
                _window.gameObject.AddComponent<RectMask2D>();
                _overFill = RightBorder(_fill, scale);
                _overTrough = RightBorder(_trough, scale);
                _overLag = RightBorder(_lag, scale);
            }
            if (glyphToken != null) Glyph(frameToken, glyphToken, scale);
            if (_sheen)
            {
                _shCapL = art.Image("ShieldCapL", Root, "sprite.hud.strip4.shield.l", "color.hud.shield.glow", Image.Type.Simple);
                _shMid = art.Image("ShieldRun", Root, "sprite.hud.strip4.shield.mid", "color.hud.shield.glow", Image.Type.Tiled);
                _shCapR = art.Image("ShieldCapR", Root, "sprite.hud.strip4.shield.r", "color.hud.shield.glow", Image.Type.Simple);
                _shCapL.gameObject.SetActive(false);
                _shMid.gameObject.SetActive(false);
                _shCapR.gameObject.SetActive(false);
            }
            if (!rail)
            {
                _overlay = art.Image("Overlay", Root, "sprite.hud.overlay.frost", "color.overlay.frost", Image.Type.Simple);
                _overlay.gameObject.SetActive(false);
                for (var i = 0; i < _wet.Length; i++)
                {
                    // Three drip variants alternate along the frame (art f907652: overlay_wet, _2, _3); a missing variant falls back.
                    var token = i == 0 || !ThemeTokens.GetSprite("sprite.hud.overlay.wet." + (i + 1)) ? "sprite.hud.overlay.wet" : "sprite.hud.overlay.wet." + (i + 1);
                    _wet[i] = art.Image("Wet" + i, Root, token, "color.overlay.wet", Image.Type.Simple);
                    _wet[i].gameObject.SetActive(false);
                }
            }
            var numberSize = ThemeTokens.GetFloat(mainValue ? "size.hud.font.value" : rail ? "size.hud.font.timer" : "size.hud.font.minor", 16f);
            // Values in the body sans (lining digits; the display serif has old-style ones) with a dark outline and a
            // slight dilate, readable on any fill (user call 2026-10-22, as the damage numbers).
            _number = art.Text("Value", Root, "font.body", "color.hud.text", numberSize,
                TextAlignmentOptions.Center); // every bar: value centred in the window, both axes (user call 2026-10-22)
            WorldHud.TextLook.ApplyHudNumber(_number);
            _number.richText = mainValue;
            _textColor = _number.color;
            _aux = art.Text("Aux", Root, "font.body", "color.hud.timer", ThemeTokens.GetFloat("size.hud.font.timer", 13f), TextAlignmentOptions.MidlineLeft);
            _aux.gameObject.SetActive(false);
            _paused = art.Image("Paused", Root, "sprite.hud.glyph.seal", "color.hud.glyph", Image.Type.Simple);
            _paused.gameObject.SetActive(false);
            foreach (var img in Root.GetComponentsInChildren<Image>(true))
                art.Track(img, scale);
        }

        // Window geometry (the trough/fill rect inside the carved frame), in reference px (sprite px x 100 / PPU, the
        // reference PPU; HudArt.Track maps the canvas' own reference onto it). The art states the window per frame
        // sprite in sprite px as theme floats "window.<asset>.left/right/top/bottom" (art\sprites\sprites.json notes);
        // without them the 9-slice border is the window inset. The window height is the frame height minus top and
        // bottom insets.
        public static void FrameGeometry(string frameToken, out float left, out float right, out float bottom, out float top, out float height)
        {
            var sprite = ThemeTokens.GetSprite(frameToken);
            left = right = bottom = top = 0f;
            height = 20f;
            if (!sprite) return;
            var toRef = 100f / Mathf.Max(1f, sprite.pixelsPerUnit);
            var b = sprite.border; // (left, bottom, right, top)
            var key = "window." + (sprite.name.EndsWith("(Clone)", System.StringComparison.Ordinal) ? sprite.name.Substring(0, sprite.name.Length - 7) : sprite.name);
            left = ThemeTokens.GetFloat(key + ".left", b.x) * toRef;
            right = ThemeTokens.GetFloat(key + ".right", b.z) * toRef;
            bottom = ThemeTokens.GetFloat(key + ".bottom", b.y) * toRef;
            top = ThemeTokens.GetFloat(key + ".top", b.w) * toRef;
            height = Mathf.Max(2f, sprite.rect.height * toRef - bottom - top);
        }
        // Pool glyph in the frame's icon recess: the art states the recess per frame sprite as theme floats
        // "recess.<asset>.x/y/r" (sprite px, y from the top; art\sprites\sprites.json notes). Without them, or
        // without the glyph sprite, no glyph is drawn.
        private void Glyph(string frameToken, string glyphToken, float scale)
        {
            var frame = ThemeTokens.GetSprite(frameToken);
            if (!frame || !ThemeTokens.GetSprite(glyphToken)) return;
            var key = "recess." + (frame.name.EndsWith("(Clone)", System.StringComparison.Ordinal) ? frame.name.Substring(0, frame.name.Length - 7) : frame.name);
            var r = ThemeTokens.GetFloat(key + ".r", 0f);
            if (!(r > 0f)) return;
            var toRef = 100f / Mathf.Max(1f, frame.pixelsPerUnit) * scale;
            _glyphAt = new Vector2(ThemeTokens.GetFloat(key + ".x", 0f), frame.rect.height - ThemeTokens.GetFloat(key + ".y", 0f)) * toRef;
            _glyphSize = 2f * r * ThemeTokens.GetFloat("share.hud.glyph.recess", 0.9f) * toRef;
            _glyph = _art.Image("Glyph", Root, glyphToken, "color.hud.glyph", Image.Type.Simple);
            _glyph.preserveAspect = true;
        }

        // Transparent rows above / below the frame's rail (ref px, scaled): the art states them per frame sprite in
        // sprite px as theme floats "edge.<asset>.top/bottom" (measured from the sprite's alpha along the rail).
        // HudCluster stacks frames rail to rail through them (HudMath.StackStep). Without them: 0, frames edge to edge.
        public static void FrameEdges(string frameToken, out float top, out float bottom)
        {
            top = bottom = 0f;
            var sprite = ThemeTokens.GetSprite(frameToken);
            if (!sprite) return;
            var toRef = 100f / Mathf.Max(1f, sprite.pixelsPerUnit);
            var key = "edge." + (sprite.name.EndsWith("(Clone)", System.StringComparison.Ordinal) ? sprite.name.Substring(0, sprite.name.Length - 7) : sprite.name);
            top = ThemeTokens.GetFloat(key + ".top", 0f) * toRef;
            bottom = ThemeTokens.GetFloat(key + ".bottom", 0f) * toRef;
        }

        // Right border of a window sprite in reference px at the bar scale (how far its rounded / fading end reaches).
        private static float RightBorder(Image img, float scale)
        {
            var s = img ? img.sprite : null;
            return s ? s.border.z * 100f / Mathf.Max(1f, s.pixelsPerUnit) * scale : 0f;
        }

        // Cap variant swap (harness crops): restyles the cap when CapVariantOverride changed since the last frame.
        private void SyncCap()
        {
            if (!_cap || _shownCapVariant == CapVariantOverride) return;
            _shownCapVariant = CapVariantOverride;
            var token = string.IsNullOrEmpty(CapVariantOverride) ? _capToken : _capToken + "." + CapVariantOverride;
            _art.Restyle(_cap, ThemeTokens.GetSprite(token) ? token : _capToken, "color.hud.frame", Image.Type.Simple);
        }

        // Right-aligned values keep clear of the frame's end cap (Codex band review: numbers lost in the caps).

        public float EdgeTop { get; }
        public float EdgeBottom { get; }

        public float Height => _h + _insetB + _insetT;
        public float Width => Mathf.Max(0f, _length) + _insetL + _insetR;
        public float WindowLength => _length;
        // Frame width outside the window (left + right caps).
        public float Caps => _insetL + _insetR;

        public void SetVisible(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        // Fixed outer width: window = outer - caps (readable floor size.hud.bar.min).
        public void SetOuterWidth(float outer)
        {
            var l = HudMath.FixedWindow(outer, Caps, ThemeTokens.GetFloat("size.hud.bar.min", 160f));
            if (Mathf.Abs(l - _length) < 0.05f) return;
            _length = l;
            _shownCur = -1f; // fill widths follow the window
            Layout();
        }

        // Pool max B: a real step (share.hud.pulse.step) starts the highlight pulse of the value and the frame.
        public void SetMax(float max)
        {
            if (_lastMax >= 0f && HudMath.IsMaxStep(_lastMax, max, ThemeTokens.GetFloat("share.hud.pulse.step", 0.03f)))
                _pulse = 1f;
            _lastMax = max; // per-frame compare: slow food decay never adds up to a pulse
        }

        // Per-frame animation clock (runs even when no value changed).
        public void Tick(float dt)
        {
            SyncCap();
            if (_front && _front.gameObject.activeSelf)
            {
                _frontClock += dt;
                var lo = ThemeTokens.GetFloat("share.hud.front.min", 0.7f);
                _front.canvasRenderer.SetAlpha(Mathf.Lerp(lo, 1f, 0.5f + 0.5f * Mathf.Sin(_frontClock * ThemeTokens.GetFloat("rate.hud.front", 2.4f))));
            }
            if (_pulse <= 0f) return;
            _pulse = Mathf.Max(0f, _pulse - dt / Mathf.Max(0.05f, ThemeTokens.GetFloat("time.hud.pulse", 0.6f)));
            var k = Mathf.Sin(_pulse * Mathf.PI * 0.5f); // full highlight at the step, eases out
            var hi = ThemeTokens.GetColor("color.hud.pulse", new Color(0.31f, 0.88f, 0.9f, 1f));
            _number.color = Color.Lerp(_textColor, hi, k);
            _frame.color = Color.Lerp(_frameColor, Color.Lerp(_frameColor, hi, 0.5f), k);
        }

        private void Layout()
        {
            var l = Mathf.Max(0f, _length);
            _shownRun = -1f; // the shield perimeter follows the width
            Root.sizeDelta = new Vector2(l + _insetL + _insetR, Height);
            _window.sizeDelta = new Vector2(l, _h);
            Full(_trough.rectTransform, _cap ? HudMath.ClippedEnd(l, l, _overTrough) : l);
            Stretch(_frame.rectTransform);
            if (_cap)
            {
                var cr = _cap.rectTransform;
                HudMath.CapBox(_insetL, l, _capW, Height, EdgeTop, EdgeBottom, out var cx, out var cy, out var cw, out var ch);
                cr.anchorMin = cr.anchorMax = cr.pivot = Vector2.zero;
                cr.anchoredPosition = new Vector2(cx, cy);
                cr.sizeDelta = new Vector2(cw, ch);
            }
            if (_glyph)
            {
                var gr = _glyph.rectTransform;
                gr.anchorMin = gr.anchorMax = Vector2.zero;
                gr.pivot = new Vector2(0.5f, 0.5f);
                gr.anchoredPosition = _glyphAt;
                gr.sizeDelta = new Vector2(_glyphSize, _glyphSize);
            }
            if (_overlay) PlaceOverlay();
            FxLayout();
            var nr = _number.rectTransform;
            nr.anchoredPosition = new Vector2(_insetL, _insetB);
            nr.sizeDelta = new Vector2(l, _h);
            var ar = _aux.rectTransform;
            ar.anchoredPosition = new Vector2(_insetL + 4f, _insetB);
            ar.sizeDelta = new Vector2(Mathf.Max(0f, l - 8f), _h);
            var pr = _paused.rectTransform;
            var g = _h + _insetB;
            pr.sizeDelta = new Vector2(g, g);
            pr.anchoredPosition = new Vector2(_insetL + l - 4f - g - 56f, (Height - g) / 2f);
        }

        // Renders one frame of data. cur / max (B) / avail (C) in units; stripes in window px; tint = fill colour
        // token override (one per bar) or null; overlay sprite token or null.
        public void Render(float cur, float max, float avail, HudMath.Stripe[] stripes, int stripeCount, int[] stripeKinds,
            string tint, string overlaySprite, string overlayColor, bool inert, float dt, bool showAvail)
        {
            var l = Mathf.Max(0f, _length);
            if (!(max > 0f)) max = 1f;
            SetMax(max);
            cur = Mathf.Clamp(cur, 0f, max);
            // Damage-lag ghost: absolute value, holds then eases down; heals and max changes never create one.
            // Rising value (regen, heal) snaps the ghost to the fill: a ghost only ever follows a decrease (user live
            // test 2026-10-22: a ghost easing after an earlier hit read as a trail while the bar refilled).
            if (_lagValue < 0f || cur >= _lagValue || cur > _lastCur + 0.001f) { _lagValue = cur; _lagHold = 0f; }
            else
            {
                if (cur < _lastCur - 0.001f) _lagHold = ThemeTokens.GetFloat("time.hud.lag.hold", 0.6f);
                if (_lagHold > 0f) _lagHold -= dt;
                else
                {
                    var ease = Mathf.Max(0.05f, ThemeTokens.GetFloat("time.hud.lag.ease", 0.5f));
                    _lagValue = Mathf.MoveTowards(_lagValue, cur, Mathf.Max(0.5f, (_lagValue - cur)) * dt / ease * 2f);
                }
            }
            _lagValue = Mathf.Min(_lagValue, max);
            _lastCur = cur;

            var fillToken = tint ?? _fillColor;
            if (fillToken != _shownFill || inert != _shownInert)
            {
                _shownFill = fillToken;
                _shownInert = inert;
                _fill.color = ThemeTokens.GetColor(inert ? "color.hud.inert" : fillToken, Color.white);
            }
            {
                var fillW = l * cur / max;
                Place(_fill, 0f, HudMath.ClippedEnd(fillW, l, _overFill), cur > 0f);
                var mode = EdgeMode;
                if (_edge) Edge(_edge, fillW, 0f, _h, mode == 2 && cur > 0f && fillW < l - 0.5f);
                if (_front && mode != 0 && _front.gameObject.activeSelf) _front.gameObject.SetActive(false);
                if (_front && mode == 0)
                {
                    var fs = _front.sprite;
                    var fwid = fs ? _h * fs.rect.width / Mathf.Max(1f, fs.rect.height) : _h;
                    // Rides the fill edge and never reaches past it into the empty trough (user live test 2026-10-22:
                    // the overhanging wisps read as a static trail): the sprite ends at the edge, its core
                    // (_frontCore of its width) just inside; hidden while the fill is shorter than the sprite.
                    var on = fillW >= fwid && fillW < l - 0.5f;
                    if (_front.gameObject.activeSelf != on) _front.gameObject.SetActive(on);
                    if (on)
                    {
                        var fr = _front.rectTransform;
                        fr.sizeDelta = new Vector2(fwid, _h);
                        fr.anchoredPosition = new Vector2(fillW - fwid, 0f);
                    }
                }
                var lagW = l * _lagValue / max;
                Place(_lag, 0f, HudMath.ClippedEnd(lagW, l, _overLag), _lagValue > cur + 0.01f);
                HudMath.Reserved(avail, max, l, out var rs, out var rw);
                var reserve = rw >= 0.5f && !Plain;
                Place(_reserved, rs, rw, reserve);
                Place(_reservedEdge, rs - 1f, 2f, reserve);
                var lockSize = ThemeTokens.GetFloat("size.hud.glyph.lock", 18f);
                var lockOn = reserve && rw >= ThemeTokens.GetFloat("size.hud.lock.min", 18f) && _lock.sprite;
                if (lockOn)
                {
                    var lr = _lock.rectTransform;
                    lr.sizeDelta = new Vector2(lockSize, lockSize);
                    lr.anchoredPosition = new Vector2(rs + (rw - lockSize) / 2f, (_h - lockSize) / 2f);
                }
                if (_lock.gameObject.activeSelf != lockOn) _lock.gameObject.SetActive(lockOn);
                // Value centred in the window on every bar (user call 2026-10-22: HP centred in the fill, the others
                // right-aligned read inconsistent); with a locked segment, centred in the unlocked part.
                var nw = HudMath.ValueSpan(l, rs, reserve);
                var nr = _number.rectTransform;
                if (Mathf.Abs(nr.sizeDelta.x - nw) > 0.5f) nr.sizeDelta = new Vector2(nw, _h);
            }
            if (!_rail)
            {
                if (Plain) stripeCount = 0;
                if (stripeCount != _shownStripes || stripeCount > 0)
                {
                    for (var i = 0; i < MaxStripes; i++)
                    {
                        var on = i < stripeCount;
                        if (on)
                        {
                            var kind = stripeKinds[i];
                            var spriteToken = kind == (int)DotKind.Fire ? "sprite.hud.dot.fire" : kind == (int)DotKind.Poison ? "sprite.hud.dot.poison" : "sprite.hud.dot.bleed";
                            var colorToken = kind == (int)DotKind.Fire ? "color.dot.fire" : kind == (int)DotKind.Poison ? "color.dot.poison" : kind == (int)DotKind.Spirit ? "color.dot.spirit" : "color.dot.other";
                            var sprite = ThemeTokens.GetSprite(spriteToken);
                            if (_stripes[i].sprite != sprite) { _stripes[i].sprite = sprite; _stripes[i].enabled = sprite; }
                            var c = ThemeTokens.GetColor(colorToken, Color.white);
                            if (_stripes[i].color != c) _stripes[i].color = c;
                        }
                        Place(_stripes[i], on ? stripes[i].Start : 0f, on ? stripes[i].Width : 0f, on);
                    }
                    _shownStripes = stripeCount;
                }
                if (overlaySprite != _shownOverlaySprite)
                {
                    _shownOverlaySprite = overlaySprite;
                    var s = ThemeTokens.GetSprite(overlaySprite);
                    _overlayWet = overlaySprite == "sprite.hud.overlay.wet";
                    var frost = s && !_overlayWet;
                    if (frost)
                    {
                        _overlay.sprite = s;
                        _overlay.color = ThemeTokens.GetColor(overlayColor, Color.white);
                    }
                    if (_overlay.gameObject.activeSelf != frost) _overlay.gameObject.SetActive(frost);
                    var wet = s && _overlayWet;
                    foreach (var w in _wet)
                        if (w.gameObject.activeSelf != wet) w.gameObject.SetActive(wet);
                    PlaceOverlay();
                }
            }
            if (_fx) FxRender(cur, max, fillToken, inert);
            else if (ShieldPerimeter && _sheen)
            {
                Perimeter(_shieldValue, max, l);
            }
            else if (_shield)
            {
                HudMath.ShieldBand(_shieldValue, max, l, _h, ThemeTokens.GetFloat("share.hud.shield.band", 0.4f), out var sx, out var sy, out var sw, out var sh);
                if (ShieldLine)
                {
                    // Mockup hud-2 / hud-3: a thin glowing cyan line just above the HP well, from its start, shield /
                    // max long - not a band over the fill. Child of the bar root (outside the clipped window).
                    if (_shield.transform.parent != Root) { _shield.transform.SetParent(Root, false); if (_shieldEdge) _shieldEdge.gameObject.SetActive(false); }
                    sx = _insetL;
                    sw = HudMath.ShieldOverlay(_shieldValue, max, l);
                    sh = ThemeTokens.GetFloat("size.hud.strip.shield.line", 6f);
                    sy = Height + ThemeTokens.GetFloat("size.hud.strip.shield.gap", 1f);
                }
                var son = sw > 0.05f && sh > 0.05f;
                if (_shield.gameObject.activeSelf != son) _shield.gameObject.SetActive(son);
                if (son)
                {
                    var sr = _shield.rectTransform;
                    sr.anchoredPosition = new Vector2(sx, sy);
                    sr.sizeDelta = new Vector2(sw, sh);
                }
                if (_shieldEdge && !ShieldLine) Edge(_shieldEdge, sx + sw, sy, sh, son && EdgeMode == 2 && sx + sw < l - 0.5f);
            }
            if (Plain) showAvail = false;
            var extra = showAvail ? avail : -1f;
            var shieldShown = _shield && !Plain ? Mathf.Ceil(Mathf.Max(0f, _shieldValue)) : 0f;
            if (Mathf.CeilToInt(cur) != Mathf.CeilToInt(_shownCur) || Mathf.CeilToInt(extra) != Mathf.CeilToInt(_shownExtra) || _shownCur < 0f || shieldShown != _shownShield)
            {
                // Health: "HP + shield" with the shield in its colour, "HP" alone without one (user call 2026-10-22).
                if (shieldShown > 0f) _number.text = $"{Mathf.Ceil(cur)} <color=#{ColorUtility.ToHtmlStringRGB(ThemeTokens.GetColor("color.hud.shield.text", new Color(0.44f, 0.85f, 0.9f)))}>+ {shieldShown}</color>";
                else if (showAvail) _number.SetText("{0}/{1}", Mathf.Ceil(cur), Mathf.Ceil(avail));
                else _number.SetText("{0}", Mathf.Ceil(cur));
                _shownExtra = extra;
                _shownShield = shieldShown;
            }
            _shownCur = cur;
            _shownAvail = avail;
        }

        // Frame overlay placement (art\sprites\sprites.json notes): overlay_frost = ice-crystal cluster at native size
        // with its pivot on the frame top-left corner; overlay_wet = drips hanging from the frame's bottom rail: single
        // clusters (pivot top on the window's bottom edge, share.hud.overlay.wet of native size so they end in the gap,
        // not over the bar below) at WetSpots along the window. Never stretched over the window (the value stays
        // readable).
        private bool _overlayWet;

        private void PlaceOverlay()
        {
            if (!_overlay) return;
            var s = _overlay.sprite;
            if (s)
            {
                var r = _overlay.rectTransform;
                var toRef = 100f / Mathf.Max(1f, s.pixelsPerUnit) * _scale;
                _overlay.preserveAspect = true;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = Vector2.zero;
                r.sizeDelta = new Vector2(s.rect.width, s.rect.height) * toRef;
            }
            var l = Mathf.Max(0f, _length);
            var k = ThemeTokens.GetFloat("share.hud.overlay.wet", 0.6f);
            for (var i = 0; i < _wet.Length; i++)
            {
                var w = _wet[i];
                if (!w || !w.sprite) continue;
                var toRef = 100f / Mathf.Max(1f, w.sprite.pixelsPerUnit) * _scale * k;
                var size = new Vector2(w.sprite.rect.width, w.sprite.rect.height) * toRef;
                var r = w.rectTransform;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.pivot = new Vector2(0f, 1f);
                r.sizeDelta = size;
                r.anchoredPosition = new Vector2(_insetL + Mathf.Max(0f, l - size.x) * WetSpots[i], _insetB);
            }
        }

        // Shield perimeter glow + sheen (ShieldPerimeter). Outer box = the bar root grown by size.hud.strip4.shield.out;
        // caps at the sprites' aspect, the run between them tiled, shield / max of the straight part.
        private float _shownRun = -1f, _shownCapL = -1f, _shownCapR = -1f;

        private void Perimeter(float shield, float max, float l)
        {
            var run = StripMath.ShieldRun(shield, max, out var capL, out var capR);
            if (Mathf.Abs(run - _shownRun) < 0.0005f && capL == _shownCapL && capR == _shownCapR) return;
            _shownRun = run;
            _shownCapL = capL;
            _shownCapR = capR;
            if (_shield && _shield.gameObject.activeSelf) _shield.gameObject.SetActive(false);
            var o = ThemeTokens.GetFloat("size.hud.strip4.shield.out", 3f);
            var h = Height + 2f * o;
            var w = Width + 2f * o;
            var cw = CapWidth(_shCapL, h);
            var cwr = CapWidth(_shCapR, h);
            var straight = Mathf.Max(0f, w - cw - cwr);
            PlaceAt(_shCapL, -o, -o, cw, h, capL > 0f);
            if (_shCapL.gameObject.activeSelf) _shCapL.canvasRenderer.SetAlpha(capL);
            PlaceAt(_shMid, -o + cw, -o, straight * run, h, run > 0f);
            PlaceAt(_shCapR, -o + cw + straight, -o, cwr, h, capR > 0f);
            // Sheen over the fill: shield / max of the window, from its start.
            var sw = l * run;
            var on = sw > 0.05f;
            if (_sheen.gameObject.activeSelf != on) _sheen.gameObject.SetActive(on);
            if (on)
            {
                var sr = _sheen.rectTransform;
                sr.anchoredPosition = Vector2.zero;
                sr.sizeDelta = new Vector2(sw, _h);
            }
        }

        private static float CapWidth(Image img, float h)
        {
            var s = img ? img.sprite : null;
            return s ? h * s.rect.width / Mathf.Max(1f, s.rect.height) : 0f;
        }

        private static void PlaceAt(Image img, float x, float y, float w, float h, bool on)
        {
            if (!img) return;
            on = on && w > 0.05f && img.sprite;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) return;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            var p = new Vector2(x, y);
            var s = new Vector2(w, h);
            if (rt.anchoredPosition != p) rt.anchoredPosition = p;
            if (rt.sizeDelta != s) rt.sizeDelta = s;
        }

        // ---- shader bars (HUD strip, user call 2026-10-23) ---------------------------------------------------------
        // One Image in the window with the RuneSkin/UI Bar material draws trough depth, glossy fill, straight-cut end,
        // lag ghost, grey missing part and the shield sheen; a second one over the frame draws the shield perimeter
        // glow. The sprite layers (trough, lag, fill, edge, front, shield band / sheen / perimeter) stay built but
        // disabled. One material instance per bar; properties written only when they change.
        private Image _fx, _fxGlow;
        private Material _fxMat, _fxGlowMat;
        private float _fxFill = -1f, _fxLag = -1f, _fxShield = -1f, _fxAspect = -1f, _fxGlowAspect = -1f;
        private string _fxColor;
        private string _fxColorToken;
        private static readonly int PFill = Shader.PropertyToID("_Fill"), PLag = Shader.PropertyToID("_Lag"),
            PShield = Shader.PropertyToID("_Shield"), PAspect = Shader.PropertyToID("_Aspect"), PFillColor = Shader.PropertyToID("_FillColor"),
            PMode = Shader.PropertyToID("_Mode");

        public bool ShaderFill => _fx;

        // colorToken: the pool's fill colour (color.hud.bar.fx.*).
        public void UseShader(Shader shader, string colorToken)
        {
            if (!shader || _fx) return;
            _fxColorToken = colorToken;
            foreach (var img in new[] { _trough, _lag, _fill, _edge, _front, _shield, _shieldEdge, _sheen, _shCapL, _shMid, _shCapR })
                if (img) img.enabled = false;
            _fx = _art.Image("BarFx", _window, null, "color.frame", Image.Type.Simple);
            _fx.color = Color.white;
            _fx.transform.SetAsFirstSibling();
            _fxMat = FxMaterial(shader, "RuneSkin bar " + Root.name, 0f);
            _fxMat.SetColor("_LagColor", ThemeTokens.GetColor("color.hud.bar.fx.lag", new Color(0.95f, 0.88f, 0.82f)));
            _fxMat.SetColor("_MissingColor", ThemeTokens.GetColor("color.hud.bar.fx.missing", new Color(0.42f, 0.42f, 0.42f)));
            _fxMat.SetColor("_SheenColor", ThemeTokens.GetColor("color.hud.shield.sheen", new Color(0.36f, 0.91f, 0.95f, 0.22f)));
            _fx.material = _fxMat;
            Full(_fx.rectTransform, Mathf.Max(0f, _length));
            if (ShieldPerimeter)
            {
                _fxGlow = _art.Image("ShieldGlow", Root, null, "color.frame", Image.Type.Simple);
                _fxGlow.color = Color.white;
                _fxGlow.raycastTarget = false;
                _fxGlowMat = FxMaterial(shader, "RuneSkin bar glow " + Root.name, 1f);
                _fxGlowMat.SetColor("_GlowColor", ThemeTokens.GetColor("color.hud.shield.glow", new Color(0.36f, 0.91f, 0.95f)));
                _fxGlow.material = _fxGlowMat;
                _number.transform.SetAsLastSibling();
            }
            _fx.raycastTarget = false;
            _shownRun = -1f;
            FxLayout();
        }

        private static Material FxMaterial(Shader shader, string name, float mode)
        {
            var m = new Material(shader) { name = name };
            m.SetFloat(PMode, mode);
            m.SetFloat("_HiPos", ThemeTokens.GetFloat("share.hud.bar.fx.hi.pos", 0.78f));
            m.SetFloat("_HiWidth", ThemeTokens.GetFloat("share.hud.bar.fx.hi.width", 0.12f));
            m.SetFloat("_HiAmount", ThemeTokens.GetFloat("share.hud.bar.fx.hi", 0.38f));
            m.SetFloat("_Bottom", ThemeTokens.GetFloat("share.hud.bar.fx.bottom", 0.55f));
            m.SetFloat("_Depth", ThemeTokens.GetFloat("share.hud.bar.fx.depth", 0.45f));
            m.SetFloat("_Rim", ThemeTokens.GetFloat("share.hud.bar.fx.rim", 0.35f));
            m.SetFloat("_GlowWidth", ThemeTokens.GetFloat("share.hud.bar.fx.glow", 0.16f));
            return m;
        }

        // Quad geometry: the fill quad spans the window; the glow quad the bar root grown by size.hud.strip4.shield.out.
        private void FxLayout()
        {
            if (!_fx) return;
            var l = Mathf.Max(0f, _length);
            Full(_fx.rectTransform, l);
            var aspect = _h > 0f ? l / _h : 1f;
            if (!Mathf.Approximately(aspect, _fxAspect)) { _fxAspect = aspect; _fxMat.SetFloat(PAspect, aspect); }
            if (!_fxGlow) return;
            var o = ThemeTokens.GetFloat("size.hud.strip4.shield.out", 3f);
            var w = Width + 2f * o;
            var h = Height + 2f * o;
            var rt = _fxGlow.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(-o, -o);
            rt.sizeDelta = new Vector2(w, h);
            var ga = h > 0f ? w / h : 1f;
            if (!Mathf.Approximately(ga, _fxGlowAspect)) { _fxGlowAspect = ga; _fxGlowMat.SetFloat(PAspect, ga); }
        }

        private void FxRender(float cur, float max, string fillToken, bool inert)
        {
            var color = inert ? "color.hud.inert" : fillToken == _fillColor ? _fxColorToken : fillToken;
            if (color != _fxColor)
            {
                _fxColor = color;
                _fxMat.SetColor(PFillColor, ThemeTokens.GetColor(color, Color.white));
            }
            var f = Mathf.Clamp01(cur / max);
            if (Mathf.Abs(f - _fxFill) > 0.0001f) { _fxFill = f; _fxMat.SetFloat(PFill, f); }
            var lag = Mathf.Clamp01(Mathf.Max(_lagValue, cur) / max);
            if (Mathf.Abs(lag - _fxLag) > 0.0001f) { _fxLag = lag; _fxMat.SetFloat(PLag, lag); }
            var run = StripMath.ShieldRun(_shieldValue, max, out _, out _);
            if (Mathf.Abs(run - _fxShield) > 0.0001f)
            {
                _fxShield = run;
                _fxMat.SetFloat(PShield, run);
                if (_fxGlowMat) _fxGlowMat.SetFloat(PShield, run);
                var on = run > 0f;
                if (_fxGlow && _fxGlow.gameObject.activeSelf != on) _fxGlow.gameObject.SetActive(on);
            }
        }

        // Shield amount drawn over the health fill on the next Render (0 = none).
        public void SetShield(float amount) => _shieldValue = Mathf.Max(0f, amount);

        // Small text left inside the window (ES recharge countdown) or hidden.
        public void Aux(int seconds)
        {
            var on = seconds > 0;
            if (_aux.gameObject.activeSelf != on) _aux.gameObject.SetActive(on);
            if (on) _aux.SetText("{0}", seconds);
        }

        public void Paused(bool on)
        {
            on = on && _paused.sprite && !Plain;
            if (_paused.gameObject.activeSelf != on) _paused.gameObject.SetActive(on);
        }

        public void Dim(bool dim)
        {
            var a = dim ? 0.45f : 1f;
            if (!Mathf.Approximately(_fill.canvasRenderer.GetAlpha(), a)) _fill.canvasRenderer.SetAlpha(a);
            if (_fx && !Mathf.Approximately(_fx.canvasRenderer.GetAlpha(), a)) _fx.canvasRenderer.SetAlpha(a);
        }

        // Rim ending at window x (its right edge), from y up h; width from the sprite's aspect, whole ref px.
        private void Edge(Image img, float x, float y, float h, bool on)
        {
            var s = img.sprite;
            var w = HudMath.EdgeWidth(s ? s.rect.width : 0f, s ? s.rect.height : 0f, h);
            on = on && w > 0f && x >= w;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) return;
            var rt = img.rectTransform;
            var pos = new Vector2(Mathf.Round(x - w), y);
            var size = new Vector2(w, h);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        // Window-local horizontal strip [x, x+w] of the full window height.
        private void Place(Image img, float x, float w, bool on)
        {
            on = on && w > 0.05f;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) return;
            var rt = img.rectTransform;
            var pos = new Vector2(x, 0f);
            var size = new Vector2(w, _h);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        private void Full(RectTransform rt, float l)
        {
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(l, _h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
