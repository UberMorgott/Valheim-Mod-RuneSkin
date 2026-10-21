using System;

namespace RuneSkin.InventoryScreen
{
    // Pure geometry of inventory layout A (no Unity types: unit-tested on net8). Coordinates are logical canvas
    // units of the InventoryGui canvas (screen px / CanvasScaler.scaleFactor), origin top-left, y down.
    // Layout A (inventory-layout.md section 9): left dock (chest, backpack under it only while worn), centre column
    // (name + tabs header, doll, player grid, footer), crafting right; HUD band reserved at the bottom; on wide
    // screens a centred column capped at the design width. Every count comes from the input (grid sizes, doll
    // cells, mirrors); tokens give only sizes and gaps.
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

    public enum InventoryMode
    {
        // Dock | centre | crafting at their design widths.
        Full,
        // Same three columns, crafting and dock narrowed towards their minimums.
        Narrow,
        // Two columns: centre left, crafting over the dock on the right.
        Stacked
    }

    // Sizes in design units (multiplied by SkinScale inside Compute). Defaults = 1920x1080 reference of layout A.
    public sealed class InventoryTokens
    {
        public float Margin = 32f, Gap = 16f, InnerGap = 8f;
        public float DockW = 528f, CenterW = 720f, CraftW = 576f;
        public float MinDockW = 380f, MinCraftW = 456f;
        public float WorkspaceMaxW = 1920f;
        public float PanelMaxH = 840f, PanelActionsH = 48f;
        // Fallback HUD band when the HUD cluster top is not known (InventoryInput.HudTop).
        public float HudBand = 210f;
        public float HeaderH = 88f, FooterH = 48f, PanelHeaderH = 56f, PanelPad = 20f;
        public float GridCell = 64f, GridGap = 8f, HotbarHeaderH = 30f;
        public float ContainerCell = 56f, ContainerGap = 6f;
        public float DollCell = 48f, DollGap = 8f, DollPad = 8f, DollColumns = 3f, DollMirrorGap = 24f, DollMinRows = 2f;
        public float ScrollbarW = 14f;
        public float MinCellPhysPx = 44f;
    }

    public struct GridFit
    {
        public int Columns, Rows, VisibleRows, VisibleColumns;
        public float Pitch, Cell;
        public bool ScrollV, ScrollH;
        public float ContentW, ContentH;
        // Panel height this grid wants: header + padding + all rows (or the visible ones when capped).
        public float DesiredH;

        public override string ToString() => $"{Columns}x{Rows} pitch {Pitch:0.#} vis {VisibleColumns}x{VisibleRows}{(ScrollV ? " scrollV" : "")}{(ScrollH ? " scrollH" : "")}";
    }

    public struct DollFit
    {
        public int StorageColumns, StorageRows, MirrorRows, Rows, VisibleRows;
        public float Pitch, Cell;
        public bool Scroll;
    }

    public sealed class InventoryInput
    {
        public float CanvasW = 1920f, CanvasH = 1080f;
        // Safe area in the same logical units (top-left origin); empty = whole canvas.
        public Box Safe;
        // Physical pixels per logical unit (CanvasScaler.scaleFactor): converts the 44 px cell floor.
        public float PhysPerUnit = 1f;
        public float SkinScale = 1f;
        public int GridW = 8, GridH = 4;
        public int ChestW, ChestH;
        public int BackpackW, BackpackH;
        public int DollStorage = 9, DollMirrors = 3;
        // Top of the HUD cluster in the same logical units (0 = unknown): the columns end a gap above it.
        public float HudTop;
    }

    public sealed class InventoryLayout
    {
        public InventoryMode Mode;
        public Box Workspace, Dock, Chest, Backpack, Center, Header, Doll, Grid, Footer, Crafting;
        public GridFit Player, ChestGrid, BackpackGrid;
        public DollFit DollCells;
        // Dock minimums did not fit: both panels at their minimum, the dock scrolls.
        public bool DockOverflow;
        // Dock too short for one row of each: backpack not docked beside the chest.
        public bool BackpackFolded;
        public float MinCell;
    }

