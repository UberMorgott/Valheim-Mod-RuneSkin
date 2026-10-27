using RuneSkin.ModScreens;
using Xunit;

namespace RuneSkin.Tests
{
    // Epic Loot modal dialogs (ModScreens\ElDialogs, user 2026-10-27: augment choices truncated, icon over the text).
    // Frame units (the dialog frame is canvas-scaled: 380 x 550 at 720p and 1440p alike; only the GUI scale differs).
    public class ElDialogTests
    {
        private const float FrameH = 550f, ScrollTop = 74f, Gap = ElDialogMath.ElGap;

        // Longest real label of run 20261027-165655: "(сохранить) Урон по оглушенному врагу +7,5% [3,5-10,5]" (54 chars).
        // Wrapped at the readable floor 12 in the 360 column minus the plate insets (~326 text units): two lines,
        // ~2 x 14.4 = 28.8 high -> fits Epic Loot's 40 with 3 above and below.
        [Theory]
        [InlineData(28.8f, 40f)]  // two lines at the floor: Epic Loot's height
        [InlineData(43.2f, 49.2f)] // three lines (a longer legendary text): the button grows, nothing clipped
        [InlineData(0f, 40f)]
        public void ButtonGrowsOnlyWhenTheFloorNeedsIt(float need, float want) =>
            Assert.Equal(want, ElDialogMath.ButtonHeight(ElDialogMath.ElHeight, need, 3f), 3);

        // Bottom corner caps of the shipped theme reach ~58 frame units at 720p (GUI scale 0.83) and ~58 at 1440p
        // (caps are canvas-scaled with the frame); 3 choices (Augment level 2) and 5 (max level).
        [Theory]
        [InlineData(58f, 3)]
        [InlineData(58f, 5)]
        [InlineData(0f, 3)]   // no panel stack: Epic Loot's own base 35
        public void StackClearsTheCapsAndTheDescription(float capBand, int count)
        {
            var heights = new float[count];
            for (var i = 0; i < count; i++) heights[i] = 40f;
            var frame = FrameH + (count > 3 ? (count - 3) * 45f : 0f);
            var s = ElDialogMath.Stack(frame, ScrollTop, heights, capBand, Gap, 80f);
            var bottom = s.Bottoms[count - 1];
            Assert.Equal(System.Math.Max(ElDialogMath.ElBase, capBand + Gap), bottom, 3);
            for (var i = 0; i < count - 1; i++)
                Assert.Equal(s.Bottoms[i + 1] + heights[i + 1] + Gap, s.Bottoms[i], 3); // top button first, no overlap
            Assert.Equal(s.Bottoms[0] + heights[0], s.Top, 3);
            // The description ends a gap above the stack and keeps a usable height.
            Assert.True(frame - ScrollTop - s.ScrollHeight >= s.Top + Gap - 0.001f);
            Assert.True(s.ScrollHeight >= 80f);
        }

        [Fact]
        public void GrownButtonsPushTheStackUp()
        {
            var s = ElDialogMath.Stack(FrameH, ScrollTop, new[] { 49.2f, 40f, 40f }, 58f, Gap, 80f);
            Assert.Equal(63f, s.Bottoms[2], 3);
            Assert.Equal(108f, s.Bottoms[1], 3);
            Assert.Equal(153f, s.Bottoms[0], 3);
            Assert.Equal(202.2f, s.Top, 3);
            Assert.Equal(FrameH - ScrollTop - 202.2f - Gap, s.ScrollHeight, 3);
        }

        [Theory]
        [InlineData(true, false, true)]   // cloned mark, no registered root: stale
        [InlineData(true, true, false)]   // the registered root itself
        [InlineData(false, false, false)] // no mark
        public void ClonedRootMarkIsStale(bool hasMark, bool registered, bool stale) =>
            Assert.Equal(stale, ElDialogMath.IsStaleRootMark(hasMark, registered));
    }
}
