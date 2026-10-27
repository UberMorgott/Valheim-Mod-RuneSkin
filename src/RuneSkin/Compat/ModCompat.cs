namespace RuneSkin.Compat
{
    // Every other mod RuneSkin adapts to, by name (perf audit B5: these names were spread over a dozen files). All soft:
    // no reference, nothing breaks when a mod is absent or renames a type / object (the hook finds nothing, logs, and
    // the vanilla look stays). Third-party mods are matched here; own mods register through RuneSkin.API where the API
    // has the capability (AdventureBackpacks fork: ABAPI.GetBackpackPanel + Skin.RegisterPanel("inventory.backpack"),
    // MorgottTweaks: Skin.RegisterPanelAction / RegisterResourceProvider) and are listed here only for what the API does
    // not carry yet (object names their UI is placed by, ZDO keys they publish).
    public static class ModCompat
    {
        // ---- skins and frameworks
        public const string ValheimPlusGuid = "org.bepinex.plugins.valheim_plus";
        public const string JotunnAssembly = "Jotunn";
        public const string JotunnGuiManager = "Jotunn.Managers.GUIManager";

        // ---- Equipment and Quick Slots (randyknapp): doll cells read from its slot table
        public const string EaqsGuid = "randyknapp.mods.equipmentandquickslots";
        public const string EaqsSlotsType = "EquipmentAndQuickSlots.Slots";
        // Its pool of unused cells (EquipmentPanel.InventoryGrid_UpdateGui_RelocateSlotElements.EnsureHiddenRoot: an inactive RectTransform under
        // m_player, kept by its static reference, cells parked into it by ParkHiddenCell).
        public const string EaqsHiddenRoot = "EaqsHiddenSlotRoot";

        // ---- VNEI (MSchmoecker): item browser docked over the crafting body
        public const string VneiGuid = "com.maxsch.valheim.vnei";
        public const string VneiBaseUi = "VNEI.UI.BaseUI";
        public const string VneiPanel = "VNEI(Clone)";

        // ---- Almanac (RustyMods, our fork): window restyled in place
        public const string AlmanacGuid = "RustyMods.Almanac";
        public const string AlmanacPanelType = "Almanac.UI.AlmanacPanel";
        public const string AlmanacRoot = "Almanac";

        // ---- Epic Loot (RandyKnapp)
        public const string EpicLootGuid = "randyknapp.mods.epicloot";
        public const string EpicLootAssembly = "EpicLoot";
        public const string EpicLootEnchantingUi = "EpicLoot_UnityLib.EnchantingTableUI";
        public const string EpicLootMerchantPanel = "EpicLoot.Adventure.MerchantPanel";
        public const string EpicLootTemperPanel = "EpicLoot.TemperPanel";
        public const string EpicLootAbilityBar = "EpicLoot.Abilities.AbilityBar";
        public const string EpicLootDebugText = "EpicLoot.DebugText";
        public const string EpicLootSocketsUi = "EpicLoot.ShardStones.SocketsUI";
        // 0.15 TraderPanelDrag (EpicLoot\TraderPanelDrag.cs) on MerchantPanel / TemperPanel: a clear full-panel hit
        // surface shown while the drag key is held, and a corner move handle whose colour it writes every frame.
        public const string EpicLootDragSurface = "ModifierDragSurface";
        public const string EpicLootDragHandle = "MoveHandle";

        // ---- CurrencyPocket: coin plate in the inventory footer
        public const string CoinPocketPlate = "CoinPocketUI";

        // ---- own mods: what the API does not carry yet
        // AdventureBackpacks fork 1.9.15+: docking API (AbApi); no name fallback.
        public const string AbApiType = "AdventureBackpacks.API.ABAPI";
        // MorgottTweaks: footer trash plate, CraftingFilter row / search / tabs, RecipeChains box, ZDO keys it publishes.
        public const string MtTrashPlate = "MT_Trash";
        public const string MtCraftFilter = "MT_CraftFilter";
        public const string MtCraftSearch = "MT_CraftSearch";
        public const string MtCraftTabPrefix = "MT_CraftTab_";
        public const string MtCategoryIcon = "MT_Icon";
        // CraftingFilter sub-group row (MorgottTweaks 1.128.0): MT_CraftFilter/Subs/MT_CraftSub_<group>, first = All.
        public const string MtCraftSubRow = "Subs";
        public const string MtCraftSubPrefix = "MT_CraftSub_";
        public const string MtChainTree = "MT_ChainTree";
        public const string MtChainCraftButton = "MT_ChainCraftButton";
        public const string MtGuid = "morgott.valheim.tweaks";
        // Vaettir (was BossAwakening before 0.14.0): rarity frame child per enemy HUD (its tier sprite), its plugin id.
        public const string VaettirFrame = "VT_Frame";
        public const string VaettirGuid = "morgott.valheim.vaettir";
        public const string StarLevelSystemGuid = "MidnightsFX.StarLevelSystem";
    }
}
