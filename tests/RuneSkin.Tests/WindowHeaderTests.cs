using RuneSkin.InventoryScreen;
using RuneSkin.ModScreens;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    // RuneWindowHeader logic: band / title boxes, drag offset, double-click reset, never-empty titles, ids.
    public class WindowHeaderTests
    {
        [Fact]
        public void FrameKeepOut_PlateBelowTheBorderAndBetweenTheCorners()
        {
            // Band 0..600 x 460..500 (y up); frame inner 10..590 x 0..495 (top border 5 down from 500).
            var band = new WindowHeaderMath.Span(0f, 460f, 600f, 500f);
            var inner = new WindowHeaderMath.Span(10f, 0f, 590f, 495f);
            WindowHeaderMath.FrameKeepOut(band, inner, null, 2f, out var l0, out var r0, out var t0);
            Assert.Equal(12f, l0, 3);
            Assert.Equal(12f, r0, 3);
            Assert.Equal(7f, t0, 3);
            // Corner caps 0..100 / 500..600 reaching into the band push both sides past them; a cap beside / above / below
            // the band does not.
            var caps = new[]
            {
                new WindowHeaderMath.Span(-5f, 420f, 100f, 505f), new WindowHeaderMath.Span(500f, 420f, 605f, 505f),
                new WindowHeaderMath.Span(250f, 0f, 350f, 100f), new WindowHeaderMath.Span(700f, 460f, 800f, 500f),
            };
            WindowHeaderMath.FrameKeepOut(band, inner, caps, 2f, out var l1, out var r1, out var t1);
            Assert.Equal(102f, l1, 3);
            Assert.Equal(102f, r1, 3);
            Assert.Equal(t0, t1, 3);
            // A band already inside: no inset.
            WindowHeaderMath.FrameKeepOut(new WindowHeaderMath.Span(150f, 400f, 450f, 440f), inner, caps, 2f, out var l2, out var r2, out var t2);
            Assert.Equal(0f, l2 + r2 + t2, 3);
        }

        [Fact]
        public void PlateInFrame_StaysCentredTitleInsideThePlate()
        {
            WindowHeaderMath.PlateInFrame(2f, 30f, 30f, 102f, 40f, out var pl, out var pr, out var tl, out var tr);
            Assert.Equal(102f, pl, 3);
            Assert.Equal(pl, pr, 3); // centred plate: the larger keep-out on both sides
            Assert.Equal(130f, tl, 3); // title keeps its 28 margin inside the plate
            Assert.Equal(tl, tr, 3);
            WindowHeaderMath.PlateInFrame(2f, 30f, 30f, 0f, 0f, out pl, out pr, out tl, out tr);
            Assert.Equal(2f, pl, 3); // no keep-out: the plain slots
            Assert.Equal(30f, tl, 3);
        }

        // AB 3-column backpack (window 316 = size.inv.backpack.min, band 316 - 2 x 12 pad = 292, header 40, frame keep-out
        // 82 a side -> plate 128 as drawn in run 20261027-144717, header glyph left, no aux): the centred title room was 8
        // units -> '…' at 60 %. A short title (72 units at size 20) gets the empty side's reserve and keeps a readable box at
        // 720p and 1440p (canvas scale 0.667 / 1.333): >= 16 screen px wide and fitted above the 60 % floor. Full AB names
        // need a wider window: InventoryWindowsTests.AbBackpackHeader_FullTitleFits.
        [Theory]
        [InlineData(0.6667f)]
        [InlineData(1.3333f)]
        public void BackpackHeaderTitle_KeepsAReadableBox(float canvasScale)
        {
            const float w = 292f, h = 40f, keep = 82f, natural = 72f;
            WindowHeaderMath.LocalSlots(w, h, true, 0f, out var inset, out var titleL, out var titleR, out var font);
            WindowHeaderMath.PlateInFrame(inset, titleL, titleR, keep, keep, out var pl, out var pr, out var tl, out var tr);
            Assert.Equal(128f, w - pl - pr, 3);
            Assert.True(w - tl - tr < 16f); // the symmetric reserve alone left no room (the regression)
            WindowHeaderMath.FreeEmptySide(w, h, pl, pr, true, false, natural, ref tl, ref tr);
            Assert.Equal(titleL + keep - inset, tl, 3); // the icon side keeps its slot
            Assert.Equal(pr + h * (WindowHeaderMath.InsetShare - WindowHeaderMath.PlateInsetShare), tr, 3); // empty side down to its edge inset
            var room = w - tl - tr;
            Assert.True(room * canvasScale >= 16f, $"title box {room * canvasScale:0.#} px");
            Assert.True(WindowHeaderMath.FitFont(font, natural, room, 0.6f) > font * 0.6f + 0.5f);
        }

        [Fact]
        public void FreeEmptySide_KeepsAFittingTitleCentredAndSlottedSidesFixed()
        {
            float tl = 62f, tr = 62f;
            WindowHeaderMath.FreeEmptySide(400f, 40f, 2f, 2f, true, false, 100f, ref tl, ref tr);
            Assert.Equal(62f, tr, 3); // fits: centred, untouched
            WindowHeaderMath.FreeEmptySide(200f, 40f, 2f, 2f, true, true, 500f, ref tl, ref tr);
            Assert.Equal(62f, tl, 3); // both sides hold a slot: nothing given up
            Assert.Equal(62f, tr, 3);
            WindowHeaderMath.FreeEmptySide(200f, 40f, 2f, 2f, true, false, 86f, ref tl, ref tr);
            Assert.Equal(52f, tr, 3); // gives just the 10 missing units
        }

        [Fact]
        public void FitFont_StepsDownOnlyWhenTooWide()
        {
            Assert.Equal(20f, WindowHeaderMath.FitFont(20f, 300f, 400f, 0.75f), 3);
            Assert.Equal(16f, WindowHeaderMath.FitFont(20f, 500f, 400f, 0.75f), 3);
            Assert.Equal(15f, WindowHeaderMath.FitFont(20f, 2000f, 400f, 0.75f), 3); // floor
        }

        [Fact]
        public void Band_IsTheWindowTop()
        {
            var w = new Box(100f, 50f, 600f, 400f);
            Assert.Equal(new Box(100f, 50f, 600f, 40f).ToString(), WindowHeaderMath.Band(w, 40f).ToString());
            Assert.Equal(20f, WindowHeaderMath.Band(new Box(0f, 0f, 10f, 20f), 40f).H); // never taller than the window
            Assert.True(WindowHeaderMath.Band(default, 40f).IsEmpty);
        }

        [Fact]
        public void Slots_TitleCentredWhateverTheExtras()
        {
            var band = new Box(100f, 50f, 600f, 40f);
            WindowHeaderMath.Slots(band, false, 0f, out var i0, out var a0, out var t0);
            WindowHeaderMath.Slots(band, true, 0f, out var i1, out var a1, out var t1);
            WindowHeaderMath.Slots(band, true, 110f, out var i2, out var a2, out var t2);
            foreach (var t in new[] { t0, t1, t2 })
            {
                Assert.Equal(band.X + band.W / 2f, t.X + t.W / 2f, 3); // centre = band centre
                Assert.Equal(band.Y, t.Y, 3);
                Assert.Equal(band.H, t.H, 3);
            }
            Assert.True(i0.IsEmpty && a0.IsEmpty && a1.IsEmpty);
            // Icon left, aux right, both vertically centred, never under the title.
            Assert.Equal(band.X + 40f * WindowHeaderMath.InsetShare, i1.X, 3);
            Assert.Equal(band.Y + (40f - i1.H) / 2f, i1.Y, 3);
            Assert.Equal(band.Right - 40f * WindowHeaderMath.InsetShare, a2.Right, 3);
            Assert.False(i2.Overlaps(t2) || a2.Overlaps(t2));
            // One height = one font size, one inset: identical headers.
            WindowHeaderMath.LocalSlots(600f, 40f, false, 0f, out var p1, out var l1, out var r1, out var f1);
            WindowHeaderMath.LocalSlots(900f, 40f, false, 0f, out var p2, out var l2, out var r2, out var f2);
            Assert.Equal(f1, f2);
            Assert.Equal(p1, p2);
            Assert.Equal(l1, r1);
            Assert.Equal(40f * WindowHeaderMath.FontShare, f1, 3);
            Assert.Equal(46f, WindowHeaderMath.HeaderHeight(40f, 1.15f), 3);
        }

        [Fact]
        public void Drag_PointerTravelInCanvasUnits_YDown()
        {
            // 1440p: 1.333 px per unit; pointer +200 px right, +100 px up (screen y up) -> +150 right, -75 (up) in canvas units.
            WindowHeaderMath.DragOffset(10f, 20f, 500f, 500f, 700f, 600f, 1440f / 1080f, out var dx, out var dy);
            Assert.Equal(10f + 150f, dx, 3);
            Assert.Equal(20f - 75f, dy, 3);
            // No movement: the start offset.
            WindowHeaderMath.DragOffset(-4f, 8f, 1f, 1f, 1f, 1f, 1f, out dx, out dy);
            Assert.Equal(-4f, dx);
            Assert.Equal(8f, dy);
            // A broken scale never divides by zero.
            WindowHeaderMath.DragOffset(0f, 0f, 0f, 0f, 10f, 0f, 0f, out dx, out _);
            Assert.Equal(10f, dx, 3);
        }

        [Fact]
        public void DoubleClickLeftResets()
        {
            Assert.True(WindowHeaderMath.IsReset(2, true));
            Assert.True(WindowHeaderMath.IsReset(3, true));
            Assert.False(WindowHeaderMath.IsReset(1, true));
            Assert.False(WindowHeaderMath.IsReset(2, false));
        }

        [Fact]
        public void Title_NeverEmpty_TagsStripped()
        {
            Assert.Equal("Сундук", WindowHeaderMath.Title("  <color=#fff>Сундук</color> ", "x"));
            Assert.Equal("Chest", WindowHeaderMath.Title("", "Chest"));
            Assert.Equal("Chest", WindowHeaderMath.Title("<b></b>", "<i>Chest</i>"));
            Assert.Equal("-", WindowHeaderMath.Title(null, null));
            Assert.Equal("A B", WindowHeaderMath.Title("A\nB", null));
        }

        [Fact]
        public void ModWindowTitle_Readable()
        {
            Assert.Equal("Enchanting UI", ForeignMath.WindowTitle("EnchantingUI(Clone)", "x"));
            Assert.Equal("AB Backpack Panel", ForeignMath.WindowTitle("AB_BackpackPanel", "x"));
            Assert.Equal("Merchant Panel", ForeignMath.WindowTitle("EpicLoot.Adventure.MerchantPanel", "x"));
            Assert.Equal("mod.id", ForeignMath.WindowTitle("", "mod.id"));
        }

        // Integrated action header (variant A plinth, 2026-10-27): right cap flush with the plate's right edge, one cell
        // per action leftwards, the left cap before them; the plate ends under the left cap; buttons centred in cells.
        [Fact]
        public void Plinth_RightAlignedCellsAndPlateEndsUnderTheLeftCap()
        {
            var p = WindowHeaderMath.PlinthLayout(400f, 0f, 30f, 3, 1.3f, 2f, 0.9f, 0.4f, 0.8f, 0.3f);
            Assert.False(p.IsEmpty);
            Assert.Equal(39f, p.H, 3);
            Assert.Equal(-4.5f, p.Y, 3); // centred on the plate: protrudes evenly above and below
            Assert.Equal(400f, p.CapRightX + p.CapRightW, 3);
            Assert.Equal(p.CapRightX, p.CellX(3), 3);
            Assert.Equal(p.CellsX, p.CapLeftX + p.CapLeftW, 3);
            Assert.Equal(0.9f * 39f, p.CellW, 3);
            Assert.Equal(p.CapLeftX + 0.3f * p.CapLeftW, p.PlateEnd, 3);
            Assert.True(p.PlateEnd < p.CellsX); // no plate under a button
            Assert.Equal(0.8f * p.CellW, p.Button, 3);
            Assert.Equal(p.CellX(1) + (p.CellW - p.Button) / 2f, p.ButtonX(1), 3);
            Assert.Equal(p.Y + (p.H - p.Button) / 2f, p.ButtonY, 3);
            Assert.Equal(p.CapLeftW + 3 * p.CellW + p.CapRightW, p.Width, 3);
        }

        [Fact]
        public void Plinth_EmptyWithoutActionsOrPlate()
        {
            Assert.True(WindowHeaderMath.PlinthLayout(400f, 0f, 30f, 0, 1.3f, 2f, 0.9f, 0.4f, 0.8f, 0.3f).IsEmpty);
            Assert.True(WindowHeaderMath.PlinthLayout(400f, 0f, 0f, 3, 1.3f, 2f, 0.9f, 0.4f, 0.8f, 0.3f).IsEmpty);
            Assert.Equal(0f, WindowHeaderMath.TitleClearOfPlinth(400f, default, 5f));
        }

        // 0.25.10 title band (BandForTitle / FreeEmptySide) with a plinth: the plinth reserve counts as the aux slot, so the
        // band sized for a title keeps it at the readable floor (TitleMinShare) left of the plinth.
        [Fact]
        public void Plinth_BandForTitleKeepsTheTitleAtTheFloor()
        {
            const float h = 30f, natural = 200f;
            var probe = WindowHeaderMath.PlinthLayout(1000f, 0f, h, 3, 1.32f, 2f, 0.81f, 0.37f, 0.86f, 0.48f);
            var aux = 1000f - probe.CapLeftX - h * WindowHeaderMath.InsetShare; // RuneWindowHeader._plinthAux
            var w = WindowHeaderMath.BandForTitle(natural, h, false, aux, 0f, 0f);
            var p = WindowHeaderMath.PlinthLayout(w, 0f, h, 3, 1.32f, 2f, 0.81f, 0.37f, 0.86f, 0.48f);
            var tr = WindowHeaderMath.TitleClearOfPlinth(w, p, h * WindowHeaderMath.GapShare);
            var tl = tr; // centred
            WindowHeaderMath.FreeEmptySide(w, h, h * WindowHeaderMath.PlateInsetShare, w - p.PlateEnd, false, true, natural, ref tl, ref tr);
            Assert.True(w - tl - tr >= natural * WindowHeaderMath.TitleMinShare - 0.01f);
            Assert.True(w - tr <= p.CapLeftX); // never into the plinth
        }
        [Fact]
        public void Plinth_TitleClearsTheWholePlinth()
        {
            var p = WindowHeaderMath.PlinthLayout(390f, 0f, 30f, 2, 1.3f, 2f, 0.9f, 0.4f, 0.8f, 0.3f);
            var inset = WindowHeaderMath.TitleClearOfPlinth(400f, p, 6f);
            Assert.Equal(400f - p.CapLeftX + 6f, inset, 3);
            Assert.True(400f - inset < p.CapLeftX); // title right edge left of the plinth
        }
    }
}
