using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class SeatExchangeTests
    {
        private static SeatExchange Seated()
        {
            var seats = new SeatExchange();
            seats.SetBothSeated(true);
            return seats;
        }

        [Fact]
        public void ConfirmationRequiresTheOtherPlayerAndCurrentRevision()
        {
            var s = Seated();
            uint initial = s.Revision;
            Assert.True(s.Press(1, initial, 5));
            Assert.Equal(1, s.Driver);
            Assert.False(s.Press(2, initial, 6));
            Assert.True(s.Press(2, s.Revision, 6));
            Assert.Equal(2, s.Driver);
            Assert.Equal(0, s.Requester);
        }

        [Fact]
        public void RequestCanBeCancelledAndDriverCanSwapBack()
        {
            var s = Seated();
            s.Press(2, s.Revision, 1);
            s.Press(2, s.Revision, 2);
            Assert.Equal(0, s.Requester);
            Assert.Equal(1, s.Driver);
            s.Press(2, s.Revision, 3);
            s.Press(1, s.Revision, 4);
            Assert.Equal(2, s.Driver);
            s.Press(1, s.Revision, 5);
            s.Press(2, s.Revision, 6);
            Assert.Equal(1, s.Driver);
        }

        [Fact]
        public void ExpiredConsentCannotChangeDriver()
        {
            var s = Seated();
            s.Press(1, s.Revision, 1);
            uint requestRevision = s.Revision;
            Assert.False(s.Press(2, requestRevision, 11));
            Assert.Equal(0, s.Requester);
            Assert.Equal(1, s.Driver);
            Assert.False(s.Press(2, requestRevision, 12));
        }

        [Fact]
        public void LeavingOrDisconnectInvalidatesRequestsAndRestoresLocalDriver()
        {
            var s = Seated();
            s.Press(1, s.Revision, 1);
            uint revision = s.Revision;
            s.SetBothSeated(false);
            Assert.False(s.Press(2, revision, 2));
            Assert.Equal(0, s.Requester);
            Assert.Equal(1, s.Driver);
            Assert.False(s.Press(2, s.Revision, 3));
        }

        [Theory]
        [InlineData(0, 1d)]
        [InlineData(3, 1d)]
        [InlineData(1, double.NaN)]
        [InlineData(2, double.PositiveInfinity)]
        public void InvalidIdentityAndClockCannotMutateState(byte player, double now)
        {
            var s = Seated();
            uint revision = s.Revision;
            Assert.False(s.Press(player, revision, now));
            Assert.Equal(revision, s.Revision);
        }
    }
}
