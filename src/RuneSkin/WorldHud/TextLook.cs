using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // Shared TMP looks for texts drawn over the world (enemy names, damage numbers, messages): the theme font with a
    // dark SDF outline (+ optional dilate for bold numbers). One material per (font, look), restyled on theme reload.
    public static class TextLook
    {
        // One material per (font, look) for the session. A theme reload updates it in place (live texts, templates and
        // their clones keep pointing at it): a new copy per revision leaked one material per reload, destroying the old
        // one would leave those texts on a destroyed material.
        private static readonly Dictionary<(TMP_FontAsset font, string look), KeyValuePair<Material, int>> Cache =
            new Dictionary<(TMP_FontAsset, string), KeyValuePair<Material, int>>();

        public static Material Outlined(TMP_FontAsset font, string look, float width, float dilate)
        {
            if (!font || !font.material) return null;
            if (Cache.TryGetValue((font, look), out var entry) && entry.Key)
            {
                if (entry.Value == ThemeTokens.Revision) return entry.Key;
                Style(entry.Key, width, dilate);
                Cache[(font, look)] = new KeyValuePair<Material, int>(entry.Key, ThemeTokens.Revision);
                return entry.Key;
            }
            var m = new Material(font.material) { name = font.material.name + " (RuneSkin " + look + ")" };
            Style(m, width, dilate);
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            Cache[(font, look)] = new KeyValuePair<Material, int>(m, ThemeTokens.Revision);
            return m;
        }

        private static void Style(Material m, float width, float dilate)
        {
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            m.SetColor(ShaderUtilities.ID_OutlineColor, ThemeTokens.GetColor("color.text.outline", new Color(0.02f, 0.035f, 0.045f, 0.94f)));
            m.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
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
