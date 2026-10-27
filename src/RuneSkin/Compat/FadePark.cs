using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RuneSkin.Compat
{
    // Other mods' HIDDEN subtrees kept outside the inventory's fade group. Every CanvasGroup alpha write of the open /
    // close fade (InventorySkin.Fade, CanvasGroup on the InventoryGui object) broadcasts OnCanvasGroupChanged to every
    // component under InventoryGui, inactive ones included: rp-open-hitch run 20261027-220134 measured 0.421 ms per write
    // for 2585 objects, of which VNEI's hidden item browser (Crafting/VNEI(Clone)/root) is 877 and EAQS's unused-cell
    // pool (Player/EaqsHiddenSlotRoot) 169. Unity 6000.0.75 batchmode probe (874-object inactive browser under a faded
    // group of ~800 active objects): 0.38 ms per write inside, 0.16 ms parked in an inactive holder outside the group.
    // Hidden, they draw nothing and receive nothing, so they wait in one inactive holder beside InventoryGui (same canvas,
    // outside the group) and go back before their mod shows them. Both mods keep their subtree by object reference, not
    // by path, so nothing they do changes:
    // - VNEI: BaseUI.SetVisibility(visible) is its only show / hide of the browser (root.SetActive + dragHandler.SetActive;
    //   MainVneiHandler.SetTabActive / SetTabInactive, BaseUI.UpdateVisibility). A postfix moves `root` home when it was
    //   shown (still inactive in hierarchy inside the holder, so its OnEnable / Awake run at home, where VNEI built it)
    //   and parks it when hidden. Its sibling index is restored. The browser goes with BaseUI if VNEI destroys it
    //   (MainVneiHandler.CreateBaseUI(forceRecreate)): FadeParkLink.OnDestroy.
    // - EAQS: EnsureHiddenRoot creates its pool once (inactive, kept in a static field, recreated only when destroyed) and
    //   ParkHiddenCell / UpdateInventorySlots move cells in and out of that object wherever it is: the pool itself parks.
    public static class FadePark
    {
        internal static Harmony Harmony;
        private const string HolderName = "RuneSkinFadePark";
        private static bool _tried;
        private static Type _vneiType;
        private static FieldInfo _vneiRoot;

        // InventoryGui.Awake (CraftingSkin.Init, after VNEI's own Awake postfix built its browser): VNEI hook once, then
        // the browser VNEI already hid before the hook existed.
        public static void Attach(InventoryGui gui)
        {
            if (!gui) return;
            EnsurePatched();
            if (_vneiType == null || _vneiRoot == null) return;
            foreach (var ui in gui.GetComponentsInChildren(_vneiType, true)) SyncVnei(ui);
        }

        private static void EnsurePatched()
        {
            if (_tried || Harmony == null) return;
            _tried = true;
            var type = AccessTools.TypeByName(ModCompat.VneiBaseUi);
            if (type == null) return; // no VNEI
            var set = AccessTools.Method(type, "SetVisibility", new[] { typeof(bool) });
            var root = AccessTools.Field(type, "root");
            if (set == null || root == null || !typeof(RectTransform).IsAssignableFrom(root.FieldType))
            {
                Plugin.Log.LogWarning($"[RuneSkin] {ModCompat.VneiBaseUi}.SetVisibility(bool) / root not found: VNEI's hidden browser stays in the inventory's fade group");
                return;
            }
            try
            {
                Harmony.Patch(set, postfix: new HarmonyMethod(typeof(FadePark), nameof(SetVisibility_Postfix)));
                _vneiType = type;
                _vneiRoot = root;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] {ModCompat.VneiBaseUi}.SetVisibility patch failed (its hidden browser stays in the fade group): {e.Message}");
            }
        }

        private static void SetVisibility_Postfix(Component __instance)
        {
            try { SyncVnei(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] VNEI browser park: {e.Message}"); }
        }

        private static void SyncVnei(Component ui)
        {
            if (!ui || _vneiRoot == null || !(_vneiRoot.GetValue(ui) is RectTransform root) || !root || root == ui.transform) return;
            var host = ui.transform;
            var gui = InventoryGui.instance;
            var inFade = gui && host.IsChildOf(gui.transform);
            switch (FadeParkMath.Decide(root.gameObject.activeSelf, root.parent == host, inFade))
            {
                case FadeParkMove.Home:
                    var link = host.GetComponent<FadeParkLink>();
                    root.SetParent(host, false);
                    if (link && link.Index >= 0 && link.Index < host.childCount) root.SetSiblingIndex(link.Index);
                    break;
                case FadeParkMove.Park:
                    var holder = Holder(gui);
                    if (!holder) return;
                    var l = host.GetComponent<FadeParkLink>() ?? host.gameObject.AddComponent<FadeParkLink>();
                    l.Root = root;
                    l.Index = root.GetSiblingIndex();
                    root.SetParent(holder, false);
                    break;
            }
        }

        // InventorySkin.Park (the screen faded out): EAQS's pool of unused cells, once per pool (it stays parked; EAQS
        // only moves cells in and out of it).
        public static void ParkEaqsPool(InventoryGui gui)
        {
            if (!gui || !gui.m_player) return;
            var pool = gui.m_player.Find(ModCompat.EaqsHiddenRoot);
            if (!pool || FadeParkMath.Decide(pool.gameObject.activeSelf, true, true) != FadeParkMove.Park) return;
            var holder = Holder(gui);
            if (holder) pool.SetParent(holder, false);
        }

        // One inactive holder per InventoryGui, its sibling: same canvas and lifetime (destroyed with the GUI on logout),
        // outside the fade group.
        private static Transform Holder(InventoryGui gui)
        {
            var parent = gui ? gui.transform.parent : null;
            if (!parent) return null;
            var found = parent.Find(HolderName);
            if (found) return found;
            var go = new GameObject(HolderName, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }

    // On VNEI's BaseUI while its browser is parked: the home sibling index, and the browser destroyed with BaseUI, as it
    // would be as its child.
    public sealed class FadeParkLink : MonoBehaviour
    {
        public Transform Root;
        public int Index = -1;

        private void OnDestroy()
        {
            if (Root && Root.parent != transform) Destroy(Root.gameObject);
        }
    }
}
