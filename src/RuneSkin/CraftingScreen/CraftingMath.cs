using System;
using RuneSkin.InventoryScreen;

namespace RuneSkin.CraftingScreen
{
    // Pure geometry of the crafting column (M3, mockup-1 / layout A right column; no Unity types: unit-tested on
    // net8). Logical canvas units of the InventoryGui canvas, origin top-left, y down, inside the crafting box that
    // InventoryMath gives (same top / bottom / gutters as dock and centre). Top-down: header (station icon | name
    // plaque | level badge), mode tab row (craft / upgrade / mod tabs), category row (CraftingFilter), then two
    // columns: search + recipe list well left, detail well right (title, item icon, description, quality row,
    // requirement grid) with the button row (repair | craft) under it. Every count is an input (tabs, categories,
    // requirements); tokens give only sizes and gaps.
    public sealed class CraftingTokens
    {
        public float Pad = 18f, Gap = 8f;
        public float HeaderH = 80f, TabH = 36f, TabW = 150f, TabMinW = 96f, ArrowW = 28f;
        public float CategoryH = 44f, SearchH = 38f;
        public float ListShare = 0.4f, RowH = 46f, ScrollbarW = 10f;
        public float TitleH = 32f, CraftTypeH = 24f, IconH = 112f, QualityH = 44f, DescriptionMinH = 48f;
        public float ReqW = 68f, ReqH = 88f, ReqMinW = 52f, ReqMaxRows = 2f;
        public float ButtonH = 64f;
        public float WellPad = 10f;
    }

    public sealed class CraftingInput
    {
        public Box Panel;
        // Content inset of the panel stack (corner-cap keep-out, canvas units); 0 = Pad only.
        public float CapInset;
        public float Scale = 1f;
        public int Tabs, Categories, Requirements;
        public bool Search, Repair, CraftType, Quality;
        // First visible tab / category of a scrolled row (row arrows).
        public int TabOffset, CategoryOffset;
    }

    // A row of equal cells that scrolls when the count does not fit: Cells[i] empty = scrolled out.
    public sealed class RowFit
    {
        public Box[] Cells = Array.Empty<Box>();
        public Box Prev, Next;
        public int First, Visible;
        public bool Scroll;
    }

    public sealed class CraftingLayout
    {
        public Box Content, Header, StationIcon, Title, Level;
        public Box TabRow, CategoryRow, Search, List, ListView, ListScrollbar;
        public Box Detail, DetailTitle, CraftType, Icon, Description, Quality, Requirements, ButtonRow, Repair, Craft;
        public RowFit Tabs = new RowFit(), Categories = new RowFit();
        public Box[] RequirementCells = Array.Empty<Box>();
        public int RequirementColumns, RequirementRows;
        public float RowPitch;

        public override string ToString() =>
            $"header {Header} tabs {TabRow}({Tabs.Visible}/{Tabs.Cells.Length}{(Tabs.Scroll ? " scroll" : "")}) cats {CategoryRow}({Categories.Visible}/{Categories.Cells.Length}) " +
            $"list {List} detail {Detail} req {Requirements} {RequirementColumns}x{RequirementRows} buttons {ButtonRow}";
    }

