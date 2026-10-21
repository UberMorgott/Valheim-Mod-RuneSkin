using System.Collections.Generic;
using RuneSkin.API;
using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    public class BarLengthTests
    {
        [Theory]
        [InlineData(25f, 160f)]       // short pool: floor
        [InlineData(200f, 256f)]      // proportional (1.28 px per unit, Hud.cs:1084)
        [InlineData(300f, 384f)]      // the cap
        [InlineData(1000f, 384f)]     // above cap: fixed
        [InlineData(0f, 160f)]
        [InlineData(float.NaN, 160f)]
        public void ClampsProportionalLength(float max, float expected) =>
            Assert.Equal(expected, HudMath.BarLength(max, 160f, 384f, 0.28f, 1920f), 3);

        [Fact]
        public void NarrowSafeAreaCapsAtShareButNeverBelowMin()
        {
            // 28 % of 1000 = 280 < 384.
            Assert.Equal(280f, HudMath.BarLength(1000f, 160f, 384f, 0.28f, 1000f), 3);
            // 28 % of 400 = 112 < min 160: limits never invert.
            Assert.Equal(160f, HudMath.BarLength(1000f, 160f, 384f, 0.28f, 400f), 3);
        }

        [Theory]
        [InlineData(100f, 100.5f, false)] // food decay tick: debounced
        [InlineData(100f, 102f, false)]   // < 3 %
        [InlineData(100f, 104f, true)]
        [InlineData(160f, 100f, true)]    // shrink
        public void GhostOnlyForRealSteps(float a, float b, bool ghost) =>
            Assert.Equal(ghost, HudMath.IsGhostStep(a, b, 0.03f));

        [Fact]
        public void EaseEndsOnTarget()
        {
            Assert.Equal(10f, HudMath.Ease(10f, 20f, 0f), 3);
            Assert.Equal(20f, HudMath.Ease(10f, 20f, 1f), 3);
            Assert.Equal(15f, HudMath.Ease(10f, 20f, 0.5f), 3);
        }
    }

    public class StripeTests
    {
        private static List<KeyValuePair<int, float>> P(params (int k, float v)[] e)
        {
            var l = new List<KeyValuePair<int, float>>();
            foreach (var x in e) l.Add(new KeyValuePair<int, float>(x.k, x.v));
            return l;
        }

        [Fact]
        public void RightAlignedFromFillEndInFixedOrder()
        {
            var o = new HudMath.Stripe[3];
            // B 100 over 200 px, current 50 -> fill end at 100 px; poison 10, fire 5.
            var n = HudMath.Stripes(P((1, 10f), (2, 5f)), 50f, 100f, 200f, 4f, 3, o);
            Assert.Equal(2, n);
            Assert.Equal(1, o[0].Key);
            Assert.Equal(80f, o[0].Start, 3);
            Assert.Equal(20f, o[0].Width, 3);
            Assert.Equal(2, o[1].Key);
            Assert.Equal(70f, o[1].Start, 3);
            Assert.Equal(10f, o[1].Width, 3);
        }

        [Fact]
        public void TotalCappedAtCurrentValue()
        {
            var o = new HudMath.Stripe[3];
            var n = HudMath.Stripes(P((1, 500f)), 20f, 100f, 200f, 4f, 3, o);
            Assert.Equal(1, n);
            Assert.Equal(0f, o[0].Start, 3);
            Assert.Equal(40f, o[0].Width, 3); // whole current fill, never beyond
        }

        [Fact]
        public void MoreThanBudgetMergesIntoOther()
        {
            var o = new HudMath.Stripe[3];
            var n = HudMath.Stripes(P((1, 5f), (2, 5f), (3, 5f), (4, 5f)), 100f, 100f, 200f, 1f, 3, o);
            Assert.Equal(3, n);
            Assert.Equal(-1, o[2].Key);
            Assert.Equal(20f, o[2].Width, 3); // 3rd + 4th merged
        }

        [Fact]
        public void MinimumWidthWithoutGrowingTotal()
        {
            var o = new HudMath.Stripe[3];
            // 0.5 + 20 units over 200 px: 1 px + 40 px; min 4 px taken from the wide one.
            var n = HudMath.Stripes(P((1, 0.5f), (2, 20f)), 100f, 100f, 200f, 4f, 3, o);
            Assert.Equal(2, n);
            Assert.Equal(4f, o[0].Width, 3);
            Assert.Equal(37f, o[1].Width, 3);
            Assert.Equal(41f, o[0].Width + o[1].Width, 3);
        }

        [Fact]
        public void TooNarrowForAllKeepsWhatFits()
        {
            var o = new HudMath.Stripe[3];
            // total 6 px with min 4 -> one stripe holding everything.
            var n = HudMath.Stripes(P((1, 1f), (2, 1f), (3, 1f)), 100f, 100f, 200f, 4f, 3, o);
            Assert.Equal(1, n);
            Assert.Equal(6f, o[0].Width, 3);
        }

        [Fact]
        public void NothingForZeroOrInvalid()
        {
            var o = new HudMath.Stripe[3];
            Assert.Equal(0, HudMath.Stripes(P((1, 5f)), 0f, 100f, 200f, 4f, 3, o));
            Assert.Equal(0, HudMath.Stripes(P((1, 5f)), 10f, 0f, 200f, 4f, 3, o));
            Assert.Equal(0, HudMath.Stripes(P((1, float.NaN), (2, -3f)), 10f, 100f, 200f, 4f, 3, o));
        }

        [Fact]
        public void ReservedSegmentAtBarEnd()
        {
            HudMath.Reserved(60f, 100f, 200f, out var s, out var w);
            Assert.Equal(120f, s, 3);
            Assert.Equal(80f, w, 3);
            HudMath.Reserved(100f, 100f, 200f, out _, out w);
            Assert.Equal(0f, w);
            HudMath.Reserved(5f, 0f, 200f, out _, out w);
            Assert.Equal(0f, w);
        }
    }

    public class FlowTests
    {
        [Fact]
        public void EmptyDrawsNothing()
        {
            var f = HudMath.Wrap(0, 38f, 4f, 300f, 2);
            Assert.Equal(0, f.Visible);
            Assert.Equal(0, f.Rows);
            Assert.False(f.MoreCell);
        }

        [Fact]
        public void WrapsIntoRows()
        {
            // 300 px: (300+4)/(42) = 7 columns.
            var f = HudMath.Wrap(9, 38f, 4f, 300f, 2);
            Assert.Equal(7, f.Columns);
            Assert.Equal(2, f.Rows);
            Assert.Equal(9, f.Visible);
            Assert.Equal(0, f.Overflow);
        }

        [Fact]
        public void OverflowReservesTheMoreCellBeforeCounting()
        {
            var f = HudMath.Wrap(20, 38f, 4f, 300f, 2); // 14 cells capacity
            Assert.True(f.MoreCell);
            Assert.Equal(13, f.Visible);
            Assert.Equal(7, f.Overflow);
            Assert.Equal(14, f.Visible + 1);
        }

        [Fact]
        public void ExactFitNeedsNoMoreCell()
        {
            var f = HudMath.Wrap(14, 38f, 4f, 300f, 2);
            Assert.False(f.MoreCell);
            Assert.Equal(14, f.Visible);
        }

        [Fact]
        public void ZeroRowsSendsEverythingToTheFullList()
        {
            var f = HudMath.Wrap(5, 38f, 4f, 300f, 0);
            Assert.Equal(0, f.Visible);
            Assert.Equal(5, f.Overflow);
            Assert.False(f.MoreCell);
        }

        [Theory]
        [InlineData(84f, 38f, 4f, 2)]
        [InlineData(37f, 38f, 4f, 0)]
        [InlineData(38f, 38f, 4f, 1)]
        public void RowsFromHeightBudget(float budget, float cell, float gap, int rows) =>
            Assert.Equal(rows, HudMath.MaxRows(budget, cell, gap));

        [Fact]
        public void OneCellWideStillWraps()
        {
            var f = HudMath.Wrap(3, 46f, 5f, 10f, 3); // narrower than one cell -> 1 column
            Assert.Equal(1, f.Columns);
            Assert.Equal(3, f.Rows);
        }
    }

    public class ScaleTests
    {
        [Fact]
        public void HudScaleMultipliesGuiFactor() =>
            Assert.Equal(1.25f, HudMath.ClusterScale(1f, 1.25f, 300f, 200f, 1920f, 1080f, 0.34f, 0.3f, 16f, 14f, 13f, 12f), 3);

        [Fact]
        public void HudScaleClampedToRange()
        {
            Assert.Equal(1.5f, HudMath.ClusterScale(1f, 9f, 10f, 10f, 1920f, 1080f, 1f, 1f, 16f, 14f, 13f, 12f), 3);
            Assert.Equal(0.9230769f, HudMath.ClusterScale(1f, 0.1f, 10f, 10f, 1920f, 1080f, 1f, 1f, 16f, 14f, 13f, 12f), 4); // floor 12/13
        }

        [Fact]
        public void ClusterShrinksToItsScreenShare()
        {
            // 800 x 324 cluster: 34 % of 1920 = 652.8 -> 0.816; 30 % of 1080 = 324 -> 1.0.
            Assert.Equal(0.923f, HudMath.ClusterScale(1f, 1f, 800f, 324f, 1920f, 1080f, 0.34f, 0.3f, 16f, 14f, 13f, 12f), 3);
        }

        [Fact]
        public void FontFloorWinsOverShrink()
        {
            // 720p guiFactor 0.667: numbers 16 ref px would be 10.7 px (floor 14 -> 0.875), timers 13 ref px
            // would be 8.7 px (floor 12 -> 0.923): the stricter floor wins.
            Assert.Equal(0.923f, HudMath.ClusterScale(0.6667f, 1f, 400f, 300f, 1280f, 720f, 0.34f, 0.3f, 16f, 14f, 13f, 12f), 3);
        }

        [Fact]
        public void UltrawideUsesCentredBox()
        {
            HudMath.HudBox(0f, 2560f, 1080f, true, 2.2f, out var x, out var w);
            Assert.Equal(1920f, w, 2);
            Assert.Equal(320f, x, 2);
            HudMath.HudBox(0f, 1920f, 1080f, true, 2.2f, out x, out w);
            Assert.Equal(0f, x);
            Assert.Equal(1920f, w);
            HudMath.HudBox(0f, 2560f, 1080f, false, 2.2f, out x, out w);
            Assert.Equal(2560f, w);
        }

        [Theory]
        [InlineData(0.4f, "1")]
        [InlineData(59.2f, "1:00")]
        [InlineData(65f, "1:05")]
        [InlineData(900f, "15m")]
        [InlineData(0f, "")]
        public void WholeSecondTimers(float s, string text) => Assert.Equal(text, HudMath.Timer(s));
    }

    public class TaxonomyTests
    {
        private static EffectFacts F() => new EffectFacts { HealthRegen = 1f, StaminaRegen = 1f, EitrRegen = 1f, ClassName = "SE_Stats" };

        [Fact]
        public void PoisonWithPoolIsForecastDot()
        {
            var f = F();
            f.ClassName = "SE_Poison";
            f.IsPoison = true;
            f.HasForecast = true;
            var c = EffectTaxonomy.Classify(f);
            Assert.True((c.Caps & Capability.DamageOverTime) != Capability.None);
            Assert.Equal(DotKind.Poison, c.Dot);
            Assert.True(c.Harmful);
            Assert.Equal(0, c.Category);
            Assert.Equal("color.dot.poison", c.TintToken);
        }

        [Fact]
        public void BurningSpiritKeepsItsKind()
        {
            var f = F();
            f.ClassName = "SE_Burning";
            f.IsSpirit = true;
            f.HasForecast = true;
            Assert.Equal(DotKind.Spirit, EffectTaxonomy.Classify(f).Dot);
        }

        [Fact]
        public void RestedIsRegenBuff()
        {
            var f = F();
            f.IsRested = true;
            f.StaminaRegen = 1.5f;
            var c = EffectTaxonomy.Classify(f);
            Assert.False(c.Harmful);
            Assert.True((c.Caps & Capability.RegenUp) != Capability.None);
            Assert.Equal(3, c.Category);
        }

        [Fact]
        public void NegativeHealthOverTimeIsNotDot()
        {
            var f = F();
            f.HealthOverTime = -10f;
            var c = EffectTaxonomy.Classify(f);
            Assert.Equal(Capability.None, c.Caps & Capability.DamageOverTime);
        }

        [Fact]
        public void ColdSuppressesRegenAndGetsFrostOverlayWetDrips()
        {
            var f = F();
            f.IsCold = true;
            f.HealthRegen = 0.5f;
            var c = EffectTaxonomy.Classify(f);
            Assert.True(c.Harmful);
            Assert.Equal(1, c.Category);
            Assert.Equal(FrameOverlay.Frost, c.Overlay);
            var w = F();
            w.IsWet = true;
            var wc = EffectTaxonomy.Classify(w);
            Assert.Equal(FrameOverlay.Wet, wc.Overlay);
            Assert.True(wc.OverlayPriority < c.OverlayPriority);
        }

        [Fact]
        public void NoFieldsIsMarkerNotHarmless()
        {
            var c = EffectTaxonomy.Classify(F());
            Assert.Equal(Capability.Marker, c.Caps);
            Assert.Equal(4, c.Category);
        }

        [Fact]
        public void MixedResistBuffWithSlowSortsAsControl()
        {
            var f = F();
            f.ResistUps = 3;
            f.Speed = -0.8f;
            var c = EffectTaxonomy.Classify(f);
            Assert.True((c.Caps & Capability.Defense) != Capability.None);
            Assert.True((c.Caps & Capability.Control) != Capability.None);
            Assert.Equal(0, c.Category);
        }

        [Fact]
        public void StripOrderCategoryThenPriorityThenKey()
        {
            Assert.True(EffectTaxonomy.Compare(0, 0, 5, 3, 99, 1) < 0);
            Assert.True(EffectTaxonomy.Compare(1, 10, 5, 1, 2, 1) < 0);
            Assert.True(EffectTaxonomy.Compare(1, 2, 1, 1, 2, 5) < 0);
        }
    }

    public class ResourceCheckTests
    {
        private static ResourceState S() => new ResourceState { Valid = true, Current = 50f, Max = 100f, Available = 80f, ReservedAuras = 15f, ReservedSummons = 5f };

        [Fact]
        public void ConsistentSnapshotPasses() => Assert.True(ResourceCheck.IsSane(S(), true));

        [Fact]
        public void RejectsInconsistent()
        {
            var s = S(); s.Valid = false; Assert.False(ResourceCheck.IsSane(s, true));
            s = S(); s.Available = 120f; Assert.False(ResourceCheck.IsSane(s, true));
            s = S(); s.Max = float.NaN; Assert.False(ResourceCheck.IsSane(s, true));
            s = S(); s.ReservedAuras = 30f; Assert.False(ResourceCheck.IsSane(s, true));
            s = S(); s.Current = 150f; Assert.False(ResourceCheck.IsSane(s, true));
            s = S(); s.Current = 150f; Assert.True(ResourceCheck.IsSane(s, false)); // vanilla pools: current not used
            s = S(); s.PhaseSeconds = -1f; Assert.False(ResourceCheck.IsSane(s, true));
        }

        [Fact]
        public void ZeroCapacityIsValid()
        {
            var s = new ResourceState { Valid = true };
            Assert.True(ResourceCheck.IsSane(s, true));
            Assert.True(ResourceCheck.IsSane(new CapacityState { Valid = true, Count = 0 }));
            Assert.False(ResourceCheck.IsSane(new CapacityState { Valid = true, Count = -1 }));
        }
    }
}
