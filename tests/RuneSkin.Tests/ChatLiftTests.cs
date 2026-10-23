using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // StripMath.ChatLift (HudCluster.ChatTick): the chat's drawn bottom at its home, in the window parent's units, against
    // the strip top + gap. Values from run 20261023-200109 (IngameGui scale 1, 1 unit = 1 px).
    public class ChatLiftTests
    {
        [Fact]
        public void LiftsTheDrawnBottomToTheStripTop()
        {
            // 1080: drawn chat bottom 66 px, strip top 192 + gap 12 -> lifted by 138 (not by the canvas-wide rect)
            Assert.Equal(138f, StripMath.ChatLift(66f, 204f), 3);
        }

        [Fact]
        public void AlreadyAboveIsNeverLowered()
        {
            Assert.Equal(0f, StripMath.ChatLift(246f, 151f), 3);
        }

        [Fact]
        public void EachResolutionUsesItsOwnHome()
        {
            // 720: drawn bottom 44, strip top 128 + gap 8 -> 92; back at 1080 the home is re-taken (66) -> 138 again,
            // never the 720 home with the 1080 strip top
            Assert.Equal(92f, StripMath.ChatLift(44f, 136f), 3);
            Assert.Equal(138f, StripMath.ChatLift(66f, 204f), 3);
        }
    }
}
