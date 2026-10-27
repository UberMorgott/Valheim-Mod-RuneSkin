using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
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
    //   3. The choice stack (after the unit pass: the panel stack's corner caps exist then) moves above the bottom corner
    //      caps and takes the frame's inner width between its drawn side borders (ElDialogMath.InnerWidth, never under
    //      Epic Loot's 300; run 20261027-165655 truncated "(сохранить) Урон по оглушенному врагу +7,5% [3,5-10,5]" at
    //      300, run 20261027-170958 sized them to the description view, which measured narrower than 300); each label
    //      fills its button, wraps, auto-sizes to the readable floor and grows its button past two lines
    //      (ElDialogMath.Stack / ButtonHeight); the description view gives up that height (it scrolls).
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
                if (d.GetType().FullName == AugmentDialog && Field(d.GetType(), "EffectChoiceButtons")?.GetValue(d) is List<Button> buttons)
                    Choices(d.transform, buttons);
            });
        }

        // Label inset from the button's top / bottom edge (frame units).
        private const float ChoiceVPad = 3f;

        private static readonly List<RectTransform> Active = new List<RectTransform>(8);
        private static readonly Vector3[] Corners4 = new Vector3[4];

        // The choice stack: the frame's inner width between its drawn side borders, above the bottom corner caps; each label fills its button (inset by the plate's 9-slice border), wraps, auto-sizes down to the
        // readable floor, and its button grows when two lines at the floor still need more; the description view gives
        // up the height the stack takes.
        private static void Choices(Transform dialog, List<Button> buttons)
        {
            Active.Clear();
            RectTransform frame = null;
            foreach (var b in buttons)
                if (b && b.gameObject.activeSelf && b.transform is RectTransform rt && rt.parent is RectTransform f)
                {
                    frame = frame ? frame : f;
                    if (f == frame) Active.Add(rt);
                }
            if (!frame || Active.Count == 0) return;
            var scroll = Column(frame);
            if (!scroll) return;
            var fr = frame.rect;
            var col = RectIn(scroll, frame);
            // Above the caps the row runs between the frame art's drawn side borders (+ row pad): the whole inner width.
            SideInsets(frame, dialog, out var left, out var right);
            var width = ElDialogMath.InnerWidth(fr.width, left, right);
            var centre = fr.center.x + (left - right) * 0.5f;
            if (Logged.Add(dialog.GetInstanceID()))
                Plugin.Log.LogInfo($"[RuneSkin] el dialog {dialog.name}: frame {fr.width:0.#}x{fr.height:0.#} scale {frame.lossyScale.x:0.###}, description view {col.width:0.#} wide " +
                                   $"(anchors {scroll.anchorMin}-{scroll.anchorMax} sizeDelta {scroll.sizeDelta}), side insets {left:0.#}/{right:0.#} -> choices {width:0.#}");
            var floor = ThemeTokens.GetFloat("size.text.fit.min", 12f);
            var vpad = ChoiceVPad;
            var heights = new float[Active.Count];
            for (var i = 0; i < Active.Count; i++)
                heights[i] = Label(Active[i], width, floor, vpad);
            var stack = ElDialogMath.Stack(fr.height, fr.yMax - col.yMax, heights, CapBand(frame, dialog), ElDialogMath.ElGap, 80f);
            for (var i = 0; i < Active.Count; i++)
            {
                var rt = Active[i];
                var h = heights[i];
                if (Mathf.Abs(rt.rect.width - width) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                if (Mathf.Abs(rt.rect.height - h) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                // Pivot position in the frame's space (anchor-agnostic): column centre, the stack's bottom edge.
                var p = new Vector3(centre + (rt.pivot.x - 0.5f) * width, fr.yMin + stack.Bottoms[i] + rt.pivot.y * h, rt.localPosition.z);
                if ((rt.localPosition - p).sqrMagnitude > 0.01f) rt.localPosition = p;
            }
            if (Mathf.Abs(scroll.rect.height - stack.ScrollHeight) > 0.5f)
                scroll.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, fr.yMax - col.yMax, stack.ScrollHeight);
            Active.Clear();
        }

        private static readonly HashSet<int> Logged = new HashSet<int>();

        // Left / right inset of the row from the frame edges (frame units): the panel stack's frame art edge + its
        // 9-slice side border + size.row.cap.pad; without a stack Epic Loot's own content inset (10, its scroll view).
        private static void SideInsets(RectTransform frame, Transform dialog, out float left, out float right)
        {
            var fr = frame.rect;
            var art = Decor(frame, dialog)?.Frame;
            if (!art || !art.enabled)
            {
                left = right = 10f;
                return;
            }
            var pad = ThemeTokens.GetFloat("size.row.cap.pad", 6f);
            var a = RectIn(art.rectTransform, frame);
            var border = TextInset.SliceBorderIn(art, frame);
            left = Mathf.Max(0f, a.xMin + border.x - fr.xMin) + pad;
            right = Mathf.Max(0f, fr.xMax - (a.xMax - border.z)) + pad;
        }

        // Epic Loot's description scroll view (CraftSuccessDialog.ConvertToScrollingDescription under the frame).
        private static RectTransform Column(RectTransform frame)
        {
            foreach (Transform c in frame)
                if (c.TryGetComponent<ScrollRect>(out _)) return (RectTransform)c;
            return null;
        }

        // One label across its button; returns the button height it needs (Epic Loot's 40, or more for two lines at the floor).
        private static float Label(RectTransform button, float width, float floor, float vpad)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (!label) return ElDialogMath.ElHeight;
            var plate = button.TryGetComponent<Button>(out var b) ? b.targetGraphic as Image : null;
            var hpad = (plate ? TextInset.SliceBorder(plate).x : 0f) + ThemeTokens.GetFloat("size.row.cap.pad", 6f);
            var lr = label.rectTransform;
            if (lr.parent == button)
            {
                if (lr.anchorMin != Vector2.zero) lr.anchorMin = Vector2.zero;
                if (lr.anchorMax != Vector2.one) lr.anchorMax = Vector2.one;
                var min = new Vector2(hpad, vpad);
                var max = new Vector2(-hpad, -vpad);
                if (lr.offsetMin != min) lr.offsetMin = min;
                if (lr.offsetMax != max) lr.offsetMax = max;
            }
            if (label.textWrappingMode != TextWrappingModes.Normal) label.textWrappingMode = TextWrappingModes.Normal;
            var top = label.enableAutoSizing ? Mathf.Max(label.fontSizeMax, floor) : Mathf.Max(label.fontSize, floor);
            if (!label.enableAutoSizing || Mathf.Abs(label.fontSizeMin - floor) > 0.01f || Mathf.Abs(label.fontSizeMax - top) > 0.01f)
            {
                label.fontSizeMax = top;
                label.fontSizeMin = floor;
                label.enableAutoSizing = true;
            }
            // Height of the label wrapped in the text width at the floor size (auto-size off for the measure only).
            var textW = width - 2f * hpad - label.margin.x - label.margin.z;
            if (textW <= 1f || string.IsNullOrEmpty(label.text)) return ElDialogMath.ElHeight;
            var size = label.fontSize;
            label.enableAutoSizing = false;
            label.fontSize = floor;
            var need = label.GetPreferredValues(label.text, textW, 0f).y + label.margin.y + label.margin.w;
            label.fontSize = size;
            label.enableAutoSizing = true;
            return ElDialogMath.ButtonHeight(ElDialogMath.ElHeight, need, vpad);
        }

        // Top edge of the bottom corner caps over the frame's bottom edge, in frame units (0 without a panel stack).
        private static float CapBand(RectTransform frame, Transform dialog)
        {
            var decor = Decor(frame, dialog);
            var corners = decor ? decor.CornerCaps : null;
            if (corners == null || corners.Length < 4) return 0f;
            var band = 0f;
            for (var i = 2; i < 4; i++)
            {
                var c = corners[i];
                if (!c || !c.TryGetComponent<Image>(out var img) || !img.enabled || !img.sprite || !c.gameObject.activeInHierarchy) continue;
                band = Mathf.Max(band, RectIn(c, frame).yMax - frame.rect.yMin);
            }
            return band;
        }

        // The panel stack on the frame, an ancestor up to the dialog, or the frame's own panel art (PanelSwap child).
        private static PanelDecor Decor(RectTransform frame, Transform dialog)
        {
            for (var p = (Transform)frame; p; p = p.parent)
            {
                var decor = PanelDecor.Of(p);
                if (decor) return decor;
                if (p == dialog) break;
            }
            foreach (Transform c in frame)
            {
                var decor = PanelDecor.Of(c);
                if (decor) return decor;
            }
            return null;
        }

        // rt's rect in space's local units.
        private static Rect RectIn(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(Corners4);
            var a = space.InverseTransformPoint(Corners4[0]);
            var b = space.InverseTransformPoint(Corners4[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
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
