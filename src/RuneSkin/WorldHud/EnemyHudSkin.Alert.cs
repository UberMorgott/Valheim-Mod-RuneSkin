using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // The alert / aware indicator ("!" / "?", EnemyHud.cs:151-152, 205-206) sits in the prefab over the name centre, so it
    // covered the name. It gets its own slot left of the name text, size.enemy.alert.gap away, and name + indicator are
    // centred as a group (the name shifts right by half the slot). Laid out again only when the name text changes.
    public static partial class EnemyHudSkin
    {
        private static void AlertSlot(State s, EnemyHud.HudData data)
        {
            var name = data.m_name;
            if (!name || (!data.m_alerted && !data.m_aware)) return;
            var text = name.text;
            if (string.Equals(text, s.AlertLaidFor, System.StringComparison.Ordinal)) return;
            s.AlertLaidFor = text;
            var nameRect = name.rectTransform;
            var textW = name.GetPreferredValues(text).x;
            var gap = ThemeTokens.GetFloat("size.enemy.alert.gap", 3f);
            // the slot is the visible icon graphic, not its container (the prefab container is a wide row)
            var iconW = Mathf.Max(IconWidth(data.m_alerted), IconWidth(data.m_aware));
            if (!s.NameBaseSet)
            {
                s.NameBaseSet = true;
                s.NameBase = nameRect.anchoredPosition;
            }
            var shift = EnemyHudMath.AlertShift(textW, iconW, gap, out var iconX);
            nameRect.anchoredPosition = s.NameBase + new Vector2(shift, 0f);
            // the name's visual centre in its parent (pivot-corrected), the icon on the same line left of the text
            var centre = s.NameBase + new Vector2((0.5f - nameRect.pivot.x) * nameRect.rect.width, (0.5f - nameRect.pivot.y) * nameRect.rect.height);
            Place(data.m_alerted, nameRect, centre + new Vector2(iconX, 0f));
            Place(data.m_aware, nameRect, centre + new Vector2(iconX, 0f));
        }

        private static UnityEngine.UI.Graphic IconGraphic(RectTransform icon) => icon ? icon.GetComponentInChildren<UnityEngine.UI.Graphic>(true) : null;

        private static float IconWidth(RectTransform icon)
        {
            var g = IconGraphic(icon);
            if (!g) return 0f;
            var r = g.rectTransform.rect;
            var w = r.width;
            // an aspect-kept sprite in a wide rect: the visible width is its height x the sprite aspect
            if (g is UnityEngine.UI.Image img && img.sprite && img.sprite.rect.height > 0f)
                w = Mathf.Min(w, r.height * img.sprite.rect.width / img.sprite.rect.height);
            return w * Mathf.Abs(g.rectTransform.lossyScale.x / Mathf.Max(1e-5f, icon.lossyScale.x));
        }

        // Moves the icon container so its visible graphic's centre lands on centre (name parent space).
        private static void Place(RectTransform icon, RectTransform name, Vector2 centre)
        {
            if (!icon) return;
            if (icon.parent != name.parent) icon.SetParent(name.parent, false);
            icon.anchorMin = name.anchorMin;
            icon.anchorMax = name.anchorMax;
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = centre;
            var g = IconGraphic(icon);
            if (!g || g.rectTransform == icon) return;
            var parent = (RectTransform)icon.parent;
            var gc = parent.InverseTransformPoint(g.rectTransform.TransformPoint(g.rectTransform.rect.center));
            var ic = parent.InverseTransformPoint(icon.TransformPoint(icon.rect.center));
            icon.anchoredPosition = centre - (Vector2)(gc - ic);
        }
    }
}
