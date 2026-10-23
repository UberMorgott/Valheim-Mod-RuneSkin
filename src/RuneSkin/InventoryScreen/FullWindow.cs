using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RuneSkin.InventoryScreen
{
    // Full windows opened from the inventory (character stats, compendium) are modal screens: while one is open the
    // inventory panels (every child of InventoryGui.m_inventoryRoot that does not hold the window) are hidden and take
    // no input - a CanvasGroup per panel at alpha 0, not blocking raycasts, not interactable - and they come back
    // exactly as they were when it closes (Esc / B / close button / Tab). One window at a time: opening another closes
    // nothing but hides the same panels; the panels return when the last window leaves. The HUD (outside the
    // inventory root) stays. No GameObject is deactivated, so vanilla state machines (groups, drag, crafting) keep
    // running unchanged.
    public static class FullWindow
    {
        private sealed class Hidden
        {
            public CanvasGroup Group;
            public bool Added;
            public float Alpha;
            public bool Blocks, Interactable;
        }

        private static readonly List<Transform> Open = new List<Transform>(2);
        private static readonly List<Hidden> HiddenPanels = new List<Hidden>(8);

        public static bool Any => Open.Count > 0;

        public static void Enter(Transform window)
        {
            var gui = InventoryGui.instance;
            if (!window || !gui || !gui.m_inventoryRoot) return;
            if (!Open.Contains(window)) Open.Add(window);
            Apply(gui.m_inventoryRoot);
        }

        public static void Exit(Transform window)
        {
            Open.Remove(window);
            Open.RemoveAll(t => !t);
            if (Open.Count == 0) Restore();
            else if (InventoryGui.instance) Apply(InventoryGui.instance.m_inventoryRoot);
        }

        // Every frame (InventoryGui.Update postfix): a window closed by any path (vanilla Escape handling, SetActive)
        // leaves the set; the panels return when none is left.
        public static void Tick()
        {
            WatchAlmanac();
            if (Open.Count == 0) return;
            var n = Open.Count;
            Open.RemoveAll(t => !t || !t.gameObject.activeInHierarchy);
            if (Open.Count == 0) Restore();
            else if (Open.Count != n && InventoryGui.instance) Apply(InventoryGui.instance.m_inventoryRoot);
        }

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
            if (h.Added) Object.Destroy(h.Group);
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
