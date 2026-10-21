using System;
using System.Collections.Generic;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    public class InventoryLayoutTests
    {
        private static readonly InventoryTokens T = new InventoryTokens();

        private static InventoryLayout Run(float w, float h, float phys = 1f, int gw = 8, int gh = 4, int cw = 0, int ch = 0, int bw = 0, int bh = 0,
            int storage = 9, int mirrors = 3, float skin = 1f, Box safe = default, float hudTop = 0f) =>
            InventoryMath.Compute(new InventoryInput
            {
                CanvasW = w,
                CanvasH = h,
                PhysPerUnit = phys,
                GridW = gw,
                GridH = gh,
                ChestW = cw,
                ChestH = ch,
                BackpackW = bw,
                BackpackH = bh,
                DollStorage = storage,
                DollMirrors = mirrors,
                SkinScale = skin,
                Safe = safe,
                HudTop = hudTop
            }, T);

        private static void NoOverlaps(InventoryLayout o)
        {
            var boxes = new List<Box> { o.Dock, o.Center, o.Crafting };
            for (var a = 0; a < boxes.Count; a++)
                for (var b = a + 1; b < boxes.Count; b++)
                    Assert.False(boxes[a].Overlaps(boxes[b]), $"{boxes[a]} overlaps {boxes[b]}");
            foreach (var inner in new[] { o.Header, o.Doll, o.Grid, o.Footer })
                if (!inner.IsEmpty) Assert.True(inner.Inside(o.Center), $"{inner} outside centre {o.Center}");
            Assert.False(o.Doll.Overlaps(o.Grid));
            Assert.False(o.Grid.Overlaps(o.Footer));
            Assert.False(o.Header.Overlaps(o.Doll));
            if (!o.Chest.IsEmpty) Assert.True(o.Chest.Inside(o.Dock));
            if (!o.Backpack.IsEmpty) Assert.True(o.Backpack.Inside(o.Dock));
            Assert.False(o.Chest.Overlaps(o.Backpack));
        }

        private static void OneTopOneBottom(InventoryLayout o)
        {
            foreach (var b in new[] { o.Dock, o.Crafting })
            {
                if (b.IsEmpty) continue;
                Assert.Equal(o.Center.Y, b.Y, 2);
                Assert.Equal(o.Center.Bottom, b.Bottom, 2);
            }
        }

        [Fact]
        public void ReferenceCanvasIsFullLayoutA()
        {
            var o = Run(1920, 1080, cw: 8, ch: 4);
            Assert.Equal(InventoryMode.Full, o.Mode);
            Assert.Equal(32f, o.Dock.X, 2);
            Assert.Equal(528f, o.Dock.W, 2);
            Assert.Equal(576f, o.Center.X, 2);
            Assert.Equal(1312f, o.Crafting.X, 2);
            Assert.Equal(Math.Min(T.PanelMaxH, 1080 - T.HudBand - T.Margin), o.Center.H, 2);
            OneTopOneBottom(o);
            Assert.True(o.Center.Bottom <= 1080 - T.HudBand + 0.01f, "the block ends above the HUD band");
            Assert.Equal(T.GridCell + T.GridGap, o.Player.Pitch, 2);
            Assert.False(o.Player.ScrollV);
            Assert.False(o.DollCells.Scroll);
            Assert.Equal(o.Dock.H, o.Chest.H, 2); // chest alone fills the dock
            NoOverlaps(o);
        }

        [Fact]
        public void NoContainerDropsTheDockAndRecentres()
        {
            var o = Run(1920, 1080);
            Assert.True(o.Dock.IsEmpty);
            Assert.Equal(960f, (o.Center.X + o.Crafting.Right) / 2f, 1);
            Assert.Equal(T.Gap, o.Crafting.X - o.Center.Right, 2);
            OneTopOneBottom(o);
            NoOverlaps(o);
        }

        [Fact]
        public void LiveHudTopSetsTheBottomEdge()
        {
            var o = Run(1920, 1080, cw: 8, ch: 4, bw: 3, bh: 1, hudTop: 730f);
            Assert.Equal(730f - T.Gap, o.Center.Bottom, 2);
            OneTopOneBottom(o);
            Assert.False(o.Player.ScrollV, "grid rows shrink to fit, no scroll");
            Assert.False(o.DollCells.Scroll, "every doll row visible");
            Assert.True(o.Player.Pitch < T.GridCell + T.GridGap && o.Player.Cell >= T.MinCellPhysPx - 0.01f);
            Assert.True(o.Grid.Bottom <= o.Footer.Y + 0.01f && o.Doll.Y >= o.Header.Bottom - 0.01f);
            Assert.Equal(o.Dock.Bottom, o.Backpack.Bottom, 2);
            NoOverlaps(o);
        }

        [Fact]
        public void UltrawideKeepsCentredDesignColumn()
        {
            var o = Run(3440f * 1080f / 1440f, 1080, cw: 8, ch: 4);
            Assert.Equal(InventoryMode.Full, o.Mode);
            var mid = (o.Dock.X + o.Crafting.Right) / 2f;
            Assert.Equal(3440f * 1080f / 1440f / 2f, mid, 1);
            Assert.Equal(1856f, o.Crafting.Right - o.Dock.X, 1);
            NoOverlaps(o);
        }

        [Theory]
        [InlineData(1700, 1080, InventoryMode.Narrow)]
        [InlineData(1280, 720, InventoryMode.Stacked)]   // GuiScale 1.5 on 1920x1080
        [InlineData(1440, 1080, InventoryMode.Stacked)]  // 4:3
        public void BreakpointsNeverOverlap(float w, float h, InventoryMode mode)
        {
            var o = Run(w, h, cw: 5, ch: 2, bw: 6, bh: 3);
            Assert.Equal(mode, o.Mode);
            NoOverlaps(o);
            Assert.True(o.Crafting.W >= T.MinCraftW - 0.01f || mode == InventoryMode.Stacked);
        }

        [Fact]
        public void PhysicalCellFloorWinsOverSmallScale()
        {
            // 1280x720 at GuiScale 1: scaleFactor 0.667, 44 px = 66 logical units -> grid pitch >= 66 + gap.
            var o = Run(1920, 1080, phys: 2f / 3f);
            Assert.True(o.Player.Cell >= 66f - 0.01f, $"cell {o.Player.Cell}");
            Assert.True(o.Center.W >= 8 * (66f + T.GridGap));
        }

        [Fact]
        public void TinyAndHugeContainers()
        {
            var one = Run(1920, 1080, cw: 1, ch: 1);
            Assert.Equal(1, one.ChestGrid.Columns);
            Assert.Equal(T.ContainerCell + T.ContainerGap, one.ChestGrid.Pitch, 2);
            Assert.Equal(one.Dock.H, one.Chest.H, 2); // one bottom edge for every column

            var huge = Run(1920, 1080, cw: 8, ch: 30);
            Assert.True(huge.ChestGrid.ScrollV);
            Assert.True(huge.ChestGrid.VisibleRows < 30);
            Assert.True(huge.Chest.H <= huge.Dock.H + 0.01f);
            Assert.True(huge.ChestGrid.Cell >= 44f - 0.01f);

            var wide = Run(1920, 1080, phys: 0.5f, cw: 20, ch: 3);
            Assert.True(wide.ChestGrid.ScrollH, "20 columns at the 88-unit floor do not fit 528");
            Assert.True(wide.ChestGrid.VisibleColumns < 20);
        }

        [Fact]
        public void DockSplitByNeededHeight()
        {
            var both = Run(1920, 1080, cw: 8, ch: 4, bw: 6, bh: 3);
            Assert.False(both.DockOverflow);
            Assert.Equal(both.BackpackGrid.DesiredH, both.Backpack.H, 2);
            Assert.Equal(both.Dock.Bottom, both.Backpack.Bottom, 2); // the chest takes the rest of the dock
            Assert.True(both.Backpack.Y > both.Chest.Bottom);

            var tall = Run(1920, 1080, cw: 8, ch: 20, bw: 8, bh: 10);
            Assert.False(tall.DockOverflow);
            Assert.True(tall.ChestGrid.ScrollV && tall.BackpackGrid.ScrollV);
            Assert.Equal(tall.Dock.H, tall.Backpack.Bottom - tall.Chest.Y, 1);
            Assert.True(tall.Chest.H > tall.Backpack.H, "split follows wanted height");

            var packOnly = Run(1920, 1080, bw: 6, bh: 3);
            Assert.True(packOnly.Chest.IsEmpty);
            Assert.Equal(packOnly.Dock.Y, packOnly.Backpack.Y, 2);
            Assert.Equal(packOnly.Dock.H, packOnly.Backpack.H, 2);
        }

        [Fact]
        public void ImpossibleDockMinimumsFlagOverflow()
        {
            var folded = Run(1280, 600, cw: 8, ch: 10, bw: 8, bh: 10);
            Assert.True(folded.BackpackFolded && folded.Backpack.IsEmpty);
            Assert.True(folded.Chest.Inside(folded.Dock) && folded.ChestGrid.VisibleRows >= 1);
            var o = Run(1920, 760, cw: 8, ch: 10, bw: 8, bh: 10);
            Assert.True(o.DockOverflow);
            Assert.False(o.Chest.Overlaps(o.Backpack));
            Assert.True(o.Chest.Inside(o.Dock) && o.Backpack.Inside(o.Dock), "fallback stays inside the dock");
            Assert.True(o.ChestGrid.VisibleRows >= 1 && o.BackpackGrid.VisibleRows >= 1);
            Assert.True(o.ChestGrid.ScrollV && o.BackpackGrid.ScrollV);
        }

        [Fact]
        public void DollGrowsWithRegisteredSlotsAndScrolls()
        {
            var nine = Run(1920, 1080);
            Assert.Equal(3, nine.DollCells.StorageRows);
            Assert.Equal(3, nine.DollCells.Rows);
            var many = Run(1920, 1080, storage: 40);
            Assert.Equal(14, many.DollCells.StorageRows);
            Assert.True(many.DollCells.Scroll);
            Assert.True(many.DollCells.VisibleRows >= 2);
            var none = Run(1920, 1080, storage: 0, mirrors: 0);
            Assert.Equal(0, none.DollCells.Rows);
            Assert.False(none.DollCells.Scroll);
        }

        [Fact]
        public void PlayerGridFollowsInventorySize()
        {
            var vplus = Run(1920, 1080, gh: 7);
            Assert.Equal(7, vplus.Player.Rows);
            NoOverlaps(vplus);
            var deep = Run(1920, 1080, gh: 20);
            Assert.True(deep.Player.ScrollV);
            Assert.True(deep.DollCells.VisibleRows >= 2, "doll keeps its minimum rows");
            NoOverlaps(deep);
        }

        [Fact]
        public void SafeAreaAndSkinScale()
        {
            var o = Run(1920, 1080, cw: 8, ch: 4, safe: new Box(60, 0, 1800, 1080));
            Assert.True(o.Dock.X >= 60 + T.Margin - 0.01f);
            Assert.True(o.Crafting.Right <= 1860 - T.Margin + 0.01f);
            NoOverlaps(o);
            var big = Run(1920, 1080, cw: 8, ch: 4, skin: 1.15f);
            Assert.NotEqual(InventoryMode.Full, big.Mode);
            NoOverlaps(big);
        }
    }
}
