using RuneSkin.Classify;
using RuneSkin.InventoryScreen;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.MapScreen
{
    // Map window maximize / restore (user 2026-10-26): a skinned icon button in the map header's aux slot toggles the
    // window between its place (vanilla m_largeRoot rect + the user's drag offset, MovableWindow) and the screen's safe
    // area. Maximized, the frame sits on the safe-area edges, the side panel stays docked right and the map image fills
    // the rest of the content rect (LargeLateUpdate). The state persists per resolution with the window positions
    // (WindowMove flag "map#max"); Layout.ResetLayout clears it. While maximized the window does not move (its drag
    // offset is kept for the restore).
    public sealed partial class MinimapSkin
    {
        public const string MaxFlag = "max";
        private const float MaxButtonShare = 0.9f; // aux slot width in header heights

        private Button _maxButton;
        private Image _maxGlyph;
        private string _maxRole;
        private bool _maxApplied;
        private Vector2 _restAnchorMin, _restAnchorMax, _restOffsetMin, _restOffsetMax;

        // Harness: the window is shown maximized.
        public bool Maximized => _maxApplied;

        private void BuildMaxButton()
        {
            var image = new GameObject("RuneSkinMapMaximize", typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(_large, false);
            var mark = DollCell.Mark(image, "button.icon", "map window maximize / restore button");
            mark.Outcome = Restyler.Apply(image, mark, ThemeTokens.Role("button.icon")) ? Outcome.Styled : Outcome.Degraded;
            if (mark.Outcome != Outcome.Styled) image.color = new Color(0.1f, 0.12f, 0.14f, 0.9f); // degraded: plain hit area
            _maxButton = image.gameObject.AddComponent<Button>();
            _maxButton.targetGraphic = image;
            _maxButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _maxButton.onClick.AddListener(ToggleMaximize);
            _maxGlyph = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Image>();
            _maxGlyph.rectTransform.SetParent(image.rectTransform, false);
            _maxGlyph.raycastTarget = false;
            _maxGlyph.preserveAspect = true;
            var gr = _maxGlyph.rectTransform;
            gr.anchorMin = new Vector2(0.18f, 0.18f);
            gr.anchorMax = new Vector2(0.82f, 0.82f);
            gr.offsetMin = gr.offsetMax = Vector2.zero;
        }

        public void ToggleMaximize() => WindowMove.SetFlag(WindowMove.Map, MaxFlag, !WindowMove.Flag(WindowMove.Map, MaxFlag));

        // Window rect: the safe area while the flag is on (re-fitted every frame: resolution / safe-area changes), the
        // restored rect + MovableWindow offset otherwise.
        private void Maximize()
        {
            if (!_large) return;
            var want = WindowMove.Flag(WindowMove.Map, MaxFlag);
            var mover = _large.GetComponent<MovableWindow>();
            if (want && !_maxApplied)
            {
                if (mover) mover.enabled = false; // puts vanilla's own position back (MovableWindow.OnDisable)
                _restAnchorMin = _large.anchorMin;
                _restAnchorMax = _large.anchorMax;
                _restOffsetMin = _large.offsetMin;
                _restOffsetMax = _large.offsetMax;
                _maxApplied = true;
            }
            else if (!want && _maxApplied)
            {
                _large.anchorMin = _restAnchorMin;
                _large.anchorMax = _restAnchorMax;
                _large.offsetMin = _restOffsetMin;
                _large.offsetMax = _restOffsetMax;
                _maxApplied = false;
                if (mover) mover.enabled = true; // re-bases on the restored rect, adds the user offset again
            }
            if (_maxApplied) FitSafeArea();
        }

        private void FitSafeArea()
        {
            if (!(_large.parent is RectTransform parent)) return;
            var canvas = _large.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            var cam = root && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            var safe = Screen.safeArea;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, cam, out var a)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, cam, out var b)) return;
            var origin = parent.rect.min;
            // Anchored at the parent's bottom-left corner: offsets = the safe-area corners relative to the parent rect min.
            var offMin = Vector2.Min(a, b) - origin;
            var offMax = Vector2.Max(a, b) - origin;
            if (_large.anchorMin != Vector2.zero) _large.anchorMin = Vector2.zero;
            if (_large.anchorMax != Vector2.zero) _large.anchorMax = Vector2.zero;
            if ((_large.offsetMin - offMin).sqrMagnitude > 0.0001f) _large.offsetMin = offMin;
            if ((_large.offsetMax - offMax).sqrMagnitude > 0.0001f) _large.offsetMax = offMax;
        }

        // The button in the header's aux slot (right end of the drawn plate), its glyph by state.
        private void PlaceMaxButton()
        {
            if (!_maxButton) return;
            var on = _header && _header.isActiveAndEnabled && _header.AuxLocal.width > 0f;
            if (_maxButton.gameObject.activeSelf != on) _maxButton.gameObject.SetActive(on);
            if (!on) return;
            var ht = (RectTransform)_header.transform;
            var aux = _header.AuxLocal;
            var s = Mathf.Min(aux.width, aux.height);
            var centre = _large.InverseTransformPoint(ht.TransformPoint(new Vector3(aux.xMax - s / 2f, aux.center.y, 0f)));
            var bt = (RectTransform)_maxButton.transform;
            var scale = ht.lossyScale.y / Mathf.Max(Mathf.Abs(_large.lossyScale.y), 1e-6f);
            var size = new Vector2(s, s) * scale;
            var half = new Vector2(0.5f, 0.5f);
            if (bt.anchorMin != half) bt.anchorMin = bt.anchorMax = half;
            if (bt.pivot != half) bt.pivot = half;
            var pos = (Vector2)centre - _large.rect.center;
            if ((bt.anchoredPosition - pos).sqrMagnitude > 0.0001f) bt.anchoredPosition = pos;
            if ((bt.sizeDelta - size).sqrMagnitude > 0.0001f) bt.sizeDelta = size;
            if (bt.GetSiblingIndex() != _large.childCount - 1) bt.SetAsLastSibling(); // over the header face, before nothing
            var role = _maxApplied ? "glyph.win.restore" : "glyph.win.max";
            if (role != _maxRole && _maxGlyph)
            {
                _maxRole = role;
                var mark = _maxGlyph.GetComponent<SkinMark>() ?? DollCell.Mark(_maxGlyph, role, "map window maximize / restore glyph");
                mark.Role = role;
                mark.Outcome = Restyler.Apply(_maxGlyph, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            }
        }
    }
}
