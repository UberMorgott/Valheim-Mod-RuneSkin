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
