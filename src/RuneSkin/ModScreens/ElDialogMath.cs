using System;

namespace RuneSkin.ModScreens
{
    // Pure math of the Epic Loot modal dialogs (ElDialogs; unit tested).
    public static class ElDialogMath
    {
        // Augment choice buttons (Epic Loot AugmentHelper.CreateAugmentChoiceDialog: 300 wide, centred, in a 380 frame)
        // take the frame's inner width: frame minus the corner-cap keep-out on both sides. Never narrower than Epic
        // Loot's own width (a frame without a panel stack keeps it).
        public static float ChoiceWidth(float current, float frameWidth, float keepOut)
        {
            var inner = frameWidth - 2f * Math.Max(0f, keepOut);
            return inner > current ? inner : current;
        }

        // A RootMark on an object that is no registered root came with Instantiate from a RuneSkin root (Epic Loot clones
        // the restyled vanilla variant dialog): it is no root, and as a "nested root" it would keep the owning unit's pass
        // off the whole dialog.
        public static bool IsStaleRootMark(bool hasMark, bool registeredRoot) => hasMark && !registeredRoot;
    }
}
