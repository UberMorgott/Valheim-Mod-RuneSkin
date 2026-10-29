using System;
using RuneSkin.PlayerHud;

namespace RuneSkin.InventoryScreen
{
    // Hotbar row of the bottom HUD strip in the inventory canvas (canvas units, top-left origin, y down): where the
    // player grid's columns and the side column line up (mockup inv-layout-A, user 2026-10-26).
    public struct HotbarRow
    {
        public float C0;      // centre x of hotbar cell 0
        public float Pitch;   // centre-to-centre distance of neighbouring hotbar cells
        public float Cell;    // hotbar cell side (= the inventory slot, StripMath.SlotPx)
        public float SideC;   // centre x of the first ability cell (F); <= 0: one pitch after the last hotbar cell
        public int Count;     // hotbar cells (8)
        public float Top;     // the strip's reserved top (canvas y); <= 0: no strip

        public bool Valid => Cell > 0f && Pitch >= Cell && Count > 0;
        public float Gap => Math.Max(0f, Pitch - Cell);
        public float Centre(int i) => C0 + i * Pitch;
        public float Side => SideC > 0f ? SideC : C0 + Count * Pitch;
    }

    // Grid with a fixed cell and pitch (the hotbar's): how much of it a window shows.
    public struct WindowGrid
    {
        public int Columns, Rows, VisibleColumns, VisibleRows;
        public float Pitch, Cell;
        public bool ScrollH, ScrollV;

        // Cell block size of the visible part (cells only, no trailing gap).
        public float ViewW => VisibleColumns > 0 ? VisibleColumns * Pitch - (Pitch - Cell) : 0f;
        public float ViewH => VisibleRows > 0 ? VisibleRows * Pitch - (Pitch - Cell) : 0f;

        // As the grid fit the rest of the skin reads (cell scale, element space, scroll flags).
        public GridFit Fit => new GridFit
        {
            Columns = Columns,
            Rows = Rows,
            VisibleColumns = VisibleColumns,
            VisibleRows = VisibleRows,
            Pitch = Pitch,
            Cell = Cell,
            ScrollH = ScrollH,
            ScrollV = ScrollV,
            ContentW = Columns * Pitch,
            ContentH = Rows * Pitch
        };

        public override string ToString() => $"{Columns}x{Rows} cell {Cell:0.#} pitch {Pitch:0.##} vis {VisibleColumns}x{VisibleRows}{(ScrollH ? " scrollH" : "")}{(ScrollV ? " scrollV" : "")}";
    }

    public sealed class WindowsInput
    {
        public float CanvasW = 1920f, CanvasH = 1080f;
        // Safe area in the same logical units (top-left origin); empty = whole canvas.
        public Box Safe;
        public float PhysPerUnit = 1f;
        public float SkinScale = 1f;
        // The strip's hotbar row (InventoryWindowsMath.FromStrip); invalid = no RuneSkin strip (token fallback).
        public HotbarRow Hotbar;
        // Player grid rows shown in the window (inventory rows under the strip's hotbar row), columns = inventory width.
        public int GridW = 8, GridH = 3;
        public int ChestW, ChestH, BackpackW, BackpackH;
        // Backpack window width its header needs for the current backpack's full title at the fitted (smallest) title size
        // (RuneWindowHeader.TitleBandNeed + the panel pads, canvas units); 0 = not measured yet.
        public float BackpackHeaderW;
        // Epic Loot 0.15.4 shard slot panel (ShardSlotPanel: one row, a cell per socket of the item whose slots are open);
        // 0 = not open. SocketsHeaderW: the width its header needs for the full title ("<item> - Shard Slots"), like
        // BackpackHeaderW; 0 = not measured yet.
        public int SocketsW;
        public float SocketsHeaderW;
        // Equipment cells of the doll per side (InventoryWindowsMath.DollSide: armour left, jewellery / light / mods right).
        public int DollLeft = 5, DollRight = 4;
        // Side column entries (trash, armour, weight, coins: whatever is present).
        public int SidePlates = 4;
    }

