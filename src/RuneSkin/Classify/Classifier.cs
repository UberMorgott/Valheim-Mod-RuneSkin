using System;
using System.Collections.Generic;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Classify
{
    public struct Classification
    {
        public string Role;
        public Outcome Outcome;
        public string Reason;

        public static Classification Of(string role, string reason)
        {
            var outcome = role == "content" ? Outcome.Content : role == "skip" ? Outcome.Skipped : Outcome.Styled;
            return new Classification { Role = role, Outcome = outcome, Reason = reason };
        }

        public static Classification Unknown(string reason) => new Classification { Outcome = Outcome.Unknown, Reason = reason };
    }

    // What a screen adapter knows about its own screen: roles from vanilla field references (classifier step b) and
    // subtrees that belong to someone else.
    public sealed class ScreenContext
    {
        public readonly string ScreenId;
        public readonly string Family;
        public readonly Dictionary<Graphic, Classification> AdapterRoles = new Dictionary<Graphic, Classification>();
        public readonly Dictionary<Transform, string> Excluded = new Dictionary<Transform, string>();

        public ScreenContext(string screenId, string family)
        {
            ScreenId = screenId;
            Family = family;
        }

        public void Role(Graphic g, string role, string reason)
        {
            if (g) AdapterRoles[g] = Classification.Of(role, reason);
        }

        public void Exclude(Transform t, string reason)
        {
            if (t) Excluded[t] = reason;
        }
    }

    // Order: (a) explicit registration -> (b) adapter field refs -> (c) component semantics -> (d) vanilla sprite
    // name from theme data. Anything else is Unknown: left untouched and reported by diagnostics.
    public static class Classifier
    {
        // (a) Skin.Bind registrations (owner-qualified in Host).
        public static readonly Dictionary<Graphic, Classification> Bindings = new Dictionary<Graphic, Classification>();

        public static Classification Classify(Graphic g, string originalSprite, ScreenContext ctx)
        {
            if (Bindings.TryGetValue(g, out var bound))
                return bound;
            if (ctx.AdapterRoles.TryGetValue(g, out var adapted))
                return adapted;
            var semantic = Semantics(g, originalSprite, ctx);
            if (semantic.HasValue)
                return semantic.Value;
            if (g is Image && !string.IsNullOrEmpty(originalSprite))
            {
                var role = BySprite(originalSprite, ctx.Family);
                if (role != null)
                    return Classification.Of(role, $"vanilla sprite '{originalSprite}'");
                return Classification.Unknown($"Image sprite '{originalSprite}' not in theme spriteRoles");
            }
            return Classification.Unknown(g is Image ? "Image without sprite" : $"{g.GetType().Name} not handled");
        }

        // (c) Component semantics.
        private static Classification? Semantics(Graphic g, string originalSprite, ScreenContext ctx)
        {
            // GuiBar caches its fill colour in Awake and restores it (GuiBar.cs:32-38, ResetColor :140); mods tint fills.
            var guiBar = g.GetComponentInParent<GuiBar>(true);
            if (guiBar && guiBar.m_bar && guiBar.m_bar.gameObject == g.gameObject)
                return Classification.Of("content", "GuiBar fill (colour cache, GuiBar.cs:32-38)");

            if (g is Image image)
            {
                if (image.material && image.material.name.IndexOf("blur", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Classification.Of("skip", $"blur material '{image.material.name}'");
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable && selectable.targetGraphic == g)
                {
                    if (selectable is Button)
                        return Classification.Of("button", "Button.targetGraphic");
                    if (selectable is TMP_InputField || selectable is InputField)
                        return Classification.Of("backdrop", "input field graphic");
                }
                // Scroll views (Epic Loot item tooltips wrap their texts in one): masks and hit areas are structure,
                // scrollbars get their own art later (M1); both explained, not unknown, and never a screen dim.
                if (g.GetComponent<Mask>() || g.GetComponent<RectMask2D>())
                    return Classification.Of("skip", "mask / viewport graphic");
                var scroll = g.GetComponentInParent<ScrollRect>(true);
                if (scroll && scroll.viewport == g.rectTransform)
                    return Classification.Of("skip", "ScrollRect.viewport (clip area)");
                if (g.GetComponent<ScrollRect>())
                    return Classification.Of("skip", "ScrollRect hit area");
                // Scrollbar: the handle is the Selectable's targetGraphic (state sprites via SpriteSwap), the track is
                // the Image on the Scrollbar's own object; anything else inside (arrows, mod decor) stays content.
                var bar = g.GetComponentInParent<Scrollbar>(true);
                if (bar)
                {
                    if (bar.targetGraphic == g || (bar.handleRect && bar.handleRect == g.rectTransform))
                        return Classification.Of("scrollbar.handle", "Scrollbar.targetGraphic / handleRect");
                    if (bar.gameObject == g.gameObject)
                        return Classification.Of("scrollbar.track", "Image on the Scrollbar object");
                    return Classification.Of("content", "other graphic inside a Scrollbar");
                }
                if (!image.sprite && string.IsNullOrEmpty(originalSprite) && Stretches(image.rectTransform))
                    return Classification.Of("scrim", "sprite-less full-stretch image (screen dim)");
                return null;
            }

            if (g is TMP_SubMeshUI)
                return Classification.Of("content", "TMP_SubMeshUI (fallback-font sub-mesh of its parent text)");
            if (g is TMP_Text text)
            {
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable is Button button)
                {
                    if (button.targetGraphic == g)
                        return Classification.Of("menu.entry", "TMP is the Button's targetGraphic (text-only entry)");
                    return Classification.Of("button.label", "TMP inside Button");
                }
                if (selectable is TMP_InputField)
                    return Classification.Of("content", "input field text (user content)");
                var header = ThemeTokens.GetFloat("size.text.header.min", float.MaxValue);
                return Classification.Of(text.fontSize >= header ? "text.header" : "text.body",
                    text.fontSize >= header ? "TMP size >= size.text.header.min" : "TMP body text");
            }
            return null;
        }

        private static bool Stretches(RectTransform rt) =>
            rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;

        // (d) Vanilla sprite name. Runtime copies from SpriteAtlas.GetSprite are named "<x>(Clone)".
        public static string BySprite(string sprite, string family)
        {
            if (sprite.EndsWith("(Clone)", StringComparison.Ordinal))
                sprite = sprite.Substring(0, sprite.Length - 7);
            foreach (var entry in ThemeTokens.SpriteRoles)
            {
                if (entry.Context != null && entry.Context != family) continue;
                var hit = entry.Prefix ? sprite.StartsWith(entry.Match, StringComparison.Ordinal) : sprite == entry.Match;
                if (hit) return entry.Role;
            }
            return null;
        }
    }
}
