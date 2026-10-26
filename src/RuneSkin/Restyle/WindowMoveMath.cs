using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RuneSkin.InventoryScreen;

namespace RuneSkin.Restyle
{
    // Pure maths of movable windows (no Unity types: unit-tested on net8). A window's position is its layout default
    // plus a user offset (canvas units, y down), stored per window id and screen resolution. The offset is clamped so
    // the moved window stays inside the bounds (the canvas above the bottom HUD strip); a default that already reaches
    // past the bounds is never moved by the clamp itself (offset 0 always stays valid), so "no saved position" keeps
    // today's docking exactly.
    public static class WindowMoveMath
    {
        // Canvas bounds a window may occupy: the whole canvas, its bottom cut at the HUD strip top (canvas units from the
        // top; 0 or past the canvas = no strip).
        public static Box Bounds(float canvasW, float canvasH, float stripTop)
        {
            var bottom = stripTop > 0f && stripTop < canvasH ? stripTop : canvasH;
            return new Box(0f, 0f, Math.Max(0f, canvasW), Math.Max(0f, bottom));
        }

        // Bounds shrunk by a frame overhang on the left, top and right (and the bottom unless a strip top already counts it).
        public static Box Inset(Box b, float overhang, bool bottomCounted)
        {
            var o = Math.Max(0f, overhang);
            return new Box(b.X + o, b.Y + o, Math.Max(0f, b.W - 2f * o), Math.Max(0f, b.H - o - (bottomCounted ? 0f : o)));
        }

        // The offset (dx, dy) clamped per axis so window + offset stays inside bounds; the range always includes 0.
        public static void Clamp(Box window, Box bounds, ref float dx, ref float dy)
        {
            dx = ClampAxis(dx, bounds.X - window.X, bounds.Right - window.Right);
            dy = ClampAxis(dy, bounds.Y - window.Y, bounds.Bottom - window.Bottom);
        }

        private static float ClampAxis(float v, float lo, float hi)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            lo = Math.Min(lo, 0f);
            hi = Math.Max(hi, 0f);
            return v < lo ? lo : v > hi ? hi : v;
        }

        public static Box Shift(Box b, float dx, float dy) => b.IsEmpty ? b : new Box(b.X + dx, b.Y + dy, b.W, b.H);

        // Smallest box holding both (an empty box is ignored).
        public static Box Union(Box a, Box b)
        {
            if (a.IsEmpty) return b;
            if (b.IsEmpty) return a;
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            return new Box(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
        }

        // Store key: window id + resolution bucket (the screen size the position was made at).
        public static string Key(string id, int screenW, int screenH) =>
            id + "@" + screenW.ToString(CultureInfo.InvariantCulture) + "x" + screenH.ToString(CultureInfo.InvariantCulture);

        // One "key=dx,dy" line per entry (invariant culture), sorted by key; zero offsets are left out.
        public static string Format(IDictionary<string, (float X, float Y)> offsets)
        {
            var keys = new List<string>(offsets.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("# RuneSkin window positions: <window>@<screen WxH>=<dx>,<dy> (canvas units from the default dock)\n");
            foreach (var k in keys)
            {
                var v = offsets[k];
                if (Math.Abs(v.X) < 0.01f && Math.Abs(v.Y) < 0.01f) continue;
                sb.Append(k).Append('=').Append(v.X.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v.Y.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        // Reads Format output; malformed lines and comments are skipped.
        public static Dictionary<string, (float X, float Y)> Parse(string text)
        {
            var result = new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                var parts = line.Substring(eq + 1).Split(',');
                if (parts.Length != 2) continue;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) continue;
                result[line.Substring(0, eq).Trim()] = (x, y);
            }
            return result;
        }
    }
}
