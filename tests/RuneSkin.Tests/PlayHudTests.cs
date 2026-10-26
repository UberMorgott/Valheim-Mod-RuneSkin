using System;
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
        public void CenterPlateWrapsTheGlyphBoxNotTheTextRect()
        {
            // Two wrapped lines of rune caps overshooting the line box: glyphs -310..290 x, -40..36 y (text space).
            var quads = new[] { -310f, -5f, -280f, 36f, 250f, -40f, 290f, -2f, 0f, 0f, 0f, 0f, float.NaN, 0f, 1f, 1f };
            Assert.True(PlayHudMath.GlyphBox(quads, 4, out var x0, out var y0, out var x1, out var y1));
            Assert.Equal((-310f, -40f, 290f, 36f), (x0, y0, x1, y1));
            var (cx, w) = PlayHudMath.CenterPlate(x0, x1, 54f, 16f);
            var (cy, h) = PlayHudMath.CenterPlate(y0, y1, 12f, 16f);
            Assert.Equal(-10f, cx, 3);
            Assert.Equal(600f + 2f * 70f, w, 3);
            Assert.Equal(-2f, cy, 3);
            Assert.Equal(76f + 2f * 28f, h, 3);
            // Every glyph edge stays pad inside the opaque core (plate minus the fade) on all four sides.
            Assert.Equal(54f, x0 - (cx - w * 0.5f + 16f), 3);
            Assert.Equal(54f, (cx + w * 0.5f - 16f) - x1, 3);
            Assert.Equal(12f, y0 - (cy - h * 0.5f + 16f), 3);
            Assert.Equal(12f, (cy + h * 0.5f - 16f) - y1, 3);
            Assert.False(PlayHudMath.GlyphBox(new float[] { 1f, 1f, 1f, 1f }, 1, out _, out _, out _, out _));
            Assert.False(PlayHudMath.GlyphBox(null, 3, out _, out _, out _, out _));
        }

        [Theory]
        [InlineData(12f, 10f, 1f, 12f)]      // 1080p: the theme pad already >= 10 px
        [InlineData(12f, 10f, 0.6667f, 15f)] // 720p: canvas shrinks, pad raised to 10 px on screen
        [InlineData(12f, 10f, 1.3333f, 12f)] // 1440p
        [InlineData(-3f, 10f, 0f, 0f)]
        public void PadUnitsKeepsTheScreenMinimum(float pad, float minPx, float scale, float expected) =>
            Assert.Equal(expected, PlayHudMath.PadUnits(pad, minPx, scale), 2);

        [Fact]
        public void EdgeFadeRampsSmoothlyToOpaque()
        {
            Assert.Equal(0f, PlayHudMath.EdgeFade(0f, 24f));
            Assert.Equal(0.5f, PlayHudMath.EdgeFade(12f, 24f), 4);
            Assert.Equal(1f, PlayHudMath.EdgeFade(24f, 24f));
            Assert.Equal(1f, PlayHudMath.EdgeFade(40f, 24f));
            Assert.Equal(1f, PlayHudMath.EdgeFade(1f, 0f));
            Assert.True(PlayHudMath.EdgeFade(6f, 24f) < PlayHudMath.EdgeFade(7f, 24f));
        }

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

        [Fact]
        public void UnlockRowCentresIconAndTextInTheContentSpan()
        {
            // 0.23.0 live report: plate 0..400, border 9 + pad 10 -> content 19..381; icon socket 64 at the plate's left
            // edge (0..64), gap 10, "Обжаренные соты" 150 wide -> group 224 centred at 200: 88..312.
            var (shift, tMin, tMax) = PlayHudMath.UnlockRow(19f, 381f, 0f, 64f, 10f, 150f);
            Assert.Equal(88f, shift, 3);
            Assert.Equal(162f, tMin, 3);
            Assert.Equal(312f, tMax, 3);
            Assert.Equal(200f, (0f + shift + tMax) * 0.5f, 3);
            // A long name: cut to what is left, the group fills the content span from its start.
            var (s2, a2, b2) = PlayHudMath.UnlockRow(19f, 381f, 0f, 64f, 10f, 1000f);
            Assert.Equal(19f, s2, 3);
            Assert.Equal(93f, a2, 3);
            Assert.Equal(381f, b2, 3);
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

        [Fact]
        public void LadderTokensFollowLadderIndex()
        {
            Assert.Equal(PlayHudMath.LadderCells, PlayHudMath.LadderTokens.Length);
            Assert.Equal("$radial_speed_fast", PlayHudMath.LadderTokens[PlayHudMath.LadderIndex(4)]);   // Full
            Assert.Equal("$radial_speed_medium", PlayHudMath.LadderTokens[PlayHudMath.LadderIndex(3)]); // Half
            Assert.Equal("$radial_speed_slow", PlayHudMath.LadderTokens[PlayHudMath.LadderIndex(2)]);   // Slow
            Assert.Equal("$radial_speed_off", PlayHudMath.LadderTokens[PlayHudMath.LadderIndex(0)]);    // Stop
            Assert.Equal("$settings_backward", PlayHudMath.LadderTokens[PlayHudMath.LadderIndex(1)]);   // Back
        }

        // Rudder side arrows: right (+1) lights for a positive rudder value, left (-1) for a negative one; dead zone 0.02.
        [Theory]
        [InlineData(0f, 1, 0.3f)]
        [InlineData(0.01f, 1, 0.3f)]   // inside the dead zone
        [InlineData(0.5f, -1, 0.3f)]   // the other side stays idle
        [InlineData(-0.5f, 1, 0.3f)]
        [InlineData(1f, 1, 1f)]
        [InlineData(-1f, -1, 1f)]
        [InlineData(5f, 1, 1f)]
        [InlineData(float.NaN, 1, 0.3f)]
        public void RudderSideAlphaFollowsTheTurnedSide(float value, int side, float alpha) =>
            Assert.Equal(alpha, PlayHudMath.RudderSideAlpha(value, side, 0.3f, 0.6f), 3);

        [Fact]
        public void RudderSideLightsAtOnceAndGrows()
        {
            var justPast = PlayHudMath.RudderSideAlpha(0.03f, 1, 0.3f, 0.6f);
            Assert.True(justPast >= 0.6f && justPast < 0.62f, $"{justPast}");
            Assert.True(PlayHudMath.RudderSideAlpha(0.5f, 1, 0.3f, 0.6f) > justPast);
            Assert.True(PlayHudMath.RudderSideAlpha(-0.5f, -1, 0.3f, 0.6f) > justPast);
        }

        [Fact]
        public void ShipBlockFitsLadderAndRudderRow()
        {
            var (w, h) = PlayHudMath.ShipBlock(46f, 6f, 12f, 90f, 8f, 64f, 28f);
            Assert.Equal(12f + 90f + 8f + 46f + 12f, w, 3);
            Assert.Equal(PlayHudMath.LadderHeight(46f, 6f, 12f) + 6f + 64f, h, 3);
            var (w2, _) = PlayHudMath.ShipBlock(46f, 6f, 12f, 0f, 8f, 120f, 40f);
            Assert.Equal(120f + 2f * 46f, w2, 3); // rudder row wider than a label-less ladder
        }

        // The ship HUD never overlaps the RuneSkin minimap footprint (MinimapSkin.Layout: ring = min(320, 0.26 * safe
        // height / scale) units at the HUD-box top-right corner minus the 24-unit margin, biome plaque 32 * box / 220
        // units starting 0.02 box above the ring bottom, key tip line under it), stays above the strip and on screen at
        // 16:9, 21:9, 32:9 and 16:10 (canvas scale = ResponsiveContext.GuiFactor).
        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        [InlineData(1280, 720)]
        [InlineData(3840, 2160)]
        [InlineData(2560, 1080)]
        [InlineData(3440, 1440)]
        [InlineData(5120, 1440)]
        [InlineData(1920, 1200)]
        [InlineData(1680, 1050)]
        [InlineData(2560, 1600)]
        public void ShipHudClearsTheMinimap(int sw, int sh)
        {
            foreach (var minimapScale in new[] { 1f, 1.5f })
                foreach (var stripShare in new[] { 0.2f, 0.32f })
                {
                    var scale = Math.Min(sw / 1920f, sh / 1080f); // ResponsiveContext.GuiFactor at GUI scale 1
                    var box = Math.Min(320f * minimapScale, 0.26f * minimapScale * sh / scale);
                    var boxW = (float)sw / sh >= 2.2f ? sh * 16f / 9f : sw;
                    var boxX = (sw - boxW) / 2f;
                    var margin = 24f * scale;
                    float fpRight = boxX + boxW - margin, fpTop = sh - margin;
                    var plaqueTop = fpTop - box * scale + 0.02f * box * scale;
                    var fpBottom = plaqueTop - (32f * box / 220f + 2f + 24f) * scale;
                    var fpLeft = fpRight - box * scale;
                    var floor = sh * stripShare + 12f * scale;
                    var (bw, bh) = PlayHudMath.ShipBlock(46f, 6f, 12f, 110f, 8f, 64f, 28f);
                    bw *= scale;
                    bh *= scale;
                    var (right, top, beside) = PlayHudMath.ShipPlacement(fpLeft, fpRight, fpBottom, fpTop, 0f, floor, bw, bh, 12f * scale);
                    float left = right - bw, bottom = top - bh;
                    var overlaps = left < fpRight && right > fpLeft && bottom < fpTop && top > fpBottom;
                    Assert.False(overlaps, $"{sw}x{sh} map x{minimapScale} strip {stripShare}: block [{left:0},{bottom:0}]-[{right:0},{top:0}] footprint [{fpLeft:0},{fpBottom:0}]-[{fpRight:0},{fpTop:0}] beside={beside}");
                    Assert.True(bottom >= floor - 0.01f, $"{sw}x{sh}: bottom {bottom} under floor {floor}");
                    Assert.True(left >= 0f && right <= sw && top <= sh, $"{sw}x{sh}: off screen");
                    if (minimapScale == 1f && stripShare == 0.2f) Assert.False(beside, $"{sw}x{sh}: default layout should fit under the minimap");
                }
        }

        [Fact]
        public void ShipPlacementFallsBesideWhenTheStripIsTooHigh()
        {
            // footprint 1600..1880 x 700..1056, strip floor at 500: 400-tall block cannot fit under it.
            var (right, top, beside) = PlayHudMath.ShipPlacement(1600f, 1880f, 700f, 1056f, 0f, 500f, 170f, 400f, 12f);
            Assert.True(beside);
            Assert.Equal(1588f, right, 3);
            Assert.Equal(1056f, top, 3);
            var under = PlayHudMath.ShipPlacement(1600f, 1880f, 700f, 1056f, 0f, 200f, 170f, 400f, 12f);
            Assert.False(under.beside);
            Assert.Equal(1880f, under.right, 3);
            Assert.Equal(688f, under.top, 3);
        }

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
