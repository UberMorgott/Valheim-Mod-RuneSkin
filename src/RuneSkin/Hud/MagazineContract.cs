using System;
using System.Reflection;

namespace RuneSkin.PlayerHud
{
    // The crossbow magazine read contract (side-session note crossbow-magazine-ui-contract.md), the managed part
    // (unit-tested): MorgottTweaks' public static class MorgottTweaks.CrossbowMagazine bound to typed delegates, exact
    // signatures only - int Capacity(ItemData), int Loaded(ItemData), int Revision(). A member that is missing or has
    // another signature stays null (Complete false = no magazine readout; Revision null = re-read on the timers only).
    public sealed class MagazineBinding<TItem>
    {
        public Func<TItem, int> Capacity { get; internal set; }
        public Func<TItem, int> Loaded { get; internal set; }
        public Func<int> Revision { get; internal set; }

        public bool Complete => Capacity != null && Loaded != null;
    }

    public static class MagazineContract
    {
        public const string TypeName = "MorgottTweaks.CrossbowMagazine";

        public static MagazineBinding<TItem> Bind<TItem>(Type type)
        {
            var c = new MagazineBinding<TItem>();
            if (type == null) return c;
            c.Capacity = Make<Func<TItem, int>>(type, "Capacity", typeof(TItem));
            c.Loaded = Make<Func<TItem, int>>(type, "Loaded", typeof(TItem));
            c.Revision = Make<Func<int>>(type, "Revision");
            return c;
        }

        private static T Make<T>(Type type, string name, params Type[] args) where T : Delegate
        {
            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null);
            return m != null && m.ReturnType == typeof(int) ? (T)Delegate.CreateDelegate(typeof(T), m, false) : null;
        }
    }
}
