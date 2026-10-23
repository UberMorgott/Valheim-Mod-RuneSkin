using RuneSkin.PlayerHud;
using Xunit;

namespace RuneSkin.Tests
{
    // BarFill.Smooth (user 2026-10-23: a small max HP moved the fill in whole-point steps): the drawn share glides
    // through sub-pixel positions as a critically damped spring, frame-rate independent, 95 % of a change after `seconds`.
    public class BarFillTests
    {
        private static float Run(float from, float to, float dt, float seconds, int frames)
        {
            float v = 0f, s = from;
            for (var i = 0; i < frames; i++) s = BarFill.Smooth(s, ref v, to, dt, seconds);
            return s;
        }

        [Fact]
        public void FirstFrame_AndOff_Snap()
        {
            var v = 0f;
            Assert.Equal(0.4f, BarFill.Smooth(-1f, ref v, 0.4f, 0.016f, 0.25f), 5);
            Assert.Equal(0.4f, BarFill.Smooth(0.9f, ref v, 0.4f, 0.016f, 0f), 5);
        }

        [Fact]
        public void OnePointOfTwentyFive_GlidesOverManyFrames_NoBigStep()
        {
            // HP 25 -> 24 at 60 fps on a 640 px bar (25.6 px gap): monotonic, no overshoot, >= 10 frames of motion, no
            // single frame moves more than 15 % of the gap (a plain exponential moves ~20 % on the first frame)
            float shown = 1f, v = 0f, target = 24f / 25f, prev = shown;
            var frames = 0;
            var maxStep = 0f;
            while (shown > target && frames < 200)
            {
                shown = BarFill.Smooth(shown, ref v, target, 1f / 60f, 0.25f);
                var stepPx = (prev - shown) * 640f;
                Assert.True(stepPx >= 0f, "monotonic");
                Assert.True(shown >= target, "no overshoot");
                if (stepPx > maxStep) maxStep = stepPx;
                prev = shown;
                frames++;
            }
            Assert.True(frames >= 10, $"{frames} frames");
            Assert.True(maxStep <= 0.15f * 25.6f, $"max step {maxStep} px");
            Assert.Equal(target, shown, 5);
        }

        [Fact]
        public void FrameRateIndependent_95PercentAfterSeconds()
        {
            var a = Run(0f, 1f, 1f / 60f, 0.25f, 15);
            var b = Run(0f, 1f, 1f / 180f, 0.25f, 45);
            Assert.Equal(a, b, 3);
            Assert.InRange(a, 0.94f, 0.96f);
        }
    }
}
