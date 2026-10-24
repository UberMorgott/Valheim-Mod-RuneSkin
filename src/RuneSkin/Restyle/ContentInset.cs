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
        private bool _tookWidth;
        private int _px;

        // False when the box has no LayoutGroup (the caller reports it: inset not applicable there).
        // minInset: floor from the box's panel stack (PanelDecor.ContentInset: corner-cap keep-out), every side.
        public static bool Ensure(RectTransform box, string insetToken, float minInset = 0f)
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
            var px = Mathf.CeilToInt(Mathf.Max(ThemeTokens.GetFloat(insetToken, 0f), minInset));
            inset._px = px;
            var want = Raise(inset._original, px);
            if (!Same(group.padding, want))
            {
                group.padding = want;
                LayoutRebuilder.MarkLayoutForRebuild(box);
            }
            // A column that leaves child widths alone (Epic Loot's tooltip content: fixed-width texts) only shifts
            // its children by the left padding, so the right side runs under the rail; the column then sizes its
            // children's width to the padded inner width (texts wrap inside it). Journaled like the padding.
            if (group is VerticalLayoutGroup column && !column.childControlWidth)
            {
                column.childControlWidth = true;
                inset._tookWidth = true;
                LayoutRebuilder.MarkLayoutForRebuild(box);
            }
            inset._written = want;
            return true;
        }

        // Foreign layout code that rewrites the padding after Ensure (Epic Loot 0.15 TooltipBox.LayOut: (10, right, 5, 5)
        // on every fit, EpicLoot.dll decompile 2026-10-24) wiped the keep-out: the item tooltip's Topic ran under the
        // top corner caps, its lines under the side caps (menufix run 20261024-194221). The padding it wrote becomes the
        // new original and every side is raised again. grow = how much wider the box must get so the inner width that
        // code chose stays (LayoutMath.InsetGrowth). False when the keep-out still holds (nothing written).
        public bool Reassert(out int grow)
        {
            grow = 0;
            if (!_group || _original == null || _px <= 0) return false;
            var p = _group.padding;
            if (_written != null && Same(p, _written)) return false;
            _original = new RectOffset(p.left, p.right, p.top, p.bottom);
            var want = Raise(_original, _px);
            _group.padding = want;
            _written = want;
            grow = LayoutMath.InsetGrowth(p.left, p.right, _px);
            return true;
        }

        private static RectOffset Raise(RectOffset o, int px) =>
            new RectOffset(LayoutMath.RaiseSide(o.left, px), LayoutMath.RaiseSide(o.right, px), LayoutMath.RaiseSide(o.top, px), LayoutMath.RaiseSide(o.bottom, px));

        public void Revert()
        {
            if (_group && _written != null && Same(_group.padding, _written))
            {
                _group.padding = _original;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_group.transform);
            }
            if (_tookWidth && _group is VerticalLayoutGroup column && column.childControlWidth)
            {
                column.childControlWidth = false;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_group.transform);
            }
            _tookWidth = false;
            _written = null;
            Destroy(this);
        }

        private static bool Same(RectOffset a, RectOffset b) =>
            a.left == b.left && a.right == b.right && a.top == b.top && a.bottom == b.bottom;
    }
}
