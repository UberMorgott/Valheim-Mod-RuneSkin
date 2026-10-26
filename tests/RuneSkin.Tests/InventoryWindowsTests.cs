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

        private static InventoryTokens Theme(float cap = 20f) => new InventoryTokens
        {
            Margin = F("size.inv.margin"),
            Gap = F("size.inv.gap"),
            StripGap = F("size.inv.strip.gap"),
            CraftW = F("size.inv.craft"),
            MinCraftW = F("size.inv.craft.min"),
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
            Assert.InRange(o.Hotbar.Top - o.Inventory.Bottom, 0f, F("size.inv.strip.gap") * 1.01f);
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
            foreach (var g in new[] { "head", "chest", "legs", "back", "belt", "neck" }) Assert.Equal(0, InventoryWindowsMath.DollSide(g));
            foreach (var g in new[] { "ring", "light", "generic", null }) Assert.Equal(1, InventoryWindowsMath.DollSide(g));
            Assert.True(InventoryWindowsMath.DollRank("head") < InventoryWindowsMath.DollRank("neck"));
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
    }
}
