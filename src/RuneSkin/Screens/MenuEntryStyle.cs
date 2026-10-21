using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.Screens
{
    // Pause-menu entry states. Vanilla entries are Buttons with Transition.Animation (main scene bundle 17245031,
    // MenuEntries/Continue: m_Transition 3 + an Animator): the Animator's state clips write the label colour (orange on
    // highlight) and the knots' alpha every frame, so any colour RuneSkin set was overwritten. The owning components
    // are switched instead, journaled here: the Animator is disabled, the Button's transition becomes ColorTint on the
    // label (state colours from the menu.entry theme role, applied by the Restyler through Button.colors), and the
    // knots the clips used to fade are shown on hover/selection by these event handlers (no per-frame code).
    [DisallowMultipleComponent]
    public sealed class MenuEntryStyle : MonoBehaviour, IRevertible, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Button Button;
        public Animator Animator;
        public bool AnimatorWasEnabled;
        public Selectable.Transition OrigTransition;
        public Graphic OrigTarget;
        public Graphic[] Knots;
        public Color[] KnotColors;
        private bool _hover, _selected, _reverting;

        public static void Ensure(Button button, TMP_Text label)
        {
            if (!button || !label || button.TryGetComponent<MenuEntryStyle>(out _)) return;
            if (button.transition != Selectable.Transition.Animation) return; // not the vanilla animated entry
            var s = button.gameObject.AddComponent<MenuEntryStyle>();
            s.hideFlags = HideFlags.HideInInspector;
            s.Button = button;
            s.Animator = button.GetComponent<Animator>();
            s.AnimatorWasEnabled = s.Animator && s.Animator.enabled;
            if (s.Animator) s.Animator.enabled = false;
            s.OrigTransition = button.transition;
            s.OrigTarget = button.targetGraphic;
            button.targetGraphic = label;
            button.transition = Selectable.Transition.ColorTint;
            var knots = new System.Collections.Generic.List<Graphic>();
            foreach (var g in button.GetComponentsInChildren<Image>(true))
                if (g.name.EndsWith("Knot", System.StringComparison.Ordinal)) knots.Add(g);
            s.Knots = knots.ToArray();
            s.KnotColors = new Color[s.Knots.Length];
            for (var i = 0; i < s.Knots.Length; i++)
            {
                s.KnotColors[i] = s.Knots[i].color;
                var c = s.Knots[i].color;
                c.a = 1f; // the clips left it at their last alpha; visibility is the canvas alpha below
                s.Knots[i].color = c;
            }
            s.Show(false, true);
        }

        public void OnPointerEnter(PointerEventData eventData) { _hover = true; Show(true, false); }

        public void OnPointerExit(PointerEventData eventData) { _hover = false; Show(_selected, false); }

        public void OnSelect(BaseEventData eventData) { _selected = true; Show(true, false); }

        public void OnDeselect(BaseEventData eventData) { _selected = false; Show(_hover, false); }

        private void OnDisable()
        {
            if (_reverting) return; // Destroy(this) below disables first: keep the restored knot alpha
            _hover = _selected = false;
            Show(false, true);
        }

        private void Show(bool on, bool instant)
        {
            if (Knots == null) return;
            var fade = Button ? Button.colors.fadeDuration : 0.1f;
            foreach (var k in Knots)
                if (k) k.CrossFadeAlpha(on ? 1f : 0f, instant ? 0f : fade, true);
        }

        public void Revert()
        {
            _reverting = true;
            if (Button)
            {
                if (Button.transition == Selectable.Transition.ColorTint) Button.transition = OrigTransition;
                Button.targetGraphic = OrigTarget;
            }
            if (Animator) Animator.enabled = AnimatorWasEnabled;
            for (var i = 0; Knots != null && i < Knots.Length; i++)
                if (Knots[i])
                {
                    Knots[i].CrossFadeAlpha(1f, 0f, true);
                    Knots[i].color = KnotColors[i];
                }
            Destroy(this);
        }
    }
}
