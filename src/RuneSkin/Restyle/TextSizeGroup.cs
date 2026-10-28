using System.Collections.Generic;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;

namespace RuneSkin.Restyle
{
    // One font size per group of sibling labels (batch fix 0.14.2): auto-sized labels of a group each shrink to their own
    // box, so one long label ("Управление сохранениями" in the world-select button column, "Количество ресурсов" in the
    // modifier rows) read much smaller than its siblings. The group's size is the smallest fitted size of its members:
    // each member's auto-size maximum drops to it (every member still fits, all read the same). Groups: plated buttons
    // under one parent with the same box size (PlateLabelFit joins them), or a screen's explicit key. A member's text or
    // box change restores the original maxima for a re-measure. Processed every 10 frames by the first member that runs.
    [DisallowMultipleComponent]
    public sealed class TextSizeGroup : MonoBehaviour, IRevertible
    {
        private static readonly Dictionary<object, List<TextSizeGroup>> Groups = new Dictionary<object, List<TextSizeGroup>>();
        private static readonly Dictionary<object, int> Next = new Dictionary<object, int>();
        private static readonly List<TextSizeGroup> Bucket = new List<TextSizeGroup>(8);

        private object _key;
        private RectTransform _box;
        private TMP_Text _text;
        private float _origMax;
        private bool _has;
        private string _seen;
        private Vector2 _seenSize;

        // Original auto-size maximum (the label's full size before the group lowered it).
        public float Full => _has ? _origMax : _text ? _text.fontSizeMax : 0f;

        public static float FullOf(TMP_Text text) =>
            text && text.TryGetComponent<TextSizeGroup>(out var g) && g._has ? g._origMax : text ? text.fontSizeMax : 0f;

        // box: the rect whose size decides the bucket (the button of a plate label); null = one bucket per key.
        public static void Join(TMP_Text text, object key, RectTransform box)
        {
            if (!text || key == null) return;
            if (!text.TryGetComponent<TextSizeGroup>(out var g))
            {
                g = text.gameObject.AddComponent<TextSizeGroup>();
                g.hideFlags = HideFlags.HideInInspector;
            }
            g._text = text;
            g._box = box;
            if (!g._has)
            {


                g._origMax = text.enableAutoSizing ? Mathf.Max(text.fontSizeMax, text.fontSize) : text.fontSize;
                g._has = true;
            }
            if (Equals(g._key, key)) return;
            g.Leave();
            g._key = key;
            if (!Groups.TryGetValue(key, out var list))
            {
                Prune();
                Groups[key] = list = new List<TextSizeGroup>(4);
            }
            list.Add(g);
        }

        // Members on objects that never ran (inactive templates, a logged-out GUI destroyed whole) get no OnDestroy, so their
        // groups stayed (run 20261028-200801 rp-relog: Groups 579 -> 1155 over 5 relogs, 278 under dead keys). Whenever the
        // table doubled since the last prune: dead members out, groups under a destroyed key or without members dropped.
        private static int _pruneAt = Diagnostics.Bounded.MinPrune;
        private static readonly List<object> DeadKeys = new List<object>();

        private static void Prune()
        {
            if (Groups.Count < _pruneAt) return;
            foreach (var pair in Groups)
            {
                pair.Value.RemoveAll(m => !m);
                if (pair.Value.Count == 0 || (pair.Key is Object o && !o)) DeadKeys.Add(pair.Key);
            }
            foreach (var key in DeadKeys)
            {
                Groups.Remove(key);
                Next.Remove(key);
            }
            DeadKeys.Clear();
            foreach (var key in new List<object>(Next.Keys))
                if (!Groups.ContainsKey(key)) Next.Remove(key);
            _pruneAt = System.Math.Max(Diagnostics.Bounded.MinPrune, Groups.Count * 2);
        }

        private void Leave()
        {
            if (_key == null || !Groups.TryGetValue(_key, out var list)) return;
            list.Remove(this);
            if (list.Count == 0)
            {
                Groups.Remove(_key);
                Next.Remove(_key);
            }
            _key = null;
        }

        private void OnDestroy() => Leave();

        private void LateUpdate()
        {
            if (_reverted || _key == null || !Groups.TryGetValue(_key, out var list) || list.Count < 2) return;
            Next.TryGetValue(_key, out var next);
            if (Time.frameCount < next) return;
            Next[_key] = Time.frameCount + 10;
            Process(list);
        }

        private static void Process(List<TextSizeGroup> list)
        {
            var changed = false;
            foreach (var m in list)
            {
                if (!m._text) continue;
                var size = m.BoxSize();
                if (!ReferenceEquals(m._seen, m._text.text) || (size - m._seenSize).sqrMagnitude > 0.25f)
                {
                    m._seen = m._text.text;
                    m._seenSize = size;
                    changed = true;
                }
            }
            if (changed)
            {
                // Re-measure: every member back to its own full size; TMP refits on its next mesh update.
                foreach (var m in list) if (m._text) m._text.fontSizeMax = m._origMax;
                return;
            }
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (!a.Shown() || a._done) continue;
                Bucket.Clear();
                for (var j = i; j < list.Count; j++)
                {
                    var b = list[j];
                    if (!b.Shown() || b._done) continue;
                    if (a._box && b._box && (a.BoxSize() - b.BoxSize()).sqrMagnitude > 16f) continue;
                    Bucket.Add(b);
                    b._done = true;
                }
                if (Bucket.Count >= 2) Equalize(Bucket);
            }
            foreach (var m in list) m._done = false;
            Bucket.Clear();
        }

        private bool _done;

        private bool Shown() => _text && _text.isActiveAndEnabled && !string.IsNullOrEmpty(_text.text);

        private Vector2 BoxSize() => _box ? _box.rect.size : _text ? _text.rectTransform.rect.size : Vector2.zero;

        private static void Equalize(List<TextSizeGroup> bucket)
        {
            var min = float.MaxValue;
            foreach (var m in bucket) min = Mathf.Min(min, m._text.fontSize);
            if (!(min > 1f) || min == float.MaxValue) return;
            // Readability floor (run 20261022-205726: the modifier labels equalised down to 10.7 px): never below
            // size.text.group.floor, nor above a member's own full size; a label that does not fit there runs over its
            // box (or ends in its ellipsis) instead of shrinking the whole group.
            // Explicit screen groups only (string keys): plated button groups floored at 16 truncated their labels
            // ("Управление сох", run 20261022-210451); their plate fit already shrinks / wraps to fit.
            if (bucket[0]._key is string) min = Mathf.Max(min, ThemeTokens.GetFloat("size.text.group.floor", 16f));
            foreach (var m in bucket)
            {
                var t = m._text;
                // Explicit groups: the floor wins over a smaller vanilla size (modifier checkbox labels were 14, run
                // 20261022-230744); plate groups never grow past a label's own full size.
                var size = m._key is string ? min : Mathf.Min(min, m._origMax);
                if (!t.enableAutoSizing)
                {
                    t.fontSizeMin = Mathf.Min(t.fontSize, t.fontSizeMin);
                    t.enableAutoSizing = true;
                }
                if (Mathf.Abs(t.fontSizeMin - size) > 0.05f) t.fontSizeMin = size;
                if (Mathf.Abs(t.fontSizeMax - size) > 0.05f) t.fontSizeMax = size;
            }
        }

        private bool _reverted;

        public void Revert()
        {
            _reverted = true;
            // Only the maximum is the group's write (auto-size / minimum belong to the label fit that reverts itself).
            if (_text && _has && _text.fontSizeMax < _origMax) _text.fontSizeMax = _origMax;
            Leave();
            Destroy(this);
        }
    }
}
