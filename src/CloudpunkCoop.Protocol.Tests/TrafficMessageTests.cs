using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class TrafficMessageTests
    {
        private static TrafficVehicleState Car(ushort id, bool active = true) =>
            new TrafficVehicleState(id, active, -3, 20, 100, 0, 0.7f, 0, 0.7f);

        [Fact]
        public void FullPoolRoundTripsWithExplicitSizeAndImmutableStorage()
        {
            var cars = new TrafficVehicleState[256];
            for (ushort i = 0; i < cars.Length; i++) cars[i] = Car(i, i % 2 == 0);
            var m = new TrafficMessage(uint.MaxValue, 6, new string('F', 64), cars);
            byte[] bytes = MessageCodec.Encode(m);
            Assert.Equal(1+4+4+4+64+2+31*256, bytes.Length);
            Assert.Equal(bytes.Length, MessageCodec.GetPayloadSize(m));
            cars[0] = Car(0, false); m.Vehicles[1] = Car(1, true);
            Assert.Equal(bytes, MessageCodec.Encode(m));
            var copy = Assert.IsType<TrafficMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(m.SequenceNumber, copy.SequenceNumber);
            Assert.Equal(m.Scene, copy.Scene); Assert.Equal(m.PoolFingerprint, copy.PoolFingerprint);
            Assert.Equal(m.Vehicles, copy.Vehicles);
        }

        [Fact]
        public void InvalidIdentityCountManifestAndPoseAreRejected()
        {
            Assert.Throws<ProtocolException>(() => new TrafficMessage(0, 6, new string('A', 64), new[] { Car(0), Car(0) }));
            Assert.Throws<ProtocolException>(() => new TrafficMessage(0, 6, new string('A', 64), new[] { Car(512) }));
            Assert.Throws<ProtocolException>(() => new TrafficMessage(0, 6, new string('Z', 64), new[] { Car(0) }));
            Assert.Throws<ProtocolException>(() => new TrafficMessage(0, 6, new string('A', 64), new TrafficVehicleState[513]));
            Assert.Throws<ProtocolException>(() => new TrafficVehicleState(0, true, float.NaN, 0, 0, 0, 0, 0, 1));
            Assert.Throws<ProtocolException>(() => new TrafficVehicleState(0, true, 0, 0, 0, 0, 0, 0, 0));
            Assert.Throws<ProtocolException>(() => new TrafficMessage(0,6,new string('A',64),new TrafficVehicleState[1]));
        }

        [Fact]
        public void TruncationAndNoncanonicalBooleanCannotPartiallyApply()
        {
            byte[] bytes = MessageCodec.Encode(new TrafficMessage(1, 6, new string('A', 64), new[] { Car(0) }));
            for (int i = 0; i < bytes.Length; i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out _));
            bytes[1+4+4+4+64+2+2] = 2;
            Assert.False(MessageCodec.TryDecode(bytes, out _));
        }
    }
}
