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
        private float _frontCore, _frontClock;
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
            float frontCore = 0.5f)
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
            _window = HudArt.Rect("Window", Root);
            _window.anchoredPosition = new Vector2(_insetL, _insetB);
            _trough = art.Image("Trough", _window, rail ? "sprite.hud.rail.trough" : "sprite.hud.bar.trough", "color.hud.trough", Image.Type.Sliced);
            _lag = art.Image("Lag", _window, rail ? "sprite.hud.rail.fill" : "sprite.hud.bar.lag", "color.hud.lag", Image.Type.Sliced);
            _fill = art.Image("Fill", _window, rail ? "sprite.hud.rail.fill" : "sprite.hud.bar.fill", fillColorToken, Image.Type.Sliced);
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
            _reserved = art.Image("Reserved", _window, "sprite.hud.reserved", "color.hud.reserve", Image.Type.Tiled);
            _reservedEdge = art.Image("ReservedEdge", _window, "sprite.hud.reserved.edge", "color.hud.reserve.edge", Image.Type.Simple);
            _lock = art.Image("Lock", _window, "sprite.hud.glyph.lock", "color.hud.glyph", Image.Type.Simple);
            _reserved.gameObject.SetActive(false);
            _reservedEdge.gameObject.SetActive(false);
            _lock.gameObject.SetActive(false);
            _frame = art.Image("Frame", Root, frameToken, "color.hud.frame", Image.Type.Tiled);
            _frameColor = _frame.color;
            if (glyphToken != null) Glyph(frameToken, glyphToken, scale);
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
            _number = art.Text("Value", Root, "font.display", "color.hud.text", numberSize,
                mainValue ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight, glow: true);
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
            Root.sizeDelta = new Vector2(l + _insetL + _insetR, Height);
            _window.sizeDelta = new Vector2(l, _h);
            Full(_trough.rectTransform, l);
            Stretch(_frame.rectTransform);
            if (_glyph)
            {
                var gr = _glyph.rectTransform;
                gr.anchorMin = gr.anchorMax = Vector2.zero;
                gr.pivot = new Vector2(0.5f, 0.5f);
                gr.anchoredPosition = _glyphAt;
                gr.sizeDelta = new Vector2(_glyphSize, _glyphSize);
            }
            if (_overlay) PlaceOverlay();
            var nr = _number.rectTransform;
            nr.anchoredPosition = new Vector2(_insetL, _insetB);
            nr.sizeDelta = new Vector2(_main ? l : l - 4f, _h);
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
            if (_lagValue < 0f || cur >= _lagValue) { _lagValue = cur; _lagHold = 0f; }
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
                Place(_fill, 0f, fillW, cur > 0f);
                if (_front)
                {
                    var fs = _front.sprite;
                    var fwid = fs ? _h * fs.rect.width / Mathf.Max(1f, fs.rect.height) : _h;
                    var on = fillW > fwid * _frontCore;
                    if (_front.gameObject.activeSelf != on) _front.gameObject.SetActive(on);
                    if (on)
                    {
                        var fr = _front.rectTransform;
                        fr.sizeDelta = new Vector2(fwid, _h);
                        fr.anchoredPosition = new Vector2(fillW - fwid * _frontCore, 0f);
                    }
                }
                Place(_lag, 0f, l * _lagValue / max, _lagValue > cur + 0.01f);
                HudMath.Reserved(avail, max, l, out var rs, out var rw);
                var reserve = rw >= 0.5f;
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
                // The number ends before the locked segment (never over the hatch or the lock rune).
                // Main value: centred in the filled part (mockup), never narrower than the text itself.
                var nw = _main && !reserve ? Mathf.Clamp(fillW, Mathf.Min(l, _number.preferredWidth + 24f), l) : Mathf.Max(0f, (reserve ? rs : l) - 4f);
                var nr = _number.rectTransform;
                if (Mathf.Abs(nr.sizeDelta.x - nw) > 0.5f) nr.sizeDelta = new Vector2(nw, _h);
            }
            if (!_rail)
            {
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
            var extra = showAvail ? avail : -1f;
            if (Mathf.CeilToInt(cur) != Mathf.CeilToInt(_shownCur) || Mathf.CeilToInt(extra) != Mathf.CeilToInt(_shownExtra) || _shownCur < 0f)
            {
                if (showAvail) _number.SetText("{0}/{1}", Mathf.Ceil(cur), Mathf.Ceil(avail));
                else _number.SetText("{0}", Mathf.Ceil(cur));
                _shownExtra = extra;
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

        // Small text left inside the window (ES recharge countdown) or hidden.
        public void Aux(int seconds)
        {
            var on = seconds > 0;
            if (_aux.gameObject.activeSelf != on) _aux.gameObject.SetActive(on);
            if (on) _aux.SetText("{0}", seconds);
        }

        public void Paused(bool on)
        {
            on = on && _paused.sprite;
            if (_paused.gameObject.activeSelf != on) _paused.gameObject.SetActive(on);
        }

        public void Dim(bool dim)
        {
            var a = dim ? 0.45f : 1f;
            if (!Mathf.Approximately(_fill.canvasRenderer.GetAlpha(), a)) _fill.canvasRenderer.SetAlpha(a);
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
