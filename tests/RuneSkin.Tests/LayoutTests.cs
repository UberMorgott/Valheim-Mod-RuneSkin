using System;
using RuneSkin.PlayerHud;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    public class PanelCapTests
    {
        [Theory]
        [InlineData(87f, 200f, 0f, 43.5f)]  // frame_panel_corner_bl keep-out at PPU 200, tooltip stack pad 0
        [InlineData(87f, 200f, 40f, 3.5f)]  // stack reaching 40 beyond the host covers most of it
        [InlineData(87f, 200f, 60f, 0f)]    // never negative
        [InlineData(87f, 100f, 0f, 87f)]
        [InlineData(0f, 200f, 0f, 0f)]      // no clearance stated: no inset
        [InlineData(87f, 0f, 0f, 0f)]
        public void CapInsetIsClearanceMinusPad(float clear, float ppu, float pad, float expected) =>
            Assert.Equal(expected, PanelMath.CapInset(clear, ppu, pad), 3);
    }

    public class StackTests
    {
        [Theory]
        [InlineData(43.5f, 6.5f, 1f, 0f, 36f)]  // HP frame (87 px, rail top margin 13) under ES rail (bottom 2) at PPU 200
        [InlineData(25.3f, 0.55f, 2f, 0f, 22.75f)]
        [InlineData(20f, 0f, 0f, 0f, 20f)]       // no margins: edge to edge
        [InlineData(20f, 0f, 0f, 3f, 23f)]       // gap token on top
        [InlineData(4f, 5f, 5f, 0f, 0f)]         // never negative
        public void RailsTouch(float h, float edgeTop, float edgeBottom, float gap, float expected) =>
            Assert.Equal(expected, HudMath.StackStep(h, edgeTop, edgeBottom, gap), 3);

        [Theory]
        [InlineData(10.2f, 1.333f, 10.503f)]
        [InlineData(10f, 1f, 10f)]
        [InlineData(10.4f, 0f, 10.4f)]           // no scale known: unchanged
        public void SnapToPhysicalPixels(float v, float px, float expected) =>
            Assert.Equal(expected, HudMath.Snap(v, px), 2);
    }

    public class DiamondTests
    {
        // Diamond i = |x - cx| + |y - cy| <= cell / 2. Neighbours' facing edges lie on parallel lines; the
        // perpendicular distance between them must equal the gap, and the steps must be equal on both axes.
        [Theory]
        [InlineData(56f, 6f)]
        [InlineData(50f, 0f)]
        [InlineData(46f, 4f)]
        public void CheckerboardWithEvenGap(float cell, float gap)
        {
            var step = HudMath.DiamondStep(cell, gap);
            for (var i = 0; i < 4; i++)
            {
                HudMath.DiamondCell(i, step, out var x0, out var y0);
                HudMath.DiamondCell(i + 1, step, out var x1, out var y1);
                var dx = Math.Abs(x1 - x0);
                var dy = Math.Abs(y1 - y0);
                Assert.Equal(dx, dy, 3);
                // Facing edges: x + y = c (or x - y = c) lines, offset by (dx + dy - cell) in L1 -> / sqrt 2 perpendicular.
                Assert.Equal(gap, (dx + dy - cell) / (float)Math.Sqrt(2.0), 3);
                Assert.Equal(i % 2 == 0 ? 0f : step, y0, 3);
            }
        }
    }
}