    public static class CraftingMath
    {
        public static CraftingLayout Compute(CraftingInput i, CraftingTokens t)
        {
            var o = new CraftingLayout();
            var k = i.Scale > 0f ? i.Scale : 1f;
            float S(float v) => v * k;
            var gap = S(t.Gap);
            // Content: Pad from the edges; the corner caps only reach into the header row (kept clear horizontally)
            // and the bottom edge (half their keep-out).
            var pad = S(t.Pad);
            var bottomPad = Math.Max(pad, i.CapInset * 0.5f);
            var c = new Box(i.Panel.X + pad, i.Panel.Y + pad, Math.Max(0f, i.Panel.W - 2f * pad), Math.Max(0f, i.Panel.H - pad - bottomPad));
            o.Content = c;
            var y = c.Y;

            // Header: station icon (square) | name plaque | level badge (square, a little smaller), clear of the caps.
            var hh = Math.Min(S(t.HeaderH), c.H * 0.14f);
            var capX = Math.Max(0f, i.CapInset - pad);
            o.Header = new Box(c.X + capX, y, Math.Max(0f, c.W - 2f * capX), hh);
            var badge = hh * 0.8f;
            o.StationIcon = new Box(o.Header.X, y, hh, hh);
            o.Level = new Box(o.Header.Right - badge, y + (hh - badge) / 2f, badge, badge);
            o.Title = new Box(o.StationIcon.Right + gap, y, Math.Max(0f, o.Level.X - gap - o.StationIcon.Right - gap), hh);
            y += hh + gap;

            // Mode tabs (craft / upgrade / mod tabs): centred row, shrinks to TabMinW, then scrolls.
            if (i.Tabs > 0)
            {
                o.TabRow = new Box(c.X, y, c.W, S(t.TabH));
                o.Tabs = Row(o.TabRow, i.Tabs, S(t.TabW), S(t.TabMinW), gap, S(t.ArrowW), i.TabOffset, true);
                y += o.TabRow.H + gap;
            }
            // Category filter (CraftingFilter): square tabs, left-aligned, scrolls when they do not fit.
            if (i.Categories > 0)
            {
                var ch = S(t.CategoryH);
                o.CategoryRow = new Box(c.X, y, c.W, ch);
                o.Categories = Row(o.CategoryRow, i.Categories, ch, ch, gap * 0.5f, S(t.ArrowW), i.CategoryOffset, false);
                y += ch + gap;
            }

            // Body: list column (search + list well) | detail column (detail well + button row).
            var body = new Box(c.X, y, c.W, Math.Max(0f, c.Bottom - y));
            var listW = (float)Math.Floor(body.W * Clamp(t.ListShare, 0.25f, 0.6f));
            var lx = body.X;
            var ly = body.Y;
            if (i.Search)
            {
                o.Search = new Box(lx, ly, listW, S(t.SearchH));
                ly += o.Search.H + gap;
            }
            o.List = new Box(lx, ly, listW, Math.Max(0f, body.Bottom - ly));
            var wp = S(t.WellPad) * 0.5f;
            var sb = S(t.ScrollbarW);
            o.ListScrollbar = new Box(o.List.Right - wp - sb, o.List.Y + wp, sb, Math.Max(0f, o.List.H - 2f * wp));
            o.ListView = new Box(o.List.X + wp, o.List.Y + wp, Math.Max(0f, o.List.W - 3f * wp - sb), Math.Max(0f, o.List.H - 2f * wp));
            o.RowPitch = S(t.RowH);

            var dx = o.List.Right + gap;
            var dw = Math.Max(0f, body.Right - dx);
            var bh = S(t.ButtonH);
            o.ButtonRow = new Box(dx, body.Bottom - bh, dw, bh);
            if (i.Repair)
            {
                o.Repair = new Box(dx, o.ButtonRow.Y, bh, bh);
                o.Craft = new Box(o.Repair.Right + gap, o.ButtonRow.Y, Math.Max(0f, dw - bh - gap), bh);
            }
            else o.Craft = o.ButtonRow;
            o.Detail = new Box(dx, body.Y, dw, Math.Max(0f, o.ButtonRow.Y - gap - body.Y));

            // Detail well, top-down: title, craft type line, icon; bottom-up: requirements, quality row; the
            // description takes what is left (never below its minimum: the icon shrinks first).
            var d = Inset(o.Detail, S(t.WellPad), S(t.WellPad));
            var top = d.Y;
            o.DetailTitle = new Box(d.X, top, d.W, S(t.TitleH));
            top += o.DetailTitle.H;
            if (i.CraftType)
            {
                o.CraftType = new Box(d.X, top, d.W, S(t.CraftTypeH));
                top += o.CraftType.H;
            }
            top += gap * 0.5f;
            var bottom = d.Bottom;
            Requirements(o, i.Requirements, new Box(d.X, d.Y, d.W, d.H), S(t.ReqW), S(t.ReqH), S(t.ReqMinW), (int)Math.Max(1f, t.ReqMaxRows), gap * 0.75f);
            if (o.RequirementRows > 0)
            {
                var rh = o.Requirements.H;
                o.Requirements = new Box(d.X, bottom - rh, d.W, rh);
                for (var n = 0; n < o.RequirementCells.Length; n++)
                {
                    var cell = o.RequirementCells[n];
                    o.RequirementCells[n] = new Box(cell.X, cell.Y + o.Requirements.Y - d.Y, cell.W, cell.H);
                }
                bottom = o.Requirements.Y - gap;
            }
            if (i.Quality)
            {
                o.Quality = new Box(d.X, bottom - S(t.QualityH), d.W, S(t.QualityH));
                bottom = o.Quality.Y - gap * 0.5f;
            }
            var room = Math.Max(0f, bottom - top);
            var icon = Math.Max(0f, Math.Min(S(t.IconH), room - S(t.DescriptionMinH) - gap * 0.5f));
            o.Icon = new Box(d.X + (d.W - icon) / 2f, top, icon, icon);
            var descTop = top + (icon > 0f ? icon + gap * 0.5f : 0f);
            o.Description = new Box(d.X, descTop, d.W, Math.Max(0f, bottom - descTop));
            return o;
        }

