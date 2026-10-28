using UnityEngine;
using UnityEngine.EventSystems;

namespace RuneSkin.PlayerHud
{
    // Pointer on a strip cell. While the inventory is open the strip's raycast-only layer (StripHits, HudCluster
    // AboveInventory) sorts above the GUI with a hit area per hotbar / potion cell forwarding here (StripHit), so the strip,
    // not the inventory element under it, gets the press while its visuals stay below every window: each
    // item cell routes it to its player-grid position through the grid's own callbacks, exactly as a press on that grid
    // cell (InventoryGrid.OnLeftDown, InventoryGrid.cs:430-448: Shift = Split, Ctrl = Move, else Select ->
    // InventoryGui.OnSelectedItem picks up / drops / swaps the drag item; right press = m_onRightClick: use / equip).
    // Hotbar cell i = player inventory slot (i, 0) (HotkeyBar binds row 0, HotkeyBar.cs:90-110); potion cell i = EAQS
    // quick cell i (Slot.GridPosition, set by HudStrip.Fill). Without this the potion cells swallowed every press: their
    // bowls sat over the EAQS quick cells InventorySkin.PlaceQuickCells puts there, with no handler.
    // Press-drag: a press on a grid element picks the item up (vanilla), the release over a strip cell drops it here
    // (IDropHandler: the grid element's UIDragHandler makes it the drag source); a press-drag that starts on a strip cell
    // drops at the release on a grid element (OnEndDrag; vanilla PC handles only the second press, OnReleasedOn is touch).
    // Ability cells activate their slot on a left press while the inventory is closed.
    // Hover (pointer on the cell or its hit area): HudStrip draws the grid's hover frame and fills the cell's UITooltip with
    // the vanilla item tooltip (InventoryGrid.CreateItemTooltip, InventoryGrid.cs:409-412: topic = m_shared.m_name, text =
    // ItemData.GetTooltip()); vanilla HotkeyBar elements carry no UITooltip at all.
    public sealed class StripInput : MonoBehaviour, IPointerDownHandler, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public int Hotbar = -1;
        public int Ability = -1;
        public int Potion = -1;
        public int Food = -1;
        // Bar hit area (HP / stamina / eitr, ResourcePools id): hover shows the reserve tooltip (auras / summons), no press.
        public string Pool;

        // Pointer over this cell (directly or through its StripHit while the inventory is open).
        public bool Hovered { get; private set; }
        // The cell's tooltip (HudStrip.EnsureTooltip) and its writer: fills topic / text for this cell, false = nothing to show.
        public UITooltip Tooltip;
        public static System.Func<StripInput, bool> FillTooltip;

        // Potion cell: its player-grid position (EAQS quick cell), valid while HasCell.
        public Vector2i Cell;
        public bool HasCell;

        private bool TryPos(out Vector2i pos)
        {
            if (Hotbar >= 0)
            {
                pos = new Vector2i(Hotbar, 0);
                return true;
            }
            pos = Cell;
            return Potion >= 0 && HasCell;
        }

        private static InventoryGrid OpenGrid(Player player)
        {
            var gui = InventoryGui.instance;
            if (!player || player.IsDead() || !gui || !InventoryGui.IsVisible() || !gui.m_playerGrid) return null;
            return gui.m_playerGrid;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            var player = Player.m_localPlayer;
            if (!player || player.IsDead()) return;
            if (TryPos(out var pos))
            {
                var grid = OpenGrid(player);
                if (!grid) return;
                var item = player.GetInventory().GetItemAt(pos.x, pos.y);
                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    grid.m_onRightClick?.Invoke(grid, item, pos);
                    return;
                }
                if (eventData.button != PointerEventData.InputButton.Left) return;
                grid.m_onSelected?.Invoke(grid, item, pos, Modifier());
                return;
            }
            if (Ability >= 0 && eventData.button == PointerEventData.InputButton.Left && !InventoryGui.IsVisible())
                RuneSkin.Api.Providers.ActivateAbility(Ability, player);
        }

        // The object the pointer entered (this cell or its StripHit): the hover target a live tooltip refresh restarts on.
        public GameObject HoverTarget { get; private set; }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Hovered = true;
            HoverTarget = eventData != null && eventData.pointerEnter ? eventData.pointerEnter : gameObject;
            if (!Tooltip && Pool != null) HudStrip.EnsurePoolTooltip(this);
            if (!Tooltip || ZInput.IsTouchActive()) return;
            // text first, then the hover start (UITooltip.OnHoverStart shows nothing for an empty topic / text)
            if (FillTooltip != null && FillTooltip(this)) Tooltip.OnHoverStart(HoverTarget);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hovered = false;
            if (Tooltip && UITooltip.m_current == Tooltip) UITooltip.HideTooltip();
        }

        private void OnDisable() => Hovered = false;

        private static InventoryGrid.Modifier Modifier()
        {
            if (ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift)) return InventoryGrid.Modifier.Split;
            if (ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl)) return InventoryGrid.Modifier.Move;
            return InventoryGrid.Modifier.Select;
        }

        // Release of a press-drag over this cell: the press (on a grid element or another strip cell) picked the item up;
        // drop it here as a second press would. A drag that started on this very cell already ran its press here.
        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPos(out var pos)) return;
            if (Of(eventData.pointerPress) == this) return;
            DropCarried(OpenGrid(Player.m_localPlayer), pos);
        }

        // A drag source: Unity sends OnDrop to the release target only when the press object handles drags.
        public void OnBeginDrag(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData) { }

        // Press-drag from this cell released over an inventory grid element: drop there (a strip cell target already took
        // it in OnDrop, which runs first, so nothing is carried any more).
        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPos(out _)) return;
            var playerGrid = OpenGrid(Player.m_localPlayer);
            if (!playerGrid) return;
            var hit = eventData.pointerCurrentRaycast.gameObject;
            var element = hit ? hit.GetComponentInParent<InventoryElement>() : null;
            var grid = element ? element.GetComponentInParent<InventoryGrid>() : null;
            if (!grid)
            {
                // Released over the cursor's own drag icon or a frame: the element under the pointer (InventoryGrid's own
                // hover test, RectangleContainsScreenPoint) of the player grid or the open container grid.
                var gui = InventoryGui.instance;
                foreach (var g in new[] { playerGrid, gui ? gui.m_containerGrid : null })
                {
                    if (!g || !g.isActiveAndEnabled) continue;
                    var e = g.GetHoveredElement();
                    if (!e) continue;
                    element = e;
                    grid = g;
                    break;
                }
            }
            if (!grid || !element) return;
            var pos = grid.GetElementPos(element);
            if (pos.x < 0) return;
            DropCarried(grid, pos);
        }

        // The cell a pointer object belongs to: the cell itself or its hit area on the raycast layer.
        private static StripInput Of(GameObject go)
        {
            if (!go) return null;
            var hit = go.GetComponentInParent<StripHit>();
            return hit ? hit.Target : go.GetComponentInParent<StripInput>();
        }

        private static void DropCarried(InventoryGrid grid, Vector2i pos)
        {
            var gui = InventoryGui.instance;
            if (!grid || !gui || !gui.m_dragGo || gui.m_dragItem == null) return;
            var item = grid.GetInventory().GetItemAt(pos.x, pos.y);
            grid.m_onSelected?.Invoke(grid, item, pos, InventoryGrid.Modifier.Select);
        }
    }
}
