using RuneSkin.InfoScreens;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // One tab of Epic Loot's enchanting table column (EnchantingUI prefab Panel/Tabs/<feature>, roles ForeignRoles.EnchantTab)
    // as a uniform cell (ForeignMath.ElTabLayout): icon centred at one size, caption under it on every tab, lock badge on
    // the icon's top-right corner.
    // Lock truth stays Epic Loot's: FeatureStatus.Refresh shows "Image/Locked" exactly while
    // EnchantingTable.IsFeatureLocked(feature) (level -1) and hides the caption "Text" (UnlockedContainer) then. The badge
    // IS that container (socket + padlock, roles badge.ench.lock / glyph.ench.lock), so it follows Epic Loot by
    // construction; the cell dims the icon and adds the unlock hint to the tab tooltip while it is shown. The caption is
    // RuneSkin's own text (Epic Loot's localized label copied), so locked tabs keep theirs. Journaled: Revert puts Epic
    // Loot's rects, label and icon alpha back.
    [DisallowMultipleComponent]
    public sealed class ElTabCell : MonoBehaviour, IRevertible
    {
        public const string CaptionName = "RuneSkinTabCaption";
        private const string HintKey = "$runeskin_el_tab_locked";

        private struct Saved
        {
            public RectTransform Rt;
            public Vector2 AnchorMin, AnchorMax, Pivot, Pos, Size;
            public static Saved Of(RectTransform rt) => new Saved { Rt = rt, AnchorMin = rt.anchorMin, AnchorMax = rt.anchorMax, Pivot = rt.pivot, Pos = rt.anchoredPosition, Size = rt.sizeDelta };
            public void Restore()
            {
                if (!Rt) return;
                Rt.anchorMin = AnchorMin; Rt.anchorMax = AnchorMax; Rt.pivot = Pivot; Rt.anchoredPosition = Pos; Rt.sizeDelta = Size;
            }
        }

        private RectTransform _cell;
        private Image _icon;
        private RectTransform _lock;
        private RectTransform _padlock;
        private TMP_Text _label;
        private TMP_Text _caption;
        private UITooltip _tooltip;
        private Saved _iconSaved, _lockSaved, _padlockSaved;
        private bool _labelWasEnabled;
        private string _hint;

        public static ElTabCell Ensure(Button button)
        {
            if (!button || !(button.transform is RectTransform rt)) return null;
            if (!button.TryGetComponent<ElTabCell>(out var cell))
            {
                cell = button.gameObject.AddComponent<ElTabCell>();
                cell.hideFlags = HideFlags.HideInInspector;
                cell.Bind(rt);
            }
            cell.Apply();
            return cell;
        }

        private void Bind(RectTransform cell)
        {
            _cell = cell;
            var icon = cell.Find("Image");
            _icon = icon ? icon.GetComponent<Image>() : null;
            _lock = icon ? icon.Find("Locked") as RectTransform : null;
            _padlock = _lock ? _lock.Find("LockIcon") as RectTransform : null;
            var label = cell.Find("Text");
            _label = label ? label.GetComponent<TMP_Text>() : null;
            _tooltip = cell.GetComponent<UITooltip>();
            if (_icon) _iconSaved = Saved.Of(_icon.rectTransform);
            if (_lock) _lockSaved = Saved.Of(_lock);
            if (_padlock) _padlockSaved = Saved.Of(_padlock);
            if (_label) _labelWasEnabled = _label.enabled;
        }

        public bool Locked => _lock && _lock.gameObject.activeSelf;

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (!_cell) return;
            var r = _cell.rect;
            ForeignMath.ElTabLayout(r.width, r.height, F("size.el.tab.icon", 28f), F("size.el.tab.gap", 3f), F("size.el.tab.caption.h", 40f),
                F("size.el.tab.pad", 4f), F("size.el.tab.badge", 18f), out var icon, out var caption, out var badge);
            if (_icon)
            {
                InfoArt.Rect(_icon.rectTransform, icon.X, icon.Y, icon.W, icon.H, r.height);
                if (!_icon.preserveAspect) _icon.preserveAspect = true;
                var alpha = Locked ? F("size.el.tab.locked.alpha", 0.45f) : 1f;
                if (Mathf.Abs(_icon.canvasRenderer.GetAlpha() - alpha) > 0.001f) _icon.canvasRenderer.SetAlpha(alpha);
            }
            if (_lock) InfoArt.Rect(_lock, badge.X - icon.X, badge.Y - icon.Y, badge.W, badge.H, icon.H);
            if (_padlock)
            {
                var inset = Mathf.Round(badge.W * 0.22f);
                InfoArt.Rect(_padlock, inset, inset, badge.W - 2f * inset, badge.H - 2f * inset, badge.H);
            }
            Caption(caption);
            Tooltip();
        }

        private void Caption(ForeignMath.Box box)
        {
            if (_label && _label.enabled) _label.enabled = false;
            if (!_caption)
            {
                _caption = InfoArt.RowText(_cell, CaptionName, "craft.tab.label", TextAlignmentOptions.Center);
                _caption.textWrappingMode = TextWrappingModes.Normal;
                _caption.overflowMode = TextOverflowModes.Ellipsis;
                _caption.maxVisibleLines = 2;
                if (_caption.TryGetComponent<LayoutElement>(out var le)) le.ignoreLayout = true;
                else _caption.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            var max = F("size.el.tab.caption", 16f);
            var min = Mathf.Min(max, F("size.el.tab.caption.min", 16f));
            if (!_caption.enableAutoSizing || Mathf.Abs(_caption.fontSizeMax - max) > 0.01f || Mathf.Abs(_caption.fontSizeMin - min) > 0.01f)
            {
                _caption.fontSizeMax = max;
                _caption.fontSizeMin = min;
                _caption.enableAutoSizing = true;
            }
            InfoArt.Rect(_caption.rectTransform, box.X, box.Y, box.W, box.H, 0f);
            var text = LabelText();
            if (_caption.text != text) _caption.text = text;
            if (!_caption.gameObject.activeSelf) _caption.gameObject.SetActive(true);
        }

        // Epic Loot's label (localized at EnchantingTableUI.Start, also while hidden); a raw key is localized here.
        private string LabelText()
        {
            var t = _label ? _label.text : null;
            if (string.IsNullOrEmpty(t)) return "";
            return t.StartsWith("$") && Localization.instance != null ? Localization.instance.Localize(t) : t;
        }

        // FeatureStatus.Refresh rewrites the tooltip (topic + level + description); the unlock hint rides on top while
        // the feature is locked.
        private void Tooltip()
        {
            if (!_tooltip) return;
            if (_hint == null || _hint.StartsWith("[")) _hint = Words.Get(HintKey);
            var text = _tooltip.m_text ?? "";
            var has = text.Contains(_hint);
            if (Locked && !has) _tooltip.m_text = text.TrimEnd() + "\n\n" + _hint;
            else if (!Locked && has) _tooltip.m_text = text.Replace("\n\n" + _hint, "").Replace(_hint, "");
        }

        private static float F(string token, float fallback) => ThemeTokens.GetFloat(token, fallback);

        public void Revert()
        {
            _iconSaved.Restore();
            _lockSaved.Restore();
            _padlockSaved.Restore();
            if (_icon) _icon.canvasRenderer.SetAlpha(1f);
            if (_label) _label.enabled = _labelWasEnabled;
            if (_tooltip && _hint != null && _tooltip.m_text != null) _tooltip.m_text = _tooltip.m_text.Replace("\n\n" + _hint, "");
            Destroy(this);
        }
    }

    // Epic Loot's tab column (Panel/Tabs: RectMask2D + VerticalLayoutGroup, padding right -10, aligned upper right, 137 px
    // plates in a 160 px mask): the plates ran 10 px under the mask's right edge (their frame was cut off). The column
    // centres its plates inside the mask at one size (size.el.tab.w x size.el.tab.h, ForeignMath.ElTabWidth), keeping
    // Epic Loot's spacing (equal pitch). Journaled.
    [DisallowMultipleComponent]
    public sealed class ElTabColumn : MonoBehaviour, IRevertible
    {
        private VerticalLayoutGroup _group;
        private RectOffset _padding;
        private TextAnchor _align;
        private readonly System.Collections.Generic.Dictionary<RectTransform, Vector2> _sizes = new System.Collections.Generic.Dictionary<RectTransform, Vector2>();
        private TabHandler _tabs;

        public static void Ensure(TabHandler tabs)
        {
            if (!tabs || !tabs.TryGetComponent<VerticalLayoutGroup>(out var group)) return;
            if (!tabs.TryGetComponent<ElTabColumn>(out var col))
            {
                col = tabs.gameObject.AddComponent<ElTabColumn>();
                col.hideFlags = HideFlags.HideInInspector;
                col._group = group;
                col._tabs = tabs;
                var p = group.padding;
                col._padding = new RectOffset(p.left, p.right, p.top, p.bottom);
                col._align = group.childAlignment;
            }
            col.Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (!_group || !_tabs) return;
            var mask = ((RectTransform)_group.transform).rect.width;
            var w = ForeignMath.ElTabWidth(ThemeTokens.GetFloat("size.el.tab.w", 136f), mask, ThemeTokens.GetFloat("size.el.tab.margin", 4f));
            var h = ThemeTokens.GetFloat("size.el.tab.h", 80f);
            var dirty = false;
            var p = _group.padding;
            if (p.left != 0 || p.right != 0 || p.top != _padding.top || p.bottom != _padding.bottom)
            {
                _group.padding = new RectOffset(0, 0, _padding.top, _padding.bottom);
                dirty = true;
            }
            if (_group.childAlignment != TextAnchor.UpperCenter) { _group.childAlignment = TextAnchor.UpperCenter; dirty = true; }
            foreach (var tab in _tabs.m_tabs)
            {
                if (tab == null || !tab.m_button || !(tab.m_button.transform is RectTransform rt)) continue;
                if (!_sizes.ContainsKey(rt)) _sizes[rt] = rt.sizeDelta;
                var size = new Vector2(w, h);
                if ((rt.sizeDelta - size).sqrMagnitude > 0.01f) { rt.sizeDelta = size; dirty = true; }
            }
            if (dirty) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_group.transform);
        }

        public void Revert()
        {
            if (_group && _padding != null)
            {
                _group.padding = _padding;
                _group.childAlignment = _align;
                foreach (var pair in _sizes)
                    if (pair.Key) pair.Key.sizeDelta = pair.Value;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_group.transform);
            }
            Destroy(this);
        }
    }
}