        // Requirement grid: N cells left-aligned, rows wrap; when more rows than maxRows would be needed the cells
        // shrink (aspect kept) down to minW so they fit maxRows; past that the rows grow (the description gives way).
        // Cell boxes relative to `area` (y from area.Y). Requirements.H = grid height.
        public static void Requirements(CraftingLayout o, int n, Box area, float w, float h, float minW, int maxRows, float gap)
        {
            o.RequirementCells = new Box[Math.Max(0, n)];
            o.RequirementColumns = o.RequirementRows = 0;
            o.Requirements = new Box(area.X, area.Y, area.W, 0f);
            if (n <= 0 || !(area.W > 0f)) return;
            var cols = Columns(area.W, w, gap);
            if ((n + cols - 1) / cols > maxRows)
            {
                var want = (n + maxRows - 1) / maxRows;
                var fitW = (area.W - (want - 1) * gap) / want;
                var cw = Math.Max(minW, Math.Min(w, fitW));
                h *= cw / w;
                w = cw;
                cols = Columns(area.W, w, gap);
            }
            cols = Math.Min(cols, n);
            var rows = (n + cols - 1) / cols;
            o.RequirementColumns = cols;
            o.RequirementRows = rows;
            for (var i = 0; i < n; i++)
            {
                var col = i % cols;
                var row = i / cols;
                o.RequirementCells[i] = new Box(area.X + col * (w + gap), area.Y + row * (h + gap), w, h);
            }
            o.Requirements = new Box(area.X, area.Y, area.W, rows * h + (rows - 1) * gap);
        }

        // One scrollable row of `count` cells of width w (shrinking to minW) in `row`. centred: the row is centred
        // when it fits. Scrolled: arrows at both ends, `offset` = first visible cell (clamped).
        public static RowFit Row(Box row, int count, float w, float minW, float gap, float arrowW, int offset, bool centred)
        {
            var f = new RowFit { Cells = new Box[Math.Max(0, count)] };
            if (count <= 0 || !(row.W > 0f)) return f;
            var need = count * w + (count - 1) * gap;
            if (need > row.W)
            {
                w = Math.Max(minW, (row.W - (count - 1) * gap) / count);
                need = count * w + (count - 1) * gap;
            }
            if (need <= row.W + 0.01f)
            {
                var x = centred ? row.X + (row.W - need) / 2f : row.X;
                for (var i = 0; i < count; i++) f.Cells[i] = new Box(x + i * (w + gap), row.Y, w, row.H);
                f.Visible = count;
                return f;
            }
            f.Scroll = true;
            f.Prev = new Box(row.X, row.Y, arrowW, row.H);
            f.Next = new Box(row.Right - arrowW, row.Y, arrowW, row.H);
            var inner = row.W - 2f * (arrowW + gap);
            var visible = Math.Max(1, (int)Math.Floor((inner + gap) / (w + gap)));
            f.Visible = Math.Min(visible, count);
            f.First = Math.Max(0, Math.Min(offset, count - f.Visible));
            var x0 = row.X + arrowW + gap + (inner - (f.Visible * w + (f.Visible - 1) * gap)) / 2f;
            for (var i = f.First; i < f.First + f.Visible; i++) f.Cells[i] = new Box(x0 + (i - f.First) * (w + gap), row.Y, w, row.H);
            return f;
        }

        // VNEI item grid (BaseUI.RebuildSize: root = x*50+10 by y*50+100 VNEI units) that fits a box of w x h canvas
        // units, `unit` canvas units per VNEI unit; at least 1 x 1.
        public static (int X, int Y) VneiCells(float w, float h, float unit)
        {
            if (!(unit > 0f)) return (1, 1);
            var x = (int)Math.Floor((w / unit - 10f) / 50f);
            var y = (int)Math.Floor((h / unit - 100f) / 50f);
            return (Math.Max(1, x), Math.Max(1, y));
        }

        // RecipeChains box height cap: its own height, at most `max` and at most the room between `top` (description
        // minimum) and the box bottom, never under `min` (a header line + a line of tree).
        public static float ChainHeight(float height, float bottom, float top, float max, float min)
        {
            var cap = Math.Max(min, Math.Min(max, bottom - top));
            return Math.Min(height, cap);
        }

        private static int Columns(float width, float cell, float gap) => cell <= 0f ? 1 : Math.Max(1, (int)Math.Floor((width + gap) / (cell + gap)));

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        public static Box Inset(Box b, float x, float y) => new Box(b.X + x, b.Y + y, Math.Max(0f, b.W - 2f * x), Math.Max(0f, b.H - 2f * y));
    }
}
