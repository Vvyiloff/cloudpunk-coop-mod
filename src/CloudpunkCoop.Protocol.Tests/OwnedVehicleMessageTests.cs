using System;
using Xunit;
namespace CloudpunkCoop.Protocol.Tests
{
    public class OwnedVehicleMessageTests
    {
        [Fact]
        public void OwnedCarPreservesVehicleSceneAndFullNativePose()
        {
            var pose=new CarStateMessage(1,2,3,0,0,0,1,4,5,6,7,8,9,0,0,uint.MaxValue);
            var message=new OwnedCarMessage(2,6,true,pose);
            var bytes=MessageCodec.Encode(message);
            Assert.Equal(bytes.Length,MessageCodec.GetPayloadSize(message));
            var copy=Assert.IsType<OwnedCarMessage>(MessageCodec.Decode(bytes));
            Assert.Equal((byte)2,copy.Vehicle); Assert.Equal(6,copy.Scene); Assert.True(copy.Parked); Assert.Equal(pose,copy.Pose);
            for(int i=0;i<bytes.Length;i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0,i),out _));
            bytes[7]=(byte)Opcode.OwnedCar;
            Assert.False(MessageCodec.TryDecode(bytes,out _)); // Nested recursion is never accepted.
        }
        [Fact]
        public void ParkingCommandsAreBoundedAndCannotBeTruncated()
        {
            for(byte kind=0;kind<=2;kind++)
            {
                var m=new VehicleParkMessage(uint.MaxValue,2,kind);
                var bytes=MessageCodec.Encode(m);
                Assert.Equal(7,bytes.Length);
                Assert.Equal(m,Assert.IsType<VehicleParkMessage>(MessageCodec.Decode(bytes)));
                for(int i=0;i<bytes.Length;i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0,i),out _));
            }
            Assert.Throws<ProtocolException>(()=>new VehicleParkMessage(1,0,0));
            Assert.Throws<ProtocolException>(()=>new VehicleParkMessage(1,3,0));
            Assert.Throws<ProtocolException>(()=>new VehicleParkMessage(1,2,3));
        }

        [Fact]
        public void OccupancyStateRejectsImpossibleDriversAndRoundTrips()
        {
            var state=new OccupancyStateMessage(4,2,2,0,1,2,4);
            var bytes=MessageCodec.Encode(state);
            Assert.Equal(14,bytes.Length);
            var copy=Assert.IsType<OccupancyStateMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(state,copy);
            Assert.Throws<ProtocolException>(()=>new OccupancyStateMessage(0,1,2,2,1,0,0));
            Assert.Throws<ProtocolException>(()=>new OccupancyStateMessage(0,1,1,0,0,0,0));
            Assert.Throws<ProtocolException>(()=>new OccupancyStateMessage(0,0,0,1,0,0,0));
            Assert.Throws<ProtocolException>(()=>new OccupancyStateMessage(0,1,2,1,2,1,4));
            Assert.Throws<ProtocolException>(()=>new OccupancyRequestMessage(0,3));
            var request=new OccupancyRequestMessage(uint.MaxValue,2);
            Assert.Equal(request,Assert.IsType<OccupancyRequestMessage>(MessageCodec.Decode(MessageCodec.Encode(request))));
        }
    }
}
