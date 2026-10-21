using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RuneSkin.Theme
{
    // Theme file reader: a small strict JSON parser (objects, arrays, strings, numbers, true/false/null) mapped onto
    // ThemeFile. Managed only, so the shipped default theme is parsed and validated by the unit tests exactly as in
    // game (UnityEngine.JsonUtility is native and dropped array entries of the default theme in game).
    public static class ThemeJson
    {
        public static ThemeFile Parse(string json)
        {
            var p = new Reader(json);
            var root = p.Value() as Dictionary<string, object> ?? throw new FormatException("root is not an object");
            p.End();
            return new ThemeFile
            {
                schema = (int)Num(root, "schema"),
                name = Str(root, "name"),
                bundle = Str(root, "bundle"),
                colors = Arr(root, "colors").Select(o => new ColorEntry { token = Str(o, "token"), value = Str(o, "value") }).ToArray(),
                sprites = Arr(root, "sprites").Select(o => new AssetEntry { token = Str(o, "token"), asset = Str(o, "asset") }).ToArray(),
                fonts = Arr(root, "fonts").Select(o => new AssetEntry { token = Str(o, "token"), asset = Str(o, "asset") }).ToArray(),
                floats = Arr(root, "floats").Select(o => new FloatEntry { token = Str(o, "token"), value = (float)Num(o, "value") }).ToArray(),
                roles = Arr(root, "roles").Select(o => new RoleRule
                {
                    role = Str(o, "role"),
                    sprite = Str(o, "sprite"),
                    color = Str(o, "color"),
                    imageType = Str(o, "imageType"),
                    ppuMultiplier = (float)Num(o, "ppuMultiplier"),
                    preserveAspect = Bool(o, "preserveAspect"),
                    keepColor = Bool(o, "keepColor"),
                    fillCenter = Str(o, "fillCenter"),
                    font = Str(o, "font"),
                    textColor = Str(o, "textColor"),
                    stateNormal = Str(o, "stateNormal"),
                    stateHover = Str(o, "stateHover"),
                    statePressed = Str(o, "statePressed"),
                    stateSelected = Str(o, "stateSelected"),
                    stateDisabled = Str(o, "stateDisabled"),
                    spriteHover = Str(o, "spriteHover"),
                    spritePressed = Str(o, "spritePressed"),
                    spriteSelected = Str(o, "spriteSelected"),
                    spriteDisabled = Str(o, "spriteDisabled"),
                }).ToArray(),
                spriteRoles = Arr(root, "spriteRoles").Select(o => new SpriteRoleEntry { match = Str(o, "match"), role = Str(o, "role"), context = Str(o, "context") }).ToArray(),
                families = Arr(root, "families").Select(o => new FamilyEntry
                {
                    family = Str(o, "family"),
                    requires = o.TryGetValue("requires", out var r) && r is List<object> l ? l.OfType<string>().ToArray() : new string[0]
                }).ToArray(),
            };
        }

        private static string Str(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) ? v as string : null;

        private static double Num(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) && v is double d ? d : 0d;

        private static bool Bool(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) && v is bool b && b;

        private static IEnumerable<Dictionary<string, object>> Arr(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out var v) && v is List<object> l ? l.OfType<Dictionary<string, object>>() : Enumerable.Empty<Dictionary<string, object>>();

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s ?? throw new FormatException("empty");

            public void End()
            {
                Ws();
                if (_i != _s.Length) Fail("trailing characters");
            }

            public object Value()
            {
                Ws();
                if (_i >= _s.Length) Fail("unexpected end");
                switch (_s[_i])
                {
                    case '{': return Obj();
                    case '[': return List();
                    case '"': return Text();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    default: return Number();
                }
            }

            private Dictionary<string, object> Obj()
            {
                var o = new Dictionary<string, object>();
                _i++;
                Ws();
                if (Peek('}')) { _i++; return o; }
                while (true)
                {
                    Ws();
                    var key = Text();
                    Ws();
                    Expect(':');
                    if (o.ContainsKey(key)) Fail($"duplicate key '{key}'");
                    o[key] = Value();
                    Ws();
                    if (Peek(',')) { _i++; continue; }
                    Expect('}');
                    return o;
                }
            }

            private List<object> List()
            {
                var l = new List<object>();
                _i++;
                Ws();
                if (Peek(']')) { _i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    Ws();
                    if (Peek(',')) { _i++; continue; }
                    Expect(']');
                    return l;
                }
            }

            private string Text()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_i < _s.Length && _s[_i] != '"')
                {
                    var c = _s[_i++];
                    if (c != '\\') { sb.Append(c); continue; }
                    if (_i >= _s.Length) Fail("bad escape");
                    var e = _s[_i++];
                    switch (e)
                    {
                        case '"': case '\\': case '/': sb.Append(e); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) Fail("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: Fail($"bad escape \\{e}"); break;
                    }
                }
                Expect('"');
                return sb.ToString();
            }

            private double Number()
            {
                var start = _i;
                while (_i < _s.Length && NumberChar(_s[_i])) _i++;
                if (start != _i && double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                Fail("bad value");
                return 0d;
            }

            private static bool NumberChar(char c) => (c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.' || c == 'e' || c == 'E';

            private void Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) Fail($"expected {w}");
                _i += w.Length;
            }

            private void Ws()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(char c)
            {
                if (!Peek(c)) Fail($"expected '{c}'");
                _i++;
            }

            private void Fail(string what)
            {
                var line = 1 + _s.Take(Math.Min(_i, _s.Length)).Count(ch => ch == '\n');
                throw new FormatException($"{what} at line {line}");
            }
        }
    }
}
