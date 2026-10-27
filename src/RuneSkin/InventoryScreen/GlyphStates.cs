using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.InventoryScreen
{
    // Per-state glyph look of a header action button (approved variant B, 2026-10-27): the button's own SpriteSwap draws
    // the frame states; the glyph is a second graphic a Selectable cannot tint, so this follows the same pointer events:
    // normal = soft silver (color.action.glyph.header, Codex r2: pure white sat on the stone too hard), hover + pressed = rune cyan (color.action.glyph.hover), pressed also sinks
    // the glyph a little (share.action.glyph.pressed). Disabled = normal.
    [DisallowMultipleComponent]
    internal sealed class GlyphStates : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Image Glyph;
        private Selectable _owner;
        private bool _over, _down;

        private void Awake() => _owner = GetComponent<Selectable>();

        private void OnEnable()
        {
            _over = _down = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData e) { _over = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { _down = true; Apply(); } }
        public void OnPointerUp(PointerEventData e) { _down = false; Apply(); }

        public void Apply()
        {
            if (!Glyph) return;
            var live = !_owner || _owner.IsInteractable();
            var lit = live && (_over || _down);
            var c = lit ? ThemeTokens.GetColor("color.action.glyph.hover", new Color(0.31f, 0.88f, 0.9f, 1f))
                : ThemeTokens.GetColor("color.action.glyph.header", ThemeTokens.GetColor("color.action.glyph", new Color(0.89f, 0.9f, 0.91f, 1f)));
            if (Glyph.color != c) Glyph.color = c;
            var s = live && _down ? ThemeTokens.GetFloat("share.action.glyph.pressed", 0.88f) : 1f;
            var scale = new Vector3(s, s, 1f);
            if (Glyph.rectTransform.localScale != scale) Glyph.rectTransform.localScale = scale;
        }
    }
}
