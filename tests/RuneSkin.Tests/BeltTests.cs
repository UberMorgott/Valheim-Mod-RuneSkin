using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Belt row rules (BeltMath, MorgottTweaks Belt API v1): wing sources (belt replaces EAQS quick slots), row order
    // (food index 0 nearest the centre growing left, potions growing right), the gamepad walk and its presses.
    public class BeltTests
    {
        [Fact]
        public void OnlyApiOneIsRead()
        {
            Assert.True(BeltMath.ApiUsable(1));
            Assert.False(BeltMath.ApiUsable(0));
            Assert.False(BeltMath.ApiUsable(2));
        }

        [Fact]
        public void BeltReplacesEaqsQuickCells()
        {
            Assert.Equal(BeltMath.Source.Belt, BeltMath.PotionSource(true, true));
            Assert.Equal(BeltMath.Source.Belt, BeltMath.PotionSource(true, false));
            Assert.Equal(BeltMath.Source.EaqsQuick, BeltMath.PotionSource(false, true));
            Assert.Equal(BeltMath.Source.Vanilla, BeltMath.PotionSource(false, false));
            Assert.Equal(BeltMath.Source.Belt, BeltMath.FoodSource(true));
            Assert.Equal(BeltMath.Source.Vanilla, BeltMath.FoodSource(false));
        }

        [Fact]
        public void RowOrderFoodGrowsLeftPotionsRight()
        {
            // 3 food + 2 potion: positions 0..4 = food 2, food 1, food 0, potion 0, potion 1
            int[] kinds = { 0, 0, 0, 1, 1 }, indices = { 2, 1, 0, 0, 1 };
            for (var pos = 0; pos < 5; pos++)
            {
                Assert.True(BeltMath.RowCell(pos, 3, 2, out var k, out var i));
                Assert.Equal(kinds[pos], k);
                Assert.Equal(indices[pos], i);
                Assert.Equal(pos, BeltMath.RowPos(k, i, 3, 2));
            }
            Assert.False(BeltMath.RowCell(5, 3, 2, out _, out _));
            Assert.False(BeltMath.RowCell(-1, 3, 2, out _, out _));
            Assert.Equal(-1, BeltMath.RowPos(BeltMath.Food, 3, 3, 2)); // locked cell has no place
            Assert.Equal(-1, BeltMath.RowPos(BeltMath.Potion, 2, 3, 2));
        }

        [Fact]
        public void RowClampsToLayoutMax()
        {
            Assert.Equal(10, BeltMath.RowLength(9, 7));
            Assert.Equal(0, BeltMath.RowLength(-1, 0));
            Assert.True(BeltMath.RowCell(9, 9, 7, out var k, out var i));
            Assert.Equal(BeltMath.Potion, k);
            Assert.Equal(4, i);
        }

        [Fact]
        public void StepClampsWithoutWrap()
        {
            Assert.Equal(0, BeltMath.Step(0, -1, 3, 2));
            Assert.Equal(4, BeltMath.Step(4, 1, 3, 2));
            Assert.Equal(3, BeltMath.Step(2, 1, 3, 2));
            Assert.Equal(4, BeltMath.Step(9, 0, 3, 2)); // capacity shrank under the focus
            Assert.Equal(-1, BeltMath.Step(0, 1, 0, 0));
        }

        [Fact]
        public void EntryAndExitKeepTheRelativeColumn()
        {
            // 8-wide grid, 3 + 2 row
            Assert.Equal(0, BeltMath.EntryPos(0, 8, 3, 2));
            Assert.Equal(4, BeltMath.EntryPos(7, 8, 3, 2));
            Assert.Equal(2, BeltMath.EntryPos(4, 8, 3, 2));
            Assert.Equal(0, BeltMath.GridColumn(0, 8, 3, 2));
            Assert.Equal(7, BeltMath.GridColumn(4, 8, 3, 2));
            for (var x = 0; x < 8; x++)
            {
                var pos = BeltMath.EntryPos(x, 8, 5, 5);
                Assert.InRange(pos, 0, 9);
                Assert.InRange(System.Math.Abs(BeltMath.GridColumn(pos, 8, 5, 5) - x), 0, 1);
            }
            Assert.Equal(-1, BeltMath.EntryPos(3, 8, 0, 0));
        }

        [Fact]
        public void PadPressesMirrorTheGrid()
        {
            Assert.Equal(BeltMath.PadAction.Select, BeltMath.PadPress(true, false, false, false));
            Assert.Equal(BeltMath.PadAction.Select, BeltMath.PadPress(true, true, true, false));
            Assert.Equal(BeltMath.PadAction.ToggleAuto, BeltMath.PadPress(false, true, false, false));
            Assert.Equal(BeltMath.PadAction.MoveOut, BeltMath.PadPress(false, true, true, false));
            Assert.Equal(BeltMath.PadAction.UseNow, BeltMath.PadPress(false, true, false, true));
            Assert.Equal(BeltMath.PadAction.None, BeltMath.PadPress(false, false, true, true));
        }

        [Fact]
        public void RingAndWedgeFractions()
        {
            Assert.Equal(0.5f, BeltMath.Fraction(30f, 60f));
            Assert.Equal(1f, BeltMath.Fraction(90f, 60f));
            Assert.Equal(-1f, BeltMath.Fraction(0f, 60f));
            Assert.Equal(-1f, BeltMath.Fraction(10f, 0f));
            Assert.Equal(0.25f, BeltMath.Cooldown(15f, 60f));
            Assert.Equal(0f, BeltMath.Cooldown(0f, 60f));
        }
    }
}