    public sealed class WindowsLayout
    {
        public HotbarRow Hotbar;
        public float MinCell;
        // Player inventory window: header band, grid cell block (all visible cells), side column.
        public Box Inventory, InventoryHeader, PlayerView, Side;
        public WindowGrid Player;
        public float SidePitch, SidePlateH;
        // Doll window: header (name + info tab row), equipment columns, figure alcove, live preview rect.
        public Box Doll, DollHeader, DollContent, DollFigure, DollPreview;
        public float DollLeftX, DollRightX, DollCellsY;
        public WindowGrid DollGrid; // Columns = 2 (left, right), Rows = cells per column
        // Left column: backpack above chest, Epic Loot's shard slots on top.
        public Box Backpack, Chest, Sockets;
        public WindowGrid BackpackGrid, ChestGrid, SocketsGrid;
        public bool BackpackFolded, DockOverflow;
        public Box Crafting;
        public bool CraftingTight;

        // Left edge / top of player grid cell (col, visible row) (canvas units).
        public float PlayerCellX(int col) => Hotbar.Centre(col) - Hotbar.Cell / 2f;
        public float PlayerCellY(int row) => PlayerView.Y + row * Player.Pitch;
        public Box SidePlate(int i) => new Box(Side.X, Side.Y + i * SidePitch, Side.W, SidePlateH);

        // Every window box (empty ones included), in a fixed order: inventory, doll, backpack, chest, crafting, sockets.
        public Box[] Windows => new[] { Inventory, Doll, Backpack, Chest, Crafting, Sockets };
    }

    // Pure geometry of the windowed inventory screen (mockup inv-layout-A, user 2026-10-26; no Unity types: unit-tested on
    // net8). Coordinates are logical canvas units of the InventoryGui canvas, origin top-left, y down.
    // - The player grid window sits right above the HUD strip: its cells ARE the strip hotbar cells (same side, same
    //   pitch), column j centred over hotbar cell j, the side column (trash / armour / weight / coins) over the first
    //   ability cell (F). Rows bottom-aligned; rows past the room above it scroll inside (the window never moves).
    // - Doll window over it, same left / right edges, from the top margin down to a gap above the grid window.
    //   Equipment cells (the slot again) in a left and a right column, overflow scrolls inside; the figure alcove
    //   between them, the live preview a smaller portrait box centred in it.
    // - Left of the centre column: backpack above chest, right-aligned against it, bottom on the grid window's bottom,
    //   content-sized; cells = the slot (columns past the room scroll sideways, rows past it scroll down). Epic Loot's
    //   shard slot window (one row) on top of that stack: opened on purpose, it never folds; chest and backpack share
    //   what is left.
    // - Right of it: crafting, top margin to the grid window's bottom.
    // Every window then gets its own user offset (WindowMove); this is the default ("reset") layout.
    public static class InventoryWindowsMath
    {
        // The strip's hotbar row in canvas units: strip layout / input (StripMath), the strip root's screen origin
        // (bottom-left, px), px per strip unit, its reserved top (screen px from the bottom) and the canvas scale.
        public static HotbarRow FromStrip(in StripMath.Layout l, in StripMath.Input i, int abilities, float originXPx, float pxPerUnit, float reservedTopPx, float screenH, float canvasScale)
        {
            var s = canvasScale > 0f ? canvasScale : 1f;
            float X(float stripX) => (originXPx + stripX * pxPerUnit) / s;
            var cell = i.Cell * pxPerUnit / s;
            var c0 = X(l.HotbarX + i.Cell / 2f);
            var pitch = (i.Cell + l.Gap) * pxPerUnit / s;
            return new HotbarRow
            {
                C0 = c0,
                Pitch = pitch,
                Cell = cell,
                Count = Math.Max(0, i.Hotbar),
                SideC = abilities > 0 ? X(l.AbilityX0 + l.AbilityX) : 0f,
                Top = reservedTopPx > 0f ? (screenH - reservedTopPx) / s : 0f
            };
        }

