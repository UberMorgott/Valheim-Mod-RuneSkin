using System;
using RuneSkin.InventoryScreen;
using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Windowed inventory screen (mockup inv-layout-A, user 2026-10-26): the player grid locked onto the HUD strip's
    // hotbar (same cell, same column centres, side column over F), doll above it, backpack over chest left, crafting
    // right, all from the shipped theme and the real strip geometry (StripMath) at the resolution / GUI scale given.
    public class InventoryWindowsTests
    {
        private static float F(string token) => StripLayoutTests.Floats[token];

        // cap: the panel stack's corner-cap keep-out at the shipped art (clear.frame_panel_corner_* 87 sprite px / PPU 200 x 100 -
        // size.panel.glass.inset 11.5 = 32 canvas units, PanelMath.CapInset)
        private static InventoryTokens Theme(float cap = 32f) => new InventoryTokens
        {
            Margin = F("size.inv.margin"),
            Gap = F("size.inv.gap"),
            StripGap = F("size.inv.strip.gap"),
            CraftW = F("size.inv.craft"),
            MinCraftW = F("size.inv.craft.min"),
            BackpackMinW = F("size.inv.backpack.min"),
            PanelActionsH = F("size.inv.panel.actions"),
            HeaderH = F("size.inv.doll.header"),
            PanelHeaderH = F("size.inv.panel.header"),
            PanelPad = F("size.inv.panel.pad"),
            WinHeaderH = F("size.win.header"),
            GridCell = F("size.slot"),
            GridGap = F("size.inv.cell.gap"),
            DollFigureGap = F("size.inv.doll.figure.gap"),
            DollFigureAspect = F("share.inv.doll.figure"),
            PreviewMaxH = F("size.inv.preview.max"),
            DollMinShare = F("share.inv.doll.min"),
            ScrollbarW = F("size.inv.scrollbar"),
            PanelCapInset = cap,
            CenterCapInset = cap
        };

        // One screen: GuiScaler canvas (min(W/1920, H/1080) x GuiScale), the strip as HudCluster.Relayout builds it
        // (slot px -> px per strip unit, 16:9 box on ultrawide, centred, flush with the bottom).
        private sealed class Screen
        {
            public float W, H, S, PxPerUnit, Left;
            public StripMath.Input In;
            public StripMath.Layout L;
            public HotbarRow Row;
            public int Abilities;

            // Strip hotbar cell j / ability cell 0 centre and the cell side, screen px.
            public float HotbarCentrePx(int j) => Left + (L.HotbarX + In.Cell / 2f + j * (In.Cell + L.Gap)) * PxPerUnit;
            public float AbilityCentrePx => Left + (L.AbilityX0 + L.AbilityX) * PxPerUnit;
            public float CellPx => In.Cell * PxPerUnit;
        }

        private static Screen At(float w, float h, float gui = 1f, int abilities = 1, int potions = 4, int food = 3)
        {
            var s = Math.Min(w / 1920f, h / 1080f) * gui;
            var slotPx = StripMath.SlotPx(F("size.slot"), 1f, s);
            var ppu = StripMath.UnitPx(slotPx, F("size.hud.strip.cell"));
            HudMath.HudBox(0f, w, h, true, F("ratio.hud.ultrawide"), out var boxX, out var boxW);
            var widthPx = StripMath.WidthPx(boxW, 1f);
            var input = StripLayoutTests.In(abilities, food, potions, widthPx / ppu);
            var l = StripMath.Compute(input);
            var left = boxX + (boxW - widthPx) / 2f;
            var reserved = l.Height * ppu + F("size.panel.glass.inset") * s;
            return new Screen
            {
                W = w,
                H = h,
                S = s,
                PxPerUnit = ppu,
                Left = left,
                In = input,
                L = l,
                Abilities = abilities,
                Row = InventoryWindowsMath.FromStrip(l, input, abilities, left, ppu, reserved, h, s)
            };
        }

        private static WindowsLayout Run(Screen sc, int chestW = 0, int chestH = 0, int packW = 0, int packH = 0, int gridW = 8, int gridH = 3, int doll = 9, int plates = 4, InventoryTokens t = null) =>
            InventoryWindowsMath.Compute(new WindowsInput
            {
                CanvasW = sc.W / sc.S,
                CanvasH = sc.H / sc.S,
                PhysPerUnit = sc.S,
                Hotbar = sc.Row,
                GridW = gridW,
                GridH = gridH,
                ChestW = chestW,
                ChestH = chestH,
                BackpackW = packW,
                BackpackH = packH,
                DollLeft = Math.Min(6, doll),
                DollRight = Math.Max(0, doll - 6),
                SidePlates = plates
            }, t ?? Theme());

        public static TheoryData<float, float, float> Screens => new TheoryData<float, float, float>
        {
            { 1280f, 720f, 1f }, { 1600f, 900f, 1f }, { 1920f, 1080f, 1f }, { 2560f, 1440f, 1f }, // 16:9 720p..1440p
            { 1680f, 1050f, 1f }, { 1920f, 1200f, 1f }, { 2560f, 1600f, 1f },                     // 16:10
            { 2560f, 1080f, 1f }, { 3440f, 1440f, 1f },                                           // 21:9
            { 1920f, 1080f, 1.15f }, { 2560f, 1440f, 1.15f }                                     // accessibility GUI scale
        };

        [Theory]
        [MemberData(nameof(Screens))]
        public void PlayerGridIsTheHotbarRow(float w, float h, float gui)
        {
            var sc = At(w, h, gui);
            var o = Run(sc);
            // same cell side as the strip hotbar (screen px, +-1)
            Assert.InRange(o.Player.Cell * sc.S - sc.CellPx, -1f, 1f);
            // column j centred over hotbar cell j (screen px, +-1)
            for (var j = 0; j < 8; j++)
                Assert.InRange((o.PlayerCellX(j) + o.Player.Cell / 2f) * sc.S - sc.HotbarCentrePx(j), -1f, 1f);
            // side column over the F ability cell
            Assert.InRange((o.Side.X + o.Side.W / 2f) * sc.S - sc.AbilityCentrePx, -1f, 1f);
            // directly above the strip: the window ends the strip gap over its reserved top, the rows on its bottom inset
            Assert.True(o.Inventory.Bottom <= o.Hotbar.Top + 0.01f);
            Assert.InRange(o.Hotbar.Top - o.Inventory.Bottom, -0.01f, F("size.inv.strip.gap") + 0.01f);
            Assert.False(o.Player.ScrollV);
            Assert.Equal(3, o.Player.VisibleRows);
        }

        [Theory]
        [MemberData(nameof(Screens))]
        public void ContainerCellsAreTheHotbarCell(float w, float h, float gui)
        {
            var sc = At(w, h, gui);
            foreach (var (cw, ch) in new[] { (2, 2), (3, 2), (5, 2), (6, 3), (6, 4), (8, 3), (8, 4) })
                foreach (var (pw, ph) in new[] { (0, 0), (2, 3), (6, 3), (6, 4), (8, 2) })
                {
                    var o = Run(sc, cw, ch, pw, ph);
                    Assert.InRange(o.ChestGrid.Cell * sc.S - sc.CellPx, -1f, 1f);
                    Assert.Equal(o.Player.Pitch, o.ChestGrid.Pitch, 3);
                    if (!o.BackpackFolded && pw > 0)
                    {
                        Assert.InRange(o.BackpackGrid.Cell * sc.S - sc.CellPx, -1f, 1f);
                        Assert.Equal(o.Player.Pitch, o.BackpackGrid.Pitch, 3);
                    }
                }
        }

        [Theory]
        [MemberData(nameof(Screens))]
        public void WindowsNeverOverlapAndStayOnScreen(float w, float h, float gui)
        {
            var sc = At(w, h, gui);
            var cw = sc.W / sc.S;
            foreach (var (chW, chH) in new[] { (0, 0), (2, 2), (5, 2), (6, 4), (8, 4), (8, 8) })
                foreach (var (pw, ph) in new[] { (0, 0), (3, 2), (6, 4), (8, 2) })
                    foreach (var doll in new[] { 9, 12, 24 })
                    {
                        var o = Run(sc, chW, chH, pw, ph, doll: doll);
                        var wins = o.Windows;
                        for (var a = 0; a < wins.Length; a++)
                        {
                            if (wins[a].IsEmpty) continue;
                            Assert.True(wins[a].X >= -0.01f && wins[a].Right <= cw + 0.01f && wins[a].Y >= -0.01f, $"{a} {wins[a]} off canvas {cw}");
                            Assert.True(wins[a].Bottom <= o.Hotbar.Top + 0.01f, $"{a} {wins[a]} under the strip top {o.Hotbar.Top}");
                            for (var b = a + 1; b < wins.Length; b++)
                                Assert.False(wins[a].Overlaps(wins[b]), $"{w}x{h}@{gui} chest {chW}x{chH} pack {pw}x{ph} doll {doll}: {a} {wins[a]} overlaps {b} {wins[b]}");
                        }
                        // the inventory never moves with the containers or the doll
                        Assert.Equal(Run(sc).Inventory.ToString(), o.Inventory.ToString());
                    }
        }

        [Fact]
        public void Reference1080MatchesMockupA()
        {
            var sc = At(1920f, 1080f);
            var o = Run(sc, 8, 4, 6, 3);
            // centre column: doll on top, inventory on the strip; backpack above chest left, crafting right
            Assert.Equal(o.Inventory.X, o.Doll.X, 2);
            Assert.Equal(o.Inventory.W, o.Doll.W, 2);
            Assert.True(o.Doll.Bottom < o.Inventory.Y);
            Assert.True(o.Backpack.Bottom < o.Chest.Y);
            Assert.True(o.Chest.Right < o.Inventory.X && o.Crafting.X > o.Inventory.Right);
            Assert.Equal(o.Inventory.Bottom, o.Chest.Bottom, 2);
            Assert.Equal(o.Inventory.Bottom, o.Crafting.Bottom, 2);
            Assert.Equal(o.Doll.Y, o.Crafting.Y, 2);
            Assert.False(o.CraftingTight);
            // centre column on the screen centre (strip centred), within a few units
            Assert.InRange(o.Inventory.X + o.Inventory.W / 2f - 960f, -60f, 60f);
            // all 9 equipment cells shown (6 body slots left, rings + light right), no scroll; cells at least 80 % of the slot
            Assert.Equal(6, o.DollGrid.Rows);
            Assert.False(o.DollGrid.ScrollV);
            Assert.True(o.DollGrid.Cell >= 0.8f * o.Player.Cell - 0.01f && o.DollGrid.Cell <= o.Player.Cell + 0.01f, $"{o.DollGrid}");
        }

        [Fact]
        public void PreviewIsSmallerThanTheMockupFigure()
        {
            var t = Theme();
            var o = Run(At(1920f, 1080f), t: t);
            // mockup A's character is ~460 units tall; the user wants it close to the old in-game size (~270)
            Assert.True(o.DollPreview.H <= t.PreviewMaxH + 0.01f && o.DollPreview.H < 400f, $"{o.DollPreview}");
            Assert.Equal(o.DollPreview.H * t.DollFigureAspect, o.DollPreview.W, 2);
            Assert.True(o.DollPreview.Inside(o.DollFigure));
            Assert.True(o.DollFigure.X >= o.DollLeftX + o.DollGrid.Cell - 0.01f);
            Assert.True(o.DollFigure.Right <= o.DollRightX + 0.01f);
            // centred in the alcove
            Assert.Equal(o.DollFigure.X + o.DollFigure.W / 2f, o.DollPreview.X + o.DollPreview.W / 2f, 2);
        }

        [Fact]
        public void PreviewFigureFollowsTheInventoryCellScale()
        {
            // 2026-10-27 user report: the figure kept its size when the GUI scale / screen changed the inventory around it.
            // The figure box is the cap times the slot cell over its token (skin scale and any strip squeeze), so its
            // ratio to the grid cell holds wherever the alcove has room; on screen it grows with the GUI scale.
            var t = Theme();
            foreach (var (w, h, gui) in new[] { (1920f, 1080f, 1f), (2560f, 1440f, 1.2f), (1280f, 1024f, 1f), (1600f, 900f, 0.9f) })
            {
                var sc = At(w, h, gui);
                var o = Run(sc, t: t);
                var expected = Math.Min(o.DollFigure.H, t.PreviewMaxH * o.Player.Cell / t.GridCell);
                Assert.True(Math.Abs(o.DollPreview.H - expected) < 0.05f || o.DollPreview.W >= o.DollFigure.W - 0.05f, $"{w}x{h} gui {gui}: {o.DollPreview} figure {o.DollFigure} cell {o.Player.Cell}");
                Assert.True(o.DollPreview.Inside(o.DollFigure), $"{w}x{h}");
            }
            // room in the alcove at both: the figure on screen (units x canvas scale) grows with the GUI scale
            var a = Run(At(2560f, 1440f, 0.9f), t: t);
            var b = Run(At(2560f, 1440f, 1f), t: t);
            Assert.True(a.DollPreview.H < a.DollFigure.H && b.DollPreview.H < b.DollFigure.H, $"{a.DollFigure} {b.DollFigure}");
            var sa = Math.Min(2560f / 1920f, 1440f / 1080f);
            Assert.Equal(1f / 0.9f, b.DollPreview.H * sa / (a.DollPreview.H * sa * 0.9f), 2);
        }

        [Fact]
        public void DollOverflowScrollsInsideInventoryNeverMoves()
        {
            var sc = At(1920f, 1080f);
            var few = Run(sc, doll: 9);
            var many = Run(sc, doll: 40); // mods adding slots (EAQS AddSlot / RegisterSlot)
            Assert.True(many.DollGrid.ScrollV);
            Assert.True(many.DollGrid.VisibleRows >= 2);
            Assert.Equal(few.Doll.ToString(), many.Doll.ToString());
            Assert.Equal(few.Inventory.ToString(), many.Inventory.ToString());
            Assert.True(many.DollCellsY + many.DollGrid.ViewH <= many.DollContent.Bottom + 0.01f);
            Assert.True(many.DollGrid.Cell >= 0.8f * many.Player.Cell - 0.01f);
            // sides by slot kind, stable whatever else is registered: body left, jewellery / light / mods right
            foreach (var g in new[] { "head", "chest", "legs", "back", "belt" }) Assert.Equal(0, InventoryWindowsMath.DollSide(g));
            foreach (var g in new[] { "neck", "ring", "light", "generic", null }) Assert.Equal(1, InventoryWindowsMath.DollSide(g));
            Assert.True(InventoryWindowsMath.DollRank("head") < InventoryWindowsMath.DollRank("belt"));
            // necklace heads the right column, above the rings (user 2026-10-27)
            Assert.True(InventoryWindowsMath.DollRank("neck") < InventoryWindowsMath.DollRank("ring"));
            Assert.True(InventoryWindowsMath.DollRank("ring") < InventoryWindowsMath.DollRank("light") && InventoryWindowsMath.DollRank("light") < InventoryWindowsMath.DollRank("generic"));
        }

        [Fact]
        public void ModsAddingInventorySlots()
        {
            var sc = At(1920f, 1080f);
            var wide = Run(sc, gridW: 10);
            // columns past the hotbar continue at its pitch, the side column moves right past them
            Assert.Equal(sc.Row.Centre(9), wide.PlayerCellX(9) + wide.Player.Cell / 2f, 2);
            Assert.True(wide.Side.X >= wide.PlayerCellX(9) + wide.Player.Cell - 0.01f);
            Assert.InRange((wide.PlayerCellX(0) + wide.Player.Cell / 2f) * sc.S - sc.HotbarCentrePx(0), -1f, 1f);
            // a tall inventory (more rows): rows up to the doll's minimum, then the grid scrolls; bottom stays put
            var tall = Run(sc, gridH: 12);
            Assert.True(tall.Player.ScrollV);
            Assert.True(tall.Doll.H >= 0f && tall.DollGrid.VisibleRows >= 2);
            Assert.Equal(Run(sc).Inventory.Bottom, tall.Inventory.Bottom, 2);
            var four = Run(sc, gridH: 4);
            Assert.Equal(4, four.Player.VisibleRows);
            Assert.False(four.Player.ScrollV);
        }

        [Fact]
        public void SidePlatesStayInTheGridHeight()
        {
            var o = Run(At(1920f, 1080f), plates: 4);
            for (var n = 0; n < 4; n++)
            {
                var p = o.SidePlate(n);
                Assert.True(p.Y >= o.PlayerView.Y - 0.01f && p.Bottom <= o.PlayerView.Bottom + 0.01f, $"{n} {p}");
                Assert.True(p.H >= 40f, $"plate {n} {p.H}");
            }
            var two = Run(At(1920f, 1080f), plates: 2);
            Assert.Equal(two.Player.Pitch, two.SidePitch, 2); // room: full slot pitch
        }

        [Fact]
        public void WideChestScrollsSidewaysAtSixteenNine()
        {
            var o = Run(At(1920f, 1080f), 8, 4);
            // left room ~540 units: 6 of 8 columns at the hotbar pitch, the rest scroll; never narrower cells
            Assert.True(o.ChestGrid.ScrollH);
            Assert.True(o.ChestGrid.VisibleColumns >= 5 && o.ChestGrid.VisibleColumns < 8);
            Assert.True(o.Chest.Right <= o.Inventory.X - 15.99f);
            // 21:9: the whole chest fits
            var wide = Run(At(2560f, 1080f), 8, 4);
            Assert.False(wide.ChestGrid.ScrollH);
            Assert.Equal(8, wide.ChestGrid.VisibleColumns);
        }

        [Fact]
        public void ContainerGeometryMatchesItsPanelHelpers()
        {
            var t = Theme(32f);
            var o = Run(At(1920f, 1080f), 6, 4, 6, 3, t: t);
            foreach (var (box, g) in new[] { (o.Chest, o.ChestGrid), (o.Backpack, o.BackpackGrid) })
            {
                var view = InventoryMath.ContainerGridView(box, g.Fit, t, 1f);
                var row = InventoryMath.ActionRow(box, t, 1f);
                Assert.True(view.Bottom <= row.Y + 0.01f);
                Assert.True(g.ViewW <= view.W + 0.01f, $"{g} in {view}");
                Assert.True(g.ViewH <= view.H + 0.01f);
                Assert.True(row.Bottom <= box.Bottom - 32f + 0.01f);
            }
        }

        [Fact]
        public void TallStackSplitsOrFoldsTheBackpack()
        {
            var sc = At(1920f, 1080f);
            var tall = Run(sc, 6, 10, 6, 8);
            Assert.True(tall.DockOverflow);
            Assert.True(tall.ChestGrid.ScrollV && tall.BackpackGrid.ScrollV);
            Assert.True(tall.ChestGrid.VisibleRows >= 2 && tall.BackpackGrid.VisibleRows >= 2);
            Assert.True(tall.Backpack.Y >= tall.Doll.Y - 0.01f);
            // a very short screen (big GUI scale): the backpack folds rather than showing a sliver
            var tiny = Run(At(1280f, 720f, 1.6f), 6, 4, 6, 4);
            Assert.True(tiny.BackpackFolded || tiny.BackpackGrid.VisibleRows >= 1);
        }

        [Fact]
        public void FallbackWithoutStripCentresTheRow()
        {
            var t = Theme();
            var o = InventoryWindowsMath.Compute(new WindowsInput { CanvasW = 1920f, CanvasH = 1080f }, t);
            Assert.Equal(t.GridCell, o.Player.Cell, 2);
            Assert.Equal(t.GridCell + t.GridGap, o.Player.Pitch, 2);
            Assert.InRange(o.Inventory.X + o.Inventory.W / 2f, 950f, 970f);
            Assert.True(o.Inventory.Bottom <= 1080f - t.Margin);
        }

        private static readonly System.Text.Json.JsonSerializerOptions PreviewJson = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        // Offline preview (art/tools compare-inv): the 1080p layout of mockup A's state (backpack 6x3 over a reinforced
        // chest 8x4, 1 ability, 3 foods, Z/V/B) as JSON, written only when RUNESKIN_INV_PREVIEW names the file.
        [Fact]
        public void ExportPreviewLayout()
        {
            var path = Environment.GetEnvironmentVariable("RUNESKIN_INV_PREVIEW");
            if (string.IsNullOrEmpty(path)) return;
            var t = Theme();
            var sc = At(1920f, 1080f, 1f, 1, 3, 3);
            var o = Run(sc, 8, 4, 6, 3, t: t);
            float X(float stripX) => (sc.Left + stripX * sc.PxPerUnit) / sc.S;
            var height = sc.L.Height * sc.PxPerUnit / sc.S;
            var top = sc.H / sc.S - height;
            object B(Box b) => new { b.X, b.Y, b.W, b.H };
            object S(StripMath.Box b) => new { X = X(b.X), Y = top, W = b.W * sc.PxPerUnit / sc.S, H = height };
            var pieces = new System.Collections.Generic.List<object>
            {
                new { Name = "panel_block_l", Box = S(sc.L.EndL) },
                new { Name = "panel_hotbar", Box = S(sc.L.Hotbar) },
                new { Name = "panel_ability", Box = S(new StripMath.Box(sc.L.AbilityX0, 0f, sc.L.AbilitySliceW, sc.L.Height)) },
                new { Name = "panel_block_r", Box = S(sc.L.EndR) },
                new { Name = "panel_food_in", Box = S(sc.L.WingL.Inner) },
                new { Name = "panel_food_end", Box = S(sc.L.WingL.End) },
                new { Name = "panel_potion_in", Box = S(sc.L.WingR.Inner) },
                new { Name = "panel_potion_end", Box = S(sc.L.WingR.End) }
            };
            for (var k = 0; k < sc.L.WingL.Slots; k++) pieces.Add(new { Name = "panel_food_seg" + StripMath.SegArt(k, sc.L.WingL.Slots, 3), Box = S(sc.L.WingL.Seg(k)) });
            for (var k = 0; k < sc.L.WingR.Slots; k++) pieces.Add(new { Name = "panel_potion_seg" + StripMath.SegArt(k, sc.L.WingR.Slots, 4), Box = S(sc.L.WingR.Seg(k)) });
            float CellY(float stripY, float side) => sc.H / sc.S - (stripY + side) * sc.PxPerUnit / sc.S;
            var cell = sc.In.Cell * sc.PxPerUnit / sc.S;
            var hotbar = new System.Collections.Generic.List<object>();
            for (var j = 0; j < 8; j++) hotbar.Add(B(new Box(X(StripMath.CellX(sc.L.HotbarX, j, sc.In.Cell, sc.L.Gap)), CellY(sc.L.CellsY, sc.In.Cell), cell, cell)));
            var ab = sc.In.AbilityCell * sc.PxPerUnit / sc.S;
            var ability = B(new Box(X(sc.L.AbilityLeft(0, sc.In.AbilityCell)), CellY(sc.L.CellsY - (sc.In.AbilityCell - sc.In.CellH) / 2f, sc.In.AbilityCell), ab, ab));
            var potions = new System.Collections.Generic.List<object>();
            for (var n = 0; n < sc.L.PotionShown; n++) potions.Add(B(new Box(X(sc.L.PotionLeft(n)), CellY(sc.L.PotionY, sc.L.PotionCell), sc.L.PotionCell * sc.PxPerUnit / sc.S, sc.L.PotionCell * sc.PxPerUnit / sc.S)));
            var foods = new System.Collections.Generic.List<object>();
            for (var n = 0; n < sc.L.FoodShown; n++) foods.Add(B(new Box(X(sc.L.FoodLeft(n)), CellY(sc.L.FoodY, sc.L.FoodCell), sc.L.FoodCell * sc.PxPerUnit / sc.S, sc.L.FoodCell * sc.PxPerUnit / sc.S)));
            var bars = B(new Box(X(sc.L.BarsX), CellY(sc.L.BarsY, sc.L.BarsH), sc.L.BarsW * sc.PxPerUnit / sc.S, sc.L.BarsH * sc.PxPerUnit / sc.S));
            var player = new System.Collections.Generic.List<object>();
            for (var r = 0; r < o.Player.VisibleRows; r++)
                for (var c = 0; c < o.Player.Columns; c++)
                    player.Add(B(new Box(o.PlayerCellX(c), o.PlayerCellY(r), o.Player.Cell, o.Player.Cell)));
            var plates = new System.Collections.Generic.List<object>();
            for (var n = 0; n < 4; n++) plates.Add(B(o.SidePlate(n)));
            var doll = new System.Collections.Generic.List<object>();
            var left = new[] { "head", "chest", "legs", "back", "belt" };
            var right = new[] { "neck", "ring", "ring", "light" };
            for (var r = 0; r < left.Length; r++) doll.Add(new { Glyph = left[r], Box = B(new Box(o.DollLeftX, o.DollCellsY + r * o.DollGrid.Pitch, o.DollGrid.Cell, o.DollGrid.Cell)) });
            for (var r = 0; r < right.Length; r++) doll.Add(new { Glyph = right[r], Box = B(new Box(o.DollRightX, o.DollCellsY + r * o.DollGrid.Pitch, o.DollGrid.Cell, o.DollGrid.Cell)) });
            object Container(Box box, WindowGrid g)
            {
                var view = InventoryMath.ContainerGridView(box, g.Fit, t, 1f);
                var x0 = view.X + InventoryMath.BlockOffset(view.W, g.VisibleColumns, g.Pitch, g.Pitch - g.Cell);
                var cells = new System.Collections.Generic.List<object>();
                for (var r = 0; r < g.VisibleRows; r++)
                    for (var c = 0; c < g.VisibleColumns; c++)
                        cells.Add(B(new Box(x0 + c * g.Pitch, view.Y + r * g.Pitch, g.Cell, g.Cell)));
                return new { Box = B(box), View = B(view), Actions = B(InventoryMath.ActionRow(box, t, 1f)), Header = B(new Box(box.X + t.PanelPad, box.Y + t.PanelPad / 2f, box.W - 2f * t.PanelPad, t.PanelHeaderH - t.PanelPad / 2f)), Cells = cells, g.ScrollH, g.Columns, g.VisibleColumns };
            }
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                Inventory = B(o.Inventory),
                InventoryHeader = B(o.InventoryHeader),
                Player = player,
                Plates = plates,
                Doll = B(o.Doll),
                DollHeader = B(o.DollHeader),
                DollFigure = B(o.DollFigure),
                DollPreview = B(o.DollPreview),
                DollCells = doll,
                Backpack = Container(o.Backpack, o.BackpackGrid),
                Chest = Container(o.Chest, o.ChestGrid),
                Crafting = B(o.Crafting),
                Strip = pieces,
                Hotbar = hotbar,
                Ability = ability,
                Potions = potions,
                Foods = foods,
                Bars = bars,
                StripTop = top,
                o.Hotbar.Top
            }, PreviewJson);
            System.IO.File.WriteAllText(path, json);
        }

        // Every window's whole frame (the panel stack reaches size.panel.glass.inset beyond the box) on the canvas and above
        // the strip; container windows exactly their rows tall (no clipped row, no dead band).
        [Theory]
        [MemberData(nameof(Screens))]
        public void FramesOnScreenAndContainersFitTheirRows(float w, float h, float gui)
        {
            var sc = At(w, h, gui);
            var t = Theme();
            var over = F("size.panel.glass.inset");
            var cw = sc.W / sc.S;
            foreach (var (cW, cH) in new[] { (5, 2), (6, 4), (8, 4) })
            {
                var o = Run(sc, cW, cH, 6, 3, t: t);
                foreach (var b in o.Windows)
                {
                    if (b.IsEmpty) continue;
                    Assert.True(b.X - over >= -0.01f && b.Right + over <= cw + 0.01f && b.Y - over >= -0.01f, $"{w}x{h}@{gui} frame of {b} off the canvas");
                    Assert.True(b.Bottom <= o.Hotbar.Top + 0.01f);
                }
                foreach (var (box, g) in new[] { (o.Chest, o.ChestGrid), (o.Backpack, o.BackpackGrid) })
                {
                    if (box.IsEmpty) continue;
                    var view = InventoryMath.ContainerGridView(box, g.Fit, t, 1f);
                    Assert.Equal(g.VisibleRows * g.Pitch, view.H, 2);
                    Assert.Equal(InventoryWindowsMath.ContainerH(g.VisibleRows, g.Pitch, t, 1f), box.H, 2);
                }
            }
        }

        // The 16:9 numbers behind 6-of-8 chest columns (canvas units, 1920x1080 and 2560x1440 share the 1920x1080 canvas):
        // the left column has 527.5, an 8-column chest at the slot pitch needs 678.5 (614.5 cells + 2 x 32 cap keep-out);
        // even with no gap, no margin and no keep-out the 8 cells (614.5) exceed the 575.5 left of the grid window frame.
        [Fact]
        public void EightColumnsCannotFitAtSixteenNine()
        {
            var t = Theme();
            foreach (var (w, h) in new[] { (1920f, 1080f), (2560f, 1440f) })
            {
                var o = Run(At(w, h), 8, 4, t: t);
                var room = o.Inventory.X - t.Gap - t.Margin;
                var need = InventoryWindowsMath.ContainerW(8, o.Player.Pitch, o.Player.Pitch - o.Player.Cell, false, t, 1f);
                Assert.Equal(527.5f, room, 1);
                Assert.Equal(678.5f, need, 1);
                Assert.True(8 * o.Player.Pitch - (o.Player.Pitch - o.Player.Cell) > o.Inventory.X); // cells alone > all room
            }
        }

        // Side column plate parts (user 2026-10-26: values under the plates, the coin value under CurrencyPocket's button):
        // glyph, value and extra controls inside the plate, none overlapping, at the 1080 plate size and a tall one.
        [Theory]
        [InlineData(70f, 52f, 1)]
        [InlineData(70f, 52f, 0)]
        [InlineData(70f, 70f, 2)]
        [InlineData(93f, 69f, 1)]
        public void SidePlatePartsInsideNoOverlap(float w, float h, int extras)
        {
            var plate = new Box(1234f, 650f, w, h);
            InventoryWindowsMath.SidePlateParts(plate, true, extras, 0.9f, 0.32f, out var icon, out var value, out var e0, out var step);
            var parts = new System.Collections.Generic.List<Box> { icon, value };
            for (var n = 0; n < extras; n++) parts.Add(new Box(e0.X, e0.Y + n * step, e0.W, e0.H));
            foreach (var p in parts) Assert.True(p.Inside(plate) && !p.IsEmpty, $"{p} outside {plate}");
            for (var a = 0; a < parts.Count; a++)
                for (var b = a + 1; b < parts.Count; b++)
                    Assert.False(parts[a].Overlaps(parts[b]), $"{parts[a]} x {parts[b]}");
            Assert.True(value.Y >= icon.Bottom - 0.01f);
            Assert.True(icon.W >= (extras > 1 ? 0.2f : 0.3f) * w, $"icon {icon.W}");
        }

        [Fact]
        public void FitSizeIsDeterministicAndReadable()
        {
            Assert.Equal(20f, InventoryWindowsMath.FitSize(20f, 40f, 60f, 14f), 3); // fits: the wanted size
            Assert.Equal(15f, InventoryWindowsMath.FitSize(20f, 80f, 60f, 14f), 3); // too wide: scaled to the box
            Assert.Equal(14f, InventoryWindowsMath.FitSize(20f, 200f, 60f, 14f), 3); // never under the readable floor
            Assert.Equal(10f, InventoryWindowsMath.FitSize(10f, 200f, 60f, 14f), 3); // a wanted size under the floor stays
        }

        [Fact]
        public void BoundsInsetKeepsTheFrame()
        {
            var b = Restyle.WindowMoveMath.Inset(new Box(0f, 0f, 1920f, 900f), 11.5f, true);
            Assert.Equal(11.5f, b.X, 3);
            Assert.Equal(11.5f, b.Y, 3);
            Assert.Equal(1920f - 23f, b.W, 3);
            Assert.Equal(900f - 11.5f, b.H, 3);
            Assert.Equal(1080f - 23f, Restyle.WindowMoveMath.Inset(new Box(0f, 0f, 1920f, 1080f), 11.5f, false).H, 3);
        }

        [Fact]
        public void ScrollHelpers()
        {
            Assert.Equal(0, InventoryWindowsMath.ClampScroll(-3, 8, 6));
            Assert.Equal(2, InventoryWindowsMath.ClampScroll(5, 8, 6));
            Assert.Equal(0, InventoryWindowsMath.ClampScroll(5, 4, 6));
            Assert.Equal(2, InventoryWindowsMath.ScrollTo(0, 7, 6));
            Assert.Equal(1, InventoryWindowsMath.ScrollTo(2, 1, 6));
            Assert.Equal(2, InventoryWindowsMath.ScrollTo(2, 5, 6));
        }

        // Crafting content in the layout-A crafting window (docked in the room right of the hotbar-locked centre column:
        // 536 at 16:9 instead of 0.24.0's 576, run 20261026-222320): the recipe list keeps its 0.24.0 width (204, names
        // untruncated) and 8 requirements keep full-size cells (>= 66 wide: 3 columns, rows grow into the detail's height)
        // instead of shrinking to 60 (names under the 10 font floor). Shipped theme tokens, scale 1.
        public static TheoryData<float, float> CraftScreens => new TheoryData<float, float>
        {
            { 1280f, 720f }, { 1600f, 900f }, { 1920f, 1080f }, { 2560f, 1440f }, // 16:9
            { 1680f, 1050f }, { 1920f, 1200f }, { 2560f, 1600f },               // 16:10
            { 2560f, 1080f }, { 3440f, 1440f }                                  // 21:9
        };

        [Theory]
        [MemberData(nameof(CraftScreens))]
        public void CraftingContentKeepsItsRoom(float w, float h)
        {
            var sc = At(w, h);
            var o = Run(sc, 8, 4, 6, 3);
            Assert.False(o.CraftingTight);
            var t = new RuneSkin.CraftingScreen.CraftingTokens
            {
                Pad = F("size.craft.pad"),
                Gap = F("size.craft.gap"),
                ListShare = F("share.craft.list"),
                ListMinW = F("size.craft.list.min"),
                RowH = F("size.craft.row"),
                ScrollbarW = F("size.craft.scrollbar"),
                IconH = F("size.craft.icon"),
                DescriptionMinH = F("size.craft.description.min"),
                ReqW = F("size.craft.req.w"),
                ReqH = F("size.craft.req.h"),
                ReqMinW = F("size.craft.req.min"),
                ReqMaxRows = F("size.craft.req.rows"),
                WellPad = F("size.craft.well.pad")
            };
            foreach (var reqs in new[] { 1, 4, 6, 8 })
            {
                var i = new RuneSkin.CraftingScreen.CraftingInput
                {
                    Panel = o.Crafting,
                    CapInset = 32f,
                    Scale = 1f,
                    Tabs = 3,
                    Categories = 9,
                    Requirements = reqs,
                    Search = true,
                    Repair = true,
                    CraftType = true,
                    Quality = true,
                    WindowHeaderH = F("size.win.header")
                };
                var c = RuneSkin.CraftingScreen.CraftingMath.Compute(i, t);
                var at = $"{w}x{h} crafting {o.Crafting} reqs {reqs}";
                Assert.True(c.List.W >= 204f - 0.01f, $"{at}: list {c.List}");
                Assert.True(c.Detail.X >= c.List.Right && c.Detail.Right <= o.Crafting.Right - 32f + 0.01f, $"{at}: detail {c.Detail}");
                foreach (var cell in c.RequirementCells)
                {
                    Assert.True(cell.W >= 66f, $"{at}: req cell {cell}");
                    Assert.True(cell.Inside(c.Detail), $"{at}: req {cell} outside {c.Detail}");
                }
                for (var a = 0; a < c.RequirementCells.Length; a++)
                    for (var b = a + 1; b < c.RequirementCells.Length; b++)
                        Assert.False(c.RequirementCells[a].Overlaps(c.RequirementCells[b]), at);
                // the icon and the description minimum keep theirs above the grid
                Assert.Equal(t.IconH, c.Icon.H, 2);
                Assert.True(c.Description.H >= t.DescriptionMinH - 0.01f, $"{at}: description {c.Description}");
                Assert.True(c.Description.Bottom <= c.Requirements.Y + 0.01f, at);
            }
        }

        // Dock weight plate (run 20261027-105618: AB weight text "1…" at 1280x720): the token width grows until the value fits
        // (value box = plate - socket - 12, StatPlate), never past the action row's room; a roomy token stays as it is.
        [Fact]
        public void StatPlateWidth_GrowsToFitTheValue_WithinTheRoom()
        {
            Assert.Equal(110f, InventoryMath.StatPlateWidth(110f, 24f, 40f, 300f));
            Assert.Equal(24f + 90f + 12f, InventoryMath.StatPlateWidth(73f, 24f, 90f, 300f));
            Assert.Equal(90f, InventoryMath.StatValueBoxW(InventoryMath.StatPlateWidth(73f, 24f, 90f, 300f), 24f), 3);
            Assert.Equal(100f, InventoryMath.StatPlateWidth(73f, 24f, 90f, 100f));
            Assert.Equal(0f, InventoryMath.StatPlateWidth(73f, 24f, 90f, -5f));
        }

        // Offline sim of the AB backpack panel (run 20261027-111356 log: backpack (269.9,205.1,289.6,223.8), 3 columns, the
        // same canvas box at every resolution; skin scale 1, three 38.4 glyph actions at gap 8). Before: room 102.4 left of
        // the actions, value box 58.4 - "12.5/40" cut to "1…". Now the window is at least size.inv.backpack.min (316 =
        // 289.6 + 26) wide, grown leftwards: room 128.8, value box 84.8. Widths are PT Sans Bold advances (art/fonts, em):
        // the probe "00.0/00" = 3.489 em, "000.0/000" = 4.623 em; body floor 16 px = 24 units at 720p, 16 units at 1080p+.
        [Fact]
        public void AbBackpackDock_WeightPlate_FitsAtTheMinimumWidth()
        {
            var t = new InventoryTokens();
            Assert.Equal(316f, t.BackpackMinW);
            Assert.Equal(316f, F("size.inv.backpack.min"));
            var right = 269.9f + 289.6f;
            var actions = InventoryMath.ActionRow(new Box(right - t.BackpackMinW, 205.1f, t.BackpackMinW, 223.8f), t, 1f);
            var glyphs = 3f * t.FooterH * 0.8f + 2f * (t.Gap / 2f);
            var room = actions.W - glyphs - t.Gap;
            Assert.Equal(128.8f, room, 1);
            const float ph = 32f;
            float Box(float textW) => InventoryMath.StatValueBoxW(InventoryMath.StatPlateWidth(110f, ph, textW, room), ph);
            var small720 = 3.489f * 24f;  // "12.5/40" at 1280x720
            var big1080 = 4.623f * 16f;   // "999.9/999" at 1920x1080 and up
            Assert.True(Box(small720) >= small720, $"{Box(small720)} < {small720}");
            Assert.True(Box(big1080) >= big1080, $"{Box(big1080)} < {big1080}");
            // "999.9/999" at 720p (111 units) still needs ~27 more units: ellipsis there (out of option A's scope)
            Assert.True(Box(4.623f * 24f) < 4.623f * 24f);
        }

        // The backpack minimum grows the window leftwards only: right edge on the centre column, never past the room.
        [Theory]
        [MemberData(nameof(Screens))]
        public void BackpackMinimumWidthGrowsLeftwards(float w, float h, float gui)
        {
            var t = Theme();
            var sc = At(w, h, gui);
            foreach (var (cw, ch) in new[] { (0, 0), (5, 2), (8, 4) })
                foreach (var (pw, ph) in new[] { (2, 2), (3, 2), (3, 4), (6, 3) })
                {
                    var o = Run(sc, cw, ch, pw, ph, t: t);
                    if (o.BackpackFolded) continue;
                    var roomW = o.Inventory.X - t.Gap - (t.Margin);
                    Assert.Equal(o.Inventory.X - t.Gap, o.Backpack.Right, 2);
                    Assert.True(o.Backpack.W >= Math.Min(t.BackpackMinW, roomW) - 0.01f, $"{w}x{h}@{gui} pack {pw}x{ph}: {o.Backpack}");
                    Assert.True(o.Backpack.X >= t.Margin - 0.01f, $"{w}x{h}@{gui} pack {pw}x{ph}: {o.Backpack}");
                }
        }
    }
}
