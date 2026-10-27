using System.Collections.Generic;
using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Ammo plate + crossbow magazine dots (AmmoMath, MagazineContract): vanilla's ammo choice (Attack.FindAmmo /
    // Inventory.GetAmmoItem), the shot total, the dot rules (<= 7 dots, more = one dot + n/N) at 2560 x 1440 and
    // 1920 x 1080, and the MorgottTweaks read contract.
    public class AmmoHudTests
    {
        private const string Arrows = "$ammo_arrows", Bolts = "$ammo_bolts";

        private static AmmoCandidate C(string prefab, string ammo, int x, int y, int stack, int type = AmmoMath.Ammo) =>
            new AmmoCandidate { Prefab = prefab, AmmoType = ammo, GridX = x, GridY = y, Stack = stack, ItemType = type };

        [Fact]
        public void EquippedAmmoWinsWhenItsTypeMatches()
        {
            var items = new List<AmmoCandidate> { C("ArrowWood", Arrows, 0, 1, 50), C("ArrowFire", Arrows, 5, 3, 20) };
            Assert.Equal(1, AmmoMath.SelectAmmo(Arrows, items, 1, 8));
        }

        [Fact]
        public void EquippedAmmoOfAnotherTypeFallsBackToFirstInGridOrder()
        {
            var items = new List<AmmoCandidate> { C("BoltIron", Bolts, 0, 3, 20), C("ArrowFire", Arrows, 5, 2, 20), C("ArrowWood", Arrows, 2, 2, 50) };
            // equipped = the bolts, weapon = bow: first arrows by y * width + x = (2,2)
            Assert.Equal(2, AmmoMath.SelectAmmo(Arrows, items, 0, 8));
        }

        [Fact]
        public void NoEquippedAmmoPicksLowestGridIndexIncludingEaqsRows()
        {
            // EAQS quick cells are rows of the player inventory (y past the visible rows): they lose to any earlier cell
            var items = new List<AmmoCandidate> { C("BoltBlackmetal", Bolts, 1, 5, 30), C("BoltIron", Bolts, 7, 3, 20) };
            Assert.Equal(1, AmmoMath.SelectAmmo(Bolts, items, -1, 8));
            items.RemoveAt(1);
            Assert.Equal(0, AmmoMath.SelectAmmo(Bolts, items, -1, 8));
        }

        [Fact]
        public void OnlyAmmoTypesGetAmmoItemAcceptsAreCandidates()
        {
            var items = new List<AmmoCandidate>
            {
                C("Odd", Arrows, 0, 0, 1, 3),                        // a weapon-type item with an ammo type: skipped
                C("Turret", Arrows, 1, 0, 5, AmmoMath.AmmoNonEquipable),
                C("Bait", Arrows, 2, 0, 5, AmmoMath.Consumable)
            };
            Assert.Equal(1, AmmoMath.SelectAmmo(Arrows, items, -1, 8));
            Assert.Equal(-1, AmmoMath.SelectAmmo("", items, -1, 8));
            Assert.Equal(-1, AmmoMath.SelectAmmo(Bolts, items, -1, 8));
        }

        [Fact]
        public void TotalSumsEveryStackOfTheChosenPrefab()
        {
            var items = new List<AmmoCandidate>
            {
                C("ArrowFire", Arrows, 0, 1, 100), C("ArrowWood", Arrows, 1, 1, 100), C("ArrowFire", Arrows, 2, 4, 43) // 3rd: EAQS row
            };
            Assert.Equal(143, AmmoMath.Total(items, 0));
            Assert.Equal(100, AmmoMath.Total(items, 1));
            Assert.Equal(0, AmmoMath.Total(items, -1));
            Assert.Equal("×143", AmmoMath.CountText(143));
            Assert.Equal("×0", AmmoMath.CountText(-2));
        }

        [Theory]
        [InlineData(0, MagazineMode.None)]
        [InlineData(1, MagazineMode.Dots)]
        [InlineData(7, MagazineMode.Dots)]
        [InlineData(8, MagazineMode.Compact)]
        [InlineData(20, MagazineMode.Compact)]
        public void ModeSwitchesToCompactPastSeven(int capacity, MagazineMode mode) => Assert.Equal(mode, AmmoMath.Mode(capacity));

        [Fact]
        public void MagazineTextClamps()
        {
            Assert.Equal("3/5", AmmoMath.MagazineText(3, 5));
            Assert.Equal("15/12", AmmoMath.MagazineText(15, 12)); // bolts kept over a lowered capacity
            Assert.Equal("0/9", AmmoMath.MagazineText(-1, 9));
        }

        // Inventory cell (InventoryMath.Cell durability pill: x 0.12 side, top = h - 0.08 side - 0.06 side) at the
        // reference cell side 64 units; px = screen px per unit (1080: 1, 1440: 1.333).
        [Theory]
        [InlineData(64f, 1f)]
        [InlineData(64f, 1.3333f)]
        [InlineData(40f, 0.75f)]
        public void CellDotsFitAboveTheDurabilityPill(float side, float px)
        {
            var durX = side * 0.12f;
            var durY = side - side * 0.08f - side * 0.06f;
            for (var cap = 1; cap <= 20; cap++)
            {
                var r = AmmoMath.CellRow(side, side, durX, durY, cap / 2, cap, px);
                Assert.Equal(cap <= 7 ? cap : 1, r.Count);
                Assert.Equal(cap / 2, r.Filled);
                Assert.True(r.X >= 0f);
                var right = r.Mode == MagazineMode.Compact ? r.TextX + r.TextW : r.X + (r.Count - 1) * r.Pitch + r.Dot;
                Assert.True(right <= side - side * 0.07f + 0.01f, $"cap {cap}: row ends at {right} past the inset");
                Assert.True(r.Y >= 0f);
                Assert.True((durY - (r.Y + r.Dot)) * px >= 3f - 0.01f, $"cap {cap}: gap to the pill under 3 px");
                Assert.True(r.Dot > 0f && r.Dot <= r.Pitch);
                if (r.Mode == MagazineMode.Compact)
                {
                    Assert.True(r.TextW > r.Dot, "compact text box too narrow");
                    var textBottom = r.Y + r.Dot / 2f + r.Dot * 1.45f / 2f; // MagazinePips: text 1.45 x Dot centred on the dot
                    Assert.True((durY - textBottom) * px >= 3f - 0.01f, $"cap {cap}: n/N box under 3 px from the pill");
                }
            }
        }

        [Fact]
        public void SevenDotsShrinkToTheRowButFiveKeepTheMockupSize()
        {
            var side = 64f;
            var five = AmmoMath.CellRow(side, side, side * 0.12f, side * 0.86f, 3, 5, 1f);
            Assert.Equal(side * 0.144f, five.Pitch, 3);
            Assert.Equal(side * 0.12f, five.Dot, 3);
            var seven = AmmoMath.CellRow(side, side, side * 0.12f, side * 0.86f, 7, 7, 1f);
            Assert.True(seven.Pitch < five.Pitch);
            Assert.Equal(7, seven.Filled);
        }

        // Ammo plate second line (units): pad 11.25 + "×9999" (~ 4.5 glyphs x 0.6 x 12) + 9, dots 7.5 + 1.5, text gap 4.5.
        [Fact]
        public void PlateRowFitsTheFixedPlateWidth()
        {
            const float plateW = 144f, pad = 11.25f, countW = 33f;
            var x = pad + countW + 9f;
            foreach (var cap in new[] { 1, 5, 7, 8, 12, 20 })
            {
                var r = AmmoMath.PlateRow(x, 24f, cap - 1, cap, 7.5f, 1.5f, 4.5f);
                Assert.Equal(cap <= 7 ? cap : 1, r.Count);
                var magW = (cap >= 10 ? 5 : 3) * 0.6f * 9f; // "17/20" at the 9-unit font
                Assert.True(r.TextX + magW <= plateW - pad * 0.5f, $"cap {cap}: n/N ends past the plate");
                Assert.Equal(24f - 3.75f, r.Y, 3);
            }
            Assert.Equal(MagazineMode.None, AmmoMath.PlateRow(x, 24f, 0, 0, 7.5f, 1.5f, 4.5f).Mode);
        }

        // Content-sized plate (mockup f1-varA: bow ~110, crossbow 3/5 ~131, 17/20 ~154 units): longer line + pad,
        // clamped to [min, max].
        [Fact]
        public void PlateWidthFollowsTheLongerLine()
        {
            const float pad = 11.25f, min = 96f, max = 160f;
            Assert.Equal(110f + pad, AmmoMath.PlateWidth(110f, 40f, pad, min, max), 3);   // name longer (bow)
            Assert.Equal(125f + pad, AmmoMath.PlateWidth(90f, 125f, pad, min, max), 3);   // readout longer (crossbow)
            Assert.Equal(min, AmmoMath.PlateWidth(30f, 20f, pad, min, max), 3);           // short content: minimum
            Assert.Equal(max, AmmoMath.PlateWidth(400f, 20f, pad, min, max), 3);          // long name: capped (autoshrink)
            Assert.Equal(min, AmmoMath.PlateWidth(-5f, -5f, pad, min, max), 3);
            Assert.Equal(min, AmmoMath.PlateWidth(500f, 0f, pad, min, 50f), 3);           // max below min: min wins
            Assert.True(AmmoMath.PlateWidth(120f, 40f, pad, min, max) > AmmoMath.PlateWidth(100f, 40f, pad, min, max));
        }

        // ---- MorgottTweaks read contract

        public static class FakeMagazine
        {
            public static int Capacity(string item) => item == "xbow" ? 5 : 0;
            public static int Loaded(string item) => item == "xbow" ? 3 : 0;
            public static int Revision() => 42;
        }

        public static class WrongSignatures
        {
            public static float Capacity(string item) => 1f;
            public static int Loaded(int item) => 1;
            public static long Revision() => 1;
        }

        [Fact]
        public void ContractBindsTheExactSignatures()
        {
            var c = MagazineContract.Bind<string>(typeof(FakeMagazine));
            Assert.True(c.Complete);
            Assert.Equal(5, c.Capacity("xbow"));
            Assert.Equal(3, c.Loaded("xbow"));
            Assert.Equal(0, c.Capacity("bow"));
            Assert.Equal(42, c.Revision());
            Assert.Equal("MorgottTweaks.CrossbowMagazine", MagazineContract.TypeName);
        }

        [Fact]
        public void ContractWithoutMorgottTweaksOrChangedMembersIsOff()
        {
            Assert.False(MagazineContract.Bind<string>(null).Complete);
            var w = MagazineContract.Bind<string>(typeof(WrongSignatures));
            Assert.False(w.Complete);
            Assert.Null(w.Capacity);
            Assert.Null(w.Loaded);
            Assert.Null(w.Revision);
        }
    }
}
