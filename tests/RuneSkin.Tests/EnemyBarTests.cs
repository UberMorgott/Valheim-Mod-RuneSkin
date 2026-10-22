using RuneSkin.WorldHud;
using Xunit;
using R = RuneSkin.WorldHud.EnemyHudMath.Reason;
using P = RuneSkin.WorldHud.EnemyHudMath.StaggerPhase;

namespace RuneSkin.Tests
{
    // hud-design spec 1 (visibility) and 5 (stagger): pure EnemyHudMath rules.
    public class EnemyBarTests
    {
        [Fact]
        public void AimedBeatsEngagedBeatsThreat()
        {
            var aimed = EnemyHudMath.Score(0f, 1.5f, -1f, 8f, false, 30f, 15f, false, out var ra);
            var engaged = EnemyHudMath.Score(-1f, 1.5f, 0f, 8f, true, 3f, 15f, true, out var re);
            var threat = EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 2f, 15f, true, out var rt);
            Assert.Equal(R.Aimed, ra);
            Assert.Equal(R.Engaged, re);
            Assert.Equal(R.Threat, rt);
            Assert.True(aimed > engaged && engaged > threat);
        }

        [Theory]
        [InlineData(1.4f, R.Aimed)]   // inside the 1.5 s linger
        [InlineData(1.6f, R.None)]    // linger over
        public void AimLinger(float since, R expected)
        {
            EnemyHudMath.Score(since, 1.5f, -1f, 8f, false, 5f, 15f, false, out var r);
            Assert.Equal(expected, r);
        }

        [Theory]
        [InlineData(7.9f, R.Engaged)]
        [InlineData(8.1f, R.None)]
        public void EngagedWindow(float since, R expected)
        {
            EnemyHudMath.Score(-1f, 1.5f, since, 8f, false, 12f, 15f, false, out var r);
            Assert.Equal(expected, r);
        }

