using System;
using RuneSkin.InventoryScreen;

namespace RuneSkin.CraftingScreen
{
    // Pure geometry of the crafting column (M3, mockup-1 / layout A right column; no Unity types: unit-tested on
    // net8). Logical canvas units of the InventoryGui canvas, origin top-left, y down, inside the crafting box that
    // InventoryMath gives (same top / bottom / gutters as dock and centre). Top-down: header (station icon | name
    // plaque | level badge), mode tab row (craft / upgrade / mod tabs), category row (CraftingFilter), its sub-group row (when shown), then two
    // columns: search + recipe list well left, detail well right (title, item icon, description, quality row,
    // requirement grid) with the button row (repair | craft) under it. Every count is an input (tabs, categories,
    // requirements); tokens give only sizes and gaps.
    public sealed class CraftingTokens
    {
        public float Pad = 18f, Gap = 8f;
        public float HeaderH = 80f, TabH = 36f, TabW = 150f, TabMinW = 96f, ArrowW = 28f;
        public float CategoryH = 44f, SubCategoryH = 44f, SearchH = 38f;
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
        // CraftingFilter sub-group tabs (second row under the categories, MT_CraftFilter/Subs); 0 = no row.
        public int SubCategories;
        public bool Search, Repair, CraftType, Quality;
        // First visible tab / category of a scrolled row (row arrows).
        public int TabOffset, CategoryOffset, SubCategoryOffset;
        // The shared window header band height (RuneWindowHeader, canvas units): > 0 = the header row is that band at the
        // panel's top edge (full width, like every window), the content starts under it; 0 = the padded header row.
        public float WindowHeaderH;
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
        public Box TabRow, CategoryRow, SubCategoryRow, Search, List, ListView, ListScrollbar;
        public Box Detail, DetailTitle, CraftType, Icon, Description, Quality, Requirements, ButtonRow, Repair, Craft;
        public RowFit Tabs = new RowFit(), Categories = new RowFit(), SubCategories = new RowFit();
        public Box[] RequirementCells = Array.Empty<Box>();
        public int RequirementColumns, RequirementRows;
        public float RowPitch;

        public override string ToString() =>
            $"header {Header} tabs {TabRow}({Tabs.Visible}/{Tabs.Cells.Length}{(Tabs.Scroll ? " scroll" : "")}) cats {CategoryRow}({Categories.Visible}/{Categories.Cells.Length}) subs {SubCategoryRow}({SubCategories.Visible}/{SubCategories.Cells.Length}) " +
            $"list {List} detail {Detail} req {Requirements} {RequirementColumns}x{RequirementRows} buttons {ButtonRow}";
    }

