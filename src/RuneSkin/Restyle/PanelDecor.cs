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
            decor.RaiseCaps();
            decor.Layout();
            return decor;
        }

        public const string CapsName = "RuneSkinPanelCaps";

        public string PadToken;
        public int Revision = -1;
        private RectTransform _backing, _glass, _plaque, _caps;
        private RectTransform[] _corners;
        private bool _fitted;

        // Geometry from tokens (pad, glass inset, plaque size, corner native sizes); re-run on theme reload.
        private void Layout()
        {
            Revision = ThemeTokens.Revision;
            var self = (RectTransform)transform;
            if (!_fitted) Stretch(self, -ThemeTokens.GetFloat(PadToken, 0f));
            foreach (Transform child in self)
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
            _fitted = true;
            // The stack reaches at least the caps' keep-out beyond the children, so no child runs under a cap.
            var pad = Mathf.Max(ThemeTokens.GetFloat(padToken, 0f), CapClearance(0f));
            min -= new Vector2(pad, pad);
            max += new Vector2(pad, pad);
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
        public float ContentInset() => _fitted ? 0f : CapClearance(ThemeTokens.GetFloat(PadToken, 0f));

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
            var mark = go.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = role;
            mark.Reason = "RuneSkin panel stack layer";
            mark.BoundInstance = go.GetInstanceID();
            mark.hideFlags = HideFlags.HideInInspector;
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
