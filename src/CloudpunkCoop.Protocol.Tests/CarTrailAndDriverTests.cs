using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CarTrailAndDriverTests
    {
        [Fact]
        public void TrailStateIsImmutableBoundedAndRoundTrips()
        {
            var array = new[] { new TrailVisualState("boosters/left/trail", true, true, 1, 0.3f, 0.05f) };
            var message = new CarTrailMessage(uint.MaxValue, array);
            byte[] bytes = MessageCodec.Encode(message);
            array[0] = default;
            message.Trails[0] = default;
            Assert.Equal(bytes, MessageCodec.Encode(message));
            var decoded = Assert.IsType<CarTrailMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(uint.MaxValue, decoded.SequenceNumber);
            Assert.Equal(message.Trails, decoded.Trails);
            for (int i = 0; i < bytes.Length; i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out _));
            Assert.False(MessageCodec.TryDecode(new byte[] { 12, 0, 0, 0, 0, 65 }, out _));
            Assert.Throws<ProtocolException>(() => new CarTrailMessage(0, new[] { new TrailVisualState("t", true, true, float.NaN, 1, 1) }));
            Assert.Throws<ProtocolException>(() => new CarTrailMessage(0, new[] { new TrailVisualState("t", true, true, 1, -1, 1) }));
        }

        [Fact]
        public void DriverCommandsCarrySeatRevisionAndRejectInvalidAxes()
        {
            var m = new DriverInputMessage(uint.MaxValue, 42, -1, 0.25f, 1, -0.5f);
            var copy = Assert.IsType<DriverInputMessage>(MessageCodec.Decode(MessageCodec.Encode(m)));
            Assert.Equal(m, copy);
            Assert.Throws<ProtocolException>(() => new DriverInputMessage(0, 0, float.NaN, 0, 0, 0));
            Assert.Throws<ProtocolException>(() => new DriverInputMessage(0, 0, 0, 0, 2, 0));
            var bytes = MessageCodec.Encode(m);
            for (int i = 0; i < bytes.Length; i++) Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out _));
        }
    }
}
