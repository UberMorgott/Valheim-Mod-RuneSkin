using RuneSkin.ModScreens;
using Xunit;

namespace RuneSkin.Tests
{
    // Epic Loot modal dialogs (ModScreens\ElDialogs, user 2026-10-27: augment choices truncated, icon over the text).
    public class ElDialogTests
    {
        // Epic Loot: 300 wide choice buttons in a 380 frame (AugmentHelper.cs:88 / CreateDialog 380).
        [Theory]
        [InlineData(300f, 380f, 0f, 380f)]   // no panel stack keep-out: the whole frame
        [InlineData(300f, 380f, 12f, 356f)]  // corner caps 12 in from each side
        [InlineData(300f, 380f, 60f, 300f)]  // caps leave less than Epic Loot's width: its width stays
        [InlineData(300f, 380f, -5f, 380f)]  // a negative keep-out never widens past the frame
        public void ChoiceButtonsTakeTheInnerWidth(float current, float frame, float keepOut, float want) =>
            Assert.Equal(want, ElDialogMath.ChoiceWidth(current, frame, keepOut), 3);

        [Theory]
        [InlineData(true, false, true)]   // cloned mark, no registered root: stale
        [InlineData(true, true, false)]   // the registered root itself
        [InlineData(false, false, false)] // no mark
        public void ClonedRootMarkIsStale(bool hasMark, bool registered, bool stale) =>
            Assert.Equal(stale, ElDialogMath.IsStaleRootMark(hasMark, registered));
    }
}
