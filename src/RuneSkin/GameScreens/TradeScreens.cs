using System;
using HarmonyLib;
using RuneSkin.Classify;
using RuneSkin.Ownership;
using RuneSkin.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.GameScreens
{
    // M6 batch 3 (art\README.md "M6 sprites", mockups m6-trader-a, m6-dialogs-a; M5 rules for the rest): restyled in
    // place through the role pass like batch 2 (GameScreenKit.Form).
    // - Store (StoreGui: Haldor, Hildir, Bog Witch): panel stack, one item list in a well, rows row_recipe_* (selected
    //   = row_recipe_selected), icon in a slot_cell_recess socket, price + coin right, buy / sell = buttons (buy
    //   primary). Epic Loot's TemperPanel / MerchantPanel under the store are their own M7 families.
    // - Barber (PlayerCustomizaton barber instance; the character-creation one belongs to MainMenu): panel stack,
    //   colour sliders keep the vanilla gradient (handle art only), apply primary.
    // - DevPanels: the F5 console window (Console = Terminal: m_chatWindow, m_output, m_input) as a popup with the chat
    //   rules, and the F2 connect panel (ConnectPanel.m_root, player row template) as a panel.
    // - Feedback: the feedback dialog (Menu / FejdStartup instance), M5 form rules.
    public static class TradeScreens
    {
        // ---- Store ----

        public static void Store(StoreGui s)
        {
            var row = s.m_listElement ? s.m_listElement.transform : null;
            GameScreenKit.Form(s.transform, "Store", OwnershipRegistry.Store, (ctx, r) =>
            {
                ExcludeEpicLoot(ctx, r);
                if (row && row.IsChildOf(r)) ctx.Exclude(row, "store row template (own registration Store.Row)");
                foreach (var t in Children.Of(s.m_listRoot ? s.m_listRoot : r))
                    if (s.m_listRoot && t != row) ctx.Exclude(t, "store row instance (clone of the styled template)");
                ctx.Role(s.m_coinText, "text.body", "coin purse amount");
                GameScreenKit.Primary(ctx, s.m_buyButton, "buy (button_selected)");
                PopupSkin.Backdrops(ctx, r);
            });
            if (row) Row(row);
        }

        // Epic Loot's trader panels (TemperPanel at Hildir, MerchantPanel at Haldor): built under the StoreGui in its
        // Show finalizer; their own families (M7a, fallback units of ModScreens\ForeignUi).
        private static void ExcludeEpicLoot(ScreenContext ctx, Transform r)
        {
            foreach (var n in new[] { "TemperPanel", "MerchantPanel" })
                if (r.Find(n) is Transform t) ctx.Exclude(t, $"Epic Loot {n} (own family EpicLootBoard / EpicLootTemper, ModScreens\\ForeignRoles)");
        }

        private static void Row(Transform row)
        {
            GameScreenKit.Form(row, "Store.Row", OwnershipRegistry.Store, (ctx, r) =>
            {
                if (r.Find("bkg") is Transform b && b.TryGetComponent<Image>(out var bi)) ctx.Role(bi, "craft.row", "store row plate");
                else if (r.TryGetComponent<Button>(out var btn) && btn.targetGraphic is Image tg) ctx.Role(tg, "craft.row", "store row plate");
                if (r.Find("selected") is Transform sel && sel.TryGetComponent<Image>(out var si)) ctx.Role(si, "craft.row.selected", "store row selection");
                if (r.Find("icon") is Transform ic && ic.TryGetComponent<Image>(out var ii)) ctx.Role(ii, "content", "item icon");
                foreach (var img in r.GetComponentsInChildren<Image>(true))
                    if (!ctx.AdapterRoles.ContainsKey(img) && img.sprite && img.sprite.name.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0)
                        ctx.Role(img, "content", "coin icon");
                foreach (var t in r.GetComponentsInChildren<TMP_Text>(true))
                    ctx.Role(t, t.name == "name" ? "row.label" : "text.body", t.name == "name" ? "item name" : "price / quantity");
                PopupSkin.Backdrops(ctx, r);
            }, panels: false);
        }

        public static void StoreRefresh(StoreGui s)
        {
            GameScreenKit.Restyle(s);
            // Rows cloned before the template was styled (mod rows) take the look now.
            if (s.m_listRoot)
                foreach (var t in Children.Of(s.m_listRoot))
                    if (t && !(s.m_listElement && t == s.m_listElement.transform) && Api.ScreenRegistry.ContextOf(t) == null) Row(t);
        }

        // ---- Barber ----

        public static bool IsBarber(PlayerCustomizaton p) => p && !p.GetComponentInParent<FejdStartup>(true);

        public static void Barber(PlayerCustomizaton p)
        {
            GameScreenKit.Form(p.transform, "Barber", OwnershipRegistry.Barber, (ctx, r) =>
            {
                // Colour sliders: vanilla gradient tracks stay (mockup m6-dialogs-a), only the handles take the art.
                foreach (var s in r.GetComponentsInChildren<Slider>(true))
                    foreach (var img in s.GetComponentsInChildren<Image>(true))
                        if (s.handleRect != img.rectTransform && s.targetGraphic != img) ctx.Role(img, "content", "barber colour slider gradient (vanilla)");
                // Panel title (BarberGui/root/title, 20 px): under size.text.header.min, so the classifier took it for
                // body text; panel titles are the rune face (0.7.1 rule, text.header).
                foreach (var t in r.GetComponentsInChildren<TMP_Text>(true))
                    if (t.name == "title" && !t.GetComponentInParent<Selectable>(true)) ctx.Role(t, "text.header", "barber panel title (rune face)");
                ctx.Role(p.m_selectedHair, "text.body", "selected hair name");
                ctx.Role(p.m_selectedBeard, "text.body", "selected beard name");
                GameScreenKit.Primary(ctx, p.m_apply, "apply (button_selected)");
                PopupSkin.Backdrops(ctx, r);
            });
        }

        // ---- Dev panels ----

        public static void Console(Console c)
        {
            var window = c.m_chatWindow ? c.m_chatWindow : (RectTransform)c.transform;
            GameScreenKit.Form(window, "DevPanels.Console", OwnershipRegistry.DevPanels, (ctx, r) =>
            {
                foreach (var img in r.GetComponentsInChildren<Image>(true))
                {
                    if (img.GetComponentInParent<Selectable>(true) || img.GetComponentInParent<Scrollbar>(true)) continue;
                    if (img.transform == r || (img.transform.parent == r && img.rectTransform.anchorMin == Vector2.zero && img.rectTransform.anchorMax == Vector2.one))
                        ctx.Role(img, "popup", "console window backdrop (popup)");
                }
                ctx.Role(c.m_output, "text.body", "console log");
                ctx.Role(c.m_devTest, "text.muted", "console dev line");
                PopupSkin.Backdrops(ctx, r);
            }, panels: false);
            if (c.m_output)
            {
                var pad = Theme.ThemeTokens.GetFloat("size.chat.pad", 14f);
                c.m_output.margin = new Vector4(pad, pad * 0.5f, pad, pad * 0.5f);
            }
        }

        public static void Connect(ConnectPanel p)
        {
            var root = p.m_root ? p.m_root : p.transform;
            var row = p.m_playerElement ? p.m_playerElement.transform : null;
            GameScreenKit.Form(root, "DevPanels.Connect", OwnershipRegistry.DevPanels, (ctx, r) =>
            {
                if (row && row.IsChildOf(r)) ctx.Exclude(row, "player row template (own registration)");
                PopupSkin.Backdrops(ctx, r);
            });
            if (row) GameScreenKit.Form(row, "DevPanels.ConnectRow", OwnershipRegistry.DevPanels, (ctx, r) => PopupSkin.Backdrops(ctx, r), panels: false);
            // The vanilla panel sits flush in the top-left corner; the panel stack reaches the pad beyond it.
            OnScreen.Ensure(root as RectTransform);
        }

        // ---- Feedback ----

        public static void Feedback(Feedback f) =>
            GameScreenKit.Form(f.transform, "Feedback", OwnershipRegistry.Feedback, (ctx, r) =>
            {
                GameScreenKit.Primary(ctx, f.m_sendButton, "send (button_selected)");
                foreach (var img in r.GetComponentsInChildren<Image>(true))
                    if (img.sprite && img.sprite.name == "mail") ctx.Role(img, "content", "mail glyph (vanilla)");
                PopupSkin.Backdrops(ctx, r);
            });
    }

    [HarmonyPatch]
    public static class TradeScreensAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => AnyOwned();

        private static readonly string[] Families = { OwnershipRegistry.Store, OwnershipRegistry.Barber, OwnershipRegistry.DevPanels, OwnershipRegistry.Feedback };

        public static bool AnyOwned() => Array.Exists(Families, OwnershipRegistry.IsOwned);

        private static void Try(string what, string family, Action attach)
        {
            if (!GameScreenKit.Owned(family)) return;
            try { attach(); }
            catch (Exception e) { Plugin.Log.LogError($"[RuneSkin] {what} restyle failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Store_Postfix(StoreGui __instance) => Try("store", OwnershipRegistry.Store, () => TradeScreens.Store(__instance));

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void StoreFill_Postfix(StoreGui __instance) => Try("store rows", OwnershipRegistry.Store, () => TradeScreens.StoreRefresh(__instance));

        [HarmonyPatch(typeof(PlayerCustomizaton), nameof(PlayerCustomizaton.OnEnable))]
        [HarmonyPostfix]
        private static void Barber_Postfix(PlayerCustomizaton __instance)
        {
            if (!TradeScreens.IsBarber(__instance) || Api.ScreenRegistry.ContextOf(__instance.transform) != null) return;
            Try("barber", OwnershipRegistry.Barber, () => TradeScreens.Barber(__instance));
        }

        [HarmonyPatch(typeof(Console), nameof(Console.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Console_Postfix(Console __instance) => Try("console", OwnershipRegistry.DevPanels, () => TradeScreens.Console(__instance));

        [HarmonyPatch(typeof(ConnectPanel), nameof(ConnectPanel.Start))]
        [HarmonyPostfix]
        private static void Connect_Postfix(ConnectPanel __instance) => Try("connect panel", OwnershipRegistry.DevPanels, () => TradeScreens.Connect(__instance));

        [HarmonyPatch(typeof(Feedback), nameof(Feedback.Awake))]
        [HarmonyPostfix]
        private static void Feedback_Postfix(Feedback __instance) => Try("feedback", OwnershipRegistry.Feedback, () => TradeScreens.Feedback(__instance));
    }
}
