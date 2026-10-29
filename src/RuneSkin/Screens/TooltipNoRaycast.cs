using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // A live tooltip instance never takes the pointer (user bug 2026-10-23: the previous tooltip's texts / Epic Loot's
    // scroll view - own Canvas + GraphicRaycaster + CanvasGroup, TooltipBoxPatches.AddScrollbar - sat over the next cell or
    // footer plate: no OnPointerEnter there, the trash plate got no click, a drag picked the tooltip's Viewport). Every
    // CanvasGroup stops blocking, every GraphicRaycaster is off and every Graphic is no raycast target; swept on attach,
    // on enable and whenever children change (Epic Loot adds its scroll view after OnHoverStart). Same sweep as branch
    // `tooltips` (TooltipPlacer.Sweep), without its placement.
    [DisallowMultipleComponent]
    public sealed class TooltipNoRaycast : MonoBehaviour
    {
        private bool _dirty = true;

        public static void Attach(GameObject tip)
        {
            if (!tip) return;
            var p = tip.GetComponent<TooltipNoRaycast>();
            if (!p) p = tip.AddComponent<TooltipNoRaycast>();
            p.Sweep(); // at once: the instance may already be shown
        }

        private void OnEnable() => _dirty = true;

        private void OnTransformChildrenChanged() => _dirty = true;

        private void LateUpdate()
        {
            if (!_dirty) return;
            Sweep();
        }

        private void Sweep()
        {
            _dirty = false;
            if (!GetComponent<CanvasGroup>()) gameObject.AddComponent<CanvasGroup>();
            foreach (var g in GetComponentsInChildren<CanvasGroup>(true)) { g.blocksRaycasts = false; g.interactable = false; }
            foreach (var r in GetComponentsInChildren<GraphicRaycaster>(true)) r.enabled = false;
            foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }
    }
}
