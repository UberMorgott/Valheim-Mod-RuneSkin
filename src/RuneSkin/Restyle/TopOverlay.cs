using UnityEngine;

namespace RuneSkin.Restyle
{
    // The one "top overlay" sort for transient pointer visuals (tooltips, the inventory drag icon): an override-sorted
    // Canvas Band orders above the highest sorted UI it can be shown over - its own root canvas, the GUI root
    // (InventoryGui) and the HUD strip, whose canvas sorts above the GUI while the inventory is open
    // (HudCluster.AboveInventory). No GraphicRaycaster: a lifted visual never takes the pointer.
    public static class TopOverlay
    {
        public const int Band = 10;

        // Sorting order the overlay uses near this transform (re-read every call: the strip re-sorts when the
        // inventory opens / closes).
        public static int Order(Transform near, out int sortingLayerId) => BaseOrder(near, out sortingLayerId) + Band;

        // The highest order the overlay must clear (root canvas near, GUI root, HUD strip); the modal band
        // (ModalLayer, PanelMath.ModalOrder) sits between it and the overlay.
        public static int BaseOrder(Transform near, out int sortingLayerId)
        {
            sortingLayerId = 0;
            var order = 0;
            var root = near ? near.GetComponentInParent<Canvas>() : null;
            root = root ? root.rootCanvas : null;
            if (root)
            {
                order = root.sortingOrder;
                sortingLayerId = root.sortingLayerID;
            }
            if (InventoryGui.instance && InventoryGui.instance.GetComponentInParent<Canvas>() is Canvas inv && inv.rootCanvas)
                order = Mathf.Max(order, inv.rootCanvas.sortingOrder);
            var strip = PlayerHud.HudCluster.Instance;
            if (strip && strip.TryGetComponent<Canvas>(out var sc) && sc.overrideSorting) order = Mathf.Max(order, sc.sortingOrder);
            return order;
        }

        // Puts go on the top overlay sort; idempotent, cheap when nothing changed.
        public static Canvas Lift(GameObject go)
        {
            if (!go) return null;
            var order = Order(go.transform.parent, out var layer);
            var c = go.GetComponent<Canvas>();
            if (!c) c = go.AddComponent<Canvas>();
            if (!c.overrideSorting) c.overrideSorting = true;
            if (c.sortingOrder != order) c.sortingOrder = order;
            if (c.sortingLayerID != layer) c.sortingLayerID = layer;
            return c;
        }
    }
}
