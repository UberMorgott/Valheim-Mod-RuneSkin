using System;
using UnityEngine;

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

        // Rune spinner glow chase (0.10.2): the runes stay fixed on the ring, a light head runs clockwise through them
        // (phase = rune steps since start). Rune i of n lies d = (phase - i) mod n steps behind the head: full at the
        // head, fading out over `tail` steps behind it, rising during the last step before the head reaches it;
        // baseLevel = the unlit brightness. Smoothstep ease, result in [baseLevel, 1].
        public static float RuneGlow(int i, int n, float phase, float tail, float baseLevel)
        {
            baseLevel = Math.Max(0f, Math.Min(1f, baseLevel));
            if (n <= 0 || float.IsNaN(phase) || float.IsInfinity(phase)) return baseLevel;
            var d = (phase - i) % n;
            if (d < 0f) d += n;
            float k;
            if (d > n - 1f) k = d - (n - 1f);
            else k = tail > 0f && d < tail ? 1f - d / tail : 0f;
            k = Math.Max(0f, Math.Min(1f, k));
            k = k * k * (3f - 2f * k);
            return baseLevel + (1f - baseLevel) * k;
        }

        // Index of the brightest rune at phase (the head).
        public static int RuneHead(int n, float phase)
        {
            if (n <= 0 || float.IsNaN(phase) || float.IsInfinity(phase)) return 0;
            var h = (int)Math.Floor(phase) % n;
            return h < 0 ? h + n : h;
        }

        // Vertical category column: y offset (down from the column top) of tab i.
        public static float TabOffset(int index, float height, float gap) => index * (height + gap);

        // Camera position (rotation kept) that shows a standing figure (feet at foot, body metres tall, world up) with
        // its centre at viewport x vx and its height spanning viewport y bottom..top (pinhole, vertical fov degrees).
        // Never further out than the current position along the view axis (the vanilla framing already larger stays).
        public static Vector3 FrameMarker(Quaternion rotation, Vector3 foot, float body, float fovDeg, float aspect, float vx, float bottom, float top, Vector3 current)
        {
            var span = top - bottom;
            if (!(span > 0.05f) || !(fovDeg > 1f) || !(aspect > 0f) || !(body > 0f)) return current;
            var f = rotation * Vector3.forward;
            var r = rotation * Vector3.right;
            var u = rotation * Vector3.up;
            var c = foot + Vector3.up * (body * 0.5f);
            var tan = (float)Math.Tan(fovDeg * 0.5f * Math.PI / 180.0);
            var d = body / (2f * span * tan);
            var now = Vector3.Dot(c - current, f);
            if (now > 0f && d > now) return current;
            var vh = 2f * d * tan;
            var vw = vh * aspect;
            var vyc = (top + bottom) * 0.5f;
            return c - f * d - r * ((vx - 0.5f) * vw) - u * ((vyc - 0.5f) * vh);
        }
    }
}
