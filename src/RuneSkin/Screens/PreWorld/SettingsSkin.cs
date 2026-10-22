using System;
using HarmonyLib;
using RuneSkin.Api;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using RuneSkin.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.PreWorld
{
    // Family "Settings" (mockup m5-settings-a): the vanilla Settings window (instantiated from FejdStartup
    // m_settingsPrefab :2965 and Menu.OnSettings :414; Settings.Awake builds its tabs, Settings.cs:50) restyled in place
    // in its Awake (inside Instantiate, before the first frame): panel stack + header plaque, the tab buttons
    // (Settings.m_tabHandler: selected = non-interactable, TabHandler.SetActiveTab) as a vertical category column on the
    // left with the pages shifted right of it (pages keep their own scroll), controls by component (slider A, toggle =
    // checkbox, TMP_Dropdown A, key binding buttons as plates), Ok / Back as buttons (Ok = primary).
    public static class SettingsSkin
    {
        public const string ScreenId = "Settings";

        public static void Attach(Settings settings)
        {
            var root = settings.transform;
            // The window = root/Panel (title, tabs, pages, Ok / Back); its vanilla wood image becomes the panel stack.
            var panel = root.Find("Panel") as RectTransform;
            PreWorldKit.Panels(root, "size.panel.pad");
            MainMenuSkin.TabSelected(root);
            Column(settings, panel);
            GamepadInset(settings);
            ScreenRegistry.Register(root, () => Context(settings));
            ScreenRegistry.Restyle(root);
        }

        // Tab buttons stacked vertically in a column on the panel's left; the pages start right of it.
        private static void Column(Settings settings, RectTransform panel)
        {
            var handler = settings.m_tabHandler;
            if (!handler || !panel || handler.m_tabs.Count == 0) return;
            var first = handler.m_tabs[0].m_button ? handler.m_tabs[0].m_button.transform.parent as RectTransform : null;
            if (!first) return;
            foreach (var g in first.GetComponents<LayoutGroup>()) g.enabled = false;
            if (first.TryGetComponent<ContentSizeFitter>(out var fit)) fit.enabled = false;
            var w = ThemeTokens.GetFloat("size.settings.tab.w", 250f);
            var h = ThemeTokens.GetFloat("size.settings.tab.h", 52f);
            var gap = ThemeTokens.GetFloat("size.settings.tab.gap", 8f);
            var top = ThemeTokens.GetFloat("size.settings.top", 96f);
            var left = ThemeTokens.GetFloat("size.settings.left", 36f);
            Widen(panel, first, left + w + ThemeTokens.GetFloat("size.settings.page.gap", 24f));
            first.SetParent(panel, false);
            first.anchorMin = first.anchorMax = new Vector2(0f, 1f);
            first.pivot = new Vector2(0f, 1f);
            first.anchoredPosition = new Vector2(left, -top);
            first.sizeDelta = new Vector2(w, handler.m_tabs.Count * (h + gap));
            var index = 0;
            foreach (var tab in handler.m_tabs)
            {
                if (tab == null || !tab.m_button) continue;
                var rt = (RectTransform)tab.m_button.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -PreWorldMath.TabOffset(index++, h, gap));
                rt.sizeDelta = new Vector2(w, h);
                foreach (var t in rt.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (PreWorldKit.IsKeyHint(t.transform, rt)) continue;
                    t.alignment = TextAlignmentOptions.Center;
                    t.enableAutoSizing = false;
                    t.fontSize = ThemeTokens.GetFloat("size.settings.tab.font", 20f);
                    var tr = t.rectTransform;
                    tr.anchorMin = Vector2.zero;
                    tr.anchorMax = Vector2.one;
                    tr.offsetMin = tr.offsetMax = Vector2.zero;
                }
            }
        }

        // Controller page (the one holding the vanilla GamepadMap diagram): its rows touched the well's top edge and the
        // diagram's outer labels reached the rim (Codex M5 r4). The page content scales about its centre
        // (scale.settings.gamepad), leaving an even margin inside the well; vanilla layout inside untouched.
        private static void GamepadInset(Settings settings)
        {
            var handler = settings.m_tabHandler;
            if (!handler) return;
            var k = ThemeTokens.GetFloat("scale.settings.gamepad", 0.93f);
            foreach (var tab in handler.m_tabs)
            {
                var page = tab?.m_page;
                if (!page || page.GetComponent<LiftMark>()) continue;
                var map = false;
                foreach (var t in page.GetComponentsInChildren<Transform>(true))
                    if (t.name.IndexOf("GamepadMap", StringComparison.Ordinal) >= 0) { map = true; break; }
                if (!map) continue;
                page.gameObject.AddComponent<LiftMark>();
                var c = page.rect.center;
                page.localScale = new Vector3(k, k, 1f);
                page.localPosition += (Vector3)(c * (1f - k));
            }
        }

        // The window grows by s (both sides, it stays centred) and every child of it moves so that the content keeps its
        // size and sits right of a new s-wide band on the left, where the category column goes. A child anchored at x = a
        // moves by s(1 - a) (its anchor point already moved by (a - 0.5)s with the edges).
        private static void Widen(RectTransform panel, RectTransform column, float s)
        {
            if (panel.TryGetComponent<WidenMark>(out _)) return;
            panel.gameObject.AddComponent<WidenMark>().Shift = s;
            var children = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform t in panel)
                if (t is RectTransform r && r != column && !t.name.StartsWith("RuneSkin", StringComparison.Ordinal)) children.Add(r);
            panel.sizeDelta += new Vector2(s, 0f);
            foreach (var r in children)
            {
                if (Mathf.Approximately(r.anchorMin.x, r.anchorMax.x)) r.anchoredPosition += new Vector2(s * (1f - r.anchorMin.x), 0f);
                else
                {
                    r.offsetMin += new Vector2(s * (1f - r.anchorMin.x), 0f);
                    r.offsetMax += new Vector2(s * (1f - r.anchorMax.x), 0f);
                }
            }
        }

        private static ScreenContext Context(Settings settings)
        {
            var root = settings.transform;
            var ctx = new ScreenContext(ScreenId, OwnershipRegistry.Settings);
            if (settings.m_tabHandler)
                foreach (var tab in settings.m_tabHandler.m_tabs)
                    if (tab?.m_button && tab.m_button.targetGraphic) ctx.Role(tab.m_button.targetGraphic, "tab", "Settings tab button (category column)");
            MainMenuSkin.Primary(ctx, settings.m_okButton);
            var title = root.Find("Panel/Title");
            if (title && title.TryGetComponent<TMP_Text>(out var tt)) ctx.Role(tt, "text.header", "Settings title");
            PreWorldKit.Common(ctx, root);
            return ctx;
        }
    }

    // Keeps a graphic on the vertical centre of a text rect (laid out after Awake), change-only writes.
    public sealed class FollowTextCenter : MonoBehaviour
    {
        public RectTransform Text;

        private void LateUpdate()
        {
            if (!Text || !(transform is RectTransform rt) || !(rt.parent is RectTransform host)) return;
            var c = host.InverseTransformPoint(Text.TransformPoint(Text.rect.center));
            // Anchored y relative to the anchor point (anchors at y 0.5 of the host).
            var y = c.y - (host.rect.yMin + host.rect.height * rt.anchorMin.y);
            if (Mathf.Abs(rt.anchoredPosition.y - y) > 0.5f) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
        }
    }

    // Marks a Settings window already widened for the category column (Awake runs once per instance).
    public sealed class WidenMark : MonoBehaviour
    {
        public float Shift;
    }

    [HarmonyPatch]
    public static class SettingsAdapter
    {
        [HarmonyPatch(typeof(Settings), nameof(Settings.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(Settings __instance)
        {
            try { SettingsSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] settings restyle failed (vanilla settings stay): {e}"); }
        }
    }

    // Family "Connection": ZNet's password and connecting dialogs (ZNet.m_passwordDialog / m_connectingDialog, each a
    // root canvas of its own under LoadingGUI, shown by RPC_ClientHandshake ZNet.cs:910-918 and Connect :762/819).
    // Restyled in place (AugaSkin swapped prefabs here): panel stack, password field, buttons; the connecting dialog
    // gets the rune spinner (no real progress exists while connecting).
    public static class ConnectionSkin
    {
        public static void Attach(ZNet net)
        {
            Dialog(net.m_passwordDialog, "Password", false);
            Dialog(net.m_connectingDialog, "Connecting", true);
        }

        private static void Dialog(RectTransform dialog, string id, bool spinner)
        {
            if (!dialog) return;
            PreWorldKit.Panels(dialog, "size.panel.pad");
            if (spinner && !dialog.GetComponentInChildren<RuneSpinner>(true))
            {
                // The rune spinner left of the text (mockup m5-load-a) replaces the vanilla hammer icon (panel/loading).
                var host = dialog.Find("panel") as RectTransform ?? dialog;
                foreach (var img in dialog.GetComponentsInChildren<Image>(true))
                    if (img.name == "loading") PanelSwap.Apply(img, img.sprite ? img.sprite.name : null);
                var size = ThemeTokens.GetFloat("size.spinner.dialog", 44f);
                var s = RuneSpinner.Add(host, "RuneSkin_Spinner", size);
                var rt = s.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                // One row with the status text (Codex M5 r1: the spinner sat below it): same vertical centre.
                var text = dialog.GetComponentInChildren<TMPro.TMP_Text>(true);
                // Same vertical centre as the status line: the text is centred in the panel (vanilla layout; a rect
                // measured here in Awake is not laid out yet and put the spinner low, run 20261022-064153), so the
                // spinner sits on the panel's middle, clear of the corner caps on the left.
                if (text)
                {
                    text.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
                    text.fontSize = Mathf.Max(text.fontSize, ThemeTokens.GetFloat("size.connect.font", 20f));
                }
                rt.anchoredPosition = new Vector2(ThemeTokens.GetFloat("size.spinner.dialog.left", 34f), 0f);
                // The status line sits above the panel middle once laid out (run 20261022-071214: spinner ~20 ref
                // low): follow the laid-out text centre.
                if (text)
                {
                    var follow = s.gameObject.AddComponent<FollowTextCenter>();
                    follow.Text = text.rectTransform;
                }
                host.localScale = new Vector3(1.2f, 1.2f, 1f);
            }
            ScreenRegistry.Register(dialog, () =>
            {
                var ctx = new ScreenContext("Connection." + id, OwnershipRegistry.Connection);
                PreWorldKit.Common(ctx, dialog);
                return ctx;
            });
            ScreenRegistry.Restyle(dialog);
        }
    }

    [HarmonyPatch]
    public static class ConnectionAdapter
    {
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(ZNet __instance)
        {
            try { ConnectionSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] connection dialogs restyle failed (vanilla dialogs stay): {e}"); }
        }
    }
}
