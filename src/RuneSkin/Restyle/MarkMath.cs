namespace RuneSkin.Restyle
{
    // How SkinMark.Own treats an object (pure, unit-tested). SkinMark is [DisallowMultipleComponent]: a second
    // AddComponent<SkinMark> on an object that already carries one returns null (0.25.12 RC run 20261027-163702: the
    // inventory action bar re-marked its button backgrounds for the header plinth role -> NullReferenceException in
    // DollCell.Mark every LateUpdate). One mark per object, owned by SkinMark.Own: add it once, then reuse it.
    public enum MarkStep
    {
        Add,    // no mark yet
        Reuse,  // the object's own mark
        Rebind  // a mark Instantiate copied from a template (journal = the template's true originals): adopt it
    }

    public static class MarkMath
    {
        public static MarkStep Plan(bool hasMark, bool ownMark) => !hasMark ? MarkStep.Add : ownMark ? MarkStep.Reuse : MarkStep.Rebind;
    }
}
