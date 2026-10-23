using System;
using RuneSkin.API;
using RuneSkin.ModScreens;
using RuneSkin.Theme;
using Xunit;
using static RuneSkin.ModScreens.ForeignMath;

namespace RuneSkin.Tests
{
    // M7 fallback restyler rules (ModScreens\ForeignMath): what counts as a foreign unit, and the shipped theme.
    public class ForeignUiTests
    {
        private static Verdict Root => default;

        [Theory]
        [InlineData("assembly_valheim", AssemblyKind.Game)]
        [InlineData("assembly_guiutils", AssemblyKind.Game)]
        [InlineData("UnityEngine.UI", AssemblyKind.Game)]
        [InlineData("Unity.TextMeshPro", AssemblyKind.Game)]
        [InlineData("RuneSkin", AssemblyKind.Own)]
        [InlineData("EpicLoot", AssemblyKind.Foreign)]
        [InlineData("Jotunn", AssemblyKind.Foreign)]
        [InlineData("OtherSkin", AssemblyKind.Foreign)]
        public void AssembliesAreClassified(string asm, AssemblyKind want) => Assert.Equal(want, Classify(asm));

        [Fact]
        public void OptOutListIsCaseInsensitiveAndTrimmed()
        {
            var set = ParseList(" OtherSkin, BA_WorldBossBar ,,");
            Assert.Equal(2, set.Count);
            Assert.Contains("otherskin", set);
            Assert.Contains("BA_WorldBossBar", set);
            Assert.Empty(ParseList(null));
        }

        // A mod window (its own component, no game component) outside every family root is a ModUi unit.
        [Fact]
        public void ForeignComponentStartsAUnit()
        {
            var v = Step(Root, new Node { Foreign = true }, 3, true);
            Assert.True(v.HasUnit);
            Assert.Equal(SkinFamilies.ModUi, v.Family);
        }

        // Vanilla objects (or vanilla clones a mod adds a component to) and the boundary never start a unit.
        [Fact]
        public void VanillaAndBoundaryAreNotUnits()
        {
            Assert.False(Step(Root, new Node { Foreign = true, Game = true }, 1, true).HasUnit);
            Assert.False(Step(Root, new Node { Foreign = true, Boundary = true }, 1, true).HasUnit);
            Assert.False(Step(Root, new Node(), 1, true).HasUnit);
        }

        // Inside a family root only anchors (Jotunn-built) and precise adapters start a unit.
        [Fact]
        public void FamilyRootKeepsGenericForeignPartsButNotPreciseOnes()
        {
            var inRoot = Step(Root, new Node { FamilyRoot = true }, 1, true);
            Assert.True(inRoot.UnderRoot);
            Assert.False(Step(inRoot, new Node { Foreign = true }, 2, true).HasUnit);
            Assert.True(Step(inRoot, new Node { Anchor = true }, 2, true).HasUnit);
            var board = Step(inRoot, new Node { Foreign = true, Hint = SkinFamilies.EpicLootBoard, HintOwned = true }, 2, true);
            Assert.True(board.HasUnit);
            Assert.Equal(SkinFamilies.EpicLootBoard, board.Family);
        }

        // A precise family the other skin keeps blocks the subtree (no generic restyle on top of its look).
        [Fact]
        public void UnownedPreciseFamilyBlocks()
        {
            var v = Step(Root, new Node { Foreign = true, Hint = SkinFamilies.EpicLootTemper, HintOwned = false }, 1, true);
            Assert.True(v.Blocked);
            Assert.True(Step(v, new Node { Foreign = true }, 2, true).Blocked);
        }

        // Opt-out names, RuneSkin's own objects and world-space canvases block everything below.
        [Fact]
        public void OptOutOwnAndWorldCanvasBlock()
        {
            Assert.True(Step(Root, new Node { OptedOut = true, Foreign = true }, 1, true).Blocked);
            Assert.True(Step(Root, new Node { Own = true }, 1, true).Blocked);
            Assert.True(Step(Root, new Node { WorldCanvas = true }, 1, true).Blocked);
        }

