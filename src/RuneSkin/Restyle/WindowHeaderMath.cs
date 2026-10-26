using System;
using System.Text;
using RuneSkin.InventoryScreen;

namespace RuneSkin.Restyle
{
    // Pure logic of the one shared window header (RuneWindowHeader, RuneSkin 0.20.3; no Unity types: unit-tested on net8).
    // Every window the skin owns or adopts carries exactly one header: a band at the window top with the window title
    // in the rune face (Norsebold); dragging the band moves that window (offset from its layout default, canvas units,
    // y down), a double-click resets it, positions persist per window id (WindowMove).
    public static class WindowHeaderMath
    {
        // Header band of a window box: its top `height` (never taller than the window).
        public static Box Band(Box window, float height) =>
            window.IsEmpty ? default : new Box(window.X, window.Y, window.W, Math.Max(0f, Math.Min(height, window.H)));

        // An axis-aligned rect, y up (world / screen space of the frame keep-out).
        public readonly struct Span
        {
            public readonly float X0, Y0, X1, Y1;

            public Span(float x0, float y0, float x1, float y1)
            {
                X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
            }

            public float CentreX => (X0 + X1) / 2f;
        }

        // Frame keep-out of a header band (user 2026-10-24: the plate lay over the frame's top border and cut the corner
        // runes): insets that keep it inside the frame's inner rect (the frame minus its 9-slice border) by `margin`, and
        // clear of every ornament (corner cap, junction piece) it still touches below that top inset - an ornament left of
        // the band centre pushes the left edge past its right side, one right of it the right edge.
        public static void FrameKeepOut(Span band, Span inner, System.Collections.Generic.IReadOnlyList<Span> ornaments, float margin, out float left, out float right, out float top)
        {
            top = Math.Max(0f, band.Y1 - (inner.Y1 - margin));
            left = Math.Max(0f, inner.X0 + margin - band.X0);
            right = Math.Max(0f, band.X1 - (inner.X1 - margin));
            var y1 = band.Y1 - top;
            if (ornaments == null) return;
            for (var n = 0; n < ornaments.Count; n++)
            {
                var o = ornaments[n];
                if (o.Y1 <= band.Y0 || o.Y0 >= y1 || o.X1 <= band.X0 || o.X0 >= band.X1) continue;
                if (o.CentreX < band.CentreX) left = Math.Max(left, o.X1 + margin - band.X0);
                else right = Math.Max(right, band.X1 - (o.X0 - margin));
            }
        }

        // Plate and title insets (header-local) with a frame keep-out: the plate stays centred (one inset both sides, the
        // larger of its own and the keep-outs), the title keeps its own margin inside the plate and stays centred too.
        public static void PlateInFrame(float plateInset, float titleLeft, float titleRight, float keepLeft, float keepRight,
            out float plateLeft, out float plateRight, out float titleL, out float titleR)
        {
            var p = Math.Max(plateInset, Math.Max(keepLeft, keepRight));
            plateLeft = plateRight = p;
            var margin = Math.Max(0f, Math.Min(titleLeft, titleRight) - plateInset);
            var t = Math.Max(Math.Max(titleLeft, titleRight), p + margin);
            titleL = titleR = t;
        }

        // Title size: the header's one size, stepped down only when the title is wider than its room (never below
        // minShare of it).
        public static float FitFont(float size, float naturalWidth, float room, float minShare)
        {
            if (!(naturalWidth > room) || !(naturalWidth > 0f)) return size;
            return Math.Max(size * minShare, size * room / naturalWidth);
        }

        // Every header part is a share of the header height, so one height = one look on every window.
        public const float InsetShare = 0.5f, IconShare = 0.8f, GapShare = 0.25f, PlateInsetShare = 0.05f, FontShare = 0.5f;

        // Header height in canvas units: the theme token x the skin scale (the same on every window).
        public static float HeaderHeight(float token, float skinScale) => Math.Max(1f, token) * (skinScale > 0f ? skinScale : 1f);

        // Side reserved on BOTH sides of the title (symmetric: the title centre is the band centre whatever the slots
        // hold): edge inset + the wider of the icon slot (left) and the aux slot (right).
        public static float TitleSide(float h, bool icon, float auxW)
        {
            var iconSlot = icon ? h * (IconShare + GapShare) : 0f;
            var auxSlot = auxW > 0f ? auxW + h * GapShare : 0f;
            return h * InsetShare + Math.Max(iconSlot, auxSlot);
        }

        // How far the icon / aux slots move in from the band edges: the plate's frame keep-out beyond the plate's own
        // inset (PlateInFrame narrows the plate by the same amount, so the slots stay inside the drawn plate).
        public static float SlotShift(float keepOut, float h) => Math.Max(0f, keepOut - h * PlateInsetShare);

        // Canvas slots of a header band: small icon left, aux value right (both vertically centred, IconShare of the
        // height tall), the title box between the symmetric sides.
        public static void Slots(Box band, bool icon, float auxW, out Box iconBox, out Box auxBox, out Box titleBox)
        {
            var h = band.H;
            var s = h * IconShare;
            var y = band.Y + (h - s) / 2f;
            iconBox = icon ? new Box(band.X + h * InsetShare, y, s, s) : default;
            auxBox = auxW > 0f ? new Box(band.Right - h * InsetShare - auxW, y, auxW, s) : default;
            var side = Math.Min(TitleSide(h, icon, auxW), band.W / 2f);
            titleBox = new Box(band.X + side, band.Y, Math.Max(0f, band.W - 2f * side), h);
        }

        // The same in the header's own units (w x h rect): plate inset, title insets left / right, title font size.
        public static void LocalSlots(float w, float h, bool icon, float auxW, out float plateInset, out float titleLeft, out float titleRight, out float fontSize)
        {
            plateInset = h * PlateInsetShare;
            titleLeft = titleRight = Math.Min(TitleSide(h, icon, auxW), w / 2f);
            fontSize = h * FontShare;
        }

        // Offset during a drag: the offset at drag start plus the pointer travel (screen px, y up) in canvas units (y down).
        public static void DragOffset(float startDx, float startDy, float fromX, float fromY, float toX, float toY, float scale, out float dx, out float dy)
        {
            var s = scale > 0f ? scale : 1f;
            dx = startDx + (toX - fromX) / s;
            dy = startDy - (toY - fromY) / s;
        }

        // A left double-click on the band resets the window to its default.
        public static bool IsReset(int clickCount, bool leftButton) => leftButton && clickCount >= 2;

        // Shown title: rich-text tags and surrounding space removed; empty -> the fallback (never an empty header).
        public static string Title(string text, string fallback)
        {
            var clean = StripTags(text);
            if (clean.Length > 0) return clean;
            var f = StripTags(fallback);
            return f.Length > 0 ? f : "-";
        }

        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            var depth = 0;
            foreach (var ch in text)
            {
                if (ch == '<') { depth++; continue; }
                if (ch == '>' && depth > 0) { depth--; continue; }
                if (depth == 0) sb.Append(ch == '\n' || ch == '\r' || ch == '\t' ? ' ' : ch);
            }
            return sb.ToString().Trim();
        }
    }
}
