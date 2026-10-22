using System.Collections.Generic;
using RuneSkin.Classify;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // Character select as a scrolling list (mockup m5-char-a, Codex pick over the carousel): one row per profile of
    // FejdStartup.m_profiles (SaveSystem.GetAllPlayerProfiles, FejdStartup.cs:448) with an initial in a recessed socket,
    // the name and its save source (FileHelpers.GetSourceString, the data vanilla shows under the name). A row click
    // sets m_profileIndex and calls the vanilla UpdateCharacterList (:2711), exactly what the carousel arrows do
    // (OnCharacterLeft / Right :2879-2895), so the preview, start / remove buttons and save stay vanilla. The arrows
    // are hidden (the list replaces them), the rest of the panel (name, buttons, remove dialog) is vanilla restyled.
    public static class CharacterList
    {
        public const string PanelName = "RuneSkin_CharacterList";
        private static RectTransform _panel, _content;
        private static ScrollRect _scroll;
        private static TMP_Text _header;
        private static readonly List<Row> Rows = new List<Row>();

        private sealed class Row
        {
            public RectTransform Root;
            public Image Back, Selected, Socket;
            public TMP_Text Initial, Name, Sub;
            public Button Button;
            public int Index;
        }

        public static void Build(FejdStartup fejd)
        {
            var host = fejd.m_selectCharacterPanel ? fejd.m_selectCharacterPanel.transform : null;
            if (!host || host.Find(PanelName)) return;
            // A new FejdStartup (logout back to the main menu, Game.Logout -> start scene) gets a new list: the rows of
            // the previous scene are destroyed (run 20261022-055949: Sync touched them, NRE on every menu return).
            Rows.Clear();
            Pending.Clear();
            _panel = Rect(PanelName, host);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 0.5f);
            _panel.pivot = new Vector2(0f, 0.5f);
            _panel.anchoredPosition = new Vector2(ThemeTokens.GetFloat("size.charlist.margin", 70f), ThemeTokens.GetFloat("size.charlist.y", 30f));
            _panel.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.charlist.w", 520f), ThemeTokens.GetFloat("size.charlist.h", 640f));
            PanelDecor.Ensure(_panel, "size.panel.pad", glass: true, plaque: false);
            // Below the remove-character confirm (same parent): the list drew over the modal's left edge (Codex M5 r4).
            if (fejd.m_removeCharacterDialog && fejd.m_removeCharacterDialog.transform.parent == host)
                _panel.SetSiblingIndex(fejd.m_removeCharacterDialog.transform.GetSiblingIndex());
            _header = Text("Header", _panel, "font.display", "color.text.header", ThemeTokens.GetFloat("size.charlist.header", 28f), TextAlignmentOptions.Center);
            var hr = _header.rectTransform;
            hr.anchorMin = new Vector2(0f, 1f);
            hr.anchorMax = Vector2.one;
            hr.pivot = new Vector2(0.5f, 0.5f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.anchoredPosition = new Vector2(0f, -ThemeTokens.GetFloat("size.charlist.header.top", 6f));
            hr.sizeDelta = new Vector2(0f, 44f);
            _header.fontStyle |= FontStyles.UpperCase;

            var view = Rect("View", _panel);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            var pad = ThemeTokens.GetFloat("size.charlist.pad", 22f);
            view.offsetMin = new Vector2(pad, pad);
            view.offsetMax = new Vector2(-pad, -pad - 44f);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            _content = Rect("Content", view);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ThemeTokens.GetFloat("size.charlist.gap", 8f);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = _panel.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = view;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            // The list replaces the carousel arrows (still wired for gamepad shoulder navigation in vanilla Update).
            Hide(fejd.m_csLeftButton);
            Hide(fejd.m_csRightButton);
        }

        private static void Hide(Button b)
        {
            if (!b) return;
            if (!b.TryGetComponent<CanvasGroup>(out var g)) g = b.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            g.blocksRaycasts = false;
            g.interactable = false;
        }

        public static void Sync(FejdStartup fejd)
        {
            if (!_panel || !_panel.IsChildOf(fejd.transform)) return;
            var profiles = fejd.m_profiles;
            var n = profiles?.Count ?? 0;
            _header.text = Words.Get("$runeskin_characters");
            while (Rows.Count < n) Rows.Add(NewRow(fejd, Rows.Count));
            for (var i = 0; i < Rows.Count; i++)
            {
                var r = Rows[i];
                var on = i < n;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                if (!on) continue;
                var p = profiles[i];
                var name = p.GetName();
                r.Name.text = name;
                r.Initial.text = PreWorldMath.Initial(name);
                r.Sub.text = Localization.instance != null ? Localization.instance.Localize(FileHelpers.GetSourceString(p.m_fileSource)) : "";
                var sel = i == fejd.m_profileIndex;
                r.Selected.enabled = sel;
                r.Name.color = ThemeTokens.GetColor(sel ? "color.text.item.title" : "color.text.body", Color.white);
            }
            _panel.gameObject.SetActive(n > 0);
            // Height fits the rows (min 3, Codex M5 r1), capped by the theme height; more rows scroll.
            var row = ThemeTokens.GetFloat("size.charlist.row", 84f);
            var gap = ThemeTokens.GetFloat("size.charlist.gap", 8f);
            var pad = ThemeTokens.GetFloat("size.charlist.pad", 22f);
            var want = 2f * pad + 44f + Mathf.Max(3, n) * (row + gap);
            var h = Mathf.Min(want, ThemeTokens.GetFloat("size.charlist.h", 640f));
            if (Mathf.Abs(_panel.sizeDelta.y - h) > 0.5f) _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, h);
        }

        private static Row NewRow(FejdStartup fejd, int index)
        {
            var h = ThemeTokens.GetFloat("size.charlist.row", 84f);
            var r = new Row { Index = index, Root = Rect("Row", _content) };
            r.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
            r.Back = Graphic(r.Root, "Back", "craft.row");
            Stretch(r.Back.rectTransform, 0f);
            r.Selected = Graphic(r.Root, "Selected", "craft.row.selected");
            Stretch(r.Selected.rectTransform, 0f);
            r.Selected.raycastTarget = false;
            var cell = h - 16f;
            r.Socket = Graphic(r.Root, "Socket", "slot.recess");
            r.Socket.raycastTarget = false;
            var sr = r.Socket.rectTransform;
            sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.anchoredPosition = new Vector2(10f, 0f);
            sr.sizeDelta = new Vector2(cell, cell);
            r.Initial = Text("Initial", r.Socket.transform, "font.display", "color.text.active", cell * 0.5f, TextAlignmentOptions.Center);
            Stretch(r.Initial.rectTransform, 0f);
            r.Name = Text("Name", r.Root, "font.display", "color.text.body", ThemeTokens.GetFloat("size.charlist.name", 27f), TextAlignmentOptions.BottomLeft);
            var nr = r.Name.rectTransform;
            nr.anchorMin = new Vector2(0f, 0.5f);
            nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(cell + 26f, 0f);
            nr.offsetMax = new Vector2(-12f, -6f);
            r.Name.overflowMode = TextOverflowModes.Overflow; // Ellipsis hid the whole name when the line box was short
            r.Sub = Text("Sub", r.Root, "font.body", "color.state.disabled", ThemeTokens.GetFloat("size.charlist.sub", 19f), TextAlignmentOptions.TopLeft);
            var su = r.Sub.rectTransform;
            su.anchorMin = Vector2.zero;
            su.anchorMax = new Vector2(1f, 0.5f);
            su.offsetMin = new Vector2(cell + 26f, 8f);
            su.offsetMax = new Vector2(-12f, -2f);
            r.Sub.overflowMode = TextOverflowModes.Overflow;
            r.Button = r.Root.gameObject.AddComponent<Button>();
            r.Button.targetGraphic = r.Back;
            r.Button.onClick.AddListener(() =>
            {
                if (!fejd || fejd.m_profiles == null || r.Index >= fejd.m_profiles.Count) return;
                fejd.m_profileIndex = r.Index;
                fejd.UpdateCharacterList();
            });
            ApplyRoles(r.Root);
            return r;
        }

        // Classification of the list's own graphics (internal marks applied directly; the root pass keeps them).
        public static void Roles(ScreenContext ctx)
        {
            if (_panel) ctx.Exclude(_panel.Find("View"), "RuneSkin character list rows (own graphics)");
            if (_header) ctx.Role(_header, "text.header", "character list header");
        }

        private static readonly Dictionary<Graphic, string> Pending = new Dictionary<Graphic, string>();

        private static Image Graphic(Transform parent, string name, string role)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            Pending[img] = role;
            return img;
        }

        private static void ApplyRoles(Transform root)
        {
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
            {
                if (!Pending.TryGetValue(g, out var role)) continue;
                Pending.Remove(g);
                var mark = g.gameObject.AddComponent<SkinMark>();
                mark.Internal = true;
                mark.Role = role;
                mark.Reason = "RuneSkin character list";
                mark.BoundInstance = g.gameObject.GetInstanceID();
                mark.hideFlags = HideFlags.HideInInspector;
                mark.Outcome = Restyler.Apply(g, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            }
        }

        private static TMP_Text Text(string name, Transform parent, string font, string color, float size, TextAlignmentOptions align)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = align;
            t.fontSize = size;
            var f = ThemeTokens.GetFont(font);
            if (f) t.font = f;
            t.color = ThemeTokens.GetColor(color, Color.white);
            return t;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
