using System.Collections.Generic;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Crossbow magazine dots on an item icon (user decision 2A, mockup docs\mockups\ammo-hud\f2-varA-bottom-row-zoom.png):
    // a row of rune dots just above the durability pill, filled = loaded bolt, empty = bright-rimmed socket; a magazine of
    // more than 7 = one filled dot + "n/N" (AmmoMath.CellRow). One provider for every icon cell: inventory / container /
    // EAQS grid cells (InventorySkin.AfterGrid) and HUD strip cells (hotbar + quick slots, HudStrip.ItemCell). Lives as a
    // component on the cell; built on the first magazine item the cell shows, every write change-only. Data:
    // MagazineSource (MorgottTweaks CrossbowMagazine; re-read on its Revision, capacity also every CapacityTtl s).
    public sealed class MagazinePips : MonoBehaviour
    {
        public const string RootName = "RuneSkinMagazine";

        private RectTransform _root;
        private readonly List<Image> _dots = new List<Image>();
        private TMP_Text _text;
        private ItemDrop.ItemData _item;
        private int _revision = int.MinValue, _loaded = -1, _capacity = -1, _theme = -1;
        private float _capacityAt;
        private float _w, _h, _durX, _durY, _px;
        private bool _shown;

        // Cell `cell` shows `item` (null = empty); durX / durY = the durability pill's left / top edge (cell-local units,
        // origin top-left, y down); px = screen px per cell unit. Cheap for every other item: no component is added
        // until a magazine item appears.
        public static void Apply(Component cell, ItemDrop.ItemData item, float durX, float durY, float px)
        {
            if (!cell) return;
            var magazine = IsCandidate(item);
            if (!cell.TryGetComponent<MagazinePips>(out var pips))
            {
                if (!magazine) return;
                pips = cell.gameObject.AddComponent<MagazinePips>();
            }
            pips.Show(magazine ? item : null, durX, durY, px);
        }

        public static void Hide(Component cell)
        {
            if (cell && cell.TryGetComponent<MagazinePips>(out var pips)) pips.Show(null, 0f, 0f, 0f);
        }

        // MorgottTweaks' magazine predicate is m_requiresReload (CrossbowMagazine.IsMagazine); checked here first so a
        // non-crossbow never reaches the reflection delegates.
        public static bool IsCandidate(ItemDrop.ItemData item) =>
            item?.m_shared?.m_attack != null && item.m_shared.m_attack.m_requiresReload && MagazineSource.Available;

        private void Show(ItemDrop.ItemData item, float durX, float durY, float px)
        {
            if (item == null)
            {
                _item = null;
                SetShown(false);
                return;
            }
            var revision = MagazineSource.Revision;
            var now = Time.unscaledTime;
            if (!ReferenceEquals(item, _item) || revision != _revision || now >= _capacityAt)
            {
                _item = item;
                _revision = revision;
                _capacityAt = now + MagazineSource.CapacityTtl;
                MagazineSource.Read(item, out var loaded, out var capacity);
                if (loaded != _loaded || capacity != _capacity)
                {
                    _loaded = loaded;
                    _capacity = capacity;
                    _w = -1f; // re-lay
                }
            }
            if (_capacity <= 0)
            {
                SetShown(false);
                return;
            }
            var cell = (RectTransform)transform;
            var w = cell.rect.width;
            var h = cell.rect.height;
            if (Mathf.Approximately(w, _w) && Mathf.Approximately(h, _h) && Mathf.Approximately(durX, _durX) && Mathf.Approximately(durY, _durY) &&
                Mathf.Abs(px - _px) < 0.001f && _theme == ThemeTokens.Revision && _shown)
                return;
            _w = w;
            _h = h;
            _durX = durX;
            _durY = durY;
            _px = px;
            _theme = ThemeTokens.Revision;
            Layout(AmmoMath.CellRow(w, h, durX, durY, _loaded, _capacity, px,
                ThemeTokens.GetFloat("share.inv.cell.pip", 0.12f), ThemeTokens.GetFloat("share.inv.cell.pip.pitch", 0.144f),
                ThemeTokens.GetFloat("share.inv.cell.inset", 0.07f), ThemeTokens.GetFloat("size.inv.cell.pip.gap.px", 3f),
                ThemeTokens.GetFloat("share.inv.cell.pip.gap", 0.04f), ThemeTokens.GetFloat("share.inv.cell.pip.text", 1.45f)));
            SetShown(true);
        }

        private void SetShown(bool on)
        {
            _shown = on && _root;
            if (_root && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }

        private void Layout(DotRow r)
        {
            Build();
            _root.SetAsLastSibling(); // over the icon (Epic Loot re-parents its set gem last on every UpdateGui)
            Fill(_dots, _root, r, "RuneSkin crossbow magazine dot");
            var compact = r.Mode == MagazineMode.Compact;
            if (_text.gameObject.activeSelf != compact) _text.gameObject.SetActive(compact);
            if (compact)
            {
                _text.text = AmmoMath.MagazineText(_loaded, _capacity);
                var size = r.Dot * ThemeTokens.GetFloat("share.inv.cell.pip.text", 1.45f);
                Place(_text.rectTransform, r.TextX, r.Y + r.Dot / 2f - size / 2f, r.TextW, size);
                _text.fontSize = size;
            }
        }

        // Dots of a row on `parent` (pooled images): filled first, then empty sockets.
        internal static void Fill(List<Image> dots, RectTransform parent, DotRow r, string reason)
        {
            var full = ThemeTokens.GetSprite("sprite.hud.pip.full");
            var empty = ThemeTokens.GetSprite("sprite.hud.pip.empty");
            for (var i = 0; i < r.Count; i++)
            {
                if (i == dots.Count) dots.Add(Dot(parent, i, reason));
                var d = dots[i];
                var filled = r.Mode == MagazineMode.Compact ? r.Filled > 0 : i < r.Filled;
                var s = filled ? full : empty;
                if (d.sprite != s) d.sprite = s;
                d.enabled = s;
                if (!d.gameObject.activeSelf) d.gameObject.SetActive(true);
                Place(d.rectTransform, r.X + i * r.Pitch, r.Y, r.Dot, r.Dot);
            }
            for (var i = r.Count; i < dots.Count; i++)
                if (dots[i].gameObject.activeSelf) dots[i].gameObject.SetActive(false);
        }

        private static Image Dot(RectTransform parent, int i, string reason)
        {
            var go = new GameObject("Pip" + i, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.type = Image.Type.Simple;
            img.color = Color.white; // colour baked into the art
            SkinMark.Own(go, "content", reason);
            return img;
        }

        private void Build()
        {
            if (_root) return;
            _root = new GameObject(RootName, typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = Vector2.zero;
            var go = new GameObject("Count", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            _text = go.AddComponent<TextMeshProUGUI>();
            _text.raycastTarget = false;
            _text.richText = false;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.alignment = TextAlignmentOptions.Left;
            WorldHud.TextLook.ApplyHudNumber(_text);
            _text.color = ThemeTokens.GetColor("color.hud.text", Color.white);
            SkinMark.Own(go, "content", "RuneSkin crossbow magazine n/N");
            go.SetActive(false);
        }

        // Box from the parent's top-left (y down), parent-local units.
        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            var corner = new Vector2(0f, 1f);
            if (rt.anchorMin != corner || rt.anchorMax != corner) rt.anchorMin = rt.anchorMax = corner;
            if (rt.pivot != corner) rt.pivot = corner;
            var pos = new Vector2(x, -y);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            var size = new Vector2(w, h);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        // Harness: "loaded/capacity mode dots(filled) text" of a cell, or "-".
        public static string Describe(Component cell)
        {
            if (!cell || !cell.TryGetComponent<MagazinePips>(out var p) || !p._shown) return "-";
            var on = 0;
            var filled = 0;
            foreach (var d in p._dots)
            {
                if (!d.gameObject.activeSelf) continue;
                on++;
                if (d.sprite && d.sprite == ThemeTokens.GetSprite("sprite.hud.pip.full")) filled++;
            }
            return $"{p._loaded}/{p._capacity} {AmmoMath.Mode(p._capacity)} dots={on}({filled}) text='{(p._text && p._text.gameObject.activeSelf ? p._text.text : "")}'";
        }
    }
}
