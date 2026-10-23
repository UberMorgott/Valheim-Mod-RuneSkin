using System;

namespace RuneSkin.WorldHud
{
    // Pure math of the NPC / creature speech bubble (SpeechBubbleSkin), unit tested. Canvas units: the chat canvas is
    // scaled with the screen (GuiScaler), so a width token in reference px at 1080p keeps the same screen share at every
    // resolution.
    public static class SpeechMath
    {
        // Wrap width for the text: its single-line width, never wider than the max width token. Rounded up (+1) so
        // TMP's own float layout does not wrap a line that measured exactly the cap. NaN / negative = the cap.
        public static float WrapWidth(float oneLine, float maxWidth)
        {
            if (!(maxWidth > 0f)) maxWidth = 1f;
            if (float.IsNaN(oneLine) || oneLine < 0f) return maxWidth;
            return Math.Min((float)Math.Ceiling(oneLine) + 1f, maxWidth);
        }

        // Bubble rect = wrapped text + padding on each side. Empty text = no bubble (0 x 0).
        public static void BubbleSize(float wrapWidth, float textHeight, float pad, float vpad, out float width, out float height)
        {
            if (!(wrapWidth > 0f) || !(textHeight > 0f))
            {
                width = height = 0f;
                return;
            }
            pad = Math.Max(0f, pad);
            vpad = Math.Max(0f, vpad);
            width = wrapWidth + 2f * pad;
            height = (float)Math.Ceiling(textHeight) + 2f * vpad;
        }

        // Fade near the cull distance (vanilla Chat.UpdateNpcTexts hides the bubble past m_cullDistance, Chat.cs:639):
        // full alpha up to (1 - fadeShare) of it, linear to 0 at the cull distance. No cull distance = always 1.
        public static float DistanceFade(float distance, float cullDistance, float fadeShare)
        {
            if (!(cullDistance > 0f) || float.IsNaN(distance)) return 1f;
            fadeShare = Math.Min(1f, Math.Max(0f, fadeShare));
            if (fadeShare <= 0f) return distance <= cullDistance ? 1f : 0f;
            var start = cullDistance * (1f - fadeShare);
            if (distance <= start) return 1f;
            if (distance >= cullDistance) return 0f;
            return 1f - (distance - start) / (cullDistance - start);
        }
    }
}
