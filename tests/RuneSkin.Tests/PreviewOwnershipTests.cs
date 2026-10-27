using RuneSkin.InventoryScreen;
using Xunit;

namespace RuneSkin.Tests
{
    // Character preview clone ownership (CharacterPreview.ClaimClone): runtime objects seen on the clone are claimed once,
    // baseline (pre-build) and engine-internal objects never.
    public class PreviewOwnershipTests
    {
        private static PreviewOwnership WithBaseline(params int[] ids)
        {
            var o = new PreviewOwnership();
            o.SetBaseline(ids);
            return o;
        }

        [Fact]
        public void NothingIsClaimedWithoutABaseline()
        {
            var o = new PreviewOwnership();
            Assert.False(o.Claim(-500, false));
            Assert.Equal(0, o.Owned);
        }

        [Fact]
        public void BaselineObjectsAreNeverClaimed()
        {
            var o = WithBaseline(10, 20, -30);
            Assert.False(o.Claim(10, false));
            Assert.False(o.Claim(-30, false));
            Assert.Equal(0, o.Owned);
        }

        [Fact]
        public void RuntimeObjectIsClaimedOnceWhateverItsName()
        {
            var o = WithBaseline(10, 20);
            Assert.True(o.Claim(-40, false)); // e.g. a new Material(src) copy that keeps the source name
            Assert.False(o.Claim(-40, false)); // seen again on the next refresh / teardown: already owned
            Assert.True(o.IsOwned(-40));
            Assert.Equal(1, o.Owned);
        }

        [Fact]
        public void EngineInternalObjectsAreNeverClaimed()
        {
            var o = WithBaseline(10);
            Assert.False(o.Claim(-41, true));
            Assert.False(o.IsOwned(-41));
        }

        [Fact]
        public void NewBaselineAndResetForgetOwnership()
        {
            var o = WithBaseline(10);
            Assert.True(o.Claim(-42, false));
            o.SetBaseline(new[] { 10, -42 });
            Assert.False(o.IsOwned(-42));
            Assert.False(o.Claim(-42, false)); // part of the new baseline
            o.Reset();
            Assert.False(o.HasBaseline);
            Assert.False(o.Claim(-43, false));
        }
    }
}
