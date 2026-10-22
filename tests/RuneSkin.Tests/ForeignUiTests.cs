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
        [InlineData("AugaSkin", AssemblyKind.Foreign)]
        public void AssembliesAreClassified(string asm, AssemblyKind want) => Assert.Equal(want, Classify(asm));

        [Fact]
        public void OptOutListIsCaseInsensitiveAndTrimmed()
        {
            var set = ParseList(" AugaSkin, BA_WorldBossBar ,,");
            Assert.Equal(2, set.Count);
            Assert.Contains("augaskin", set);
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

        // A precise family the other skin keeps blocks the subtree (no generic restyle on top of AugaSkin's).
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

        // Stable family names (AugaSkin asks for them by string) and the shipped theme declares each one with its roles.
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
    }
}
