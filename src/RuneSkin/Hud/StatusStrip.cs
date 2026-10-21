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

        private readonly RectTransform _panel;

        // harmful: the debuff strip under the bars (true) or the buff row above the food (false); each takes only its
        // side of EffectTaxonomy Harmful. The guardian power's own effect (ExcludeHash) shows on its socket instead.
        private readonly bool _harmful;
        public int ExcludeHash;

        public StatusStrip(HudArt art, Transform parent, bool harmful, string name)
        {
            _art = art;
            _harmful = harmful;
            Root = HudArt.Rect(name, parent);
            // One framed dark-glass panel behind the cells (mockup hud-status-b), only while effects exist.
            _panel = HudArt.Rect("Panel", Root);
            // frame_strip carries its own dark interior (art notes); separator_v studs between entries.
            var frame = art.Image("Frame", _panel, "sprite.hud.frame.strip", "color.frame", Image.Type.Tiled);
            Stretch(frame.rectTransform, 0f);
            _panel.gameObject.SetActive(false);
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
                if (!se || se.m_hidden || (ExcludeHash != 0 && se.NameHash() == ExcludeHash)) continue;
                var c = StatusSource.Classify(se, out var registered);
                if (c.Harmful != _harmful) continue;
                var prio = registered && RuneSkin.Api.Providers.TryEffect(se.NameHash(), out var d) ? d.Priority : c.TintPriority + c.OverlayPriority;
                _entries.Add(new Entry { Effect = se, Hash = se.NameHash(), Class = c, Priority = prio });
            }
            _entries.Sort(Order);
            if (_popupOpen && changed) BuildPopup(); // keep the open list in step with the effects
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
            var panelOn = flow.Rows > 0;
            if (_panel.gameObject.activeSelf != panelOn) _panel.gameObject.SetActive(panelOn);
            if (panelOn)
            {
                var pad = ThemeTokens.GetFloat("size.hud.status.pad", 6f);
                // Frame length follows the drawn cells (never empty cells); the tiled frame grows with the count.
                var cols = Mathf.Min(flow.Columns, flow.Visible + (flow.MoreCell ? 1 : 0));
                // Extra room at the end: timer text is wider than its cell (h:mm:ss) and must clear the frame edge.
                _panel.sizeDelta = new Vector2(cols * (cell + gap) - gap + 2f * pad + ThemeTokens.GetFloat("size.hud.status.pad.end", 0f), flow.Rows * (cell + gap) - gap + 2f * pad);
                _panel.anchoredPosition = new Vector2(-pad, -flow.Rows * (cell + gap) + gap - pad);
            }
            Separators(flow, cell, gap);
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

        private readonly List<Image> _separators = new List<Image>(16);

        // One stud between neighbours in a row (none after the last drawn cell).
        private void Separators(HudMath.Flow flow, float cell, float gap)
        {
            var drawn = flow.Visible + (flow.MoreCell ? 1 : 0);
            var n = 0;
            for (var i = 0; i + 1 < drawn; i++)
            {
                if ((i + 1) % flow.Columns == 0) continue;
                if (n == _separators.Count)
                {
                    var s = _art.Image("Separator" + n, Root, "sprite.hud.separator", "color.frame", Image.Type.Simple);
                    s.SetNativeSize();
                    var native = s.rectTransform.sizeDelta;
                    var k = native.y > 0f ? Mathf.Min(1f, cell / native.y) : 1f;
                    s.rectTransform.sizeDelta = native * k;
                    s.rectTransform.SetSiblingIndex(1); // above the panel, below the cells
                    _separators.Add(s);
                }
                var sep = _separators[n++];
                if (!sep.gameObject.activeSelf) sep.gameObject.SetActive(true);
                var size = sep.rectTransform.sizeDelta;
                var col = i % flow.Columns;
                var row = i / flow.Columns;
                var pos = new Vector2((col + 1) * (cell + gap) - gap / 2f - size.x / 2f, -(row + 1) * (cell + gap) + gap + (cell - size.y) / 2f);
                if (sep.rectTransform.anchoredPosition != pos) sep.rectTransform.anchoredPosition = pos;
            }
            for (var i = n; i < _separators.Count; i++)
                if (_separators[i].gameObject.activeSelf) _separators[i].gameObject.SetActive(false);
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
            // Bounded: at most size.hud.list.rows rows per column, more columns side by side (every entry reachable).
            var rows = Mathf.Max(1, Mathf.Min(_entries.Count, (int)ThemeTokens.GetFloat("size.hud.list.rows", 10f)));
            var columns = (_entries.Count + rows - 1) / rows;
            var h = rows * (cell + gap) - gap + 2f * pad;
            _popup.sizeDelta = new Vector2(columns * width, h);
            // Right of the cluster, bottom-aligned with the strip, growing upward (never over the bars).
            _popup.anchoredPosition = new Vector2(Root.sizeDelta.x + 12f, -Root.sizeDelta.y);
            for (var i = 0; i < _popupRows.Count; i++)
            {
                var on = i < _entries.Count;
                var r = _popupRows[i];
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                if (!on) continue;
                r.Root.anchoredPosition = new Vector2(pad + i / rows * width, h - pad - (i % rows + 1) * (cell + gap) + gap);
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
            // Effect cells are bare icons on the strip (mockup); only the "+N" cell has its socket art.
            c.Back = _art.Image("Socket", c.Root, more ? "sprite.hud.status.more" : null, more ? "color.frame" : "color.hud.clear", Image.Type.Simple, raycast: more);
            Stretch(c.Back.rectTransform, 0f);
            if (!more)
            {
                c.Icon = _art.Image("Icon", c.Root, null, "color.frame", Image.Type.Simple);
                c.Icon.preserveAspect = true;
                var ir = c.Icon.rectTransform;
                ir.anchorMin = new Vector2(0.5f, 1f);
                ir.anchorMax = new Vector2(0.5f, 1f);
                ir.pivot = new Vector2(0.5f, 1f);
                ir.sizeDelta = new Vector2(size * 0.62f, size * 0.62f);
                ir.anchoredPosition = Vector2.zero;
            }
            Image plate = null;
            if (plate)
            {
                var pr = plate.rectTransform;
                pr.sizeDelta = new Vector2(size * 0.8f, size * 0.34f);
                pr.anchoredPosition = new Vector2(size * 0.1f, -size * 0.12f);
                plate.gameObject.SetActive(false);
            }
            c.Timer = _art.Text("Timer", c.Root, more ? "font.display" : "font.body", "color.hud.timer",
                ThemeTokens.GetFloat(more ? "size.hud.font.number" : "size.hud.font.status", 15f), TextAlignmentOptions.Center);
            var tr = c.Timer.rectTransform;
            tr.sizeDelta = more ? new Vector2(size, size) : new Vector2(size + 16f, size * 0.38f);
            tr.anchoredPosition = more ? Vector2.zero : new Vector2(-8f, 0f);
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
                custom = StatusSource.Guard(se.NameHash(), d.Timer, se);
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
