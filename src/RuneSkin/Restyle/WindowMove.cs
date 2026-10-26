using System;
using System.Collections.Generic;
using System.IO;
using RuneSkin.InventoryScreen;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    // Movable windows (user call 2026-10-23): every RuneSkin window carries one RuneWindowHeader and is dragged by it
    // (WindowDrag, never the whole window: item drag in the grids stays vanilla). A window's place = its layout default +
    // a user offset (canvas units, y down), clamped fully on screen above the bottom HUD strip (WindowMoveMath.Clamp: a
    // default already past the bounds is never moved, so no saved offset = the default layout). Every window moves on
    // its own: the inventory screen's player grid, doll, chest, backpack and crafting (mockup inv-layout-A, each its own
    // id), skills / trophies / achievements, stats / compendium / map legend / Almanac / mod windows. Offsets
    // persist per window id + screen resolution in BepInEx\cache\RuneSkin\window-positions.txt; a double-click on the
    // header resets that window, the Layout.ResetLayout action in ConfigurationManager resets all. Owners apply the
    // offset in their own layout pass (WindowMove.Place / Applied on the default box); windows vanilla or another mod
    // positions (compendium, map legend, mod panels) get a MovableWindow that adds it after their writes.
    public static class WindowMove
    {
        // The inventory screen's windows (mockup inv-layout-A): player grid, doll / character, chest, backpack, crafting.
        public const string InventoryGrid = "inventory.grid";
        public const string Doll = "inventory.doll";
        public const string Chest = "inventory.chest";
        public const string Backpack = "inventory.backpack";
        public const string Crafting = "crafting";

        // Every window of the inventory screen (the reset-layout action puts them all back on their defaults).
        public static readonly string[] InventoryWindows = { InventoryGrid, Doll, Chest, Backpack, Crafting, Skills, Trophies, Achievements };
        public const string Skills = "skills";
        public const string Trophies = "trophies";
        public const string Achievements = "achievements";
        public const string Stats = "stats";
        public const string Compendium = "compendium";
        public const string Map = "map";

        private static Dictionary<string, (float X, float Y)> _store;
        private static readonly Dictionary<string, Box> Defaults = new Dictionary<string, Box>();
        private static readonly Dictionary<string, RectTransform> Canvases = new Dictionary<string, RectTransform>();
        private static readonly Dictionary<string, Action> Listeners = new Dictionary<string, Action>();
        private static readonly List<Action> Scratch = new List<Action>();

        // Bumped on every offset change (drag, reset, load): owners that cache layouts compare it.
        public static int Revision { get; private set; }

        public static bool Enabled => Plugin.MovableWindows == null || Plugin.MovableWindows.Value;

        public static string FilePath => Path.Combine(BepInEx.Paths.CachePath, "RuneSkin", "window-positions.txt");

        private static string Key(string id) => WindowMoveMath.Key(id, Screen.width, Screen.height);

        private static Dictionary<string, (float X, float Y)> Store
        {
            get
            {
                if (_store == null) Load();
                return _store;
            }
        }

        // Stored offset of the window at this resolution (unclamped; zero when none or moving is off).
        public static Vector2 Offset(string id) =>
            Enabled && Store.TryGetValue(Key(id), out var v) ? new Vector2(v.X, v.Y) : Vector2.zero;

        // The window's box: its default moved by the stored offset, clamped to the bounds of `canvas`. Records the
        // default so a drag can clamp against it.
        public static Box Place(string id, Box def, RectTransform canvas)
        {
            var o = Applied(id, def, canvas);
            return WindowMoveMath.Shift(def, o.x, o.y);
        }

        // The clamped offset Place applies (canvas units, y down).
        public static Vector2 Applied(string id, Box def, RectTransform canvas)
        {
            Defaults[id] = def;
            if (canvas) Canvases[id] = canvas;
            var o = Offset(id);
            if (o != Vector2.zero && !def.IsEmpty && canvas)
            {
                float dx = o.x, dy = o.y;
                WindowMoveMath.Clamp(def, Bounds(canvas), ref dx, ref dy);
                o = new Vector2(dx, dy);
            }
            else o = Vector2.zero;
            WindowMoveProbe.Note(id, o);
            return o;
        }

        // Canvas bounds: the whole canvas above the RuneSkin HUD strip (its reserved top while the HUD shows).
        public static Box Bounds(RectTransform canvas)
        {
            var r = canvas.rect;
            var cluster = PlayerHud.HudCluster.Instance;
            var top = cluster ? cluster.ReservedScreenTop : 0f;
            var strip = top > 0f && Screen.height > 0 ? r.height * (1f - top / Screen.height) : 0f;
            return WindowMoveMath.Bounds(r.width, r.height, strip);
        }

        // A drag result: the wanted offset, clamped against the window's last default, stored (saved on Save) and
        // announced to its owner.
        public static void Set(string id, Vector2 wanted)
        {
            if (!Enabled) return;
            float dx = wanted.x, dy = wanted.y;
            if (Defaults.TryGetValue(id, out var def) && Canvases.TryGetValue(id, out var canvas) && canvas)
                WindowMoveMath.Clamp(def, Bounds(canvas), ref dx, ref dy);
            var key = Key(id);
            if (Store.TryGetValue(key, out var old) && Mathf.Abs(old.X - dx) < 0.01f && Mathf.Abs(old.Y - dy) < 0.01f) return;
            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f) Store.Remove(key);
            else Store[key] = (dx, dy);
            Changed(id);
        }

        // A per-window state flag at this resolution (e.g. the map window maximized), stored with the positions
        // ("<id>#<flag>@WxH=1,0"); Layout.ResetLayout clears it with the offsets.
        public static bool Flag(string id, string flag) => Enabled && Store.ContainsKey(Key(id + "#" + flag));

        public static void SetFlag(string id, string flag, bool on)
        {
            if (!Enabled) return;
            var key = Key(id + "#" + flag);
            if (on == Store.ContainsKey(key)) return;
            if (on) Store[key] = (1f, 0f);
            else Store.Remove(key);
            Changed(id);
            Save();
        }

        public static void Reset(string id)
        {
            if (!Store.Remove(Key(id))) return;
            Changed(id);
            Save();
        }

        // Several windows at this resolution (the inventory screen's reset-layout action), one save. Own name: harness
        // reflection looks Reset(string) up by name.
        public static void ResetMany(string[] ids)
        {
            var any = false;
            foreach (var id in ids)
                if (Store.Remove(Key(id)))
                {
                    any = true;
                    Changed(id);
                }
            if (any) Save();
        }

        // Every window, every resolution (ConfigurationManager Layout.ResetLayout).
        public static void ResetAll()
        {
            Store.Clear();
            Save();
            Changed(null);
            Plugin.Log?.LogInfo("[RuneSkin] window positions reset (all windows back to their docks)");
        }

        // Owner callback when its window's offset changes (null id = all).
        public static void OnMoved(string id, Action relayout) => Listeners[id] = relayout;

        private static void Changed(string id)
        {
            Revision++;
            Scratch.Clear();
            foreach (var pair in Listeners)
                if (id == null || pair.Key == id) Scratch.Add(pair.Value);
            foreach (var a in Scratch)
            {
                try { a(); }
                catch (Exception e) { Plugin.Log?.LogWarning($"[RuneSkin] window move relayout failed: {e.GetBaseException().Message}"); }
            }
            Scratch.Clear();
        }

        public static void Load()
        {
            try
            {
                _store = File.Exists(FilePath) ? WindowMoveMath.Parse(File.ReadAllText(FilePath)) : new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);
            }
            catch (Exception e)
            {
                _store = new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);
                Plugin.Log?.LogWarning($"[RuneSkin] window positions not read ({FilePath}): {e.GetBaseException().Message}");
            }
            if (Listeners.Count > 0) Changed(null);
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, WindowMoveMath.Format(Store));
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"[RuneSkin] window positions not saved ({FilePath}): {e.GetBaseException().Message}");
            }
        }

        // Harness / diagnostics: "id=dx,dy" of this resolution's stored offsets.
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            var suffix = "@" + Screen.width + "x" + Screen.height;
            foreach (var pair in Store)
                if (pair.Key.EndsWith(suffix, StringComparison.Ordinal))
                    sb.Append(pair.Key.Substring(0, pair.Key.Length - suffix.Length)).Append('=').Append(pair.Value.X.ToString("0.#")).Append(',').Append(pair.Value.Y.ToString("0.#")).Append(' ');
            return sb.ToString().TrimEnd();
        }

        // ---- handles -----------------------------------------------------------------------------------------------

        public const string HandleName = "RuneSkinDragHandle";

        // The drag band of `window` (a non-drawing raycast target, last child of `parent` so it is hit before the band's
        // decor; Selectables of the window under it keep their clicks, WindowDrag.IsRaycastLocationValid).
        public static WindowDrag Handle(Transform parent, string id, Transform window = null, string name = HandleName)
        {
            if (!parent) return null;
            var t = parent.Find(name);
            WindowDrag drag;
            if (t) drag = t.GetComponent<WindowDrag>();
            else
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                drag = go.AddComponent<WindowDrag>();
                // Own graphic: never re-classified by a restyle pass (no role art: it draws nothing).
                var mark = go.AddComponent<SkinMark>();
                mark.Internal = true;
                mark.Role = "content";
                mark.Reason = "window drag band (draws nothing)";
                mark.Outcome = Outcome.Content;
                mark.ThemeRevision = -1;
                mark.BoundInstance = go.GetInstanceID();
                mark.hideFlags = HideFlags.HideInInspector;
            }
            if (!drag) return null;
            drag.Id = id;
            drag.Window = window ? window : parent;
            drag.raycastTarget = true;
            if (drag.transform.GetSiblingIndex() != parent.childCount - 1) drag.transform.SetAsLastSibling();
            if (drag.enabled != Enabled) drag.enabled = Enabled;
            return drag;
        }

    }

    // The drag band: draws nothing (empty mesh), takes the pointer. Drag = move the window by the pointer travel (screen
    // px -> canvas units); double-click = back to the default dock.
    public sealed class WindowDrag : Graphic, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, ICanvasRaycastFilter
    {
        public string Id;
        public Transform Window;

        private Vector2 _start, _startOffset;
        private bool _dragging;
        private readonly List<Selectable> _selectables = new List<Selectable>();
        private int _selectablesFrame = -1000;

        public bool Dragging => _dragging;

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        private float Scale()
        {
            var c = canvas ? canvas.rootCanvas : null;
            return c && c.scaleFactor > 0f ? c.scaleFactor : 1f;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || string.IsNullOrEmpty(Id) || !WindowMove.Enabled) return;
            _dragging = true;
            _start = eventData.position;
            _startOffset = WindowMove.Offset(Id);
            // Start from what is shown (a stored offset beyond the current bounds is clamped on screen).
            if (WindowMoveProbe.TryApplied(Id, out var applied)) _startOffset = applied;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            WindowHeaderMath.DragOffset(_startOffset.x, _startOffset.y, _start.x, _start.y, eventData.position.x, eventData.position.y, Scale(), out var dx, out var dy);
            WindowMove.Set(Id, new Vector2(dx, dy));
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            OnDrag(eventData);
            _dragging = false;
            WindowMove.Save();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (WindowHeaderMath.IsReset(eventData.clickCount, eventData.button == PointerEventData.InputButton.Left) && !string.IsNullOrEmpty(Id)) WindowMove.Reset(Id);
        }

        // A button / toggle / field of the window under the band keeps its clicks.
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
        {
            if (!Window) return true;
            if (Time.frameCount - _selectablesFrame > 30)
            {
                _selectablesFrame = Time.frameCount;
                _selectables.Clear();
                Window.GetComponentsInChildren(false, _selectables);
            }
            rectTransform.GetWorldCorners(Own);
            foreach (var s in _selectables)
            {
                if (!s || !s.isActiveAndEnabled || !s.interactable) continue;
                if (s.transform.IsChildOf(transform) || !(s.transform is RectTransform rt)) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, sp, eventCamera)) continue;
                // Clipped away there (a scrolled grid's rows above its view, under a RectMask2D / Mask): not a control at
                // this point. Run 20261026-184538: a chest's masked top rows lay under the top-aligned dock header, the
                // band gave way and the pointer fell through to InventoryGui's dropButton.
                if (ClippedAt(rt, sp, eventCamera)) continue;
                // A selectable spanning the whole band is a backdrop (full-screen closer, scrim), not a control in it.
                if (Covers(rt)) continue;
                return false;
            }
            return true;
        }

        private static bool ClippedAt(RectTransform rt, Vector2 sp, Camera cam)
        {
            for (var p = rt.parent; p; p = p.parent)
            {
                if (!(p is RectTransform pr)) continue;
                var clips = p.TryGetComponent<RectMask2D>(out var m2) && m2.enabled || p.TryGetComponent<Mask>(out var m) && m.enabled;
                if (clips && !RectTransformUtility.RectangleContainsScreenPoint(pr, sp, cam)) return true;
            }
            return false;
        }

        private static readonly Vector3[] Own = new Vector3[4];
        private static readonly Vector3[] Other = new Vector3[4];

        private static bool Covers(RectTransform rt)
        {
            rt.GetWorldCorners(Other);
            const float e = 0.5f;
            return Other[0].x <= Own[0].x + e && Other[0].y <= Own[0].y + e && Other[2].x >= Own[2].x - e && Other[2].y >= Own[2].y - e;
        }
    }

    // Offsets as last applied per window (what the screen shows), so a drag starts from there.
    internal static class WindowMoveProbe
    {
        private static readonly Dictionary<string, Vector2> Last = new Dictionary<string, Vector2>();

        public static void Note(string id, Vector2 applied) => Last[id] = applied;

        public static bool TryApplied(string id, out Vector2 applied) => Last.TryGetValue(id, out applied);
    }

    // A window vanilla or another mod positions: its offset is added after their writes (LateUpdate). The position it
    // finds is the default whenever it differs from what this component last wrote (vanilla re-anchors or an animator
    // writes it each frame), so their own layout keeps working underneath.
    public sealed class MovableWindow : MonoBehaviour
    {
        public string Id;
        private RectTransform _rt, _canvas;
        private Vector2 _base, _written, _applied;
        private bool _has;

        public static MovableWindow Ensure(RectTransform window, string id)
        {
            if (!window) return null;
            var m = window.GetComponent<MovableWindow>() ?? window.gameObject.AddComponent<MovableWindow>();
            m.Id = id;
            return m;
        }

        private void LateUpdate() => Apply();

        public void Apply()
        {
            if (!_rt) _rt = transform as RectTransform;
            if (!_canvas)
            {
                var c = GetComponentInParent<Canvas>();
                _canvas = c ? (RectTransform)c.rootCanvas.transform : null;
            }
            var parent = _rt ? _rt.parent : null;
            if (!_rt || !_canvas || !parent || string.IsNullOrEmpty(Id)) return;
            var cur = _rt.anchoredPosition;
            if (!_has || (cur - _written).sqrMagnitude > 0.01f)
            {
                _base = cur;
                _has = true;
                _applied = Vector2.zero;
            }
            var shown = Geo.BoxOf(_canvas, _rt);
            var def = WindowMoveMath.Shift(shown, -_applied.x, -_applied.y);
            var o = WindowMove.Applied(Id, def, _canvas);
            var k = _canvas.lossyScale.x / Mathf.Max(Mathf.Abs(parent.lossyScale.x), 1e-6f);
            _written = _base + new Vector2(o.x * k, -o.y * k);
            if ((_rt.anchoredPosition - _written).sqrMagnitude > 0.0001f) _rt.anchoredPosition = _written;
            _applied = o;
        }

        private void OnDisable()
        {
            // Closed: the next open finds whatever vanilla writes then (re-based above).
            if (_rt && _has && (_rt.anchoredPosition - _written).sqrMagnitude < 0.01f) _rt.anchoredPosition = _base;
            _has = false;
        }
    }
}