    // Craft row parts (canvas boxes): idle = Minus | Field | Plus | Button, progress = Bar | Cancel.
    public struct CraftRow
    {
        public Box Minus, Field, Plus, Button, Bar, Cancel;
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
            if (i.WindowHeaderH > 0f)
            {
                // The one window header (RuneWindowHeader): the band at the panel top, content under it.
                hh = i.WindowHeaderH;
                y = i.Panel.Y;
                o.Header = new Box(i.Panel.X, y, i.Panel.W, hh);
                c = new Box(c.X, i.Panel.Y + hh + gap, c.W, Math.Max(0f, c.Bottom - (i.Panel.Y + hh + gap)));
                o.Content = c;
            }
            var badge = hh * 0.8f;
            o.StationIcon = new Box(o.Header.X, y, hh, hh);
            o.Level = new Box(o.Header.Right - badge, y + (hh - badge) / 2f, badge, badge);
            o.Title = new Box(o.StationIcon.Right + gap, y, Math.Max(0f, o.Level.X - gap - o.StationIcon.Right - gap), hh);
            y = i.WindowHeaderH > 0f ? c.Y : y + hh + gap;

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
            // Sub-group row (weapon kinds, armor slots, ...): same square tabs and size as the categories, only while the filter
            // shows it (two or more groups); the body gives way.
            if (i.Categories > 0 && i.SubCategories > 0)
            {
                var sh = S(t.SubCategoryH);
                o.SubCategoryRow = new Box(c.X, y, c.W, sh);
                o.SubCategories = Row(o.SubCategoryRow, i.SubCategories, sh, sh, gap * 0.5f, S(t.ArrowW), i.SubCategoryOffset, false);
                y += sh + gap;
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

        // The craft row (o.Craft) split for its two states (approved mockup craft-progress.png). Idle: amount stepper
        // [-][N][+] at the left (step buttons stepShare * h wide, field fieldShare * h wide, all stepH * h tall, centred;
        // the whole stepper at most maxShare of the row, shrunk evenly below that), the craft button takes the rest.
        // Progress: framed bar | cancel button (cancelShare of the row, clamped to [h * 1.2, h * 2.6]). Everything inside
        // `craft`; stepper == false -> the button gets the whole row.
        public static CraftRow ActionRow(Box craft, bool stepper, float gap, float stepShare = 0.68f, float fieldShare = 0.85f,
            float maxShare = 0.4f, float cancelShare = 0.27f, float stepH = 0.95f)
        {
            var r = new CraftRow { Button = craft };
            if (craft.IsEmpty) return r;
            var h = craft.H;
            var inner = gap * 0.35f;
            if (stepper)
            {
                var step = h * stepShare;
                var field = h * fieldShare;
                var sh = h * Clamp(stepH, 0.3f, 1f);
                var need = 2f * step + field + 2f * inner;
                var max = craft.W * Clamp(maxShare, 0.1f, 0.6f);
                if (need > max)
                {
                    var k = max / need;
                    step *= k;
                    field *= k;
                    inner *= k;
                }
                var y = craft.Y + (h - sh) / 2f;
                r.Minus = new Box(craft.X, y, step, sh);
                r.Field = new Box(r.Minus.Right + inner, y, field, sh);
                r.Plus = new Box(r.Field.Right + inner, y, step, sh);
                r.Button = new Box(r.Plus.Right + gap, craft.Y, Math.Max(0f, craft.Right - r.Plus.Right - gap), h);
            }
            var cw = Clamp(craft.W * cancelShare, h * 1.2f, h * 2.6f);
            cw = Math.Min(cw, craft.W * 0.45f);
            r.Cancel = new Box(craft.Right - cw, craft.Y, cw, h);
            r.Bar = new Box(craft.X, craft.Y, Math.Max(0f, r.Cancel.X - gap - craft.X), h);
            return r;
        }

        // Craft amount: clamp to [1, max] (max < 1 -> 1); typed text parsed as digits only (empty / junk -> fallback).
        public static int ClampAmount(int n, int max) => Math.Max(1, Math.Min(n, Math.Max(1, max)));

        public static int ParseAmount(string s, int fallback, int max)
        {
            var i = 0;
            if (string.IsNullOrEmpty(s) || !Number(s, ref i, out var n) || !End(s, i)) return ClampAmount(fallback, max);
            return ClampAmount(n, max);
        }

        // Largest n in [1, cap] with ok(n) true, ok monotonic (true up to some n, false above); 0 when ok(1) is false.
        public static int LargestOk(int cap, Func<int, bool> ok)
        {
            if (cap < 1 || !ok(1)) return 0;
            int lo = 1, hi = cap;
            while (lo < hi)
            {
                var mid = lo + (hi - lo + 1) / 2;
                if (ok(mid)) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        private static int Columns(float width, float cell, float gap) => cell <= 0f ? 1 : Math.Max(1, (int)Math.Floor((width + gap) / (cell + gap)));

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        public static Box Inset(Box b, float x, float y) => new Box(b.X + x, b.Y + y, Math.Max(0f, b.W - 2f * x), Math.Max(0f, b.H - 2f * y));

        // Requirement amount "have/need" (ValheimPlus CraftFromChest rewrites the label so; vanilla writes only "need"),
        // read without substrings (this runs per requirement cell per frame). Spaces around the numbers allowed.
        public static bool TryParseHaveNeed(string s, out int have, out int need)
        {
            have = need = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var i = 0;
            return Number(s, ref i, out have) && Skip(s, ref i, '/') && Number(s, ref i, out need) && End(s, i);
        }

        private static bool Number(string s, ref int i, out int value)
        {
            value = 0;
            while (i < s.Length && s[i] == ' ') i++;
            var start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                if (value > 99999999) return false;
                value = value * 10 + (s[i] - '0');
                i++;
            }
            while (i < s.Length && s[i] == ' ') i++;
            return i > start && s[start] >= '0' && s[start] <= '9';
        }

        private static bool Skip(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) return false;
            i++;
            return true;
        }

        private static bool End(string s, int i) => i == s.Length;
    }
}
