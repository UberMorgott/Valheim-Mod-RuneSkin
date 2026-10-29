using System;

namespace RuneSkin.InventoryScreen
{
    // Shared geometry of the inventory screen (no Unity types: unit-tested on net8). Coordinates are logical canvas
    // units of the InventoryGui canvas (screen px / CanvasScaler.scaleFactor), origin top-left, y down. The window
    // layout itself is InventoryWindowsMath; this holds the box type, the tokens and the per-panel / per-cell helpers.
    public struct Box
    {
        public float X, Y, W, H;

        public Box(float x, float y, float w, float h)
        {
            X = x; Y = y; W = w; H = h;
        }

        public float Right => X + W;
        public float Bottom => Y + H;
        public bool IsEmpty => !(W > 0f) || !(H > 0f);

        public bool Overlaps(Box o) => !IsEmpty && !o.IsEmpty && X < o.Right - 0.01f && o.X < Right - 0.01f && Y < o.Bottom - 0.01f && o.Y < Bottom - 0.01f;

        public bool Inside(Box o) => X >= o.X - 0.01f && Y >= o.Y - 0.01f && Right <= o.Right + 0.01f && Bottom <= o.Bottom + 0.01f;

        public override string ToString() => $"({X:0.#},{Y:0.#},{W:0.#},{H:0.#})";
    }

    // Sizes in design units (multiplied by SkinScale). Defaults = default-theme.json.
    public sealed class InventoryTokens
    {
        public float Margin = 32f, Gap = 16f, InnerGap = 8f;
        // Gap between the player grid window and the HUD strip's reserved top.
        public float StripGap = 8f;
        public float CraftW = 576f, MinCraftW = 456f;
        // Smallest backpack window width (size.inv.backpack.min): the AB 3-column window (289.6) + 26 units, so its weight
        // plate holds "12.5/40" at the 1280x720 body-floor font (24 units) left of the three action glyphs.
        public float BackpackMinW = 316f;
        public float PanelActionsH = 48f;
        // Doll window header band: character name (window header) + the info tab row.
        public float HeaderH = 104f;
        public float FooterH = 48f, PanelHeaderH = 56f, PanelPad = 20f;
        // Window header band height (size.win.header) of the windows without a taller band of their own.
        public float WinHeaderH = 40f;
        // Corner-cap keep-out of the dock panels (PanelDecor.ContentInset -> PanelMath.CapInset, canvas units, set
        // live): the glyph action row sits at least this far from the bottom and right edges.
        public float PanelCapInset;
        // Corner-cap keep-out of the centre windows' panel stacks (canvas units, PanelDecor.KeepOut).
        public float CenterCapInset;
        // Live preview: portrait box (width = height x aspect), at most PreviewMaxH tall.
        public float DollFigureAspect = 0.66f, PreviewMaxH = 300f;
        // Fallback slot without the HUD strip (size.slot) and its gap.
        public float GridCell = 64f, GridGap = 8f;
        public float DollFigureGap = 24f, DollMinRows = 2f;
        // Smallest equipment cell as a share of the slot before the doll rows scroll.
        public float DollMinShare = 0.8f;
        public float ScrollbarW = 14f;
        public float MinCellPhysPx = 44f;
    }

    public struct GridFit
    {
        public int Columns, Rows, VisibleRows, VisibleColumns;
        public float Pitch, Cell;
        public bool ScrollV, ScrollH;
        public float ContentW, ContentH;

        public override string ToString() => $"{Columns}x{Rows} pitch {Pitch:0.#} vis {VisibleColumns}x{VisibleRows}{(ScrollV ? " scrollV" : "")}{(ScrollH ? " scrollH" : "")}";
    }

    public static class InventoryMath
    {
        // How far the action row rises above its default spot (pad / 2 above the panel bottom) to clear the cap.
        public static float ActionLift(InventoryTokens t, float k) => Math.Max(0f, t.PanelCapInset - t.PanelPad * k / 2f);

        // Action row of a dock panel: right-aligned, at least the cap keep-out from the bottom and right edges.
        // Width of a dock stat plate (StatPlate layout: round socket as wide as the plate is tall, value box = plate width -
        // socket - 12): the token width, grown until the value text fits at its font size, never past the room left of the
        // action glyphs. The value font has a screen-px floor, so at small canvas scales the fixed token width alone cut
        // "12.5/40" to "1…" (run 20261027-105618, AB backpack panel next to a chest at 1280x720).
        public static float StatValueBoxW(float plateW, float plateH) => Math.Max(1f, plateW - plateH - 12f);

        public static float StatPlateWidth(float tokenW, float plateH, float textW, float room)
        {
            var need = plateH + textW + 12f;
            return Math.Max(0f, Math.Min(Math.Max(tokenW, need), room));
        }

        public static Box ActionRow(Box panel, InventoryTokens t, float k)
        {
            var pad = t.PanelPad * k;
            var h = t.PanelActionsH * k;
            var side = DockSide(t, k);
            var bottom = Math.Max(pad / 2f, t.PanelCapInset);
            return new Box(panel.X + side, panel.Bottom - bottom - h, Math.Max(0f, panel.W - 2f * side), h);
        }

        // Grid view of a dock panel: under the title row, top-aligned, as tall as its visible rows (InventoryGrid
        // centres its root in the view, so a taller view would push short grids down), never into the action row.
        // Grid view of the shard slot window (InventoryWindowsMath.SocketsH: no action row): under the title row, one cell
        // tall, the cells centred across like a container's.
        public static Box SocketsGridView(Box panel, GridFit fit, InventoryTokens t, float k)
        {
            var pad = t.PanelPad * k;
            var top = panel.Y + t.PanelHeaderH * k + pad;
            var cols = fit.VisibleColumns > 0 ? fit.VisibleColumns : fit.Columns;
            var side = Math.Max(pad, Math.Min(DockSide(t, k), (panel.W - (cols * fit.Pitch - (fit.Pitch - fit.Cell))) / 2f));
            return new Box(panel.X + side, top, Math.Max(0f, panel.W - 2f * side), Math.Max(0f, fit.Cell));
        }

