namespace RuneSkin.Compat
{
    public enum FadeParkMove
    {
        None,
        Home, // back under its own host, where its mod shows it
        Park, // into the inactive holder outside the inventory's fade group
    }

    // Where another mod's hidden subtree lives (FadePark). Managed-only, unit-tested.
    public static class FadeParkMath
    {
        // shown = the mod's own visibility of the subtree (activeSelf), home = it sits under its host, hostInFade = the
        // host is inside InventoryGui (whose CanvasGroup the fade writes). Shown or no longer inside the fade group: home
        // (the mod's own hierarchy, nothing of it changes). Hidden at home inside the fade group: parked.
        public static FadeParkMove Decide(bool shown, bool home, bool hostInFade)
        {
            if (shown || !hostInFade) return home ? FadeParkMove.None : FadeParkMove.Home;
            return home ? FadeParkMove.Park : FadeParkMove.None;
        }
    }
}
