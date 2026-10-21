using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Responsive;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        }

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
                PanelDecor.Ensure(box.rectTransform, "size.tooltip.panel.pad", glass: false, plaque: false); // box = the glass
                // The frame lies on the box edge: content laid out inside the box needs the inset (Epic Loot's
                // InventoryTooltip content has padding 0). Boxes without a LayoutGroup keep their own text margins.
                ContentInset.Ensure(box.rectTransform, "size.tooltip.content.inset");
            }
            else if (text && text.TryGetComponent<TMP_Text>(out var bare)) TooltipFrame.Ensure(bare);
            return ctx;
        }
    }

    // RuneSkin's frame behind a box-less tooltip text: sized to the text's rendered bounds plus the padding token after
    // all LateUpdates (UITooltip moves and clamps the tooltip there), right before canvases render; then clamped
    // into the safe area by moving the parent it shares with the text, so both stay together.
    public sealed class TooltipFrame : MonoBehaviour
    {
        private TMP_Text _text;
        private RectTransform _rect;
        private Image _image;
        private SkinMark _mark;
        private string _shown;
        private Vector3 _lossy;
        private readonly Vector3[] _corners = new Vector3[4];

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

        private void OnEnable()
        {
            _shown = null;
            Canvas.willRenderCanvases += Fit;
        }

        private void OnDisable() => Canvas.willRenderCanvases -= Fit;

        private void OnDestroy() => Canvas.willRenderCanvases -= Fit;

        private void Fit()
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
            ClampToSafeArea();
        }

        private void ClampToSafeArea()
        {
            var parent = _rect.parent;
            if (!parent) return;
            var safe = ResponsiveContext.SafeArea;
            _rect.GetWorldCorners(_corners);
            var dx = 0f;
            var dy = 0f;
            if (_corners[2].x > safe.xMax) dx -= _corners[2].x - safe.xMax;
            if (_corners[0].x < safe.xMin) dx += safe.xMin - _corners[0].x;
            if (_corners[2].y > safe.yMax) dy -= _corners[2].y - safe.yMax;
            if (_corners[0].y < safe.yMin) dy += safe.yMin - _corners[0].y;
            if (dx != 0f || dy != 0f) parent.position += new Vector3(dx, dy, 0f);
        }
    }
}
