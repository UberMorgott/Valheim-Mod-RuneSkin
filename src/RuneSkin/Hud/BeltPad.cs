using HarmonyLib;
using RuneSkin.Ownership;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Gamepad focus on the belt row (MorgottTweaks Belt, BeltBridge) while the inventory is open. The belt is no grid
    // row (own 10x1 store), so InventoryGrid's gamepad walk never reaches it: Down on the bottom row of the LOWEST open
    // grid (the container grid when a container is open, else the player grid; InventoryGui.MoveToLowerInventoryGrid
    // only links player -> container, InventoryGui.cs) moves the focus onto the belt row, Left / Right walk it
    // (BeltMath row order), Up returns to that grid at the same relative column. While focused, that grid's own
    // UpdateGamepad is skipped (no double move / double press). Presses mirror InventoryGrid.UpdateGamepad
    // (InventoryGrid.cs:177-200): A = select (pick up / drop, vanilla drag), X = the right-click action (toggle
    // auto-use), LTrigger+X = Move (stack to the inventory), RTrigger+X = use now (mouse Alt+RMB).
    // HudStrip draws the focused cell's hover frame + tooltip (Hover padFocus). Visuals of the focus: UI phase.
    [HarmonyPatch]
    public static class BeltPad
    {
        public static bool Focused { get; private set; }
        public static int Pos { get; private set; } = -1;
        private static InventoryGrid _from;

        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Hud);

        // Focused belt cell (kind, index), false when the focus is elsewhere.
        public static bool Cell(Player p, out int kind, out int index)
        {
            kind = 0;
            index = -1;
            if (!Focused || !p) return false;
            return BeltMath.RowCell(Pos, BeltBridge.Capacity(p, BeltMath.Food), BeltBridge.Capacity(p, BeltMath.Potion), out kind, out index);
        }

        public static void Release()
        {
            Focused = false;
            Pos = -1;
            _from = null;
        }

        // Before EAQS: its UpdateGamepad prefix returns false on direction presses (own spatial walk over its rows), which
        // would skip this one (Harmony skips later prefixes after a false).
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGamepad))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(InventoryScreen.EaqsBridge.PluginGuid)]
        private static bool UpdateGamepad_Prefix(InventoryGrid __instance)
        {
            var gui = InventoryGui.instance;
            var p = Player.m_localPlayer;
            if (!gui || !InventoryGui.IsVisible() || !p || !BeltBridge.On || ZInput.IsTouchActive() || !ZInput.IsExclusiveGamepadActive())
            {
                if (Focused) Release();
                return true;
            }
            if (!__instance.m_uiGroup || !__instance.m_uiGroup.IsActive || Console.IsVisible()) return true;
            var food = BeltBridge.Capacity(p, BeltMath.Food);
            var potion = BeltBridge.Capacity(p, BeltMath.Potion);
            var lowest = gui.IsContainerOpen() ? gui.m_containerGrid : gui.m_playerGrid;
            if (!Focused)
            {
                if (__instance != lowest || BeltMath.RowLength(food, potion) == 0 || !Down()) return true;
                if (__instance.m_selected.y < BottomRow(gui, __instance)) return true;
                Focused = true;
                _from = __instance;
                Pos = BeltMath.EntryPos(__instance.m_selected.x, __instance.GridWidth, food, potion);
                return false;
            }
            if (__instance != _from)
            {
                // another grid took the active group (container closed / opened): the belt focus ends there
                if (_from == null || !_from.m_uiGroup || !_from.m_uiGroup.IsActive) Release();
                return true;
            }
            if (BeltMath.RowLength(food, potion) == 0)
            {
                Release();
                return true;
            }
            if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyLStickLeft")) Pos = BeltMath.Step(Pos, -1, food, potion);
            if (ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyLStickRight")) Pos = BeltMath.Step(Pos, 1, food, potion);
            Pos = BeltMath.Step(Pos, 0, food, potion); // clamp after a capacity shrink
            if (ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyLStickUp"))
            {
                var sel = __instance.m_selected;
                sel.x = BeltMath.GridColumn(Pos, __instance.GridWidth, food, potion);
                sel.y = BottomRow(gui, __instance);
                Release();
                __instance.SetGamepadSelection(sel);
                var e = __instance.GetGamepadSelectedElement();
                if (e && e.TryGetComponent<Selectable>(out var s)) s.Select();
                return false;
            }
            if (!BeltMath.RowCell(Pos, food, potion, out var kind, out var index)) return false;
            switch (BeltMath.PadPress(ZInput.GetButtonDown("JoyButtonA"), ZInput.GetButtonDown("JoyButtonX"),
                        ZInput.GetButton("JoyLTrigger"), ZInput.GetButton("JoyRTrigger")))
            {
                case BeltMath.PadAction.Select: BeltBridge.Click(kind, index, 0); break;
                case BeltMath.PadAction.ToggleAuto: BeltBridge.ToggleAuto(kind, index); break;
                case BeltMath.PadAction.MoveOut: BeltBridge.MoveToInventory(kind, index); break;
                case BeltMath.PadAction.UseNow: BeltBridge.UseNow(kind, index); break;
            }
            return false;
        }

        // Closing the inventory ends the focus (the grids stop updating, so the prefix would not see it): a reopen starts
        // on the grid's own selection.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void Hide_Postfix() => Release();

        // Last row that is real inventory: EAQS hides its slot rows below the player grid's visible rows.
        private static int BottomRow(InventoryGui gui, InventoryGrid grid) =>
            grid == gui.m_playerGrid ? System.Math.Max(0, System.Math.Min(grid.m_height, InventoryScreen.EaqsBridge.VisibleRows(grid.m_height)) - 1) : grid.m_height - 1;

        private static bool Down() => ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyLStickDown");
    }
}