    public static class InventoryMath
    {
        public static InventoryLayout Compute(InventoryInput i, InventoryTokens t)
        {
            var k = Clamp(i.SkinScale, 0.5f, 2f);
            var phys = i.PhysPerUnit > 0f ? i.PhysPerUnit : 1f;
            var o = new InventoryLayout();
            // 44 physical px floor in logical units: never below, whatever GuiScale / SkinScale say.
            o.MinCell = t.MinCellPhysPx / phys;

            var safe = i.Safe.IsEmpty ? new Box(0f, 0f, i.CanvasW, i.CanvasH) : i.Safe;
            var margin = t.Margin * k;
            var gap = t.Gap * k;
            var usableW = Math.Max(0f, safe.W - 2f * margin);
            var wsW = Math.Min(usableW, Math.Max(0f, t.WorkspaceMaxW * k - 2f * margin));
            var wsX = safe.X + (safe.W - wsW) / 2f;
            var top = safe.Y + margin;
            // layout-a2/a3: dock, centre and crafting share one top and one bottom edge, the HUD band below the block.
            // The band is the live HUD cluster top when known (it sits bottom-left, under the dock), else the token.
            var bottom = i.HudTop > 0f ? Math.Min(safe.Bottom - margin, i.HudTop - gap) : safe.Bottom - t.HudBand * k;
            var panelH = Math.Max(0f, Math.Min(t.PanelMaxH * k, bottom - top));
            var centerH = panelH;
            o.Workspace = new Box(wsX, top, wsW, panelH);
            // No chest and no backpack: no dock column, centre + crafting re-centre (inventory-layout.md section 9).
            var hasDock = i.ChestW > 0 && i.ChestH > 0 || i.BackpackW > 0 && i.BackpackH > 0;

            // Centre width: its design width, but never narrower than the grid at the minimum pitch.
            var gridMinW = i.GridW * Math.Max(o.MinCell + t.GridGap * k, 1f) + 2f * t.PanelPad * k;
            var centerW = Math.Max(t.CenterW * k, gridMinW);
            var dockW = hasDock ? t.DockW * k : 0f;
            var craftW = t.CraftW * k;
            var dockGap = hasDock ? gap : 0f;
            var full = dockW + centerW + craftW + gap + dockGap;
            if (wsW >= full - 0.01f)
            {
                o.Mode = InventoryMode.Full;
            }
            else
            {
                // Narrow: give back crafting first, then dock, down to their minimums.
                var deficit = full - wsW;
                var craftCut = Math.Min(deficit, craftW - t.MinCraftW * k);
                craftW -= craftCut;
                deficit -= craftCut;
                var dockCut = hasDock ? Math.Min(deficit, dockW - t.MinDockW * k) : 0f;
                dockW -= dockCut;
                deficit -= dockCut;
                o.Mode = deficit <= 0.01f ? InventoryMode.Narrow : InventoryMode.Stacked;
            }

            if (o.Mode != InventoryMode.Stacked)
            {
                // Centred group: dock | centre | crafting; leftover width splits on both sides.
                var used = dockW + centerW + craftW + gap + dockGap;
                var x = wsX + Math.Max(0f, (wsW - used) / 2f);
                o.Dock = hasDock ? new Box(x, top, dockW, panelH) : default;
                o.Center = new Box(x + dockW + dockGap, top, centerW, centerH);
                o.Crafting = new Box(o.Center.Right + gap, top, craftW, panelH);
            }
            else
            {
                // Two columns: the centre keeps its width when possible, the right column takes the rest.
                centerW = Math.Min(centerW, Math.Max(gridMinW, wsW - gap - t.MinDockW * k));
                var rightW = Math.Max(0f, wsW - gap - centerW);
                o.Center = new Box(wsX, top, centerW, centerH);
                if (hasDock)
                {
                    var half = Math.Max(0f, (panelH - gap) / 2f);
                    o.Crafting = new Box(o.Center.Right + gap, top, rightW, half);
                    o.Dock = new Box(o.Center.Right + gap, top + half + gap, rightW, panelH - half - gap);
                }
                else
                {
                    o.Crafting = new Box(o.Center.Right + gap, top, rightW, panelH);
                }
            }

            LayoutCenter(i, t, k, o);
            LayoutDock(i, t, k, o);
            return o;
        }

