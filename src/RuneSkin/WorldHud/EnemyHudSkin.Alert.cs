using RuneSkin.Theme;
using UnityEngine;

namespace RuneSkin.WorldHud
{
    // The alert / aware indicator ("!" / "?", EnemyHud.cs:151-152, 205-206) sits in the prefab over the name centre, so it
    // covered the name. It is stacked above the name (user 2026-10-23, replaces the 0.16.3 slot left of it): centred on
    // the name text, its visible glyph's bottom size.enemy.alert.gap above the text top. Laid out again only when the
    // name text changes.
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
            var parent = (RectTransform)nameRect.parent;
            name.ForceMeshUpdate();
            var b = name.textBounds;
            var hasText = b.size.x > 0f && b.size.x < 10000f;
            var r = nameRect.rect;
            var topLocal = hasText ? new Vector3(b.center.x, b.max.y, 0f) : new Vector3(r.center.x, r.yMax, 0f);
            var top = (Vector2)parent.InverseTransformPoint(nameRect.TransformPoint(topLocal));
            var gap = ThemeTokens.GetFloat("size.enemy.alert.gap", 3f);
            // the slot is the visible icon graphic, not its container (the prefab container is a wide row)
            var iconH = Mathf.Max(IconHeight(data.m_alerted, parent), IconHeight(data.m_aware, parent));
            var centre = new Vector2(top.x, EnemyHudMath.AlertAboveY(top.y, iconH, gap));
            Place(data.m_alerted, nameRect, centre);
            Place(data.m_aware, nameRect, centre);
        }

        private static UnityEngine.UI.Graphic IconGraphic(RectTransform icon) => icon ? icon.GetComponentInChildren<UnityEngine.UI.Graphic>(true) : null;

        // Visible glyph height in the name parent's units (an aspect-kept sprite in a wide rect keeps its full height; in a
        // tall rect it is the width x the sprite aspect).
        private static float IconHeight(RectTransform icon, RectTransform parent)
        {
            var g = IconGraphic(icon);
            if (!g) return 0f;
            var r = g.rectTransform.rect;
            var h = r.height;
            if (g is UnityEngine.UI.Image img && img.sprite && img.sprite.rect.width > 0f)
                h = Mathf.Min(h, r.width * img.sprite.rect.height / img.sprite.rect.width);
            return h * Mathf.Abs(g.rectTransform.lossyScale.y / Mathf.Max(1e-5f, parent.lossyScale.y));
        }

        // Moves the icon container so its visible graphic's centre lands on centre (name parent space).
        private static void Place(RectTransform icon, RectTransform name, Vector2 centre)
        {
            if (!icon) return;
            if (icon.parent != name.parent) icon.SetParent(name.parent, false);
            icon.anchorMin = name.anchorMin;
            icon.anchorMax = name.anchorMax;
            icon.pivot = new Vector2(0.5f, 0.5f);
            var parent = (RectTransform)icon.parent;
            var g = IconGraphic(icon);
            var gt = g ? g.rectTransform : icon;
            var gc = (Vector2)parent.InverseTransformPoint(gt.TransformPoint(gt.rect.center));
            icon.anchoredPosition += centre - gc;
        }
    }
}
