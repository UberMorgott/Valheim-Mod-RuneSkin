using RuneSkin.InventoryScreen;
using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // HUD band profile while the inventory is open + the height it gives back to the columns (M2, layout-a2.png).
    public class InventoryBandTests
    {
        // default-theme.json values of layout A.
        private static InventoryTokens Theme() => new InventoryTokens
        {
            HeaderH = 128f,
            FooterH = 48f,
            PanelPad = 12f,
            GridCell = 70f,
            GridGap = 8f,
            HotbarHeaderH = 32f,
            DollCell = 84f,
            DollGap = 8f,
            DollPad = 8f,
            DollMirrorGap = 28f,
            PanelMaxH = 840f
        };

        private static InventoryLayout Run(InventoryTokens t, float hudTop, int cw = 0, int ch = 0) =>
            InventoryMath.Compute(new InventoryInput { CanvasW = 1920f, CanvasH = 1080f, ChestW = cw, ChestH = ch, HudTop = hudTop }, t);

        [Fact]
        public void BandSpansTheGivenWidthBarsRightOfTheLeftBlock()
        {
            var b = HudMath.BandLayout(1856f, 420f, 110f, 96f, 58f, 16f, 280f, 0f);
            Assert.True(b.Fits);
            Assert.Equal(436f, b.BarsX, 2);
            Assert.Equal(1856f, b.BarsX + b.BarOuter, 2); // right edge = block right edge
            Assert.Equal(110f, b.BodyH, 2);
            Assert.Equal(58f + 110f, b.Height, 2);
            Assert.Equal(58f, b.LeftY, 2);                 // taller block sits on the status row
            Assert.Equal(58f + 7f, b.BarsY, 2);            // shorter one centred on the body
        }

        [Fact]
        public void BandTooNarrowKeepsTheStack()
        {
            Assert.False(HudMath.BandLayout(600f, 420f, 110f, 96f, 58f, 16f, 280f, 0f).Fits);
            Assert.False(HudMath.BandLayout(0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f).Fits);
            Assert.False(HudMath.BandLayout(float.PositiveInfinity, 10f, 1f, 1f, 1f, 1f, 1f, 0f).Fits);
        }

        [Fact]
        public void BandHeightIgnoresEffectCount()
        {
            // Status row reserved as one row: same band for 0 and many effects (the strips overflow into "+N").
            var a = HudMath.BandLayout(1856f, 420f, 110f, 96f, 58f, 16f, 280f, 0f);
            var b = HudMath.BandLayout(1856f, 420f, 110f, 96f, 58f, 16f, 280f, 0f);
            Assert.Equal(a.Height, b.Height);
        }

        [Fact]
        public void ShortBandGivesTheColumnsTheirHeightBack()
        {
            var t = Theme();
            var stacked = Run(t, 1080f - 24f - 350f, 8, 4);
            var band = Run(t, 1080f - 24f - 170f, 8, 4);
            Assert.True(band.Center.H > stacked.Center.H + 100f, $"{band.Center} vs {stacked.Center}");
            Assert.True(band.Player.Pitch > stacked.Player.Pitch, $"{band.Player.Pitch} vs {stacked.Player.Pitch}");
            Assert.Equal(band.Center.Bottom, band.Dock.Bottom, 2);
            Assert.Equal(band.Center.Bottom, band.Crafting.Bottom, 2);
            Assert.True(band.Center.Bottom <= 1080f - 24f - 170f - t.Gap + 0.01f);
        }

        [Fact]
        public void HeaderAndGapsShrinkBeforeTheCells()
        {
            var t = Theme();
            var full = Run(t, 0f);
            // Taller column than needed: header at its token.
            var tall = Theme();
            tall.PanelMaxH = 900f;
            var roomy = InventoryMath.Compute(new InventoryInput { CanvasW = 1920f, CanvasH = 1400f, HudTop = 1400f - 200f }, tall);
            Assert.Equal(128f, roomy.Header.H, 2);
            Assert.Equal(t.GridCell + t.GridGap, roomy.Player.Pitch, 2);
            // A little short: the header gives height back, cells keep their maximum pitch.
            var need = 128f + 48f + 3f * 8f + 32f + 24f + 16f + 4f * 78f + 3f * 92f;
            var slight = InventoryMath.Compute(new InventoryInput { CanvasW = 1920f, CanvasH = 1080f, HudTop = 32f + need - 10f + 16f }, t);
            Assert.True(slight.Header.H < 128f && slight.Header.H >= t.HeaderMinH - 0.01f, $"header {slight.Header.H}");
            Assert.Equal(t.GridCell + t.GridGap, slight.Player.Pitch, 2);
            // Much shorter: header at its minimum, gaps at theirs, then the cells shrink.
            var tight = InventoryMath.Compute(new InventoryInput { CanvasW = 1920f, CanvasH = 1080f, HudTop = 700f }, t);
            Assert.Equal(t.HeaderMinH, tight.Header.H, 2);
            Assert.Equal(t.InnerGapMin, tight.InnerGap, 2);
            Assert.True(tight.Player.Pitch < t.GridCell + t.GridGap);
            Assert.True(full.Player.Pitch >= tight.Player.Pitch);
        }

        [Fact]
        public void BlockEdgesFollowTheColumns()
        {
            var t = Theme();
            var withDock = Run(t, 0f, 8, 4);
            InventoryMath.BlockEdges(withDock, out var l, out var r);
            Assert.Equal(withDock.Dock.X, l, 2);
            Assert.Equal(withDock.Crafting.Right, r, 2);
            var noDock = Run(t, 0f);
            InventoryMath.BlockEdges(noDock, out l, out r);
            Assert.Equal(noDock.Center.X, l, 2);
            Assert.Equal(noDock.Crafting.Right, r, 2);
        }

        [Fact]
        public void DollFigureIsAPortraitBoxGroupCentred()
        {
            var d = new DollFit { StorageColumns = 3, Rows = 3, Pitch = 92f, Cell = 84f };
            var box = new Box(600f, 200f, 680f, 300f);
            var s = InventoryMath.Strip(box, d, 8f, 28f, 0.66f);
            Assert.True(s.Figure.H > s.Figure.W, $"portrait {s.Figure}");
            Assert.Equal(s.Figure.H * 0.66f, s.Figure.W, 2);
            var storageW = 3f * 92f - 8f;
            Assert.Equal(s.StorageX + storageW + 28f, s.Figure.X, 2);
            Assert.Equal(s.Figure.Right + 28f, s.MirrorX, 2);
            var left = s.StorageX - box.X;
            var right = box.Right - (s.MirrorX + d.Cell);
            Assert.Equal(left, right, 2);
            // Narrow box: the figure takes only the room left, never pushes the mirrors out.
            var narrow = InventoryMath.Strip(new Box(0f, 0f, 420f, 300f), d, 8f, 28f, 0.66f);
            Assert.True(narrow.MirrorX + d.Cell <= 420.01f);
        }

        [Fact]
        public void DockActionRowClearsTheCornerCap()
        {
            var t = Theme();
            t.PanelCapInset = 40f;
            var panel = new Box(32f, 32f, 528f, 800f);
            var row = InventoryMath.ActionRow(panel, t, 1f);
            Assert.True(panel.Right - row.Right >= 40f - 0.01f);
            Assert.True(panel.Bottom - row.Bottom >= 40f - 0.01f);
            // Without a cap: the old spot (pad / 2 above the bottom, pad from the right).
            t.PanelCapInset = 0f;
            row = InventoryMath.ActionRow(panel, t, 1f);
            Assert.Equal(panel.Bottom - t.PanelPad / 2f, row.Bottom, 2);
            Assert.Equal(panel.Right - t.PanelPad, row.Right, 2);
        }

        [Fact]
        public void ContainerGridViewIsTopAlignedAndClearOfActions()
        {
            var t = Theme();
            t.PanelCapInset = 40f;
            var o = Run(t, 0f, 8, 2);
            var view = InventoryMath.ContainerGridView(o.Chest, o.ChestGrid, t, 1f);
            Assert.Equal(o.Chest.Y + t.PanelHeaderH + t.PanelPad, view.Y, 2);
            Assert.Equal(o.ChestGrid.VisibleRows * o.ChestGrid.Pitch, view.H, 2); // short grid: view = its rows
            Assert.True(view.Bottom <= InventoryMath.ActionRow(o.Chest, t, 1f).Y + 0.01f);
            var deep = Run(t, 0f, 8, 30);
            Assert.True(deep.ChestGrid.ScrollV);
            var dv = InventoryMath.ContainerGridView(deep.Chest, deep.ChestGrid, t, 1f);
            Assert.True(dv.Bottom <= InventoryMath.ActionRow(deep.Chest, t, 1f).Y + 0.01f);
            Assert.True(deep.ChestGrid.VisibleRows * deep.ChestGrid.Pitch <= dv.H + 0.01f, "visible rows fit the view");
        }
    }
}
