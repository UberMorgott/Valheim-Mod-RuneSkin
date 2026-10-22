namespace RuneSkin
{
    // ConfigurationManager (BepInEx.ConfigurationManager, dev / tuning tool, not shipped in the pack) reads these fields
    // by name from a ConfigDescription tag; no reference to it. Field names as its documented contract (same class as
    // Jotunn's JotunnLib\Utils\ConfigurationManagerAttributes.cs); only the ones RuneSkin sets.
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
    }
}
