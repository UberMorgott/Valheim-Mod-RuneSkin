using System.Collections.Generic;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    public class LayoutMathTests
    {
        private static LayoutMath.Span S(float min, float max) => new LayoutMath.Span(min, max);

        // An icon before the text bounds the span on the left, one after it on the right; overlapping and outside
        // images do not count.
        [Fact]
        public void FreeSpanStopsAtIconsBesideTheText()
        {
            var span = LayoutMath.FreeSpan(-100f, 100f, -40f, 40f, new List<LayoutMath.Span> { S(-90f, -60f), S(70f, 90f), S(-10f, 10f), S(120f, 140f) });
            Assert.Equal(-60f, span.Min, 3);
            Assert.Equal(70f, span.Max, 3);
        }

        [Fact]
        public void FreeSpanWithoutIconsIsTheBox()
        {
            var span = LayoutMath.FreeSpan(0f, 200f, 50f, 150f, null);
            Assert.Equal(0f, span.Min, 3);
            Assert.Equal(200f, span.Max, 3);
            Assert.Equal(100f, span.Centre, 3);
        }

        // Recipe row: icon right edge at 36, name rect starting at 44 -> margin 4 puts the text at 36 + 12.
        [Theory]
        [InlineData(44f, 36f, 12f, 4f)]
        [InlineData(0f, 0f, 12f, 12f)]
        [InlineData(40f, 0f, 12f, -28f)]
        [InlineData(10f, 0f, -5f, -10f)]
        public void StartMarginPutsTheTextPadAfterTheEdge(float rectMin, float edge, float pad, float want) =>
            Assert.Equal(want, LayoutMath.StartMargin(rectMin, edge, pad), 3);

        [Theory]
        [InlineData(-15f, 0f, 12f, -3f)]
        [InlineData(100f, 100f, 12f, 12f)]
        public void EndMarginPutsTheTextPadBeforeTheEdge(float rectMax, float edge, float pad, float want) =>
            Assert.Equal(want, LayoutMath.EndMargin(rectMax, edge, pad), 3);

        // Requirement socket 86 tall: icon 26.7 from the top, 22.4 from the bottom -> down by 2.15.
        [Theory]
        [InlineData(0f, 86f, 22.4f, 59.3f, 2.15f)]
        [InlineData(0f, 64f, 8f, 56f, 0f)]
        [InlineData(-32f, 32f, -20f, 40f, -10f)]
        public void CentreOffsetCentresTheIcon(float cellMin, float cellMax, float min, float max, float want) =>
            Assert.Equal(want, LayoutMath.CentreOffset(cellMin, cellMax, min, max), 3);

        // Key letter 2.1 over the cell top -> down 2.1; inside -> 0; wider than the cell -> centred.
        [Theory]
        [InlineData(-64f, 0f, -22.1f, 2.1f, -2.1f)]
        [InlineData(-64f, 0f, -65f, -45.6f, 1f)]
        [InlineData(0f, 64f, 10f, 30f, 0f)]
        [InlineData(0f, 64f, -10f, 84f, -5f)]
        public void ClampOffsetKeepsTextsInside(float boxMin, float boxMax, float min, float max, float want) =>
            Assert.Equal(want, LayoutMath.ClampOffset(boxMin, boxMax, min, max), 3);

        // Item cell zones (0.10.2): 64-unit inventory cell, share 0.18 -> band 11.52, icon 40.96 with equal margins;
        // the floor wins on small cells, the 30 % cap on silly shares.
        [Theory]
        [InlineData(64f, 0.18f, 9f, 11.52f)]
        [InlineData(40f, 0.18f, 9f, 9f)]
        [InlineData(64f, 0.6f, 9f, 19.2f)]
        [InlineData(64f, 0f, 0f, 0f)]
        public void ZoneBandIsAShareOfTheCell(float side, float share, float floor, float want) =>
            Assert.Equal(want, LayoutMath.ZoneBand(side, share, floor), 3);

        [Fact]
        public void ZoneIconKeepsTheBandOnEverySide()
        {
            var band = LayoutMath.ZoneBand(64f, 0.18f, 9f);
            var icon = LayoutMath.ZoneIconSide(64f, band);
            Assert.Equal(64f - 2f * band, icon, 3);
            // Top band [64 - band, 64] and the icon [band, 64 - band] share no row: a count inside the band never
            // overlaps the icon.
            Assert.False(LayoutMath.Overlaps(64f - band, 64f, band, band + icon));
            Assert.False(LayoutMath.Overlaps(0f, band, band, band + icon));
            Assert.True(LayoutMath.Overlaps(0f, band + 1f, band, band + icon));
        }

        // Amount glyphs 11 tall at size 15 in a 9.5 band -> 12.95; already small -> back up to the screen's size,
        // never below the floor.
        [Theory]
        [InlineData(15f, 15f, 11f, 9.52f, 8f, 12.98f)]
        [InlineData(12.98f, 15f, 9.52f, 9.52f, 8f, 12.98f)]
        [InlineData(10f, 15f, 5f, 9.52f, 8f, 15f)]
        [InlineData(15f, 15f, 40f, 9.52f, 8f, 8f)]
        [InlineData(6f, 6f, 20f, 9.52f, 8f, 6f)]
        public void FitFontSizeFitsTheBand(float current, float original, float height, float avail, float floor, float want) =>
            Assert.Equal(want, LayoutMath.FitFontSize(current, original, height, avail, floor), 2);

        [Theory]
        [InlineData(64f, 64f, true)]
        [InlineData(68f, 86f, true)]
        [InlineData(80f, 60f, true)]
        [InlineData(187f, 38f, false)]
        [InlineData(10f, 10f, false)]
        [InlineData(300f, 300f, false)]
        public void CellShapeIsSquareIsh(float w, float h, bool want) => Assert.Equal(want, LayoutMath.IsCellShape(w, h));
    }
}
