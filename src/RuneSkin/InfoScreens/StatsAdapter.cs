using HarmonyLib;
using RuneSkin.Ownership;

namespace RuneSkin.InfoScreens
{
    // Family "CharacterStats" hooks (installed only while owned, with Inventory): build after every other
    // InventoryGui.Awake postfix; Escape / B closes the window before the inventory (InventoryGui.Update :513-542 reads
    // the same key, so that frame's Update is skipped); the window closes with the inventory (InventoryGui.Hide).
    [HarmonyPatch]
    public static class StatsAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly)).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.CharacterStats);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.CharacterStats)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { StatsWindow.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] character stats window build failed: {e}"); }
        }

        // First among the InventoryGui.Update prefixes: the stats window sits above the inventory, so its Escape / B is
        // read before any mod's (a later prefix never runs once an earlier one returns false - HarmonyX; run 20261026-184538:
        // with a worn AdventureBackpacks backpack the window stayed open on Escape).
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Update_Prefix(InventoryGui __instance) => StatsWindow.HandleBack(__instance);

        // Real attack timing for the damage-per-second line (AttackTimer): a local player attack starts here ...
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        [HarmonyPostfix]
        private static void StartAttack_Postfix(Humanoid __instance, bool __result, bool secondaryAttack)
        {
            if (__result && __instance == Player.m_localPlayer) AttackTimer.Started(__instance.m_currentAttack, secondaryAttack);
        }

        // ... and ends when the humanoid leaves its attack animation (Humanoid.InAttack, checked each attack update).
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateAttack))]
        [HarmonyPostfix]
        private static void UpdateAttack_Postfix(Humanoid __instance)
        {
            if (!AttackTimer.Running || __instance != Player.m_localPlayer) return;
            // The animator enters the attack state a frame or two after StartAttack: only an attack seen running ends.
            if (__instance.InAttack()) AttackTimer.Seen();
            else AttackTimer.Ended();
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void Hide_Postfix() => StatsWindow.Instance?.Close();
    }
}
