using RuneSkin.API;
using RuneSkin.WorldHud;
using Xunit;

namespace RuneSkin.Tests
{
    public class HoldPromptTests
    {
        [Theory]
        [InlineData(0.5f, 0.5f)]
        [InlineData(-1f, 0f)]
        [InlineData(2f, 1f)]
        [InlineData(float.NaN, 0f)]
        [InlineData(float.PositiveInfinity, 1f)]
        public void ProgressIsClampedToTheRing(float given, float drawn)
        {
            var s = HoldPromptMath.Sanitize(new HoldPromptState { Visible = true, Key = "G", Label = "Give up", Progress = given });
            Assert.Equal(drawn, s.Progress);
            Assert.True(s.Visible);
        }

        [Fact]
        public void PromptWithoutKeyAndLabelIsHidden()
        {
            Assert.False(HoldPromptMath.Sanitize(new HoldPromptState { Visible = true, Caption = "x" }).Visible);
            Assert.True(HoldPromptMath.Sanitize(new HoldPromptState { Visible = true, Key = "G" }).Visible);
        }

        [Fact]
        public void FillWritesOnlyVisibleSteps()
        {
            const float ring = 64f;
            Assert.False(HoldPromptMath.FillChanged(0.5f, 0.5f, ring));
            Assert.False(HoldPromptMath.FillChanged(0.5f, 0.501f, ring)); // 0.2 px of arc
            Assert.True(HoldPromptMath.FillChanged(0.5f, 0.51f, ring));   // 2 px of arc
            Assert.True(HoldPromptMath.FillChanged(0.999f, 1f, ring));    // full always lands
            Assert.True(HoldPromptMath.FillChanged(0.001f, 0f, ring));    // release resets at once
        }

        [Fact]
        public void RowIsCentredOnTheAnchor()
        {
            const float ring = 64f, gap = 10f, label = 120f;
            var ringX = HoldPromptMath.RingCentreX(ring, gap, label);
            var left = ringX - ring / 2f;
            var right = HoldPromptMath.LabelLeftX(ring, gap, label) + label;
            Assert.Equal(-left, right, 3);
            Assert.Equal(0f, HoldPromptMath.RingCentreX(ring, gap, 0f) + gap / 2f, 3);
        }
    }
}