        public static Box ContainerGridView(Box panel, GridFit fit, InventoryTokens t, float k)
        {
            var pad = t.PanelPad * k;
            var top = panel.Y + t.PanelHeaderH * k + pad;
            var maxH = Math.Max(0f, ActionRow(panel, t, k).Y - pad / 2f - top); // = Fit's inner height
            var h = fit.ScrollV || !(fit.Pitch > 0f) ? maxH : Math.Min(maxH, fit.VisibleRows * fit.Pitch);
            // Content inset, narrowed to the panel pad when the shown columns need the room (0.24.0 FitDock rule; the shown block = the visible columns of a sideways-scrolling grid).
            var cols = fit.VisibleColumns > 0 ? fit.VisibleColumns : fit.Columns;
            var side = Math.Max(pad, Math.Min(DockSide(t, k), (panel.W - (cols * fit.Pitch - (fit.Pitch - fit.Cell))) / 2f));
            return new Box(panel.X + side, top, Math.Max(0f, panel.W - 2f * side), h);
        }

        // Left / right edge of a dock panel's content: its pad, at least the window content inset (corner caps: the
        // bottom grid rows sit inside the caps' reach, user 2026-10-26).
        public static float DockSide(InventoryTokens t, float k) => Math.Max(t.PanelPad * k, t.PanelCapInset);

        // Offset of a grid's cell block inside its view along one axis (user defect 2026-10-23, RuneSkin 0.20.0: uneven
        // side margins, empty band under the rows). The block is the cells only: count x pitch minus the one trailing gap
        // (pitch = cell + gap, so count x pitch overshoots by a gap and centring that put the whole gap on the right /
        // bottom). Centred when it fits; a taller block (scrolling view) starts at the view edge.
        public static float BlockOffset(float view, int count, float pitch, float gap)
        {
            if (count <= 0 || !(pitch > 0f)) return 0f;
            var block = count * pitch - Math.Max(0f, gap);
            return block >= view ? 0f : (view - block) / 2f;
        }

        // Inside of one inventory / container cell (user defect 2026-10-23: counts tiny and cramped, icons off, the
        // durability bar past the cell). Shares of the cell side, the HUD strip cell's language (StripCell): icon centred,
        // stack count bottom-right in the outlined HUD number style, quality top-right, durability pill inset above the
        // bottom edge, all inside the cell. Cell-local, origin top-left, y down; w x h = the cell rect.
        public static CellParts Cell(float w, float h, CellTokens t)
        {
            var side = Math.Min(w, h);
            var p = new CellParts();
            if (!(side > 0f)) return p;
            var icon = side * Clamp(t.Icon, 0.1f, 1f);
            p.Icon = new Box((w - icon) / 2f, (h - icon) / 2f, icon, icon);
            var inset = side * Clamp(t.Inset, 0f, 0.25f);
            var countH = side * 0.5f;
            p.Count = new Box(inset, h - inset - countH, Math.Max(0f, w - 2f * inset), countH);
            p.CountFont = side * t.CountFont;
            var qh = side * 0.4f;
            p.Quality = new Box(w / 2f, inset, Math.Max(0f, w / 2f - inset), qh);
            p.QualityFont = side * t.QualityFont;
            var dx = side * Clamp(t.DurInsetX, 0f, 0.4f);
            var dh = Math.Max(t.DurMinH, side * t.DurH);
            var dy = side * Clamp(t.DurInsetY, 0f, 0.4f);
            p.Durability = new Box(dx, h - dy - dh, Math.Max(0f, w - 2f * dx), dh);
            return p;
        }

        // Auto-size range of a cell number: from the readable floor (screen px over px per unit) up to the theme size,
        // the theme size raised to the floor where the cell is drawn that small (720p).
        public static void FontRange(float size, float floorPx, float pxPerUnit, out float min, out float max)
        {
            max = Math.Max(1f, size);
            if (!(pxPerUnit > 0f))
            {
                min = max;
                return;
            }
            min = Math.Max(0f, floorPx) / pxPerUnit;
            if (max < min) max = min;
        }

        // anchoredPosition of the same rect with the pivot moved to the centre, one axis (run 20261024-132030 m2/m3-layout:
        // the empty EAQS cell glyph copied the icon's top-left pivot from GridCellLook and its 0.82 scale shrank it into the
        // corner, margins 7/16). Valid for any anchors: the pivot shift moves the anchor reference by pivot x anchor span,
        // the pivot point by pivot x rect size, so the offset changes by (0.5 - pivot) x sizeDelta.
        public static float CentredPivotPos(float anchoredPos, float pivot, float sizeDelta) => anchoredPos + (0.5f - pivot) * sizeDelta;

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }

    // Theme shares of the cell side (InventorySkin.CellTokens from the theme; defaults = default-theme.json).
    public sealed class CellTokens
    {
        public float Icon = 0.78f, Inset = 0.07f, CountFont = 0.3f, QualityFont = 0.26f;
        public float DurInsetX = 0.12f, DurInsetY = 0.08f, DurH = 0.06f, DurMinH = 3f;
    }

    public struct CellParts
    {
        public Box Icon, Count, Quality, Durability;
        public float CountFont, QualityFont;
    }
}
