using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.InventoryScreen
{
    // Inside of every inventory / container cell (vanilla InventoryElement prefab, scaled by InventorySkin): icon,
    // stack count, quality, durability pill laid out from InventoryMath.Cell (user defect 2026-10-23 on 0.20.0: counts
    // tiny and cramped at the bottom, icons off-centre, the sword's durability bar past the cell). The prefab's own
    // child rects were kept until then: a small bottom-stretched amount text whose auto-size shrank "50/50" to ~9 px, a
    // durability bar wider than the cell. Now the HUD strip cell's language (StripCell): centred icon, count
    // bottom-right in THE HUD number style (TextLook.ApplyHudNumber), never below the readable floor, inside the cell.
    // Change-only: a cell is re-laid only when its rect, its on-screen scale or the theme changed.
    internal sealed class GridCellLook : MonoBehaviour
    {
        private float _w = -1f, _h = -1f, _px = -1f;
        private int _theme = -1;

        private static CellTokens _tokens;
        private static int _tokensRevision = -1;

        internal static CellTokens Tokens
        {
            get
            {
                if (_tokens != null && _tokensRevision == ThemeTokens.Revision) return _tokens;
                float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);
                var t = new CellTokens();
                t.Icon = F("share.inv.cell.icon", t.Icon);
                t.Inset = F("share.inv.cell.inset", t.Inset);
                t.CountFont = F("share.inv.cell.count.font", t.CountFont);
                t.QualityFont = F("share.inv.cell.quality.font", t.QualityFont);
                t.DurInsetX = F("share.inv.cell.dur.inset.x", t.DurInsetX);
                t.DurInsetY = F("share.inv.cell.dur.inset.y", t.DurInsetY);
                t.DurH = F("share.inv.cell.dur.h", t.DurH);
                t.DurMinH = F("size.inv.cell.dur.min", t.DurMinH);
                _tokens = t;
                _tokensRevision = ThemeTokens.Revision;
                return t;
            }
        }

        internal static void Apply(InventoryElement e)
        {
            if (!e) return;
            if (!e.TryGetComponent<GridCellLook>(out var look)) look = e.gameObject.AddComponent<GridCellLook>();
            look.Layout(e);
        }

        private void Layout(InventoryElement e)
        {
            var cell = (RectTransform)e.transform;
            var w = cell.rect.width;
            var h = cell.rect.height;
            if (!(w > 1f) || !(h > 1f)) return;
            // screen px per cell unit (overlay canvas: world = screen px); hidden (scale 0) cells keep their last layout
            var px = Mathf.Abs(cell.lossyScale.y);
            if (!(px > 0f)) return;
            var iconMoved = e.m_icon && e.m_icon.rectTransform.localScale != Vector3.one; // someone scaled it (old CellFit pass)
            if (!iconMoved && Mathf.Approximately(w, _w) && Mathf.Approximately(h, _h) && Mathf.Abs(px - _px) < 0.001f && _theme == ThemeTokens.Revision) return;
            _w = w;
            _h = h;
            _px = px;
            _theme = ThemeTokens.Revision;
            var parts = InventoryMath.Cell(w, h, Tokens);
            if (e.m_icon)
            {
                e.m_icon.preserveAspect = true;
                Place(e.m_icon.rectTransform, parts.Icon);
                var glyph = e.m_icon.rectTransform.parent ? e.m_icon.rectTransform.parent.Find("RuneSkinGlyph") as RectTransform : null;
                if (glyph) CentreOn(glyph, e.m_icon.rectTransform);
            }
            var floor = ThemeTokens.GetFloat("floor.text.count.px", 12f);
            Number(e.m_amount, parts.Count, parts.CountFont, floor, px, TextAlignmentOptions.BottomRight);
            Number(e.m_quality, parts.Quality, parts.QualityFont, floor, px, TextAlignmentOptions.TopRight);
            if (e.m_durability)
            {
                var dr = (RectTransform)e.m_durability.transform;
                Place(dr, parts.Durability);
                var bar = e.m_durability.m_bar;
                if (bar)
                {
                    // fill grows from the left edge over the full pill height; GuiBar keeps its own width for SetValue
                    bar.anchorMin = new Vector2(0f, 0f);
                    bar.anchorMax = new Vector2(0f, 1f);
                    bar.pivot = new Vector2(0f, 0.5f);
                    bar.anchoredPosition = Vector2.zero;
                    bar.sizeDelta = new Vector2(bar.sizeDelta.x, 0f);
                }
                e.m_durability.SetWidth(parts.Durability.W);
            }
        }

        private static void Number(TMP_Text t, Box box, float size, float floorPx, float px, TextAlignmentOptions align)
        {
            if (!t) return;
            WorldHud.TextLook.ApplyHudNumber(t);
            Place(t.rectTransform, box);
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = Vector4.zero;
            t.raycastTarget = false;
            InventoryMath.FontRange(size, floorPx, px, out var min, out var max);
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
            t.fontSize = max;
        }

        // The icon's rect on `rt` with a centre pivot, so a scale on `rt` (the empty EAQS slot glyph, share.inv.glyph.scale)
        // shrinks it about the icon centre, not into the icon's top-left pivot corner (run 20261024-132030 icon-off 7/16).
        internal static void CentreOn(RectTransform rt, RectTransform icon)
        {
            if (rt.anchorMin != icon.anchorMin) rt.anchorMin = icon.anchorMin;
            if (rt.anchorMax != icon.anchorMax) rt.anchorMax = icon.anchorMax;
            var centre = new Vector2(0.5f, 0.5f);
            if (rt.pivot != centre) rt.pivot = centre;
            if (rt.sizeDelta != icon.sizeDelta) rt.sizeDelta = icon.sizeDelta;
            var pos = new Vector2(InventoryMath.CentredPivotPos(icon.anchoredPosition.x, icon.pivot.x, icon.sizeDelta.x),
                InventoryMath.CentredPivotPos(icon.anchoredPosition.y, icon.pivot.y, icon.sizeDelta.y));
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
        }

        // Cell-local box (origin top-left, y down) on a child rect: anchored to the cell's top-left corner.
        private static void Place(RectTransform rt, Box b)
        {
            var corner = new Vector2(0f, 1f);
            if (rt.anchorMin != corner || rt.anchorMax != corner) rt.anchorMin = rt.anchorMax = corner;
            if (rt.pivot != corner) rt.pivot = corner;
            var pos = new Vector2(b.X, -b.Y);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            var size = new Vector2(b.W, b.H);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (rt.localScale != Vector3.one) rt.localScale = Vector3.one;
        }
    }
}
