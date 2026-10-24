using System.Collections.Generic;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.PreWorld
{
    // Pre-world row spacing (0.20.3 track F). Vanilla laid these rows out for its own sizes; RuneSkin's taller tab plates
    // and readable-floor texts made them collide:
    // - NewWorldHint: the new-world dialog's "Минимум 5 символов" (panel/name_tip, vanilla top-anchored 1.4 units into
    //   the name field, 5 units over the seed row) gets its own row under the name field (size.menu.row.gap), left on the
    //   field's text start; the rows under it and the dialog's bottom edge move down by what that row needs.
    // - HeaderBand: a tab row over a separator over the content (start game: mode tabs "Начать игру / Присоединиться",
    //   Panel/"bkg (1)", the sub-panel titles "ВЫБОР МИРА"; servers tab: filter tabs, JoinPanel/bkg, the server list).
    //   The 44-unit RuneSkin tab plates grown about their centre reached 7 units over the separator; the separator now
    //   sits size.menu.row.gap under the tab row and the content size.menu.row.gap under the separator.
    // Both lay out from the vanilla rects they journaled (every pass restores them first, so a pass never stacks on the
    // last one) and only when what they measure changed (text, font size, sizes): idempotent, no per-frame writes.
    internal sealed class RectJournal
    {
        private readonly Dictionary<RectTransform, (Vector2 pos, Vector2 size)> _orig = new Dictionary<RectTransform, (Vector2, Vector2)>();

        public void Keep(RectTransform rt)
        {
            if (rt && !_orig.ContainsKey(rt)) _orig[rt] = (rt.anchoredPosition, rt.sizeDelta);
        }

        public void Restore()
        {
            foreach (var kv in _orig)
            {
                if (!kv.Key) continue;
                kv.Key.anchoredPosition = kv.Value.pos;
                kv.Key.sizeDelta = kv.Value.size;
            }
        }
    }

    internal static class RowOps
    {
        // Rect of rt in space (local min / max).
        public static Rect In(RectTransform rt, Transform space) => TextInset.RectIn(rt, rt.rect, space);

        // Moves rt by dy units of its parent's space (rt's parent = space for the direct children used here).
        public static void MoveY(RectTransform rt, float dy) => rt.anchoredPosition += new Vector2(0f, dy);

        // Grows host downward by d (top edge kept) while every direct child with a fixed vertical anchor keeps its place
        // on screen (stretched children - the vanilla backdrop carrying the RuneSkin panel stack - follow the host).
        public static void GrowDown(RectTransform host, float d, RectJournal journal)
        {
            if (!(d > 0.05f) || !Mathf.Approximately(host.anchorMin.y, host.anchorMax.y)) return;
            var keep = new List<(RectTransform rt, Vector3 pos)>();
            foreach (Transform c in host)
                if (c is RectTransform rt && Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
                {
                    journal.Keep(rt);
                    keep.Add((rt, rt.position));
                }
            journal.Keep(host);
            host.sizeDelta += new Vector2(0f, d);
            host.anchoredPosition -= new Vector2(0f, d * (1f - host.pivot.y));
            foreach (var (rt, pos) in keep) rt.position = pos;
        }

        // Visual top of a text's line(s) in space: the rect centre plus half the larger of rect and rendered height.
        public static float TextTop(TMP_Text t, Transform space)
        {
            var r = In(t.rectTransform, space);
            var k = Mathf.Abs(space.InverseTransformVector(t.rectTransform.TransformVector(Vector3.up)).y);
            var h = string.IsNullOrEmpty(t.text) ? 0f : t.GetPreferredValues(t.text, t.rectTransform.rect.width, 0f).y * (k > 0.0001f ? k : 1f);
            return r.center.y + Mathf.Max(r.height, h) * 0.5f;
        }
    }

    [DisallowMultipleComponent]
    public sealed class NewWorldHint : MonoBehaviour, IRevertible
    {
        private RectTransform _panel, _field;
        private TMP_Text _tip;
        private readonly RectJournal _journal = new RectJournal();
        private bool _hasAlign;
        private HorizontalAlignmentOptions _origAlign;
        private string _seenText;
        private float _seenSize = -1f;
        private Vector2 _seenField;

        public static void Ensure(FejdStartup fejd)
        {
            var panel = fejd && fejd.m_createWorldPanel ? fejd.m_createWorldPanel.transform.Find("panel") as RectTransform : null;
            var tip = panel ? panel.Find("name_tip") : null;
            if (!panel || !tip || !fejd.m_newWorldName || panel.GetComponent<NewWorldHint>()) return;
            var h = panel.gameObject.AddComponent<NewWorldHint>();
            h.hideFlags = HideFlags.HideInInspector;
            h._panel = panel;
            h._field = (RectTransform)fejd.m_newWorldName.transform;
            h._tip = tip.GetComponent<TMP_Text>();
        }

        private void LateUpdate()
        {
            if (!_tip || !_field || !_tip.isActiveAndEnabled) return;
            var size = _field.rect.size;
            if (_seenText == _tip.text && Mathf.Approximately(_seenSize, _tip.fontSize) && (size - _seenField).sqrMagnitude < 0.01f) return;
            _seenText = _tip.text;
            _seenSize = _tip.fontSize;
            _seenField = size;
            Layout();
        }

        public void Layout()
        {
            _journal.Restore();
            var p = _panel;
            var tr = _tip.rectTransform;
            var field = RowOps.In(_field, p);
            var gap = ThemeTokens.GetFloat("size.menu.row.gap", 8f);
            var pref = _tip.GetPreferredValues(_tip.text ?? "", float.PositiveInfinity, float.PositiveInfinity);
            var tipH = Mathf.Max(RowOps.In(tr, p).height, pref.y);
            // The next row: the highest top among the dialog's rows whose centre lies under the field (the seed label and
            // field, the buttons), not the field's own label beside it.
            var below = new List<RectTransform>();
            var nextTop = float.MinValue;
            foreach (Transform c in p)
            {
                if (!(c is RectTransform rt) || rt == tr || rt == _field || !rt.gameObject.activeSelf) continue;
                if (!Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) continue; // stretched backdrop / stack
                if (rt.GetComponent<PanelCaps>() || rt.GetComponent<PanelDecor>()) continue;
                var r = RowOps.In(rt, p);
                if (r.center.y >= field.yMin) continue;
                below.Add(rt);
                nextTop = Mathf.Max(nextTop, r.yMax);
            }
            var (hintTop, shift) = PreWorldMath.RowBelow(field.yMin, tipH, gap, nextTop == float.MinValue ? field.yMin - 1e6f : nextTop);
            // The hint: own row, starting at the field's text start, wide enough for its line.
            _journal.Keep(tr);
            var w = Mathf.Max(tr.rect.width, pref.x + 2f);
            tr.sizeDelta = new Vector2(tr.sizeDelta.x + (w - tr.rect.width), tr.sizeDelta.y + Mathf.Max(0f, tipH - tr.rect.height));
            var now = RowOps.In(tr, p);
            var left = field.xMin + ThemeTokens.GetFloat("size.field.inset", 12f);
            tr.anchoredPosition += new Vector2(left - now.xMin, hintTop - now.yMax);
            if (!_hasAlign) { _origAlign = _tip.horizontalAlignment; _hasAlign = true; }
            _tip.horizontalAlignment = HorizontalAlignmentOptions.Left;
            if (shift > 0.05f)
            {
                RowOps.GrowDown(p, shift, _journal);
                foreach (var rt in below)
                {
                    _journal.Keep(rt);
                    RowOps.MoveY(rt, -shift);
                }
            }
        }

        public void Revert()
        {
            _journal.Restore();
            if (_hasAlign && _tip && _tip.horizontalAlignment == HorizontalAlignmentOptions.Left) _tip.horizontalAlignment = _origAlign;
            Destroy(this);
        }
    }

    // Tab row / separator / content of one panel. Content either moves down (sub-panels under the title; the host grows
    // downward by the same amount, Grow) or gives up its top (a list viewport whose bottom row stays, Shrink).
    [DisallowMultipleComponent]
    public sealed class HeaderBand : MonoBehaviour, IRevertible
    {
        private RectTransform _host, _divider;
        private System.Action<List<RectTransform>> _tabSource;
        private readonly List<RectTransform> _tabs = new List<RectTransform>(8);
        private TMP_Text[] _titles;
        private RectTransform[] _movers, _shrink;
        private readonly RectJournal _journal = new RectJournal();
        private int _seen;
        private bool _hasSeen;

        // host: the panel whose children are tabs, divider and movers / shrink; titles: texts whose visual top is the
        // content top (else the movers' / shrink rects' tops).
        public static HeaderBand Ensure(RectTransform host, RectTransform divider, RectTransform[] tabs,
            TMP_Text[] titles, RectTransform[] movers, RectTransform[] shrink) =>
            tabs == null ? null : Ensure(host, divider, list => list.AddRange(tabs), titles, movers, shrink);

        // tabs: fills the list with the tab row's current buttons, read every LateUpdate. The server filter tabs are not
        // fixed objects: ServerListGui.RecreateTabs (OnEnable, ServerListGui.cs:156-170 / 313-371) destroys and
        // re-instantiates them every time the join tab shows, and before the first show the list is empty (Bands runs
        // at menu attach). A tab array captured then left the filter band without tabs: never laid out, the plates
        // 3 units into JoinPanel's separator and the server list touching it (0.21.1 menufix run 20261024-194221).
        public static HeaderBand Ensure(RectTransform host, RectTransform divider, System.Action<List<RectTransform>> tabs,
            TMP_Text[] titles, RectTransform[] movers, RectTransform[] shrink)
        {
            if (!host || !divider || tabs == null) return null;
            if (!host.TryGetComponent<HeaderBand>(out var b))
            {
                b = host.gameObject.AddComponent<HeaderBand>();
                b.hideFlags = HideFlags.HideInInspector;
            }
            b._host = host;
            b._divider = divider;
            b._tabSource = tabs;
            b._titles = titles ?? new TMP_Text[0];
            b._movers = movers ?? new RectTransform[0];
            b._shrink = shrink ?? new RectTransform[0];
            b._hasSeen = false;
            return b;
        }

        // A separator under a tab row: the widest short Image directly under host (sprite panel_separator in vanilla).
        public static RectTransform FindDivider(Transform host)
        {
            RectTransform best = null;
            foreach (Transform c in host)
            {
                if (!(c is RectTransform rt) || !c.TryGetComponent<UnityEngine.UI.Image>(out _)) continue;
                var r = rt.rect;
                if (r.height > 12f || r.width < 200f) continue;
                if (!best || r.width > best.rect.width) best = rt;
            }
            return best;
        }

        private void LateUpdate()
        {
            // Signature: which tabs, their heights + the content's measured top (tab plates / title sizes may change late,
            // the filter tabs are re-instantiated on every join-tab show).
            ReadTabs();
            var sig = _tabs.Count;
            foreach (var t in _tabs)
                if (t && t.gameObject.activeInHierarchy) sig = unchecked(sig * 31 + t.GetInstanceID() * 7 + Mathf.RoundToInt(t.rect.height * 100f));
            foreach (var t in _titles)
                if (t && t.gameObject.activeInHierarchy) sig = unchecked(sig * 31 + Mathf.RoundToInt(t.fontSize * 100f) + (t.text?.Length ?? 0));
            if (_hasSeen && sig == _seen) return;
            _hasSeen = true;
            _seen = sig;
            Layout();
        }

        private void ReadTabs()
        {
            _tabs.Clear();
            _tabSource?.Invoke(_tabs);
        }

        public void Layout()
        {
            ReadTabs();
            _journal.Restore();
            var host = _host;
            var gap = ThemeTokens.GetFloat("size.menu.row.gap", 8f);
            var tabsBottom = float.MaxValue;
            foreach (var t in _tabs)
                if (t && t.gameObject.activeInHierarchy) tabsBottom = Mathf.Min(tabsBottom, RowOps.In(t, host).yMin);
            if (tabsBottom == float.MaxValue) return;
            var div = RowOps.In(_divider, host);
            var top = float.MinValue;
            foreach (var t in _titles)
                if (t && t.gameObject.activeInHierarchy) top = Mathf.Max(top, RowOps.TextTop(t, host));
            if (top == float.MinValue)
                foreach (var rt in _shrink.Length > 0 ? _shrink : _movers)
                    if (rt && rt.gameObject.activeInHierarchy) top = Mathf.Max(top, RowOps.In(rt, host).yMax);
            if (top == float.MinValue) top = div.yMin - gap;
            var (d, b) = PreWorldMath.HeaderBand(tabsBottom, div.yMax, div.height, top, gap);
            if (d > 0.05f)
            {
                _journal.Keep(_divider);
                RowOps.MoveY(_divider, -d);
            }
            if (!(b > 0.05f)) return;
            if (_movers.Length > 0)
            {
                RowOps.GrowDown(host, b, _journal);
                foreach (var m in _movers)
                {
                    if (!m) continue;
                    _journal.Keep(m);
                    RowOps.MoveY(m, -b);
                }
            }
            foreach (var s in _shrink)
            {
                if (!s) continue;
                _journal.Keep(s);
                // Top edge down by b, bottom edge kept (offsetMin untouched), fixed or stretched vertical anchors alike.
                s.offsetMax -= new Vector2(0f, b);
            }
        }

        public void Revert()
        {
            _journal.Restore();
            Destroy(this);
        }
    }
}