        private static void LayoutCenter(InventoryInput i, InventoryTokens t, float k, InventoryLayout o)
        {
            var c = o.Center;
            var gap = t.InnerGap * k;
            var pad = t.PanelPad * k;
            o.Header = new Box(c.X, c.Y, c.W, t.HeaderH * k);
            var footerH = t.FooterH * k;
            o.Footer = new Box(c.X, c.Bottom - footerH, c.W, footerH);

            // Doll and grid share the height between header and footer (header | doll | grid | footer). Both pitches
            // shrink by one factor from their tokens until every doll row and grid row fits, never below the physical
            // floor; past that the doll keeps DollMinRows rows and the grid scrolls.
            var dollPad = t.DollPad * k;
            var dollRows = DollRows(i, t);
            var hotbarH = t.HotbarHeaderH * k;
            var avail = Math.Max(0f, c.H - o.Header.H - footerH - 3f * gap);
            var gridMaxPitch = t.GridCell * k + t.GridGap * k;
            var dollMaxPitch = Math.Max(t.DollCell * k, o.MinCell) + t.DollGap * k;
            var gridMinPitch = o.MinCell + t.GridGap * k;
            var dollMinPitch = o.MinCell + t.DollGap * k;
            var fixedH = hotbarH + 2f * pad + (dollRows > 0 ? 2f * dollPad : 0f);
            var wantH = i.GridH * gridMaxPitch + dollRows * dollMaxPitch;
            var f = wantH > 0f ? Clamp((avail - fixedH) / wantH, 0f, 1f) : 1f;
            var gridPitch = Math.Max(gridMinPitch, gridMaxPitch * f);
            var dollPitch = Math.Max(dollMinPitch, dollMaxPitch * f);
            var dollWantH = dollRows > 0 ? dollRows * dollPitch + 2f * dollPad : 0f;
            var dollMinH = dollRows > 0 ? Math.Min(t.DollMinRows, dollRows) * dollPitch + 2f * dollPad : 0f;
            var gridBoxMaxH = Math.Max(0f, avail - Math.Min(dollWantH, Math.Max(dollMinH, avail - (hotbarH + 2f * pad + i.GridH * gridPitch))));
            var grid = Fit(i.GridW, i.GridH, c.W, gridBoxMaxH, hotbarH, pad, gridPitch - t.GridGap * k, t.GridGap * k, o.MinCell, t.ScrollbarW * k);
            o.Player = grid;
            var gridH = Math.Min(grid.DesiredH, gridBoxMaxH);
            o.Grid = new Box(c.X, o.Footer.Y - gap - gridH, c.W, gridH);

            var dollTop = o.Header.Bottom + gap;
            var dollH = Math.Max(0f, o.Grid.Y - gap - dollTop);
            o.Doll = new Box(c.X, dollTop, c.W, dollH);
            var d = new DollFit
            {
                Cell = dollPitch - t.DollGap * k,
                Pitch = dollPitch,
                StorageColumns = Math.Max(1, (int)t.DollColumns),
            };
            d.StorageRows = (i.DollStorage + d.StorageColumns - 1) / d.StorageColumns;
            d.MirrorRows = Math.Max(0, i.DollMirrors);
            d.Rows = Math.Max(d.StorageRows, d.MirrorRows);
            d.VisibleRows = Math.Max(0, (int)Math.Floor((dollH - 2f * dollPad + t.DollGap * k + 0.01f) / dollPitch));
            d.Scroll = d.Rows > d.VisibleRows;
            o.DollCells = d;
        }

        private static int DollRows(InventoryInput i, InventoryTokens t)
        {
            var cols = Math.Max(1, (int)t.DollColumns);
            return Math.Max((i.DollStorage + cols - 1) / cols, i.DollMirrors);
        }