        // Without the RuneSkin strip: the slot token row centred on the safe area, a margin above its bottom.
        public static HotbarRow Fallback(Box safe, InventoryTokens t, float k, int count)
        {
            var cell = t.GridCell * k;
            var pitch = cell + t.GridGap * k;
            var n = Math.Max(1, count);
            var total = (n + 1) * pitch - (pitch - cell); // the columns + the side column
            return new HotbarRow
            {
                Cell = cell,
                Pitch = pitch,
                Count = n,
                C0 = safe.X + (safe.W - total) / 2f + cell / 2f,
                SideC = 0f,
                Top = safe.Bottom - t.Margin * k
            };
        }

        public static WindowsLayout Compute(WindowsInput i, InventoryTokens t)
        {
            var k = Clamp(i.SkinScale, 0.5f, 2f);
            var phys = i.PhysPerUnit > 0f ? i.PhysPerUnit : 1f;
            var safe = i.Safe.IsEmpty ? new Box(0f, 0f, i.CanvasW, i.CanvasH) : i.Safe;
            var o = new WindowsLayout { MinCell = t.MinCellPhysPx / phys };
            var h = i.Hotbar.Valid ? i.Hotbar : Fallback(safe, t, k, i.GridW);
            o.Hotbar = h;
            var margin = t.Margin * k;
            var gap = t.Gap * k;
            var top = safe.Y + margin;
            var bottom = (h.Top > 0f ? Math.Min(h.Top, safe.Bottom) : safe.Bottom - margin) - t.StripGap * k;
            var pad = t.PanelPad * k;
            var inset = Math.Max(pad, t.CenterCapInset);
            var headerH = t.WinHeaderH * k;

            // ---- player grid window: columns locked to the hotbar
            var cols = Math.Max(0, i.GridW);
            var extra = Math.Max(0, cols - h.Count);
            var sideC = h.Side + extra * h.Pitch;
            var left = h.C0 - h.Cell / 2f - inset;
            var right = sideC + h.Cell / 2f + inset;
            // rows: all of them, unless the doll would lose its minimum (DollMinRows) - then the grid scrolls inside
            var dollMinH = DollFixedH(t, k, inset) + Math.Max(1f, t.DollMinRows) * h.Pitch - h.Gap;
            var invFixed = headerH + pad + inset;
            var maxInvH = Math.Max(0f, bottom - top - gap - dollMinH);
            var rows = Math.Max(0, i.GridH);
            var fitRows = (int)Math.Floor((maxInvH - invFixed + h.Gap + 0.01f) / h.Pitch);
            var visRows = rows == 0 ? 0 : Math.Max(1, Math.Min(rows, fitRows));
            o.Player = new WindowGrid
            {
                Columns = cols,
                Rows = rows,
                VisibleColumns = cols,
                VisibleRows = visRows,
                Pitch = h.Pitch,
                Cell = h.Cell,
                ScrollV = rows > visRows
            };
            var gridH = o.Player.ViewH;
            var invH = invFixed + gridH;
            o.Inventory = new Box(left, bottom - invH, right - left, invH);
            o.InventoryHeader = new Box(o.Inventory.X, o.Inventory.Y, o.Inventory.W, headerH);
            o.PlayerView = new Box(h.C0 - h.Cell / 2f, o.Inventory.Bottom - inset - gridH, cols > 0 ? cols * h.Pitch - h.Gap : 0f, gridH);
            // side column: plates stacked over the grid height (never past it), one cell wide
            var plates = Math.Max(0, i.SidePlates);
            o.Side = new Box(sideC - h.Cell / 2f, o.PlayerView.Y, h.Cell, gridH);
            // the plate gap shrinks with the plates (n plates exactly fill the grid height when they do not get the pitch)
            var plateGap = plates > 0 ? h.Gap * Math.Min(1f, (gridH + h.Gap) / plates / h.Pitch) : 0f;
            o.SidePitch = plates > 0 ? Math.Min(h.Pitch, (gridH + plateGap) / plates) : 0f;
            o.SidePlateH = Math.Max(0f, o.SidePitch - plateGap);

            // ---- doll window: same edges, top margin down to a gap above the grid window
            o.Doll = new Box(o.Inventory.X, top, o.Inventory.W, Math.Max(0f, o.Inventory.Y - gap - top));
            LayoutDoll(i, t, k, inset, o);

            // ---- left column: backpack above chest, right-aligned against the centre column
            var roomRight = o.Inventory.X - gap;
            var roomLeft = safe.X + margin;
            LayoutDock(i, t, k, roomLeft, roomRight, top, o.Inventory.Bottom, o);

            // ---- crafting right of the centre column
            var craftX = o.Inventory.Right + gap;
            var craftRoom = Math.Max(0f, safe.Right - margin - craftX);
            var craftW = Math.Min(t.CraftW * k, craftRoom);
            o.Crafting = new Box(craftX, top, craftW, Math.Max(0f, o.Inventory.Bottom - top));
            o.CraftingTight = craftW < t.MinCraftW * k - 0.01f;
            return o;
        }

