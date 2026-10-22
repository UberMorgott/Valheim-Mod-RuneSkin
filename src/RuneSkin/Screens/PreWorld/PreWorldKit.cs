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
    // and 3D viewports stay content, text buttons get a button plate, a rune spinner (fixed runes, chasing glow).
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

        // Readable floor for the pre-world screens (m5-text-min, size.text.readable = 16 px at 1080p; runs 20261022-205726
        // / -210451: settings stepper values 14.9, the sex toggle label 15.9, the modded notice 15): fixed sizes below it
        // rise to it, auto-size minima below it rise to it. Inactive templates included (clones copy it).
        public static void ReadableFloor(Transform root)
        {
            if (!root) return;
            var floor = ThemeTokens.GetFloat("size.text.readable", 16f);
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                // Plate labels shrink to their plate by design (PlateLabelFit wraps first; a floor truncated them, run
                // 20261022-210451); size groups carry their own floor.
                if (t.GetComponent<PlateLabelFit>() || t.GetComponent<TextSizeGroup>()) continue;
                // Measured where it renders: screen code scales subtrees / sets sizes after the restyle (settings stepper
                // 14.9 px, sex toggles 15.5 px with a one-shot floor, run 20261022-232945), so the floor re-applies for a
                // sweep keeps it every 30 frames (ReadableSweep, in screen px like the check).
                if (t.isActiveAndEnabled) ReadableSweep.Apply(t, floor);
            }
            if (!root.TryGetComponent<ReadableSweep>(out var sweep))
            {
                sweep = root.gameObject.AddComponent<ReadableSweep>();
                sweep.hideFlags = HideFlags.HideInInspector;
            }
            sweep.Floor = floor;
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
                // Controller map: the art stays, its labels take the body face (two-font theme, run 20261022-230744).
                if (Under(g.transform, root, "GamepadMap")) { if (g is TMP_Text) ctx.Role(g, "text.body", "controller map label (body face)"); else ctx.Role(g, "content", "controller map art (Settings gamepad tab)"); continue; }
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
    // Readable floor on a pre-world root (PreWorldKit.ReadableFloor): every 30 frames the active texts under it are
    // measured where they render (screen px of one local unit, normalised to a 1080 px screen, the m5-text-min
    // measure) and raised to Floor px. A one-shot pass in canvas units missed texts made later (settings stepper value
    // 14.9 px, panels instantiated per tab) and the canvas scaler's own factor (sex toggles 15.5 px), runs
    // 20261022-232945 / 20261023-012017. Plate labels and size groups keep their own fit.
    [DisallowMultipleComponent]
    public sealed class ReadableSweep : MonoBehaviour, IRevertible
    {
        public float Floor = 16f;
        private int _next;
        private static readonly System.Collections.Generic.List<TMP_Text> Buffer = new System.Collections.Generic.List<TMP_Text>(128);

        private void LateUpdate()
        {
            if (Time.frameCount < _next || Screen.height <= 0) return;
            _next = Time.frameCount + 30;
            Buffer.Clear();
            GetComponentsInChildren(false, Buffer);
            foreach (var t in Buffer)
            {
                if (!t || !t.enabled) continue;
                if (t.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t.GetComponent<PlateLabelFit>() || t.GetComponent<TextSizeGroup>()) continue;
                Apply(t, Floor);
            }
            Buffer.Clear();
        }

        public static void Apply(TMP_Text t, float floor)
        {
            var canvas = t.canvas;
            if (!canvas) return;
            var root = canvas.rootCanvas;
            if (root.renderMode == RenderMode.WorldSpace) return;
            var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var p0 = RectTransformUtility.WorldToScreenPoint(cam, t.transform.TransformPoint(Vector3.zero));
            var p1 = RectTransformUtility.WorldToScreenPoint(cam, t.transform.TransformPoint(Vector3.up));
            var k = Vector2.Distance(p0, p1) * 1080f / Screen.height; // px@1080 per font unit
            if (!(k > 0.01f)) return;
            var want = floor / k + 0.05f;
            if (!t.enableAutoSizing) { if (t.fontSize < want - 0.05f) t.fontSize = want; }
            else
            {
                if (t.fontSizeMax < want) t.fontSizeMax = want;
                if (t.fontSizeMin < want) t.fontSizeMin = want;
            }
        }

        public void Revert() => Destroy(this);
    }
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

    // Rune spinner (0.10.2, user: the runes must not turn): the 8 runes stay FIXED on the ring (art spinner_rune_0..7 =
    // mechanical sector slices of spinner_runes, same frame, tint rune glow), a light runs through them clockwise -
    // each rune brightens as the head reaches it and fades over size.spinner.tail steps behind (PreWorldMath.RuneGlow). Per-rune
    // CanvasRenderer alpha only: no rotation, no mesh rebuild, no per-frame allocation. Unscaled time (runs while
    // loading stalls Time.timeScale). The spinner's own world rotation is held at its canvas' (an animated vanilla
    // parent never turns it). Without the slices the whole ring stays still and breathes.
    public sealed class RuneSpinner : MonoBehaviour
    {
        public const int Runes = 8;
        public float StepSeconds = 0.18f;
        private readonly Image[] _runes = new Image[Runes];
        private Image _base;
        private Transform _canvas;
        private bool _built, _sliced;
        private float _t;
        private Color _shownColor;

        public float Phase => _t / Mathf.Max(0.02f, ThemeTokens.GetFloat("time.spinner.step", StepSeconds));
        public bool Sliced => _sliced;

        private void OnEnable() { _t = 0f; }

        private void Build()
        {
            _built = true;
            _base = GetComponent<Image>();
            var canvas = GetComponentInParent<Canvas>();
            _canvas = canvas ? canvas.rootCanvas.transform : null;
            for (var i = 0; i < Runes; i++)
                if (!ThemeTokens.GetSprite("sprite.spinner.rune." + i)) return;
            for (var i = 0; i < Runes; i++)
            {
                var go = new GameObject("Rune" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var img = go.GetComponent<Image>();
                img.sprite = ThemeTokens.GetSprite("sprite.spinner.rune." + i);
                img.preserveAspect = true;
                img.raycastTarget = false;
                _runes[i] = img;
            }
            _sliced = true;
        }

        private void LateUpdate()
        {
            if (!_built) Build();
            _t += Time.unscaledDeltaTime;
            var phase = Phase;
            var low = ThemeTokens.GetFloat("share.spinner.base", 0.22f);
            if (_sliced)
            {
                if (_base && _base.enabled) _base.enabled = false;
                var c = _base ? _base.color : Color.white;
                var recolor = c != _shownColor;
                _shownColor = c;
                var tail = ThemeTokens.GetFloat("size.spinner.tail", 3f);
                for (var i = 0; i < Runes; i++)
                {
                    var img = _runes[i];
                    if (!img) continue;
                    if (recolor) img.color = c;
                    img.canvasRenderer.SetAlpha(PreWorldMath.RuneGlow(i, Runes, phase, tail, low));
                }
            }
            else if (_base)
                _base.canvasRenderer.SetAlpha(Mathf.Lerp(low, 1f, 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f / Runes)));
            var want = _canvas ? _canvas.rotation : Quaternion.identity;
            if (transform.rotation != want) transform.rotation = want;
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
