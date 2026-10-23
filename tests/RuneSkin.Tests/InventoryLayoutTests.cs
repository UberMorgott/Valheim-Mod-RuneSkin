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
            int storage = 9, float skin = 1f, Box safe = default, float hudTop = 0f) =>
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
            // Chest dock: the character block (header + doll) lives in the left column, the containers above the grid.
            var character = o.Docked ? o.Dock : o.Center;
            foreach (var inner in new[] { o.Header, o.Doll })
                if (!inner.IsEmpty) Assert.True(inner.Inside(character), $"{inner} outside {character}");
            foreach (var inner in new[] { o.Grid, o.Footer })
                if (!inner.IsEmpty) Assert.True(inner.Inside(o.PlayerPanel), $"{inner} outside player panel {o.PlayerPanel}");
            Assert.True(o.PlayerPanel.Inside(o.Center));
            Assert.False(o.Doll.Overlaps(o.Grid));
            Assert.False(o.Grid.Overlaps(o.Footer));
            Assert.False(o.Header.Overlaps(o.Doll));
            var dock = o.Docked ? o.Stack : o.Dock;
            foreach (var panel in new[] { o.Chest, o.Backpack })
            {
                if (panel.IsEmpty) continue;
                Assert.True(panel.Inside(dock), $"{panel} outside {dock}");
                Assert.False(panel.Overlaps(o.PlayerPanel), $"{panel} over the player panel {o.PlayerPanel}");
                Assert.False(panel.Overlaps(o.Crafting));
                Assert.False(panel.Overlaps(o.Header) || panel.Overlaps(o.Doll));
            }
            Assert.False(o.Chest.Overlaps(o.Backpack));
        }

        // Chest dock contract (harness ui-runeskin-m2 `chest-dock`): the lowest container panel ends one gap above the player
        // panel, every container panel spans the centre column (grids centred over the player grid), same pitch.
        private static void DockedOverGrid(InventoryLayout o)
        {
            Assert.True(o.Docked, "container stack over the player grid");
            var lowest = o.Chest.IsEmpty ? o.Backpack : o.Chest;
            Assert.Equal(o.PlayerPanel.Y - T.Gap, lowest.Bottom, 2);
            foreach (var (panel, fit) in new[] { (o.Chest, o.ChestGrid), (o.Backpack, o.BackpackGrid) })
            {
                if (panel.IsEmpty) continue;
                // Centred on the column axis, as wide as its grid (min width for title + actions, never past the column).
                Assert.Equal(o.Center.X + o.Center.W / 2f, panel.X + panel.W / 2f, 2);
                var want = fit.ContentW + 2f * (T.PanelPad + (fit.ScrollV ? T.ScrollbarW : 0f));
                Assert.Equal(fit.Columns >= o.Player.Columns ? o.Center.W : Math.Min(o.Center.W, Math.Max(T.ContainerMinW, want)), panel.W, 2);
                Assert.True(panel.H <= T.ContainerMaxH + 0.01f);
            }
            if (!o.Chest.IsEmpty && !o.Backpack.IsEmpty) Assert.Equal(o.Chest.Y - T.Gap, o.Backpack.Bottom, 2);
            // Character panel in the left column: header + doll, ends a pad under the doll (mockups chest-small / chest-big).
            Assert.True(o.Character.Inside(o.Dock), $"{o.Character} outside {o.Dock}");
            Assert.True(o.Header.Inside(o.Character) && o.Doll.Inside(o.Character), $"{o.Header} {o.Doll} outside {o.Character}");
            Assert.True(o.Character.H < o.Dock.H || o.Doll.Bottom >= o.Dock.Bottom - T.PanelPad - 0.01f, "character panel sized to its content");
        }

        [Fact]
        public void DraggedLayoutKeepsTheDock()
        {
            var o = Run(1920, 1080, cw: 5, ch: 2, hudTop: 900f);
            var s = InventoryMath.Shift(o, 40f, -20f);
            Assert.True(s.Docked);
            Assert.Equal(o.PlayerPanel.X + 40f, s.PlayerPanel.X, 2);
            Assert.Equal(o.Character.Y - 20f, s.Character.Y, 2);
            Assert.Equal(o.Stack.W, s.Stack.W, 2);
        }

        [Fact]
        public void SmallChestPanelFitsItsGrid()
        {
            var o = Run(1920, 1080, gh: 3, cw: 5, ch: 2, hudTop: 900f);
            DockedOverGrid(o);
            Assert.True(o.Chest.W < o.PlayerPanel.W - 1f, $"{o.Chest} vs {o.PlayerPanel}");
            Assert.False(o.ChestGrid.ScrollV);
            var full = Run(1920, 1080, gh: 3, cw: 8, ch: 2, hudTop: 900f);
            Assert.Equal(full.PlayerPanel.X, full.Chest.X, 2);
            Assert.Equal(full.PlayerPanel.W, full.Chest.W, 2);
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
            DockedOverGrid(o);
            Assert.Equal(o.Player.Pitch, o.ChestGrid.Pitch, 2);
            Assert.True(o.ChestGrid.VisibleRows >= 3, $"{o.ChestGrid}");
            // Live strip layout (hotbar row on the HUD strip, 3 grid rows, no hotbar header): the 8x4 chest shows whole.
            var strip = InventoryMath.Compute(new InventoryInput { CanvasW = 1920, CanvasH = 1080, GridW = 8, GridH = 3, ChestW = 8, ChestH = 4 },
                new InventoryTokens { HotbarHeaderH = 0f });
            Assert.True(strip.Docked && !strip.ChestGrid.ScrollV, $"{strip.ChestGrid}");
            Assert.Equal(strip.ChestGrid.DesiredH, strip.Chest.H, 2); // as tall as its rows
            NoOverlaps(o);
        }

        [Fact]
        public void ChestNeverMovesThePlayerGrid()
        {
            var alone = Run(1920, 1080, hudTop: 900f);
            foreach (var (cw, ch) in new[] { (5, 2), (8, 4), (8, 30), (12, 8) })
            {
                var o = Run(1920, 1080, cw: cw, ch: ch, hudTop: 900f);
                DockedOverGrid(o);
                Assert.Equal(alone.Grid.Y, o.Grid.Y, 2);
                Assert.Equal(alone.Grid.H, o.Grid.H, 2);
                Assert.Equal(alone.Footer.Y, o.Footer.Y, 2);
                Assert.Equal(alone.Player.Pitch, o.Player.Pitch, 2);
                // Grid centred in the centre column, container grid centred in a panel of the same column: one axis.
                Assert.Equal(o.Grid.X + o.Grid.W / 2f, o.Chest.X + o.Chest.W / 2f, 2);
                NoOverlaps(o);
            }
        }

        [Fact]
        public void CharacterBlockMovesToTheLeftColumn()
        {
            var o = Run(1920, 1080, cw: 8, ch: 4);
            Assert.True(o.Header.Inside(o.Dock) && o.Doll.Inside(o.Dock));
            Assert.Equal(o.Center.Y, o.Header.Y, 2);
            Assert.False(o.DollCells.Scroll, "the left column holds every doll row");
            var alone = Run(1920, 1080);
            Assert.False(alone.Docked);
            Assert.True(alone.Header.Inside(alone.Center) && alone.Doll.Inside(alone.Center));
            Assert.Equal(alone.Center.X, alone.PlayerPanel.X, 2);
            Assert.Equal(alone.Center.H, alone.PlayerPanel.H, 2);
        }

        [Theory]
        [InlineData(1f)]          // 1920x1080 at GuiScale 1
        [InlineData(2f / 3f)]     // 1280x720: logical 1920x1080, 44 px floor = 66 units
        [InlineData(4f / 3f)]     // 2560x1440
        public void ChestDockAtEveryResolution(float phys)
        {
            foreach (var (cw, ch) in new[] { (5, 2), (8, 4) })
            {
                var o = Run(1920, 1080, phys: phys, cw: cw, ch: ch, hudTop: 1080f - 190f);
                DockedOverGrid(o);
                Assert.True(o.ChestGrid.VisibleRows >= 1);
                Assert.True(o.Chest.Inside(new Box(0, 0, 1920, 1080)) && o.Chest.Y >= o.Center.Y - 0.01f);
                NoOverlaps(o);
            }
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
            DockedOverGrid(o);
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
            Assert.Equal(one.Player.Pitch, one.ChestGrid.Pitch, 2); // the player grid's cells
            Assert.Equal(one.ChestGrid.DesiredH, one.Chest.H, 2);
            DockedOverGrid(one);

            // Drawers / big chests / carts: scroll inside the capped stack, the grid stays put.
            var huge = Run(1920, 1080, cw: 8, ch: 30);
            DockedOverGrid(huge);
            Assert.True(huge.ChestGrid.ScrollV);
            Assert.True(huge.ChestGrid.VisibleRows >= 1 && huge.ChestGrid.VisibleRows < 30);
            Assert.True(huge.Chest.H <= huge.Stack.H + 0.01f);
            Assert.True(huge.ChestGrid.Cell >= 44f - 0.01f);
            NoOverlaps(huge);

            var wide = Run(1920, 1080, phys: 0.5f, cw: 20, ch: 3);
            Assert.True(wide.ChestGrid.ScrollH, "20 columns at the 88-unit floor do not fit the centre column");
            Assert.True(wide.ChestGrid.VisibleColumns < 20);
        }

        [Fact]
        public void BackpackStacksAboveTheChest()
        {
            var both = Run(1920, 1080, gh: 2, cw: 5, ch: 2, bw: 3, bh: 1);
            DockedOverGrid(both);
            Assert.False(both.DockOverflow);
            Assert.Equal(both.BackpackGrid.DesiredH, both.Backpack.H, 2);
            Assert.Equal(both.ChestGrid.DesiredH, both.Chest.H, 2);
            // Short of room: one row each at least, both scroll, the backpack never folds while a row each fits.
            // (live strip layout: 3 grid rows, no hotbar header; ui-runeskin-m2 chest-backpack state)
            var tight = InventoryMath.Compute(new InventoryInput { CanvasW = 1920, CanvasH = 1080, GridW = 8, GridH = 3, ChestW = 8, ChestH = 4, BackpackW = 3, BackpackH = 1 },
                new InventoryTokens { HotbarHeaderH = 0f });
            Assert.True(tight.DockOverflow);
            DockedOverGrid(tight);
            Assert.False(tight.BackpackFolded);
            Assert.True(tight.ChestGrid.VisibleRows >= 1 && tight.BackpackGrid.VisibleRows >= 1);
            NoOverlaps(tight);
            NoOverlaps(both);

            var tall = Run(1920, 1080, gh: 2, cw: 8, ch: 20, bw: 8, bh: 10);
            DockedOverGrid(tall);
            Assert.True(tall.ChestGrid.ScrollV && tall.BackpackGrid.ScrollV);
            Assert.Equal(tall.Stack.H, tall.Chest.Bottom - tall.Backpack.Y, 1);
            Assert.True(tall.Chest.H >= tall.Backpack.H, "split follows wanted height");
            NoOverlaps(tall);

            // Worn backpack alone (AdventureBackpacks shows it in m_container): same spot as a chest.
            var packOnly = Run(1920, 1080, bw: 6, bh: 3);
            Assert.True(packOnly.Chest.IsEmpty);
            DockedOverGrid(packOnly);
            NoOverlaps(packOnly);
        }

        [Fact]
        public void ShortCanvasFallsBackToTheSideDock()
        {
            // Not one container row fits above the grid: the stack takes the left column, character block stays.
            var o = Run(1280, 480, cw: 8, ch: 10, bw: 8, bh: 10);
            Assert.False(o.Docked);
            Assert.True(o.Chest.Inside(o.Dock) && o.ChestGrid.VisibleRows >= 1);
            Assert.True(o.Header.Inside(o.Center) && o.Doll.Inside(o.Center));
            Assert.Equal(o.Center.X, o.PlayerPanel.X, 2);
            NoOverlaps(o);
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
            var none = Run(1920, 1080, storage: 0);
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
