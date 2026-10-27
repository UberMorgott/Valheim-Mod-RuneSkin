using RuneSkin.CraftingScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // CraftingMath.RowImageRole: the recipe row roles the full crafting pass and the per-row pass (rows re-instantiated
    // on every InventoryGui.Show) both use - one rule, so the rows look the same whichever pass styled them.
    public class CraftingRowRoleTests
    {
        [Theory]
        [InlineData("bkg", true, false, true, "craft.row")]
        [InlineData("bkg", true, false, false, "craft.row.unavailable")]
        [InlineData("bkg", true, true, false, "craft.row.unavailable")] // the strip is the button target: canCraft wins
        [InlineData("selected", true, false, true, "craft.row.selected")]
        [InlineData("selected", true, false, false, "craft.row.selected")]
        [InlineData("icon", true, false, true, "content")]
        [InlineData("icon", true, false, false, "content")]
        [InlineData("Durability", true, false, false, "content")]
        [InlineData("magicItemIconBG", true, false, true, "content")]
        [InlineData("bar", false, false, false, "content")]       // a grandchild named like nothing special
        [InlineData("bkg", false, false, false, "content")]       // "bkg" deeper than the row: not the strip
        [InlineData("selected", false, true, true, "craft.row")]  // deeper "selected" that is the button target
        [InlineData("plate", false, true, false, "craft.row")]    // the row button's target graphic, canCraft irrelevant
        public void Roles(string name, bool childOfRow, bool buttonTarget, bool canCraft, string expected) =>
            Assert.Equal(expected, CraftingMath.RowImageRole(name, childOfRow, buttonTarget, canCraft));

        [Fact]
        public void TextsAreBodySans() => Assert.Equal("text.body", CraftingMath.RowTextRole);

        [Theory]
        [InlineData("bkg", true, false, "recipe row strip (canCraft from m_availableRecipes)")]
        [InlineData("selected", true, false, "recipe row selected rim (SetRecipe toggles it)")]
        [InlineData("plate", false, true, "recipe row button")]
        [InlineData("icon", true, false, "recipe row icon / durability / magic background")]
        public void Reasons(string name, bool childOfRow, bool buttonTarget, string expected) =>
            Assert.Equal(expected, CraftingMath.RowImageReason(name, childOfRow, buttonTarget));
    }
}