        private static void LayoutDock(InventoryInput i, InventoryTokens t, float k, InventoryLayout o)
        {
            var dock = o.Dock;
            var gap = t.Gap * k;
            // Container panels: title row on top, glyph action row at the bottom (layout-a2.png "Взять всё").
            var headerH = (t.PanelHeaderH + t.PanelActionsH) * k;
            var pad = t.PanelPad * k;
            var hasChest = i.ChestW > 0 && i.ChestH > 0;
            var hasPack = i.BackpackW > 0 && i.BackpackH > 0;
            GridFit chest = default, pack = default;
            if (hasChest) chest = Fit(i.ChestW, i.ChestH, dock.W, dock.H, headerH, pad, t.ContainerCell * k, t.ContainerGap * k, o.MinCell, t.ScrollbarW * k);
            if (hasPack) pack = Fit(i.BackpackW, i.BackpackH, dock.W, dock.H, headerH, pad, t.ContainerCell * k, t.ContainerGap * k, o.MinCell, t.ScrollbarW * k);

            float chestH = 0f, packH = 0f;
            if (hasChest && hasPack)
            {
                var minChest = MinPanelH(i.ChestH, chest.Pitch, headerH, pad);
                var minPack = MinPanelH(i.BackpackH, pack.Pitch, headerH, pad);
                var avail = dock.H - gap;
                if (chest.DesiredH + pack.DesiredH <= avail + 0.01f)
                {
                    // Both fit: the backpack keeps its content height, the chest fills the rest of the dock (one
                    // bottom edge with centre and crafting, layout-a3.png).
                    packH = pack.DesiredH;
                    chestH = avail - packH;
                }
                else if (minChest + minPack > avail + 0.01f)
                {
                    // Minimums do not fit: halves of the dock, each grid scrolls inside its panel (at least one
                    // visible row each), so both stay reachable and nothing overlaps (Codex M2 review #9).
                    chestH = avail / 2f;
                    packH = avail - chestH;
                    o.DockOverflow = true;
                    // Not even one row each: the chest keeps the dock, the backpack folds away (still reachable
                    // through its own open key, which shows it alone) - never an unusable sliver (Codex M2 review).
                    if (chestH < headerH + 2f * pad + chest.Pitch || packH < headerH + 2f * pad + pack.Pitch)
                    {
                        chestH = dock.H;
                        packH = 0f;
                        hasPack = false;
                        o.BackpackFolded = true;
                    }
                }
                else
                {
                    // Proportional to wanted height, each at least its minimum.
                    chestH = avail * chest.DesiredH / (chest.DesiredH + pack.DesiredH);
                    chestH = Clamp(chestH, minChest, avail - minPack);
                    packH = avail - chestH;
                }
            }
            // One panel: the whole dock height (same bottom edge as the other columns; the grid sits at the top).
            else if (hasChest)
            {
                chestH = dock.H;
            }
            else if (hasPack)
            {
                packH = dock.H;
            }

            if (hasChest)
            {
                o.Chest = new Box(dock.X, dock.Y, dock.W, chestH);
                o.ChestGrid = Refit(chest, chestH, headerH, pad);
            }
            if (hasPack)
            {
                var y = hasChest ? o.Chest.Bottom + gap : dock.Y;
                o.Backpack = new Box(dock.X, y, dock.W, packH);
                o.BackpackGrid = Refit(pack, packH, headerH, pad);
            }
        }

        private static float MinPanelH(int rows, float pitch, float headerH, float pad) =>
            headerH + 2f * pad + Math.Min(rows, 2) * pitch;

        // Visible rows / scroll for the height the panel finally got.
        private static GridFit Refit(GridFit g, float panelH, float headerH, float pad)
        {
            var inner = Math.Max(0f, panelH - headerH - 2f * pad);
            g.VisibleRows = Math.Min(g.Rows, Math.Max(1, (int)Math.Floor((inner + 0.01f) / g.Pitch)));
            g.ScrollV = g.Rows > g.VisibleRows;
            return g;
        }

        // Cell pitch for a W x H grid in a box: as large as the token allows, never below the physical floor;
        // subtract padding and a vertical scrollbar when the rows do not fit; horizontal scroll when even the
        // minimum pitch is too wide. DesiredH = all rows (capped to the box).
        public static GridFit Fit(int w, int h, float boxW, float boxH, float headerH, float pad, float cellMax, float cellGap, float minCell, float scrollbarW)
        {
            var g = new GridFit { Columns = Math.Max(0, w), Rows = Math.Max(0, h) };
            if (g.Columns == 0 || g.Rows == 0) return g;
            var minPitch = minCell + cellGap;
            var maxPitch = Math.Max(minPitch, cellMax + cellGap);
            var innerW = Math.Max(0f, boxW - 2f * pad);
            var innerH = Math.Max(0f, boxH - headerH - 2f * pad);

            var pitch = Clamp(innerW / g.Columns, minPitch, maxPitch);
            if (boxH > 0f && g.Rows * pitch > innerH + 0.01f)
            {
                g.ScrollV = true;
                pitch = Clamp((innerW - scrollbarW) / g.Columns, minPitch, maxPitch);
            }
            g.Pitch = pitch;
            g.Cell = pitch - cellGap;
            g.ContentW = g.Columns * pitch;
            g.ContentH = g.Rows * pitch;
            var viewW = innerW - (g.ScrollV ? scrollbarW : 0f);
            g.ScrollH = g.ContentW > viewW + 0.01f;
            g.VisibleColumns = g.ScrollH ? Math.Max(1, (int)Math.Floor((viewW + 0.01f) / pitch)) : g.Columns;
            var visible = boxH > 0f ? Math.Max(1, (int)Math.Floor((innerH + 0.01f) / pitch)) : g.Rows;
            g.VisibleRows = Math.Min(g.Rows, visible);
            g.ScrollV = g.Rows > g.VisibleRows;
            g.DesiredH = headerH + 2f * pad + g.Rows * pitch;
            return g;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