        // Doll window height besides its cell rows: header band (name + tab row), pad under it, bottom inset.
        private static float DollFixedH(InventoryTokens t, float k, float inset) => t.HeaderH * k + t.PanelPad * k + inset;

        private static void LayoutDoll(WindowsInput i, InventoryTokens t, float k, float inset, WindowsLayout o)
        {
            var h = o.Hotbar;
            var d = o.Doll;
            o.DollHeader = new Box(d.X + inset, d.Y, Math.Max(0f, d.W - 2f * inset), t.HeaderH * k);
            var contentTop = d.Y + t.HeaderH * k + t.PanelPad * k;
            o.DollContent = new Box(d.X + inset, contentTop, Math.Max(0f, d.W - 2f * inset), Math.Max(0f, d.Bottom - inset - contentTop));
            var perColumn = Math.Max(Math.Max(0, i.DollLeft), Math.Max(0, i.DollRight));
            // Equipment cells are not grid cells (the 1:1 rule is the grids'): the slot pitch, shrunk to show every row
            // down to DollMinShare of the slot (never under the physical floor), only then the rows scroll.
            var minCell = Math.Min(h.Cell, Math.Max(o.MinCell, h.Cell * Clamp(t.DollMinShare, 0.1f, 1f)));
            var pitch = perColumn > 0 ? Clamp((o.DollContent.H + h.Gap) / perColumn, minCell + h.Gap, h.Pitch) : h.Pitch;
            var fit = (int)Math.Floor((o.DollContent.H + h.Gap + 0.01f) / pitch);
            var vis = perColumn == 0 ? 0 : Math.Max(1, Math.Min(perColumn, fit));
            o.DollGrid = new WindowGrid
            {
                Columns = 2,
                Rows = perColumn,
                VisibleColumns = 2,
                VisibleRows = vis,
                Pitch = pitch,
                Cell = pitch - h.Gap,
                ScrollV = perColumn > vis
            };
            var cell = o.DollGrid.Cell;
            o.DollLeftX = o.DollContent.X;
            o.DollRightX = o.DollContent.Right - cell;
            // the visible cell block centred in the content height
            o.DollCellsY = o.DollContent.Y + Math.Max(0f, (o.DollContent.H - o.DollGrid.ViewH) / 2f);
            var figGap = t.DollFigureGap * k;
            var fx = o.DollLeftX + cell + figGap;
            var fr = o.DollRightX - figGap;
            o.DollFigure = new Box(fx, o.DollContent.Y, Math.Max(0f, fr - fx), o.DollContent.H);
            // live preview figure: a portrait box, never taller than the preview cap (user: smaller than the mockup's
            // figure, close to the old in-game size), centred in the alcove. The cap scales with the slot cell the whole
            // inventory is built on (hotbar cell over its size.slot token: skin scale and any strip squeeze), not with
            // the skin scale alone: a squeezed inventory kept a full-size figure (2026-10-27 user report). This box is
            // the silhouette's; the live preview renders into the whole alcove (DollFigure) and frames the body to the
            // alcove's full height (CharacterPreview.Frame, user 2026-10-28).
            var unit = t.GridCell > 0f && h.Cell > 0f ? h.Cell / t.GridCell : k;
            var ph = Math.Min(o.DollFigure.H, t.PreviewMaxH * unit);
            var pw = Math.Min(o.DollFigure.W, ph * Math.Max(0f, t.DollFigureAspect));
            if (pw < ph * t.DollFigureAspect && t.DollFigureAspect > 0f) ph = pw / t.DollFigureAspect;
            o.DollPreview = new Box(o.DollFigure.X + (o.DollFigure.W - pw) / 2f, o.DollFigure.Y + (o.DollFigure.H - ph) / 2f, pw, ph);
        }

