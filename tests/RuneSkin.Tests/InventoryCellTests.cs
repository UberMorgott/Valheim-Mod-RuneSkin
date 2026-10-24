using System;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // Inventory grid cells (user defect 2026-10-23, RuneSkin 0.20.0): block centred without the trailing gap, cell parts
    // inside the cell, icon centred, count floor.
    public class InventoryCellTests
    {
        private static readonly CellTokens T = new CellTokens();

        [Fact]
        public void BlockOffsetGivesEqualMarginsWithoutTrailingGap()
        {
            // run 20261023-231942 at 1440: view 696 wide, 8 columns, pitch 78, gap 9 -> block 615
            var off = InventoryMath.BlockOffset(696f, 8, 78f, 9f);
            var block = 8 * 78f - 9f;
            Assert.Equal(off, 696f - off - block, 3);
            Assert.Equal(40.5f, off, 3);
        }

        [Fact]
        public void BlockOffsetVerticalEqualMargins()
        {
            var off = InventoryMath.BlockOffset(250f, 3, 78f, 9f);
            Assert.Equal(off, 250f - off - (3 * 78f - 9f), 3);
        }

        [Fact]
        public void GlyphCentredPivotKeepsTheIconRect()
        {
            // run 20261024-132030: icon placed top-left (pivot 0,1) at (7, -7), 50 x 50 -> centre at (32, -32)
            Assert.Equal(32f, InventoryMath.CentredPivotPos(7f, 0f, 50f), 3);
            Assert.Equal(-32f, InventoryMath.CentredPivotPos(-7f, 1f, 50f), 3);
            // already centred: unchanged
            Assert.Equal(5f, InventoryMath.CentredPivotPos(5f, 0.5f, 50f), 3);
            // scaled 0.82 about that centre: equal margins in a 64 cell
            var size = 50f * 0.82f;
            var left = 32f - size / 2f;
            Assert.Equal(left, 64f - (32f + size / 2f), 3);
        }

        [Fact]
        public void BlockTallerThanViewStartsAtTop()
        {
            Assert.Equal(0f, InventoryMath.BlockOffset(100f, 4, 78f, 9f));
            Assert.Equal(0f, InventoryMath.BlockOffset(100f, 0, 78f, 9f));
            Assert.Equal(0f, InventoryMath.BlockOffset(100f, 2, 0f, 9f));
        }

        [Theory]
        [InlineData(64f, 64f)]
        [InlineData(46f, 46f)]
        [InlineData(90f, 72f)]
        public void PartsInsideCellAndIconCentred(float w, float h)
        {
            var cell = new Box(0f, 0f, w, h);
            var p = InventoryMath.Cell(w, h, T);
            Assert.True(p.Icon.Inside(cell));
            Assert.True(p.Count.Inside(cell));
            Assert.True(p.Quality.Inside(cell));
            Assert.True(p.Durability.Inside(cell));
            Assert.Equal(w / 2f, p.Icon.X + p.Icon.W / 2f, 3);
            Assert.Equal(h / 2f, p.Icon.Y + p.Icon.H / 2f, 3);
            Assert.Equal(p.Icon.X, w - p.Icon.Right, 3);
            var side = Math.Min(w, h);
            Assert.InRange(p.Icon.W / side, 0.75f, 0.8f);
            // durability pill: inset on both sides and above the bottom edge
            Assert.True(p.Durability.X > 0f && p.Durability.Right < w && p.Durability.Bottom < h);
            Assert.Equal(p.Durability.X, w - p.Durability.Right, 3);
            // count bottom-right, quality top-right
            Assert.True(p.Count.Bottom < h && p.Count.Right < w && p.Count.Y + p.Count.H / 2f > h / 2f);
            Assert.True(p.Quality.Y > 0f && p.Quality.Right < w && p.Quality.Bottom <= h / 2f + 0.01f);
        }

        [Fact]
        public void EmptyCellHasNoParts()
        {
            var p = InventoryMath.Cell(0f, 64f, T);
            Assert.True(p.Icon.IsEmpty && p.Count.IsEmpty);
        }

        [Fact]
        public void FontRangeKeepsReadableFloor()
        {
            // 64-unit cell at 720p (0.6 px per unit): theme 19.2 units = 11.5 px < 12 px floor -> raised to the floor
            InventoryMath.FontRange(19.2f, 12f, 0.6f, out var min, out var max);
            Assert.Equal(20f, max, 3);
            Assert.Equal(20f, min, 3);
            // 1440p (1.44 px per unit): floor 12 px = 8.33 units
            InventoryMath.FontRange(19.2f, 12f, 1.44f, out min, out max);
            Assert.Equal(12f / 1.44f, min, 3);
            Assert.True(min * 1.44f >= 12f - 0.01f);
            InventoryMath.FontRange(19.2f, 12f, 0f, out min, out max);
            Assert.Equal(max, min);
        }
    }
}
