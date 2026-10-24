using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // Party frame 0.20.3 (PoE style, mockup v3 column B): grouping, aggregation, respawn sweep, state mapping, grid wrap.
    public class PartyFrameTests
    {
        private const int Warrior = 11, Archer = 22, Wolf = 33;

        private static PartyUnit U(int prefab, SummonState state, float hp = 100f, float max = 100f, float es = 0f, float esCur = 0f,
            byte flags = 0, bool pet = false, double cd = 0d, int id = 0) =>
            new PartyUnit { Id = id, Prefab = prefab, State = state, HpCur = hp, HpMax = max, Hp = max > 0f ? hp / max : 0f, Es = es, EsCur = esCur, Flags = flags, Pet = pet, CdUntil = cd };

        [Fact]
        public void Groups_by_type_and_state_class_in_first_seen_type_order()
        {
            var units = new List<PartyUnit>
            {
                U(Archer, SummonState.Dead, cd: 50),
                U(Warrior, SummonState.Alive),
                U(Archer, SummonState.Alive),
                U(Warrior, SummonState.Alive),
                U(Archer, SummonState.Spawning),
                U(Archer, SummonState.Waiting),
                U(Archer, SummonState.NoReserve),
                U(Archer, SummonState.Off),
            };
            var s = PartyMath.Group(units);
            Assert.Equal(new[] { Archer, Archer, Archer, Archer, Archer, Warrior }, s.Select(x => x.Prefab));
            Assert.Equal(new[] { SummonState.Alive, SummonState.Waiting, SummonState.NoReserve, SummonState.Off, SummonState.Dead, SummonState.Alive }, s.Select(x => x.State));
            Assert.Equal(2, s[1].Count); // spawning + waiting share one stack
            Assert.Equal(2, s[5].Count);
        }

        [Fact]
        public void Pets_are_their_own_stack_after_summons()
        {
            var s = PartyMath.Group(new[] { U(Wolf, SummonState.Alive, pet: true), U(Wolf, SummonState.Alive), U(Warrior, SummonState.Alive) });
            Assert.Equal(3, s.Count);
            Assert.Equal(new[] { false, false, true }, s.Select(x => x.Pet));
            Assert.Equal(new[] { Wolf, Warrior, Wolf }, s.Select(x => x.Prefab));
        }

        [Fact]
        public void Aggregates_health_shield_weakest_and_badges()
        {
            var s = PartyMath.Group(new[]
            {
                U(Warrior, SummonState.Alive, 190, 200, es: 0.5f, esCur: 60, flags: PartyMath.FlagDefensive),
                U(Warrior, SummonState.Alive, 132, 200, es: 1f, esCur: 40),
                U(Warrior, SummonState.Alive, 161, 200, es: 0f, esCur: 0, flags: PartyMath.FlagStay),
            }).Single();
            Assert.Equal(3, s.Count);
            Assert.Equal(483f, s.HpCur);
            Assert.Equal(600f, s.HpMax);
            Assert.Equal(483f / 600f, s.Hp, 4);
            Assert.Equal(100f, s.EsCur);
            Assert.Equal(0.5f, s.Es, 4);
            Assert.Equal(132f, s.Weakest);
            Assert.True(s.Stay);
            Assert.Equal(1, s.Mode);
        }

        [Fact]
        public void Bodyless_stacks_have_no_fill_and_soonest_respawn()
        {
            var s = PartyMath.Group(new[] { U(Warrior, SummonState.Dead, cd: 120, id: 1), U(Warrior, SummonState.Dead, cd: 90, id: 2) }).Single();
            Assert.Equal(0f, s.Hp);
            Assert.Equal(0f, s.Es);
            Assert.Equal(-1f, s.Weakest);
            Assert.Equal(90d, s.NextCd);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(PartyMath.FlagStay, 0)]
        [InlineData(PartyMath.FlagDefensive, 1)]
        [InlineData(PartyMath.FlagAggressive | PartyMath.FlagStay, 2)]
        [InlineData(PartyMath.FlagAggressive | PartyMath.FlagDefensive, 2)]
        public void Mode_from_flags(byte flags, int mode) => Assert.Equal(mode, PartyMath.Mode(flags));

        [Theory]
        [InlineData(SummonState.Spawning, SummonState.Waiting)]
        [InlineData(SummonState.Waiting, SummonState.Waiting)]
        [InlineData(SummonState.Alive, SummonState.Alive)]
        [InlineData(SummonState.Dead, SummonState.Dead)]
        [InlineData(SummonState.Off, SummonState.Off)]
        [InlineData(SummonState.NoReserve, SummonState.NoReserve)]
        public void State_class(SummonState s, SummonState cls) => Assert.Equal(cls, PartyMath.StateClass(s));

        [Fact]
        public void Only_alive_has_a_body() =>
            Assert.Equal(new[] { SummonState.Alive }, new[] { SummonState.Spawning, SummonState.Alive, SummonState.Dead, SummonState.Off, SummonState.NoReserve, SummonState.Waiting }.Where(PartyMath.HasBody));

        [Theory]
        [InlineData(15d, 30d, 0.5f)]
        [InlineData(30d, 30d, 1f)]
        [InlineData(45d, 30d, 1f)]
        [InlineData(0d, 30d, 0f)]
        [InlineData(-3d, 30d, 0f)]
        [InlineData(5d, 0d, 0f)]
        public void Sweep_fraction(double remaining, double total, float want) => Assert.Equal(want, PartyMath.SweepFraction(remaining, total), 4);

        [Fact]
        public void Sweep_total_is_at_least_the_configured_respawn()
        {
            Assert.Equal(30d, PartyMath.SweepTotal(12d, 30d));
            Assert.Equal(45d, PartyMath.SweepTotal(45d, 30d));
        }

        [Theory]
        [InlineData(45d, "0:45")]
        [InlineData(44.2d, "0:45")]
        [InlineData(75d, "1:15")]
        [InlineData(0d, "0:00")]
        [InlineData(-2d, "0:00")]
        public void Timer_text(double remaining, string want) => Assert.Equal(want, PartyMath.Timer(remaining));

        [Fact]
        public void Layout_wraps_after_nine_with_mockup_pitch()
        {
            var h = PartyMath.BlockHeight(44f, 3f, 4f);
            Assert.Equal(54f, h);
            PartyMath.Cell(0, 9, 44f, 6f, h, 6f, out var x0, out var y0);
            PartyMath.Cell(8, 9, 44f, 6f, h, 6f, out var x8, out var y8);
            PartyMath.Cell(9, 9, 44f, 6f, h, 6f, out var x9, out var y9);
            Assert.Equal((0f, 0f), (x0, y0));
            Assert.Equal((400f, 0f), (x8, y8));
            Assert.Equal((0f, 60f), (x9, y9));
            PartyMath.GridSize(3, 9, 44f, 6f, h, 6f, out var w3, out var h3);
            Assert.Equal((144f, 54f), (w3, h3)); // mockup v3-poe-3: 144x54
            PartyMath.GridSize(11, 9, 44f, 6f, h, 6f, out var w11, out var h11);
            Assert.Equal((444f, 114f), (w11, h11)); // mockup v3-poe-40 (11 stacks): 444x114
            PartyMath.GridSize(0, 9, 44f, 6f, h, 6f, out var w0, out var h0);
            Assert.Equal((0f, 0f), (w0, h0));
            Assert.Equal(5, PartyMath.Rows(45, 9));
        }

        [Fact]
        public void Forty_summons_fit_above_the_chat_at_1080p()
        {
            // Worst case: every stack distinct, capped at SummonFrames.MaxStacks (45) = 5 rows from y 244.
            PartyMath.GridSize(45, 9, 44f, 6f, 54f, 6f, out var w, out var h);
            Assert.True(244f + h <= 600f, $"bottom {244f + h}");
            Assert.True(16f + w <= 480f, $"right {16f + w}");
        }

        [Fact]
        public void Theme_has_the_party_block()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            var text = r.ReadToEnd();
            foreach (var t in new[] { "size.hud.party.x", "size.hud.party.y", "size.hud.party.tile", "count.hud.party.columns", "time.hud.party.respawn" })
                Assert.Contains($"\"{t}\"", text);
        }
    }
}
