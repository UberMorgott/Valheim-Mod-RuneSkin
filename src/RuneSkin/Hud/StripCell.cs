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

        public StripCell(HudArt art, Transform parent, string name, bool round)
        {
            _round = round;
            Root = HudArt.Rect(name, parent);
            // Dark seat around the cell (mockup hud-3: cells sunk into the plate).
            _shadow = art.Image("Seat", Root, round ? "sprite.hud.strip.bowl" : "sprite.hud.strip3.cell", "color.hud.strip.seat", round ? Image.Type.Simple : Image.Type.Sliced);
            _glow = art.Image("Glow", Root, "sprite.hud.strip.glow", "color.hud.strip.halo", Image.Type.Simple);
            _glow.gameObject.SetActive(false);
            // Selection ring just outside the bowl rim (teal, with the glow): the rarity rim stays visible inside it.
            _select = art.Image("Select", Root, round ? "sprite.hud.gp.ring" : "sprite.hud.strip.rim", "color.hud.strip.glow", round ? Image.Type.Simple : Image.Type.Sliced);
            _select.gameObject.SetActive(false);
            Bowl = art.Image("Bowl", Root, round ? "sprite.hud.strip.bowl" : "sprite.hud.strip3.cell", round ? "color.hud.strip.bowl" : "color.hud.strip.panel",
                round ? Image.Type.Simple : Image.Type.Sliced, raycast: true);
            _rim = art.Image("Rim", Root, "sprite.hud.strip.rim", "color.frame", Image.Type.Sliced);
            _rim.gameObject.SetActive(false);
            // Faint silhouette of the slot kind (food bowl, flask, rune...) while the cell is empty (SetGhost).
            _ghost = art.Image("Ghost", Root, null, "color.hud.strip.ghost", Image.Type.Simple);
            _ghost.preserveAspect = true;
            _ghost.enabled = false;
            _icon = art.Image("Icon", Root, null, "color.frame", Image.Type.Simple);
            _icon.preserveAspect = true;
            _icon.enabled = false;
            _sweep = art.Image("Sweep", Root, "sprite.hud.strip3.cell", "color.hud.strip.cooldown", Image.Type.Simple);
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
            _key.gameObject.SetActive(false);
            _count.gameObject.SetActive(false);
            _timer.gameObject.SetActive(false);
        }

        private TMP_Text Text(HudArt art, string name, string sizeToken, string colorToken, TextAlignmentOptions align)
        {
            var t = art.Text(name, Root, "font.body", colorToken, ThemeTokens.GetFloat(sizeToken, 16f), align);
            WorldHud.TextLook.Apply(t, "font.body", "hudvalue", ThemeTokens.GetFloat("size.hud.value.dilate", 0.2f));
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
            var side = Mathf.Min(w, h);
            Root.sizeDelta = new Vector2(w, h);
            var halo = side * (ThemeTokens.GetFloat("share.hud.strip.glow", 1.375f) - 1f);
            Center(_glow.rectTransform, w + halo, h + halo);
            var seat = side * ThemeTokens.GetFloat("share.hud.strip.seat", 0.08f);
            Center(_shadow.rectTransform, w + seat, h + seat);
            var sel = side * (ThemeTokens.GetFloat("share.hud.strip.select", 1.08f) - 1f);
            Center(_select.rectTransform, w + sel, h + sel);
            Fill(Bowl.rectTransform, 0f);
            Fill(_rim.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.rim.inset", 0.035f)); // inside the metal rim
            Center(_icon.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.icon", 0.72f));
            Center(_ghost.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.ghost", 0.62f));
            Fill(_sweep.rectTransform, side * 0.06f);
            Fill(_ring.rectTransform, _round ? side * 0.06f : side * 0.04f);
            var m = side * ThemeTokens.GetFloat("share.hud.strip.mark", 0.24f);
            var mr = _mark.rectTransform;
            mr.anchorMin = mr.anchorMax = mr.pivot = new Vector2(1f, 1f);
            mr.sizeDelta = new Vector2(m, m);
            mr.anchoredPosition = new Vector2(-side * 0.06f, -side * 0.06f);
            var inset = side * 0.09f;
            Box(_key.rectTransform, inset, h * 0.5f, w - 2f * inset, h * 0.5f - inset);
            Box(_count.rectTransform, inset, inset * 0.8f, w - 2f * inset, h * 0.5f);
            var timerH = ThemeTokens.GetFloat("size.hud.strip.timer", 18f);
            if (timerBelow)
                Box(_timer.rectTransform, -w * 0.1f, -timerH, w * 1.2f, timerH);
            else
                Box(_timer.rectTransform, 0f, 0f, w, h);
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

        private static void Center(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Fill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
