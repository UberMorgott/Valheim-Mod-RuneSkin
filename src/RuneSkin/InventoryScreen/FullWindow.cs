using System;
using System.Collections.Generic;
using HarmonyLib;
using RuneSkin.Restyle;
using UnityEngine;

namespace RuneSkin.InventoryScreen
{
    // Full windows opened from the inventory (character stats, compendium) are modal screens: while one is open the
    // inventory panels (every child of InventoryGui.m_inventoryRoot that does not hold the window) are hidden and take
    // no input - a CanvasGroup per panel at alpha 0, not blocking raycasts, not interactable, and a render gate
    // (RenderGate: a disabled Canvas, so the hidden panels are not drawn at all; alpha 0 alone still batched and drew
    // every panel under the window) - and they come back exactly as they were when it closes (Esc / B / close button /
    // Tab). One window at a time: opening another closes nothing but hides the same panels; the panels return when the
    // last window leaves. A window that declares its opaque area (screen rect) also gates the UI drawn before it inside
    // that area (CoverCull: HUD parts, minimap) until it closes. No GameObject is deactivated, so vanilla state machines
    // (groups, drag, crafting) keep running unchanged.
    public static class FullWindow
    {
        public const string Owner = "fullwindow";

        private sealed class Hidden
        {
            public CanvasGroup Group;
            public bool Added;
            public float Alpha;
            public bool Blocks, Interactable;
        }

        private static readonly List<Transform> Open = new List<Transform>(2);
        private static readonly Dictionary<Transform, Func<Rect>> Opaque = new Dictionary<Transform, Func<Rect>>();
        private static readonly List<Hidden> HiddenPanels = new List<Hidden>(8);
        private static readonly List<Transform> CoverWindows = new List<Transform>(2);
        private static readonly List<Rect> CoverRects = new List<Rect>(2);
        private static readonly List<Transform> Stale = new List<Transform>(2);
        private static float _nextCull;

        // Seconds between two cover passes while a window with an opaque area shows.
        private const float CullEvery = 0.25f;

        public static bool Any => Open.Count > 0;

        public static void Enter(Transform window, Func<Rect> opaque = null)
        {
            var gui = InventoryGui.instance;
            if (!window || !gui || !gui.m_inventoryRoot) return;
            if (!Open.Contains(window)) Open.Add(window);
            if (opaque != null) Opaque[window] = opaque;
            Apply(gui.m_inventoryRoot);
            _nextCull = 0f;
        }

        public static void Exit(Transform window)
        {
            Open.Remove(window);
            Opaque.Remove(window);
            Open.RemoveAll(t => !t);
            if (Open.Count == 0) Restore();
            else if (InventoryGui.instance) Apply(InventoryGui.instance.m_inventoryRoot);
            _nextCull = 0f;
        }

        // Every frame (InventoryGui.Update postfix): a window closed by any path (vanilla Escape handling, SetActive)
        // leaves the set; the panels return when none is left.
        public static void Tick()
        {
            WatchAlmanac();
            if (Open.Count > 0)
            {
                var n = Open.Count;
                Open.RemoveAll(t => !t || !t.gameObject.activeInHierarchy);
                if (Open.Count == 0) Restore();
                else if (Open.Count != n && InventoryGui.instance) Apply(InventoryGui.instance.m_inventoryRoot);
                if (Open.Count != n)
                {
                    _nextCull = 0f;
                    Stale.Clear();
                    foreach (var w in Opaque.Keys)
                        if (!w || !Open.Contains(w)) Stale.Add(w);
                    foreach (var w in Stale) Opaque.Remove(w);
                    Stale.Clear();
                }
            }
            var gui = InventoryGui.instance;
            var standing = StandingShown(gui);
            if (standing != _standing)
            {
                _standing = standing;
                _nextCull = 0f;
            }
            if (Open.Count == 0 && !standing)
            {
                if (CoverWindows.Count > 0 || CoverCull.CoveredCount > 0)
                {
                    CoverWindows.Clear();
                    CoverRects.Clear();
                    CoverCull.Clear();
                }
                return;
            }
            Cull(gui);
        }

        // Standing covers: the open inventory's own windows (every RuneSkin panel stack under the inventory root with a
        // drawn opaque backing) gate the UI drawn before them inside their backing, like a full window's opaque area -
        // layout A docks crafting at the top right over the minimap, the doll over the crosshair (run 20261026-222320
        // overdraw-crafting covered=9). Only while the screen shows fully faded in (the fade CanvasGroup on InventoryGui)
        // and no full window is open (that one hides these panels).
        private static bool _standing;

        private static bool StandingShown(InventoryGui gui)
        {
            if (!gui || !gui.m_inventoryRoot || Open.Count > 0 || !InventoryGui.IsVisible()) return false;
            return !gui.TryGetComponent<CanvasGroup>(out var fade) || fade.alpha >= 0.999f;
        }

