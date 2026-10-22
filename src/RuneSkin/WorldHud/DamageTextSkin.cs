using System;
using HarmonyLib;
using RuneSkin.Diagnostics;
using RuneSkin.PlayerHud;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // Family "DamageText" (art\README.md "M4 sprites": damage text A, font only, no sprites). Vanilla clones
    // m_worldTextBase per number (AddInworldText, DamageText.cs:94) and writes a hard-coded colour per TextType
    // (:105-115), the size (m_largeFontSize / m_smallFontSize, :39-41, Bonus x1.5) and later only the alpha
    // (UpdateWorldTexts :77). RuneSkin: the template text gets the body font (the display serif has old-style digits),
    // a dark outline and a little dilate (bold read); sizes from tokens; each new number's colour from
    // color.damage.<type> (self damage stays its own red token). No prefab swap.
    public static class DamageTextSkin
    {
        public const string ScreenId = "DamageText";
        private static readonly HudArt Art = new HudArt(ScreenId);
        private static Transform _template;

        internal static void Attach(DamageText dt)
        {
            if (!dt || !dt.m_worldTextBase) return;
            _template = dt.m_worldTextBase.transform;
            var text = dt.m_worldTextBase.GetComponentInChildren<TMP_Text>(true);
            var ok = TextLook.Apply(text, "font.body", "damage", ThemeTokens.GetFloat("size.damage.dilate", 0.15f));
            if (text) text.fontStyle |= FontStyles.Bold;
            Art.Note(text, "damage.text", ok, "damage number template (body font, outline, bold)");
            dt.m_largeFontSize = Mathf.RoundToInt(ThemeTokens.GetFloat("size.damage.font", 24f));
            dt.m_smallFontSize = Mathf.RoundToInt(ThemeTokens.GetFloat("size.damage.font.small", 15f));
            Plugin.Log.LogInfo("[RuneSkin] damage text restyled");
        }

        public static string ColorToken(DamageText.TextType type, bool mySelf)
        {
            if (mySelf && type <= DamageText.TextType.Immune) return "color.damage.self";
            switch (type)
            {
                case DamageText.TextType.Weak: return "color.damage.weak";
                case DamageText.TextType.Bonus: return "color.damage.bonus";
                case DamageText.TextType.Resistant: return "color.damage.resistant";
                case DamageText.TextType.Immune: return "color.damage.immune";
                case DamageText.TextType.Heal: return "color.damage.heal";
                case DamageText.TextType.TooHard: return "color.damage.toohard";
                case DamageText.TextType.Blocked: return "color.damage.blocked";
                default: return "color.damage.normal";
            }
        }

        // The number AddInworldText just appended (last of m_worldTexts): colour from the theme, alpha kept.
        internal static void Recolor(DamageText dt, DamageText.TextType type, bool mySelf, string text)
        {
            var list = dt.m_worldTexts;
            if (list == null || list.Count == 0) return;
            var inst = list[list.Count - 1];
            if (inst == null || !inst.m_textField) return;
            // "0" damage keeps vanilla's grey (DamageText.cs:105-108).
            if (mySelf && text == "0") return;
            var c = ThemeTokens.GetColor(ColorToken(type, mySelf), inst.m_textField.color);
            c.a = inst.m_textField.color.a;
            inst.m_textField.color = c;
        }

        public static void Record()
        {
            if (_template) SkinDiagnostics.Record(Art.Report(_template));
        }
    }

    [HarmonyPatch]
    public static class DamageTextAdapter
    {
        [HarmonyPatch(typeof(DamageText), nameof(DamageText.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(DamageText __instance)
        {
            try { DamageTextSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] damage text restyle failed (vanilla numbers stay): {e}"); }
        }

        // Count before: AddInworldText returns early past m_maxTextDistance (nothing appended, nothing to recolour).
        [HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
        [HarmonyPrefix]
        private static void AddInworldText_Prefix(DamageText __instance, out int __state) =>
            __state = __instance.m_worldTexts != null ? __instance.m_worldTexts.Count : 0;

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void AddInworldText_Postfix(DamageText __instance, DamageText.TextType type, string text, bool mySelf, int __state)
        {
            if (__instance.m_worldTexts == null || __instance.m_worldTexts.Count <= __state) return;
            try { DamageTextSkin.Recolor(__instance, type, mySelf, text); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] damage text colour failed: {e}"); }
        }
    }
}
