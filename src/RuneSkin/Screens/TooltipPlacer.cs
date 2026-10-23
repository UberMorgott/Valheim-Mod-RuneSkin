using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.GameScreens;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // Drives a live tooltip instance (TooltipAdapter.Apply), after every LateUpdate (UITooltip moves the tooltip to the
    // pointer there, UITooltip.cs:91-93) and after the layout rebuild, right before canvases render:
    //  - top overlay sort (TopOverlay): above the GUI root and the HUD strip, never covered;
    //  - never raycastable (user bug 2026-10-23: moving the pointer up a column showed no tooltips - the previous
    //    tooltip's texts / Jotunn scroll view (own Canvas + GraphicRaycaster + CanvasGroup ignoreParentGroups, Epic Loot
    //    PatchOnHoverFix.AddScrollbar) took the pointer, so the next cell never got OnPointerEnter);
    //  - height capped to the space above the strip; overflowing scroll views scroll with the wheel while the item is
    //    hovered (one wheel path, ZInput; Epic Loot's own ScrollWheelHandler is disabled) and the
    //    wheel is withheld from every other ScrollRect (TooltipAdapter.OnScroll_Prefix) so the list behind stays put;
    //    a thin theme scrollbar and edge fades show there is more;
    //  - placed beside the hovered element (TooltipMath.Place), never over it, never under the strip.
    public sealed class TooltipPlacer : MonoBehaviour
    {
        private sealed class View
        {
            public ScrollRect Scroll;
            public RectTransform Clip, Viewport, Content;
            public float OrigClipH = -1f, OrigViewH = -1f;
            public bool Overflow;
            public Image Track, Handle, FadeTop, FadeBottom;
        }

        // The shown tooltip's placer (one UITooltip.m_tooltip at a time).
        public static TooltipPlacer Live { get; private set; }

        // A shown tooltip has overflowing content: the wheel belongs to it while its item is hovered.
        public static bool CapturesWheel => Live && Live.isActiveAndEnabled && Live._overflow;

        public static bool Owns(Transform t) => Live && t && t.IsChildOf(Live.transform);

        private readonly List<View> _views = new List<View>();
        private readonly List<ScrollRect> _scrollBuf = new List<ScrollRect>();
        private readonly Vector3[] _corners = new Vector3[4];
        private TooltipFrame _frame;
        private bool _dirty = true, _overflow, _nested;

        public static void Attach(GameObject tip)
        {
            if (!tip) return;
            var p = tip.GetComponent<TooltipPlacer>();
            if (!p) p = tip.AddComponent<TooltipPlacer>();
            p._dirty = true;
            p.Sweep(); // raycasts off at once (the instance may already be shown by a later OnHoverStart)
        }

        private void OnEnable()
        {
            // Epic Loot's comparison tooltip is an Instantiate of the whole tooltip under it (PatchOnHoverFix
            // .AddComparisonTooltip): the copied placer stands down, the outer one drives both.
            _nested = transform.parent && transform.parent.GetComponentInParent<TooltipPlacer>(true);
            if (_nested) { enabled = false; return; }
            Live = this;
            _dirty = true;
            Canvas.willRenderCanvases += Pass;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= Pass;
            if (Live == this) Live = null;
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= Pass;
            if (Live == this) Live = null;
        }

        private void OnTransformChildrenChanged() => _dirty = true;

        private void Pass()
        {
            if (!this || !isActiveAndEnabled) return;
            try
            {
                if (_dirty) { _dirty = false; Sweep(); }
                TopOverlay.Lift(gameObject);
                if (_frame) _frame.Fit();
                var bounds = Bounds();
                Scrolls(bounds);
                Place(bounds);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] tooltip placer: {e.GetType().Name}: {e.Message}");
                enabled = false;
            }
        }

        // ---------------------------------------------------------------- raycasts + views

        private void Sweep()
        {
            var group = GetComponent<CanvasGroup>();
            if (!group) group = gameObject.AddComponent<CanvasGroup>();
            foreach (var g in GetComponentsInChildren<CanvasGroup>(true)) { g.blocksRaycasts = false; g.interactable = false; }
            foreach (var r in GetComponentsInChildren<GraphicRaycaster>(true)) r.enabled = false;
            foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            _frame = GetComponentInChildren<TooltipFrame>(true);

            _scrollBuf.Clear();
            GetComponentsInChildren(true, _scrollBuf);
            _views.RemoveAll(v => !v.Scroll);
            var added = false;
            foreach (var sr in _scrollBuf)
            {
                if (!sr.vertical || !sr.content || _views.Exists(v => v.Scroll == sr)) continue;
                var view = new View
                {
                    Scroll = sr,
                    Clip = (RectTransform)sr.transform,
                    Viewport = sr.viewport ? sr.viewport : (RectTransform)sr.transform,
                    Content = sr.content,
                };
                StopForeignWheel(sr);
                view.Track = GameScreenKit.Decor(sr.transform, "RuneSkinTipTrack", "scrollbar.track", "tooltip scroll indicator track");
                view.Handle = GameScreenKit.Decor(view.Track.transform, "RuneSkinTipHandle", "scrollbar.handle", "tooltip scroll indicator handle");
                view.FadeTop = GameScreenKit.Decor(sr.transform, "RuneSkinTipFadeTop", "tooltip.fade", "tooltip edge fade (more above)");
                view.FadeBottom = GameScreenKit.Decor(sr.transform, "RuneSkinTipFadeBottom", "tooltip.fade", "tooltip edge fade (more below)");
                Edge(view.FadeTop.rectTransform, top: true);
                Edge(view.FadeBottom.rectTransform, top: false);
                var track = view.Track.rectTransform;
                track.anchorMin = new Vector2(1f, 0f);
                track.anchorMax = Vector2.one;
                track.pivot = new Vector2(1f, 0.5f);
                var handle = view.Handle.rectTransform;
                handle.anchorMin = Vector2.zero;
                handle.anchorMax = new Vector2(1f, 0f);
                handle.pivot = new Vector2(0.5f, 0f);
                foreach (var img in new[] { view.Track, view.Handle, view.FadeTop, view.FadeBottom })
                {
                    img.raycastTarget = false;
                    img.transform.SetAsLastSibling();
                    img.enabled = false;
                }
                view.FadeTop.transform.SetAsLastSibling();
                view.FadeBottom.transform.SetAsLastSibling();
                view.Track.transform.SetAsLastSibling();
                _views.Add(view);
                added = true;
            }
            if (added) ScreenRegistry.Restyle(transform); // the new decor gets its theme roles
        }

        private static void Edge(RectTransform rt, bool top)
        {
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, 0f); // top fade mirrored about its pivot, which lies on the clip top
            rt.anchoredPosition = Vector2.zero;
            // scroll_fade is opaque at its bottom edge: the top fade is the same art mirrored.
            rt.localScale = new Vector3(1f, top ? -1f : 1f, 1f);
        }

        // Epic Loot's ScrollWheelHandler (Update: legacy Input "Mouse ScrollWheel" -> verticalNormalizedPosition, whatever
        // the pointer is over and whether the content overflows or not) is replaced by the one wheel path here (ZInput,
        // only while the tooltip overflows, the list behind withheld): disabled, not doubled.
        private static void StopForeignWheel(ScrollRect sr)
        {
            foreach (var mb in sr.GetComponents<MonoBehaviour>())
                if (mb && mb.enabled && mb.GetType().Name == "ScrollWheelHandler") mb.enabled = false;
        }

        // ---------------------------------------------------------------- cap, wheel, indicators

        private float Scale()
        {
            var c = GetComponentInParent<Canvas>();
            return c && c.rootCanvas ? c.rootCanvas.scaleFactor : 1f;
        }

        private TipRect Bounds()
        {
            var safe = ResponsiveSafe();
            var strip = PlayerHud.HudCluster.Instance;
            var stripTop = strip ? strip.ReservedScreenTop : 0f;
            var margin = ThemeTokens.GetFloat("size.tooltip.screen.margin", 8f) * Scale();
            return TooltipMath.Bounds(safe, stripTop, margin);
        }

        private static TipRect ResponsiveSafe()
        {
            var s = Screen.safeArea;
            return new TipRect(s.xMin, s.yMin, s.xMax, s.yMax);
        }

        private void Scrolls(TipRect bounds)
        {
            _overflow = false;
            var maxH = TooltipMath.MaxHeight(bounds);
            var wheel = ZInput.GetMouseScrollWheel();
            var scale = Scale();
            foreach (var v in _views)
            {
                if (!v.Scroll || !v.Scroll.gameObject.activeInHierarchy) continue;
                if (v.OrigClipH < 0f) { v.OrigClipH = v.Clip.rect.height; v.OrigViewH = v.Viewport.rect.height; }
                var lossy = Mathf.Max(0.0001f, v.Clip.lossyScale.y);
                var capLocal = maxH / lossy;
                SetHeightKeepTop(v.Clip, Mathf.Min(v.OrigClipH, capLocal));
                if (v.Viewport != v.Clip) SetHeightKeepTop(v.Viewport, Mathf.Min(v.OrigViewH, capLocal));

                var clipH = Mathf.Min(v.Clip.rect.height, v.Viewport.rect.height);
                var contentH = v.Content.rect.height;
                v.Overflow = TooltipMath.Overflows(contentH, clipH);
                if (v.Overflow)
                {
                    _overflow = true;
                    if (wheel != 0f)
                    {
                        var step = ThemeTokens.GetFloat("size.tooltip.scroll.step", 60f);
                        v.Scroll.verticalNormalizedPosition = TooltipMath.Scrolled(v.Scroll.verticalNormalizedPosition, wheel, step, contentH, clipH);
                    }
                }
                Indicators(v, clipH, contentH, scale / lossy);
            }
        }

        private static void SetHeightKeepTop(RectTransform rt, float h)
        {
            var old = rt.rect.height;
            if (Mathf.Abs(old - h) < 0.5f) return;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            rt.anchoredPosition += new Vector2(0f, (old - h) * (1f - rt.pivot.y));
        }

        private static void Indicators(View v, float clipH, float contentH, float pxToLocal)
        {
            var n = v.Scroll.verticalNormalizedPosition;
            TooltipMath.Fades(v.Overflow, n, out var top, out var bottom);
            Show(v.Track, v.Overflow);
            Show(v.Handle, v.Overflow);
            Show(v.FadeTop, top);
            Show(v.FadeBottom, bottom);
            if (!v.Overflow) return;
            var w = ThemeTokens.GetFloat("size.tooltip.scrollbar", 4f) * pxToLocal;
            var inset = ThemeTokens.GetFloat("size.tooltip.scrollbar.inset", 6f) * pxToLocal;
            var fadeH = ThemeTokens.GetFloat("size.tooltip.fade", 24f) * pxToLocal;
            var track = v.Track.rectTransform;
            track.sizeDelta = new Vector2(w, -2f * inset);
            track.anchoredPosition = new Vector2(-inset, 0f);
            // The clip may be taller than the visible box (Epic Loot's fixed-height view): the track spans the clip.
            TooltipMath.Handle(clipH, contentH, n, Mathf.Max(0f, track.rect.height), w * 3f, out var hh, out var hy);
            var handle = v.Handle.rectTransform;
            handle.sizeDelta = new Vector2(0f, hh);
            handle.anchoredPosition = new Vector2(0f, hy);
            v.FadeTop.rectTransform.sizeDelta = new Vector2(0f, fadeH);
            v.FadeBottom.rectTransform.sizeDelta = new Vector2(0f, fadeH);
        }

        private static void Show(Image img, bool on)
        {
            if (img && img.enabled != on) img.enabled = on;
        }

        // ---------------------------------------------------------------- placement

        private void Place(TipRect bounds)
        {
            if (!Visible(out var vis)) return;
            var anchor = Anchor();
            var target = TooltipMath.Place(anchor, vis.W, vis.H, bounds, ThemeTokens.GetFloat("size.tooltip.gap", 10f) * Scale());
            var dx = target.XMin - vis.XMin;
            var dy = target.YMin - vis.YMin;
            if (Mathf.Abs(dx) > 0.01f || Mathf.Abs(dy) > 0.01f) transform.position += new Vector3(dx, dy, 0f);
        }

        // The hovered element (UITooltip.m_current: the slot / button owning the tooltip), else the pointer.
        private TipRect Anchor()
        {
            var cur = UITooltip.m_current;
            if (cur && UITooltip.m_tooltip == gameObject && cur.transform is RectTransform rt)
            {
                rt.GetWorldCorners(_corners);
                return new TipRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
            }
            var p = ZInput.pointerPosition;
            return new TipRect(p.x, p.y, p.x, p.y);
        }

        // On-screen box of the tooltip: the visible part of each scroll view (content inside its clip), else
        // RuneSkin's bare-text frame, else the first shown child (the panel UITooltip clamps, UITooltip.cs:93).
        private bool Visible(out TipRect vis)
        {
            vis = default;
            var any = false;
            foreach (var v in _views)
            {
                if (!v.Scroll || !v.Scroll.gameObject.activeInHierarchy) continue;
                var r = World(v.Clip).Intersect(World(v.Content));
                if (r.Empty) continue;
                vis = any ? vis.Union(r) : r;
                any = true;
            }
            if (any) return true;
            if (_frame && _frame.TryGetComponent<Image>(out var fi) && fi.enabled)
            {
                vis = World((RectTransform)_frame.transform);
                return !vis.Empty;
            }
            for (var i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i) is RectTransform c && c.gameObject.activeSelf)
                {
                    vis = World(c);
                    return !vis.Empty;
                }
            }
            return false;
        }

        private TipRect World(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            return new TipRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
        }

        // Diagnostics for the harness: the shown box, the strip floor and the overlay sort.
        public bool Probe(out TipRect box, out float floor, out int order, out bool overflow, out float normalized)
        {
            box = default;
            floor = Bounds().YMin;
            order = TryGetComponent<Canvas>(out var c) ? c.sortingOrder : 0;
            overflow = _overflow;
            normalized = 1f;
            foreach (var v in _views)
                if (v.Scroll && v.Overflow) { normalized = v.Scroll.verticalNormalizedPosition; break; }
            return Visible(out box);
        }
    }
}
