using RuneSkin.WorldHud;
using Xunit;

namespace RuneSkin.Tests
{
    public class PlayHudTests
    {
        // Ship.Speed Stop 0, Back 1, Slow 2, Half 3, Full 4 -> ladder top-down full, half, oar, stop, reverse.
        [Theory]
        [InlineData(4, 0)]
        [InlineData(3, 1)]
        [InlineData(2, 2)]
        [InlineData(0, 3)]
        [InlineData(1, 4)]
        [InlineData(99, 3)]
        public void LadderIndexMapsEverySpeed(int speed, int index) => Assert.Equal(index, PlayHudMath.LadderIndex(speed));

        [Theory]
        [InlineData(16f, 18f, 18f)]
        [InlineData(26f, 18f, 26f)]
        [InlineData(12f, -1f, 12f)]
        public void ReadableRaisesSmallSizesToTheFloor(float size, float min, float expected) => Assert.Equal(expected, PlayHudMath.Readable(size, min));

        [Fact]
        public void PlateSpanKeepsBorderAndPadOnBothSides()
        {
            var (c, s) = PlayHudMath.PlateSpan(10f, 110f, 12f, 6f);
            Assert.Equal(60f, c, 3);
            Assert.Equal(136f, s, 3);
            // Content edge (an icon at min) stays border + pad inside the plate edge.
            Assert.Equal(18f, 10f - (c - s * 0.5f), 3);
            var (c2, s2) = PlayHudMath.PlateSpan(110f, 10f, -5f, 0f);
            Assert.Equal(60f, c2, 3);
            Assert.Equal(100f, s2, 3);
        }

        [Fact]
        public void UnlockTitleSitsBelowTheTopRailAndThePlateGrows()
        {
            // Run 20261023-072719 geometry: plate 108.7 high (top 0), frame_small border 9 top / bottom, vanilla title
            // line box centred on the top edge (+14 .. -14), row = 64 icon socket at -28 .. -92.
            var (ts, rs, bottom) = PlayHudMath.UnlockStack(0f, 14f, -14f, -28f, -92f, 9f, 9f, 4f, 2f);
            Assert.Equal(-27f, ts, 3);              // title top lands at -13 = rail (9) + pad (4) below the plate top
            Assert.True(14f + ts <= -9f);           // never on the rail
            Assert.Equal(-15f, rs, 3);              // row top at title bottom (-41) - gap (2) = -43
            Assert.Equal(-120f, bottom, 3);         // row bottom -107 - pad 4 - border 9
            Assert.True(-bottom > 108.7f);          // plate grew to fit title + row
            // Already inside: the title still lands exactly at the inset (deterministic, no drift).
            var (ts2, _, _) = PlayHudMath.UnlockStack(0f, -30f, -50f, -60f, -100f, 9f, 9f, 4f, 2f);
            Assert.Equal(17f, ts2, 3);
            // Negative inputs clamp to zero.
            var (ts3, rs3, b3) = PlayHudMath.UnlockStack(0f, 0f, -10f, -10f, -20f, -5f, -5f, -1f, -1f);
            Assert.Equal(0f, ts3, 3);
            Assert.Equal(0f, rs3, 3);
            Assert.Equal(-20f, b3, 3);
        }

        [Theory]
        [InlineData(0, 120f, 0f)]
        [InlineData(1, 100f, -110f)]  // fits the vanilla 110 spacing
        [InlineData(2, 120f, -252f)]  // grown plate: 120 + 6
        [InlineData(-1, 120f, 0f)]
        public void UnlockSlotsNeverOverlap(int slot, float plate, float y) =>
            Assert.Equal(y, PlayHudMath.UnlockSlotY(0f, slot, 110f, plate, 6f), 3);

        [Fact]
        public void LadderCellsStackInsideTheColumn()
        {
            const float cell = 44f, gap = 6f, pad = 10f;
            var h = PlayHudMath.LadderHeight(cell, gap, pad);
            Assert.Equal(10f, PlayHudMath.LadderCellTop(0, cell, gap, pad));
            Assert.Equal(h - pad - cell, PlayHudMath.LadderCellTop(PlayHudMath.LadderCells - 1, cell, gap, pad), 3);
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(1f, -60f)]
        [InlineData(-1f, 60f)]
        [InlineData(3f, -60f)]
        [InlineData(float.NaN, 0f)]
        public void RudderAngleClamps(float value, float deg) => Assert.Equal(deg, PlayHudMath.RudderAngle(value, 60f), 3);

        // Growing a rect by insets keeps it centred on the target for any pivot.
        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void GrowPositionKeepsCentre(float pivot)
        {
            const float pos = 100f, size = 200f, inset = 20f;
            var centre = pos + (0.5f - pivot) * size;
            var grown = PlayHudMath.GrowPosition(pos, pivot, inset, inset);
            Assert.Equal(centre, grown + (0.5f - pivot) * (size + 2f * inset), 3);
            // Unequal insets: the grown rect's min edge moves out by insetMin.
            var g2 = PlayHudMath.GrowPosition(pos, pivot, 10f, 30f);
            Assert.Equal(pos - pivot * size - 10f, g2 - pivot * (size + 40f), 3);
        }

        [Fact]
        public void RuleOffsetClearsTitle() => Assert.Equal(118f, PlayHudMath.RuleOffset(200f, 18f));

        [Theory]
        [InlineData(10f, 30f, 6f, 30f)]
        [InlineData(60f, 30f, 6f, 72f)]
        public void KeyPlateWidthFitsLabel(float label, float h, float pad, float want) => Assert.Equal(want, PlayHudMath.KeyPlateWidth(label, h, pad));
    }
}
