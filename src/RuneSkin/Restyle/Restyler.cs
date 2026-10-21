using System.Collections.Generic;
using RuneSkin.Classify;
using RuneSkin.Diagnostics;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Applies theme roles to classified graphics. Visual properties only (sprite, image type, colour, PPU multiplier,
    // preserveAspect, TMP font, text colour, Selectable colour states); RectTransforms and layout stay vanilla.
    // Every write goes through the SkinMark journal so a root can be reverted exactly; a revert restores a property
    // only while it still holds RuneSkin's value (a later change by another mod wins).
    public static class Restyler
    {
        private static readonly List<Graphic> Buffer = new List<Graphic>(256);

        public static ScreenReport RestyleRoot(Transform root, ScreenContext ctx)
        {
            var report = new ScreenReport(ctx.ScreenId, root);
            if (!root) return report;
            ThemeTokens.EnsureFontFallbacks();
            Buffer.Clear();
            root.GetComponentsInChildren(true, Buffer);
            foreach (var g in Buffer)
            {
                if (!g) continue;
                var excluded = ExcludedBy(g.transform, root, ctx);
                if (excluded != null)
                {
                    report.Add(g, root, new Classification { Outcome = Outcome.Skipped, Reason = excluded, Role = "skip" });
                    continue;
                }
                report.Add(g, root, Style(g, ctx));
            }
            Buffer.Clear();
            SkinDiagnostics.Record(report);
            return report;
        }

        public static void RevertRoot(Transform root)
        {
            if (!root) return;
            foreach (var mark in root.GetComponentsInChildren<SkinMark>(true))
            {
                Revert(mark);
                if (mark.Internal) Object.Destroy(mark.gameObject);
                else Object.Destroy(mark);
            }
            foreach (var scale in root.GetComponentsInChildren<ScaleMark>(true))
                Responsive.ResponsiveContext.RevertScale(scale);
        }

        // Classifies (once per theme revision) and applies one graphic.
        public static Classification Style(Graphic g, ScreenContext ctx)
        {
            var mark = g.GetComponent<SkinMark>();
            if (mark && !mark.IsOwnMark)
                mark.BoundInstance = g.gameObject.GetInstanceID(); // clone of a styled template: same originals
            if (mark && mark.Internal)
            {
                if (mark.ThemeRevision != ThemeTokens.Revision)
                    mark.Outcome = Apply(g, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
                return new Classification { Role = mark.Role, Outcome = mark.Outcome, Reason = mark.Reason };
            }
            var original = mark ? mark.OriginalSprite : g is Image img && img.sprite ? img.sprite.name : null;
            var c = Classifier.Classify(g, original, ctx);
            if (c.Outcome == Outcome.Unknown || c.Outcome == Outcome.Skipped || c.Outcome == Outcome.Content)
            {
                if (mark) { Revert(mark); mark.Role = c.Role; mark.Outcome = c.Outcome; mark.Reason = c.Reason; }
                return c;
            }
            var role = ThemeTokens.Role(c.Role);
            if (role == null)
                return Classification.Unknown($"role '{c.Role}' not in theme ({c.Reason})");
            if (!mark)
            {
                mark = g.gameObject.AddComponent<SkinMark>();
                mark.BoundInstance = g.gameObject.GetInstanceID();
                mark.OriginalSprite = original;
                mark.hideFlags = HideFlags.HideInInspector;
            }
            else if (mark.Role == c.Role && mark.ThemeRevision == ThemeTokens.Revision)
            {
                return new Classification { Role = c.Role, Outcome = mark.Outcome, Reason = mark.Reason };
            }
            mark.Role = c.Role;
            mark.Reason = c.Reason;
            var applied = Apply(g, mark, role);
            mark.Outcome = applied ? Outcome.Styled : Outcome.Degraded;
            if (!applied)
                mark.Reason = $"{c.Reason}; asset missing for '{role.SpriteToken ?? role.FontToken}'";
            return new Classification { Role = c.Role, Outcome = mark.Outcome, Reason = mark.Reason };
        }

        // Writes the role from the originals (never on top of an earlier write), so re-theming never accumulates.
        // Returns false when the role's art is missing (graphic left vanilla).
        public static bool Apply(Graphic g, SkinMark mark, ResolvedRole role)
        {
            Revert(mark);
            mark.ThemeRevision = ThemeTokens.Revision;
            if (role == null) return false;
            var complete = true;

            if (g is Image image)
            {
                if (role.ImageUsable)
                {
                    if (role.Sprite) { mark.HasSprite = true; mark.OrigSprite = image.sprite; image.sprite = mark.SetSprite = role.Sprite; }
                    if (role.ImageType.HasValue) { mark.HasType = true; mark.OrigType = image.type; image.type = mark.SetType = role.ImageType.Value; }
                    if (role.PpuMultiplier > 0f) { mark.HasPpu = true; mark.OrigPpu = image.pixelsPerUnitMultiplier; image.pixelsPerUnitMultiplier = mark.SetPpu = role.PpuMultiplier; }
                    if (role.PreserveAspect) { mark.HasPreserve = true; mark.OrigPreserve = image.preserveAspect; image.preserveAspect = mark.SetPreserve = true; }
                    if (role.Color.HasValue) { mark.HasImageColor = true; mark.OrigImageColor = image.color; image.color = mark.SetImageColor = role.Color.Value; }
                }
                else complete = false;
            }
            else if (g is TMP_Text text)
            {
                if (role.Font)
                {
                    mark.HasFont = true;
                    mark.OrigFont = text.font;
                    mark.OrigFontMaterial = text.fontSharedMaterial;
                    text.font = mark.SetFont = role.Font;
                }
                else if (role.FontToken != null) complete = false;
                if (role.TextColor.HasValue) { mark.HasTextColor = true; mark.OrigTextColor = text.color; text.color = mark.SetTextColor = role.TextColor.Value; }
            }

            if (role.HasStates)
            {
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable && selectable.targetGraphic == g)
                {
                    mark.HasStates = true;
                    mark.OrigColors = selectable.colors;
                    mark.OrigTransition = selectable.transition;
                    var block = selectable.colors;
                    if (role.StateNormal.HasValue) block.normalColor = role.StateNormal.Value;
                    if (role.StateHover.HasValue) block.highlightedColor = role.StateHover.Value;
                    if (role.StatePressed.HasValue) block.pressedColor = role.StatePressed.Value;
                    if (role.StateSelected.HasValue) block.selectedColor = role.StateSelected.Value;
                    if (role.StateDisabled.HasValue) block.disabledColor = role.StateDisabled.Value;
                    block.colorMultiplier = 1f;
                    selectable.transition = Selectable.Transition.ColorTint;
                    selectable.colors = mark.SetColors = block;
                }
            }
            return complete;
        }

        public static void Revert(SkinMark mark)
        {
            if (!mark) return;
            var g = mark.GetComponent<Graphic>();
            if (g is Image image)
            {
                if (mark.HasSprite && image.sprite == mark.SetSprite) image.sprite = mark.OrigSprite;
                if (mark.HasType && image.type == mark.SetType) image.type = mark.OrigType;
                if (mark.HasPpu && Mathf.Approximately(image.pixelsPerUnitMultiplier, mark.SetPpu)) image.pixelsPerUnitMultiplier = mark.OrigPpu;
                if (mark.HasPreserve && image.preserveAspect == mark.SetPreserve) image.preserveAspect = mark.OrigPreserve;
                if (mark.HasImageColor && image.color == mark.SetImageColor) image.color = mark.OrigImageColor;
            }
            else if (g is TMP_Text text)
            {
                if (mark.HasFont && text.font == mark.SetFont)
                {
                    text.font = mark.OrigFont;
                    if (mark.OrigFontMaterial) text.fontSharedMaterial = mark.OrigFontMaterial;
                }
                if (mark.HasTextColor && text.color == mark.SetTextColor) text.color = mark.OrigTextColor;
            }
            if (mark.HasStates && g)
            {
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable && selectable.colors == mark.SetColors)
                {
                    selectable.colors = mark.OrigColors;
                    selectable.transition = mark.OrigTransition;
                }
            }
            mark.ClearJournal();
        }

        private static string ExcludedBy(Transform t, Transform root, ScreenContext ctx)
        {
            for (var p = t; p; p = p.parent)
            {
                if (ctx.Excluded.TryGetValue(p, out var reason))
                    return reason;
                if (p != root && p.TryGetComponent<RootMark>(out var nested))
                    return $"nested root '{nested.ScreenId}' (own pass)";
                if (p == root) break;
            }
            return null;
        }
    }
}
