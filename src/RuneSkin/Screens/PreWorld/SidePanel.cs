using System;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // A vanilla side panel shown next to a window (modifier description ServerOptionsGUI.m_toolTipPanel, world source
    // notice FejdStartup.m_worldSourceInfoPanel, the changelog FejdStartup.m_changeLog): vanilla draws them as bare dark
    // rectangles at fixed spots that ran under the RuneSkin window stacks (the RuneSkin stack reaches size.panel.pad
    // beyond a vanilla window) or under the modal itself. Here: the vanilla backdrop is off under a RuneSkin panel stack
    // (PanelSwap + PanelDecor), the panel draws above its window (own Canvas, override sorting), and every frame it is
    // shown it docks beside the "window" box (the union of the given rects' RuneSkin stacks, root canvas space) with
    // size.sidepanel.gap: right of it, else left, kept inside the screen (PanelMath.DockBeside). Revert restores the
    // vanilla position and drops the canvas.
    [DisallowMultipleComponent]
    public sealed class SidePanel : MonoBehaviour, IRevertible
    {
        private Func<RectTransform[]> _window;
        private RectTransform[] _wins; // window rects, looked up on enable (no per-frame search)
        private Vector2 _vanilla;
        private bool _saved;
        private Canvas _canvas;
        private bool _addedCanvas;
        private bool _onTop = true;
        private bool _decor;
        private static readonly System.Collections.Generic.List<TMPro.TMP_Text> TextBuffer = new System.Collections.Generic.List<TMPro.TMP_Text>(4);

        // Texts inside the frame (run 20261022-210451: the modifier description ran left past the panel under its
        // rail): a text rect reaching past the panel horizontally is stretched across it, inset by the stack's content
        // inset; its vertical placement stays vanilla.
        private void FitTexts()
        {
            var rt = (RectTransform)transform;
            var pr = rt.rect;
            var inset = KeepOut();
            TextBuffer.Clear();
            GetComponentsInChildren(true, TextBuffer);
            foreach (var t in TextBuffer)
            {
                var tr = t.rectTransform;
                if (tr.parent != rt) continue;
                var a = rt.InverseTransformPoint(tr.TransformPoint(tr.rect.min));
                var b = rt.InverseTransformPoint(tr.TransformPoint(tr.rect.max));
                if (a.x >= pr.xMin + inset - 0.5f && b.x <= pr.xMax - inset + 0.5f) continue;
                tr.anchorMin = new Vector2(0f, tr.anchorMin.y);
                tr.anchorMax = new Vector2(1f, tr.anchorMax.y);
                tr.offsetMin = new Vector2(inset, tr.offsetMin.y);
                tr.offsetMax = new Vector2(-inset, tr.offsetMax.y);
            }
            TextBuffer.Clear();
        }
        private static readonly Vector3[] Corners = new Vector3[4];

        // Text inset from the panel edge: the corner caps' keep-out (PanelDecor.ContentInset: every opaque cap pixel lies
        // within it of an edge) plus size.frame.text.margin, at least size.sidepanel.inset; every side.
        private float KeepOut()
        {
            var floor = ThemeTokens.GetFloat("size.sidepanel.inset", 14f);
            var decor = GetComponentInChildren<PanelDecor>(true);
            return decor ? Mathf.Max(floor, decor.ContentInset() + ThemeTokens.GetFloat("size.frame.text.margin", 8f)) : floor;
        }
        private static readonly System.Collections.Generic.List<PanelDecor> DecorBuffer = new System.Collections.Generic.List<PanelDecor>(4);

        public static SidePanel Ensure(RectTransform panel, Func<RectTransform[]> window, string padToken = "size.tooltip.panel.pad", bool decor = true, bool onTop = true)
        {
            if (!panel) return null;
            if (decor)
            {
                foreach (var img in panel.GetComponents<Image>())
                    if (!(img.GetComponent<SkinMark>() is SkinMark m && m.Internal)) PanelSwap.Apply(img, img.sprite ? img.sprite.name : "flat");
                PanelDecor.Ensure(panel, padToken, glass: true, plaque: false);
            }
            if (!panel.TryGetComponent<SidePanel>(out var s))
            {
                s = panel.gameObject.AddComponent<SidePanel>();
                s.hideFlags = HideFlags.HideInInspector;
            }
            s._window = window;
            s._onTop = onTop;
            s._decor = decor;
            return s;
        }

        private void OnEnable()
        {
            // Top of its canvas: an own canvas sorted above the root (the modal / window it describes).
            if (_decor) FitTexts();
            if (!_onTop) { Dock(); return; }
            var root = transform.parent ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (!_canvas && !TryGetComponent(out _canvas))
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _addedCanvas = true;
                // Graphics register with their nearest canvas: without its own raycaster the panel's scroll view and
                // close button would stop taking clicks.
                if (!GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            }
            if (_addedCanvas && root)
            {
                _canvas.overrideSorting = true;
                _canvas.sortingLayerID = root.rootCanvas.sortingLayerID;
                _canvas.sortingOrder = root.rootCanvas.sortingOrder + 20;
            }
            _wins = null;
            Dock();
        }

        private void LateUpdate()
        {
            if (_decor) FitHeight();
            Dock();
        }

        private TMPro.TMP_Text _body;
        private string _bodyText;
        private float _baseH = -1f;

        private bool _stretched;
        private Vector2 _bodyAMin, _bodyAMax, _bodyOMin, _bodyOMax;

        // The body text (largest text rect; picked by rect, not by text: the description is empty when the panel first
        // enables, run 20261022-232945 picked the topic) is stretched down the panel from its vanilla top to the inset,
        // and a longer text than that grows the panel downward (top edge kept). Only fixed-height panels with the body
        // as a direct child.
        private void FitHeight()
        {
            var rt = (RectTransform)transform;
            if (!Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) return;
            var inset = KeepOut();
            if (!_body)
            {
                TextBuffer.Clear();
                GetComponentsInChildren(true, TextBuffer);
                var area = 0f;
                foreach (var t in TextBuffer)
                {
                    if (t.rectTransform.parent != rt) continue;
                    var r = t.rectTransform.rect;
                    if (r.width * r.height > area) { area = r.width * r.height; _body = t; }
                }
                TextBuffer.Clear();
                if (!_body) return;
            }
            var tr = _body.rectTransform;
            if (_baseH < 0f) _baseH = rt.sizeDelta.y;
            if (!_stretched)
            {
                _stretched = true;
                _bodyAMin = tr.anchorMin; _bodyAMax = tr.anchorMax; _bodyOMin = tr.offsetMin; _bodyOMax = tr.offsetMax;
                var top = rt.InverseTransformPoint(tr.TransformPoint(new Vector3(0f, tr.rect.yMax, 0f))).y;
                // Never above the corner caps' keep-out (0.20.3 track F: the modifier description's first line ran under
                // the top rune caps, "Ч" of "Частота" and the "т" of "зависит" cut).
                var topOff = Mathf.Max(inset, rt.rect.yMax - top);
                tr.anchorMin = new Vector2(tr.anchorMin.x, 0f);
                tr.anchorMax = new Vector2(tr.anchorMax.x, 1f);
                tr.offsetMin = new Vector2(tr.offsetMin.x, inset);
                tr.offsetMax = new Vector2(tr.offsetMax.x, -topOff);
                _bodyText = null;
            }
            if (ReferenceEquals(_body.text, _bodyText)) return;
            _bodyText = _body.text;
            var avail = tr.rect.height - (rt.sizeDelta.y - _baseH); // text height at the vanilla panel height
            var need = string.IsNullOrEmpty(_bodyText) ? 0f : _body.GetPreferredValues(_bodyText, tr.rect.width - _body.margin.x - _body.margin.z, 0f).y + _body.margin.y + _body.margin.w;
            var h = _baseH + Mathf.Max(0f, need - avail);
            if (Mathf.Abs(rt.sizeDelta.y - h) > 0.5f)
            {
                var topY = rt.rect.yMax;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
                rt.anchoredPosition += new Vector2(0f, topY - rt.rect.yMax); // keep the top edge
            }
        }
        public void Dock()
        {
            var rt = (RectTransform)transform;
            if (!_saved)
            {
                _vanilla = rt.anchoredPosition;
                _saved = true;
            }
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas || _window == null) return;
            var c = (RectTransform)canvas.rootCanvas.transform;
            var wins = _wins ?? (_wins = _window());
            if (wins == null) return;
            var wmin = new Vector2(float.MaxValue, float.MaxValue);
            var wmax = new Vector2(float.MinValue, float.MinValue);
            foreach (var w in wins)
            {
                if (!w || !w.gameObject.activeInHierarchy) continue;
                Union(w, c, ref wmin, ref wmax);
            }
            if (wmin.x > wmax.x) { _wins = null; return; }
            var pmin = new Vector2(float.MaxValue, float.MaxValue);
            var pmax = new Vector2(float.MinValue, float.MinValue);
            Union(rt, c, ref pmin, ref pmax);
            if (pmin.x > pmax.x) return;
            var r = c.rect;
            var (dx, dy) = PanelMath.DockBeside(wmin.x, wmin.y, wmax.x, wmax.y, pmin.x, pmin.y, pmax.x, pmax.y, r.xMin, r.yMin, r.xMax, r.yMax,
                ThemeTokens.GetFloat("size.sidepanel.gap", 24f));
            if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f) return;
            var world = c.TransformVector(new Vector3(dx, dy, 0f));
            rt.anchoredPosition += (Vector2)(rt.parent ? rt.parent.InverseTransformVector(world) : world);
        }

        // Box of a rect's RuneSkin stacks (frame + caps reach beyond the host), or of the rect itself without one.
        private static void Union(RectTransform host, RectTransform space, ref Vector2 min, ref Vector2 max)
        {
            var any = false;
            DecorBuffer.Clear();
            host.GetComponentsInChildren(false, DecorBuffer);
            foreach (var decor in DecorBuffer)
            {
                Add((RectTransform)decor.transform, space, ref min, ref max);
                any = true;
            }
            if (!any) Add(host, space, ref min, ref max);
        }

        private static void Add(RectTransform rt, RectTransform space, ref Vector2 min, ref Vector2 max)
        {
            rt.GetWorldCorners(Corners);
            foreach (var w in Corners)
            {
                var l = (Vector2)space.InverseTransformPoint(w);
                min = Vector2.Min(min, l);
                max = Vector2.Max(max, l);
            }
        }

        public void Revert()
        {
            if (_saved) ((RectTransform)transform).anchoredPosition = _vanilla;
            if (_baseH >= 0f) ((RectTransform)transform).sizeDelta = new Vector2(((RectTransform)transform).sizeDelta.x, _baseH);
            if (_stretched && _body)
            {
                var tr = _body.rectTransform;
                tr.anchorMin = _bodyAMin; tr.anchorMax = _bodyAMax; tr.offsetMin = _bodyOMin; tr.offsetMax = _bodyOMax;
            }
            if (_addedCanvas && _canvas)
            {
                if (TryGetComponent<GraphicRaycaster>(out var gr)) Destroy(gr);
                Destroy(_canvas);
            }
            Destroy(this);
        }
    }
}
