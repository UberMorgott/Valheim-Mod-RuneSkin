using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // The modal band: confirm dialogs (pause-menu quit / logout / cloud-storage warnings) sort above every other UI
    // (GUI root, HUD strip, world / NPC texts) and below the top overlay (tooltips, drag icon) - PanelMath.ModalOrder.
    // User 2026-10-23 ("Выйти из игры?"): a speech bubble drew inside the quit dialog. While any modal shows, the world
    // texts are hidden as well (WorldTextGate), so neither sort nor overlap can put one over a dialog.
    // Own GraphicRaycaster: graphics register with their nearest canvas, without it the dialog's buttons take no click.
    [DisallowMultipleComponent]
    public sealed class ModalLayer : MonoBehaviour, IRevertible
    {
        private Canvas _canvas;
        private bool _added, _addedRaycaster, _origOverride;
        private int _origOrder, _origLayer;

        public static ModalLayer Ensure(Transform t)
        {
            if (!t) return null;
            if (t.TryGetComponent<ModalLayer>(out var m)) return m;
            m = t.gameObject.AddComponent<ModalLayer>();
            m.hideFlags = HideFlags.HideInInspector;
            return m;
        }

        // Any modal open now: the pause menu (Menu.IsVisible covers its dialogs, Menu.cs:291) or a UnifiedPopup.
        public static bool AnyOpen() => Menu.IsVisible() || UnifiedPopup.IsVisible();

        private void OnEnable() => Sort();

        private void LateUpdate() => Sort(); // the strip re-sorts when the inventory opens / closes

        private void Sort()
        {
            if (!_canvas)
            {
                if (TryGetComponent(out _canvas))
                {
                    _origOverride = _canvas.overrideSorting;
                    _origOrder = _canvas.sortingOrder;
                    _origLayer = _canvas.sortingLayerID;
                }
                else
                {
                    _canvas = gameObject.AddComponent<Canvas>();
                    _added = true;
                }
                if (!GetComponent<GraphicRaycaster>()) { gameObject.AddComponent<GraphicRaycaster>(); _addedRaycaster = true; }
            }
            var order = PanelMath.ModalOrder(TopOverlay.BaseOrder(transform.parent, out var layer), TopOverlay.Band);
            if (!_canvas.overrideSorting) _canvas.overrideSorting = true;
            if (_canvas.sortingOrder != order) _canvas.sortingOrder = order;
            if (_canvas.sortingLayerID != layer) _canvas.sortingLayerID = layer;
        }

        public void Revert()
        {
            if (_addedRaycaster && TryGetComponent<GraphicRaycaster>(out var r)) DestroyImmediate(r); // requires the Canvas: gone first
            if (_added) { if (_canvas) Destroy(_canvas); }
            else if (_canvas)
            {
                _canvas.overrideSorting = _origOverride;
                _canvas.sortingOrder = _origOrder;
                _canvas.sortingLayerID = _origLayer;
            }
            Destroy(this);
        }
    }
}
