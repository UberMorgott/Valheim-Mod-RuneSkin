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
        private readonly Image _shadow, _glow, _select, _rim, _hover, _icon, _sweep, _ring, _mark, _durTrack, _durFill;
        private readonly TMP_Text _key, _count, _timer;
        // Belt cells only (MorgottTweaks belt, approved mockup docs\mockups\belt\a-wings-extended.png): auto-use "A" pip
        // top-right, gear-granted gem at 12 o'clock on the rim. Made on first use.
        private TMP_Text _pip;
        private Image _gem;
        private HudArt _art;
        private bool _shownPip, _shownGem, _countTimes, _timerSuffix;
        private readonly bool _round;
        private float _side = -1f, _dur = -1f;
        private Sprite _shownIcon;
        private string _shownKey;
        private int _shownCount = int.MinValue, _shownSeconds = int.MinValue;
        private bool _shownGlow, _shownRim, _shownMark, _shownHover;
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

        // The cell's pointer input (hover state + tooltip), cached by HudStrip.
        public StripInput Input;

        public bool IsRound => _round;

        public StripCell(HudArt art, Transform parent, string name, CellKind kind)
        {
            var round = kind == CellKind.Food;
            _round = round;
            _art = art;
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
            // Hover frame = the inventory grid's hover look (role slot.cell spriteHover sprite.slot.cell.selected, the grid
            // element's Button SpriteSwap): its lit border over the bowl, centre left open so the bowl / icon stay as they are.
            _hover = art.Image("Hover", Root, round ? "sprite.hud.gp.ring" : "sprite.slot.cell.selected", "color.hud.strip.hover", round ? Image.Type.Simple : Image.Type.Sliced);
            _hover.fillCenter = false;
            _hover.gameObject.SetActive(false);
            // strip4 art: borders at their authored size (PPU 200 maps the 2x cuts onto the mockup's px); theme scale on top.
            var artScale = ThemeTokens.GetFloat("share.hud.strip4.art", 1f);
            art.Track(_shadow, artScale);
            art.Track(Bowl, artScale);
            art.Track(_rim, artScale);
            if (!round) art.Track(_select, artScale);
            if (!round) art.Track(_hover, artScale);
            // No placeholder art in an empty cell (user 2026-10-26, mockup v2: only real items; the panel art carries the empty well).
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
            // strip_glow carries its whole falloff (alpha 0 at the sprite edge; cell = 0.52 of the sprite): 1.8 keeps the
            // ring where the old tight cut (0.72, 1.3) drew it, without the old cut's hard square edge.
            var halo = side * (ThemeTokens.GetFloat("share.hud.strip.glow", 1.8f) - 1f);
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
            Center(_hover.rectTransform, bw * artW, bh * artH, oy);
            Center(_icon.rectTransform, side * ThemeTokens.GetFloat("share.hud.strip.icon", 0.72f), side * ThemeTokens.GetFloat("share.hud.strip.icon", 0.72f), oy);
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
            // round cell with its timer band: the count sits above the band (bottom-right of the disc), never on the timer
            var countY = _round && timerBelow ? band : inset * 0.8f;
            Box(_count.rectTransform, inset, countY, w - 2f * inset, h * 0.5f);
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
            PlaceBeltMarks();
            BeltText();
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

        public void Icon(Sprite s, Color tint)
        {
            if (s != _shownIcon)
            {
                _shownIcon = s;
                _icon.sprite = s;
                _icon.enabled = s;
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

        // Stack count (<= 0 hides); times = "xN" (belt: the stored stack queued behind a different active item).
        public void Count(int n, bool times = false)
        {
            if (n == _shownCount && times == _countTimes) return;
            _shownCount = n;
            _countTimes = times;
            var on = n > 0;
            if (_count.gameObject.activeSelf != on) _count.gameObject.SetActive(on);
            if (on) _count.SetText(times ? "x{0}" : "{0}", n);
        }

        // Belt auto-use pip: "A" top-right, share.hud.belt.pip of the cell side.
        public void Pip(bool on)
        {
            if (on == _shownPip) return;
            _shownPip = on;
            if (on && !_pip)
            {
                _pip = Text(_art, "AutoPip", "size.hud.strip.key.font", "color.hud.belt.pip", TextAlignmentOptions.Center);
                _pip.text = "A";
                PlaceBeltMarks();
            }
            if (_pip) _pip.gameObject.SetActive(on);
        }

        // Belt gear-granted cell: the gem at 12 o'clock on the rim (sprite.gem.rarity), share.hud.belt.gem of the side.
        public void Gem(bool on)
        {
            if (on == _shownGem) return;
            _shownGem = on;
            if (on && !_gem)
            {
                _gem = _art.Image("BeltGem", Root, "sprite.gem.rarity", "color.hud.belt.gem", Image.Type.Simple);
                _gem.preserveAspect = true;
                PlaceBeltMarks();
            }
            if (_gem) _gem.gameObject.SetActive(on && _gem.sprite);
        }

        private void PlaceBeltMarks()
        {
            if (_side <= 0f) return;
            var band = _round ? Mathf.Clamp(ThemeTokens.GetFloat("share.hud.strip.timer.band", 0.27f), 0f, 0.5f) * _h : 0f;
            var bw = _round ? Mathf.Min(_side, _h - band) : _side;
            var top = _round ? (_h + bw) / 2f + band / 2f : _h; // the disc top (round) / the cell top
            if (_pip)
            {
                var s = Mathf.Max(1f, bw * ThemeTokens.GetFloat("share.hud.belt.pip", 0.16f));
                var r = _pip.rectTransform;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.pivot = new Vector2(1f, 1f);
                r.sizeDelta = new Vector2(s * 1.6f, s * 1.6f);
                // inside the rim, clear of the contour / ring (Codex re-score 7/10: the "A" merged with the teal contour)
                r.anchoredPosition = new Vector2((_side + bw) / 2f - bw * 0.08f, top - bw * 0.08f);
                _pip.enableAutoSizing = true;
                _pip.fontSizeMin = 1f;
                _pip.fontSizeMax = s * 1.6f;
            }
            if (_gem)
            {
                var g = Mathf.Max(1f, bw * ThemeTokens.GetFloat("share.hud.belt.gem", 0.12f));
                var r = _gem.rectTransform;
                r.anchorMin = r.anchorMax = Vector2.zero;
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(g, g);
                r.anchoredPosition = new Vector2(_side / 2f, top);
            }
        }

        // Timer unit suffix (belt: "45s" / "12m", mockup A) instead of the bare seconds.
        public void TimerSuffix(bool on)
        {
            if (on == _timerSuffix) return;
            _timerSuffix = on;
            _shownSeconds = int.MinValue;
            BeltText();
        }

        // Belt cells (runs 20261029-025439 / -033941, Codex compare 4/10: "Alt+1", "A", "x1", "10m" and the glow filled a
        // 35 x 40 px potion cell): a square cell = icon 0.63 of the side at the top centre, a bottom row of 0.28 of the
        // height with the timer left and the count right; texts autosize down; auto = contour only (Glow).
        private void BeltText()
        {
            if (!_timerSuffix || _side <= 0f) return;
            foreach (var txt in new[] { _key, _timer, _count })
            {
                if (txt.enableAutoSizing) continue;
                txt.enableAutoSizing = true;
                txt.fontSizeMax = txt.fontSize;
                txt.fontSizeMin = txt.fontSize * 0.35f;
            }
            if (_round)
            {
                // food: the timer stays inside its band under the disc (Codex re-score 6/10: "20m" ran onto the item)
                var band = Mathf.Clamp(ThemeTokens.GetFloat("share.hud.strip.timer.band", 0.27f), 0f, 0.5f) * _h;
                _timer.fontSizeMax = Mathf.Min(_timer.fontSizeMax, band * 1.05f);
                _timer.fontSizeMin = Mathf.Min(_timer.fontSizeMin, _timer.fontSizeMax);
                // the item sits in the upper disc so the timer keeps a clean strip (Codex re-score 7/10)
                var disc = Mathf.Min(_side, _h - band);
                var fr = _icon.rectTransform;
                fr.sizeDelta = new Vector2(disc * 0.62f, disc * 0.62f);
                fr.anchoredPosition = new Vector2(0f, band / 2f + disc * 0.08f);
                return;
            }
            var row = _h * 0.30f;
            var icon = Mathf.Min(_side, _h - row) * 0.80f;
            var ir = _icon.rectTransform;
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(0.5f, 0.5f);
            ir.sizeDelta = new Vector2(icon, icon);
            ir.anchoredPosition = new Vector2(0f, row / 2f);
            var inset = _side * 0.06f;
            Box(_timer.rectTransform, inset, 0f, _side * 0.6f - inset, row);
            _timer.alignment = TextAlignmentOptions.BottomLeft;
            Box(_count.rectTransform, _side * 0.5f, 0f, _side * 0.5f - inset, row);
        }

        // Shown width of the cell in screen pixels (overlay canvas: world units = px).
        public float PixelWidth => Root.rect.width * Mathf.Abs(Root.lossyScale.x);

        public void Glow(bool on)
        {
            if (on == _shownGlow) return;
            _shownGlow = on;
            // belt cells (auto-use): the crisp contour only, no halo (Codex re-score: the teal fill drowned the item)
            _glow.gameObject.SetActive(on && _glow.sprite && !_timerSuffix);
            _select.gameObject.SetActive(on && _select.sprite);
        }

        // Pointer hover / gamepad cursor on the cell: the grid's white hover frame.
        public void Hover(bool on)
        {
            if (on == _shownHover) return;
            _shownHover = on;
            _hover.gameObject.SetActive(on && _hover.sprite);
        }

        public bool Hovered => _shownHover;
        public GameObject HoverObject => _hover.gameObject;

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

        // Sweep colour (belt: the grey cooldown sector, color.hud.belt.cooldown; default color.hud.strip.cooldown).
        public void SweepTint(Color c)
        {
            if (_sweep.color != c) _sweep.color = c;
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
            if (s < 60) _timer.SetText(_timerSuffix ? "{0}s" : "{0}", s);
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