        // Container window height for `rows` visible rows at `pitch` (the shape InventoryMath.ContainerGridView /
        // ActionRow read back: header row, pad, the rows, half a pad, the action row, its cap keep-out).
        public static float ContainerH(int rows, float pitch, InventoryTokens t, float k)
        {
            var pad = t.PanelPad * k;
            return t.PanelHeaderH * k + pad + Math.Max(0, rows) * pitch + pad / 2f + t.PanelActionsH * k + Math.Max(pad / 2f, t.PanelCapInset);
        }

        // Container window width for `visibleCols` columns (+ the vertical scrollbar when the rows scroll).
        public static float ContainerW(int visibleCols, float pitch, float gap, bool scrollV, InventoryTokens t, float k) =>
            2f * InventoryMath.DockSide(t, k) + Math.Max(0f, visibleCols * pitch - gap) + (scrollV ? t.ScrollbarW * k : 0f);

        // Shard slot window height (one row of cells: header row, pad, the cells, the bottom cap keep-out; no action row).
        public static float SocketsH(float cell, InventoryTokens t, float k)
        {
            var pad = t.PanelPad * k;
            return t.PanelHeaderH * k + pad + Math.Max(0f, cell) + Math.Max(pad, t.PanelCapInset);
        }

        private static void LayoutDock(WindowsInput i, InventoryTokens t, float k, float roomLeft, float roomRight, float top, float bottom, WindowsLayout o)
        {
            var h = o.Hotbar;
            var gap = t.Gap * k;
            var hasChest = i.ChestW > 0 && i.ChestH > 0;
            var hasPack = i.BackpackW > 0 && i.BackpackH > 0;
            var hasSockets = i.SocketsW > 0;
            if (!hasChest && !hasPack && !hasSockets) return;
            var roomW = Math.Max(0f, roomRight - roomLeft);
            var sockH = hasSockets ? SocketsH(h.Cell, t, k) : 0f;
            // the shard slots keep their window: the containers below share the rest of the column
            var avail = Math.Max(0f, bottom - top - (hasSockets ? sockH + (hasChest || hasPack ? gap : 0f) : 0f));
            var y = bottom;
            if (hasChest || hasPack) y = LayoutContainers(i, t, k, h, gap, roomW, roomRight, avail, bottom, hasChest, hasPack, o);
            if (!hasSockets) return;
            o.SocketsGrid = Grid(i.SocketsW, 1, 1, roomW, h, t, k);
            // at least the width its header needs for the full title (like the backpack), never past the column's room
            var sw = Math.Max(ContainerW(o.SocketsGrid.VisibleColumns, h.Pitch, h.Gap, false, t, k), Math.Min(i.SocketsHeaderW, roomW));
            o.Sockets = new Box(roomRight - sw, y - sockH, sw, sockH);
        }

