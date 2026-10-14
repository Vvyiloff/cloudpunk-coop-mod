using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class VehicleOccupancyTests
    {
        [Fact]
        public void InitialStateHasEmptyCarsAndPlayersOnFoot()
        {
            var s = new VehicleOccupancy();
            Assert.Equal(0u, s.Revision);
            Assert.Equal(0, s.Player1Vehicle);
            Assert.Equal(0, s.Player2Vehicle);
            Assert.Equal(0, s.Driver1);
            Assert.Equal(0, s.Driver2);
            AssertNoConsent(s);
            Assert.False(s.Expire(100));
            s.DisconnectGuest();
            Assert.Equal(0u, s.Revision);
        }

        [Theory]
        [InlineData(1, 2, 1)]
        [InlineData(2, 1, 1)]
        [InlineData(1, 2, 2)]
        [InlineData(2, 1, 2)]
        public void SameRevisionBoardingIsSerialized(byte first, byte second, byte vehicle)
        {
            var s = new VehicleOccupancy();
            uint revision = s.Revision;
            Assert.True(s.SetPlayerVehicle(first, vehicle, revision));
            Assert.False(s.SetPlayerVehicle(second, vehicle, revision));
            Assert.Equal(first, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(second, vehicle, s.Revision));
            Assert.Equal(first, Driver(s, vehicle));
            Assert.Equal(vehicle, s.Player1Vehicle);
            Assert.Equal(vehicle, s.Player2Vehicle);
            Assert.Equal(2u, s.Revision);
            AssertInvariants(s);
        }

        [Theory]
        [InlineData(1, 2, 1)]
        [InlineData(2, 1, 1)]
        [InlineData(1, 2, 2)]
        [InlineData(2, 1, 2)]
        public void PassengerReboardingPreservesDriverAndDriverLeavingPromotesPassenger(
            byte driver, byte passenger, byte vehicle)
        {
            var s = Shared(driver, vehicle);
            Assert.True(s.SetPlayerVehicle(passenger, 0, s.Revision));
            Assert.Equal(driver, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(passenger, vehicle, s.Revision));
            Assert.Equal(driver, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(driver, 0, s.Revision));
            Assert.Equal(passenger, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(driver, vehicle, s.Revision));
            Assert.Equal(passenger, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(driver, 0, s.Revision));
            Assert.True(s.SetPlayerVehicle(passenger, 0, s.Revision));
            Assert.Equal(0, Driver(s, vehicle));
            Assert.True(s.SetPlayerVehicle(driver, vehicle, s.Revision));
            Assert.Equal(driver, Driver(s, vehicle));
            AssertInvariants(s);
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        public void DistinctCarsCanBeDrivenRegardlessOfOwnership(byte hostVehicle, byte guestVehicle)
        {
            var s = new VehicleOccupancy();
            Assert.True(s.SetPlayerVehicle(1, hostVehicle, s.Revision));
            Assert.True(s.SetPlayerVehicle(2, guestVehicle, s.Revision));
            Assert.Equal(1, Driver(s, hostVehicle));
            Assert.Equal(2, Driver(s, guestVehicle));
            Assert.False(s.PressSwap(1, s.Revision, 0));
            Assert.True(s.SetPlayerVehicle(1, guestVehicle, s.Revision));
            Assert.Equal(0, Driver(s, hostVehicle));
            Assert.Equal(2, Driver(s, guestVehicle));
            AssertInvariants(s);
        }

        [Fact]
        public void StaleReplayCannotReclaimPreviousDriverSeat()
        {
            var s = Shared(1, 1);
            uint old = s.Revision;
            Assert.True(s.SetPlayerVehicle(1, 0, old));
            uint current = s.Revision;
            Assert.False(s.SetPlayerVehicle(1, 1, old));
            Assert.Equal(current, s.Revision);
            Assert.Equal(0, s.Player1Vehicle);
            Assert.Equal(2, s.Driver1);
            Assert.True(s.SetPlayerVehicle(1, 1, current));
            Assert.Equal(2, s.Driver1);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(3, 1)]
        [InlineData(255, 1)]
        [InlineData(1, 3)]
        [InlineData(2, 255)]
        public void InvalidBoardingDoesNotMutateState(byte player, byte vehicle)
        {
            var s = Shared(2, 2);
            Assert.True(s.PressSwap(1, s.Revision, 5));
            uint revision = s.Revision;
            Assert.False(s.SetPlayerVehicle(player, vehicle, revision));
            Assert.Equal(revision, s.Revision);
            Assert.Equal(2, s.Driver2);
            Assert.Equal(1, s.Requester);
            Assert.Equal(15d, s.ExpiresAt);
            AssertInvariants(s);
        }

        [Fact]
        public void DuplicateBoardingPreservesRevisionAndConsent()
        {
            var s = Shared(1, 1);
            s.PressSwap(1, s.Revision, 5);
            uint revision = s.Revision;
            Assert.False(s.SetPlayerVehicle(2, 1, revision));
            Assert.Equal(revision, s.Revision);
            Assert.Equal(1, s.Requester);
            Assert.True(s.PressSwap(2, revision, 6));
            Assert.Equal(2, s.Driver1);
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(1, 2, 2)]
        [InlineData(2, 1, 2)]
        [InlineData(2, 2, 1)]
        public void MutualConsentSwapsOnlyOnSecondPlayerAndCanSwapBack(
            byte initialDriver, byte requester, byte vehicle)
        {
            var s = Shared(initialDriver, vehicle);
            uint initial = s.Revision;
            byte other = requester == 1 ? (byte)2 : (byte)1;
            Assert.True(s.PressSwap(requester, initial, 5));
            Assert.Equal(initial + 1, s.Revision);
            Assert.Equal(initialDriver, Driver(s, vehicle));
            Assert.Equal(requester, s.Requester);
            Assert.Equal(vehicle, s.RequestedVehicle);
            Assert.Equal(15d, s.ExpiresAt);
            Assert.False(s.PressSwap(other, initial, 6));
            Assert.Equal(requester, s.Requester);
            Assert.True(s.PressSwap(other, s.Revision, 6));
            Assert.Equal(initialDriver == 1 ? 2 : 1, Driver(s, vehicle));
            AssertNoConsent(s);
            Assert.True(s.PressSwap(other, s.Revision, 7));
            Assert.True(s.PressSwap(requester, s.Revision, 8));
            Assert.Equal(initialDriver, Driver(s, vehicle));
            AssertInvariants(s);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void SamePlayerCancelsWithoutSwapping(byte player)
        {
            var s = Shared(2, 1);
            Assert.True(s.PressSwap(player, s.Revision, 1));
            uint revision = s.Revision;
            Assert.True(s.PressSwap(player, revision, 2));
            Assert.Equal(revision + 1, s.Revision);
            Assert.Equal(2, s.Driver1);
            AssertNoConsent(s);
        }

        [Fact]
        public void ExpirationIncludesDeadlineAndIsIdempotent()
        {
            var s = Shared(1, 2);
            s.PressSwap(1, s.Revision, 5);
            uint revision = s.Revision;
            Assert.False(s.Expire(14.999));
            Assert.Equal(revision, s.Revision);
            Assert.True(s.Expire(15));
            Assert.Equal(revision + 1, s.Revision);
            AssertNoConsent(s);
            Assert.False(s.Expire(16));
            Assert.False(s.PressSwap(2, revision, 16));
            Assert.Equal(1, s.Driver2);
        }

        [Fact]
        public void ConfirmationAtTimeoutClearsConsentButDoesNotSwapOrStartAnotherRequest()
        {
            var s = Shared(2, 2);
            s.PressSwap(1, s.Revision, 1);
            uint revision = s.Revision;
            Assert.False(s.PressSwap(2, revision, 11));
            Assert.Equal(revision + 1, s.Revision);
            Assert.Equal(2, s.Driver2);
            AssertNoConsent(s);
            Assert.False(s.PressSwap(2, revision, 12));
            Assert.True(s.PressSwap(2, s.Revision, 12));
            Assert.Equal(2, s.Driver2);
        }

        [Theory]
        [InlineData(0, 1d)]
        [InlineData(3, 1d)]
        [InlineData(1, double.NaN)]
        [InlineData(2, double.PositiveInfinity)]
        [InlineData(1, double.NegativeInfinity)]
        public void InvalidSwapInputCannotMutateConsent(byte player, double now)
        {
            var s = Shared(1, 1);
            s.PressSwap(1, s.Revision, 0);
            uint revision = s.Revision;
            Assert.False(s.PressSwap(player, revision, now));
            Assert.Equal(revision, s.Revision);
            Assert.Equal(1, s.Requester);
            Assert.Equal(1, s.Driver1);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void InvalidExpiryClockCannotMutateConsent(double now)
        {
            var s = Shared(1, 1);
            s.PressSwap(1, s.Revision, 0);
            uint revision = s.Revision;
            Assert.False(s.Expire(now));
            Assert.Equal(revision, s.Revision);
            Assert.Equal(1, s.Requester);
        }

        [Fact]
        public void SwapRequiresBothPlayersInsideTheSameCar()
        {
            var s = new VehicleOccupancy();
            Assert.False(s.PressSwap(1, s.Revision, 0));
            s.SetPlayerVehicle(1, 1, s.Revision);
            Assert.False(s.PressSwap(1, s.Revision, 0));
            s.SetPlayerVehicle(2, 2, s.Revision);
            uint revision = s.Revision;
            Assert.False(s.PressSwap(2, revision, 0));
            Assert.Equal(revision, s.Revision);
            AssertNoConsent(s);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(1, 2)]
        [InlineData(2, 2)]
        public void MovingEitherPlayerCancelsConsentAndInvalidatesReplay(byte player, byte destination)
        {
            var s = Shared(1, 1);
            s.PressSwap(2, s.Revision, 1);
            uint requestRevision = s.Revision;
            Assert.True(s.SetPlayerVehicle(player, destination, requestRevision));
            AssertNoConsent(s);
            Assert.False(s.PressSwap(1, requestRevision, 2));
            Assert.True(s.SetPlayerVehicle(player, 1, s.Revision));
            byte driver = s.Driver1;
            Assert.False(s.PressSwap(1, requestRevision, 3));
            Assert.Equal(driver, s.Driver1);
            AssertNoConsent(s);
            AssertInvariants(s);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 2)]
        public void DisconnectCancelsConsentAndPreservesHostOccupancy(byte driver, byte vehicle)
        {
            var s = Shared(driver, vehicle);
            s.PressSwap(2, s.Revision, 1);
            uint revision = s.Revision;
            s.DisconnectGuest();
            Assert.Equal(revision + 1, s.Revision);
            Assert.Equal(vehicle, s.Player1Vehicle);
            Assert.Equal(0, s.Player2Vehicle);
            Assert.Equal(1, Driver(s, vehicle));
            AssertNoConsent(s);
            Assert.False(s.PressSwap(1, revision, 2));
            s.DisconnectGuest();
            Assert.Equal(revision + 1, s.Revision);
            Assert.False(s.SetPlayerVehicle(2, vehicle, revision));
            Assert.True(s.SetPlayerVehicle(2, vehicle, s.Revision));
            Assert.Equal(1, Driver(s, vehicle));
            AssertInvariants(s);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        public void DisconnectEmptiesOnlyGuestsDistinctCar(byte hostVehicle, byte guestVehicle)
        {
            var s = new VehicleOccupancy();
            s.SetPlayerVehicle(1, hostVehicle, s.Revision);
            s.SetPlayerVehicle(2, guestVehicle, s.Revision);
            s.DisconnectGuest();
            Assert.Equal(hostVehicle, s.Player1Vehicle);
            Assert.Equal(0, s.Player2Vehicle);
            Assert.Equal(0, Driver(s, guestVehicle));
            if (hostVehicle != 0) Assert.Equal(1, Driver(s, hostVehicle));
            AssertInvariants(s);
        }

        private static VehicleOccupancy Shared(byte driver, byte vehicle)
        {
            var s = new VehicleOccupancy();
            Assert.True(s.SetPlayerVehicle(driver, vehicle, s.Revision));
            Assert.True(s.SetPlayerVehicle(driver == 1 ? (byte)2 : (byte)1, vehicle, s.Revision));
            return s;
        }

        private static byte Driver(VehicleOccupancy s, byte vehicle) => vehicle == 1 ? s.Driver1 : s.Driver2;

        private static void AssertNoConsent(VehicleOccupancy s)
        {
            Assert.Equal(0, s.Requester);
            Assert.Equal(0, s.RequestedVehicle);
            Assert.Equal(0d, s.ExpiresAt);
        }

        private static void AssertInvariants(VehicleOccupancy s)
        {
            Assert.InRange(s.Player1Vehicle, (byte)0, (byte)2);
            Assert.InRange(s.Player2Vehicle, (byte)0, (byte)2);
            for (byte vehicle = 1; vehicle <= 2; vehicle++)
            {
                byte driver = Driver(s, vehicle);
                bool occupied = s.Player1Vehicle == vehicle || s.Player2Vehicle == vehicle;
                Assert.Equal(occupied, driver != 0);
                Assert.True(driver == 0 || (driver == 1 && s.Player1Vehicle == vehicle) ||
                    (driver == 2 && s.Player2Vehicle == vehicle));
            }
        }
    }
}
