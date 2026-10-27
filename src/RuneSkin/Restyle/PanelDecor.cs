using System.Collections.Generic;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Something RuneSkin added or changed beyond single graphic properties; RevertRoot undoes it.
    public interface IRevertible
    {
        void Revert();
    }

    // The panel stack (art contract, art\README.md): opaque backing (color.panel.backing) + glass fill inset under the frame (Tiled, centre empty), the rune
    // glow layer tinted on top, four leather corner caps at native size with their pivot on the panel corner, and an
    // optional header plaque on the top edge. Glass/frame/glow/plaque are child graphics behind the host's own
    // children; the corner caps sit in a sibling container above them (art df8dc3a: caps overlap the content edge).
    // Both are stretched by anchors (vanilla layout moves/resizes the host, the stack follows without per-frame code).
    // Each child is an Internal SkinMark graphic, so theme reloads restyle it and RevertRoot destroys it.
    [DisallowMultipleComponent]
    public sealed class PanelDecor : MonoBehaviour, IRevertible
    {
        public const string ChildName = "RuneSkinPanel";

        public static PanelDecor Ensure(RectTransform host, string padToken, bool glass, bool plaque)
        {
            if (!host) return null;
            var existing = host.Find(ChildName);
            if (existing && existing.TryGetComponent<PanelDecor>(out var decor))
            {
                if (decor.Revision != ThemeTokens.Revision) decor.Layout(); // theme reload: geometry tokens again
                decor.RaiseCaps();
                return decor;
            }

            var go = new GameObject(ChildName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.SetAsFirstSibling(); // behind the host's own content
            go.AddComponent<LayoutElement>().ignoreLayout = true; // hosts may be layout groups (tooltip content)
            decor = go.AddComponent<PanelDecor>();
            decor.PadToken = padToken;
            if (glass)
            {
                // Opaque backing under the glass: fill_glass is alpha 0.9 by design and UI blends in linear space,
                // so one glass layer alone lets the world read through (~25 % luminance contrast in the pause menu).
                decor._backing = Add(rect, "Backing", "decor.backing");
                decor._glass = Add(rect, "Glass", "decor.glass");
            }
            Add(rect, "Frame", "decor.frame");
            Add(rect, "Glow", "decor.glow");
            if (plaque) decor._plaque = Add(rect, "Plaque", "decor.plaque");
            var caps = new GameObject(CapsName, typeof(RectTransform));
            decor._caps = (RectTransform)caps.transform;
            decor._caps.SetParent(host, false);
            caps.AddComponent<LayoutElement>().ignoreLayout = true;
            caps.AddComponent<PanelCaps>();
            decor._corners = new[]
            {
                Add(decor._caps, "CornerTL", "decor.corner.tl"), Add(decor._caps, "CornerTR", "decor.corner.tr"),
                Add(decor._caps, "CornerBL", "decor.corner.bl"), Add(decor._caps, "CornerBR", "decor.corner.br")
            };
            foreach (var corner in decor._corners) corner.gameObject.AddComponent<FrameOrnament>().Owner = decor;
            decor._frame = rect.Find("Frame")?.GetComponent<Image>();
            decor.RaiseCaps();
            decor.Layout();
            return decor;
        }

        public const string CapsName = "RuneSkinPanelCaps";

        public string PadToken;
        public int Revision = -1;
        private Image _frame;

        // Every live panel stack (window headers keep inside their frame: RuneWindowHeader.FrameInsets).
        public static readonly HashSet<PanelDecor> All = new HashSet<PanelDecor>();

        // The frame layer (9-slice border art) of this stack.
        // Bound by name on a cloned stack (Rebind): Epic Loot's dialogs clone the restyled variant dialog, and the clone's
        // stack had no frame reference, so ElDialogs measured no drawn border (log "side insets 10/10", run 20261027-174931).
        public Image Frame
        {
            get
            {
                if (!_frame) _frame = transform.Find("Frame")?.GetComponent<Image>();
                return _frame;
            }
        }

        // The opaque backing quad (glass stacks only; null otherwise).
        public RectTransform Backing => _backing;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private RectTransform _backing, _glass, _plaque, _caps;
        private RectTransform[] _corners;
        private bool _fitted;

        // Geometry from tokens (pad, glass inset, plaque size, corner native sizes); re-run on theme reload.
        private void Layout()
        {
            Revision = ThemeTokens.Revision;
            var self = (RectTransform)transform;
            if (!_fitted) Stretch(self, -ThemeTokens.GetFloat(PadToken, 0f));
            foreach (var child in Children.Of(self))
                if (child is RectTransform r && r != _plaque)
                    Stretch(r, r == _glass || r == _backing ? ThemeTokens.GetFloat("size.panel.glass.inset", 0f) : 0f);
            SyncCaps();
            if (_corners != null)
            {
                Corner(_corners[0], new Vector2(0f, 1f));
                Corner(_corners[1], new Vector2(1f, 1f));
                Corner(_corners[2], new Vector2(0f, 0f));
                Corner(_corners[3], new Vector2(1f, 0f));
            }
            if (_plaque)
            {
                _plaque.anchorMin = _plaque.anchorMax = new Vector2(0.5f, 1f); // centred on the top edge
                _plaque.pivot = new Vector2(0.5f, 0.5f);
                _plaque.anchoredPosition = Vector2.zero;
                _plaque.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.plaque.width", 0f), ThemeTokens.GetFloat("size.plaque.height", 0f));
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        // Hosts whose own rect does not cover their laid-out children (the pause menu's entry column is smaller than
        // its entries): the stack is sized to the union of the active children's rects plus the pad, in host space.
        // Called after a layout pass, never per frame.
        public void FitToChildren(string padToken)
        {
            var self = (RectTransform)transform;
            var host = self.parent as RectTransform;
            if (!host) return;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < host.childCount; i++)
            {
                var child = host.GetChild(i) as RectTransform;
                if (!child || child == self || child == _caps || !child.gameObject.activeSelf) continue;
                child.GetWorldCorners(Corners);
                foreach (var w in Corners)
                {
                    var l = (Vector2)host.InverseTransformPoint(w);
                    min = Vector2.Min(min, l);
                    max = Vector2.Max(max, l);
                }
            }
            if (min.x > max.x) return;
            // The stack reaches at least the caps' keep-out beyond the children, so no child runs under a cap.
            var pad = Mathf.Max(ThemeTokens.GetFloat(padToken, 0f), CapClearance(0f));
            SetBox(host, min - new Vector2(pad, pad), max + new Vector2(pad, pad));
        }

        // Dialogs (one box = backing + glass + frame + caps, user 2026-10-23 "Выйти из игры?"): the stack is sized to
        // what the dialog shows - text by its rendered bounds (TMP textBounds, not the vanilla fixed text rect),
        // buttons / other children by their rects - plus the pad (at least the caps' keep-out), widened to minW x minH.
        // Vanilla panel images switched off under the stack (PanelSwap) are not content. Host space; after a layout pass.
        public bool FitToContent(string padToken, float minW, float minH)
        {
            var self = (RectTransform)transform;
            var host = self.parent as RectTransform;
            if (!host) return false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < host.childCount; i++)
            {
                var child = host.GetChild(i) as RectTransform;
                if (!child || child == self || child == _caps || !child.gameObject.activeSelf) continue;
                if (child.GetComponent<PreWorld.PanelSwap>()) continue;
                if (child.TryGetComponent<TMPro.TMP_Text>(out var text) && text.enabled)
                {
                    if (string.IsNullOrEmpty(text.text)) continue;
                    text.ForceMeshUpdate();
                    var b = text.textBounds;
                    if (b.size.x <= 0f || b.size.y <= 0f) continue;
                    Grow(host, child, new Vector3(b.min.x, b.min.y), ref min, ref max);
                    Grow(host, child, new Vector3(b.max.x, b.max.y), ref min, ref max);
                    continue;
                }
                if (child.GetComponentInChildren<Graphic>(false) == null) continue; // layout-only / empty holder
                child.GetWorldCorners(Corners);
                foreach (var w in Corners)
                {
                    var l = (Vector2)host.InverseTransformPoint(w);
                    min = Vector2.Min(min, l);
                    max = Vector2.Max(max, l);
                }
            }
            var pad = Mathf.Max(ThemeTokens.GetFloat(padToken, 0f), CapClearance(0f));
            // Left / right: past the whole cap (rune end-column + brace) - a dialog is shorter than two caps.
            var boxH = Mathf.Max(minH, max.y - min.y + 2f * pad);
            var padX = boxH < CapsHeight() ? Mathf.Max(pad, CapWidth() + ThemeTokens.GetFloat("size.frame.text.margin", 8f)) : pad;
            if (!PanelMath.FitBox(min.x, min.y, max.x, max.y, padX, pad, minW, minH, out var x0, out var y0, out var x1, out var y1)) return false;
            SetBox(host, new Vector2(x0, y0), new Vector2(x1, y1));
            return true;
        }

        private static void Grow(RectTransform host, Transform child, Vector3 local, ref Vector2 min, ref Vector2 max)
        {
            var l = (Vector2)host.InverseTransformPoint(child.TransformPoint(local));
            min = Vector2.Min(min, l);
            max = Vector2.Max(max, l);
        }

        // The stack's box in host space (local min / max).
        private void SetBox(RectTransform host, Vector2 min, Vector2 max)
        {
            var self = (RectTransform)transform;
            _fitted = true;
            // Anchors at the host pivot, so the local-space box maps 1:1 onto anchoredPosition/sizeDelta.
            self.anchorMin = self.anchorMax = host.pivot;
            self.pivot = new Vector2(0.5f, 0.5f);
            var hostRect = host.rect;
            var pivotLocal = new Vector2(hostRect.xMin + hostRect.width * host.pivot.x, hostRect.yMin + hostRect.height * host.pivot.y);
            self.sizeDelta = max - min;
            self.anchoredPosition = (min + max) * 0.5f - pivotLocal;
            SyncCaps();
            RaiseCaps();
        }

        // Inset the host's content needs so nothing runs under a corner cap (ref px, host space): the largest cap
        // keep-out (theme "clear.<asset>", PanelMath.CapInset) minus how far the stack reaches beyond the host.
        public float ContentInset()
        {
            Rebind();
            return _fitted ? 0f : CapClearance(ThemeTokens.GetFloat(PadToken, 0f));
        }

        // Corner caps TL, TR, BL, BR (null when the stack has none).
        public RectTransform[] CornerCaps
        {
            get
            {
                Rebind();
                return _corners;
            }
        }

        // Instantiate copies the stack's objects but not these private references (not serialized): the stack of a
        // cloned decorated host (Epic Loot clones the restyled vanilla variant dialog, ElDialogs) had no corners and so
        // no keep-out. Bound again by name, once, from the clone's own children.
        private void Rebind()
        {
            if (_corners != null) return;
            var self = transform;
            _backing = self.Find("Backing") as RectTransform;
            _glass = self.Find("Glass") as RectTransform;
            _plaque = self.Find("Plaque") as RectTransform;
            if (!_frame) _frame = self.Find("Frame")?.GetComponent<Image>();
            _caps = self.parent ? self.parent.Find(CapsName) as RectTransform : null;
            if (!_caps) return;
            _corners = new[] { _caps.Find("CornerTL") as RectTransform, _caps.Find("CornerTR") as RectTransform, _caps.Find("CornerBL") as RectTransform, _caps.Find("CornerBR") as RectTransform };
        }

        // THE window content inset (canvas units of `canvas`) of the panel stack on `host` (0 = no stack): every owner
        // that lays a window's inner content out keeps it at least this far from the host's left / right / bottom edges
        // (and below the header at the top), so no row, plate or button runs under a corner cap (user 2026-10-26:
        // footer plates and the craft button under the bottom corner straps). One rule for every window
        // (PanelMath.CapInset: a rect inset by the keep-out on both sides of a corner never touches the cap).
        // The panel stack on `host` (null when none).
        public static PanelDecor Of(Transform host) => host && host.Find(ChildName) is Transform s && s.TryGetComponent<PanelDecor>(out var d) ? d : null;

        public static float KeepOut(Transform host, RectTransform canvas)
        {
            var stack = host ? host.Find(ChildName) : null;
            if (!stack || !stack.TryGetComponent<PanelDecor>(out var decor) || !canvas) return 0f;
            return decor.ContentInset() * host.lossyScale.x / Mathf.Max(InventoryScreen.Geo.WorldPerUnit(canvas), 1e-6f);
        }

        // Widest corner cap (native size, host units): content further than this from the left / right edge never
        // runs under a cap whatever its height (wide short hosts such as the HUD band use it instead of ContentInset).
        public float CapWidth()
        {
            var w = 0f;
            if (_corners == null) return w;
            foreach (var corner in _corners)
                if (corner && corner.TryGetComponent<Image>(out var image) && image.enabled && image.sprite) w = Mathf.Max(w, corner.rect.width);
            return w;
        }

        // Host-side inset left / right that keeps text clear of the whole corner caps (rune end-column + copper brace)
        // plus size.frame.text.margin, whatever the host height (user 2026-10-23, map filter hint).
        // Only for a stack shorter than its top + bottom caps (they meet into one end-column on each side); a taller
        // stack keeps its middle clear and ContentInset's keep-out suffices (tooltips keep their text width).
        public float SideInset() => SideInset(((RectTransform)transform).rect.height);

        public float SideInset(float stackHeight)
        {
            var reach = _fitted ? 0f : ThemeTokens.GetFloat(PadToken, 0f);
            var cap = CapWidth();
            if (!(cap > 0f) || stackHeight >= CapsHeight()) return 0f;
            return Mathf.Max(0f, cap - reach) + ThemeTokens.GetFloat("size.frame.text.margin", 8f);
        }

        // Top + bottom cap heights on the taller side (host units).
        public float CapsHeight()
        {
            if (_corners == null) return 0f;
            float H(RectTransform c) => c && c.TryGetComponent<Image>(out var i) && i.enabled && i.sprite ? c.rect.height : 0f;
            return Mathf.Max(H(_corners[0]) + H(_corners[2]), H(_corners[1]) + H(_corners[3]));
        }

        private float CapClearance(float pad)
        {
            var inset = 0f;
            if (_corners == null) return inset;
            foreach (var corner in _corners)
            {
                if (!corner || !corner.TryGetComponent<Image>(out var image) || !image.enabled || !image.sprite) continue;
                var s = image.sprite;
                var asset = s.name.EndsWith("(Clone)", System.StringComparison.Ordinal) ? s.name.Substring(0, s.name.Length - 7) : s.name;
                inset = Mathf.Max(inset, PanelMath.CapInset(ThemeTokens.GetFloat("clear." + asset, 0f), s.pixelsPerUnit, pad));
            }
            return inset;
        }

        // Caps container: same box as the stack, drawn after every host child (re-raised on each Ensure/fit, as
        // hosts may add children after the stack was built).
        public void RaiseCaps()
        {
            if (_caps) _caps.SetAsLastSibling();
        }

        private void SyncCaps()
        {
            if (!_caps) return;
            var self = (RectTransform)transform;
            _caps.anchorMin = self.anchorMin;
            _caps.anchorMax = self.anchorMax;
            _caps.pivot = self.pivot;
            _caps.anchoredPosition = self.anchoredPosition;
            _caps.sizeDelta = self.sizeDelta;
        }

        public void Revert()
        {
            if (_caps) Destroy(_caps.gameObject);
            if (this) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_caps) Destroy(_caps.gameObject);
        }

        private static RectTransform Add(RectTransform parent, string name, string role)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            var mark = SkinMark.Own(go, role, "RuneSkin panel stack layer");
            // Applied now (corners need their sprite for the native size); the root pass then finds it current.
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            image.enabled = mark.Outcome == Outcome.Styled; // missing art draws nothing, never a white box
            return rect;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // Corner caps: never stretched, native size, pivot = the panel corner they sit on.
        private static void Corner(RectTransform rect, Vector2 corner)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = Vector2.zero;
            if (rect.TryGetComponent<Image>(out var image)) Restyler.SetReferenceSize(image);
        }
    }

    // A frame ornament a window header must stay clear of (PanelDecor corner caps, the inventory's shared junction pieces):
    // a live registry, no scene search.
    [DisallowMultipleComponent]
    public sealed class FrameOrnament : MonoBehaviour
    {
        public static readonly HashSet<FrameOrnament> All = new HashSet<FrameOrnament>();

        // The stack whose corner this is (null: a piece shared by several windows, e.g. a junction).
        public PanelDecor Owner;

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);
    }

    // Marks the corner-cap container of a PanelDecor (a host sibling drawn above the host content).
    [DisallowMultipleComponent]
    public sealed class PanelCaps : MonoBehaviour, IRevertible
    {
        public void Revert()
        {
            if (this) Destroy(gameObject);
        }
    }
}
