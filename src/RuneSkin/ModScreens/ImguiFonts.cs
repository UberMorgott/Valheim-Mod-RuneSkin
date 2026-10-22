using System;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Ownership;
using RuneSkin.Theme;
using UnityEngine;

namespace RuneSkin.ModScreens
{
    // IMGUI windows (family ModUi): ConfigurationManager (dev only), DisplayBepInExInfo / ValheimBuildCamera badges and
    // any OnGUI drawn with GUI.skin. IMGUI draws with a UnityEngine.Font: the theme body face as the bundle's legacy
    // dynamic Font (ThemeTokens.LegacyFont), bold styles the display face with the Bold flag cleared (LegacyFaces: no faux
    // bold on single-weight faces). GUI.skin is only reachable inside OnGUI: one check per OnGUI call (no allocation),
    // written once per skin; ConfigurationManager rebuilds its static styles in CreateStyles.
    public sealed class ImguiFonts : MonoBehaviour
    {
        private static Font _font, _bold;
        private static FieldInfo[] _cmStyles;
        private GUISkin _done;

        public static void Init(GameObject host, Harmony harmony)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.ModUi)) return;
            _font = ThemeTokens.LegacyFont(LegacyFaces.Body);
            _bold = ThemeTokens.LegacyFont(LegacyFaces.FaceToken(LegacyFaces.Body, true));
            if (!_font)
            {
                Plugin.Log.LogInfo("[RuneSkin] IMGUI fonts: no legacy body face in the bundle, IMGUI windows keep their font");
                return;
            }
            Plugin.Log.LogInfo($"[RuneSkin] IMGUI fonts: body '{_font.name}', bold '{(_bold ? _bold.name : "-")}'");
            host.AddComponent<ImguiFonts>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) Hook(a, harmony);
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => Hook(args.LoadedAssembly, harmony);
        }

        private static void Hook(Assembly a, Harmony harmony)
        {
            if (_cmStyles != null) return;
            string asm;
            try { asm = a.GetName().Name; } catch { return; }
            if (asm != "ConfigurationManager") return;
            var styles = a.GetType("ConfigurationManager.ConfigurationManagerStyles");
            var create = styles?.GetMethod("CreateStyles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (create == null) return;
            _cmStyles = Array.FindAll(styles.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static), f => f.FieldType == typeof(GUIStyle));
            harmony.Patch(create, postfix: new HarmonyMethod(typeof(ImguiFonts), nameof(CreateStyles_Postfix)));
        }

        private static void CreateStyles_Postfix()
        {
            if (!_font || _cmStyles == null) return;
            foreach (var f in _cmStyles)
                if (f.GetValue(null) is GUIStyle s) Face(s, true);
        }

        // Bold / BoldAndItalic styles take the bold face without the synthetic Bold flag; the rest the body face (or,
        // in a skin, the skin's font: null keeps GUISkin.font).
        private static void Face(GUIStyle s, bool setBody)
        {
            if (s == null) return;
            if ((s.fontStyle == FontStyle.Bold || s.fontStyle == FontStyle.BoldAndItalic) && _bold)
            {
                s.font = _bold;
                s.fontStyle = s.fontStyle == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal;
            }
            else if (setBody) s.font = _font;
        }

        private void OnGUI()
        {
            var skin = GUI.skin;
            if (!skin || skin == _done || !_font) return;
            _done = skin;
            skin.font = _font;
            foreach (GUIStyle s in skin) Face(s, false);
        }
    }
}
