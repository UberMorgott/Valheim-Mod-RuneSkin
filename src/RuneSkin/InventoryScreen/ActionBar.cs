using System.Collections.Generic;
using RuneSkin.API;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.InventoryScreen
{
    // Glyph-only action bar of one panel instance (player panel, m_container, the backpack panel). Variable count:
    // vanilla Take all / Stack all (InventoryGui.cs:408-409, listeners kept, so V+ / MultiUserChest patched handlers
    // still run; label hidden, glyph added, tooltip names it) + every Skin.RegisterPanelAction of the panel's kind,
    // ordered by Order. Registered actions get the panel's inventory (OnInvokeFor) and their own hotkey.
    public sealed class ActionBar
    {
        private sealed class Entry
        {
            public RectTransform Rect;
            public Button Button;
            public InventoryRegistry.Action Action; // null = vanilla button
            public int Order;
        }

        private readonly RectTransform _panel;
        private readonly GameObject _tooltipPrefab;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Entry> _own = new List<Entry>();
        private PanelKind _kind = (PanelKind)(-1);
        private int _revision = -1;
        private Inventory _inventory;
        private Button _takeAll, _stackAll;

        public ActionBar(RectTransform panel, PanelKind kind, GameObject tooltipPrefab)
        {
            _panel = panel;
            _tooltipPrefab = tooltipPrefab;
        }

        public void Sync(PanelKind kind, Inventory inventory, Button takeAll, Button stackAll)
        {
            _inventory = inventory;
            if (kind == _kind && _revision == InventoryRegistry.Revision && takeAll == _takeAll && stackAll == _stackAll) return;
            _kind = kind;
            _revision = InventoryRegistry.Revision;
            _takeAll = takeAll;
            _stackAll = stackAll;
            foreach (var e in _own)
                if (e.Rect) Object.Destroy(e.Rect.gameObject);
            _own.Clear();
            _entries.Clear();
            if (takeAll) _entries.Add(Vanilla(takeAll, "glyph.action.take_all", "$inventory_takeall", 0));
            if (stackAll) _entries.Add(Vanilla(stackAll, "glyph.action.stack_all", "$inventory_stackall", 1));
            foreach (var a in InventoryRegistry.Actions)
            {
                if (a.Dead || a.D.Panel != kind) continue;
                var e = Own(a);
                _own.Add(e);
                _entries.Add(e);
            }
            _entries.Sort((x, y) => x.Order.CompareTo(y.Order));
        }

        private Entry Vanilla(Button button, string glyphRole, string tooltipKey, int order)
        {
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                if (text.enabled) text.enabled = false;
            if (!button.transform.Find("RuneSkinGlyph")) AddGlyph((RectTransform)button.transform, glyphRole, null);
            Tooltip(button.gameObject, SlotNames.Get(tooltipKey));
            return new Entry { Rect = (RectTransform)button.transform, Button = button, Order = order * 10 - 1000 };
        }

        private Entry Own(InventoryRegistry.Action a)
        {
            var go = new GameObject("RuneSkinAction_" + a.Key, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_panel, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var bg = go.AddComponent<Image>();
            var mark = DollCell.Mark(bg, "action.button", "registered panel action button");
            mark.Outcome = Restyler.Apply(bg, mark, ThemeTokens.Role("action.button")) ? Outcome.Styled : Outcome.Degraded;
            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            var glyphRole = !string.IsNullOrEmpty(a.D.GlyphToken) && ThemeTokens.HasRole(a.D.GlyphToken) ? a.D.GlyphToken : "glyph.action.generic";
            AddGlyph(rt, glyphRole, a.D.Glyph ? a.D.Glyph : (a.D.GlyphToken != null ? ThemeTokens.GetSprite(a.D.GlyphToken) : null));
            Tooltip(go, SlotNames.Get(a.D.TooltipKey ?? a.D.Id));
            var entry = new Entry { Rect = rt, Button = button, Action = a, Order = a.D.Order };
            button.onClick.AddListener(() => Invoke(entry));
            return entry;
        }

        private static void AddGlyph(RectTransform parent, string role, Sprite sprite)
        {
            var image = new GameObject("RuneSkinGlyph", typeof(RectTransform)).AddComponent<Image>();
            var rt = image.rectTransform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.18f, 0.18f);
            rt.anchorMax = new Vector2(0.82f, 0.82f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            image.raycastTarget = false;
            image.preserveAspect = true;
            var mark = DollCell.Mark(image, role, "action glyph");
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            if (sprite) { image.sprite = sprite; mark.Outcome = Outcome.Styled; }
            image.enabled = image.sprite;
        }

        private void Tooltip(GameObject go, string topic)
        {
            var tip = go.GetComponent<UITooltip>();
            if (!tip && _tooltipPrefab)
            {
                tip = go.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = _tooltipPrefab;
            }
            if (tip) tip.m_topic = topic;
        }

        public float Width(float size, float gap)
        {
            var n = 0;
            foreach (var e in _entries)
                if (e.Rect && e.Rect.gameObject.activeSelf) n++;
            return n == 0 ? 0f : n * size + (n - 1) * gap;
        }

        // Right-aligned in the box, square buttons.
        public void Place(RectTransform canvas, Box box, float size, float gap)
        {
            var x = box.Right;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (!e.Rect || !e.Rect.gameObject.activeSelf) continue;
                x -= size;
                Geo.Place(canvas, e.Rect, new Box(x, box.Y + (box.H - size) / 2f, size, size));
                x -= gap;
            }
        }

        public void Tick(bool keysAllowed, ref bool consumed)
        {
            foreach (var e in _entries)
            {
                if (e.Action == null || !e.Button) continue;
                var a = e.Action;
                var inv = _inventory;
                var enabled = a.D.EnabledFor != null ? InventoryRegistry.Guard(a, () => a.D.EnabledFor(inv), false)
                    : a.D.Enabled == null || InventoryRegistry.Guard(a, () => a.D.Enabled(), false);
                if (e.Button.interactable != enabled) e.Button.interactable = enabled;
                if (!keysAllowed || consumed || !enabled || string.IsNullOrEmpty(a.D.InputButton)) continue;
                if (ZInput.GetButtonDown(a.D.InputButton))
                {
                    consumed = true;
                    Invoke(e);
                }
            }
        }

        private void Invoke(Entry e)
        {
            var a = e.Action;
            if (a == null || a.Dead) return;
            var inv = _inventory;
            InventoryRegistry.Guard(a, () =>
            {
                if (a.D.OnInvokeFor != null) a.D.OnInvokeFor(inv);
                else a.D.OnInvoke?.Invoke();
                return true;
            }, false);
        }
    }
}
