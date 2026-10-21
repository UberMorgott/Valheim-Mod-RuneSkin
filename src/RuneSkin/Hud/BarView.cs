using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // One resource bar (hud-resource-states §2-§3). Z-order inside the window: trough -> damage-lag ghost -> fill ->
    // DoT stripes -> reserved hatch (+ edge, lock rune) -> growth shimmer / old-end ghost line; then the frame, the
    // primary overlay and the numbers. Geometry in reference px; the window (inner rect of the carved frame) is
    // length L x height H, the frame extends inset.x / inset.y around it (art: bar_frame window inset 76/18 sprite px).
    // Writes only when a value moved; animations (frame growth, lag ease, ghost fade) run on their own clocks.
    public sealed class BarView
    {
        public const int MaxStripes = 3;

        public RectTransform Root;
        private RectTransform _window;
        private Image _trough, _lag, _fill, _reserved, _reservedEdge, _lock, _ghost, _growth, _frame, _overlay, _paused;
        private readonly Image[] _stripes = new Image[MaxStripes];
        private TMP_Text _number, _aux;
        private readonly HudArt _art;
        private readonly bool _rail;
        private readonly string _fillColor;

        private float _insetX, _insetY, _h;
        private float _length = -1f, _lengthFrom, _lengthTo, _lengthT = 1f;
        private float _ghostAt, _ghostAlpha, _growFrom, _growTo, _growAlpha;
        private float _lagValue = -1f, _lagHold, _lastCur = -1f;
        private float _shownCur = -1f, _shownAvail = -1f, _shownExtra = -1f;
        private int _shownStripes = -1;
        private string _shownFill, _shownOverlaySprite;
        private bool _shownInert;
        private float _lastMax = -1f;

        public BarView(HudArt art, Transform parent, string name, bool rail, string frameToken, string fillColorToken)
        {
            _art = art;
            _rail = rail;
            _fillColor = fillColorToken;
            Root = HudArt.Rect(name, parent);
            FrameGeometry(frameToken, rail, out _insetX, out _insetY, out _h);
            _window = HudArt.Rect("Window", Root);
            _window.anchoredPosition = new Vector2(_insetX, _insetY);
            _trough = art.Image("Trough", _window, rail ? "sprite.hud.rail.trough" : "sprite.hud.bar.trough", "color.hud.trough", Image.Type.Sliced);
            _lag = art.Image("Lag", _window, rail ? "sprite.hud.rail.fill" : "sprite.hud.bar.lag", "color.hud.lag", Image.Type.Sliced);
            _fill = art.Image("Fill", _window, rail ? "sprite.hud.rail.fill" : "sprite.hud.bar.fill", fillColorToken, Image.Type.Sliced);
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
            _growth = art.Image("Growth", _window, "sprite.hud.bar.growth", "color.hud.growth", Image.Type.Simple);
            _ghost = art.Image("Ghost", _window, "sprite.hud.bar.endline", "color.hud.ghost", Image.Type.Simple);
            _growth.gameObject.SetActive(false);
            _ghost.gameObject.SetActive(false);
            _frame = art.Image("Frame", Root, frameToken, "color.hud.frame", Image.Type.Tiled);
            if (!rail)
            {
                _overlay = art.Image("Overlay", Root, "sprite.hud.overlay.frost", "color.overlay.frost", Image.Type.Simple);
                _overlay.gameObject.SetActive(false);
            }
            var numberSize = ThemeTokens.GetFloat(rail ? "size.hud.font.timer" : "size.hud.font.number", 16f);
            _number = art.Text("Value", Root, "font.display", "color.hud.text", numberSize, TextAlignmentOptions.MidlineRight);
            _aux = art.Text("Aux", Root, "font.body", "color.hud.timer", ThemeTokens.GetFloat("size.hud.font.timer", 13f), TextAlignmentOptions.MidlineLeft);
            _aux.gameObject.SetActive(false);
            _paused = art.Image("Paused", Root, "sprite.hud.glyph.seal", "color.hud.glyph", Image.Type.Simple);
            _paused.gameObject.SetActive(false);
        }

        // Window geometry from the frame sprite itself (art changes never need code): the 9-slice border is the
        // frame's inset and the window height is the native height minus top and bottom borders, in reference px
        // (sprite px x 100 / PPU, the canvas reference PPU). Theme floats of the same names override (tuning only);
        // without the sprite the theme values or small defaults apply.
        public static void FrameGeometry(string frameToken, bool rail, out float insetX, out float insetY, out float height)
        {
            var k = rail ? "size.hud.rail" : "size.hud.frame";
            var sprite = ThemeTokens.GetSprite(frameToken);
            float bx = 0f, by = 0f, h = rail ? 5f : 20f;
            if (sprite)
            {
                var toRef = 100f / Mathf.Max(1f, sprite.pixelsPerUnit);
                var b = sprite.border; // (left, bottom, right, top)
                bx = Mathf.Max(b.x, b.z) * toRef;
                by = Mathf.Max(b.y, b.w) * toRef;
                h = Mathf.Max(2f, sprite.rect.height * toRef - b.y * toRef - b.w * toRef);
            }
            insetX = ThemeTokens.TryGetFloat(k + ".inset.x", out var ix) ? ix : bx;
            insetY = ThemeTokens.TryGetFloat(k + ".inset.y", out var iy) ? iy : by;
            height = ThemeTokens.TryGetFloat(rail ? "size.hud.rail.height" : "size.hud.bar.height", out var hh) ? hh : h;
        }

        public float Height => _h + 2f * _insetY;
        public float Width => (_length < 0f ? _lengthTo : _length) + 2f * _insetX + (_rail ? ThemeTokens.GetFloat("size.hud.rail.number", 22f) : 0f);
        public float WindowLength => _length;
        public float InsetX => _insetX;

        public void SetVisible(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        // Target length of the window for pool max B. Steps >= share of B ease over time.hud.grow with a ghost line at
        // the old end (+ shimmer on growth); small steps (food decay) resize at once.
        public void SetLength(float target, float max)
        {
            if (_length < 0f)
            {
                _length = _lengthFrom = _lengthTo = target;
                _lastMax = max;
                Layout();
                return;
            }
            if (Mathf.Abs(target - _lengthTo) < 0.05f) { _lastMax = max; return; }
            var step = HudMath.IsGhostStep(_lastMax, max, ThemeTokens.GetFloat("share.hud.ghost.step", 0.03f));
            _lastMax = max;
            if (!step)
            {
                _length = _lengthFrom = _lengthTo = target;
                _lengthT = 1f;
                Layout();
                return;
            }
            _ghostAt = _length;
            _ghostAlpha = 1f;
            if (target > _length)
            {
                _growFrom = _length;
                _growTo = target;
                _growAlpha = 1f;
            }
            _lengthFrom = _length;
            _lengthTo = target;
            _lengthT = 0f;
        }

        // Per-frame animation clocks (run even when no value changed).
        public void Tick(float dt)
        {
            if (_lengthT < 1f)
            {
                var grow = Mathf.Max(0.01f, ThemeTokens.GetFloat("time.hud.grow", 0.4f));
                _lengthT = Mathf.Min(1f, _lengthT + dt / grow);
                _length = HudMath.Ease(_lengthFrom, _lengthTo, _lengthT);
                Layout();
                _shownCur = -1f; // fill widths follow the moving frame
            }
            var fade = Mathf.Max(0.01f, ThemeTokens.GetFloat("time.hud.ghost.fade", 0.8f));
            if (_ghostAlpha > 0f)
            {
                _ghostAlpha = Mathf.Max(0f, _ghostAlpha - dt / fade);
                Place(_ghost, _ghostAt - 1f, ThemeTokens.GetFloat("size.hud.ghost.width", 2f), _ghostAlpha > 0f);
                Alpha(_ghost, _ghostAlpha * ThemeTokens.GetColor("color.hud.ghost", Color.white).a);
            }
            if (_growAlpha > 0f)
            {
                _growAlpha = Mathf.Max(0f, _growAlpha - dt / fade);
                Place(_growth, _growFrom, Mathf.Max(0f, Mathf.Min(_growTo, _length) - _growFrom), _growAlpha > 0f);
                Alpha(_growth, _growAlpha * ThemeTokens.GetColor("color.hud.growth", Color.white).a);
            }
        }

        private void Layout()
        {
            var l = Mathf.Max(0f, _length);
            Root.sizeDelta = new Vector2(l + 2f * _insetX, _h + 2f * _insetY);
            _window.sizeDelta = new Vector2(l, _h);
            Full(_trough.rectTransform, l);
            Stretch(_frame.rectTransform);
            if (_overlay) Stretch(_overlay.rectTransform);
            var nr = _number.rectTransform;
            if (_rail)
            {
                // Rail number sits right of the rail (art: the cap would cover it).
                nr.anchoredPosition = new Vector2(l + 2f * _insetX + 2f, 0f);
                nr.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.hud.rail.number", 22f) + 20f, _h + 2f * _insetY);
                _number.alignment = TextAlignmentOptions.MidlineLeft;
            }
            else
            {
                nr.anchoredPosition = new Vector2(_insetX, _insetY);
                nr.sizeDelta = new Vector2(l - 4f, _h);
            }
            var ar = _aux.rectTransform;
            ar.anchoredPosition = new Vector2(_insetX + 4f, _insetY);
            ar.sizeDelta = new Vector2(Mathf.Max(0f, l - 8f), _h);
            var pr = _paused.rectTransform;
            var g = _h + _insetY;
            pr.sizeDelta = new Vector2(g, g);
            pr.anchoredPosition = new Vector2(_insetX + l - 4f - g - 56f, (Height - g) / 2f);
        }

        // Renders one frame of data. cur / max (B) / avail (C) in units; stripes in window px; tint = fill colour
        // token override (one per bar) or null; overlay sprite token or null.
        public void Render(float cur, float max, float avail, HudMath.Stripe[] stripes, int stripeCount, int[] stripeKinds,
            string tint, string overlaySprite, string overlayColor, bool inert, float dt, bool showAvail)
        {
            var l = Mathf.Max(0f, _length);
            if (!(max > 0f)) max = 1f;
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
            var lagChanged = true;

            var fillToken = tint ?? _fillColor;
            if (fillToken != _shownFill || inert != _shownInert)
            {
                _shownFill = fillToken;
                _shownInert = inert;
                _fill.color = ThemeTokens.GetColor(inert ? "color.hud.inert" : fillToken, Color.white);
            }
            if (Mathf.Abs(cur - _shownCur) > 0.01f || Mathf.Abs(avail - _shownAvail) > 0.01f || lagChanged)
            {
                Place(_fill, 0f, l * cur / max, cur > 0f);
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
                    var on = s;
                    if (on)
                    {
                        _overlay.sprite = s;
                        _overlay.color = ThemeTokens.GetColor(overlayColor, Color.white);
                    }
                    if (_overlay.gameObject.activeSelf != on) _overlay.gameObject.SetActive(on);
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

        private static void Alpha(Image img, float a)
        {
            var c = img.color;
            if (!Mathf.Approximately(c.a, a)) { c.a = a; img.color = c; }
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
