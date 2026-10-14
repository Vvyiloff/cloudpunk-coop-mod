using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class SeatExchangeMessageTests
    {
        [Fact]
        public void RequestAndAuthorityStateHaveFixedWireLayout()
        {
            var request = Assert.IsType<SeatRequestMessage>(MessageCodec.Decode(
                MessageCodec.Encode(new SeatRequestMessage(uint.MaxValue))));
            Assert.Equal(uint.MaxValue, request.Revision);
            var state = new SeatStateMessage(42, 2, 1, true, 9.5f);
            byte[] bytes = MessageCodec.Encode(state);
            Assert.Equal(12, bytes.Length);
            var copy = Assert.IsType<SeatStateMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(42u, copy.Revision);
            Assert.Equal(2, copy.Driver);
            Assert.Equal(1, copy.Requester);
            Assert.True(copy.BothSeated);
            Assert.Equal(9.5f, copy.SecondsLeft);
            for (int i = 0; i < bytes.Length; i++)
                Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out _));
            Assert.False(MessageCodec.TryDecode(new byte[] { (byte)Opcode.SeatState, 0,0,0,0,3,0,0,0,0,0,0 }, out _));
        }

        [Fact]
        public void InvalidAuthorityFieldsAreRejected()
        {
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 0, 0, false, 0));
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 2, 0, false, 0));
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 1, 3, true, 1));
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 1, 0, true, 1));
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 1, 1, true, float.NaN));
            Assert.Throws<ProtocolException>(() => new SeatStateMessage(0, 1, 1, true, 11));
        }
    }
}
