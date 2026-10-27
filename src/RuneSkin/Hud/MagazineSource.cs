using System;
using HarmonyLib;

namespace RuneSkin.PlayerHud
{
    // Crossbow magazine data from MorgottTweaks (soft, no assembly reference): its public read API
    // MorgottTweaks.CrossbowMagazine (Capacity(ItemData) / Loaded(ItemData): 0 = not a magazine crossbow; Revision():
    // bumped on every loaded-count or MagazineSize change), bound once to typed delegates like StatsSources. Contract:
    // side-session note crossbow-magazine-ui-contract.md. Without MorgottTweaks (or with its magazine off) no item is a
    // magazine: no dots, no plate readout.
    public static class MagazineSource
    {
        private static Func<ItemDrop.ItemData, int> _capacity, _loaded;
        private static Func<int> _revision;
        private static bool _bound;

        // Capacity asks Epic Loot (magazine affix; it rebuilds magic data per read): cached per item, re-read on a revision
        // change or after this many seconds (an enchant changes the affix without a revision bump).
        public const float CapacityTtl = 2f;

        public static bool Available
        {
            get
            {
                Bind();
                return _capacity != null && _loaded != null;
            }
        }

        // MorgottTweaks' change counter; 0 without it.
        public static int Revision
        {
            get
            {
                Bind();
                if (_revision == null) return 0;
                try { return _revision(); }
                catch (Exception e) { return Fail(e); }
            }
        }

        public static bool Read(ItemDrop.ItemData item, out int loaded, out int capacity)
        {
            loaded = capacity = 0;
            if (item == null || !Available) return false;
            try
            {
                capacity = _capacity(item);
                if (capacity <= 0) return false;
                loaded = _loaded(item);
                return true;
            }
            catch (Exception e)
            {
                Fail(e);
                return false;
            }
        }

        // Loaded count only (no Epic Loot call): per frame for a shown item.
        public static int Loaded(ItemDrop.ItemData item)
        {
            if (item == null || !Available) return 0;
            try { return _loaded(item); }
            catch (Exception e) { return Fail(e); }
        }

        private static int Fail(Exception e)
        {
            Plugin.Log.LogWarning($"[RuneSkin] crossbow magazine read failed, magazine readout off: {e.GetBaseException().Message}");
            _capacity = _loaded = null;
            _revision = null;
            return 0;
        }

        private static void Bind()
        {
            if (_bound) return;
            _bound = true;
            // Version probe: an older MorgottTweaks (or none) has no CrossbowMagazine -> no magazine readout.
            var type = AccessTools.TypeByName(MagazineContract.TypeName);
            if (type == null)
            {
                Plugin.Log.LogInfo("[RuneSkin] crossbow magazine source: MorgottTweaks.CrossbowMagazine absent (no magazine readout)");
                return;
            }
            var c = MagazineContract.Bind<ItemDrop.ItemData>(type);
            _capacity = c.Capacity;
            _loaded = c.Loaded;
            _revision = c.Revision;
            Plugin.Log.LogInfo($"[RuneSkin] crossbow magazine source: capacity={_capacity != null} loaded={_loaded != null} revision={_revision != null}");
        }
    }
}
