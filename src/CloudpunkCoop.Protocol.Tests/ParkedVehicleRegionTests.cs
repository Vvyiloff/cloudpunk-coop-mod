using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class ParkedVehicleRegionTests
    {
        private static OwnedCarMessage Car(bool parked) => new(2,6,parked,
            new CarStateMessage(1,2,3,0,0,0,1,5,6,7,8,9,10,.25f,.5f,100));
        [Fact]
        public void WalkingElsewhereKeepsParkedCarAndFreshSequenceInItsOriginalRegion()
        {
            var state=new ParkedVehicleRegion();state.Observe(Car(true));
            Assert.True(state.TryCaptureAway(13,true,101,out var parked));
            Assert.NotNull(parked);Assert.Equal(6,parked!.Scene);Assert.Equal(2,parked.Vehicle);
            Assert.True(parked.Parked);Assert.Equal(101u,parked.Pose.SequenceNumber);
            Assert.Equal(1,parked.Pose.PositionX);Assert.Equal(2,parked.Pose.PositionY);Assert.Equal(3,parked.Pose.PositionZ);
            Assert.Equal(0,parked.Pose.LinearVelocityX);Assert.Equal(0,parked.Pose.AngularVelocityY);
            Assert.Equal(.25f,parked.Pose.SteerAngle);Assert.Equal(.5f,parked.Pose.DamageLevel);
            Assert.Equal(6,Assert.IsType<OwnedCarMessage>(MessageCodec.Decode(MessageCodec.Encode(parked))).Scene);
        }
        [Fact]
        public void NativeDrivingAndSameRegionPosesAreNeverReplacedByAParkedRecord()
        {
            var state=new ParkedVehicleRegion();
            Assert.False(state.TryCaptureAway(13,true,1,out _));
            state.Observe(Car(true));
            Assert.False(state.TryCaptureAway(6,true,101,out _));
            Assert.False(state.TryCaptureAway(13,false,101,out _));
            state.Observe(Car(false));Assert.False(state.TryCaptureAway(13,true,102,out _));
            state.Reset();Assert.Null(state.Last);
        }
    }
}
