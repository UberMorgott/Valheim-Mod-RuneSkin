using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using RuneSkin.InventoryScreen;
using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    public class WindowMoveTests
    {
        private static readonly Box Screen1080 = WindowMoveMath.Bounds(1920f, 1080f, 900f);

        [Fact]
        public void Bounds_CutAtStripTop()
        {
            Assert.Equal(new Box(0f, 0f, 1920f, 900f).ToString(), Screen1080.ToString());
            Assert.Equal(1080f, WindowMoveMath.Bounds(1920f, 1080f, 0f).H);
            Assert.Equal(1080f, WindowMoveMath.Bounds(1920f, 1080f, 2000f).H);
        }

        [Fact]
        public void Clamp_InsideMoveKeepsOffset()
        {
            float dx = 200f, dy = -100f;
            WindowMoveMath.Clamp(new Box(400f, 300f, 600f, 400f), Screen1080, ref dx, ref dy);
            Assert.Equal(200f, dx);
            Assert.Equal(-100f, dy);
        }

        [Fact]
        public void Clamp_OffScreenStopsAtEdges()
        {
            var w = new Box(400f, 300f, 600f, 400f);
            float dx = 5000f, dy = 5000f;
            WindowMoveMath.Clamp(w, Screen1080, ref dx, ref dy);
            var moved = WindowMoveMath.Shift(w, dx, dy);
            Assert.Equal(1920f, moved.Right, 3);
            Assert.Equal(900f, moved.Bottom, 3); // above the strip
            dx = -5000f; dy = -5000f;
            WindowMoveMath.Clamp(w, Screen1080, ref dx, ref dy);
            moved = WindowMoveMath.Shift(w, dx, dy);
            Assert.Equal(0f, moved.X, 3);
            Assert.Equal(0f, moved.Y, 3);
        }

        [Fact]
        public void Clamp_DefaultPastBoundsNeverMovedByClamp()
        {
            // Docked block reaching under the strip (bottom 1000 > 900): offset 0 stays 0, going further down is refused,
            // going up is allowed.
            var w = new Box(32f, 32f, 1856f, 968f);
            float dx = 0f, dy = 0f;
            WindowMoveMath.Clamp(w, Screen1080, ref dx, ref dy);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            dx = 300f; dy = 50f;
            WindowMoveMath.Clamp(w, Screen1080, ref dx, ref dy);
            Assert.Equal(32f, dx, 3);
            Assert.Equal(0f, dy);
            dy = -20f;
            WindowMoveMath.Clamp(w, Screen1080, ref dx, ref dy);
            Assert.Equal(-20f, dy);
        }

        [Fact]
        public void Clamp_FullScreenWindowCannotMove()
        {
            var w = new Box(0f, 0f, 1920f, 1080f);
            float dx = 200f, dy = -100f;
            WindowMoveMath.Clamp(w, WindowMoveMath.Bounds(1920f, 1080f, 0f), ref dx, ref dy);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
        }

        [Fact]
        public void Clamp_NaNIsZero()
        {
            float dx = float.NaN, dy = float.PositiveInfinity;
            WindowMoveMath.Clamp(new Box(10f, 10f, 10f, 10f), Screen1080, ref dx, ref dy);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
        }

        [Fact]
        public void Key_IdAndResolution() => Assert.Equal("inventory@2560x1440", WindowMoveMath.Key("inventory", 2560, 1440));

        [Fact]
        public void Persistence_RoundTripInvariantCulture()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU"); // decimal comma must not leak into the file
            try
            {
                var store = new Dictionary<string, (float X, float Y)>
                {
                    ["inventory@1920x1080"] = (12.5f, -40.25f),
                    ["stats@2560x1440"] = (-3f, 7f),
                    ["zero@1920x1080"] = (0f, 0f),
                };
                var text = WindowMoveMath.Format(store);
                Assert.Contains("inventory@1920x1080=12.5,-40.25", text, System.StringComparison.Ordinal);
                Assert.DoesNotContain("zero@", text, System.StringComparison.Ordinal);
                var back = WindowMoveMath.Parse(text);
                Assert.Equal(2, back.Count);
                Assert.Equal((12.5f, -40.25f), back["inventory@1920x1080"]);
                Assert.Equal((-3f, 7f), back["stats@2560x1440"]);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [Fact]
        public void Persistence_SkipsMalformed()
        {
            var back = WindowMoveMath.Parse("# c\n\nbad line\na=1\nb=x,2\nc=NaN,1\nmod.ModUi.Win@800x600=4,5\r\n");
            Assert.Single(back);
            Assert.Equal((4f, 5f), back["mod.ModUi.Win@800x600"]);
            Assert.Empty(WindowMoveMath.Parse(null));
        }

        [Fact]
        public void Union_IgnoresEmpty()
        {
            var u = WindowMoveMath.Union(new Box(10f, 10f, 10f, 10f), new Box(30f, 5f, 10f, 10f));
            Assert.Equal(new Box(10f, 5f, 30f, 15f).ToString(), u.ToString());
            Assert.Equal(new Box(1f, 2f, 3f, 4f).ToString(), WindowMoveMath.Union(default, new Box(1f, 2f, 3f, 4f)).ToString());
        }

        [Fact]
        public void InventoryShift_MovesEveryBoxKeepsFits()
        {
            var o = InventoryMath.Compute(new InventoryInput { ChestW = 5, ChestH = 2 }, new InventoryTokens());
            Assert.Same(o, InventoryMath.Shift(o, 0f, 0f));
            var s = InventoryMath.Shift(o, 25f, -10f);
            foreach (var (a, b) in new[] { (o.Center, s.Center), (o.Chest, s.Chest), (o.Crafting, s.Crafting), (o.Grid, s.Grid), (o.Doll, s.Doll), (o.Footer, s.Footer), (o.Header, s.Header), (o.Dock, s.Dock) })
            {
                Assert.Equal(a.X + 25f, b.X, 3);
                Assert.Equal(a.Y - 10f, b.Y, 3);
                Assert.Equal(a.W, b.W, 3);
                Assert.Equal(a.H, b.H, 3);
            }
            Assert.True(s.Backpack.IsEmpty == o.Backpack.IsEmpty);
            Assert.Equal(o.Player.Pitch, s.Player.Pitch);
            Assert.Equal(o.ChestGrid.Columns, s.ChestGrid.Columns);
            var block = InventoryMath.Block(o);
            Assert.True(o.Center.Inside(block) && o.Crafting.Inside(block) && o.Chest.Inside(block));
            Assert.Equal(block.X + 25f, InventoryMath.Block(s).X, 3);
        }
    }
}
