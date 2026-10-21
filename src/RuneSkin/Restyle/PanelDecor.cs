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

    // The panel stack (art contract, art\README.md): glass fill inset under the frame (Tiled, centre empty), the rune
    // glow layer tinted on top, four leather corner caps at native size with their pivot on the panel corner, and an
    // optional header plaque on the top edge. Built once per host as child graphics behind the host's own children,
    // stretched by anchors (vanilla layout moves/resizes the host, the stack follows without any per-frame code).
    // Each child is an Internal SkinMark graphic, so theme reloads restyle it and RevertRoot destroys it.
    [DisallowMultipleComponent]
    public sealed class PanelDecor : MonoBehaviour, IRevertible
    {
        public const string ChildName = "RuneSkinPanel";

        public static PanelDecor Ensure(RectTransform host, string padToken, bool glass, bool plaque)
        {
            if (!host) return null;
            var existing = host.Find(ChildName);
            if (existing && existing.TryGetComponent<PanelDecor>(out var decor)) return decor;

            var go = new GameObject(ChildName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(host, false);
            rect.SetAsFirstSibling(); // behind the host's own content
            var pad = ThemeTokens.GetFloat(padToken, 0f);
            Stretch(rect, -pad);
            go.AddComponent<LayoutElement>().ignoreLayout = true; // hosts may be layout groups (tooltip content)
            decor = go.AddComponent<PanelDecor>();

            var inset = ThemeTokens.GetFloat("size.panel.glass.inset", 0f);
            if (glass) Stretch(Add(rect, "Glass", "decor.glass"), inset);
            Stretch(Add(rect, "Frame", "decor.frame"), 0f);
            Stretch(Add(rect, "Glow", "decor.glow"), 0f);
            Corner(Add(rect, "CornerTL", "decor.corner.tl"), new Vector2(0f, 1f));
            Corner(Add(rect, "CornerTR", "decor.corner.tr"), new Vector2(1f, 1f));
            Corner(Add(rect, "CornerBL", "decor.corner.bl"), new Vector2(0f, 0f));
            Corner(Add(rect, "CornerBR", "decor.corner.br"), new Vector2(1f, 0f));
            if (plaque)
            {
                var p = Add(rect, "Plaque", "decor.plaque");
                p.anchorMin = p.anchorMax = new Vector2(0.5f, 1f); // centred on the top edge
                p.pivot = new Vector2(0.5f, 0.5f);
                p.anchoredPosition = Vector2.zero;
                p.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.plaque.width", 0f), ThemeTokens.GetFloat("size.plaque.height", 0f));
            }
            return decor;
        }

        public void Revert()
        {
            if (this) Destroy(gameObject);
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
            if (rect.TryGetComponent<Image>(out var image) && image.sprite) image.SetNativeSize();
        }
    }
}
