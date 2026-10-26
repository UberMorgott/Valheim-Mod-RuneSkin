using RuneSkin.CraftingScreen;
using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // Window content inset (PanelDecor.KeepOut, user 2026-10-26): every window's inner rows keep the corner-cap keep-out
    // from the left / right / bottom edges (footer plates, dock grid + actions, crafting list / requirements / button).
    public class CornerInsetTests
    {
        private const float K = 32f;

        [Theory]
        [InlineData(0f)]
        [InlineData(K)]
        public void CraftingContent_ClearsTheCaps(float cap)
        {
            var i = new CraftingInput { Panel = new Box(1312f, 32f, 576f, 818f), Tabs = 3, Categories = 9, Requirements = 4, Scale = 1f, CapInset = cap, WindowHeaderH = 40f };
            var o = CraftingMath.Compute(i, new CraftingTokens());
            var edge = System.Math.Max(new CraftingTokens().Pad, cap);
            foreach (var b in new[] { o.List, o.Detail, o.ButtonRow, o.Requirements })
            {
                Assert.True(b.X >= i.Panel.X + edge - 0.01f, $"{b} left of {i.Panel.X + edge}");
                Assert.True(b.Right <= i.Panel.Right - edge + 0.01f, $"{b} right of {i.Panel.Right - edge}");
                Assert.True(b.Bottom <= i.Panel.Bottom - edge + 0.01f, $"{b} below {i.Panel.Bottom - edge}");
            }
        }

        [Fact]
        public void InventoryFooterAndGrid_ClearTheCaps()
        {
            var t = new InventoryTokens { CenterCapInset = K };
            var o = InventoryMath.Compute(new InventoryInput { ChestW = 5, ChestH = 2 }, t);
            var pad = t.PanelPad;
            // The skin draws the footer plates Inset(Footer, pad, FooterInsetY), the grid Inset(Grid, pad, pad).
            Assert.True(o.Footer.X + pad >= o.Center.X + K - 0.01f);
            Assert.True(o.Footer.Right - pad <= o.Center.Right - K + 0.01f);
            Assert.True(o.Footer.Bottom - InventoryMath.FooterInsetY <= o.Center.Bottom - K + 0.01f);
            Assert.True(o.Grid.X + pad >= o.Center.X + K - 0.01f);
            Assert.True(o.Grid.Bottom <= o.Footer.Y + 0.01f);
        }

        [Fact]
        public void DockGridAndActions_ClearTheCaps()
        {
            var t = new InventoryTokens { PanelCapInset = K };
            var o = InventoryMath.Compute(new InventoryInput { ChestW = 8, ChestH = 4 }, t);
            var view = InventoryMath.ContainerGridView(o.Chest, o.ChestGrid, t, 1f);
            var row = InventoryMath.ActionRow(o.Chest, t, 1f);
            Assert.True(view.X >= o.Chest.X + K - 0.01f && view.Right <= o.Chest.Right - K + 0.01f);
            Assert.True(row.X >= o.Chest.X + K - 0.01f && row.Right <= o.Chest.Right - K + 0.01f);
            Assert.True(row.Bottom <= o.Chest.Bottom - K + 0.01f);
            Assert.True(o.ChestGrid.Columns * o.ChestGrid.Pitch <= view.W + 0.5f);
        }
    }
}
