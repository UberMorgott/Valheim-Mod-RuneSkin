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

        // The face's own material (main-menu rune preset: no added outline) plus a soft dark underlay; for big texts
        // over the world (centre message). Run 20261022-210451: the outlined copy (width 0.22) ate Norsebold's thin
        // strokes, the text read grey / near-black (contrast 2.9 on white, 1.9 on black).
        public static bool ApplyShadowed(TMP_Text text, string fontToken, string look)
        {
            var font = ThemeTokens.GetFont(fontToken);
            if (!text || !font || !font.material) return false;
            text.font = font;
            text.fontStyle &= ~FontStyles.Bold;
            if (!(Cache.TryGetValue((font, look), out var entry) && entry.Key))
            {
                var m = new Material(font.material) { name = font.material.name + " (RuneSkin " + look + ")" };
                Cache[(font, look)] = entry = new KeyValuePair<Material, int>(m, -1);
            }
            if (entry.Value != ThemeTokens.Revision)
            {
                Underlay(entry.Key);
                Cache[(font, look)] = new KeyValuePair<Material, int>(entry.Key, ThemeTokens.Revision);
            }
            text.fontSharedMaterial = entry.Key;
            return true;
        }
        // Soft dark drop shadow (TMP underlay) on a RuneSkin look material: contrast over bright backgrounds where an
        // outline alone reads thin. Tokens color.text.underlay, size.text.underlay.{offset,dilate,softness}.
        public static void Underlay(Material m)
        {
            if (!m || !m.HasProperty(ShaderUtilities.ID_UnderlayColor)) return;
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, ThemeTokens.GetColor("color.text.underlay", new Color(0f, 0f, 0f, 0.85f)));
            var o = ThemeTokens.GetFloat("size.text.underlay.offset", 0.6f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, o);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -o);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, ThemeTokens.GetFloat("size.text.underlay.dilate", 0.35f));
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, ThemeTokens.GetFloat("size.text.underlay.softness", 0.55f));
        }
        // Outlined + dilated face with the soft dark underlay on top: small texts over the world that must read on
        // bright sky and foliage (enemy overhead names).
        public static bool ApplyNameplate(TMP_Text text, string fontToken, string look, float dilate)
        {
            if (!Apply(text, fontToken, look, dilate)) return false;
            Underlay(text.fontSharedMaterial);
            return true;
        }
        // THE HUD number style (user call 2026-10-23: every white number readable on a white backdrop): body sans, dark
        // outline Hud.NumberOutline (SDF width, ConfigurationManager; theme color.text.outline), dilate
        // size.hud.value.dilate, soft dark underlay (Underlay). Bar values, slot keys / counts / timers, status timers,
        // summon states, cooldowns.
        public static bool ApplyHudNumber(TMP_Text text)
        {
            var font = ThemeTokens.GetFont("font.body");
            if (!text || !font) return false;
            text.font = font;
            text.fontStyle &= ~FontStyles.Bold;
            var m = Outlined(font, "hudnumber", Plugin.NumberOutline?.Value ?? 0.18f, ThemeTokens.GetFloat("size.hud.value.dilate", 0.2f));
            if (!m) return false;
            Underlay(m);
            text.fontSharedMaterial = m;
            return true;
        }

        // Font + outline material on a live text; false when the font token has no asset (text left as it is).
        public static bool Apply(TMP_Text text, string fontToken, string look, float dilate = 0f)
        {
            var font = ThemeTokens.GetFont(fontToken);
            if (!text || !font) return false;
            text.font = font;
            text.fontStyle &= ~FontStyles.Bold; // two single-weight faces (PT Sans, Norsebold): never a faux bold
            var m = Outlined(font, look, ThemeTokens.GetFloat("size.text.outline", 0.22f), dilate);
            if (m) text.fontSharedMaterial = m;
            return true;
        }
    }
}
