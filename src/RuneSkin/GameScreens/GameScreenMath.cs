using System;

namespace RuneSkin.GameScreens
{
    // Pure layout math of the M6 batch 2 screens (art\README.md "M6 sprites": black screens load-b, text viewer
    // text-a), unit tested.
    public static class GameScreenMath
    {
        // Respawn tip banner (mockup m6-load-b: panel banner bottom-centre): the tip's own box grown by the pads, its
        // width clamped to [minWidth, maxWidth] (long tips wrap inside the vanilla box, short ones keep a banner shape).
        public static float BannerWidth(float tipWidth, float pad, float minWidth, float maxWidth)
        {
            var w = Math.Max(0f, tipWidth) + 2f * Math.Max(0f, pad);
            if (maxWidth > 0f) w = Math.Min(w, maxWidth);
            return Math.Max(w, minWidth);
        }

        // Banner height: the tip box, the title line above it (0 = none) and both pads.
        public static float BannerHeight(float tipHeight, float titleHeight, float pad) =>
            Math.Max(0f, tipHeight) + Math.Max(0f, titleHeight) + 2f * Math.Max(0f, pad);

        // Text viewer runic bands (band_runes above and below the text): centre offset of a band from the text box
        // centre, along y (top band +, bottom band -), clear of the text by gap.
        public static float BandOffset(float textHeight, float bandHeight, float gap) =>
            Math.Max(0f, textHeight) * 0.5f + Math.Max(0f, gap) + Math.Max(0f, bandHeight) * 0.5f;

        // Teleport swirl: slow continuous turn (clockwise = negative z), wrapped to (-360, 0].
        public static float SpinAngle(float seconds, float degPerSecond)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0f;
            var a = -(seconds * degPerSecond) % 360f;
            return a > 0f ? a - 360f : a;
        }

        // Sleep medallion slot: centre y of a side-high medallion gap above the text block [textBottom, textTop] (host
        // space, y up); below the block when the top slot would leave the host (hostTop - margin) and the bottom slot
        // fits (hostBottom + margin); otherwise the top slot (the text stays clear, the medallion may be cut).
        public static float MedallionY(float textTop, float textBottom, float side, float gap, float hostTop, float hostBottom, float margin)
        {
            side = Math.Max(0f, side);
            gap = Math.Max(0f, gap);
            margin = Math.Max(0f, margin);
            var above = textTop + gap + side * 0.5f;
            if (above + side * 0.5f <= hostTop - margin) return above;
            var below = textBottom - gap - side * 0.5f;
            return below - side * 0.5f >= hostBottom + margin ? below : above;
        }

        // Max line width of a centred text: the inset per side that narrows a wrap width to maxWidth (0 = no cap).
        public static float WrapInset(float width, float maxWidth) =>
            maxWidth <= 0f || width <= maxWidth ? 0f : (width - maxWidth) * 0.5f;

        // A glyph centred in a round socket: glyph side from the socket side and its share (0..1).
        public static float GlyphSide(float socket, float share) => Math.Max(0f, socket) * Math.Max(0f, Math.Min(1f, share));

        // Active effects row text: tooltip texts carry blank lines (SE_Stats separates its parts with "\n\n",
        // SE_Stats.cs:483) that read as gaps inside a row: trimmed, every blank-line run collapsed to one break.
        public static string Tidy(string text) =>
            string.IsNullOrEmpty(text) ? "" : System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s*\n\s*\n\s*", "\n");
    }
}