        // Chest at the bottom, backpack above it; returns the top of the stack minus the gap (where the next window ends).
        private static float LayoutContainers(WindowsInput i, InventoryTokens t, float k, HotbarRow h, float gap, float roomW, float roomRight, float avail, float bottom,
            bool hasChest, bool hasPack, WindowsLayout o)
        {
            int chestRows = hasChest ? i.ChestH : 0, packRows = hasPack ? i.BackpackH : 0;
            float H(int r) => ContainerH(r, h.Pitch, t, k);
            var chestH = hasChest ? H(chestRows) : 0f;
            var packH = hasPack ? H(packRows) : 0f;
            var both = hasChest && hasPack;
            var need = chestH + packH + (both ? gap : 0f);
            if (need > avail + 0.01f)
            {
                o.DockOverflow = true;
                var fixedOne = H(0);
                if (both)
                {
                    // rows that fit in the stack, shared by wanted rows, each at least min(rows, 2)
                    var rowsFit = (int)Math.Floor((avail - gap - 2f * fixedOne + 0.01f) / h.Pitch);
                    int minC = Math.Min(chestRows, 2), minP = Math.Min(packRows, 2);
                    if (rowsFit < 2)
                    {
                        // not one row each: the chest keeps the column, the backpack folds away (its own key still
                        // opens it alone) - never an unusable sliver
                        hasPack = false;
                        o.BackpackFolded = true;
                        packRows = 0;
                        chestRows = Math.Max(1, Math.Min(chestRows, (int)Math.Floor((avail - fixedOne + 0.01f) / h.Pitch)));
                    }
                    else if (minC + minP > rowsFit)
                    {
                        chestRows = Math.Max(1, rowsFit / 2);
                        packRows = Math.Max(1, rowsFit - chestRows);
                    }
                    else
                    {
                        var c = (int)Math.Round(rowsFit * (double)chestRows / (chestRows + packRows));
                        c = Math.Max(minC, Math.Min(c, rowsFit - minP));
                        packRows = Math.Min(packRows, rowsFit - c);
                        chestRows = Math.Min(chestRows, rowsFit - packRows);
                    }
                }
                else
                {
                    var r = Math.Max(1, (int)Math.Floor((avail - fixedOne + 0.01f) / h.Pitch));
                    if (hasChest) chestRows = Math.Min(chestRows, r);
                    else packRows = Math.Min(packRows, r);
                }
            }

            var y = bottom;
            if (hasChest)
            {
                o.ChestGrid = Grid(i.ChestW, i.ChestH, chestRows, roomW, h, t, k);
                var w = ContainerW(o.ChestGrid.VisibleColumns, h.Pitch, h.Gap, o.ChestGrid.ScrollV, t, k);
                var ch = H(o.ChestGrid.VisibleRows);
                o.Chest = new Box(roomRight - w, y - ch, w, ch);
                y = o.Chest.Y - gap;
            }
            if (hasPack)
            {
                o.BackpackGrid = Grid(i.BackpackW, i.BackpackH, packRows, roomW, h, t, k);
                // at least the backpack minimum (its weight plate's room) and the width its header needs for the full title
                // (AB names up to "Небывалый рюкзак исследователя Уровень 5"), never past the column's room, grown
                // leftwards: the right edge stays on the centre column, the cells centre in the wider view (ContainerGridView)
                var w = Math.Max(ContainerW(o.BackpackGrid.VisibleColumns, h.Pitch, h.Gap, o.BackpackGrid.ScrollV, t, k),
                    Math.Min(Math.Max(t.BackpackMinW * k, i.BackpackHeaderW), roomW));
                var ph = H(o.BackpackGrid.VisibleRows);
                o.Backpack = new Box(roomRight - w, y - ph, w, ph);
                y = o.Backpack.Y - gap;
            }
            return y;
        }

        // A container grid at the hotbar cell / pitch: visible rows as given, columns as many as the room holds (the
        // rest scroll sideways inside the window).
        private static WindowGrid Grid(int w, int hRows, int visRows, float roomW, HotbarRow h, InventoryTokens t, float k)
        {
            var g = new WindowGrid { Columns = Math.Max(0, w), Rows = Math.Max(0, hRows), Pitch = h.Pitch, Cell = h.Cell };
            g.VisibleRows = Math.Max(1, Math.Min(g.Rows, visRows));
            g.ScrollV = g.Rows > g.VisibleRows;
            var inner = roomW - 2f * InventoryMath.DockSide(t, k) - (g.ScrollV ? t.ScrollbarW * k : 0f);
            var fitCols = (int)Math.Floor((inner + h.Gap + 0.01f) / h.Pitch);
            g.VisibleColumns = Math.Max(1, Math.Min(g.Columns, fitCols));
            g.ScrollH = g.Columns > g.VisibleColumns;
            return g;
        }

