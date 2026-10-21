using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Status strip below the bars (ui-skin-engine-plan §8a): owned, pooled cells keyed by effect, replacing vanilla
    // Hud.UpdateStatusEffects (Hud.cs:1635, which re-instantiates every icon when the count changes). Takes over its
    // duties: hidden filter (:1645), icon, flash (m_flashIcon :1659), cooldown (:1667), icon text (:1670-1679) and the
    // new-effect flash that clears m_isNew (:1682-1686). Empty = nothing drawn. Rows from the height budget; overflow
    // reserves the last cell for "+N", which opens the full list (pointer, or the configured key / gamepad button).
    public sealed class StatusStrip
    {
        private struct Entry
        {
            public StatusEffect Effect;
            public int Hash;
            public EffectClass Class;
            public int Priority;
        }

        private sealed class Cell
        {
            public RectTransform Root;
            public Image Back, Icon;
            public TMP_Text Timer;
            public StatusEffect Effect;
            public int ShownSeconds = int.MinValue;
            public string ShownText;
            public float Flash;
            public bool Harmful;
        }

        private readonly HudArt _art;
        public readonly RectTransform Root;
        private readonly List<Entry> _entries = new List<Entry>(16);
        private readonly List<Cell> _cells = new List<Cell>(16);
        private Cell _more;
        private TMP_Text _moreText;
        private int _moreShown = -1;
        private RectTransform _popup;
        private readonly List<Cell> _popupRows = new List<Cell>(16);
        private bool _popupOpen;
        private float _resortAt;
        private int _lastCount = -1;
        private static readonly System.Comparison<Entry> Order = (a, b) =>
            EffectTaxonomy.Compare(a.Class.Category, a.Priority, a.Hash, b.Class.Category, b.Priority, b.Hash);

        public HudMath.Flow Layout { get; private set; }
        public int Count => _entries.Count;
        public bool PopupOpen => _popupOpen;

        public StatusStrip(HudArt art, Transform parent)
        {
            _art = art;
            Root = HudArt.Rect("StatusStrip", parent);
        }

        // Vanilla's HUD list (SEMan.GetHUDStatusEffects, Hud.cs:527): the effects with an icon; hidden ones dropped.
        public void Feed(List<StatusEffect> effects)
        {
            var changed = effects.Count != _lastCount;
            if (!changed)
                for (var i = 0; i < _entries.Count && !changed; i++)
                    changed = i >= effects.Count || _entries[i].Effect == null || !Contains(effects, _entries[i].Effect);
            _lastCount = effects.Count;
            if (!changed && Time.unscaledTime < _resortAt) return;
            _resortAt = Time.unscaledTime + 0.5f; // hysteresis: order re-evaluated at most twice a second
            _entries.Clear();
            foreach (var se in effects)
            {
                if (!se || se.m_hidden) continue;
                var c = StatusSource.Classify(se, out var registered);
                var prio = registered && RuneSkin.Api.Providers.TryEffect(se.NameHash(), out var d) ? d.Priority : c.TintPriority + c.OverlayPriority;
                _entries.Add(new Entry { Effect = se, Hash = se.NameHash(), Class = c, Priority = prio });
            }
            _entries.Sort(Order);
        }

        private static bool Contains(List<StatusEffect> list, StatusEffect se)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == se) return true;
            return false;
        }

        // Lays out within width x budget (ref px, top edge at y = 0 going down). Returns the used height.
        public float Arrange(float width, float budget)
        {
            var cell = ThemeTokens.GetFloat("size.hud.status.cell", 38f);
            var gap = ThemeTokens.GetFloat("size.hud.status.gap", 4f);
            var flow = HudMath.Wrap(_entries.Count, cell, gap, width, HudMath.MaxRows(budget, cell, gap));
            Layout = flow;
            Root.sizeDelta = new Vector2(width, flow.Rows * (cell + gap));
            var shown = flow.Visible;
            while (_cells.Count < shown) _cells.Add(MakeCell("Effect" + _cells.Count, Root, cell));
            for (var i = 0; i < _cells.Count; i++)
            {
                var on = i < shown;
                var c = _cells[i];
                if (c.Root.gameObject.activeSelf != on) c.Root.gameObject.SetActive(on);
                if (!on) { c.Effect = null; continue; }
                Position(c.Root, i, flow.Columns, cell, gap);
                Bind(c, _entries[i]);
            }
            var more = flow.MoreCell;
            if (more && _more == null)
            {
                _more = MakeCell("More", Root, cell, more: true);
                _moreText = _more.Timer;
                var button = _more.Root.gameObject.AddComponent<Button>();
                button.targetGraphic = _more.Back;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(TogglePopup);
            }
            if (_more != null)
            {
                if (_more.Root.gameObject.activeSelf != more) _more.Root.gameObject.SetActive(more);
                if (more)
                {
                    Position(_more.Root, shown, flow.Columns, cell, gap);
                    if (_moreShown != flow.Overflow)
                    {
                        _moreShown = flow.Overflow;
                        _moreText.SetText("+{0}", flow.Overflow);
                    }
                }
            }
            if (_popupOpen && _entries.Count == 0) _popupOpen = false;
            if (_popup && _popup.gameObject.activeSelf != _popupOpen) _popup.gameObject.SetActive(_popupOpen);
            return flow.Rows * (cell + gap);
        }

        public void TogglePopup()
        {
            _popupOpen = !_popupOpen && (_entries.Count > 0);
            if (_popupOpen) BuildPopup();
            if (_popup) _popup.gameObject.SetActive(_popupOpen);
        }

        public void ClosePopup()
        {
            _popupOpen = false;
            if (_popup) _popup.gameObject.SetActive(false);
        }

        // Full list: every entry, icon + localized name + timer, above the strip (clamped by the cluster layout).
        private void BuildPopup()
        {
            var cell = ThemeTokens.GetFloat("size.hud.status.cell", 38f);
            var gap = ThemeTokens.GetFloat("size.hud.status.gap", 4f);
            if (!_popup)
            {
                _popup = HudArt.Rect("EffectList", Root);
                var glass = _art.Image("Glass", _popup, "sprite.fill.glass", "color.glass", Image.Type.Sliced);
                Stretch(glass.rectTransform, 0f);
                var frame = _art.Image("Frame", _popup, "sprite.frame.tooltip", "color.frame", Image.Type.Sliced);
                Stretch(frame.rectTransform, -6f);
            }
            var width = 300f;
            var pad = 10f;
            while (_popupRows.Count < _entries.Count)
            {
                var row = MakeCell("Row" + _popupRows.Count, _popup, cell);
                var name = _art.Text("Name", row.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.hud.font.number", 16f), TextAlignmentOptions.MidlineLeft);
                name.rectTransform.anchoredPosition = new Vector2(cell + 8f, 0f);
                name.rectTransform.sizeDelta = new Vector2(width - cell - 8f - 2f * pad, cell);
                _popupRows.Add(row);
            }
            var h = _entries.Count * (cell + gap) - gap + 2f * pad;
            _popup.sizeDelta = new Vector2(width, h);
            // Right of the cluster, bottom-aligned with the strip, growing upward (never over the bars).
            _popup.anchoredPosition = new Vector2(Root.sizeDelta.x + 12f, -Root.sizeDelta.y);
            for (var i = 0; i < _popupRows.Count; i++)
            {
                var on = i < _entries.Count;
                var r = _popupRows[i];
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                if (!on) continue;
                r.Root.anchoredPosition = new Vector2(pad, h - pad - (i + 1) * (cell + gap) + gap);
                Bind(r, _entries[i]);
                var name = r.Root.Find("Name").GetComponent<TMP_Text>();
                name.text = Localization.instance != null ? Localization.instance.Localize(_entries[i].Effect.m_name) : _entries[i].Effect.m_name;
            }
        }

        // Per frame: timers, flash, cooldown; only writes on change (timer text once per displayed second).
        public void Tick(float dt)
        {
            for (var i = 0; i < _cells.Count; i++)
                if (_cells[i].Effect) TickCell(_cells[i], dt);
            if (_popupOpen)
                for (var i = 0; i < _popupRows.Count; i++)
                    if (_popupRows[i].Effect && _popupRows[i].Root.gameObject.activeSelf) TickCell(_popupRows[i], dt);
        }

        private Cell MakeCell(string name, Transform parent, float size, bool more = false)
        {
            var c = new Cell { Root = HudArt.Rect(name, parent) };
            c.Root.sizeDelta = new Vector2(size, size);
            c.Back = _art.Image("Socket", c.Root, more ? "sprite.hud.status.more" : "sprite.hud.status.cell", "color.frame", Image.Type.Simple, raycast: more);
            Stretch(c.Back.rectTransform, 0f);
            if (!more)
            {
                c.Icon = _art.Image("Icon", c.Root, null, "color.frame", Image.Type.Simple);
                c.Icon.preserveAspect = true;
                Stretch(c.Icon.rectTransform, size * 0.18f);
            }
            var plate = more ? null : _art.Image("Plate", c.Root, "sprite.hud.plate.timer", "color.frame", Image.Type.Sliced);
            if (plate)
            {
                var pr = plate.rectTransform;
                pr.sizeDelta = new Vector2(size * 0.8f, size * 0.34f);
                pr.anchoredPosition = new Vector2(size * 0.1f, -size * 0.12f);
                plate.gameObject.SetActive(false);
            }
            c.Timer = _art.Text("Timer", c.Root, more ? "font.display" : "font.body", "color.hud.timer",
                ThemeTokens.GetFloat(more ? "size.hud.font.number" : "size.hud.font.timer", 13f), TextAlignmentOptions.Center);
            var tr = c.Timer.rectTransform;
            tr.sizeDelta = more ? new Vector2(size, size) : new Vector2(size, size * 0.34f);
            tr.anchoredPosition = more ? Vector2.zero : new Vector2(0f, -size * 0.12f);
            return c;
        }

        private void Bind(Cell c, Entry e)
        {
            if (c.Effect == e.Effect) return;
            c.Effect = e.Effect;
            c.ShownSeconds = int.MinValue;
            c.ShownText = null;
            c.Harmful = e.Class.Harmful;
            if (c.Icon)
            {
                c.Icon.sprite = e.Effect.m_icon;
                c.Icon.enabled = e.Effect.m_icon;
            }
            c.Back.color = ThemeTokens.GetColor(e.Class.Harmful ? "color.hud.harmful" : e.Class.Category == 4 ? "color.hud.marker" : "color.frame", Color.white);
            if (e.Effect.m_isNew)
            {
                e.Effect.m_isNew = false; // vanilla acknowledges here (Hud.cs:1684)
                c.Flash = 0.6f;
            }
        }

        private static void TickCell(Cell c, float dt)
        {
            var se = c.Effect;
            if (c.Icon)
            {
                var white = Color.white;
                if (se.m_flashIcon) white = Mathf.Sin(Time.time * 10f) > 0f ? new Color(1f, 0.5f, 0.5f, 1f) : Color.white;
                if (se.m_cooldownIcon) white.a = 0.45f;
                if (c.Flash > 0f)
                {
                    c.Flash -= dt;
                    var k = Mathf.Clamp01(c.Flash / 0.6f);
                    white = Color.Lerp(white, new Color(1.4f, 1.4f, 1.4f, 1f), k);
                }
                if (c.Icon.color != white) c.Icon.color = white;
            }
            float? custom = null;
            if (RuneSkin.Api.Providers.TryEffect(se.NameHash(), out var d) && d.Timer != null)
            {
                try { custom = d.Timer(se); }
                catch { custom = null; }
            }
            if (!custom.HasValue && StatusSource.HasOwnIconText(se))
            {
                var text = se.GetIconText();
                if (!ReferenceEquals(text, c.ShownText) && text != c.ShownText)
                {
                    c.ShownText = text;
                    c.Timer.text = text ?? "";
                    Plate(c, !string.IsNullOrEmpty(text));
                }
                return;
            }
            var left = custom ?? StatusSource.Remaining(se);
            var seconds = left > 0f ? Mathf.CeilToInt(left) : 0;
            if (seconds == c.ShownSeconds) return;
            c.ShownSeconds = seconds;
            if (seconds <= 0) c.Timer.text = "";
            else if (seconds < 60) c.Timer.SetText("{0}", seconds);
            else if (seconds < 600) c.Timer.SetText("{0}:{1:00}", seconds / 60, seconds % 60);
            else c.Timer.SetText("{0}m", Mathf.CeilToInt(seconds / 60f));
            Plate(c, seconds > 0);
        }

        private static void Plate(Cell c, bool on)
        {
            var plate = c.Root.Find("Plate");
            if (plate && plate.gameObject.activeSelf != on) plate.gameObject.SetActive(on);
        }

        private static void Position(RectTransform rt, int index, int columns, float cell, float gap)
        {
            var col = index % columns;
            var row = index / columns;
            var pos = new Vector2(col * (cell + gap), -(row + 1) * (cell + gap) + gap);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
