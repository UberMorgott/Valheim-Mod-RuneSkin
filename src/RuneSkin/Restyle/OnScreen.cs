using UnityEngine;

namespace RuneSkin.Restyle
{
    // Keeps the RuneSkin panel stacks under a root inside its root canvas. The stack reaches size.panel.pad beyond the
    // vanilla panel it replaces, so a vanilla panel flush with a screen edge (F2 connect panel in the top-left corner,
    // run 20261022-124812) had its frame and corner caps cut off. On every enable the root goes back to its vanilla
    // position and moves by the overhang of the stacks' union (PanelMath.EdgeShift per axis, canvas space). Vanilla
    // never moves these roots itself; Revert restores the vanilla position.
    [DisallowMultipleComponent]
    public sealed class OnScreen : MonoBehaviour, IRevertible
    {
        private Vector2 _vanilla;
        private bool _saved;
        private static readonly Vector3[] Corners = new Vector3[4];

        public static OnScreen Ensure(RectTransform root)
        {
            if (!root) return null;
            if (!root.TryGetComponent<OnScreen>(out var o))
            {
                o = root.gameObject.AddComponent<OnScreen>();
                o.hideFlags = HideFlags.HideInInspector;
            }
            if (root.gameObject.activeInHierarchy) o.Fit();
            return o;
        }

        private void OnEnable() => Fit();

        public void Fit()
        {
            var rt = (RectTransform)transform;
            if (!_saved)
            {
                _vanilla = rt.anchoredPosition;
                _saved = true;
            }
            rt.anchoredPosition = _vanilla;
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return;
            var c = (RectTransform)canvas.rootCanvas.transform;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var decor in GetComponentsInChildren<PanelDecor>(false))
            {
                ((RectTransform)decor.transform).GetWorldCorners(Corners);
                foreach (var w in Corners)
                {
                    var l = (Vector2)c.InverseTransformPoint(w);
                    min = Vector2.Min(min, l);
                    max = Vector2.Max(max, l);
                }
            }
            if (min.x > max.x) return;
            var r = c.rect;
            var shift = new Vector2(PanelMath.EdgeShift(min.x, max.x, r.xMin, r.xMax), PanelMath.EdgeShift(min.y, max.y, r.yMin, r.yMax));
            if (shift == Vector2.zero) return;
            var world = c.TransformVector(shift);
            rt.anchoredPosition = _vanilla + (Vector2)(rt.parent ? rt.parent.InverseTransformVector(world) : world);
        }

        public void Revert()
        {
            if (_saved) ((RectTransform)transform).anchoredPosition = _vanilla;
            Destroy(this);
        }
    }
}
