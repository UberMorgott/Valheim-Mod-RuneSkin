using System;
using System.Text;
using RuneSkin.InventoryScreen;

namespace RuneSkin.InfoScreens
{
    // Pure maths of the character stats window (stats-full-b, 4x2 card grid): the design frame (1920x1080 mockup px)
    // mapped onto the canvas, card / row stacking, regeneration per SECOND (vanilla Player.UpdateFood /
    // UpdateStats formulas, cited per method), value formatting into a reused StringBuilder (no allocation).
    // Unit-tested on net8 (no Unity types).
    public static class StatsMath
    {
        public const float DesignW = 1920f, DesignH = 1080f;

        // Player.UpdateFood (Player.cs:2449-2466): every 10 s the summed food m_foodRegen x SEMan.ModifyHealthRegen
        // heals at once.
        public const float FoodTick = 10f;

        // Uniform scale of the design frame on a canvas of w x h units (the whole mockup fits, centred).
        public static float FitScale(float w, float h) => w > 0f && h > 0f ? Math.Min(w / DesignW, h / DesignH) : 0f;

        // A design rect (mockup px) on the canvas: frame centred, scale s.
        public static Box Map(float canvasW, float canvasH, float s, float x, float y, float w, float h)
        {
            var ox = (canvasW - DesignW * s) / 2f;
            var oy = (canvasH - DesignH * s) / 2f;
            return new Box(ox + x * s, oy + y * s, w * s, h * s);
        }

        // Card (col, row) of the grid: first card at (x0, y0), w x h, pitch px / py (design px).
        public static Box Card(int col, int row, float x0, float y0, float w, float h, float px, float py) =>
            new Box(x0 + col * px, y0 + row * py, w, h);

        // Health regeneration per second: sum of the eaten foods' m_foodRegen x the status-effect multiplier, paid once
        // per FoodTick (Player.cs:2457-2465).
        public static float HealthRegen(float foodRegenSum, float multiplier) =>
            foodRegenSum > 0f ? foodRegenSum * multiplier / FoodTick : 0f;

        // Seconds to the next food heal (m_foodRegenTimer counts up to FoodTick, Player.cs:2450-2455).
        public static float HealthTickIn(float timer) => Math.Max(0f, FoodTick - Math.Max(0f, timer));

        // Stamina regeneration per second at `fill` (0..1) (Player.UpdateStats, Player.cs:2099-2117):
        // (regen + (1 - fill) x regen x timeMultiplier) x factor x multiplier, plus a flat per-second bonus (MorgottTweaks
        // AddStaminaRegen through Epic Loot, same factor, not multiplied), times the world rate (Game.m_staminaRegenRate).
        public static float StaminaRegen(float regen, float timeMultiplier, float fill, float factor, float multiplier, float flat, float worldRate)
        {
            var rate = (regen + (1f - Clamp01(fill)) * regen * timeMultiplier) * factor;
            return Math.Max(0f, (rate * multiplier + flat * factor) * worldRate);
        }

        // Eitr regeneration per second at `fill` (Player.cs:2133-2152): (regen + (1 - fill) x regen) x factor x
        // (status multiplier + equipment eitr regen modifier), plus a flat per-second bonus; no world rate.
        public static float EitrRegen(float regen, float fill, float factor, float multiplier, float flat)
        {
            var rate = (regen + (1f - Clamp01(fill)) * regen) * factor;
            return Math.Max(0f, rate * multiplier + flat * factor);
        }

        // Change of a value against its base in percent (speed factors, costs): 0 when the base is 0.
        public static float ChangePercent(float value, float baseValue) =>
            baseValue > 0f ? (value / baseValue - 1f) * 100f : 0f;

        // -1 = worse, 0 = neutral, 1 = better; `higherIsBetter` flips cost rows (a lower run drain is good).
        public static int Semantic(float delta, bool higherIsBetter, float epsilon = 0.5f)
        {
            if (delta > epsilon) return higherIsBetter ? 1 : -1;
            if (delta < -epsilon) return higherIsBetter ? -1 : 1;
            return 0;
        }

        // Reduction (share) green / orange against the armor-only share of the same probe.
        public static int ReductionSemantic(float reduction, float armorOnly) => Semantic((reduction - armorOnly) * 100f, true);

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        // Skill steps of the stat breakdown (decompiled 1.0.20): Skills.GetRandomSkillFactor :188-195 (Lerp(0.4, 1, skill)
        // +-0.15, clamped), Attack.GetAttackStamina :481 (-33% x skill), Player.CheckRun :2525 / GetDodgeStaminaUse :5796
        // (Lerp(1, 0.5, skill)), Player.OnSwimming :2637 (Lerp(min, max, skill)).
        public static void SkillRange(float skillFactor, out float min, out float max)
        {
            var mid = 0.4f + 0.6f * Clamp01(skillFactor);
            min = Clamp01(mid - 0.15f);
            max = Clamp01(mid + 0.15f);
        }

        public static float AttackSkillCut(float staminaUse, float skillFactor) => staminaUse * 0.33f * Clamp01(skillFactor);

        public static float RunDrainSkill(float skillFactor) => 1f - 0.5f * Clamp01(skillFactor);

