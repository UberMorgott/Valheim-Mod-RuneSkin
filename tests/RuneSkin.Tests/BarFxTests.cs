using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // Living bar fills (barfx): real value changes become event clocks once per event.
    public class BarFxTests
    {
        private static readonly BarFxTuning Tune = BarFxTuning.Default;

        [Fact]
        public void FirstSampleOnlyRecords()
        {
            var s = new BarFxState();
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 50f, 100f, 1f, 0.016f, Tune));
            Assert.Equal(BarFxState.Never, s.HitAt);
        }

        [Fact]
        public void BloodHitAndHeal()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Blood, 100f, 150f, 1f, 0.016f, Tune);
            var c = s.Step(BarFxPool.Blood, 70f, 150f, 2f, 0.016f, Tune);
            Assert.Equal(BarFxState.Changed.Hit, c);
            Assert.Equal(2f, s.HitAt);
            Assert.Equal(0.2f, s.HitSize, 3);
            // the same value next frame: no new event (written once)
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 70f, 150f, 2.1f, 0.016f, Tune));
            // a small regen tick (2 / 150 < 4 %) is no heal wave; a mead (+30) is
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 72f, 150f, 3f, 0.016f, Tune));
            Assert.Equal(BarFxState.Changed.Heal, s.Step(BarFxPool.Blood, 102f, 150f, 4f, 0.016f, Tune));
            Assert.Equal(4f, s.HealAt);
        }

        [Fact]
        public void TinyDrainIsNoHit()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Blood, 100f, 150f, 1f, 0.016f, Tune);
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 99.5f, 150f, 1.1f, 0.016f, Tune));
        }

        [Fact]
        public void MaxChangeResyncsWithoutEvent()
        {
            // food expires: max and current drop together -> no damage splash
            var s = new BarFxState();
            s.Step(BarFxPool.Blood, 150f, 150f, 1f, 0.016f, Tune);
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 110f, 110f, 2f, 0.016f, Tune));
            Assert.Equal(BarFxState.Never, s.HitAt);
            Assert.Equal(BarFxState.Changed.Hit, s.Step(BarFxPool.Blood, 90f, 110f, 3f, 0.016f, Tune));
        }

        [Fact]
        public void ArcanePulseOnSpendOnly()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Arcane, 80f, 100f, 1f, 0.016f, Tune);
            Assert.Equal(BarFxState.Changed.Pulse, s.Step(BarFxPool.Arcane, 60f, 100f, 2f, 0.016f, Tune));
            Assert.Equal(2f, s.PulseAt);
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Arcane, 61f, 100f, 2.5f, 0.016f, Tune));
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Arcane, 59f, 100f, 3f, 0.016f, Tune)); // 2 % < 3 %
        }

        [Fact]
        public void StaminaRegenHoldsThenStops()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Energy, 50f, 100f, 1f, 0.1f, Tune);
            Assert.Equal(BarFxState.Changed.Regen, s.Step(BarFxPool.Energy, 51f, 100f, 1.1f, 0.1f, Tune));
            Assert.True(s.Regen);
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Energy, 51f, 100f, 1.2f, 0.1f, Tune)); // held 0.35 s
            s.Step(BarFxPool.Energy, 51f, 100f, 1.3f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 51f, 100f, 1.4f, 0.1f, Tune);
            Assert.Equal(BarFxState.Changed.Regen, s.Step(BarFxPool.Energy, 51f, 100f, 1.5f, 0.1f, Tune));
            Assert.False(s.Regen);
        }

        [Fact]
        public void StaminaFullIsNoRegen()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Energy, 99.95f, 100f, 1f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 100f, 100f, 1.1f, 0.1f, Tune);
            Assert.False(s.Regen);
        }

        [Fact]
        public void ExhaustionLatchesUntilRecovered()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Energy, 10f, 100f, 1f, 0.1f, Tune);
            Assert.True((s.Step(BarFxPool.Energy, 0f, 100f, 1.1f, 0.1f, Tune) & BarFxState.Changed.Exhaust) != BarFxState.Changed.None);
            Assert.True(s.Exhaust);
            s.Step(BarFxPool.Energy, 15f, 100f, 2f, 0.1f, Tune);
            Assert.True(s.Exhaust); // below 20 %: still exhausted
            Assert.True((s.Step(BarFxPool.Energy, 21f, 100f, 3f, 0.1f, Tune) & BarFxState.Changed.Exhaust) != BarFxState.Changed.None);
            Assert.False(s.Exhaust);
        }

        [Fact]
        public void OffClearsRegenHold()
        {
            // regen sparkle running, effects switched off and back on at once: no stale sparkle
            var s = new BarFxState();
            s.Step(BarFxPool.Energy, 50f, 100f, 1f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 51f, 100f, 1.1f, 0.1f, Tune);
            Assert.True(s.Regen);
            s.Step(BarFxPool.Plain, 51f, 100f, 1.2f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 51f, 100f, 1.3f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 51f, 100f, 1.4f, 0.1f, Tune);
            Assert.False(s.Regen);
        }

        [Fact]
        public void PlainOrOffClearsFlags()
        {
            var s = new BarFxState();
            s.Step(BarFxPool.Energy, 10f, 100f, 1f, 0.1f, Tune);
            s.Step(BarFxPool.Energy, 0f, 100f, 1.1f, 0.1f, Tune);
            Assert.True(s.Exhaust);
            Assert.Equal(BarFxState.Changed.Exhaust, s.Step(BarFxPool.Plain, 0f, 100f, 1.2f, 0.1f, Tune));
            Assert.False(s.Exhaust);
            // back on: first sample only records (no stale delta)
            Assert.Equal(BarFxState.Changed.None, s.Step(BarFxPool.Blood, 50f, 100f, 2f, 0.1f, Tune));
        }
    }
}
