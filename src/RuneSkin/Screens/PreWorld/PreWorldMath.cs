using System;

namespace RuneSkin.PreWorld
{
    // Pure pre-world helpers (unit tested).
    public static class PreWorldMath
    {
        // Vanilla panel art: the wooden panel family and the dark backdrops dialogs are built on.
        private static readonly string[] PanelSprites = { "woodpanel", "panel_bkg", "panel_interior_bkg" };

        public static bool IsPanelSprite(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return false;
            foreach (var p in PanelSprites)
                if (sprite.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // Character list socket letter: first letter of the name, upper case; "?" for an empty name.
        public static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var trimmed = name.TrimStart();
            return trimmed.Length == 0 ? "?" : trimmed.Substring(0, 1).ToUpperInvariant();
        }

        // Vertical category column: y offset (down from the column top) of tab i.
        public static float TabOffset(int index, float height, float gap) => index * (height + gap);
    }
}
