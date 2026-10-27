namespace RuneSkin.InventoryScreen
{
    // Pure part of the hidden first open (InventorySkin.GhostOpen): the session's first inventory open runs once behind the
    // spawn's loading screen, fully transparent and without input, so its one-time work (canvas build, TMP meshes and
    // glyphs, layout passes, first restyles, the doll's first render and alpha check, other mods' first Show) is spent
    // there instead of on the player's first Tab.
    public enum GhostStep
    {
        Wait,  // nothing yet (no local player, or one vanilla's Update would hide the screen for at once)
        Open,  // InventoryGui.Show now
        Hold,  // keep it shown (transparent) one more frame
        Close, // InventoryGui.Hide now
        Done,  // stop for this session (never started: loading screen gone / user opened first; or someone hid it)
    }

    public static class GhostOpenMath
    {
        // Shown frames: the cold open's work spreads over its first frames (rp-open-cold run 20261028-002303: RuneSkin
        // 11.2 / 43.4 / 9.8 ms on frames #0-#2, the doll's first render and alpha check inside them); one frame spare.
        public const int Frames = 4;

        // Loading screen alpha (Hud.m_loadingScreen, Hud.UpdateBlackScreen fades it out over 1 s after the spawn) needed to
        // start, and to keep holding: foreign overlays outside the inventory's fade group stay under the black screen.
        public const float StartCover = 0.9f;
        public const float HoldCover = 0.5f;

        // started: Open was issued and not yet closed; held: Hold steps since; shown: InventoryGui's animator "visible";
        // blocked: player dead / teleporting (InventoryGui.Update hides the screen then, InventoryGui.cs:513);
        // cutscene: Player.InCutscene - the spawn's "wakeup" animation (Player.cs:1690-1721) enters its cutscene tag on the
        // 2nd frame; vanilla's cutscene close is held off for the ghost only while it lasts (HoldsCutscene), and its end
        // (sawCutscene && !cutscene) closes the ghost.
        public static GhostStep Next(bool started, int held, bool player, bool blocked, bool shown, float cover, bool cutscene = false, bool sawCutscene = false)
        {
            if (!started)
            {
                if (!player) return GhostStep.Wait;
                if (shown || cover < StartCover) return GhostStep.Done;
                return blocked || cutscene ? GhostStep.Wait : GhostStep.Open;
            }
            if (!shown) return GhostStep.Done;
            // The cover rule holds in the cutscene too (Codex review): overlays outside the fade group never show over the game.
            return held + 1 >= Frames || blocked || (sawCutscene && !cutscene) || cover < HoldCover ? GhostStep.Close : GhostStep.Hold;
        }

        // InventoryGui.Update prefix decision: skip vanilla's Update for this frame (its cutscene close, InventoryGui.cs:513-516)
        // only while the ghost is open and the cutscene is the sole reason vanilla would close; dead / teleporting / no
        // player still close it as vanilla does.
        public static bool HoldsCutscene(bool ghost, bool shown, bool player, bool blocked, bool cutscene) =>
            ghost && shown && player && !blocked && cutscene;
    }
}
