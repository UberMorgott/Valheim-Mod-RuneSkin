using System.Collections.Generic;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.InventoryScreen;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.InfoScreens
{
    // Family "Skills" (skills-a.png): the vanilla SkillsDialog (root/Skills/SkillsFrame) as a RuneSkin panel on the right
    // of layout A (same top / bottom as the columns): title between two knots, the info tab row (InfoTabs), a summary
    // well (death glyph + skill loss on death, total skill level, sort toggles by level / name), one scroll list of
    // skill rows - icon socket, name, progress-to-next-level bar + percent, big level number (+bonus). The row count
    // comes from Player.GetSkills().GetSkillList() (vanilla + every mod skill: SkillsDialog.Setup pools rows,
    // SkillsDialog.cs:116-121). Reposition, never rebuild: rows keep their children and names (Setup finds them by name,
    // :130-143). Sorting permutes the pooled rows after Setup (m_elements order = gamepad order = screen order).
    public sealed class SkillsSkin : MonoBehaviour
    {
        public const string ScreenId = "Skills";
        public static SkillsSkin Instance { get; private set; }

        // 0 = vanilla order, 1 = level (high first), 2 = name. Session-wide, like the list it orders.
        private static int _sort = 1;

        private InventoryGui _gui;
        private SkillsDialog _dialog;
        private RectTransform _root, _frame, _canvas;
        private Image _deathGlyph, _summaryWell, _knotL, _knotR;
        private TMP_Text _death;
        private InfoTabs _tabs;
        private readonly SortToggle[] _sorts = new SortToggle[2];
        private int _rowsSeen = -1;

        private sealed class SortToggle
        {
            public Image Back, Glyph;
            public int Mode;
        }

        public static void Attach(InventoryGui gui)
        {
            if (!gui || !gui.m_skillsDialog || gui.GetComponent<SkillsSkin>()) return;
            gui.gameObject.AddComponent<SkillsSkin>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            _dialog = gui.m_skillsDialog;
            _root = (RectTransform)_dialog.transform;
            _frame = _root.Find("SkillsFrame") as RectTransform ?? _root;
            var canvas = gui.GetComponentInParent<Canvas>();
            _canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            // Vanilla frame art directly under the frame (bkg, list backdrop): replaced by the panel stack.
            foreach (var c in Children.Of(_frame))
                if (c.name != "Closebutton" && c.TryGetComponent<Image>(out var b) && !c.GetComponent<Selectable>()) b.enabled = false;
            PanelDecor.Ensure(_frame, "size.panel.glass.inset", glass: true, plaque: false);
            _dialog.m_spacing = ThemeTokens.GetFloat("size.info.skill.row", 58f);
            _summaryWell = InfoArt.OwnImage(_frame, "RuneSkinSkillsSummary", "craft.well", "summary row well (frame_inset)");
            _summaryWell.preserveAspect = false;
            _deathGlyph = InfoArt.OwnImage(_frame, "RuneSkinSkillsGlyph", "info.glyph.death", "summary row marker (glyph_death)");
            _death = InfoArt.RowText(_frame, "RuneSkinSkillsDeath", "text.body", TextAlignmentOptions.MidlineLeft);
            _knotL = InfoArt.OwnImage(_frame, "RuneSkinSkillsKnotL", "ornament.knot", "title ornament (knot)");
            _knotR = InfoArt.OwnImage(_frame, "RuneSkinSkillsKnotR", "ornament.knot", "title ornament (knot)");
            _tabs = new InfoTabs(gui, _frame, "OnOpenSkills", () => _dialog.OnClose());
            for (var i = 0; i < _sorts.Length; i++) _sorts[i] = MakeSort(i + 1);
            ScreenRegistry.Register(_root, Context);
            ScreenRegistry.Restyle(_root);
            Plugin.Log.LogInfo("[RuneSkin] skills panel attached");
        }

        private SortToggle MakeSort(int mode)
        {
            var s = new SortToggle { Mode = mode };
            var go = new GameObject("RuneSkinSkillsSort" + mode, typeof(RectTransform));
            go.transform.SetParent(_frame, false);
            s.Back = go.AddComponent<Image>();
            var button = go.AddComponent<Button>();
            button.targetGraphic = s.Back;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => SetSort(mode));
            var mark = DollCell.Mark(s.Back, "info.tab", "skills sort toggle");
            mark.Outcome = Restyler.Apply(s.Back, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            s.Glyph = InfoArt.OwnImage(go.transform, "Glyph", mode == 1 ? "info.glyph.sort.level" : "info.glyph.sort.name", "sort glyph (glyph_sort_*)");
            return s;
        }

        // Click on the active toggle returns to the vanilla order; a click on the other switches to it.
        private void SetSort(int mode)
        {
            _sort = _sort == mode ? 0 : mode;
            if (Player.m_localPlayer && _root.gameObject.activeInHierarchy) _dialog.Setup(Player.m_localPlayer);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private ScreenContext Context()
        {
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Skills);
            foreach (var image in _root.GetComponentsInChildren<Image>(true))
            {
                var t = image.transform;
                if (image.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t.name == "bkg" && t.parent == _frame) ctx.Role(image, "skip", "vanilla skills frame (replaced by the panel stack)");
                else if (t.name == "darken" || t.name == "blur") ctx.Role(image, "skip", "vanilla modal dim / blur behind the dialog (kept)");
                else if (t.parent == _root && image.GetComponent<Button>()) ctx.Role(image, "skip", "full-screen click-outside closer (vanilla, transparent; a button sprite would paint the screen)");
                else if (t.name == "Skills" && t.parent == _frame) ctx.Role(image, "skip", "inactive list backdrop (InputFieldBackground), replaced by the panel stack");
                else if (_dialog.m_listRoot && t == _dialog.m_listRoot) ctx.Role(image, "skip", "list content hit area (vanilla, transparent; not a screen dim)");
                else if (InRow(t, out var row))
                {
                    if (t.name == "bkg" && t.parent == row) ctx.Role(image, "craft.row", "skill row strip (row_recipe_*)");
                    else if (t.name == "icon_bkg") ctx.Role(image, "slot.recess", "skill icon socket (slot_cell_recess)");
                    else if (t.parent && t.parent.name == "currentlevel" && t.name == "bkg" || t.name == "currentlevel" && !t.GetComponent<GuiBar>()) ctx.Role(image, "skill.trough", "progress groove (bar_skill_trough)");
                    else if (t.name == "bar" && t.parent && t.parent.name == "currentlevel") ctx.Role(image, "skill.fill", "progress fill (bar_skill_fill, rune_glow)");
                    else if (t.parent && (t.parent.name == "levelbar" || t.parent.name == "levelbar_total") || t.name == "levelbar" || t.name == "levelbar_total") ctx.Role(image, "skip", "total-level bars (hidden: one progress bar, skills-a)");
                    else ctx.Role(image, "content", "skill icon / row decor");
                }
            }
            foreach (var text in _root.GetComponentsInChildren<TMP_Text>(true))
            {
                var t = text.transform;
                if (text.GetComponent<SkinMark>() is SkinMark m && m.Internal) continue;
                if (t.name == "topic" && t.parent == _frame) ctx.Role(text, "info.title", "panel title (serif)");
                else if (t.name == "leveltext") ctx.Role(text, "skill.level", "skill level number (body font digits, text_active; Codex: readable lining numerals)");
                else if (InRow(t, out _) || t.parent == _frame) ctx.Role(text, "text.body", "skill row / summary text (body sans)");
            }
            return ctx;
        }

        private bool InRow(Transform t, out Transform row)
        {
            row = null;
            var list = _dialog.m_listRoot;
            var template = _dialog.m_elementPrefab ? _dialog.m_elementPrefab.transform : null;
            for (var p = t; p && p != _root; p = p.parent)
                if (p == template || list && p.parent == list)
                {
                    row = p;
                    return true;
                }
            return false;
        }

        // SkillsDialog.Setup postfix: rows exist and carry their values (row j = skill j); restyle new rows, write the
        // percent labels, then order the rows.
        public void AfterSetup(Player player)
        {
            var skills = player.GetSkills().GetSkillList();
            var rows = _dialog.m_elements;
            if (rows.Count != _rowsSeen)
            {
                _rowsSeen = rows.Count;
                ScreenRegistry.Restyle(_root);
            }
            var n = Mathf.Min(skills.Count, rows.Count);
            for (var i = 0; i < n; i++)
            {
                var pct = InfoArt.RowText(rows[i].transform, "RuneSkinSkillPercent", "text.body", TextAlignmentOptions.MidlineRight);
                pct.SetText("{0}%", Mathf.FloorToInt(skills[i].GetLevelPercentage() * 100f));
            }
            Sort(player, skills, n);
            // Summary: skill loss on death (Skills.OnDeath: m_DeathLowerFactor x world skill reduction rate) and the total.
            var loss = Mathf.RoundToInt(player.GetSkills().m_DeathLowerFactor * Game.m_skillReductionRate * 100f);
            _death.SetText("-{0}%", loss);
            foreach (var child in new[] { "totalskills_topic", "totalskills" })
            {
                var t = Utils.FindChild(_root, child);
                if (t && !t.gameObject.activeSelf && t.TryGetComponent<TMP_Text>(out var tt) && !string.IsNullOrEmpty(tt.text)) t.gameObject.SetActive(true);
            }
            Layout();
        }

        // Permutes rows 0..n-1 (row j holds skill j after Setup) into the sort order: list position, m_elements index
        // (gamepad selection walks it) and the tooltip offset (Setup derives it from the row position, :127).
        private void Sort(Player player, List<Skills.Skill> skills, int n)
        {
            if (_sort == 0 || n < 2) return;
            var rows = _dialog.m_elements;
            var names = new string[n];
            var levels = new float[n];
            var sk = player.GetSkills();
            for (var i = 0; i < n; i++)
            {
                var nt = Utils.FindChild(rows[i].transform, "name");
                names[i] = nt && nt.TryGetComponent<TMP_Text>(out var tt) ? tt.text : skills[i].m_info.m_skill.ToString();
                levels[i] = sk.GetSkillLevel(skills[i].m_info.m_skill);
            }
            var order = InfoMath.SkillOrder(levels, names, _sort, System.StringComparer.CurrentCulture);
            var sorted = new GameObject[n];
            for (var r = 0; r < n; r++) sorted[r] = rows[order[r]];
            for (var r = 0; r < n; r++)
            {
                rows[r] = sorted[r];
                var rt = (RectTransform)sorted[r].transform;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -r * _dialog.m_spacing);
                rt.SetSiblingIndex(r);
                var tip = sorted[r].GetComponentInChildren<UITooltip>();
                var skill = skills[order[r]];
                if (tip) tip.Set("", skill.m_info.m_description, _dialog.m_tooltipAnchor, new Vector2(0f, Mathf.Min(255f, rt.localPosition.y + 10f)));
            }
        }

        private void LateUpdate()
        {
            if (!_root.gameObject.activeInHierarchy) return;
            Layout();
        }

        private void Layout()
        {
            var inv = InventorySkin.Instance;
            if (!inv || !_canvas) return;
            var k = inv.SkinScaleValue;
            var box = InfoArt.RightPanel(inv, ThemeTokens.GetFloat("size.info.skills.w", 780f) * k);
            if (box.IsEmpty) return;
            Geo.SetScale(_frame, 1f);
            Geo.Place(_canvas, _frame, box);
            var pad = ThemeTokens.GetFloat("size.info.pad", 22f) * k;
            var inner = new Box(box.X + pad, box.Y + pad, box.W - 2f * pad, box.H - 2f * pad);
            var headerH = ThemeTokens.GetFloat("size.info.header", 64f) * k;
            var tabsH = ThemeTokens.GetFloat("size.info.tabs", 70f) * k;
            var summaryH = ThemeTokens.GetFloat("size.info.summary", 48f) * k;
            var gap = 8f * k;
            var y = inner.Y;
            // Title band = a drag band of the docked inventory group (this panel docks over its columns).
            var band = WindowMove.Handle(_frame, WindowMove.Inventory);
            if (band) Geo.Place(_canvas, band.rectTransform, new Box(inner.X, y, inner.W, headerH));
            foreach (var c in Children.Of(_frame))
            {
                if (!(c is RectTransform rt) || !c.gameObject.activeSelf) continue;
                if (c.name == "topic" && c.TryGetComponent<TMP_Text>(out var title))
                {
                    Geo.Place(_canvas, rt, new Box(inner.X, y, inner.W, headerH));
                    InfoArt.Label(title, TextAlignmentOptions.Center, headerH * 0.55f, 16f, true);
                    InfoArt.TitleKnots(_canvas, title, new Box(inner.X, y, inner.W, headerH), _knotL, _knotR);
                }
                else if (c.name == "Closebutton") Geo.SetScale(rt, 0f); // the selected tab closes (mockup: no close button)
            }
            y += headerH;
            _tabs.Layout(_canvas, new Box(inner.X, y, inner.W, tabsH), k);
            y += tabsH + gap;
            // Summary well: death glyph + loss, total skill level, sort toggles on the right.
            var well = new Box(inner.X, y, inner.W, summaryH);
            Geo.Place(_canvas, _summaryWell.rectTransform, well);
            var sh = summaryH * 0.72f;
            var sy = y + (summaryH - sh) / 2f;
            if (_deathGlyph) Geo.Place(_canvas, _deathGlyph.rectTransform, new Box(inner.X + 10f * k, sy, sh, sh));
            var tx = inner.X + 10f * k + sh + 10f * k;
            Geo.Place(_canvas, _death.rectTransform, new Box(tx, sy, inner.W * 0.25f, sh));
            InfoArt.Label(_death, TextAlignmentOptions.MidlineLeft, sh * 0.62f, 13f);
            var togglesW = 2f * sh + 8f * k;
            var right = inner.Right - 10f * k;
            for (var i = 0; i < _sorts.Length; i++)
            {
                var s = _sorts[i];
                var b = new Box(right - togglesW + i * (sh + 8f * k), sy, sh, sh);
                Geo.Place(_canvas, s.Back.rectTransform, b);
                Geo.Place(_canvas, s.Glyph.rectTransform, CraftingScreen.CraftingMath.Inset(b, sh * 0.2f, sh * 0.2f));
                var on = _sort == s.Mode;
                if (s.Back.GetComponent<SkinMark>() is SkinMark m && m.Role != (on ? "info.tab.selected" : "info.tab"))
                {
                    m.Role = on ? "info.tab.selected" : "info.tab";
                    m.Outcome = Restyler.Apply(s.Back, m, ThemeTokens.Role(m.Role)) ? Outcome.Styled : Outcome.Degraded;
                }
                var want = ThemeTokens.GetColor(on ? "color.text.active" : "color.text.body", Color.white);
                if (s.Glyph.color != want) s.Glyph.color = want;
            }
            var topicT = Utils.FindChild(_root, "totalskills_topic") as RectTransform;
            var valueT = Utils.FindChild(_root, "totalskills") as RectTransform;
            var valueRight = right - togglesW - 14f * k;
            if (valueT && valueT.TryGetComponent<TMP_Text>(out var tv))
            {
                Geo.Place(_canvas, valueT, new Box(valueRight - inner.W * 0.2f, sy, inner.W * 0.2f, sh));
                InfoArt.Label(tv, TextAlignmentOptions.MidlineRight, sh * 0.62f, 13f);
            }
            if (topicT && topicT.TryGetComponent<TMP_Text>(out var tt))
            {
                Geo.Place(_canvas, topicT, new Box(valueRight - inner.W * 0.45f, sy, inner.W * 0.25f - 6f * k, sh));
                InfoArt.Label(tt, TextAlignmentOptions.MidlineRight, sh * 0.5f, 12f);
            }
            y += summaryH + gap;
            // List: the scroll view (parent chain of m_listRoot up to the frame) fills the rest.
            var list = new Box(inner.X, y, inner.W, inner.Bottom - y);
            var scroll = _dialog.m_listRoot.GetComponentInParent<ScrollRect>(true);
            var view = scroll ? (RectTransform)scroll.transform : null;
            var bar = scroll && scroll.verticalScrollbar ? (RectTransform)scroll.verticalScrollbar.transform : null;
            var barW = 16f * k;
            if (view) Geo.Place(_canvas, view, new Box(list.X, list.Y, list.W - barW - 6f * k, list.H));
            if (bar) Geo.Place(_canvas, bar, new Box(list.Right - barW, list.Y, barW, list.H));
            var pitch = _dialog.m_spacing;
            var rowW = view ? view.rect.width : list.W;
            for (var i = 0; i < _dialog.m_elements.Count; i++)
            {
                var e = _dialog.m_elements[i];
                if (!e || !e.activeSelf) continue;
                var rt = (RectTransform)e.transform;
                Row(rt, rowW, pitch, k);
                // Texts wherever the prefab nests them (Setup finds them with Utils.FindChild): placed in canvas space.
                var rb = Geo.BoxOf(_canvas, rt);
                var levelW = 80f * k;
                var lt = Utils.FindChild(rt, "leveltext") as RectTransform;
                if (lt && lt.TryGetComponent<TMP_Text>(out var l))
                {
                    Geo.SetScale(lt, 1f);
                    Geo.Place(_canvas, lt, new Box(rb.Right - levelW - 18f * k, rb.Y, levelW, rb.H));
                    InfoArt.Label(l, TextAlignmentOptions.MidlineRight, rb.H * 0.74f, 16f);
                }
                var bt = Utils.FindChild(rt, "bonustext") as RectTransform;
                if (bt && bt.TryGetComponent<TMP_Text>(out var bo))
                {
                    Geo.SetScale(bt, 1f);
                    Geo.Place(_canvas, bt, new Box(rb.Right - levelW - 60f * k, rb.Y + rb.H * 0.1f, 48f * k, rb.H * 0.4f));
                    InfoArt.Label(bo, TextAlignmentOptions.MidlineRight, rb.H * 0.28f, 11f);
                }
                var pt = rt.Find("RuneSkinSkillPercent") as RectTransform;
                if (pt && pt.TryGetComponent<TMP_Text>(out var p))
                {
                    var barRight = rb.Right - levelW - 64f * k;
                    Geo.Place(_canvas, pt, new Box(barRight + 4f * k, rb.Y + rb.H * 0.5f, 56f * k, rb.H * 0.42f));
                    InfoArt.Label(p, TextAlignmentOptions.MidlineRight, rb.H * 0.27f, 11f);
                }
            }
        }

        // Row geometry in row space (anchors from the top-left): socket | name over the progress bar + percent | level.
        private void Row(RectTransform row, float w, float pitch, float k)
        {
            var h = pitch - 4f * k;
            row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(0f, 1f);
            if (Mathf.Abs(row.sizeDelta.x - w) > 0.5f || Mathf.Abs(row.sizeDelta.y - h) > 0.5f) row.sizeDelta = new Vector2(w, h);
            var socket = h - 6f * k;
            var levelW = 64f * k;
            var x0 = socket + 18f * k;
            var barRight = w - levelW - 64f * k;
            foreach (var c in Children.Of(row))
            {
                if (!(c is RectTransform rt)) continue;
                switch (c.name)
                {
                    case "bkg":
                        InfoArt.Rect(rt, 0f, 0f, w, h, h);
                        break;
                    case "icon_bkg":
                        InfoArt.Rect(rt, 6f * k, 3f * k, socket, socket, h);
                        var icon = rt.Find("icon") as RectTransform;
                        if (icon) { icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f); icon.sizeDelta = new Vector2(socket * 0.7f, socket * 0.7f); icon.anchoredPosition = Vector2.zero; }
                        break;
                    case "name":
                        InfoArt.Rect(rt, x0, 2f * k, barRight - x0, h * 0.5f, h);
                        if (c.TryGetComponent<TMP_Text>(out var n)) InfoArt.Label(n, TextAlignmentOptions.BottomLeft, h * 0.36f, 13f);
                        break;
                    case "currentlevel":
                        var bh = Mathf.Max(8f, h * 0.18f);
                        InfoArt.Rect(rt, x0, h * 0.62f, barRight - x0, bh, h);
                        if (c.TryGetComponent<GuiBar>(out var gb) && gb.m_bar)
                        {
                            var inset = 3f * k * bh / 18f;
                            gb.m_bar.anchorMin = gb.m_bar.anchorMax = new Vector2(0f, 0.5f);
                            gb.m_bar.pivot = new Vector2(0f, 0.5f);
                            gb.m_bar.anchoredPosition = new Vector2(inset, 0f);
                            gb.m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bh - 2f * inset);
                            gb.SetWidth(barRight - x0 - 2f * inset);
                            var back = rt.Find("bkg") as RectTransform;
                            if (back) { back.anchorMin = Vector2.zero; back.anchorMax = Vector2.one; back.offsetMin = back.offsetMax = Vector2.zero; }
                        }
                        break;
                    case "levelbar":
                    case "levelbar_total":
                        // One bar (skills-a): the total-level bars stop drawing; their objects stay (Setup writes them,
                        // and the level texts may live inside them).
                        foreach (var img in c.GetComponentsInChildren<Image>(true)) if (img.enabled) img.enabled = false;
                        break;
                    case "description":
                        if (c.gameObject.activeSelf) c.gameObject.SetActive(false); // the description is the row tooltip
                        break;
                }
            }
        }
    }
}
