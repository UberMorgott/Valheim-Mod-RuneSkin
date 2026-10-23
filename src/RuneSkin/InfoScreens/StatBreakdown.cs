using System;
using System.Text;

namespace RuneSkin.InfoScreens
{
    // One stat's composition, shared by every section of the stats window: a base value, then attributed modifiers
    // (each with its source: item, status effect, skill, enchantments), then the result. Pure data + text rendering
    // (no Unity), filled by StatsReader.Explain only while its row is hovered; the pool is fixed, nothing allocates
    // while it is reused.
    internal sealed class StatBreakdown
    {
        internal enum Op
        {
            Base,   // starting value ("Оружие: 52")
            Add,    // flat delta (+12 / -3)
            Pct,    // percent of the base (+10%)
            Mul,    // factor (x1,2)
            Range,  // factor range (x0,55-0,85)
            Note,   // plain text line (formula hint, "per block")
            Result, // final value, closes the list
        }

        internal struct Line
        {
            public Op Op;
            public string Label;
            public string Source;
            public float Value, Value2;
            public string Unit;
            public int Sem; // -1 worse, 0 neutral, 1 better
            public int Color; // >= 0: index into the element colours (damage type share), else none
        }

        public const int Capacity = 32;
        private readonly Line[] _lines = new Line[Capacity];

        public int Count { get; private set; }
        public string Title { get; private set; } = "";
        public bool Overflow { get; private set; }

        public Line this[int i] => _lines[i];

        public void Clear(string title)
        {
            Title = title ?? "";
            Count = 0;
            Overflow = false;
        }

        private void Push(Op op, string label, string source, float value, float value2, string unit, int sem, int color = -1)
        {
            if (Count >= Capacity)
            {
                Overflow = true;
                return;
            }
            _lines[Count++] = new Line { Op = op, Label = label ?? "", Source = source, Value = value, Value2 = value2, Unit = unit ?? "", Sem = sem, Color = color };
        }

        public void Base(string label, float value, string unit = "") => Push(Op.Base, label, null, value, 0f, unit, 0);

        // A flat delta; skipped when it rounds to nothing. `higherIsBetter` colours it (a cost: false).
        public void Add(string label, string source, float delta, bool higherIsBetter, string unit = "", float epsilon = 0.05f)
        {
            if (Math.Abs(delta) < epsilon) return;
            Push(Op.Add, label, source, delta, 0f, unit, Sem(delta, higherIsBetter));
        }

        // A share of the base in percent (0.1 = +10%); skipped below 0.5%.
        public void Pct(string label, string source, float share, bool higherIsBetter)
        {
            if (Math.Abs(share) < 0.005f) return;
            Push(Op.Pct, label, source, share * 100f, 0f, "%", Sem(share, higherIsBetter));
        }

        public void Mul(string label, string source, float factor, bool higherIsBetter)
        {
            if (Math.Abs(factor - 1f) < 0.005f) return;
            Push(Op.Mul, label, source, factor, 0f, "", Sem(factor - 1f, higherIsBetter));
        }

        public void Range(string label, string source, float min, float max) => Push(Op.Range, label, source, min, max, "", 0);

        public void Note(string text, int color = -1) => Push(Op.Note, text, null, 0f, 0f, "", 0, color);

        // A typed share line ("режущий 82  (87%)"): value + share, tinted with the element colour `color`.
        public void Share(string label, float value, float share, int color) => Push(Op.Add, label, null, value, share, "share", 0, color);

        public void Result(string label, float value, string unit = "") => Push(Op.Result, label, null, value, 0f, unit, 0);

        public void ResultRange(string label, float min, float max, string unit = "") => Push(Op.Result, label, null, min, max, unit, 0);

        private static int Sem(float delta, bool higherIsBetter)
        {
            if (Math.Abs(delta) < 1e-4f) return 0;
            return (delta > 0f) == higherIsBetter ? 1 : -1;
        }

        // Sum of the line values of one op (tests: the attributed parts add up to the total).
        public float Sum(Op op)
        {
            var s = 0f;
            for (var i = 0; i < Count; i++)
                if (_lines[i].Op == op) s += _lines[i].Value;
            return s;
        }

        // Rich text for a tooltip: "label · source   value" per line, colours by semantic, the result in bold under a
        // rule. `elem` = element colours (hex, no #) for typed shares.
        public void Render(StringBuilder sb, bool comma, string goodHex, string badHex, string subHex, string[] elem = null)
        {
            for (var i = 0; i < Count; i++)
            {
                var l = _lines[i];
                if (sb.Length > 0) sb.Append('\n');
                if (l.Op == Op.Note)
                {
                    if (l.Color >= 0 && elem != null && l.Color < elem.Length) sb.Append("<color=#").Append(elem[l.Color]).Append('>').Append(l.Label).Append("</color>");
                    else sb.Append("<color=#").Append(subHex).Append('>').Append(l.Label).Append("</color>");
                    continue;
                }
                if (l.Op == Op.Result) sb.Append("<b>");
                if (l.Color >= 0 && elem != null && l.Color < elem.Length) sb.Append("<color=#").Append(elem[l.Color]).Append('>').Append(l.Label).Append("</color>");
                else sb.Append(l.Label);
                if (!string.IsNullOrEmpty(l.Source)) sb.Append(" <color=#").Append(subHex).Append(">(").Append(l.Source).Append(")</color>");
                sb.Append(":  ");
                StatsMath.Open(sb, l.Sem, goodHex, badHex);
                switch (l.Op)
                {
                    case Op.Base:
                    case Op.Result:
                        if (l.Op == Op.Result && l.Value2 > 0f && Math.Abs(l.Value2 - l.Value) > 0.05f)
                        {
                            StatsMath.AppendCompact(sb, l.Value, comma).Append('–');
                            StatsMath.AppendCompact(sb, l.Value2, comma);
                        }
                        else StatsMath.AppendCompact(sb, l.Value, comma);
                        AppendUnit(sb, l.Unit);
                        break;
                    case Op.Add:
                        if (l.Unit == "share")
                        {
                            StatsMath.AppendCompact(sb, l.Value, comma);
                            sb.Append("  <color=#").Append(subHex).Append('>');
                            StatsMath.AppendInt(sb, l.Value2 * 100f, false).Append("%</color>");
                            break;
                        }
                        if (l.Value > 0f) sb.Append('+');
                        else sb.Append('−');
                        StatsMath.AppendCompact(sb, Math.Abs(l.Value), comma);
                        AppendUnit(sb, l.Unit);
                        break;
                    case Op.Pct:
                        sb.Append(l.Value > 0f ? '+' : '−');
                        StatsMath.AppendCompact(sb, Math.Abs(l.Value), comma).Append('%');
                        break;
                    case Op.Mul:
                        sb.Append('×');
                        StatsMath.AppendFactor(sb, l.Value, comma);
                        break;
                    case Op.Range:
                        sb.Append('×');
                        StatsMath.AppendFactor(sb, l.Value, comma).Append('–');
                        StatsMath.AppendFactor(sb, l.Value2, comma);
                        break;
                }
                StatsMath.Close(sb, l.Sem);
                if (l.Op == Op.Result) sb.Append("</b>");
            }
        }

        private static void AppendUnit(StringBuilder sb, string unit)
        {
            if (string.IsNullOrEmpty(unit)) return;
            if (unit[0] != '%' && unit[0] != '/') sb.Append(' ');
            sb.Append(unit);
        }
    }
}
