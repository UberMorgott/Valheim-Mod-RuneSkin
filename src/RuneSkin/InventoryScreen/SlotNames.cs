namespace RuneSkin.InventoryScreen
{
    // Tooltip names of doll cells (no visible labels on the doll): "$runeskin_*" keys live in the game's Localization
    // table (Theme.Words), anything else is a mod / vanilla key ("$item_*").
    public static class SlotNames
    {
        public static string Get(string key) => Theme.Words.Get(key);
    }
}
