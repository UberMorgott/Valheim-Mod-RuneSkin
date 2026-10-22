using System;

namespace RuneSkin.WorldHud
{
    // Pure geometry of a frame drawn around a bar (unit tested): the frame's transparent window (sprite minus its
    // top / bottom borders) is fitted to the bar height, the borders spill outside the bar rect.
    public struct FrameFit
    {
        public float Scale;          // reference px of frame per reference px of art
        public float Left, Right, Top, Bottom; // outward offsets around the bar rect (reference px)
        public float PpuMultiplier;  // Image.pixelsPerUnitMultiplier (before the canvas factor)
    }

    public static class EnemyHudMath
    {
        // spriteH / borders in sprite px, ppu = sprite pixels per unit (reference px = sprite px x 100 / ppu).
        // borderScale > 0: the borders are drawn at that fixed scale and the Sliced centre stretches to the bar (the art's
        // window is only 8 of 36 px, so fitting the window made thick bars carry giant caps, run 20261022-043546);
        // 0 = the window fitted to the bar (scale = bar / window).
        public static FrameFit Fit(float barHeight, float spriteH, float left, float right, float top, float bottom, float ppu, float borderScale = 0f)
        {
            var toRef = ppu > 0f ? 100f / ppu : 1f;
            var window = Math.Max(1f, (spriteH - top - bottom) * toRef);
            var k = borderScale > 0f ? borderScale : barHeight > 0f ? barHeight / window : 1f;
            return new FrameFit
            {
                Scale = k,
                Left = left * toRef * k,
                Right = right * toRef * k,
                Top = top * toRef * k,
                Bottom = bottom * toRef * k,
                PpuMultiplier = k > 0f ? 1f / k : 1f,
            };
        }

        // Bar height growth factor (Codex r2: the frame window read vertically squashed): target / current, never
        // below 1 (a template already taller than the target stays), 1 for a degenerate rect.
        public static float BarGrowth(float current, float target)
        {
            if (current <= 0.5f || target <= 0f) return 1f;
            return Math.Max(1f, target / current);
        }

        // Vaettir rarity frame sprite name (UI\Resources\vt_frame_<tier>.png, RarityFrames.cs Files) -> RuneSkin token.
        public static string TierToken(string vtSprite, bool boss)
        {
            if (boss) return "sprite.frame.boss";
            if (string.IsNullOrEmpty(vtSprite)) return "sprite.frame.enemy.normal";
            if (vtSprite.EndsWith("magic", StringComparison.Ordinal)) return "sprite.frame.enemy.magic";
            if (vtSprite.EndsWith("rare", StringComparison.Ordinal)) return "sprite.frame.enemy.rare";
            if (vtSprite.EndsWith("unique", StringComparison.Ordinal)) return "sprite.frame.enemy.unique";
            return "sprite.frame.enemy.normal";
        }
    }
}
