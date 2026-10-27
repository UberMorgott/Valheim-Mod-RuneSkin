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

        // Choice width: the 380 frame between its drawn side borders + row pad, never under Epic Loot's 300 (runs
        // 20261027-165655: 300 truncated; 20261027-170958: the description-view width came out narrower).
        [Theory]
        [InlineData(380f, 14f, 14f, 352f)] // rune frame border 8 + pad 6 each side
        [InlineData(380f, 10f, 10f, 360f)] // no panel stack: Epic Loot's own 10 inset
        [InlineData(380f, 60f, 60f, 300f)] // a thick border never makes them narrower than before
        [InlineData(380f, -4f, 0f, 380f)]
        public void ChoicesTakeTheFrameInnerWidth(float frame, float left, float right, float want)
        {
            var w = ElDialogMath.InnerWidth(frame, left, right);
            Assert.Equal(want, w, 3);
            Assert.True(w >= ElDialogMath.ElWidth);
        }

        // The longest real label wraps into the wider button at the floor and fits without clipping: needed text height
        // (two lines at 12, ~28.8) plus the padding stays inside the button it gets.
        [Theory]
        [InlineData(352f, 28.8f)]
        [InlineData(300f, 43.2f)] // narrower fallback: three lines, the button grows
        public void LongestLabelFits(float width, float need)
        {
            Assert.True(ElDialogMath.InnerWidth(380f, (380f - width) / 2f, (380f - width) / 2f) >= ElDialogMath.ElWidth);
            var h = ElDialogMath.ButtonHeight(ElDialogMath.ElHeight, need, 3f);
            Assert.True(h - 2f * 3f >= need - 0.001f);
        }

        // The frame widens to the widest label (one line at its size) + the drawn side borders; Epic Loot's 380 is the
        // floor, MaxFrameWidth / the safe area the ceiling (1280x720 at GUI scale 0.667: 1920 units, minus margins).
        [Theory]
        [InlineData(470f, 14f, 14f, 1872f, 498f)]  // longest augment label at its size: frame grows to fit it
        [InlineData(300f, 14f, 14f, 1872f, 380f)]  // short labels keep Epic Loot's frame
        [InlineData(900f, 14f, 14f, 1872f, 760f)]  // sentence-long line: readable cap, it wraps
        [InlineData(470f, 14f, 14f, 420f, 420f)]   // narrow screen: the safe area caps it
        [InlineData(470f, 14f, 14f, 200f, 380f)]   // narrower than Epic Loot's own frame: kept, labels wrap
        public void FrameFitsTheWidestLabel(float content, float left, float right, float room, float want)
        {
            var w = ElDialogMath.FrameWidth(content, left, right, room);
            Assert.Equal(want, w, 3);
            Assert.True(w >= ElDialogMath.ElFrameWidth);
            // A grown, uncapped frame gives the choices exactly the label's width.
            if (content + left + right <= System.Math.Min(room, ElDialogMath.MaxFrameWidth) && w > ElDialogMath.ElFrameWidth)
                Assert.Equal(content, ElDialogMath.InnerWidth(w, left, right), 3);
        }

        [Theory]
        [InlineData(-249f, 249f, -936f, 936f, 0f)]      // centred, fits
        [InlineData(-1000f, -500f, -936f, 936f, 64f)]   // past the left edge: moved right
        [InlineData(700f, 1000f, -936f, 936f, -64f)]    // past the right edge: moved left
        [InlineData(-1000f, 1000f, -936f, 936f, 0f)]    // wider than the room: centred on it
        [InlineData(0f, 2000f, -936f, 936f, -1000f)]
        public void FrameStaysInsideTheSafeArea(float xMin, float xMax, float lo, float hi, float want) =>
            Assert.Equal(want, ElDialogMath.ShiftInto(xMin, xMax, lo, hi), 3);

        [Theory]
        [InlineData(330f, 446f, true)]  // fits one line at the floor
        [InlineData(446.4f, 446f, true)] // sub-pixel rounding
        [InlineData(500f, 446f, false)] // wider than the capped frame: wraps
        public void OneLineWhenTheFloorFits(float floorWidth, float textWidth, bool one) =>
            Assert.Equal(one, ElDialogMath.OneLine(floorWidth, textWidth));

        // Run 20261027-183350 (0.25.15): labels auto-sized from Jotunn's fontSizeMax ("widest label 1694.6 / 2412.1 /
        // 2089.2" units) - the frame always hit 760 and '(сохранить) Магический клинок +8,5 [5-12,5]' drew at 24.7 on one
        // line in a 748 button (text width ~716 after the plate insets), bigger than the description's 18. At the body
        // size 18 that label is at most 716 x 18 / 24.7 = 521.8 wide: it keeps 18 on one line, and the frame sizes to it.
        private const float RunTextW = 716f, RunSize = 24.7f, RunInsets = 32f;

        [Fact]
        public void RealLabelKeepsTheBodySizeAndSizesTheFrame()
        {
            var at18 = RunTextW * ElDialogMath.ElBodySize / RunSize;
            Assert.Equal(ElDialogMath.ElBodySize, ElDialogMath.LabelSize(ElDialogMath.ElBodySize, 12f, at18, at18, out var wrap), 3);
            Assert.False(wrap);
            var frame = ElDialogMath.FrameWidth(at18 + RunInsets, 6f, 6f, 1872f);
            Assert.InRange(frame, ElDialogMath.ElFrameWidth, ElDialogMath.MaxFrameWidth - 1f); // no longer pinned at the cap
            Assert.Equal(565.8f, frame, 1);
            // The label is never drawn above the body size, whatever its own fontSizeMax was.
            Assert.True(ElDialogMath.LabelSize(ElDialogMath.ElBodySize, 12f, at18 * 0.5f, RunTextW, out _) <= ElDialogMath.ElBodySize);
        }

        [Theory]
        [InlineData(500f, 716f, 18f, false)]   // fits at the body size
        [InlineData(900f, 716f, 14.3f, false)] // capped frame: shrunk on one line (18 x 716 / 900 = 14.32)
        [InlineData(1400f, 716f, 12f, true)]   // would need 9.2: the floor, wrapped
        [InlineData(0f, 716f, 18f, false)]     // empty
        public void LabelShrinksOnOneLineThenWrapsAtTheFloor(float widthAt18, float textW, float size, bool wrap)
        {
            Assert.Equal(size, ElDialogMath.LabelSize(18f, 12f, widthAt18, textW, out var w), 3);
            Assert.Equal(wrap, w);
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