        public static float SwimDrain(float minSkill, float maxSkill, float skillFactor) => minSkill + (maxSkill - minSkill) * Clamp01(skillFactor);

        // Factor with up to two decimals, trailing zeros trimmed ("1,25", "0,5", "2").
        public static StringBuilder AppendFactor(StringBuilder sb, float v, bool comma)
        {
            var hundredths = (int)Math.Round(Math.Abs(v) * 100f, MidpointRounding.AwayFromZero);
            if (v < 0f && hundredths > 0) sb.Append('−');
            Digits(sb, hundredths / 100);
            var frac = hundredths % 100;
            if (frac == 0) return sb;
            sb.Append(comma ? ',' : '.').Append((char)('0' + frac / 10));
            if (frac % 10 != 0) sb.Append((char)('0' + frac % 10));
            return sb;
        }

        // One number as a string (tooltip notes, built only while hovered).
        public static string Format(float v, bool comma) => AppendCompact(new StringBuilder(8), v, comma).ToString();

        // ---- formatting (into a reused builder; `comma` = decimal comma, Russian) ----

        // One decimal, trailing ",0" kept ("+2,4", "15,0"); `sign` writes '+' for positives.
        public static StringBuilder AppendDecimal(StringBuilder sb, float v, bool comma, bool sign)
        {
            var tenths = (int)Math.Round(Math.Abs(v) * 10f, MidpointRounding.AwayFromZero);
            if (v < 0f && tenths > 0) sb.Append('−');
            else if (sign && tenths > 0) sb.Append('+');
            Digits(sb, tenths / 10).Append(comma ? ',' : '.').Append((char)('0' + tenths % 10));
            return sb;
        }

        // Whole number with an optional sign ("+12", "−10", "0"); the minus is the typographic minus.
        public static StringBuilder AppendInt(StringBuilder sb, float v, bool sign)
        {
            var n = (int)Math.Round(v, MidpointRounding.AwayFromZero);
            if (n < 0) Digits(sb.Append('−'), -n);
            else
            {
                if (sign && n > 0) sb.Append('+');
                Digits(sb, n);
            }
            return sb;
        }

        // Shorter of one decimal / whole: 1.5 -> "1,5", 2 -> "2" (multipliers, regen per dish).
        public static StringBuilder AppendCompact(StringBuilder sb, float v, bool comma)
        {
            var tenths = (int)Math.Round(Math.Abs(v) * 10f, MidpointRounding.AwayFromZero);
            if (v < 0f && tenths > 0) sb.Append('−');
            Digits(sb, tenths / 10);
            if (tenths % 10 != 0) sb.Append(comma ? ',' : '.').Append((char)('0' + tenths % 10));
            return sb;
        }

        // m:ss below an hour, h:mm:ss above (food / rested timers; "16:15" readable, not "1 6:15").
        public static StringBuilder AppendClock(StringBuilder sb, float seconds)
        {
            var t = (int)Math.Max(0f, (float)Math.Ceiling(seconds));
            var h = t / 3600;
            var m = t / 60 % 60;
            var s = t % 60;
            if (h > 0) Two(Digits(sb, h).Append(':'), m);
            else Digits(sb, m);
            return Two(sb.Append(':'), s);
        }

        // Non-negative integer as digits, no string allocation (StringBuilder.Append(int) formats through a string on
        // .NET Framework).
        public static StringBuilder Digits(StringBuilder sb, int v)
        {
            if (v < 0) v = 0;
            var div = 1;
            while (v / div >= 10) div *= 10;
            for (; div > 0; div /= 10) sb.Append((char)('0' + v / div % 10));
            return sb;
        }

        private static StringBuilder Two(StringBuilder sb, int v) => sb.Append((char)('0' + v / 10 % 10)).Append((char)('0' + v % 10));

        // "<color=#RRGGBB>" ... "</color>" around what `write` appends; semantic 0 writes no tag.
        public static StringBuilder Open(StringBuilder sb, int semantic, string goodHex, string badHex)
        {
            if (semantic > 0) sb.Append("<color=#").Append(goodHex).Append('>');
            else if (semantic < 0) sb.Append("<color=#").Append(badHex).Append('>');
            return sb;
        }

        public static StringBuilder Close(StringBuilder sb, int semantic) => semantic != 0 ? sb.Append("</color>") : sb;

        // ---- stacking ----

        // Top offsets of rows of the given heights from `top`; returns the content height. `tops` must hold n.
        public static float Stack(float[] heights, int n, float top, float[] tops)
        {
            var y = top;
            for (var i = 0; i < n; i++)
            {
                tops[i] = y;
                y += heights[i];
            }
            return y - top;
        }

        // Height of each of `count` shrinkable rows (now `full` high) so `total` fits `view`, never below `min`.
        public static float Compress(float total, float view, int count, float full, float min)
        {
            if (count <= 0 || total <= view) return full;
            var h = full - (total - view) / count;
            return Math.Max(min, Math.Min(full, h));
        }

        // Scroll offset clamp: content taller than the view scrolls 0..(content - view).
        public static float ClampScroll(float offset, float content, float view) =>
            content <= view ? 0f : Math.Max(0f, Math.Min(offset, content - view));
    }
}