        // Parts of one side column plate (mockup inv-layout-A: glyph above, value under it, inside the plate): `extras` small
        // controls a mod put on the plate (CurrencyPocket's extract-coins button) as squares stacked down its top-right
        // corner (extra0, then extraStep lower each), the glyph centred in the rest of the upper part clear of them, the
        // value band at the bottom. Nothing overlaps; all inside the plate (pad = 6 % of its height).
        public static void SidePlateParts(Box plate, bool hasValue, int extras, float iconShare, float valueShare, out Box icon, out Box value, out Box extra0, out float extraStep)
        {
            var pad = plate.H * 0.06f;
            var valueH = hasValue ? plate.H * Clamp(valueShare, 0f, 0.6f) : 0f;
            var upper = Math.Max(0f, plate.H - valueH - 2f * pad);
            var n = Math.Max(0, extras);
            var e = n > 0 ? Math.Max(0f, Math.Min(Math.Min(plate.W, plate.H) * 0.3f, (upper - (n - 1) * pad / 2f) / n)) : 0f;
            extraStep = e + pad / 2f;
            extra0 = n > 0 ? new Box(plate.Right - pad - e, plate.Y + pad, e, e) : default;
            var side = Math.Max(0f, Math.Min(upper, plate.W - 2f * (pad + (n > 0 ? e + pad / 2f : 0f))) * Clamp(iconShare, 0.1f, 1f));
            icon = new Box(plate.X + (plate.W - side) / 2f, plate.Y + pad + (upper - side) / 2f, side, side);
            value = hasValue ? new Box(plate.X + pad, plate.Bottom - pad - valueH, Math.Max(0f, plate.W - 2f * pad), valueH) : default;
        }
        // Font size of a one-line value in a box: `want`, lowered so the measured width fits (width at `want` known), never
        // under `floor` (the readable size): one deterministic size per text / box (no auto-size feedback).
        public static float FitSize(float want, float widthAtWant, float boxW, float floor)
        {
            var s = want;
            if (widthAtWant > boxW && widthAtWant > 0f) s = want * boxW / widthAtWant;
            return Math.Max(Math.Min(floor, want), s);
        }

        // First shown row / column of a scrolled window over `count` with `visible` shown: clamped to the valid range.
        public static int ClampScroll(int offset, int count, int visible) => Math.Max(0, Math.Min(offset, Math.Max(0, count - Math.Max(1, visible))));

        // The offset that brings index `i` into the window [offset, offset + visible) with the least movement.
        public static int ScrollTo(int offset, int i, int visible)
        {
            if (visible <= 0) return offset;
            if (i < offset) return i;
            if (i >= offset + visible) return i - visible + 1;
            return offset;
        }

        // Doll side of a slot by its glyph (stable whatever else is registered, Codex review: cells never jump sides when
        // the count changes): body equipment left (helmet, chest, legs, back, belt), jewellery right from the top (necklace
        // above the rings, user 2026-10-27), then light and every other / mod slot.
        public static int DollSide(string glyph)
        {
            switch (glyph)
            {
                case "head": case "chest": case "legs": case "back": case "belt": return 0;
                default: return 1;
            }
        }

        // Order inside a side: the mockup's top-to-bottom order, then anything else (its own order after these).
        public static int DollRank(string glyph)
        {
            switch (glyph)
            {
                case "head": return 0;
                case "chest": return 1;
                case "legs": return 2;
                case "back": return 3;
                case "belt": return 4;
                case "neck": return 5;
                case "ring": return 10;
                case "light": return 20;
                default: return 100;
            }
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