        // An opted-out plugin's component blocks a would-be unit, never the vanilla boundary: BossAwakening's WorldBoss
        // on _GameMain (run 20261022-181305: no fallback unit under _GameMain at all) must not hide every screen below.
        [Fact]
        public void OptedOutPluginOnTheBoundaryPassesThrough()
        {
            var v = Step(Root, new Node { Boundary = true, Game = true, Foreign = true, OptedOutPlugin = true }, 0, true);
            Assert.False(v.Blocked);
            Assert.True(Step(v, new Node { Foreign = true }, 1, true).HasUnit);
            Assert.True(Step(Root, new Node { Foreign = true, OptedOutPlugin = true }, 1, true).Blocked);
            Assert.False(Step(Root, new Node { FamilyRoot = true, OptedOutPlugin = true }, 1, true).Blocked);
        }

        // The topmost foreign object wins: children inherit their parent's unit.
        [Fact]
        public void ChildrenInheritTheTopmostUnit()
        {
            var top = Step(Root, new Node { Foreign = true }, 2, true);
            var child = Step(top, new Node { Foreign = true, Hint = SkinFamilies.EpicLootEnchanting, HintOwned = true }, 3, true);
            Assert.Equal(2, child.UnitDepth);
            Assert.Equal(SkinFamilies.ModUi, child.Family);
        }

        // Generic family off: only precise adapters act.
        [Fact]
        public void GenericOffKeepsOnlyPreciseUnits()
        {
            Assert.False(Step(Root, new Node { Foreign = true }, 1, false).HasUnit);
            Assert.False(Step(Root, new Node { Anchor = true }, 1, false).HasUnit);
            Assert.True(Step(Root, new Node { Hint = SkinFamilies.ModHud, HintOwned = true }, 1, false).HasUnit);
        }

        [Theory]
        [InlineData(0, 1f, 400f, 300f, true, true)]
        [InlineData(1, 0.8f, 400f, 300f, true, true)]
        [InlineData(1, 0.3f, 400f, 300f, true, false)]  // a row / cell inside the window
        [InlineData(2, 1f, 400f, 300f, true, false)]
        [InlineData(0, 1f, 100f, 300f, true, false)]    // under the size floor
        [InlineData(0, 1f, 400f, 300f, false, false)]   // simple sprite = picture, not a frame
        public void WindowPanelIsTheWindowOnly(int depth, float share, float w, float h, bool sliced, bool want) =>
            Assert.Equal(want, IsWindowPanel(depth, share, w, h, 120f, sliced));

        [Theory]
        [InlineData("Sacrifice", "glyph.ench.sacrifice")]
        [InlineData("ConvertMaterials", "glyph.ench.convert")]
        [InlineData("Enchant", "glyph.ench.enchant")]
        [InlineData("Augment", "glyph.ench.augment")]
        [InlineData("Disenchant", "glyph.ench.disenchant")]
        [InlineData("Upgrade", "glyph.ench.upgrade")]
        [InlineData("Close", null)]
        public void EnchantTabsMapToKitGlyphs(string tab, string want) => Assert.Equal(want, EnchantGlyph(tab));

        [Theory]
        [InlineData("SecretStashTab", "glyph.board.stash")]
        [InlineData("GambleTab", "glyph.board.gamble")]
        [InlineData("TreasureMapsTab", "glyph.board.treasure")]
        [InlineData("BountiesTab", "glyph.board.bounty")]
        [InlineData("Buy", null)]
        public void BoardTabsMapToKitGlyphs(string tab, string want) => Assert.Equal(want, BoardGlyph(tab));

        // Stable family names (other mods ask for them by string) and the shipped theme declares each one with its roles.
        [Fact]
        public void M7FamiliesAreDeclaredInTheTheme()
        {
            Assert.Equal("ModUi", SkinFamilies.ModUi);
            Assert.Equal("EpicLootEnchanting", SkinFamilies.EpicLootEnchanting);
            Assert.Equal("EpicLootBoard", SkinFamilies.EpicLootBoard);
            Assert.Equal("EpicLootTemper", SkinFamilies.EpicLootTemper);
            Assert.Equal("ModHud", SkinFamilies.ModHud);
            Assert.Equal("InventoryExtras", SkinFamilies.InventoryExtras);
            Assert.Equal("Radial", SkinFamilies.Radial);
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Empty(ThemeSchema.Validate(f));
            foreach (var fam in new[] { "ModUi", "EpicLootEnchanting", "EpicLootBoard", "EpicLootTemper", "ModHud", "InventoryExtras", "Radial" })
                Assert.Contains(f.families, x => x.family == fam);
            foreach (var role in new[] { "legacy.title", "legacy.body", "legacy.label", "legacy.muted", "glyph.ench.enchant", "glyph.board.bounty", "plate.currency" })
                Assert.Contains(f.roles, x => x.role == role);
            Assert.Equal("font.display", Array.Find(f.roles, x => x.role == "legacy.title").font);
        }

