using HarmonyLib;
using UnityEngine;

namespace RuneSkin.Compat
{
    // Epic Loot 0.15.4 shard slot panel (no reference; EpicLoot\src\ShardStones\ShardSlotPanel.cs + ShardUI.cs): a window of
    // its own beside the inventory, root "EpicLoot_ShardSlots" built once per InventoryGui as a sibling of m_player (children
    // "Bkg" = clone of m_player/Bkg, "selected_frame" clone, "Title" = clone of m_containerName, "Grid" = an InventoryGrid
    // whose cells are m_playerGrid's element prefab, m_gridRoot "Root"). It replaced 0.15.0's SocketsUI, which showed the
    // sockets in vanilla m_container. Every frame ShardUI's InventoryGui.Update postfix calls ShardSlotPanel.Place (root
    // hung off m_player's bottom-right corner + ELConfig.ShardSlotPanelOffset, i.e. over the RuneSkin HUD strip) and then
    // Grid.UpdateInventory (hover hit test). The RuneSkin inventory lays the panel out as a dock window (InventorySkin
    // .ArrangeSockets, LateUpdate like every window); while it does, Place stands down so the root stays where the skin put
    // it for the next frame's hit test (Codex design call 2026-10-29: moving the root after Place, or its children only,
    // leaves the hit test a frame behind while the window is dragged). Without the RuneSkin layout Place runs as shipped.
    public static class EpicLootShardSlots
    {
        public const string PanelType = "EpicLoot.ShardStones.ShardSlotPanel";
        public const string RootName = "EpicLoot_ShardSlots";
        public const string GridName = "Grid";
        public const string TitleName = "Title";

        internal static Harmony Harmony;
        private static bool _tried;

        // Place is patched: only then may the skin take the panel over (else Epic Loot would put the root back before its hit
        // test every frame); without it the panel keeps Epic Loot's own placement and look.
        public static bool Patched { get; private set; }

        // True while the RuneSkin inventory places the panel (InventorySkin.SkinPlacesShardSlots).
        public static System.Func<bool> SkinPlaces;

        // Patched once from InventoryGui.Awake (Epic Loot is loaded by then).
        public static void EnsurePatched()
        {
            if (_tried || Harmony == null) return;
            _tried = true;
            var type = TypeLookup.ByName(PanelType);
            if (type == null) return; // no Epic Loot, or one before 0.15.4 (no shard slot panel)
            var place = AccessTools.DeclaredMethod(type, "Place", System.Type.EmptyTypes);
            if (place == null || place.ReturnType != typeof(void))
            {
                Plugin.Log.LogWarning($"[RuneSkin] {PanelType}.Place() not found: Epic Loot places its shard slot panel itself (it may cover the HUD strip)");
                return;
            }
            try
            {
                Harmony.Patch(place, prefix: new HarmonyMethod(typeof(EpicLootShardSlots), nameof(Place_Prefix)));
                Patched = true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[RuneSkin] {PanelType}.Place patch failed (Epic Loot places its shard slot panel): {e.Message}"); }
        }

        private static bool Place_Prefix() => SkinPlaces == null || !SkinPlaces();

        // The panel root next to m_player (null until Epic Loot built it: first open of an item's slots).
        public static RectTransform Find(RectTransform player)
        {
            var parent = player ? player.parent : null;
            return parent ? parent.Find(RootName) as RectTransform : null;
        }

        public static InventoryGrid GridOf(RectTransform root)
        {
            var t = root ? root.Find(GridName) : null;
            return t ? t.GetComponent<InventoryGrid>() : null;
        }
    }
}
