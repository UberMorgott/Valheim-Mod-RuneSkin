using HarmonyLib;
using RuneSkin.Restyle;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Compat
{
    // Epic Loot 0.15 item tooltips (no reference; EpicLoot.dll decompile 2026-10-24): PatchOnHoverFix wraps Topic + Text
    // in a Jotunn scroll view whose root carries EpicLoot.TooltipBox; every TooltipBox.Fit (UITooltip.LateUpdate postfix,
    // on a text or limit change) calls LayOut(width, rightPadding), which sets the box width and then overwrites the
    // content's VerticalLayoutGroup padding with (10, rightPadding, 5, 5). That wiped RuneSkin's corner-cap keep-out
    // (ContentInset, set once per instance by TooltipAdapter): the Topic sat 5 units under the top caps and the lines
    // ran under the side caps (0.21.1 menufix run 20261024-194221, tooltip-safe-rect state=item). After each LayOut the
    // keep-out is raised again (ContentInset.Reassert), the box widens so the text keeps the width Epic Loot chose, and
    // LayOut returns the padded height (Fit compares it to its height limit and sizes the box with it). Fit's out size
    // reports the widened box. Patched once, lazily on the first framed tooltip (Epic Loot is loaded by then).
    public static class EpicLootTooltipBox
    {
        public const string TypeName = "EpicLoot.TooltipBox";

        private static bool _tried;

        public static void EnsurePatched(Harmony harmony)
        {
            if (_tried || harmony == null) return;
            _tried = true;
            var type = RuneSkin.Compat.TypeLookup.ByName(TypeName);
            if (type == null) return; // no Epic Loot (or one without the scroll box): nothing rewrites the padding
            var layOut = AccessTools.Method(type, "LayOut", new[] { typeof(float), typeof(float) });
            var fit = AccessTools.Method(type, "Fit");
            if (layOut == null || layOut.ReturnType != typeof(float))
            {
                Plugin.Log.LogWarning($"[RuneSkin] {TypeName}.LayOut(float, float) not found: Epic Loot item tooltips keep its own padding (text may touch the corner caps)");
                return;
            }
            try
            {
                harmony.Patch(layOut, postfix: new HarmonyMethod(typeof(EpicLootTooltipBox), nameof(LayOut_Postfix)));
                if (fit != null && fit.ReturnType == typeof(bool)) harmony.Patch(fit, postfix: new HarmonyMethod(typeof(EpicLootTooltipBox), nameof(Fit_Postfix)));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[RuneSkin] {TypeName} patch failed (Epic Loot item tooltips keep its own padding): {e.Message}");
            }
        }

        private static void LayOut_Postfix(Component __instance, ref float __result)
        {
            if (!__instance) return;
            var scroll = __instance.GetComponentInChildren<ScrollRect>(true);
            var content = scroll ? scroll.content : null;
            if (!content || !content.TryGetComponent<ContentInset>(out var inset) || !inset.Reassert(out var grow)) return;
            if (grow > 0)
            {
                var box = (RectTransform)__instance.transform;
                box.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, box.rect.width + grow);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            __result = content.rect.height;
        }

        private static void Fit_Postfix(Component __instance, bool __result, ref Vector2 size)
        {
            if (__result && __instance) size.x = ((RectTransform)__instance.transform).rect.width;
        }
    }
}
