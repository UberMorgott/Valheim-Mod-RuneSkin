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

        // ---------------------------------------------------------------- visibility (spec E1, unit tested)

        // Why a creature's overhead bar may show; higher wins (hud-design spec 1).
        public enum Reason { None = 0, Threat = 1, Engaged = 2, Aimed = 3 }

        // Score of one HUD: Aimed (now or within `linger` s after the crosshair left) 1000; Engaged (local player hit it
        // or it hit the local player within `engaged` s) 500 + remaining s x 10; Threat (alerted, within `threatDist` m)
        // 100 - distance, +50 when it targets the local player. sinceX < 0 = never.
        public static float Score(float sinceAimed, float linger, float sinceEngaged, float engaged, bool alerted,
            float distance, float threatDist, bool targetsLocal, out Reason reason)
        {
            if (sinceAimed >= 0f && sinceAimed <= linger)
            {
                reason = Reason.Aimed;
                return 1000f + (linger - sinceAimed);
            }
            if (sinceEngaged >= 0f && sinceEngaged <= engaged)
            {
                reason = Reason.Engaged;
                return 500f + (engaged - sinceEngaged) * 10f;
            }
            if (alerted && distance <= threatDist)
            {
                reason = Reason.Threat;
                return Math.Max(1f, 100f - distance) + (targetsLocal ? 50f : 0f);
            }
            reason = Reason.None;
            return 0f;
        }

        // Picks the shown bars: candidates [0, count) sorted by score (insertion sort, in place, no allocation), the top
        // `cap` survive with at most `threatCap` of reason Threat; picked[i] (index into the sorted arrays) set.
        // Returns the number picked. Scores <= 0 never show.
        public static int Pick(float[] scores, Reason[] reasons, int[] ids, int count, int cap, int threatCap, bool[] picked)
        {
            for (var i = 1; i < count; i++)
            {
                var s = scores[i];
                var r = reasons[i];
                var id = ids[i];
                var j = i - 1;
                while (j >= 0 && scores[j] < s)
                {
                    scores[j + 1] = scores[j];
                    reasons[j + 1] = reasons[j];
                    ids[j + 1] = ids[j];
                    j--;
                }
                scores[j + 1] = s;
                reasons[j + 1] = r;
                ids[j + 1] = id;
            }
            int shown = 0, threats = 0;
            for (var i = 0; i < count; i++)
            {
                var ok = scores[i] > 0f && shown < cap && (reasons[i] != Reason.Threat || threats < threatCap);
                picked[i] = ok;
                if (!ok) continue;
                shown++;
                if (reasons[i] == Reason.Threat) threats++;
            }
            return shown;
        }

        // Target alpha of a picked bar: aimed / engaged full, threats at `lowAlpha`; not picked = 0.
        public static float TargetAlpha(bool picked, Reason reason, float lowAlpha) =>
            !picked ? 0f : reason == Reason.Threat ? lowAlpha : 1f;

        // Linear fade towards target: fadeIn s from 0 to 1, fadeOut s from 1 to 0 (unscaled time).
        public static float FadeStep(float current, float target, float dt, float fadeIn, float fadeOut)
        {
            var rising = target > current;
            var span = rising ? fadeIn : fadeOut;
            if (!(span > 0f)) return target;
            var step = dt / span;
            return rising ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        // Distance shrink: x1 at <= near m, xMin at >= far m, linear between, quantised to `quantum` steps.
        public static float DistanceScale(float distance, float near, float far, float min, float quantum = 0.02f)
        {
            if (!(far > near)) return 1f;
            var t = Math.Min(1f, Math.Max(0f, (distance - near) / (far - near)));
            var s = 1f + (min - 1f) * t;
            return quantum > 0f ? (float)Math.Round(s / quantum) * quantum : s;
        }

        // ---------------------------------------------------------------- stagger (spec E4, unit tested)

        public enum StaggerPhase { Hidden = 0, Building = 1, Active = 2 }

        public struct StaggerState
        {
            public StaggerPhase Phase;
            public float Fill;      // shown fill 0..1
            public float Idle;      // s at fill 0 while not staggering
            public float Flash;     // s left of the trigger flash
            public bool Was;        // staggering last step
        }

        // One step. canStagger false (m_staggerDamageFactor <= 0) = hidden. build = GetStaggerPercentage (owner) or the
        // synced value, < 0 = unknown (non-owner without data: build-up not shown). staggering = IsStaggering();
        // remaining = 1 - normalizedTime of the stagger animator state (0..1). Building eases to the value over `ease` s
        // (exponential), Active shows the remaining time directly; hidden after `hideAfter` s at zero (Hud.cs:791-797).
        public static StaggerState StaggerStep(StaggerState s, bool canStagger, float build, bool staggering, float remaining,
            float dt, float ease = 0.15f, float hideAfter = 1f, float flash = 0.2f)
        {
            if (!canStagger) return default;
            s.Flash = Math.Max(0f, s.Flash - dt);
            if (staggering)
            {
                if (!s.Was) s.Flash = flash;
                s.Was = true;
                s.Phase = StaggerPhase.Active;
                s.Fill = Math.Min(1f, Math.Max(0f, remaining));
                s.Idle = 0f;
                return s;
            }
            s.Was = false;
            var target = build >= 0f ? Math.Min(1f, build) : 0f;
            var k = ease > 0f ? 1f - (float)Math.Exp(-dt / ease) : 1f;
            s.Fill = s.Phase == StaggerPhase.Active ? target : s.Fill + (target - s.Fill) * k;
            if (Math.Abs(s.Fill - target) < 0.002f) s.Fill = target;
            if (s.Fill > 0.001f) s.Idle = 0f;
            else s.Idle += dt;
            // Hidden stays hidden at zero (a fresh creature shows nothing); a shown bar lingers hideAfter s at zero.
            if (s.Fill > 0.001f) s.Phase = StaggerPhase.Building;
            else if (s.Phase != StaggerPhase.Hidden && s.Idle >= hideAfter) s.Phase = StaggerPhase.Hidden;
            else if (s.Phase == StaggerPhase.Active) s.Phase = StaggerPhase.Building;
            return s;
        }

        // Remaining stagger fraction from the animator state's normalizedTime (speed independent; > 1 = ended).
        public static float StaggerRemaining(float normalizedTime) => Math.Min(1f, Math.Max(0f, 1f - normalizedTime));

        // Spark strength per tier (enemy-b: normal a bright line with few embers, unique a shower).
        public static float SparkAlpha(int tier) => tier switch { 1 => 0.6f, 2 => 0.85f, 3 => 1f, _ => 0.35f };

        public static int TierIndex(string token) => token switch
        {
            "sprite.frame.enemy.magic" => 1,
            "sprite.frame.enemy.rare" => 2,
            "sprite.frame.enemy.unique" => 3,
            _ => 0,
        };

        // Only a real boss gets the boss bar: the creature says IsBoss() (m_boss: vanilla bosses, Vaettir's world boss, Epic
        // Loot bounty bosses) AND vanilla built its HUD from the boss template. A normal mob never does, and the target panel
        // Vaettir copies from that template is not an EnemyHud HUD at all.
        public static bool BossBar(bool isBoss, string guiName, string bossTemplateName) =>
            isBoss && !string.IsNullOrEmpty(guiName) && !string.IsNullOrEmpty(bossTemplateName) &&
            guiName.StartsWith(bossTemplateName, StringComparison.Ordinal);

        // Alert / aware indicator stacked above the name (user 2026-10-23, replaces the 0.16.3 slot left of it): centre y
        // of an icon `iconHeight` tall whose bottom clears the name text top by `gap`.
        public static float AlertAboveY(float textTop, float iconHeight, float gap) => textTop + gap + iconHeight * 0.5f;

        // boss-c bar span (spear tip to spear tip): the body plus each cap's part outside it (cap pivot 1 at the bar end,
        // pulled inwards by capOffset).
        public static float BossSpan(float bodyWidth, float capWidth, float capOffset) =>
            bodyWidth + 2f * Math.Max(0f, capWidth - capOffset);

        // Uniform scale that makes the boss bar span widthShare of the screen (16:9 box on ultrawide) - the same rule as
        // Vaettir's world boss bar (BossMath.ScreenFit), so every boss bar has one size. Canvas units in. 1 when unknown.
        public static float BossScreenFit(float span, float canvasWidth, float canvasHeight, float widthShare)
        {
            if (!(span > 0f) || !(canvasWidth > 0f) || !(canvasHeight > 0f) || !(widthShare > 0f)) return 1f;
            return widthShare * Math.Min(canvasWidth, canvasHeight * 16f / 9f) / span;
        }

        // Animation frame of a looping sheet: index 0..count-1 at fps, and the blend toward the next frame (cross-fade).
        public static int SheetFrame(float time, float fps, int count, out float blend)
        {
            blend = 0f;
            if (count <= 1 || !(fps > 0f)) return 0;
            var f = time * fps;
            var i = (int)Math.Floor(f);
            blend = f - i;
            i %= count;
            return i < 0 ? i + count : i;
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
