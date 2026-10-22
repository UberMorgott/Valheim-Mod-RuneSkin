using System;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Ownership;
using RuneSkin.Theme;
using UnityEngine;

namespace RuneSkin.ModScreens
{
    // IMGUI windows (family ModUi): ConfigurationManager (dev only), DisplayBepInExInfo / ValheimBuildCamera badges and
    // any OnGUI drawn with GUI.skin. IMGUI draws with a UnityEngine.Font, so it takes the theme body face only where that
    // face carries a source font (ThemeTokens.LegacyFont: dynamic faces; the static bundle body face has none, then the
    // windows keep their font and the dump says so). GUI.skin is only reachable inside OnGUI: one check per OnGUI call
    // (no allocation), written once per skin; ConfigurationManager rebuilds its static styles in CreateStyles.
    public sealed class ImguiFonts : MonoBehaviour
    {
        private static Font _font;
        private static FieldInfo[] _cmStyles;
        private GUISkin _done;

        public static void Init(GameObject host, Harmony harmony)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.ModUi)) return;
            _font = ThemeTokens.LegacyFont("font.body");
            if (!_font)
            {
                Plugin.Log.LogInfo("[RuneSkin] IMGUI fonts: the theme body face has no source font (static bundle asset), IMGUI windows keep their font");
                return;
            }
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
                if (f.GetValue(null) is GUIStyle s) s.font = _font;
        }

        private void OnGUI()
        {
            var skin = GUI.skin;
            if (!skin || skin == _done || !_font) return;
            _done = skin;
            skin.font = _font;
        }
    }
}
