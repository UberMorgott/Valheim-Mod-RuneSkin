using System.Collections;
using HarmonyLib;
using RuneSkin.Ownership;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.ModScreens
{
    // Main menu lines and buttons of other mods (family ModUi; the menu itself is MainMenu's). Their texts are styled by
    // MainMenu's role pass (legacy lines) or the fallback restyler (Jotunn corner buttons in CustomGUIFront); here only
    // the placement AugaSkin used to fix: a second after Start (Jotunn's GUI and StarLevelSystem's "Mod Config" corner
    // button exist by then), the version label (DisplayBepInExInfo / ValheimPlus add lines to it) is right-aligned with
    // overflow allowed and moved left clear of any foreign button it overlaps. The 1.0.7 cinematics button stays hidden
    // (user request; no field, found by its persistent onClick -> OnCinematics).
    [HarmonyPatch]
    public static class MainMenuMods
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => Owned();

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Awake))]
        [HarmonyPostfix]
        private static void Awake_Postfix(FejdStartup __instance)
        {
            if (!__instance) return;
            foreach (var button in __instance.GetComponentsInChildren<Button>(true))
            {
                var onClick = button.onClick;
                for (var i = 0; i < onClick.GetPersistentEventCount(); i++)
                    if (onClick.GetPersistentMethodName(i) == nameof(FejdStartup.OnCinematics))
                    {
                        button.gameObject.SetActive(false);
                        break;
                    }
            }
        }

        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.MainMenuMods)]
        private static void Start_Postfix(FejdStartup __instance)
        {
            if (__instance && __instance.m_versionLabel) __instance.StartCoroutine(ClearVersionLabel(__instance, __instance.m_versionLabel));
        }

        private static IEnumerator ClearVersionLabel(FejdStartup fejd, TMP_Text label)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (!label) yield break;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.BottomRight;
            label.ForceMeshUpdate();
            var b = label.textBounds;
            var min = label.rectTransform.TransformPoint(b.min);
            var max = label.rectTransform.TransformPoint(b.max);
            var bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var shift = 0f;
            var corners = new Vector3[4];
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (!button.isActiveAndEnabled || button.transform.IsChildOf(fejd.transform)) continue;
                ((RectTransform)button.transform).GetWorldCorners(corners);
                var r = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                if (r.Overlaps(bounds)) shift = Mathf.Max(shift, bounds.xMax - r.xMin);
            }
            if (shift <= 0f) yield break;
            var scale = label.canvas ? label.canvas.scaleFactor : 1f;
            label.rectTransform.anchoredPosition -= new Vector2(shift / scale + 8f, 0f);
            Plugin.Log.LogInfo($"[RuneSkin] version label moved {shift / scale + 8f:0} units left of a foreign corner button");
        }

        public static bool Owned() => OwnershipRegistry.IsOwned(OwnershipRegistry.ModUi);
    }
}
