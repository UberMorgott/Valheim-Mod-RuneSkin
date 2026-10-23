using RuneSkin.API;
using RuneSkin.GameScreens;
using Xunit;

namespace RuneSkin.Tests
{
    public class GameScreenTests
    {
        // Tip banner: the tip box plus both pads, never narrower than the banner minimum, never wider than the cap.
        [Theory]
        [InlineData(300f, 20f, 520f, 1400f, 520f)]
        [InlineData(800f, 20f, 520f, 1400f, 840f)]
        [InlineData(2000f, 20f, 520f, 1400f, 1400f)]
        [InlineData(-5f, 20f, 0f, 0f, 40f)]
        public void BannerWidthClamps(float tip, float pad, float min, float max, float want) =>
            Assert.Equal(want, GameScreenMath.BannerWidth(tip, pad, min, max), 3);

        [Fact]
        public void BannerHeightStacksTitleAndPads() => Assert.Equal(160f, GameScreenMath.BannerHeight(100f, 24f, 18f), 3);

        // Bands sit clear of the text: their inner edge is exactly gap away from the text box.
        [Fact]
        public void BandOffsetClearsText()
        {
            const float text = 300f, band = 22f, gap = 10f;
            var off = GameScreenMath.BandOffset(text, band, gap);
            Assert.Equal(text * 0.5f + gap, off - band * 0.5f, 3);
        }

        [Theory]
        [InlineData(0f, 40f, 0f)]
        [InlineData(1f, 40f, -40f)]
        [InlineData(10f, 40f, -40f)]
        [InlineData(float.NaN, 40f, 0f)]
        public void SpinAngleTurnsClockwiseAndWraps(float t, float speed, float want) =>
            Assert.Equal(want, GameScreenMath.SpinAngle(t, speed), 3);

        [Fact]
        public void SpinAngleStaysInOneTurn()
        {
            for (var t = 0f; t < 100f; t += 0.37f)
            {
                var a = GameScreenMath.SpinAngle(t, 47f);
                Assert.InRange(a, -360f, 0f);
            }
        }

        [Theory]
        [InlineData(112f, 0.42f, 47.04f)]
        [InlineData(112f, 1.5f, 112f)]
        [InlineData(-1f, 0.5f, 0f)]
        public void GlyphSideIsAShareOfTheSocket(float socket, float share, float want) =>
            Assert.Equal(want, GameScreenMath.GlyphSide(socket, share), 3);

        // M6 batch 2 families are stable API names (other mods ask for them by string).
        [Fact]
        public void BatchTwoFamilyNames()
        {
            Assert.Equal("LoadScreen", SkinFamilies.LoadScreen);
            Assert.Equal("Chat", SkinFamilies.Chat);
            Assert.Equal("Popups", SkinFamilies.Popups);
            Assert.Equal("TextDialogs", SkinFamilies.TextDialogs);
            Assert.Equal("Compendium", SkinFamilies.Compendium);
            Assert.Equal("SplitDialog", SkinFamilies.SplitDialog);
        }

        // Active effects rows: blank-line runs of SE tooltips collapse to one break, ends trimmed.
        [Theory]
        [InlineData("Rested\n\n$se_healthregen: +50%\n \n\nDuration", "Rested\n$se_healthregen: +50%\nDuration")]
        [InlineData("  one line  ", "one line")]
        [InlineData("a\nb", "a\nb")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void TidyCollapsesBlankLines(string text, string want) => Assert.Equal(want, GameScreenMath.Tidy(text));

        // Panel stacks kept on screen: the overhang past an edge comes back; a box that fits stays; an oversize box
        // keeps its low edge (connect panel: stack 40 px past the top-left corner of a 1920 canvas).
        [Theory]
        [InlineData(-40f, 556f, 0f, 1920f, 40f)]
        [InlineData(10f, 600f, 0f, 1920f, 0f)]
        [InlineData(1500f, 1960f, 0f, 1920f, -40f)]
        [InlineData(-10f, 2000f, 0f, 1920f, 10f)]
        [InlineData(100f, 2100f, 0f, 1920f, -100f)]
        [InlineData(float.NaN, 10f, 0f, 1920f, 0f)]
        public void EdgeShiftBringsTheStackInside(float min, float max, float lo, float hi, float want) =>
            Assert.Equal(want, RuneSkin.Restyle.PanelMath.EdgeShift(min, max, lo, hi), 3);

        [Theory]
        // room on the right: panel left = window right + gap, tops level
        [InlineData(100f, 100f, 700f, 900f, 0f, 0f, 400f, 500f, 724f, 400f)]
        // no room right (window reaches 1700): goes left of the window
        [InlineData(1100f, 100f, 1700f, 900f, 0f, 0f, 400f, 500f, 676f, 400f)]
        // window top above the screen: clamped down inside
        [InlineData(100f, 100f, 700f, 1300f, 0f, 0f, 400f, 500f, 724f, 580f)]
        public void DockBesidePlacesThePanelNextToTheWindow(float wx0, float wy0, float wx1, float wy1, float px0, float py0, float px1, float py1, float wantDx, float wantDy)
        {
            var (dx, dy) = RuneSkin.Restyle.PanelMath.DockBeside(wx0, wy0, wx1, wy1, px0, py0, px1, py1, 0f, 0f, 1920f, 1080f, 24f);
            Assert.Equal(wantDx, dx, 3);
            Assert.Equal(wantDy, dy, 3);
        }
    }
}