        private static void Cull(InventoryGui gui)
        {
            if (Time.unscaledTime < _nextCull) return;
            _nextCull = Time.unscaledTime + CullEvery;
            CoverWindows.Clear();
            CoverRects.Clear();
            foreach (var w in Open)
                if (w && Opaque.TryGetValue(w, out var f) && f != null)
                {
                    CoverWindows.Add(w);
                    CoverRects.Add(f());
                }
            if (_standing && gui) AddStanding(gui.m_inventoryRoot);
            CoverCull.Refresh(CoverWindows, CoverRects);
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static void AddStanding(Transform root)
        {
            foreach (var d in PanelDecor.All)
            {
                var b = d ? d.Backing : null;
                if (!b || !b.gameObject.activeInHierarchy || !b.IsChildOf(root) || RenderGate.IsConcealed(b)) continue;
                if (!b.TryGetComponent<UnityEngine.UI.Graphic>(out var g) || !g.enabled || g.color.a < 0.999f || !g.canvas) continue;
                var canvas = g.canvas.rootCanvas;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                b.GetWorldCorners(Corners);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var c in Corners)
                {
                    var s = RectTransformUtility.WorldToScreenPoint(cam, c);
                    x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y);
                    x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
                }
                CoverWindows.Add(b);
                CoverRects.Add(Rect.MinMaxRect(x0, y0, x1, y1));
            }
        }

        // Harness / diagnostics: the open windows' opaque areas (screen px).
        public static int CoverCount => CoverWindows.Count;
        public static Transform CoverWindow(int i) => i >= 0 && i < CoverWindows.Count ? CoverWindows[i] : null;
        public static Rect CoverRect(int i) => i >= 0 && i < CoverRects.Count ? CoverRects[i] : default;

        // Almanac (another mod: its window replaces the trophies panel under hudroot's parent, HUD/Almanac) is a full window
        // too: found by its root name (no dependency), entered while it shows, left when it hides.
        private static Transform _almanac;
        private static float _nextAlmanacLookup;

        private static void WatchAlmanac()
        {
            if (!_almanac)
            {
                if (Time.unscaledTime < _nextAlmanacLookup) return;
                _nextAlmanacLookup = Time.unscaledTime + 2f;
                var gui = InventoryGui.instance;
                var hud = gui && gui.transform.parent ? gui.transform.parent.Find("HUD") : null;
                _almanac = hud ? hud.Find(RuneSkin.Compat.ModCompat.AlmanacRoot) : null;
                if (!_almanac) return;
            }
            var shown = _almanac.gameObject.activeInHierarchy;
            var listed = Open.Contains(_almanac);
            if (shown && !listed) Enter(_almanac);
            else if (!shown && listed) Exit(_almanac);
        }

        public static void ExitAll()
        {
            Open.Clear();
            Opaque.Clear();
            Restore();
        }

        // Hide every root child that holds none of the open windows.
        private static void Apply(Transform root)
        {
            foreach (Transform child in root)
            {
                if (Holds(child)) { Show(child); continue; }
                if (Find(child) != null) continue;
                var h = new Hidden();
                if (!child.TryGetComponent(out h.Group))
                {
                    h.Group = child.gameObject.AddComponent<CanvasGroup>();
                    h.Added = true;
                }
                h.Alpha = h.Group.alpha;
                h.Blocks = h.Group.blocksRaycasts;
                h.Interactable = h.Group.interactable;
                h.Group.alpha = 0f;
                h.Group.blocksRaycasts = false;
                h.Group.interactable = false;
                RenderGate.Set(child.gameObject, Owner, true);
                HiddenPanels.Add(h);
            }
        }

        private static bool Holds(Transform child)
        {
            foreach (var w in Open)
                if (w && w.IsChildOf(child)) return true;
            return false;
        }

        private static Hidden Find(Transform child)
        {
            foreach (var h in HiddenPanels)
                if (h.Group && h.Group.transform == child) return h;
            return null;
        }

        private static void Show(Transform child)
        {
            var h = Find(child);
            if (h == null) return;
            Put(h);
            HiddenPanels.Remove(h);
        }

        private static void Put(Hidden h)
        {
            if (!h.Group) return;
            RenderGate.Set(h.Group.gameObject, Owner, false);
            if (h.Added) UnityEngine.Object.Destroy(h.Group);
            else
            {
                h.Group.alpha = h.Alpha;
                h.Group.blocksRaycasts = h.Blocks;
                h.Group.interactable = h.Interactable;
            }
        }

        private static void Restore()
        {
            foreach (var h in HiddenPanels) Put(h);
            HiddenPanels.Clear();
            CoverWindows.Clear();
            CoverRects.Clear();
            CoverCull.Clear();
        }

        // Harness / diagnostics: inventory root children currently hidden by a full window.
        public static int HiddenCount => HiddenPanels.Count;
    }
    // Compendium (vanilla TextsDialog inside the inventory): a full window too. Installed with the Inventory family.
    [HarmonyPatch]
    public static class FullWindowAdapter
    {
        private static bool Prepare() => RuneSkin.Ownership.OwnershipRegistry.IsOwned(RuneSkin.Ownership.OwnershipRegistry.Inventory);

        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.Setup))]
        [HarmonyPostfix]
        private static void TextsSetup_Postfix(TextsDialog __instance) => FullWindow.Enter(__instance.transform);

        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.OnClose))]
        [HarmonyPostfix]
        private static void TextsClose_Postfix(TextsDialog __instance) => FullWindow.Exit(__instance.transform);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void Hide_Postfix() => FullWindow.ExitAll();

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        private static void Update_Postfix() => FullWindow.Tick();
    }
}
