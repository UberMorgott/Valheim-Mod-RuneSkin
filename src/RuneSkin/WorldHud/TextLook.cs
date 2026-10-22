using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // Shared TMP looks for texts drawn over the world (enemy names, damage numbers, messages): the theme font with a
    // dark SDF outline (+ optional dilate for bold numbers). One material per (font, look), rebuilt on theme reload.
    public static class TextLook
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static int _revision = -1;

        public static Material Outlined(TMP_FontAsset font, string look, float width, float dilate)
        {
            if (!font || !font.material) return null;
            if (_revision != ThemeTokens.Revision) { Cache.Clear(); _revision = ThemeTokens.Revision; }
            var key = font.name + "|" + look;
            if (Cache.TryGetValue(key, out var m) && m) return m;
            m = new Material(font.material) { name = font.material.name + " (RuneSkin " + look + ")" };
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            m.SetColor(ShaderUtilities.ID_OutlineColor, ThemeTokens.GetColor("color.text.outline", new Color(0.02f, 0.035f, 0.045f, 0.94f)));
            m.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            Cache[key] = m;
            return m;
        }

        // Font + outline material on a live text; false when the font token has no asset (text left as it is).
        public static bool Apply(TMP_Text text, string fontToken, string look, float dilate = 0f)
        {
            var font = ThemeTokens.GetFont(fontToken);
            if (!text || !font) return false;
            text.font = font;
            var m = Outlined(font, look, ThemeTokens.GetFloat("size.text.outline", 0.22f), dilate);
            if (m) text.fontSharedMaterial = m;
            return true;
        }
    }
}
