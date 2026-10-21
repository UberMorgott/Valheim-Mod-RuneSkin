using RuneSkin.Classify;
using RuneSkin.InventoryScreen;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.InfoScreens
{
    // Shared helpers of the info panels (skills, trophies): own images / texts, row-space rects, labels, the panel box.
    internal static class InfoArt
    {
        // Panel on the right of layout A: right edge = the crafting column's, same top / bottom, `width` wide (never
        // past the centre column's left edge).
        public static Box RightPanel(InventorySkin inv, float width)
        {
            var craft = inv.CraftingBox;
            var centre = inv.CenterBox;
            if (craft.IsEmpty) return default;
            var left = centre.IsEmpty ? craft.X : centre.X;
            var w = Mathf.Min(width, craft.Right - left);
            return new Box(craft.Right - w, craft.Y, w, craft.H);
        }

        public static Image OwnImage(Transform parent, string name, string role, string reason)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.raycastTarget = false;
            image.preserveAspect = true;
            var mark = DollCell.Mark(image, role, reason);
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            image.enabled = mark.Outcome == Outcome.Styled;
            return image;
        }

        // Own text child of a pooled row (created once per row, restyled with the role).
        public static TMP_Text RowText(Transform row, string name, string role, TextAlignmentOptions align)
        {
            var t = row.Find(name);
            if (t && t.TryGetComponent<TMP_Text>(out var existing)) return existing;
            var text = NewText(name, row, role);
            text.raycastTarget = false;
            text.alignment = align;
            var mark = DollCell.Mark(text, role, "RuneSkin row text");
            mark.Outcome = Restyler.Apply(text, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            return text;
        }

        // TMP created inactive with the theme font set before its Awake (no "LiberationSans not found" warning).
        public static TextMeshProUGUI NewText(string name, Transform parent, string fontRole)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            var role = ThemeTokens.Role(fontRole);
            var font = role != null && role.Font ? role.Font : ThemeTokens.GetFont("font.body");
            if (font) text.font = font;
            go.SetActive(true);
            return text;
        }

        // Rect in the parent's space from its top-left corner (x right, y down), size w x h; parentH unused (pivot top-left).
        public static void Rect(RectTransform rt, float x, float y, float w, float h, float parentH)
        {
            _ = parentH;
            var a = new Vector2(0f, 1f);
            if (rt.anchorMin != a) rt.anchorMin = a;
            if (rt.anchorMax != a) rt.anchorMax = a;
            if (rt.pivot != a) rt.pivot = a;
            var pos = new Vector2(x, -y);
            if ((rt.anchoredPosition - pos).sqrMagnitude > 0.01f) rt.anchoredPosition = pos;
            var size = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
            if ((rt.sizeDelta - size).sqrMagnitude > 0.01f) rt.sizeDelta = size;
            var s = new Vector3(1f, 1f, 1f);
            if (rt.localScale != s) rt.localScale = s;
        }

        public static void Label(TMP_Text label, TextAlignmentOptions align, float size, float min, bool upper = false)
        {
            if (label.alignment != align) label.alignment = align;
            if (label.textWrappingMode != TextWrappingModes.NoWrap) label.textWrappingMode = TextWrappingModes.NoWrap;
            if (label.overflowMode != TextOverflowModes.Ellipsis) label.overflowMode = TextOverflowModes.Ellipsis;
            if (upper && (label.fontStyle & FontStyles.UpperCase) != FontStyles.UpperCase) label.fontStyle |= FontStyles.UpperCase;
            var floor = Mathf.Min(size, min);
            if (!label.enableAutoSizing || Mathf.Abs(label.fontSizeMax - size) > 0.1f || Mathf.Abs(label.fontSizeMin - floor) > 0.1f)
            {
                label.fontSizeMax = size;
                label.fontSizeMin = floor;
                label.enableAutoSizing = true;
            }
        }
    }
}
