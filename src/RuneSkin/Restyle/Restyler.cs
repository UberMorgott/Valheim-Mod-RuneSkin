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
    // preserveAspect, TMP font, text colour, Selectable colour states); RectTransforms and layout stay vanilla, except the
    // global layout rules (batch G): TextInset (field / row / toggle text start, theme size.field.inset / size.row.pad)
    // and CellFit (theme role "cell": icon centred, texts inside) - both journaled components.
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
                if (UnderNestedRoot(mark.transform, root)) continue; // its own registration reverts it
                Revert(mark);
                if (mark.Internal) Object.Destroy(mark.gameObject);
                else Object.Destroy(mark);
            }
            foreach (var extra in root.GetComponentsInChildren<IRevertible>(true))
                if (extra is Component c && !UnderNestedRoot(c.transform, root))
                    extra.Revert();
            foreach (var scale in root.GetComponentsInChildren<ScaleMark>(true))
                if (!UnderNestedRoot(scale.transform, root))
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
                {
                    mark.Outcome = Apply(g, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
                    if (g.GetComponentInParent<PanelDecor>(true) || g.GetComponentInParent<PanelCaps>(true)) g.enabled = mark.Outcome == Outcome.Styled;
                }
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
            // Degraded marks are tried again: a font bound late (theme source "loaded") may exist by now.
            else if (mark.Role == c.Role && mark.ThemeRevision == ThemeTokens.Revision && mark.Outcome != Outcome.Degraded)
            {
                if (mark.Outcome == Outcome.Styled) Layout(g, role); // clones of a styled template: their own layout components
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
                    var ppu = role.PpuMultiplier > 0f ? role.PpuMultiplier : 0f;
                    if (role.Sprite && (image.type == Image.Type.Sliced || image.type == Image.Type.Tiled))
                        ppu = (ppu > 0f ? ppu : 1f) * CanvasPpuFactor(image);
                    if (ppu > 0f) { mark.HasPpu = true; mark.OrigPpu = image.pixelsPerUnitMultiplier; image.pixelsPerUnitMultiplier = mark.SetPpu = ppu; }
                    if (role.PreserveAspect) { mark.HasPreserve = true; mark.OrigPreserve = image.preserveAspect; image.preserveAspect = mark.SetPreserve = true; }
                    if (role.FillCenter.HasValue) { mark.HasFill = true; mark.OrigFill = image.fillCenter; image.fillCenter = mark.SetFill = role.FillCenter.Value; }
                    if (role.Color.HasValue) { mark.HasImageColor = true; mark.OrigImageColor = image.color; image.color = mark.SetImageColor = role.Color.Value; }
                    Layout(g, role);
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
                    // Loaded face (the rune face = vanilla Valheim-Norsebold): the canonical preset is the main menu's look,
                    // the font asset's own material and no faux bold. Texts that already had the face keep vanilla's
                    // preset otherwise ("- Outline" copies, Bold topics read heavy / smeared).
                    if (ThemeTokens.IsLoadedFont(role.Font))
                    {
                        if (role.Font.material) text.fontSharedMaterial = role.Font.material;
                        if ((text.fontStyle & FontStyles.Bold) == FontStyles.Bold)
                        {
                            mark.HasBoldOff = true;
                            text.fontStyle &= ~FontStyles.Bold;
                        }
                    }
                    mark.SetFontMaterial = text.fontSharedMaterial;
                }
                else if (role.FontToken != null) complete = false;
                if (role.TextColor.HasValue) { mark.HasTextColor = true; mark.OrigTextColor = text.color; text.color = mark.SetTextColor = role.TextColor.Value; }
                if (role.FitInset > 0f) Fit(text, mark, role.FitInset);
                Layout(g, role);
            }
            else if (g is Text legacy)
            {
                // Legacy uGUI text (mods built on UnityEngine.UI.Text, e.g. Almanac): theme colour and the role's face as a
                // legacy Font (ThemeTokens.LegacyFont: the bundle's dynamic PT Sans / Cormorant, the rune face's source).
                if (role.TextColor.HasValue) { mark.HasTextColor = true; mark.OrigTextColor = legacy.color; legacy.color = mark.SetTextColor = role.TextColor.Value; }
                var bold = (legacy.fontStyle & FontStyle.Bold) == FontStyle.Bold; // Bold or BoldAndItalic
                var face = role.FontToken != null ? ThemeTokens.LegacyFont(LegacyFaces.FaceToken(role.FontToken, bold)) : null;
                if (face && legacy.font != face)
                {
                    mark.HasLegacyFont = true;
                    mark.OrigLegacyFont = legacy.font;
                    legacy.font = mark.SetLegacyFont = face;
                    // No faux bold: our faces are single weights, a Bold style would be smeared synthetically. The weight
                    // comes from the face instead (LegacyFaceToken: bold body text takes the bold display face).
                    if (bold)
                    {
                        mark.HasBoldOff = true;
                        legacy.fontStyle = legacy.fontStyle == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal;
                    }
                }
            }

            if (role.HasSpriteStates && g is Image && role.ImageUsable)
            {
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable && selectable.targetGraphic == g)
                {
                    mark.HasSpriteStates = true;
                    mark.OrigSpriteState = selectable.spriteState;
                    mark.OrigTransition = selectable.transition;
                    selectable.spriteState = mark.SetSpriteState = new SpriteState
                    {
                        highlightedSprite = role.SpriteHover,
                        pressedSprite = role.SpritePressed,
                        selectedSprite = role.SpriteSelected,
                        disabledSprite = role.SpriteDisabled
                    };
                    selectable.transition = Selectable.Transition.SpriteSwap;
                }
            }
            else if (role.HasStates)
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

        // Global layout rules (batch G): cell roles centre their icon and keep their texts inside (CellFit); texts
        // without a plate fit get the field / row / toggle placement (TextInset). A label re-classified away from a
        // plate role (world row name: button.label on the first pass, row.label after the screen's own pass) drops
        // the stale plate fit, whose cap inset (40 units) pushed the row text in.
        private static void Layout(Graphic g, ResolvedRole role)
        {
            if (role == null) return;
            if (g is Image image && role.Cell && role.ImageUsable) CellFit.Ensure(image, role.Zones);
            else if (g is TMP_Text text && role.FitInset <= 0f)
            {
                if (text.TryGetComponent<PlateLabelFit>(out var stale) && !stale.Reverted) stale.Revert();
                TextInset.Ensure(text);
            }
        }

        // Theme geometry (sliced borders, tile periods, native sizes) is authored at canvas referencePixelsPerUnit 100
        // (sprite px x 100 / sprite PPU). Unity draws a sprite px as canvas.referencePixelsPerUnit / (sprite PPU x
        // pixelsPerUnitMultiplier) units, and Valheim's canvases run at 50 (live dump: Hud, Menu and tooltip refPpu=50),
        // which halves every border. Multiplying the multiplier by refPpu / 100 cancels the canvas value.
        public static float CanvasPpuFactor(Graphic g)
        {
            var canvas = g.canvas;
            if (!canvas) canvas = g.GetComponentInParent<Canvas>(true);
            var r = canvas ? canvas.referencePixelsPerUnit : 100f;
            return r > 0f ? r / 100f : 1f;
        }

        // SetNativeSize at reference 100 (SetNativeSize itself divides by the canvas value and ignores the multiplier).
        public static void SetReferenceSize(Image image)
        {
            var s = image ? image.sprite : null;
            if (!s || !(s.pixelsPerUnit > 0f)) return;
            image.rectTransform.sizeDelta = s.rect.size * (100f / s.pixelsPerUnit);
        }

        // A button label shrinks to fit its button: auto-size from its own size down to size.text.fit.min, side margins
        // keep it clear of the plate caps (inset per side measured from the button edge, minus what the label rect
        // already leaves). Theme fonts are wider than the vanilla faces the layouts were made for.
        private static void Fit(TMP_Text text, SkinMark mark, float inset)
        {
            var button = text.GetComponentInParent<Selectable>(true);
            if (!button) return;
            mark.HasFit = true;
            mark.OrigAutoSize = text.enableAutoSizing;
            mark.OrigSizeMin = text.fontSizeMin;
            mark.OrigSizeMax = text.fontSizeMax;
            mark.OrigFontSize = text.fontSize;
            var max = text.enableAutoSizing ? Mathf.Max(text.fontSizeMax, text.fontSize) : text.fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMax = max;
            text.fontSizeMin = Mathf.Min(max, ThemeTokens.GetFloat("size.text.fit.min", 13f));
            // Long labels may take a second line before they shrink (wrapping on, the plate is tall enough).
            mark.OrigWrap = text.textWrappingMode;
            text.textWrappingMode = TextWrappingModes.Normal;
            // Margins follow the laid-out button (layout groups size it after Awake): PlateLabelFit keeps them.
            if (text.TryGetComponent<TextInset>(out var placed)) placed.Revert();
            if (!text.TryGetComponent<PlateLabelFit>(out var fit)) fit = text.gameObject.AddComponent<PlateLabelFit>();
            fit.Init(button.transform as RectTransform, inset);
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
                if (mark.HasFill && image.fillCenter == mark.SetFill) image.fillCenter = mark.OrigFill;
                if (mark.HasImageColor && image.color == mark.SetImageColor) image.color = mark.OrigImageColor;
            }
            else if (g is TMP_Text text)
            {
                // The font/material pair is restored only while both are still ours (another mod may have set a preset).
                if (mark.HasFont && text.font == mark.SetFont && text.fontSharedMaterial == mark.SetFontMaterial)
                {
                    text.font = mark.OrigFont;
                    if (mark.OrigFontMaterial) text.fontSharedMaterial = mark.OrigFontMaterial;
                }
                if (mark.HasBoldOff) text.fontStyle |= FontStyles.Bold;
                if (mark.HasTextColor && text.color == mark.SetTextColor) text.color = mark.OrigTextColor;
                if (mark.HasFit)
                {
                    text.enableAutoSizing = mark.OrigAutoSize;
                    text.fontSizeMin = mark.OrigSizeMin;
                    text.fontSizeMax = mark.OrigSizeMax;
                    if (!mark.OrigAutoSize) text.fontSize = mark.OrigFontSize;
                    text.textWrappingMode = mark.OrigWrap;
                }
            }
            else if (g is Text legacy)
            {
                if (mark.HasTextColor && legacy.color == mark.SetTextColor) legacy.color = mark.OrigTextColor;
                if (mark.HasLegacyFont && legacy.font == mark.SetLegacyFont)
                {
                    legacy.font = mark.OrigLegacyFont;
                    if (mark.HasBoldOff) legacy.fontStyle = legacy.fontStyle == FontStyle.Italic ? FontStyle.BoldAndItalic : FontStyle.Bold;
                }
            }
            if (mark.HasSpriteStates && g)
            {
                var selectable = g.GetComponentInParent<Selectable>(true);
                if (selectable && SameStates(selectable.spriteState, mark.SetSpriteState))
                {
                    selectable.spriteState = mark.OrigSpriteState;
                    selectable.transition = mark.OrigTransition;
                }
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

        private static bool UnderNestedRoot(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.TryGetComponent<RootMark>(out _)) return true;
            return false;
        }

        private static bool SameStates(SpriteState a, SpriteState b) =>
            a.highlightedSprite == b.highlightedSprite && a.pressedSprite == b.pressedSprite &&
            a.selectedSprite == b.selectedSprite && a.disabledSprite == b.disabledSprite;

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
