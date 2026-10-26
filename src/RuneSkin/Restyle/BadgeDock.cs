using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // "New / unread" badges (vanilla exclamation_mark sprite: the changelog toggle's newsNotification, mod tabs reusing
    // it, e.g. a "Трейдинг" tab whose red "!" sat over its label): the badge sits centred on the top-right corner of its
    // button / tab (half outside, never over the label) at size.badge, on a small round theme plate (sprite.button.round)
    // drawn behind it. Placed every frame it is shown (layout groups move buttons); Revert restores the vanilla rect and
    // drops the plate.
    // Vanilla lays the badge out as a row child: showChangelog (start scene bundle 10ba9da1, StartGui/Menu/
    // BottomLeftButtons) is a HorizontalLayoutGroup of LeftKnot / Text / newsNotification / RightKnot, so the group
    // puts the "!" right after the label. Docked at the corner, the badge leaves that row (LayoutElement.ignoreLayout,
    // as vanilla's own m_changeLogNotice toggle never moves it): otherwise every layout rebuild (Canvas
    // willRenderCanvases, after this LateUpdate) put it back mid-button for a frame, a "!" blinking in the centre of
    // "Изменения" next to an empty corner plate (0.23.0 live report).
    [DisallowMultipleComponent]
    public sealed class BadgeDock : MonoBehaviour, IRevertible
    {
        private RectTransform _button;
        private TMP_Text[] _labels;
        private Image _plate;
        private bool _saved;
        private Vector2 _aMin, _aMax, _pivot, _pos, _size;
        private static readonly Vector3[] Corners = new Vector3[4];

        public static bool IsBadge(Image img) =>
            img && img.sprite && img.sprite.name.StartsWith("exclamation", System.StringComparison.Ordinal) && img.GetComponentInParent<Selectable>(true);

        public static void Ensure(Image img)
        {
            if (!IsBadge(img) || img.TryGetComponent<BadgeDock>(out _)) return;
            var d = img.gameObject.AddComponent<BadgeDock>();
            d.hideFlags = HideFlags.HideInInspector;
            d._button = (RectTransform)img.GetComponentInParent<Selectable>(true).transform;
            if (img.isActiveAndEnabled) d.Place();
        }

        private void OnEnable() => Place();

        private void LateUpdate() => Place();

        private void Place()
        {
            if (_reverted || !_button) return;
            var rt = (RectTransform)transform;
            if (!_saved)
            {
                _aMin = rt.anchorMin; _aMax = rt.anchorMax; _pivot = rt.pivot; _pos = rt.anchoredPosition; _size = rt.sizeDelta;
                _saved = true;
                OutOfLayout();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                var size = ThemeTokens.GetFloat("size.badge", 22f);
                rt.sizeDelta = new Vector2(size, size);
                if (TryGetComponent<Image>(out var img)) img.preserveAspect = true;
                PlateBehind(rt, size);
            }
            _button.GetWorldCorners(Corners);
            var corner = Corners[2]; // top-right
            corner = ClearOfLabels(corner, rt);
            if ((rt.position - corner).sqrMagnitude > 0.01f) rt.position = corner;
            if (_plate)
            {
                var pr = _plate.rectTransform;
                if (pr.position != rt.position) pr.position = rt.position;
                if (_plate.enabled != isActiveAndEnabled) _plate.enabled = isActiveAndEnabled;
            }
        }

        // A label running up to (or past) the button's right edge would sit under the corner badge (run 20261023-095522:
        // the changelog toggle's "!" over "Изменения"): the badge moves right along the corner row until it clears every
        // rendered label of its button.
        private Vector3 ClearOfLabels(Vector3 corner, RectTransform rt)
        {
            if (_labels == null) _labels = _button.GetComponentsInChildren<TMP_Text>(true);
            var half = rt.rect.width * 0.5f * rt.lossyScale.x;
            var x = corner.x;
            for (var i = 0; i < _labels.Length; i++)
            {
                var t = _labels[i];
                if (!t || !t.isActiveAndEnabled || t.transform.IsChildOf(transform) || string.IsNullOrEmpty(t.text)) continue;
                var b = t.textBounds;
                if (b.size.x <= 0.01f || b.size.x > 1e6f) continue;
                var min = t.transform.TransformPoint(b.min);
                var max = t.transform.TransformPoint(b.max);
                var overlapY = corner.y - half < Mathf.Max(min.y, max.y) && corner.y + half > Mathf.Min(min.y, max.y);
                var right = Mathf.Max(min.x, max.x);
                if (overlapY && x - half < right && x + half > Mathf.Min(min.x, max.x)) x = right + half + 2f * rt.lossyScale.x;
            }
            corner.x = x;
            return corner;
        }

        private LayoutElement _layout;
        private bool _layoutAdded, _layoutIgnored;

        private void OutOfLayout()
        {
            if (!TryGetComponent(out _layout))
            {
                _layout = gameObject.AddComponent<LayoutElement>();
                _layoutAdded = true;
            }
            _layoutIgnored = _layout.ignoreLayout;
            _layout.ignoreLayout = true;
            if (transform.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        private void PlateBehind(RectTransform rt, float size)
        {
            var sprite = ThemeTokens.GetSprite("sprite.button.round");
            if (!sprite || !rt.parent) return;
            var go = new GameObject("RuneSkin_BadgePlate", typeof(RectTransform));
            var pr = (RectTransform)go.transform;
            pr.SetParent(rt.parent, false);
            pr.SetSiblingIndex(rt.GetSiblingIndex()); // behind the glyph
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
            var k = ThemeTokens.GetFloat("share.badge.plate", 1.35f);
            pr.sizeDelta = new Vector2(size * k, size * k);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            _plate = go.AddComponent<Image>();
            _plate.sprite = sprite;
            _plate.preserveAspect = true;
            _plate.raycastTarget = false;
            var mark = go.AddComponent<SkinMark>();
            mark.Internal = true;
            mark.Role = "content";
            mark.Reason = "RuneSkin badge plate";
            mark.BoundInstance = go.GetInstanceID();
            mark.ThemeRevision = -1;
        }

        private void OnDisable()
        {
            if (_plate) _plate.enabled = false;
        }

        private bool _reverted;

        public void Revert()
        {
            _reverted = true;
            var rt = (RectTransform)transform;
            if (_saved)
            {
                rt.anchorMin = _aMin; rt.anchorMax = _aMax; rt.pivot = _pivot; rt.anchoredPosition = _pos; rt.sizeDelta = _size;
            }
            if (_layout)
            {
                if (_layoutAdded) Destroy(_layout);
                else _layout.ignoreLayout = _layoutIgnored;
            }
            if (_plate) Destroy(_plate.gameObject);
            Destroy(this);
        }
    }
}
