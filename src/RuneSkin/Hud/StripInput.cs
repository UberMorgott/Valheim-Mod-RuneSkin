using UnityEngine;
using UnityEngine.EventSystems;

namespace RuneSkin.PlayerHud
{
    // Pointer on a strip cell. Hotbar cell i = player inventory slot (i, 0) (HotkeyBar binds row 0, HotkeyBar.cs:90-110):
    // while the inventory is open a press goes through the player grid's own callbacks exactly as a press on that grid
    // cell (InventoryGrid.OnLeftDown, InventoryGrid.cs:428-446: Shift = Split, Ctrl = Move, else Select ->
    // InventoryGui.OnSelectedItem picks up / drops / swaps the drag item; right press = m_onRightClick: use / equip).
    // Ability cells activate their slot on a left press while the inventory is closed.
    public sealed class StripInput : MonoBehaviour, IPointerDownHandler
    {
        public int Hotbar = -1;
        public int Ability = -1;

        public void OnPointerDown(PointerEventData eventData)
        {
            var player = Player.m_localPlayer;
            if (!player || player.IsDead()) return;
            if (Hotbar >= 0)
            {
                var gui = InventoryGui.instance;
                if (!gui || !InventoryGui.IsVisible() || !gui.m_playerGrid) return;
                var grid = gui.m_playerGrid;
                var pos = new Vector2i(Hotbar, 0);
                var item = player.GetInventory().GetItemAt(pos.x, pos.y);
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    grid.m_onRightClick?.Invoke(grid, item, pos);
                    return;
                }
                if (eventData.button != PointerEventData.InputButton.Left) return;
                var mod = InventoryGrid.Modifier.Select;
                if (ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift)) mod = InventoryGrid.Modifier.Split;
                else if (ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl)) mod = InventoryGrid.Modifier.Move;
                grid.m_onSelected?.Invoke(grid, item, pos, mod);
                return;
            }
            if (Ability >= 0 && eventData.button == PointerEventData.InputButton.Left && !InventoryGui.IsVisible())
                RuneSkin.Api.Providers.ActivateAbility(Ability, player);
        }
    }
}
