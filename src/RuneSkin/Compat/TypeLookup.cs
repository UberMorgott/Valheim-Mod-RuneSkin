using System;

namespace RuneSkin.Compat
{
    // Optional-mod type by its full name (namespace + name, nested types with '+'), null when no loaded assembly has it.
    // Replaces HarmonyLib.AccessTools.TypeByName on every RuneSkin path: HarmonyX 2.9 (BepInEx 5.4.23 core) implements it as
    // Type.GetType(name) and then a linear scan of AllTypes() (Assembly.GetTypes() of every loaded assembly, a fresh array
    // each, twice when the type is absent: FullName, then Name) - game, Unity, BepInEx and every plugin assembly. One lookup cost ms to tens of ms,
    // and the first inventory open ran two (AbApi.Resolve from InventorySkin.FindBackpack, CraftingSkin.VneiLookup)
    // (rp-open-cold run 20261028-000356: Update_Postfix 43.4 ms, CraftingSkin.LateUpdate 49.7 ms on frames #0-#1).
    // Assembly.GetType(name) is the runtime's own name-table lookup: no type list is built. Not cached: a plugin assembly
    // loaded after an early miss is still found later, and a hit costs one lookup per loaded assembly.
    public static class TypeLookup
    {
        public static Type ByName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            var type = Type.GetType(fullName, false);
            if (type != null) return type;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                    type = null; // a dynamic or broken assembly: not ours to read
                }
                if (type != null) return type;
            }
            return null;
        }
    }
}
