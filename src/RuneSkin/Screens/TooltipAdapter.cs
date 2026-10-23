using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Responsive;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneSkin.Ownership;

namespace RuneSkin.Screens
{
    // Tooltips, family "Tooltips". A tooltip is an instance of the hovered UITooltip's m_tooltipPrefab, created under
    // the hovered element's canvas in OnHoverStart (UITooltip.cs:144-164), filled by UpdateTextElements through the
    // children named "Text" / "Topic" (:166-181), moved to the pointer and clamped every LateUpdate (:92-93, :116) and
    // destroyed by HideTooltip (:191-200). RuneSkin restyles each instance after every other OnHoverStart postfix;
    // marks make a repeated hover on a live instance a no-op, destruction drops marks with the instance.
    // Installed only when RuneSkin owns "Tooltips".
    [HarmonyPatch]
    public static class TooltipAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Tooltips);

        [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnHoverStart))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void OnHoverStart_Postfix()
        {
            var tip = UITooltip.m_tooltip;
            if (!tip) return;
            Apply(tip);
        }

        public static void Apply(GameObject tip)
        {
            var root = tip.transform;
            var screenId = "Tooltip:" + (tip.name.EndsWith("(Clone)") ? tip.name.Substring(0, tip.name.Length - 7) : tip.name);
            ScreenRegistry.Register(root, () => Context(root, screenId));
            ScreenRegistry.Restyle(root);
            ResponsiveContext.ApplyScale(root);
            // Top overlay sort, no raycasts, height cap + wheel scroll, placement above the strip (TooltipPlacer).
            // Root cause of tooltips under the strip: the instance lives under the hovered element's canvas
            // (UITooltip.cs:150, the GUI root), and the strip canvas sorts above that canvas while the inventory is
            // open (HudCluster.AboveInventory, order root + 1).
            TopOverlay.Lift(tip);
            TooltipPlacer.Attach(tip);
        }

        // The wheel over a hovered item whose shown tooltip overflows scrolls the tooltip, not the list behind it:
        // every other ScrollRect skips the event (the item's raycast bubbles OnScroll up to the inventory /
        // container ScrollRect, ExecuteEvents.ExecuteHierarchy). Without an overflowing tooltip nothing changes.
        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnScroll))]
        [HarmonyPrefix]
        private static bool OnScroll_Prefix(ScrollRect __instance) =>
            !TooltipPlacer.CapturesWheel || TooltipPlacer.Owns(__instance.transform);

        private static ScreenContext Context(Transform root, string screenId)
        {
            var ctx = new ScreenContext(screenId, Ownership.OwnershipRegistry.Tooltips);
            var text = Utils.FindChild(root, "Text");
            var topic = Utils.FindChild(root, "Topic");
            if (text && text.TryGetComponent<TMP_Text>(out var body)) ctx.Role(body, "tooltip.text", "UITooltip text element 'Text' (UITooltip.cs:170)");
            if (topic && topic.TryGetComponent<TMP_Text>(out var head)) ctx.Role(head, "tooltip.topic", "UITooltip text element 'Topic' (UITooltip.cs:175)");

            // The box: the nearest image above the texts (the prefab's frame). Box-less prefabs (crafting requirement
            // "Tooltip": a bare text) get RuneSkin's own frame, fitted to the rendered text.
            Image box = null;
            for (var t = text ? text.parent : null; t && !box; t = t == root ? null : t.parent)
                if (t.TryGetComponent<Image>(out var img) && (img.sprite || img.GetComponent<SkinMark>())) box = img;
            if (box)
            {
                ctx.Role(box, "tooltip.box", "image framing the tooltip texts");
                // box = the glass; the stack adds a second glass layer: fill_glass is alpha 0.9 by design (world faintly
                // through), and a tooltip over the inventory panel otherwise shows a brightness step where the panel ends.
                var decor = PanelDecor.Ensure(box.rectTransform, "size.tooltip.panel.pad", glass: true, plaque: false);
                // The frame lies on the box edge: content laid out inside the box needs the rail inset (Epic Loot's
                // InventoryTooltip content has padding 0), raised to the corner caps' keep-out so no line runs under
                // a leather strap. Boxes without a LayoutGroup keep their own text margins.
                ContentInset.Ensure(box.rectTransform, "size.tooltip.content.inset", decor ? decor.ContentInset() : 0f);
            }
            else if (text && text.TryGetComponent<TMP_Text>(out var bare)) TooltipFrame.Ensure(bare);
            return ctx;
        }
    }

    // RuneSkin's frame behind a box-less tooltip text: sized to the text's rendered bounds plus the padding token after
    // all LateUpdates (UITooltip moves and clamps the tooltip there), right before canvases render, by the tooltip's
    // TooltipPlacer (Fit), which then places the tooltip (frame included) above the strip.
    public sealed class TooltipFrame : MonoBehaviour
    {
        private TMP_Text _text;
        private RectTransform _rect;
        private Image _image;
        private SkinMark _mark;
        private string _shown;
        private Vector3 _lossy;

        public static void Ensure(TMP_Text text)
        {
            var parent = text.rectTransform.parent;
            var existing = parent.Find("RuneSkinTooltipFrame");
            if (existing) return;
            var go = new GameObject("RuneSkinTooltipFrame", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.SetSiblingIndex(text.rectTransform.GetSiblingIndex()); // drawn before (behind) the text
            rect.anchorMin = rect.anchorMax = text.rectTransform.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            go.AddComponent<LayoutElement>().ignoreLayout = true; // the prefab root lays out its children
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.enabled = false;
            var mark = go.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = "tooltip.bare";
            mark.Reason = "RuneSkin frame for a box-less tooltip";
            mark.BoundInstance = go.GetInstanceID();
            mark.ThemeRevision = -1; // applied by the root pass
            var frame = go.AddComponent<TooltipFrame>();
            frame._text = text;
            frame._rect = rect;
            frame._image = image;
            frame._mark = mark;
        }

        private void OnEnable() => _shown = null;

        public void Fit()
        {
            if (!_text || !_rect) return;
            // Text, font, wrapping width or scale changes while shown (UITooltip.Set -> UpdateTextElements :202-215).
            var lossy = _text.rectTransform.lossyScale;
            if (_shown != _text.text || lossy != _lossy)
            {
                _shown = _text.text;
                _lossy = lossy;
                _text.ForceMeshUpdate();
            }
            var visible = !string.IsNullOrEmpty(_shown) && _mark && _mark.Outcome == Outcome.Styled && _image.sprite;
            if (_image.enabled != visible) _image.enabled = visible;
            if (!visible) return;
            var pad = ThemeTokens.GetFloat("size.tooltip.padding", 0f);
            var bounds = _text.textBounds;
            var t = _text.rectTransform;
            _rect.position = t.TransformPoint(bounds.center);
            _rect.sizeDelta = Vector2.Scale(bounds.size, t.localScale) + new Vector2(pad * 2f, pad * 2f);
        }
    }
}
