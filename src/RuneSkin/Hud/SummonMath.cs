using System;

namespace RuneSkin.PlayerHud
{
    // Summon frames, the managed part (unit-tested): MorgottTweaks slot states (player ZDO mt_sum_ui, MorgottTweaks
    // SummonSlots.SlotUi), their status line and where the frame row goes relative to the hotbar.
    public enum SummonState : byte
    {
        Spawning = 0,
        Alive = 1,
        Dead = 2,
        Off = 3,
        NoReserve = 4,
        Waiting = 5,
    }

    public static class SummonMath
    {
        public const int SlotsVersion = 1;
        public const int MaxFrames = 8;

        // Status line key ($runeskin_summon_*) and its number (respawn seconds, -1 = none). Alive: "Stay" while told to
        // stay, otherwise nothing.
        public static string StatusKey(SummonState state, bool stay, double cdUntil, double now, out int seconds)
        {
            seconds = -1;
            switch (state)
            {
                case SummonState.Spawning: return "runeskin_summon_spawning";
                case SummonState.Dead:
                    seconds = Math.Max(0, (int)Math.Ceiling(cdUntil - now));
                    return "runeskin_summon_respawn";
                case SummonState.Off: return "runeskin_summon_off";
                case SummonState.NoReserve: return "runeskin_summon_noreserve";
                case SummonState.Waiting: return "runeskin_summon_waiting";
                default: return stay ? "runeskin_summon_stay" : null;
            }
        }

        // The creature shows at full strength while it has a body (alive or appearing); dimmed otherwise.
        public static bool Active(SummonState state) => state == SummonState.Alive || state == SummonState.Spawning;

        public static float Ratio(float value, float max) => max > 0f && !float.IsNaN(value) ? Math.Max(0f, Math.Min(1f, value / max)) : 0f;

        // The row hangs below the hotbar while the hotbar sits in the upper half of the screen (vanilla top-left), and
        // stands on top of it otherwise (hotbar inside a bottom strip), so it never leaves the screen.
        public static bool Below(float hotbarCenterY, float screenHeight) => hotbarCenterY >= screenHeight * 0.5f;

        // Left edge of frame i (reference px) for frames `size` wide with `gap` between them.
        public static float FrameX(int index, float size, float gap) => index * (size + gap);
    }
}
