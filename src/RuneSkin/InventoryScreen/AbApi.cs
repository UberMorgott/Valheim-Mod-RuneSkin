using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using RuneSkin.Compat;

namespace RuneSkin.InventoryScreen
{
    // AdventureBackpacks fork 1.9.15+ docking API (AdventureBackpacks.API.ABAPI: GetBackpackPanel,
    // GetBackpackPanelInventory), soft by reflection: the fork shows its backpack panel whenever a backpack is worn and,
    // while RuneSkin Supports("panel.dock"), never positions it nor hides crafting. Older forks without it: no backpack
    // dock (the pack ships the fork with the API).
    public static class AbApi
    {
        private static bool _resolved;
        private static Func<RectTransform> _panel;
        private static Func<Inventory> _inventory;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            var api = AccessTools.TypeByName(ModCompat.AbApiType);
            var panel = api?.GetMethod("GetBackpackPanel", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var inventory = api?.GetMethod("GetBackpackPanelInventory", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (panel != null && panel.ReturnType == typeof(RectTransform))
                _panel = (Func<RectTransform>)Delegate.CreateDelegate(typeof(Func<RectTransform>), panel);
            if (inventory != null && inventory.ReturnType == typeof(Inventory))
                _inventory = (Func<Inventory>)Delegate.CreateDelegate(typeof(Func<Inventory>), inventory);
            Plugin.Log.LogInfo($"[RuneSkin] AdventureBackpacks docking API: {(_panel != null ? "found" : "absent (no backpack dock)")}");
        }

        public static RectTransform Panel()
        {
            Resolve();
            try { return _panel?.Invoke(); }
            catch { return null; }
        }

        public static Inventory Inventory()
        {
            Resolve();
            try { return _inventory?.Invoke(); }
            catch { return null; }
        }
    }
}