        // Epic Loot enchanting tab children (EnchantingUI prefab Panel/Tabs/<feature>): the padlock "LockIcon" is never
        // the tab icon (its name holds "Icon": it was repainted as the tab glyph, so only the kit-less Rune tab showed
        // a lock).
        [Theory]
        [InlineData("Image", "Sacrifice", ElTabPart.Icon)]
        [InlineData("Text", "Sacrifice", ElTabPart.Caption)]
        [InlineData("Locked", "Image", ElTabPart.LockBadge)]
        [InlineData("LockIcon", "Locked", ElTabPart.LockGlyph)]
        [InlineData("Glow", "Locked", ElTabPart.LockGlyph)]
        [InlineData("convert_tabicon", "Convert", ElTabPart.Icon)]
        [InlineData("Focused", "Sacrifice", ElTabPart.None)]
        public void EnchantTabPartsAreNamedApart(string name, string parent, ElTabPart want) => Assert.Equal(want, ElTabPartOf(name, parent));

        // Every tab gets the same boxes: icon centred horizontally, caption box under it inside the cell, lock badge on
        // the icon's top-right corner and inside the cell.
        [Theory]
        [InlineData(136f, 80f)]
        [InlineData(137f, 80f)]
        [InlineData(120f, 64f)]
        public void EnchantTabCellIsUniform(float w, float h)
        {
            ElTabLayout(w, h, 28f, 3f, 40f, 4f, 18f, out var icon, out var caption, out var badge);
            Assert.True(Math.Abs(icon.CentreX - w / 2f) <= 1f);
            Assert.Equal(icon.W, icon.H);
            Assert.True(icon.Y >= 0f && caption.Y >= icon.Y + icon.H);
            Assert.True(caption.Y + caption.H <= h + 0.01f);
            Assert.True(caption.X >= 0f && caption.X + caption.W <= w);
            Assert.True(Math.Abs(caption.CentreX - w / 2f) <= 1f);
            Assert.True(badge.X >= 0f && badge.Y >= 0f && badge.X + badge.W <= w && badge.Y + badge.H <= h);
            Assert.True(badge.X + badge.W > icon.X + icon.W - badge.W && badge.Y < icon.Y + badge.H); // on the top-right corner
            // Same input, same boxes (no per-tab state).
            ElTabLayout(w, h, 28f, 3f, 40f, 4f, 18f, out var icon2, out _, out _);
            Assert.Equal(icon.X, icon2.X);
            Assert.Equal(icon.Y, icon2.Y);
        }

        [Fact]
        public void EnchantTabShrinksIconInAShortCell()
        {
            ElTabLayout(136f, 60f, 28f, 3f, 40f, 4f, 18f, out var icon, out var caption, out _);
            Assert.True(icon.H < 28f && icon.H >= 20f && icon.Y >= 0f);
            Assert.True(caption.Y + caption.H <= 60f);
        }

        // Plates fit inside the column's mask (Epic Loot: 137 px plates, 160 px mask, right padding -10).
        [Theory]
        [InlineData(137f, 160f, 4f, 137f)]
        [InlineData(160f, 160f, 4f, 152f)]
        [InlineData(137f, 100f, 4f, 92f)]
        public void EnchantTabPlatesFitTheMask(float plate, float mask, float margin, float want) =>
            Assert.Equal(want, ElTabWidth(plate, mask, margin));

        [Fact]
        public void EnchantTabLockRolesAreInTheTheme()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            foreach (var role in new[] { "badge.ench.lock", "glyph.ench.lock" })
                Assert.Contains(f.roles, x => x.role == role);
            foreach (var token in new[] { "size.el.tab.w", "size.el.tab.h", "size.el.tab.icon", "size.el.tab.caption", "size.el.tab.badge" })
                Assert.Contains(f.floats, x => x.token == token);
            foreach (var token in new[] { "sprite.ench.lock.badge", "sprite.ench.lock.glyph" })
                Assert.Contains(f.sprites, x => x.token == token);
        }
    }
}
