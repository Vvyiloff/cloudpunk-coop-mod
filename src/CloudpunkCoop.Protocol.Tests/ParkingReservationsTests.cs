using System;
using Xunit;
namespace CloudpunkCoop.Protocol.Tests
{
    public class ParkingReservationsTests
    {
        [Fact] public void FirstRequestWinsEvenBeforeCarBecomesParked()
        {var r=new ParkingReservations();Assert.True(r.TryReserve("bay",1,0));Assert.False(r.TryReserve("bay",2,0));}
        [Fact] public void GrantedNativeParkingRemainsReservedDuringLongFrameStall()
        {var r=new ParkingReservations();Assert.True(r.TryReserve("bay",1,0,true));Assert.False(r.TryReserve("bay",2,60));r.Release(1);Assert.True(r.TryReserve("bay",2,60,true));}
        [Fact] public void DuplicateOwnerRequestKeepsCommittedOccupancy()
        {var r=new ParkingReservations();r.TryReserve("bay",1,0);r.Observe(1,"bay",1);Assert.True(r.TryReserve("bay",1,2));Assert.False(r.TryReserve("bay",2,100));}
        [Fact] public void OccupiedSpaceDoesNotExpire()
        {var r=new ParkingReservations();r.TryReserve("bay",1,0);r.Observe(1,"bay",1);Assert.False(r.TryReserve("bay",2,100));}
        [Fact] public void DrivingAwayFreesSpace()
        {var r=new ParkingReservations();r.TryReserve("bay",1,0);r.Observe(1,"bay",1);r.Observe(1,null,2);Assert.True(r.TryReserve("bay",2,2));}
        [Fact] public void UncompletedParkingReservationExpires()
        {var r=new ParkingReservations();r.TryReserve("bay",1,0);Assert.True(r.TryReserve("bay",2,11));}
        [Fact] public void DifferentSpacesHaveIndependentOwners()
        {var r=new ParkingReservations();Assert.True(r.TryReserve("a",1,0));Assert.True(r.TryReserve("b",2,0));}
        [Fact] public void DisconnectReleasesOnlyThatVehicle()
        {var r=new ParkingReservations();r.TryReserve("a",1,0);r.TryReserve("b",2,0);r.Release(2);Assert.False(r.TryReserve("a",2,1));Assert.True(r.TryReserve("b",1,1));}
        [Fact] public void MessageRoundTripPreservesAuthorityAndSpace()
        {var m=new ParkingSlotMessage(3,7,2,2,5,1,2,3,"Parking space occupied");var copy=Assert.IsType<ParkingSlotMessage>(MessageCodec.Decode(MessageCodec.Encode(m)));Assert.Equal(m.Request,copy.Request);Assert.Equal(m.Revision,copy.Revision);Assert.Equal(m.Vehicle,copy.Vehicle);Assert.Equal(m.Kind,copy.Kind);Assert.Equal(m.X,copy.X);Assert.Equal(m.Scene,copy.Scene);Assert.Equal(m.Reason,copy.Reason);}
        [Fact] public void TruncatedRequestCannotGrantParking()
        {var b=MessageCodec.Encode(new ParkingSlotMessage(3,7,2,0,5,1,2,3));Assert.Throws<ProtocolException>(()=>MessageCodec.Decode(b.AsSpan(0,b.Length-1)));}
        [Theory][InlineData(4)][InlineData(5)][InlineData(6)]
        public void ParkedJoinPlacementKindsRetainTheirAuthorityIdentity(byte kind)
        {var m=new ParkingSlotMessage(37,12,2,kind,6,-369.75f,10.625f,-29.5f,"join placement");var copy=Assert.IsType<ParkingSlotMessage>(MessageCodec.Decode(MessageCodec.Encode(m)));Assert.Equal(m,copy);}
        [Fact]public void UnknownParkingKindCannotBecomeASpawnGrant()
        {Assert.Throws<ProtocolException>(()=>new ParkingSlotMessage(37,12,2,7,6,1,2,3));}
    }
}
