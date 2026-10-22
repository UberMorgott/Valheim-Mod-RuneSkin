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
                if (Controls(ctx.Family))
                {
                    var control = ControlRole(image);
                    if (control.HasValue) return control.Value;
                }
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
                // Input text and placeholder take the body font like every other RuneSkin text (left as content they kept
                // the vanilla Norse / Averia fonts: a third and fourth face next to the theme's two).
                if (selectable is TMP_InputField input)
                    return input.placeholder == g
                        ? Classification.Of("text.muted", "input field placeholder (body font, muted)")
                        : Classification.Of("text.body", "input field text (body font)");
                var header = ThemeTokens.GetFloat("size.text.header.min", float.MaxValue);
                return Classification.Of(text.fontSize >= header ? "text.header" : "text.body",
                    text.fontSize >= header ? "TMP size >= size.text.header.min" : "TMP body text");
            }
            return null;
        }

        // Families whose screens are vanilla control forms (M5: main menu screens, settings, connection dialogs): the
        // controls are classified by their component wiring (art\README.md "M5 sprites": slider A, toggle = checkbox,
        // dropdown A, input = field_search). Older families keep their adapter-specific roles.
        public static readonly HashSet<string> ControlFamilies = new HashSet<string> { "MainMenu", "Settings", "Connection", "BuildHud", "LoadScreen", "Chat", "Popups", "TextDialogs", "Compendium", "SplitDialog" };

        private static bool Controls(string family) => family != null && ControlFamilies.Contains(family);

        // Component wiring of uGUI / TMP controls: Toggle.graphic / targetGraphic, Slider.fillRect / handleRect,
        // TMP_Dropdown targetGraphic / template (its item toggles), input fields. Null = not part of a control.
        private static Classification? ControlRole(Image image)
        {
            var rt = image.rectTransform;
            var dropdown = image.GetComponentInParent<TMP_Dropdown>(true);
            if (dropdown)
            {
                if (dropdown.targetGraphic == image) return Classification.Of("dropdown.field", "TMP_Dropdown.targetGraphic");
                if (dropdown.template && rt == dropdown.template) return Classification.Of("dropdown.list", "TMP_Dropdown.template (opened list)");
                var itemToggle = image.GetComponentInParent<Toggle>(true);
                if (itemToggle && dropdown.template && itemToggle.transform.IsChildOf(dropdown.template))
                {
                    if (itemToggle.graphic == image) return Classification.Of("toggle.check", "dropdown item Toggle.graphic");
                    if (itemToggle.targetGraphic == image) return Classification.Of("dropdown.item", "dropdown item Toggle.targetGraphic");
                }
                if (dropdown.template && rt.IsChildOf(dropdown.template))
                {
                    var bar = image.GetComponentInParent<Scrollbar>(true);
                    if (bar) return bar.targetGraphic == image ? Classification.Of("scrollbar.handle", "dropdown list scrollbar handle") : Classification.Of("scrollbar.track", "dropdown list scrollbar track");
                    return Classification.Of("skip", "dropdown list structure (viewport / content)");
                }
                if (image.name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0) return Classification.Of("dropdown.arrow", "TMP_Dropdown arrow");
            }
            var toggle = image.GetComponentInParent<Toggle>(true);
            if (toggle && (toggle.graphic == image || toggle.targetGraphic == image))
                return Classification.Of(toggle.graphic == image ? "toggle.check" : "toggle.box", toggle.graphic == image ? "Toggle.graphic (check mark)" : "Toggle.targetGraphic (box)");
            var slider = image.GetComponentInParent<Slider>(true);
            if (slider)
            {
                if (slider.fillRect == rt) return Classification.Of("slider.fill", "Slider.fillRect");
                if (slider.handleRect == rt || slider.targetGraphic == image) return Classification.Of("slider.handle", "Slider.handleRect");
                return Classification.Of("slider.track", "graphic inside a Slider (track)");
            }
            var input = image.GetComponentInParent<TMP_InputField>(true);
            if (input && input.targetGraphic == image) return Classification.Of("field", "TMP_InputField.targetGraphic");
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
