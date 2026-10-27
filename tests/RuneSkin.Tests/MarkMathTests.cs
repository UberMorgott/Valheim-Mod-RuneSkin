using RuneSkin.Restyle;
using Xunit;

namespace RuneSkin.Tests
{
    // SkinMark.Own plan (MarkMath): one mark per object. 0.25.12 RC run 20261027-163702: ActionBar.Own marked a button
    // background with "action.button", ButtonRole("action.button.header") marked it again -> AddComponent<SkinMark>
    // returned null ([DisallowMultipleComponent]) -> NullReferenceException in DollCell.Mark.
    public class MarkMathTests
    {
        [Fact]
        public void FirstMarkAdds() => Assert.Equal(MarkStep.Add, MarkMath.Plan(hasMark: false, ownMark: false));

        [Fact]
        public void SecondMarkOfTheSameObjectReusesNeverAdds()
        {
            // ActionBar.Own -> DollCell.Mark(bg, DefaultRole): mark added.
            var hasMark = false;
            Assert.Equal(MarkStep.Add, MarkMath.Plan(hasMark, ownMark: false));
            hasMark = true;
            // ActionBar.ButtonRole(HeaderRole) -> DollCell.Mark(bg, HeaderRole): the object's own mark comes back.
            Assert.Equal(MarkStep.Reuse, MarkMath.Plan(hasMark, ownMark: true));
            // Back to the default role (theme without plinth art), and every later relayout: still the one mark.
            Assert.Equal(MarkStep.Reuse, MarkMath.Plan(hasMark, ownMark: true));
        }

        [Fact]
        public void MarkCopiedByInstantiateIsAdoptedNotDuplicated() =>
            Assert.Equal(MarkStep.Rebind, MarkMath.Plan(hasMark: true, ownMark: false));
    }
}
