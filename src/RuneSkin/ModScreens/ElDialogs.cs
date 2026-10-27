using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Epic Loot's modal dialogs built from vanilla parts (no reference; EpicLoot 0.15.0 decompile):
    // - AugmentChoiceDialog (AugmentHelper.CreateAugmentChoiceDialog, AugmentHelper.cs:65-98; EnchantingUIController
    //   .AugmentItem reparents it under the enchanting window, EnchantingUIController.cs:1551-1568) and CraftSuccessDialog
    //   (CraftSuccessDialog.Create, CraftSuccessDialog.cs:273-298; under the enchanting window or MerchantPanel) are
    //   Instantiate(InventoryGui.m_variantDialog) + Instantiate(m_recipeIcon / m_recipeName / m_recipeDecription) into
    //   its frame. Instantiate copies the source's live state, so the dialog depended on what RuneSkin had done to the
    //   sources by then:
    //   1. Geometry: Epic Loot lays the dialog out against the vanilla prefab geometry of m_recipeIcon / m_recipeName
    //      (icon top-left, name beside it, description pushed below: AugmentHelper.cs:73-84). CraftingSkin.Detail re-places
    //      both into the crafting detail column every layout (icon centred, IconH tall, CraftingMath o.Icon), so a clone
    //      taken after any crafting layout put the icon over the description (user 2026-10-27, 0.25.11); one taken before
    //      the first crafting layout was right. The clones get the vanilla geometry back (snapshot in InventoryGui.Awake
    //      prefix, before any RuneSkin postfix places them) - the geometry Epic Loot's own offsets assume.
    //   2. RootMark: VanillaExtras marks m_variantDialog as its own root; the clone carried that mark without being a
    //      registered root, so the owning unit's pass (ForeignUi: EpicLootEnchanting / EpicLootBoard) skipped the whole
    //      dialog as a "nested root" (Restyler.ExcludedBy) and the Jotunn choice buttons (GUIManager.CreateButton,
    //      AugmentHelper.cs:88) kept Jotunn's brown plates and Epic Loot's font, truncated at 300 wide. The stale mark
    //      leaves; the dialog is part of its window's unit and gets the variant dialog's roles there (ForeignRoles.Precise).
    //   3. The choice buttons take the frame's inner width (ElDialogMath.ChoiceWidth, after the unit pass: the panel
    //      stack's corner-cap keep-out is known then); their labels fit through TextInset.
    public static class ElDialogs
    {
        public const string AugmentDialog = "EpicLoot.Crafting.AugmentChoiceDialog";
        public const string SuccessDialog = "EpicLoot.Crafting.CraftSuccessDialog";
        private const string AugmentHelper = "EpicLoot.Crafting.AugmentHelper";

        internal static Harmony Harmony;
        private static bool _patched;

        private struct Geo
        {
            public bool Set;
            public Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
            public Vector3 Scale;
            public Quaternion Rotation;

            public static Geo Of(RectTransform rt) => rt
                ? new Geo { Set = true, AnchorMin = rt.anchorMin, AnchorMax = rt.anchorMax, Pivot = rt.pivot, Position = rt.anchoredPosition, Size = rt.sizeDelta, Scale = rt.localScale, Rotation = rt.localRotation }
                : default;

            public void To(RectTransform rt)
            {
                if (!Set || !rt) return;
                rt.anchorMin = AnchorMin;
                rt.anchorMax = AnchorMax;
                rt.pivot = Pivot;
                rt.sizeDelta = Size;
                rt.anchoredPosition = Position;
                rt.localScale = Scale;
                rt.localRotation = Rotation;
            }
        }

        private static Geo _icon, _name;

        // InventoryGui.Awake prefix: the serialized prefab geometry, before any postfix (CraftingSkin) moves them.
        internal static void Snapshot(InventoryGui gui)
        {
            if (!gui) return;
            if (gui.m_recipeIcon) _icon = Geo.Of(gui.m_recipeIcon.rectTransform);
            if (gui.m_recipeName) _name = Geo.Of(gui.m_recipeName.rectTransform);
        }

        // Patched once from InventoryGui.Awake (Epic Loot is loaded by then).
        internal static void EnsurePatched()
        {
            if (_patched || Harmony == null) return;
            _patched = true;
            var post = new HarmonyMethod(typeof(ElDialogs), nameof(Created_Postfix));
            // No Epic Loot (or one without these builders): nothing clones the variant dialog.
            var helper = AccessTools.TypeByName(AugmentHelper);
            if (helper != null) Patch(helper, "CreateAugmentChoiceDialog", post);
            var success = AccessTools.TypeByName(SuccessDialog);
            if (success != null) Patch(success, "Create", post);
        }

        private static void Patch(Type type, string method, HarmonyMethod post)
        {
            var m = AccessTools.DeclaredMethod(type, method);
            if (m == null || m.ReturnType == typeof(void))
            {
                Plugin.Log.LogWarning($"[RuneSkin] {type.FullName}.{method} not found: Epic Loot dialogs keep the crafting geometry of their cloned parts");
                return;
            }
            try { Harmony.Patch(m, postfix: post); }
            catch (Exception e) { Plugin.Log.LogWarning($"[RuneSkin] {type.FullName}.{method} patch failed: {e.Message}"); }
        }

        private static void Created_Postfix(object __result)
        {
            try { Fix(__result as Component); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] Epic Loot dialog fix failed (its cloned parts keep the crafting layout): {e}"); }
        }

        private static void Fix(Component dialog)
        {
            if (!dialog) return;
            var type = dialog.GetType();
            // Auga-built panels (IsAugaPanel) are no variant-dialog clones.
            if (Field(type, "IsAugaPanel")?.GetValue(dialog) is bool auga && auga) return;
            if (ElDialogMath.IsStaleRootMark(dialog.TryGetComponent<RootMark>(out var mark), ScreenRegistry.IsRoot(dialog.transform)))
                UnityEngine.Object.DestroyImmediate(mark);
            if (Field(type, "Icon")?.GetValue(dialog) is Image icon) _icon.To(icon.rectTransform);
            if (Field(type, "MagicBG")?.GetValue(dialog) is Image bg) _icon.To(bg.rectTransform);
            if (Field(type, "NameText")?.GetValue(dialog) is TMP_Text name) _name.To(name.rectTransform);
        }

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> Fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();

        private static FieldInfo Field(Type t, string name)
        {
            if (!Fields.TryGetValue(t, out var map)) Fields[t] = map = new Dictionary<string, FieldInfo>();
            if (!map.TryGetValue(name, out var f)) map[name] = f = AccessTools.Field(t, name);
            return f;
        }

        private static bool IsDialog(Type t) => t != null && (t.FullName == AugmentDialog || t.FullName == SuccessDialog);

        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();

        private static void Each(Transform root, Action<MonoBehaviour> a)
        {
            Behaviours.Clear();
            root.GetComponentsInChildren(true, Behaviours);
            foreach (var b in Behaviours)
                if (b && IsDialog(b.GetType()) && !(Field(b.GetType(), "IsAugaPanel")?.GetValue(b) is bool auga && auga))
                    a(b);
            Behaviours.Clear();
        }

        // Roles inside an Epic Loot window (ForeignRoles.Precise): what the variant dialog's own form gave them
        // (VanillaExtras: icons kept, popup backdrops), plus the item icon and its magic background as content.
        public static void Roles(ScreenContext ctx, Transform root)
        {
            Each(root, d =>
            {
                VanillaExtras.VariantRoles(ctx, d.transform);
                var t = d.GetType();
                if (Field(t, "Icon")?.GetValue(d) is Image icon && icon) ctx.Role(icon, "content", "Epic Loot dialog item icon");
                if (Field(t, "MagicBG")?.GetValue(d) is Image bg && bg) ctx.Role(bg, "content", "Epic Loot dialog magic item background");
            });
        }

        // After the unit pass: augment choice buttons across the frame's inner width (labels refit on the size change).
        public static void After(Transform unit)
        {
            Each(unit, d =>
            {
                if (d.GetType().FullName != AugmentDialog || !(Field(d.GetType(), "EffectChoiceButtons")?.GetValue(d) is List<Button> buttons)) return;
                foreach (var b in buttons)
                {
                    if (!b || !(b.transform is RectTransform rt) || !(rt.parent is RectTransform frame)) continue;
                    var w = ElDialogMath.ChoiceWidth(rt.rect.width, frame.rect.width, KeepOut(frame, d.transform));
                    if (Mathf.Abs(rt.rect.width - w) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                }
            });
        }

        // Corner-cap keep-out of the panel stack on the frame or an ancestor up to the dialog, in frame units.
        private static float KeepOut(RectTransform frame, Transform dialog)
        {
            for (var p = (Transform)frame; p; p = p.parent)
            {
                var decor = PanelDecor.Of(p);
                if (decor) return decor.ContentInset() * p.lossyScale.x / Mathf.Max(frame.lossyScale.x, 1e-6f);
                if (p == dialog) break;
            }
            // The frame's own panel art carries the stack (PanelSwap child).
            foreach (Transform c in frame)
            {
                var decor = PanelDecor.Of(c);
                if (decor) return decor.ContentInset() * c.lossyScale.x / Mathf.Max(frame.lossyScale.x, 1e-6f);
            }
            return 0f;
        }
    }

    [HarmonyPatch]
    public static class ElDialogsAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup). Needed while
        // RuneSkin moves the cloned parts (Crafting) or marks the cloned dialog (InventoryExtras).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting) || OwnershipRegistry.IsOwned(OwnershipRegistry.InventoryExtras);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Awake_Prefix(InventoryGui __instance)
        {
            try
            {
                ElDialogs.Snapshot(__instance);
                ElDialogs.EnsurePatched();
            }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] Epic Loot dialog hooks failed: {e}"); }
        }
    }
}
