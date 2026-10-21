using System.Globalization;
using UnityEngine;

namespace RuneSkin.Theme
{
    // "#RRGGBB" / "#RRGGBBAA" parser. Managed only (ColorUtility is a native call), so theme validation runs in tests.
    public static class HexColor
    {
        public static bool TryParse(string value, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(value) || value[0] != '#' || (value.Length != 7 && value.Length != 9))
                return false;
            if (!uint.TryParse(value.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
                return false;
            if (value.Length == 7) v = (v << 8) | 0xFF;
            color = new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
            return true;
        }
    }
}
