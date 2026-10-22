using System;
using RuneSkin.MapScreen;
using RuneSkin.WorldHud;
using Xunit;

namespace RuneSkin.Tests
{
    // M4: round minimap geometry, enemy frame fit, Vaettir tier mapping.
    public class WorldHudTests
    {
        [Fact]
        public void RingKeepsThemeSizeOnTallScreens()
        {
            Assert.Equal(220f, MinimapMath.RingSize(220f, 1440f, 1f, 0.18f), 3);
        }

        [Fact]
        public void RingClampedToShareOfSafeHeight()
        {
            // 600 px tall at 1 px / ref: 18 % = 108 ref.
            Assert.Equal(108f, MinimapMath.RingSize(220f, 600f, 1f, 0.18f), 3);
        }

        [Theory]
        [InlineData(0.25f, true, -1f, 0f)]  // sunrise: left end
        [InlineData(0.5f, true, 0f, 1f)]    // noon: top
        [InlineData(0.75f, true, 1f, 0f)]   // sunset: right end
        [InlineData(0.75f, false, -1f, 0f)] // dusk: moon rises left
        [InlineData(0.0f, false, 0f, 1f)]   // midnight: top
        [InlineData(0.25f, false, 1f, 0f)]  // dawn: right end
        public void DayPhaseRidesTheTopArc(float fraction, bool day, float ex, float ey)
        {
            MinimapMath.ArcPoint(fraction, day, 1f, out var x, out var y);
            Assert.Equal(ex, x, 3);
            Assert.Equal(ey, y, 3);
            Assert.True(y >= -1e-4f, "never below the ring centre");
        }

        [Fact]
        public void ArcClampsOutsideItsPhase()
        {
            MinimapMath.ArcPoint(0.1f, true, 10f, out var x, out var y); // "day" asked before sunrise
            Assert.Equal(-10f, x, 3);
            Assert.Equal(0f, y, 3);
        }

        [Fact]
        public void CardinalsAroundTheBand()
        {
            MinimapMath.Cardinal(0, 5f, out var nx, out var ny);
            MinimapMath.Cardinal(1, 5f, out var ex, out var ey);
            MinimapMath.Cardinal(2, 5f, out var sx, out var sy);
            MinimapMath.Cardinal(3, 5f, out var wx, out var wy);
            Assert.Equal((0f, 5f), (nx, ny));
            Assert.Equal((5f, 0f), (ex, ey));
            Assert.Equal((0f, -5f), (sx, sy));
            Assert.Equal((-5f, 0f), (wx, wy));
        }

        [Fact]
        public void PinsOutsideTheHoleAreCulled()
        {
            Assert.True(MinimapMath.InsideDisc(0f, 80f, 85f, 8f));
            Assert.False(MinimapMath.InsideDisc(70f, 70f, 85f, 8f)); // square corner, outside the disc
        }

        [Fact]
        public void MapFillsHoleOnlyWhenMasked()
        {
            Assert.Equal(220f * 0.777f, MinimapMath.MapSide(220f, 0.777f, true), 3);
            Assert.Equal(220f * 0.777f / (float)Math.Sqrt(2.0), MinimapMath.MapSide(220f, 0.777f, false), 3);
        }

        [Fact]
        public void EnemyFrameWindowMatchesTheBar()
        {
            // frame_enemy_normal: 44 px tall, borders L/R 36 T/B 14, PPU 200 -> window 8 ref.
            var fit = EnemyHudMath.Fit(12f, 44f, 36f, 36f, 14f, 14f, 200f);
            Assert.Equal(1.5f, fit.Scale, 3);
            Assert.Equal(10.5f, fit.Top, 3);
            Assert.Equal(27f, fit.Left, 3);
            Assert.Equal(44f * 0.5f * 1.5f, 12f + fit.Top + fit.Bottom, 3); // whole frame height = sprite at that scale
            Assert.Equal(1f / 1.5f, fit.PpuMultiplier, 3);
        }

        [Theory]
        [InlineData("vt_frame_normal", false, "sprite.frame.enemy.normal")]
        [InlineData("vt_frame_magic", false, "sprite.frame.enemy.magic")]
        [InlineData("vt_frame_rare", false, "sprite.frame.enemy.rare")]
        [InlineData("vt_frame_unique", false, "sprite.frame.enemy.unique")]
        [InlineData("vt_frame_rare", true, "sprite.frame.boss")]
        [InlineData(null, false, "sprite.frame.enemy.normal")]
        public void VaettirTierMapsToRuneSkinArt(string vt, bool boss, string token)
        {
            Assert.Equal(token, EnemyHudMath.TierToken(vt, boss));
        }
    }
}
