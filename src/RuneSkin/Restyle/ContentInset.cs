using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Inner padding of a framed box whose content is laid out by a LayoutGroup with less padding than the frame needs
    // (Epic Loot item tooltips: InventoryTooltip/Canvas/Scroll View/Viewport/Content with padding 0, so the text ran
    // into the frame rail). Raises each padding side to the inset token, never lowers one; the box's own layout
    // (ContentSizeFitter / scroll content) then grows by the same amount. Journaled: Revert restores vanilla padding
    // while it still holds RuneSkin's value.
    [DisallowMultipleComponent]
    public sealed class ContentInset : MonoBehaviour, IRevertible
    {
        private LayoutGroup _group;
        private RectOffset _original;
        private RectOffset _written;

        // False when the box has no LayoutGroup (the caller reports it: inset not applicable there).
        public static bool Ensure(RectTransform box, string insetToken, string bottomToken = null)
        {
            if (!box || !box.TryGetComponent<LayoutGroup>(out var group)) return false;
            if (!box.TryGetComponent<ContentInset>(out var inset))
            {
                inset = box.gameObject.AddComponent<ContentInset>();
                inset.hideFlags = HideFlags.HideInInspector;
            }
            if (inset._group != group)
            {
                inset._group = group;
                var p = group.padding;
                inset._original = new RectOffset(p.left, p.right, p.top, p.bottom);
            }
            var px = Mathf.RoundToInt(ThemeTokens.GetFloat(insetToken, 0f));
            var bottom = bottomToken != null ? Mathf.RoundToInt(ThemeTokens.GetFloat(bottomToken, px)) : px;
            var o = inset._original;
            var want = new RectOffset(Mathf.Max(o.left, px), Mathf.Max(o.right, px), Mathf.Max(o.top, px), Mathf.Max(o.bottom, bottom));
            if (!Same(group.padding, want))
            {
                group.padding = want;
                LayoutRebuilder.MarkLayoutForRebuild(box);
            }
            inset._written = want;
            return true;
        }

        public void Revert()
        {
            if (_group && _written != null && Same(_group.padding, _written))
            {
                _group.padding = _original;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_group.transform);
            }
            _written = null;
            Destroy(this);
        }

        private static bool Same(RectOffset a, RectOffset b) =>
            a.left == b.left && a.right == b.right && a.top == b.top && a.bottom == b.bottom;
    }
}
