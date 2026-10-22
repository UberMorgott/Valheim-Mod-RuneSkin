using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuneSkin.CraftingScreen;
using RuneSkin.Diagnostics;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // Perf track (audit B1-B5): bounded tables, layered theme, theme comments, allocation-free amount parsing.
    public class PerfTests
    {
        private sealed class Obj
        {
            public bool Alive = true;
        }

        [Fact]
        public void PruneWaitsUntilTheTableReachedItsMark()
        {
            var table = new Dictionary<Obj, int>();
            for (var i = 0; i < 10; i++) table[new Obj { Alive = false }] = i;
            var next = 64;
            var scratch = new List<Obj>();
            Assert.Equal(0, Bounded.PruneWhenDoubled(table, o => o.Alive, ref next, scratch));
            Assert.Equal(10, table.Count);
            Assert.Equal(64, next);
        }

        [Fact]
        public void PruneDropsDeadKeysAndDoublesTheMark()
        {
            var table = new Dictionary<Obj, int>();
            for (var i = 0; i < 100; i++) table[new Obj { Alive = i < 40 }] = i;
            var next = 64;
            var scratch = new List<Obj>();
            Assert.Equal(60, Bounded.PruneWhenDoubled(table, o => o.Alive, ref next, scratch));
            Assert.Equal(40, table.Count);
            Assert.Equal(80, next);
            Assert.Empty(scratch);
        }

        [Fact]
        public void PruneKeepsTheMinimumMark()
        {
            var table = new Dictionary<Obj, int>();
            for (var i = 0; i < 70; i++) table[new Obj { Alive = i < 3 }] = i;
            var next = 64;
            Bounded.PruneWhenDoubled(table, o => o.Alive, ref next, new List<Obj>());
            Assert.Equal(3, table.Count);
            Assert.Equal(Bounded.MinPrune, next);
        }

        [Fact]
        public void TableStaysWithinTwiceTheLiveCountUnderChurn()
        {
            // A static table fed by objects that die (world reloads, damage numbers): bounded by 2 x live + the minimum.
            var table = new Dictionary<Obj, int>();
            var live = new Queue<Obj>();
            var next = 64;
            var scratch = new List<Obj>();
            var max = 0;
            for (var i = 0; i < 100000; i++)
            {
                var o = new Obj();
                live.Enqueue(o);
                if (live.Count > 50) live.Dequeue().Alive = false;
                Bounded.PruneWhenDoubled(table, x => x.Alive, ref next, scratch);
                table[o] = i;
                max = System.Math.Max(max, table.Count);
            }
            Assert.True(max <= 2 * 50 + Bounded.MinPrune, $"max {max}");
        }

        [Fact]
        public void ListPruneRemovesDeadRows()
        {
            var list = Enumerable.Range(0, 70).Select(i => new Obj { Alive = i % 2 == 0 }).ToList();
            var next = 64;
            Assert.Equal(35, Bounded.PruneWhenDoubled(list, o => !o.Alive, ref next));
            Assert.Equal(70, next);
        }

        [Fact]
        public void CappedSetStopsAtItsCap()
        {
            var set = new HashSet<string>();
            for (var i = 0; i < 10; i++) Assert.True(Bounded.AddCapped(set, "k" + i, 10));
            Assert.False(Bounded.AddCapped(set, "k10", 10));
            Assert.False(Bounded.AddCapped(set, "k0", 10));
            Assert.Equal(10, set.Count);
        }

        [Fact]
        public void CappedListKeepsTheNewest()
        {
            var list = new List<int>();
            for (var i = 0; i < 300; i++) Bounded.AddCapped(list, i, 256);
            Assert.Equal(256, list.Count);
            Assert.Equal(44, list[0]);
            Assert.Equal(299, list[list.Count - 1]);
        }

        [Theory]
        [InlineData("3/5", true, 3, 5)]
        [InlineData(" 12 / 40 ", true, 12, 40)]
        [InlineData("0/1", true, 0, 1)]
        [InlineData("5", false, 0, 0)]
        [InlineData("", false, 0, 0)]
        [InlineData(null, false, 0, 0)]
        [InlineData("/5", false, 0, 0)]
        [InlineData("3/", false, 0, 0)]
        [InlineData("3/5x", false, 0, 0)]
        [InlineData("<color=red>3</color>/5", false, 0, 0)]
        [InlineData("1234567890/1", false, 0, 0)]
        public void HaveNeedParsesWithoutSubstrings(string text, bool ok, int have, int need)
        {
            Assert.Equal(ok, CraftingMath.TryParseHaveNeed(text, out var h, out var n));
            if (!ok) return;
            Assert.Equal(have, h);
            Assert.Equal(need, n);
        }

        private static ThemeFile Embedded()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        [Fact]
        public void ThemeAcceptsComments()
        {
            var f = ThemeJson.Parse("{ // a theme\n \"schema\": 1, /* block\n comment */ \"name\": \"x\", \"colors\": [ { \"token\": \"color.a\", \"value\": \"#112233\" } // trailing\n ] }");
            Assert.Equal(1, f.schema);
            Assert.Equal("x", f.name);
            Assert.Single(f.colors);
        }

        [Fact]
        public void CommentInsideAStringIsText()
        {
            var f = ThemeJson.Parse("{ \"schema\": 1, \"name\": \"a // b /* c */\" }");
            Assert.Equal("a // b /* c */", f.name);
        }

        [Fact]
        public void UnterminatedBlockCommentFails()
        {
            Assert.Throws<System.FormatException>(() => ThemeJson.Parse("{ \"schema\": 1 /* open"));
        }

        [Fact]
        public void PartialOverrideMergesOverTheShippedTheme()
        {
            var baseFile = Embedded();
            var color = baseFile.colors[0];
            var layer = ThemeJson.Parse("{ \"colors\": [ { \"token\": \"" + color.token + "\", \"value\": \"#010203\" }, { \"token\": \"color.perf.new\", \"value\": \"#ABCDEF\" } ], " +
                                        "\"floats\": [ { \"token\": \"size.perf.new\", \"value\": 7 } ] }");
            var merged = ThemeMerge.Merge(baseFile, layer);
            Assert.Empty(ThemeSchema.Validate(merged));
            Assert.Equal(baseFile.name, merged.name);
            Assert.Equal(baseFile.colors.Length + 1, merged.colors.Length);
            Assert.Equal("#010203", merged.colors.First(c => c.token == color.token).value);
            Assert.Equal(color.token, merged.colors[0].token); // replaced in place
            Assert.Equal("color.perf.new", merged.colors[merged.colors.Length - 1].token);
            Assert.Equal(baseFile.floats.Length + 1, merged.floats.Length);
            Assert.Equal(baseFile.roles.Length, merged.roles.Length);
            Assert.Same(baseFile.sprites, merged.sprites);
        }

        [Fact]
        public void NewSpriteRuleGoesInFrontReplacedRuleKeepsItsPlace()
        {
            var baseFile = Embedded();
            var first = baseFile.spriteRoles[0];
            var role = baseFile.roles.First(r => r.role == "content").role;
            var layer = new ThemeFile
            {
                spriteRoles = new[]
                {
                    new SpriteRoleEntry { match = "perf_new_sprite", role = role },
                    new SpriteRoleEntry { match = first.match, role = "skip", context = first.context },
                },
            };
            var merged = ThemeMerge.Merge(baseFile, layer);
            Assert.Empty(ThemeSchema.Validate(merged));
            Assert.Equal("perf_new_sprite", merged.spriteRoles[0].match);
            Assert.Equal(first.match, merged.spriteRoles[1].match);
            Assert.Equal("skip", merged.spriteRoles[1].role);
            Assert.Equal(baseFile.spriteRoles.Length + 1, merged.spriteRoles.Length);
        }

        [Fact]
        public void InvalidLayerResultIsRejectedByTheSchema()
        {
            var baseFile = Embedded();
            var layer = ThemeJson.Parse("{ \"roles\": [ { \"role\": \"content\", \"color\": \"color.does.not.exist\" } ] }");
            Assert.NotEmpty(ThemeSchema.Validate(ThemeMerge.Merge(baseFile, layer)));
        }

        [Fact]
        public void LastEntryOfALayerWinsForADuplicateKey()
        {
            var baseFile = Embedded();
            var t = baseFile.floats[0].token;
            var layer = ThemeJson.Parse("{ \"floats\": [ { \"token\": \"" + t + "\", \"value\": 1 }, { \"token\": \"" + t + "\", \"value\": 2 } ] }");
            var merged = ThemeMerge.Merge(baseFile, layer);
            Assert.Equal(2f, merged.floats.First(f => f.token == t).value);
            Assert.Equal(baseFile.floats.Length, merged.floats.Length);
        }

        [Fact]
        public void LayerOverNothingIsTheLayer()
        {
            var layer = new ThemeFile { schema = 1, name = "only" };
            Assert.Same(layer, ThemeMerge.Merge(null, layer));
        }
    }
}