        [Fact]
        public void ThreatNeedsAlertAndRange()
        {
            EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, false, 5f, 15f, false, out var notAlerted);
            EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 16f, 15f, false, out var far);
            Assert.Equal(R.None, notAlerted);
            Assert.Equal(R.None, far);
            var near = EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 4f, 15f, false, out _);
            var farther = EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 9f, 15f, false, out _);
            var targeting = EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 9f, 15f, true, out _);
            Assert.True(near > farther);
            Assert.True(targeting > near);
        }

        [Fact]
        public void PickRespectsCapAndThreatCap()
        {
            // 8 alerted threats + 1 aimed + 1 engaged: cap 4, threats 2 -> aimed, engaged, 2 nearest threats.
            var scores = new float[16];
            var reasons = new R[16];
            var ids = new int[16];
            var picked = new bool[16];
            var n = 0;
            for (var i = 0; i < 8; i++)
            {
                scores[n] = EnemyHudMath.Score(-1f, 1.5f, -1f, 8f, true, 3f + i, 15f, false, out reasons[n]);
                ids[n] = n;
                n++;
            }
            scores[n] = EnemyHudMath.Score(0.2f, 1.5f, -1f, 8f, true, 9f, 15f, false, out reasons[n]); ids[n] = n; n++; // aimed = id 8
            scores[n] = EnemyHudMath.Score(-1f, 1.5f, 2f, 8f, true, 14f, 15f, false, out reasons[n]); ids[n] = n; n++;  // engaged = id 9
            var shown = EnemyHudMath.Pick(scores, reasons, ids, n, 4, 2, picked);
            Assert.Equal(4, shown);
            var got = new System.Collections.Generic.List<int>();
            for (var i = 0; i < n; i++) if (picked[i]) got.Add(ids[i]);
            Assert.Equal(new[] { 8, 9, 0, 1 }, got);
        }

        [Fact]
        public void PickSkipsZeroScores()
        {
            var scores = new[] { 0f, 0f };
            var reasons = new[] { R.None, R.None };
            var ids = new[] { 0, 1 };
            var picked = new bool[2];
            Assert.Equal(0, EnemyHudMath.Pick(scores, reasons, ids, 2, 4, 2, picked));
        }

        [Fact]
        public void FadeTimes()
        {
            // in 0.12 s, out 0.35 s
            Assert.Equal(1f, EnemyHudMath.FadeStep(0f, 1f, 0.12f, 0.12f, 0.35f), 4);
            Assert.Equal(0.5f, EnemyHudMath.FadeStep(0f, 1f, 0.06f, 0.12f, 0.35f), 4);
            Assert.Equal(0.5f, EnemyHudMath.FadeStep(1f, 0f, 0.175f, 0.12f, 0.35f), 4);
            Assert.Equal(0.75f, EnemyHudMath.FadeStep(1f, 0.75f, 0.35f, 0.12f, 0.35f), 4); // never below the target
            Assert.Equal(0.75f, EnemyHudMath.TargetAlpha(true, R.Threat, 0.75f));
            Assert.Equal(1f, EnemyHudMath.TargetAlpha(true, R.Engaged, 0.75f));
            Assert.Equal(0f, EnemyHudMath.TargetAlpha(false, R.Aimed, 0.75f));
        }

        [Theory]
        [InlineData(3f, 1f)]
        [InlineData(6f, 1f)]
        [InlineData(13f, 0.9f)]
        [InlineData(20f, 0.8f)]
        [InlineData(40f, 0.8f)]
        public void DistanceScale(float d, float k) => Assert.Equal(k, EnemyHudMath.DistanceScale(d, 6f, 20f, 0.8f), 3);

        [Fact]
        public void StaggerBuildsDecaysAndHides()
        {
            var s = default(EnemyHudMath.StaggerState);
            s = EnemyHudMath.StaggerStep(s, true, 0f, false, 0f, 0.1f);
            Assert.Equal(P.Hidden, s.Phase); // fresh creature: nothing shown
            for (var i = 0; i < 30; i++) s = EnemyHudMath.StaggerStep(s, true, 0.6f, false, 0f, 0.05f);
            Assert.Equal(P.Building, s.Phase);
            Assert.Equal(0.6f, s.Fill, 2); // eased in 1.5 s >> 0.15 s
            // vanilla decay: threshold / 5 per s -> 0.2 per s
            for (var i = 0; i < 20; i++) s = EnemyHudMath.StaggerStep(s, true, 0.6f - 0.2f * (i + 1) * 0.05f, false, 0f, 0.05f);
            Assert.InRange(s.Fill, 0.39f, 0.44f); // 0.4 + the 0.15 s ease lag (0.2/s x 0.15 s)
            for (var i = 0; i < 40; i++) s = EnemyHudMath.StaggerStep(s, true, 0f, false, 0f, 0.05f);
            Assert.Equal(P.Hidden, s.Phase); // 1 s at zero
        }

        [Fact]
        public void StaggerActiveDrainsAndFlashes()
        {
            var s = EnemyHudMath.StaggerStep(default, true, 0.9f, false, 0f, 0.1f);
            s = EnemyHudMath.StaggerStep(s, true, 1f, true, 1f, 0.02f);
            Assert.Equal(P.Active, s.Phase);
            Assert.True(s.Flash > 0.15f);
            var last = s.Fill;
            for (var nt = 0.1f; nt <= 1f; nt += 0.1f)
            {
                s = EnemyHudMath.StaggerStep(s, true, 1f, true, EnemyHudMath.StaggerRemaining(nt), 0.05f);
                Assert.True(s.Fill <= last + 1e-4f);
                last = s.Fill;
            }
            Assert.Equal(0f, s.Flash);
            // falling edge: back to building at the (near-threshold) accumulator, no ease from 0
            s = EnemyHudMath.StaggerStep(s, true, 0.95f, false, 0f, 0.02f);
            Assert.Equal(P.Building, s.Phase);
            Assert.Equal(0.95f, s.Fill, 3);
        }

        [Fact]
        public void StaggerNeverForImmuneCreatures()
        {
            var s = EnemyHudMath.StaggerStep(default, false, 0.9f, true, 0.5f, 0.1f);
            Assert.Equal(P.Hidden, s.Phase);
        }

        [Fact]
        public void StaggerUnknownBuildShowsOnlyActive()
        {
            var s = EnemyHudMath.StaggerStep(default, true, -1f, false, 0f, 0.1f);
            Assert.Equal(P.Hidden, s.Phase);
            s = EnemyHudMath.StaggerStep(s, true, -1f, true, 0.7f, 0.1f);
            Assert.Equal(P.Active, s.Phase);
            Assert.Equal(0.7f, s.Fill, 3);
        }
    }
}
