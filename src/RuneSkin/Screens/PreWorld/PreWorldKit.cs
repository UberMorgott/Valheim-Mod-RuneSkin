using System;
using System.Collections.Generic;
using RuneSkin.Classify;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // Shared pieces of the M5 pre-world families (art\README.md "M5 sprites"): vanilla wood / backdrop panel images
    // become the RuneSkin panel stack (PanelDecor behind the panel's content, the vanilla image off), gamepad key hints
    // and 3D viewports stay content, text buttons get a button plate, a rotating rune spinner.
    public static class PreWorldKit
    {
        public static bool IsPanelSprite(string sprite) => PreWorldMath.IsPanelSprite(sprite);

        // Every vanilla panel image under root (active or not): the panel stack goes behind its content, the vanilla
        // image is switched off (journaled through PanelSwap). Hosts smaller than the size floor are cells, not panels.
        public static int Panels(Transform root, string padToken, Func<Image, bool> skip = null)
        {
            var n = 0;
            var min = ThemeTokens.GetFloat("size.preworld.panel.min", 120f);
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (!img || img.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                var sprite = img.sprite ? img.sprite.name : null;
                if (img.TryGetComponent<PanelSwap>(out var swapped)) sprite = swapped.OrigSprite;
                if (!IsPanelSprite(sprite) || (skip != null && skip(img))) continue;
                var r = img.rectTransform.rect;
                if (r.width < min || r.height < min * 0.5f) continue;
                // A panel inside another panel (world list inside the start-game window, settings content) is a
                // recessed well (frame_inset), not a second full stack with corner caps (Codex M5 r1).
                if (InsideDecor(img.transform, root))
                {
                    if (!img.GetComponent<WellMark>()) img.gameObject.AddComponent<WellMark>();
                    continue;
                }
                PanelSwap.Apply(img, sprite);
                PanelDecor.Ensure(img.rectTransform, padToken, glass: true, plaque: false);
                n++;
            }
            return n;
        }

        private static bool InsideDecor(Transform t, Transform root)
        {
            for (var p = t.parent; p; p = p.parent)
            {
                var d = p.Find(PanelDecor.ChildName);
                if (d && d.GetComponent<PanelDecor>()) return true;
                if (p == root) break;
            }
            return false;
        }

        // Gamepad key hint subtrees (vanilla "gamepad_hint", "KeyHint"): controller glyph art, content.
        public static bool IsKeyHint(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.StartsWith("gamepad_hint", StringComparison.Ordinal) || p.name == "KeyHint") return true;
            return false;
        }

        public static void Common(ScreenContext ctx, Transform root)
        {
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(g)) continue;
                if (g.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (g is RawImage) { ctx.Role(g, "content", "raw image (3D viewport / render)"); continue; }
                if (IsKeyHint(g.transform, root))
                {
                    // Glyph art stays; the hint's text takes the body font (vanilla Averia otherwise).
                    if (g is TMP_Text) ctx.Role(g, "text.body", "key hint label (body font)");
                    else ctx.Role(g, "content", "gamepad key hint (controller glyph art)");
                    continue;
                }
                if (g is Image img && img.TryGetComponent<PanelSwap>(out _)) { ctx.Role(g, "skip", "vanilla panel image (RuneSkin panel stack replaces it, off)"); continue; }
                if (g is Image well && well.GetComponent<WellMark>()) { ctx.Role(g, "well", "nested vanilla panel -> recessed well (frame_inset)"); continue; }
                if (g is TMP_Text t && t.GetComponentInParent<Button>(true) is Button b && b.targetGraphic != g && b.GetComponent<ButtonPlate>())
                {
                    ctx.Role(g, "button.label", "label of a plated button");
                    continue;
                }
                if (Under(g.transform, root, "GamepadMap")) { ctx.Role(g, "content", "controller map art (Settings gamepad tab)"); continue; }
                if (g is TMP_SelectionCaret) { ctx.Role(g, "content", "input caret"); continue; }
                if (g is Text) { ctx.Role(g, "text.legacy", "legacy uGUI text (another mod's menu line)"); continue; }
                if (g is Image image) Fallback(ctx, image);
            }
        }

        private static bool Under(Transform t, Transform root, string fragment)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // Images the classifier would leave unknown on these vanilla forms: outside every control, layout or scroll
        // structure and not mapped by the theme's sprite roles. Sprite-less = layout / hit areas (skip), the vanilla
        // input backdrop = field art, any other sprite = vanilla icon / art kept as content (status icons, logos,
        // gradients, blockers). Controls stay with the classifier's component semantics.
        private static void Fallback(ScreenContext ctx, Image img)
        {
            // Controls and button target graphics stay with the classifier; icons inside a button / row (status, trash,
            // refresh, save source glyphs) fall through to the rules below.
            var sel = img.GetComponentInParent<Selectable>(true);
            if (sel && (sel.targetGraphic == img || !(sel is Button) || img.GetComponentInParent<TMP_Dropdown>(true))) return;
            if (img.GetComponentInParent<Scrollbar>(true)) return;
            if (img.GetComponent<Mask>() || img.GetComponent<RectMask2D>() || img.GetComponent<ScrollRect>()) return;
            var scroll = img.GetComponentInParent<ScrollRect>(true);
            if (scroll && scroll.viewport == img.rectTransform) return;
            var sprite = img.sprite ? img.sprite.name : null;
            if (img.GetComponent<SkinMark>() is SkinMark mark && !string.IsNullOrEmpty(mark.OriginalSprite)) sprite = mark.OriginalSprite;
            if (sprite == null)
            {
                if (img.rectTransform.anchorMin == Vector2.zero && img.rectTransform.anchorMax == Vector2.one) return; // scrim semantics
                ctx.Role(img, "skip", "sprite-less layout / hit-area image");
                return;
            }
            if (Classifier.BySprite(sprite, ctx.Family) != null) return;
            if (sprite.StartsWith("InputFieldBackground", StringComparison.Ordinal)) { ctx.Role(img, "field", $"vanilla input backdrop '{sprite}'"); return; }
            ctx.Role(img, "content", $"vanilla icon / art '{sprite}' (kept)");
        }
    }

    // A vanilla panel image nested inside another skinned panel: styled as a well (role "well").
    public sealed class WellMark : MonoBehaviour
    {
    }

    // Vanilla panel image switched off under the RuneSkin panel stack; Revert turns it back on.
    [DisallowMultipleComponent]
    public sealed class PanelSwap : MonoBehaviour, IRevertible
    {
        public string OrigSprite;
        public bool WasEnabled;

        public static void Apply(Image img, string sprite)
        {
            if (!img.TryGetComponent<PanelSwap>(out var s))
            {
                s = img.gameObject.AddComponent<PanelSwap>();
                s.hideFlags = HideFlags.HideInInspector;
                s.OrigSprite = sprite;
                s.WasEnabled = img.enabled;
            }
            img.enabled = false;
        }

        public void Revert()
        {
            if (TryGetComponent<Image>(out var img)) img.enabled = WasEnabled;
            Destroy(this);
        }
    }

    // A text-only button (vanilla menu entries: Transition.Animation + Animator writing the label colour) gets a
    // RuneSkin plate image (role "button": button_* sprites with SpriteSwap states) as its target graphic; the Animator
    // and the vanilla knot decor are off, the label stays a child (role button.label). Journaled for RevertRoot.
    [DisallowMultipleComponent]
    public sealed class ButtonPlate : MonoBehaviour, IRevertible
    {
        public const string PlateName = "RuneSkin_Plate";
        private Button _button;
        private Graphic _origTarget;
        private Selectable.Transition _origTransition;
        private Animator _animator;
        private bool _animatorWas;
        private readonly List<Graphic> _hidden = new List<Graphic>();
        private Image _plate;

        public Image Plate => _plate;

        public static ButtonPlate Ensure(Button button, string role = "button")
        {
            if (!button) return null;
            if (button.TryGetComponent<ButtonPlate>(out var p)) return p;
            p = button.gameObject.AddComponent<ButtonPlate>();
            p.hideFlags = HideFlags.HideInInspector;
            p._button = button;
            p._origTarget = button.targetGraphic;
            p._origTransition = button.transition;
            p._animator = button.GetComponent<Animator>();
            p._animatorWas = p._animator && p._animator.enabled;
            if (p._animator) p._animator.enabled = false;
            foreach (var g in button.GetComponentsInChildren<Image>(true))
            {
                if (!g.name.EndsWith("Knot", StringComparison.Ordinal) && !g.name.StartsWith("selected", StringComparison.OrdinalIgnoreCase)) continue;
                if (PreWorldKit.IsKeyHint(g.transform, button.transform)) continue;
                if (g.enabled) { g.enabled = false; p._hidden.Add(g); }
            }
            var go = new GameObject(PlateName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(button.transform, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            p._plate = go.GetComponent<Image>();
            p._plate.raycastTarget = true;
            // Internal mark: the Restyler applies the role (sprite + SpriteSwap states via the Button) on every pass.
            var mark = go.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = role;
            mark.Reason = "RuneSkin button plate (text-only vanilla button)";
            mark.ThemeRevision = -1;
            mark.BoundInstance = go.GetInstanceID();
            mark.hideFlags = HideFlags.HideInInspector;
            button.targetGraphic = p._plate;
            return p;
        }

        public void Revert()
        {
            if (_button)
            {
                _button.targetGraphic = _origTarget;
                _button.transition = _origTransition;
            }
            if (_animator) _animator.enabled = _animatorWas;
            foreach (var g in _hidden) if (g) g.enabled = true;
            if (_plate) Destroy(_plate.gameObject);
            Destroy(this);
        }
    }

    // Rune spinner (art: spinner_runes, tint rune glow): turns in 45 degree steps (8 runes on the ring), unscaled time
    // so it also runs while loading stalls Time.timeScale.
    public sealed class RuneSpinner : MonoBehaviour
    {
        public float StepSeconds = 0.18f;
        private float _t;
        private int _step;

        private void OnEnable() { _t = 0f; }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_t < StepSeconds) return;
            _t = 0f;
            _step = (_step + 1) & 7;
            transform.localRotation = Quaternion.Euler(0f, 0f, -45f * _step);
        }

        public static Image Add(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            var mark = go.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = "spinner";
            mark.Reason = "RuneSkin rune spinner (no real progress)";
            mark.ThemeRevision = -1;
            mark.BoundInstance = go.GetInstanceID();
            mark.hideFlags = HideFlags.HideInInspector;
            go.AddComponent<RuneSpinner>();
            return img;
        }
    }
}
