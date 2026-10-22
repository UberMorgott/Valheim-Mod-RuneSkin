using System;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Ownership;
using UnityEngine;

namespace RuneSkin.ModScreens
{
    // Jotunn GUIManager soft hook (family ModUi; no reference to Jotunn, reflection only). Jotunn builds windows from
    // plain uGUI objects without a component of its own, so the fallback restyler would not see them as foreign:
    // CustomGUIFront / CustomGUIBack (its two canvases under the game's GUI root, GUIManager.TryCreateGUI; announced by
    // the static event OnCustomGUIAvailable, also on every scene change) and every GameObject a GUIManager Create*
    // builder returns (buttons, texts, inputs, toggles, dropdowns, scroll views, wood panels, key binding fields; mods
    // place them anywhere, also inside vanilla windows) become anchors of fallback units (ForeignUi.AddAnchor).
    // Callers in the pack: StarLevelSystem config UI / quick config / corner button, VNEI, Epic Loot, Jotunn's own
    // ModCompatibility window, colour / gradient pickers (children of CustomGUIFront).
    public static class JotunnHook
    {
        private static Harmony _harmony;
        private static bool _done;
        private static PropertyInfo _front, _back;

        public static void Init(Harmony harmony)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.ModUi) || !ForeignUi.Active) return;
            _harmony = harmony;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                OnAssembly(assembly);
            if (!_done)
                AppDomain.CurrentDomain.AssemblyLoad += (_, args) => OnAssembly(args.LoadedAssembly);
        }

        private static void OnAssembly(Assembly assembly)
        {
            if (_done) return;
            string name;
            try { name = assembly.GetName().Name; }
            catch { return; }
            if (name != "Jotunn") return;
            _done = true;
            try
            {
                var gui = assembly.GetType("Jotunn.Managers.GUIManager");
                if (gui == null) { Plugin.Log.LogWarning("[RuneSkin] Jotunn without Jotunn.Managers.GUIManager: its windows are styled only where they carry a mod component"); return; }
                _front = gui.GetProperty("CustomGUIFront", BindingFlags.Public | BindingFlags.Static);
                _back = gui.GetProperty("CustomGUIBack", BindingFlags.Public | BindingFlags.Static);
                var ev = gui.GetEvent("OnCustomGUIAvailable", BindingFlags.Public | BindingFlags.Static);
                if (ev != null) ev.AddEventHandler(null, (Action)Available);
                var post = new HarmonyMethod(typeof(JotunnHook), nameof(Create_Postfix)) { priority = Priority.Last };
                var n = 0;
                foreach (var m in gui.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!m.Name.StartsWith("Create", StringComparison.Ordinal) || m.ReturnType != typeof(GameObject) || m.IsGenericMethodDefinition) continue;
                    try { _harmony.Patch(m, postfix: post); n++; }
                    catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] Jotunn GUIManager.{m.Name} hook not installed: {e.GetBaseException().Message}"); }
                }
                Available();
                Plugin.Log.LogInfo($"[RuneSkin] Jotunn GUIManager hooked: {n} builders, OnCustomGUIAvailable={(ev != null)}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[RuneSkin] Jotunn GUIManager hook failed (its windows keep Jotunn's look): {e}");
            }
        }

        private static void Available()
        {
            if (_front?.GetValue(null) is GameObject f && f) ForeignUi.AddAnchor(f.transform);
            if (_back?.GetValue(null) is GameObject b && b) ForeignUi.AddAnchor(b.transform);
        }

        private static void Create_Postfix(GameObject __result)
        {
            if (__result) ForeignUi.AddAnchor(__result.transform);
        }
    }
}
