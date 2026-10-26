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
    }
}
