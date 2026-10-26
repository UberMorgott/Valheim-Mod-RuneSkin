using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using Xunit;

namespace RuneSkin.Tests
{
    // Bottom panel (mockup bottom-hud-center-v2, user approved 2026-10-26): column slices of the mockup's food 3 /
    // potion 4 row laid edge to edge (StripMath.Compute), cells on the art's wells, bars on its troughs, resource row
    // split, shield perimeter, centre gauges. Geometry from the shipped theme (size.hud.panel.*).
    public class StripLayoutTests
    {
        internal static readonly Dictionary<string, float> Floats = LoadFloats();
        private static readonly Dictionary<string, string> Sprites = LoadSprites();

        private static Dictionary<string, float> LoadFloats()
        {
            var f = ParseTheme();
            return f.floats.ToDictionary(x => x.token, x => x.value);
        }

        private static Dictionary<string, string> LoadSprites() => ParseTheme().sprites.ToDictionary(x => x.token, x => x.asset);

        private static ThemeFile ParseTheme()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("RuneSkin.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        private static float P(string name) => Floats["size.hud.panel." + name];

        // Units per art px: at 1080 the slot is 70 px = 85 units, so the panel draws 1:1 with the mockup.
        private const float SlotPx1080 = 70f;

        private static StripMath.WingArt Wing(string side) => new StripMath.WingArt
        {
            InnerW = P(side + ".in.w"),
            SegW = P(side + ".seg.w"),
            EndW = P(side + ".end.w"),
            SlotY = P(side + ".slot.y"),
            EndJoint = Floats.TryGetValue("size.hud.panel." + side + ".end.joint", out var j) ? j : 0f,
            Arts = (int)Floats["count.hud.panel." + side + ".arts"]
        };

        internal static StripMath.Input In(int abilities = 1, int food = 3, int potions = 4, float width = 1920f * 85f / SlotPx1080) => new StripMath.Input
        {
            Width = width,
            Unit = P("unit"),
            H = P("h"),
            EndLW = P("block.l.w"),
            HotbarW = P("hotbar.w"),
            AbilityW = P("ability.w"),
            EndRW = P("block.r.w"),
            Cell = Floats["size.hud.strip.cell"],
            CellH = Floats["size.hud.strip.cell"],
            AbilityCell = Floats["size.hud.strip.ability.cell"],
            HotbarC0 = P("hotbar.c0"),
            HotbarPitch = P("hotbar.pitch"),
            AbilityC = P("ability.c"),
            CellsCY = P("cells.cy"),
            BarsL = P("bars.l"),
            BarsR = P("bars.r"),
            BarsB = P("bars.b"),
            BarsT = P("bars.t"),
            WingTop = P("wing.top"),
            FoodArt = Wing("food"),
            PotionArt = Wing("potion"),
            FoodCell = Floats["size.hud.strip.food.size"],
            PotionCell = Floats["size.hud.strip.potion.size"],
            WingMax = (int)Floats["count.hud.strip.wing.max"],
            Hotbar = 8,
            Abilities = abilities,
            Food = food,
            Potions = potions
        };

        // art px from the strip middle at 1080 (1 art px = 1 screen px there)
        private static float Px(float units, in StripMath.Input i) => (units - i.Width / 2f) / i.Unit;

        // The row slices of panel_row.png (2x sprite px, cuts.json panel_*): food end 24 | segs 236 / 401 / 566 | inner
        // 731 | block 774 .. 2388 (hotbar 868, ability 2114, right end 2284) | potion inner | segs 2447 .. 2955 | end 3136.
        // Centre block midpoint = sheet x 1581 (mockup x 960.5).
        [Fact]
        public void Food3Potion4_IsTheMockupRow()
        {
            var i = In();
            var l = StripMath.Compute(i);
            Assert.True(l.Fits);
            Assert.Equal(3, l.FoodShown);
            Assert.Equal(4, l.PotionShown);
            float Sheet(float units) => 1581f + 2f * Px(units, i);
            Assert.Equal(774f, Sheet(l.CenterX), 1);
            Assert.Equal(868f, Sheet(l.Hotbar.X), 1);
            Assert.Equal(2114f, Sheet(l.AbilityX0), 1);
            Assert.Equal(2284f, Sheet(l.EndR.X), 1);
            Assert.Equal(2388f, Sheet(l.CenterX + l.CenterW), 1);
            Assert.Equal(731f, Sheet(l.WingL.Inner.X), 1);
            Assert.Equal(566f, Sheet(l.WingL.Seg(0).X), 1);
            Assert.Equal(236f, Sheet(l.WingL.Seg(2).X), 1);
            Assert.Equal(24f, Sheet(l.WingL.End.X), 1);
            Assert.Equal(2447f, Sheet(l.WingR.Seg(0).X), 1);
            Assert.Equal(2955f, Sheet(l.WingR.End.X), 1);
            // the right end = panel_food_end mirrored + the food wing's socket margin (user 2026-10-26): 233 sheet px wide
            Assert.Equal(3188f, Sheet(l.WingR.End.Right), 1);
            // pieces butt edge to edge
            Assert.Equal(l.WingL.Inner.Right, l.CenterX, 3);
            Assert.Equal(l.WingL.Seg(0).Right, l.WingL.Inner.X, 3);
            Assert.Equal(l.WingL.End.Right, l.WingL.Seg(2).X, 3);
            Assert.Equal(l.WingR.Inner.X, l.CenterX + l.CenterW, 3);
            Assert.Equal(l.WingR.Seg(0).X, l.WingR.Inner.Right, 3);
            Assert.Equal(l.WingR.End.X, l.WingR.Seg(3).Right, 3);
            Assert.Equal(l.DrawnLeft, l.WingL.End.X, 3);
            Assert.Equal(l.DrawnRight, l.WingR.End.Right, 3);
        }

        // Cells on the art's wells / sockets (measured on panel_row.png, sheet px): hotbar well centres 946 + k x 155.57,
        // F well 2199, food sockets 318 / 485 / 648 (y 240), potion wells 2510 / 2638 / 2766 / 2892 (y 249).
        [Fact]
        public void CellsSitOnTheArtWells()
        {
            var i = In();
            var l = StripMath.Compute(i);
            float Sheet(float units) => 1581f + 2f * Px(units, i);
            for (var k = 0; k < 8; k++)
                Assert.InRange(Sheet(StripMath.CellX(l.HotbarX, k, i.Cell, l.Gap) + i.Cell / 2f), 946f + k * 155.57f - 2f, 946f + k * 155.57f + 2f);
            Assert.InRange(Sheet(l.AbilityLeft(0, i.AbilityCell) + i.AbilityCell / 2f), 2197f, 2201f);
            var foods = new[] { 318f, 485f, 648f };
            for (var k = 0; k < 3; k++) Assert.InRange(Sheet(l.FoodLeft(k) + l.FoodCell / 2f), foods[k] - 3f, foods[k] + 3f);
            var pots = new[] { 2510f, 2638f, 2766f, 2892f };
            for (var k = 0; k < 4; k++) Assert.InRange(Sheet(l.PotionLeft(k) + l.PotionCell / 2f), pots[k] - 2f, pots[k] + 2f);
            // y (sheet rows from the top, the slices start at row 22, 324 rows = H art px)
            float Row(float units) => 22f + 2f * (i.H - units / i.Unit);
            Assert.InRange(Row(l.CellsY + i.CellH / 2f), 128f, 131f);
            Assert.InRange(Row(l.FoodY + l.FoodCell / 2f), 238f, 242f);
            Assert.InRange(Row(l.PotionY + l.PotionCell / 2f), 247f, 251f);
            // hotbar cell = the inventory slot; the gap fills the art pitch
            Assert.Equal(i.HotbarPitch * i.Unit, i.Cell + l.Gap, 3);
            // bars on the troughs: HP top row 220, resource bottom row 322
            Assert.Equal(220f, Row(l.BarsY + l.BarsH), 1);
            Assert.Equal(322f, Row(l.BarsY), 1);
            Assert.Equal(922f, Sheet(l.BarsX), 1);
            Assert.Equal(2252f, Sheet(l.BarsX + l.BarsW), 1);
        }

        // Each wing starts at the centre's edge and grows outward with its count; nothing drawn for 0.
        [Fact]
        public void WingsGrowOutwardFromTheCentre()
        {
            StripMath.Layout? prev = null;
            for (var n = 0; n <= 4; n++)
            {
                var i = In(food: n, potions: n);
                var l = StripMath.Compute(i);
                Assert.Equal(n, l.FoodShown);
                Assert.Equal(n, l.PotionShown);
                if (n == 0)
                {
                    Assert.Equal(l.CenterX, l.DrawnLeft, 3);
                    Assert.Equal(l.CenterX + l.CenterW, l.DrawnRight, 3);
                    Assert.Equal(0, l.WingL.Slots);
                }
                else
                {
                    Assert.Equal(l.CenterX, l.WingL.Right, 3);
                    Assert.Equal(l.CenterX + l.CenterW, l.WingR.X, 3);
                    if (prev is StripMath.Layout p && p.FoodShown > 0)
                    {
                        Assert.Equal(p.WingL.X - i.FoodArt.SegW * i.Unit, l.WingL.X, 2);
                        Assert.Equal(p.WingR.Right + i.PotionArt.SegW * i.Unit, l.WingR.Right, 2);
                        Assert.Equal(p.WingL.Inner.X, l.WingL.Inner.X, 3); // the inner end never moves
                    }
                    // reading order: food 0 leftmost (outermost), potion 0 innermost
                    for (var k = 1; k < n; k++)
                    {
                        Assert.True(l.FoodLeft(k) > l.FoodLeft(k - 1));
                        Assert.True(l.PotionLeft(k) > l.PotionLeft(k - 1));
                    }
                    Assert.Equal(l.WingL.SlotCenter(0), l.FoodLeft(n - 1) + l.FoodCell / 2f, 3);
                    Assert.Equal(l.WingR.SlotCenter(0), l.PotionLeft(0) + l.PotionCell / 2f, 3);
                }
                prev = l;
            }
        }

        // Segment art per slot: innermost and outermost keep their row slices, the ones between cycle the middles.
        [Fact]
        public void SegmentArtFollowsTheRow()
        {
            Assert.Equal(new[] { 0 }, Enumerable.Range(0, 1).Select(k => StripMath.SegArt(k, 1, 3)));
            Assert.Equal(new[] { 0, 2 }, Enumerable.Range(0, 2).Select(k => StripMath.SegArt(k, 2, 3)));
            Assert.Equal(new[] { 0, 1, 2 }, Enumerable.Range(0, 3).Select(k => StripMath.SegArt(k, 3, 3)));
            Assert.Equal(new[] { 0, 1, 1, 2 }, Enumerable.Range(0, 4).Select(k => StripMath.SegArt(k, 4, 3)));
            Assert.Equal(new[] { 0, 1, 2, 3 }, Enumerable.Range(0, 4).Select(k => StripMath.SegArt(k, 4, 4)));
            Assert.Equal(new[] { 0, 1, 3 }, Enumerable.Range(0, 3).Select(k => StripMath.SegArt(k, 3, 4)));
            Assert.Equal(new[] { 0, 1, 2, 1, 3 }, Enumerable.Range(0, 5).Select(k => StripMath.SegArt(k, 5, 4)));
            Assert.Equal(0, StripMath.SegArt(0, 1, 1));
        }

        // The centre closes its width around the live ability count (one ability slice each); the hotbar never moves
        // relative to the centre's left end.
        [Fact]
        public void CentreClosesAroundTheAbilities()
        {
            var a0 = StripMath.Compute(In(abilities: 0));
            var a1 = StripMath.Compute(In(abilities: 1));
            var a2 = StripMath.Compute(In(abilities: 2));
            var w = P("ability.w") * P("unit");
            Assert.Equal(a0.CenterW + w, a1.CenterW, 3);
            Assert.Equal(a1.CenterW + w, a2.CenterW, 3);
            Assert.Equal(a1.HotbarX - a1.CenterX, a2.HotbarX - a2.CenterX, 3);
            Assert.Equal(a2.AbilityLeft(0, 90f) + w, a2.AbilityLeft(1, 90f), 3);
            Assert.Equal(a1.BarsW + w, a2.BarsW, 3);
        }

        // Counts clip to what fits inside the strip (end medallion included) and to the wing cap.
        [Fact]
        public void CountsClipToTheStrip()
        {
            var tight = In(food: 4, potions: 4, width: 1300f * 85f / SlotPx1080);
            var l = StripMath.Compute(tight);
            Assert.False(l.Fits);
            Assert.True(l.FoodShown < 4 || l.PotionShown < 4);
            Assert.True(l.DrawnLeft >= -0.01f);
            Assert.True(l.DrawnRight <= tight.Width + 0.01f);
            var capped = StripMath.Compute(In(food: 9, potions: 9, width: 4000f));
            Assert.Equal((int)Floats["count.hud.strip.wing.max"], capped.FoodShown);
        }

        // 4 foods (MorgottTweaks 4th slot) and 4 potions fit every 16:9 / 16:10 / 21:9 strip.
        [Theory]
        [InlineData(1280, 720)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        [InlineData(1920, 1200)]
        [InlineData(3440, 1440)]
        public void FourAndFourFitEveryAspect(int w, int h)
        {
            var canvas = Math.Min(w / 1920f, h / 1080f);
            var slot = StripMath.SlotPx(70f, 1f, canvas);
            var unit = StripMath.UnitPx(slot, 85f);
            var box = Math.Min(w, h * 2.2f);
            var l = StripMath.Compute(In(food: 4, potions: 4, width: StripMath.WidthPx(box, 0.96f) / unit));
            Assert.True(l.Fits, $"{w}x{h}: food {l.FoodShown} potions {l.PotionShown}");
            var share = l.Height * unit / h;
            Assert.InRange(share, 0.1f, 0.2f);
        }

        // Theme geometry = the shipped slices (sprites authored 2x).
        [Fact]
        public void ThemeMatchesTheSlices()
        {
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "art", "sprites", "sprites.json")));
            var size = json.RootElement.GetProperty("sprites").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => (e.GetProperty("size")[0].GetSingle(), e.GetProperty("size")[1].GetSingle()));
            void Is(string token, float w)
            {
                var asset = Sprites["sprite.hud.panel." + token];
                Assert.Equal((w + 2f * P("lap")) * 2f, size[asset].Item1, 1); // + the overlap into both neighbours
                Assert.Equal(P("h") * 2f, size[asset].Item2, 1);
            }
            Is("block.l", P("block.l.w"));
            Is("hotbar", P("hotbar.w"));
            Is("ability", P("ability.w"));
            Is("block.r", P("block.r.w"));
            Is("food.in", P("food.in.w"));
            Is("food.end", P("food.end.w"));
            Is("potion.in", P("potion.in.w"));
            Is("potion.end", P("potion.end.w"));
            for (var k = 0; k < Floats["count.hud.panel.food.arts"]; k++) Is("food.seg" + k, P("food.seg.w"));
            for (var k = 0; k < Floats["count.hud.panel.potion.arts"]; k++) Is("potion.seg" + k, P("potion.seg.w"));
        }

        // Right end = mirror of the left (user 2026-10-26: the row's own right medallion sat over the last potion cell, its
        // strap under it): for any slot count the end never overlaps a cell and its strap is as far from the last potion
        // cell as the left strap from the outermost food cell (1 screen px at 1080 = 1 art px).
        [Theory]
        [InlineData(1, 1)]
        [InlineData(3, 4)]
        [InlineData(4, 4)]
        [InlineData(3, 2)]
        [InlineData(1, 4)]
        public void EndsMirrorEachOther(int food, int potions)
        {
            var i = In(food: food, potions: potions);
            var l = StripMath.Compute(i);
            Assert.True(l.Fits);
            for (var k = 0; k < l.PotionShown; k++) Assert.True(l.WingR.End.X >= l.PotionLeft(k) + l.PotionCell - 0.001f, $"end over potion {k}");
            for (var k = 0; k < l.FoodShown; k++) Assert.True(l.WingL.End.Right <= l.FoodLeft(k) + 0.001f, $"end over food {k}");
            Assert.True(l.JointL > 0f);
            Assert.True(Math.Abs(l.JointL - l.JointR) <= i.Unit, $"joint L {l.JointL} R {l.JointR}"); // within 1 art px
        }

        // Wing slots = capacity, empties included (user 2026-10-26): 0 eaten -> 3 sockets, MorgottTweaks 4th slot -> 4,
        // never fewer than the filled ones.
        [Theory]
        [InlineData(3, 0, 3)]
        [InlineData(3, 2, 3)]
        [InlineData(4, 0, 4)]
        [InlineData(4, 4, 4)]
        [InlineData(3, 4, 4)]  // capacity shrank under eaten foods
        [InlineData(0, 0, 0)]  // potion wing without quick slots
        [InlineData(-1, 0, 0)]
        public void WingSlotsAreTheCapacity(int capacity, int filled, int want) =>
            Assert.Equal(want, StripMath.WingSlots(capacity, filled));

        [Fact]
        public void NoFoodEatenStillDrawsThreeSockets()
        {
            var l = StripMath.Compute(In(food: StripMath.WingSlots(StripMath.VanillaFoodSlots, 0)));
            Assert.Equal(3, l.FoodShown);
            Assert.True(l.WingL.Slots == 3 && l.WingL.X < l.CenterX);
        }

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "RuneSkin.sln"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        [Theory]
        [InlineData(1280, 720)]
        [InlineData(1366, 768)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void HotbarCellIsTheInventorySlotAtEveryResolution(int w, int h)
        {
            var canvas = Math.Min(w / 1920f, h / 1080f);
            var slot = StripMath.SlotPx(70f, 1f, canvas);
            var unit = StripMath.UnitPx(slot, 85f);
            Assert.Equal(70f * canvas, 85f * unit, 3); // hotbar cell px = inventory grid cell px (InventoryMath: GridCell x k)
            // 1:1 with the mockup at 1080: one art px = one screen px
            if (h == 1080) Assert.Equal(1f, P("unit") * unit, 3);
        }

        [Fact]
        public void SkinScaleMovesBothTogether()
        {
            Assert.Equal(70f * 1.25f, StripMath.SlotPx(70f, 1.25f, 1f), 3);
            Assert.Equal(35f, StripMath.SlotPx(70f, 0.1f, 1f), 3); // InventoryMath's 0.5..2 clamp
            Assert.Equal(1f, StripMath.UnitPx(0f, 78f));
        }

        [Fact]
        public void FontFloorRaisesSmallTextOnly()
        {
            Assert.Equal(26f, StripMath.FontFloor(26f, 14f, 0.8f), 3);
            Assert.Equal(14f / 0.5f, StripMath.FontFloor(24f, 14f, 0.5f), 3);
        }

        [Fact]
        public void StatusRowsFitBesideTheCentre()
        {
            Assert.Equal(10, StripMath.StatusPerRow(10, 42f, 16f, 1000f));
            Assert.Equal(6, StripMath.StatusPerRow(10, 42f, 16f, 6 * 42f + 5 * 16f + 1f));
            Assert.Equal(1, StripMath.StatusPerRow(10, 42f, 16f, 10f));
            var l = StripMath.Compute(In());
            Assert.Equal(l.CenterX, l.StatusL, 3);
            Assert.Equal(l.CenterX + l.CenterW, l.StatusR, 3);
            Assert.True(l.WingH > l.BarsY + l.BarsH); // status rows start above the wings
        }

        [Fact]
        public void ResourceRowSplitsAmongPresentEntries()
        {
            var x = new float[3];
            var w = new float[3];
            // early game: stamina alone, full width
            Assert.Equal(1f, StripMath.RowSplit(700f, 18f, new[] { 1f, 0f, 0f }, 3, x, w));
            Assert.Equal(700f, w[0], 3);
            Assert.Equal(0f, w[1]);
            // eitr eaten: halves with one gap
            StripMath.RowSplit(700f, 18f, new[] { 1f, 1f, 0f }, 3, x, w);
            Assert.Equal((700f - 18f) / 2f, w[0], 3);
            Assert.Equal(w[0], w[1], 3);
            Assert.Equal(w[0] + 18f, x[1], 3);
            // three entries
            StripMath.RowSplit(700f, 18f, new[] { 1f, 1f, 1f }, 3, x, w);
            Assert.Equal((700f - 36f) / 3f, w[2], 3);
            Assert.Equal(700f, x[2] + w[2], 3);
            // sliding in: stamina shrinks monotonically while eitr grows
            var prev = 701f;
            for (var t = 0f; t <= 1f; t += 0.1f)
            {
                StripMath.RowSplit(700f, 18f, new[] { 1f, t, 0f }, 3, x, w);
                Assert.True(w[0] <= prev + 0.001f);
                Assert.True(x[1] + w[1] <= 700f + 0.01f);
                prev = w[0];
            }
            // nothing present: empty row
            Assert.Equal(0f, StripMath.RowSplit(700f, 18f, new[] { 0f, 0f, 0f }, 3, x, w));
        }

        [Fact]
        public void ShieldRunsFromTheLeftCap()
        {
            Assert.Equal(0f, StripMath.ShieldRun(0f, 150f, out var l0, out var r0));
            Assert.Equal(0f, l0);
            Assert.Equal(0.5f, StripMath.ShieldRun(75f, 150f, out var l1, out var r1), 3);
            Assert.Equal(1f, l1);
            Assert.Equal(0f, r1);
            Assert.Equal(1f, StripMath.ShieldRun(300f, 150f, out _, out var r2));
            Assert.Equal(1f, r2);
            Assert.Equal(0f, r0);
        }

        [Fact]
        public void GaugeFadesInOnChangeAndOutAfterIdle()
        {
            var g = new GaugeState();
            Assert.True(g.Step(0.3f, false, 0f, 0.1f, 1.5f, 0.25f));
            Assert.True(g.Alpha > 0f);
            for (var t = 0.1f; t < 1f; t += 0.1f) g.Step(0.3f, false, t, 0.1f, 1.5f, 0.25f);
            Assert.Equal(1f, g.Alpha);
            // idle past the delay: hides and fades out
            Assert.False(g.Step(0.3f, false, 2f, 0.1f, 1.5f, 0.25f));
            for (var t = 2.1f; t < 3f; t += 0.1f) g.Step(0.3f, false, t, 0.1f, 1.5f, 0.25f);
            Assert.Equal(0f, g.Alpha);
            // held (full rage / staggered) stays regardless of idle
            Assert.True(g.Step(1f, true, 10f, 0.1f, 1.5f, 0.25f));
            Assert.True(g.Step(1f, true, 20f, 0.1f, 1.5f, 0.25f));
            // zero never shows
            var z = new GaugeState();
            Assert.False(z.Step(0f, false, 0f, 0.1f, 1.5f, 0.25f));
        }
    }
}
