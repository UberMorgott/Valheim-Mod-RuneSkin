using System;
using System.Linq;
using RuneSkin.PlayerHud;
using Xunit;
using Xunit.Abstractions;

namespace RuneSkin.Tests
{
    // Bottom strip size (user 2026-10-27): the strip is scale.hud.strip (0.8) of the slot design, Hud.Scale multiplies
    // the whole strip (cells, frames, gaps), gaps / margins follow the GUI factor without the font floor, and the cell
    // texts keep their readable floors and still fit the smaller cells.
    public class StripScaleTests
    {
        private readonly ITestOutputHelper _out;

        public StripScaleTests(ITestOutputHelper output) => _out = output;

        private static float F(string token) => StripLayoutTests.Floats[token];

        private struct Strip
        {
            public float Gui, UnitPx, CellPx, HeightPx, Share, GapPx;
        }

        // HudCluster.Relayout for a w x h screen at GUI scale `gui`, the strip design scale and Hud.Scale.
        private static Strip At(int w, int h, float gui, float design, float hud)
        {
            var g = Math.Min(w / 1920f, h / 1080f) * gui;
            var cell = StripMath.StripSlotPx(StripMath.SlotPx(F("size.slot"), 1f, g), design, hud);
            var unit = StripMath.UnitPx(cell, F("size.hud.strip.cell"));
            HudMath.HudBox(0f, w, h, true, F("ratio.hud.ultrawide"), out _, out var boxW);
            var l = StripMath.Compute(StripLayoutTests.In(width: StripMath.WidthPx(boxW, 1f) / unit));
            return new Strip
            {
                Gui = g,
                UnitPx = unit,
                CellPx = F("size.hud.strip.cell") * unit,
                HeightPx = l.Height * unit,
                Share = l.Height * unit / h,
                GapPx = 12f * HudMath.FrameScale(g, hud) // size.hud.strip.clear.gap / chat.gap default
            };
        }

        public static TheoryData<int, int, float, float> Grid()
        {
            var d = new TheoryData<int, int, float, float>();
            var cases = from res in new[] { (w: 1280, h: 720), (w: 1920, h: 1080), (w: 2560, h: 1440) }
                        from gui in new[] { 1f, 1.33f }
                        from hud in new[] { 0.85f, 1f, 1.15f }
                        select (res.w, res.h, gui, hud);
            foreach (var (w, h, gui, hud) in cases) d.Add(w, h, gui, hud);
            return d;
        }

        [Fact]
        public void ThemeDesignScaleIsEightyPercent() => Assert.Equal(0.8f, F("scale.hud.strip"), 3);

        // Strip share of the screen height = the previous size (design 1, Hud.Scale ignored) x 0.8 x Hud.Scale.
        [Theory]
        [MemberData(nameof(Grid))]
        public void StripShareIsEightyPercentOfPrevious(int w, int h, float gui, float hud)
        {
            var old = At(w, h, gui, 1f, 1f);
            var now = At(w, h, gui, F("scale.hud.strip"), hud);
            Assert.Equal(0.8f * hud, now.Share / old.Share, 2);
            _out.WriteLine($"{w}x{h} gui {gui} hud {hud}: share {100f * old.Share:0.0} % -> {100f * now.Share:0.0} %, cell {old.CellPx:0.0} -> {now.CellPx:0.0} px");
        }

        // Gaps / margins = reference px x GUI factor x Hud.Scale, never the old font-floored 0.923 at 720.
        [Theory]
        [MemberData(nameof(Grid))]
        public void GapsScaleWithGuiFactor(int w, int h, float gui, float hud)
        {
            var s = At(w, h, gui, F("scale.hud.strip"), hud);
            Assert.Equal(12f * s.Gui * hud, s.GapPx, 3);
            if (h == 720 && gui == 1f && hud == 1f) Assert.Equal(8f, s.GapPx, 2); // was 11.08 (12 x 0.923)
        }

        // Cell texts (StripCell: key 27, count 26, timer 24 strip units) render at least their floor (number 14 px,
        // timer 12 px) and still fit the cell: key over count stacked, a 3-digit count and a "1:05" timer across
        // (digit advance <= 0.6 em in font.body).
        [Theory]
        [MemberData(nameof(Grid))]
        public void TextFloorsHoldAndFitTheCell(int w, int h, float gui, float hud)
        {
            var s = At(w, h, gui, F("scale.hud.strip"), hud);
            float Px(string sizeToken, string floorToken) => StripMath.FontFloor(F(sizeToken), F(floorToken), s.UnitPx) * s.UnitPx;
            var key = Px("size.hud.strip.key.font", "floor.text.number.px");
            var count = Px("size.hud.strip.count.font", "floor.text.number.px");
            var timer = Px("size.hud.strip.timer.font", "floor.text.timer.px");
            Assert.True(key >= F("floor.text.number.px") - 0.01f, $"key {key}");
            Assert.True(count >= F("floor.text.number.px") - 0.01f, $"count {count}");
            Assert.True(timer >= F("floor.text.timer.px") - 0.01f, $"timer {timer}");
            Assert.True(key + count <= s.CellPx, $"{w}x{h} gui {gui} hud {hud}: key {key:0.0} + count {count:0.0} > cell {s.CellPx:0.0}");
            Assert.True(3f * 0.6f * count <= s.CellPx, $"count '999' {3f * 0.6f * count:0.0} > cell {s.CellPx:0.0}");
            Assert.True(4f * 0.6f * timer <= s.CellPx, $"timer '1:05' {4f * 0.6f * timer:0.0} > cell {s.CellPx:0.0}");
        }

        // 720p, GUI 1, Hud.Scale 1: the hotbar cell is 70 x 0.667 x 0.8 = 37.3 px and the floors win over the cell size.
        [Fact]
        public void Cell720IsThirtySevenPx()
        {
            var s = At(1280, 720, 1f, F("scale.hud.strip"), 1f);
            Assert.Equal(37.33f, s.CellPx, 1);
            Assert.True(F("size.hud.strip.count.font") * s.UnitPx < F("floor.text.number.px")); // floor raises it
        }
    }
}
