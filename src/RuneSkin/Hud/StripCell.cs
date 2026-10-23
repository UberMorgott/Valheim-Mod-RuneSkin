using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // One pooled bowl cell of the HUD strip (mockup hud-2-final + the user's hotbar reference): selection glow OUTSIDE
    // the cell (teal), sunken bowl, rarity rim (Epic Loot colour) over the bowl rim, large centred icon, key number
    // top-left, stack count bottom-right, set-item gem top-right, durability pill under the cell, cooldown sweep and a
    // radial ring (food time / active ability) for the other zones. Built once; every write is change-only.
    // Socket art per zone (mockup hud-4M3): hotbar / ability cell, round food socket, square potion socket.
    public enum CellKind { Hotbar, Food, Potion }

    public sealed class StripCell
    {
        public readonly RectTransform Root;
        public readonly Image Bowl;
        private readonly Image _ghost;
        private readonly Image _shadow, _glow, _select, _rim, _icon, _sweep, _ring, _mark, _durTrack, _durFill;
        private readonly TMP_Text _key, _count, _timer;
        private readonly bool _round;
        private float _side = -1f, _dur = -1f;
        private Sprite _shownIcon;
        private string _shownKey;
        private int _shownCount = int.MinValue, _shownSeconds = int.MinValue;
        private bool _shownGlow, _shownRim, _shownMark;
        private Color _shownRimColor, _shownIconColor = Color.white;
        private bool _hasDur;
        private float _shownDurFill = -1f;
        private Color _shownDurColor;

        // Epic Loot rarity of the shown item, re-read when the item changes or once a second (its colour comes from
        // Epic Loot's config through a call that may allocate: not every frame).
        public ItemDrop.ItemData RarityItem;
        public float RarityAt;
        public bool RarityMagic, RaritySet;
        public Color RarityColor;

        public bool IsRound => _round;

        public StripCell(HudArt art, Transform parent, string name, CellKind kind)
        {
            var round = kind == CellKind.Food;
            _round = round;
            var socket = kind == CellKind.Food ? "sprite.hud.strip4.socket.round" : kind == CellKind.Potion ? "sprite.hud.strip4.socket.square" : "sprite.hud.strip4.cell";
            Root = HudArt.Rect(name, parent);
            // Dark seat around the cell (mockup hud-3: cells sunk into the plate).
            _shadow = art.Image("Seat", Root, socket, "color.hud.strip.seat", round ? Image.Type.Simple : Image.Type.Sliced);
            _glow = art.Image("Glow", Root, "sprite.hud.strip.glow", "color.hud.strip.halo", Image.Type.Simple);
            _glow.gameObject.SetActive(false);
            // Selection ring just outside the bowl rim (teal, with the glow): the rarity rim stays visible inside it.
            _select = art.Image("Select", Root, round ? "sprite.hud.gp.ring" : "sprite.hud.strip4.rim", "color.hud.strip.glow", round ? Image.Type.Simple : Image.Type.Sliced);
            _select.gameObject.SetActive(false);
            Bowl = art.Image("Bowl", Root, socket, round ? "color.hud.strip.bowl" : "color.hud.strip.panel",
                round ? Image.Type.Simple : Image.Type.Sliced, raycast: true);
            _rim = art.Image("Rim", Root, "sprite.hud.strip4.rim", "color.frame", Image.Type.Sliced);
            _rim.gameObject.SetActive(false);
            // strip4 art: borders at their authored size (PPU 200 maps the 2x cuts onto the mockup's px); theme scale on top.
            var artScale = ThemeTokens.GetFloat("share.hud.strip4.art", 1f);
            art.Track(_shadow, artScale);
            art.Track(Bowl, artScale);
            art.Track(_rim, artScale);
            if (!round) art.Track(_select, artScale);
            // Faint silhouette of the slot kind (food bowl, flask, rune...) while the cell is empty (SetGhost).
            _ghost = art.Image("Ghost", Root, null, "color.hud.strip.ghost", Image.Type.Simple);
            _ghost.preserveAspect = true;
            _ghost.enabled = false;
            _icon = art.Image("Icon", Root, null, "color.frame", Image.Type.Simple);
            _icon.preserveAspect = true;
            _icon.enabled = false;
            _sweep = art.Image("Sweep", Root, "sprite.hud.strip4.cell", "color.hud.strip.cooldown", Image.Type.Simple);
            if (_sweep.sprite)
            {
                _sweep.type = Image.Type.Filled;
                _sweep.fillMethod = Image.FillMethod.Radial360;
                _sweep.fillOrigin = (int)Image.Origin360.Top;
                _sweep.fillClockwise = false;
            }
            _sweep.gameObject.SetActive(false);
            _ring = art.Image("Ring", Root, "sprite.hud.gp.ring", "color.hud.strip.food.ring", Image.Type.Simple);
            if (_ring.sprite)
            {
                _ring.type = Image.Type.Filled;
                _ring.fillMethod = Image.FillMethod.Radial360;
                _ring.fillOrigin = (int)Image.Origin360.Top;
                _ring.fillClockwise = false;
            }
            _ring.gameObject.SetActive(false);
            _mark = art.Image("SetMark", Root, "sprite.gem.rarity", "color.hud.strip.set", Image.Type.Simple);
            _mark.gameObject.SetActive(false);
            _durTrack = art.Image("Durability", Root, "sprite.hud.strip.dur", "color.hud.strip.dur.track", Image.Type.Sliced);
            _durFill = art.Image("Fill", _durTrack.rectTransform, "sprite.hud.strip.dur", "color.hud.strip.dur", Image.Type.Sliced);
            _durTrack.gameObject.SetActive(false);
            _key = Text(art, "Key", "size.hud.strip.key.font", "color.hud.strip.key", TextAlignmentOptions.TopLeft);
            _count = Text(art, "Count", "size.hud.strip.count.font", "color.hud.text", TextAlignmentOptions.BottomRight);
            _timer = Text(art, "Timer", "size.hud.strip.timer.font", "color.hud.timer", TextAlignmentOptions.Center);
            // a timer: the timer floor (12 px), not the number floor its 24-unit size picked (run 20261023-194139: at 720
            // the 14 px number floor pushed "20m" out of the food band)
            if (_timer.TryGetComponent<TextFloor>(out var tf)) tf.Px = ThemeTokens.GetFloat("floor.text.timer.px", 12f);
            if (round)
            {
                // food timer fits its band under the disc (auto-size down to the readable floor, TextFloor.Apply)
                _timer.enableAutoSizing = true;
                _timer.fontSizeMax = _timer.fontSize;
                _timer.fontSizeMin = _timer.fontSize * 0.5f;
                // bottom-aligned in its band: at the readable floor (720: 12 px = 21.9 units) the glyph box is taller than
                // the band, centred it ran below the cell (run 20261023-200109 slots-720); it grows up, never out
                _timer.alignment = TextAlignmentOptions.Bottom;
            }
            _key.gameObject.SetActive(false);
            _count.gameObject.SetActive(false);
            _timer.gameObject.SetActive(false);
        }

        private TMP_Text Text(HudArt art, string name, string sizeToken, string colorToken, TextAlignmentOptions align)
        {
            var t = art.Text(name, Root, "font.body", colorToken, ThemeTokens.GetFloat(sizeToken, 16f), align);
            WorldHud.TextLook.ApplyHudNumber(t);
            return t;
        }

        // Cell side and the durability band under it (0 = none). timerBelow: the timer text sits under the cell
        // (food), else centred on it (cooldowns).
        public void Size(float side, float durBand, bool timerBelow) => Size(side, side, durBand, timerBelow);

        // Cell w x h (the compact strip uses landscape hotbar cells, mockup hud-3 portrait potion cells); round art
        // (glow, icon, ghost, mark) sized from the shorter side, centred.
        public void Size(float w, float h, float durBand, bool timerBelow)
        {
            if (Mathf.Approximately(w, _side) && Mathf.Approximately(h, _h) && Mathf.Approximately(durBand, _dur)) return;
            _side = w;
            _h = h;
            _dur = durBand;
            Root.sizeDelta = new Vector2(w, h);
            // Round (food) cells, user call 2026-10-23: the square cell holds the socket disc at its top, centred
            // horizontally, and a reserved band under it for the timer - the text never sits on the ring. Every other
            // cell: the whole square.
            var band = _round && timerBelow ? Mathf.Clamp(ThemeTokens.GetFloat("share.hud.strip.timer.band", 0.27f), 0f, 0.5f) * h : 0f;
            var bw = _round ? Mathf.Min(w, h - band) : w;
            var bh = _round ? bw : h;
            var oy = (h - bh) / 2f; // disc centre above the cell centre (half the band)
            var side = Mathf.Min(bw, bh);
            var halo = side * (ThemeTokens.GetFloat("share.hud.strip.glow", 1.375f) - 1f);
            Center(_glow.rectTransform, bw + halo, bh + halo, oy);
            var seat = side * ThemeTokens.GetFloat("share.hud.strip.seat", 0.08f);
            Center(_shadow.rectTransform, bw + seat, bh + seat, oy);
            var sel = side * (ThemeTokens.GetFloat("share.hud.strip.select", 1.08f) - 1f);
            Center(_select.rectTransform, bw + sel, bh + sel, oy);
            // socket art may reach past the slot (target panel: the 59 px cell's frame is 61 x 63, the 52 px socket 58)
            var artW = _round ? ThemeTokens.GetFloat("share.hud.strip.socket.art", 1f) : ThemeTokens.GetFloat("share.hud.strip.cell.art.w", 1f);
            var artH = _round ? artW : ThemeTokens.GetFloat("share.hud.strip.cell.art.h", 1f);
            Center(Bowl.rectTransform, bw * artW, bh * artH, oy);
            var rimIn = 2f * side * ThemeTokens.GetFloat("share.hud.strip.rim.inset", 0.035f); // inside the metal rim
            Center(_rim.rectTransform, bw - rimIn, bh - rimIn, oy);
            Center(_icon.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.icon", 0.72f), side * ThemeTokens.GetFloat("share.hud.strip.icon", 0.72f), oy);
            Center(_ghost.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.ghost", 0.62f), side * ThemeTokens.GetFloat("share.hud.strip.ghost", 0.62f), oy);
            Center(_sweep.rectTransform, bw - side * 0.12f, bh - side * 0.12f, oy);
            var ringIn = 2f * (_round ? side * 0.06f : side * 0.04f);
            Center(_ring.rectTransform, bw - ringIn, bh - ringIn, oy);
            var m = side * ThemeTokens.GetFloat("share.hud.strip.mark", 0.24f);
            var mr = _mark.rectTransform;
            mr.anchorMin = mr.anchorMax = mr.pivot = new Vector2(1f, 1f);
            mr.sizeDelta = new Vector2(m, m);
            mr.anchoredPosition = new Vector2(-side * 0.06f, -side * 0.06f);
            var inset = side * 0.09f;
            // key label in the upper half: top inset 0.4 x (run 20261023-191817: the 27-unit glyph box, ~38 units with its
            // outline, ran 1.5 px below the cell centre from the full inset)
            Box(_key.rectTransform, inset, h * 0.5f, w - 2f * inset, h * 0.5f - inset * 0.4f);
            Box(_count.rectTransform, inset, inset * 0.8f, w - 2f * inset, h * 0.5f);
            if (timerBelow) // food: in the band under the disc (never over the ring)
                Box(_timer.rectTransform, 0f, 0f, w, band > 0f ? band : ThemeTokens.GetFloat("size.hud.strip.timer", 18f));
            else // cooldowns: the lower 70 % of the cell, clear of the key label (top-left)
                Box(_timer.rectTransform, 0f, 0f, w, h * 0.7f);
            var line = ThemeTokens.GetFloat("size.hud.strip.dur.line", 5f);
            var dr = _durTrack.rectTransform;
            dr.anchorMin = dr.anchorMax = dr.pivot = Vector2.zero;
            dr.sizeDelta = new Vector2(w * 0.8f, line);
            dr.anchoredPosition = new Vector2(w * 0.1f, -line - ThemeTokens.GetFloat("size.hud.strip.dur.gap", 6f)); // just under the cell (mockup hud-3)
            var fr = _durFill.rectTransform;
            fr.anchorMin = fr.anchorMax = fr.pivot = Vector2.zero;
            fr.anchoredPosition = Vector2.zero;
            _hasDur = durBand > 0f;
            _shownDurFill = -1f;
        }

        private float _h = -1f;
        public void Place(float x, float y)
        {
            var p = new Vector2(x, y);
            if (Root.anchoredPosition != p) Root.anchoredPosition = p;
        }

        public void SetActive(bool on)
        {
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        private string _ghostToken;

        // Placeholder art of this slot kind (theme token; null = none).
        public void SetGhost(HudArt art, string token)
        {
            if (token == _ghostToken) return;
            _ghostToken = token;
            art.Restyle(_ghost, token, "color.hud.strip.ghost", Image.Type.Simple);
            _ghost.enabled = !_shownIcon && _ghost.sprite;
        }

        public void Icon(Sprite s, Color tint)
        {
            if (s != _shownIcon)
            {
                _shownIcon = s;
                _icon.sprite = s;
                _icon.enabled = s;
                _ghost.enabled = !s && _ghost.sprite;
            }
            if (tint != _shownIconColor) { _shownIconColor = tint; _icon.color = tint; }
        }

        public void Key(string text)
        {
            if (ReferenceEquals(text, _shownKey)) return;
            _shownKey = text;
            var on = !string.IsNullOrEmpty(text);
            if (_key.gameObject.activeSelf != on) _key.gameObject.SetActive(on);
            if (on) _key.text = text;
        }

        // Stack count (<= 0 hides).
        public void Count(int n)
        {
            if (n == _shownCount) return;
            _shownCount = n;
            var on = n > 0;
            if (_count.gameObject.activeSelf != on) _count.gameObject.SetActive(on);
            if (on) _count.SetText("{0}", n);
        }

        public void Glow(bool on)
        {
            if (on == _shownGlow) return;
            _shownGlow = on;
            _glow.gameObject.SetActive(on && _glow.sprite);
            _select.gameObject.SetActive(on && _select.sprite);
        }

        public void Rim(bool on, Color c)
        {
            if (on != _shownRim) { _shownRim = on; _rim.gameObject.SetActive(on && _rim.sprite); }
            if (on && c != _shownRimColor) { _shownRimColor = c; _rim.color = c; }
        }

        public void Mark(bool on)
        {
            if (on == _shownMark) return;
            _shownMark = on;
            _mark.gameObject.SetActive(on && _mark.sprite);
        }

        // Durability fraction (< 0 = hidden); broken items blink (vanilla HotkeyBar.UpdateIcons).
        public void Durability(float f, bool broken)
        {
            var on = _hasDur && f >= 0f;
            if (_durTrack.gameObject.activeSelf != on) _durTrack.gameObject.SetActive(on);
            if (!on) return;
            if (broken) f = 1f;
            var c = broken
                ? (Mathf.Sin(Time.time * 10f) > 0f ? ThemeTokens.GetColor("color.hud.strip.dur.low", Color.red) : Color.clear)
                : ThemeTokens.GetColor(f < 0.25f ? "color.hud.strip.dur.low" : "color.hud.strip.dur", Color.white);
            if (c != _shownDurColor) { _shownDurColor = c; _durFill.color = c; }
            if (Mathf.Abs(f - _shownDurFill) < 0.002f) return;
            _shownDurFill = f;
            var w = _durTrack.rectTransform.sizeDelta;
            _durFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, w.x * Mathf.Clamp01(f)), w.y);
            _durFill.enabled = f > 0.01f;
        }

        // Dark radial sweep over the cell (cooldown left / total), 0 hides.
        public void Sweep(float fraction)
        {
            var on = fraction > 0.001f && _sweep.sprite;
            if (_sweep.gameObject.activeSelf != on) _sweep.gameObject.SetActive(on);
            if (on && Mathf.Abs(_sweep.fillAmount - fraction) > 0.002f) _sweep.fillAmount = fraction;
        }

        // Radial ring (food time left / active ability), < 0 hides; colour token per state.
        public void Ring(float fraction, Color c)
        {
            var on = fraction >= 0f && _ring.sprite;
            if (_ring.gameObject.activeSelf != on) _ring.gameObject.SetActive(on);
            if (!on) return;
            if (_ring.color != c) _ring.color = c;
            if (Mathf.Abs(_ring.fillAmount - fraction) > 0.002f) _ring.fillAmount = fraction;
        }

        // Timer text: seconds (< 0 hides); minutes ("12m") from 60 s, "1:05" style below an hour for cooldowns.
        public void Timer(float seconds, bool minutesOnly)
        {
            var s = seconds > 0f ? Mathf.CeilToInt(seconds) : -1;
            if (s == _shownSeconds) return;
            _shownSeconds = s;
            var on = s > 0;
            if (_timer.gameObject.activeSelf != on) _timer.gameObject.SetActive(on);
            if (!on) return;
            if (s < 60) _timer.SetText("{0}", s);
            else if (minutesOnly || s >= 3600) _timer.SetText("{0}m", Mathf.CeilToInt(s / 60f));
            else _timer.SetText("{0}:{1:00}", s / 60, s % 60);
        }

        // Bowl tint: full for a filled cell, dimmer when empty.
        public void Empty(bool empty)
        {
            var c = ThemeTokens.GetColor(_round ? "color.hud.strip.bowl" : empty ? "color.hud.strip.empty" : "color.hud.strip.panel", Color.white);
            if (Bowl.color != c) Bowl.color = c;
        }

        private static void Center(RectTransform rt, float side) => Center(rt, side, side);

        private static void Center(RectTransform rt, float w, float h, float oy = 0f)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, oy);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
