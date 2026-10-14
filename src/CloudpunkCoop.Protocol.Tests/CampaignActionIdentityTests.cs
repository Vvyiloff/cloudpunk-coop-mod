using System;
using System.Buffers.Binary;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CampaignActionIdentityTests
    {
        private static CampaignActionMessage Action(long sessionId = 0, ulong conversation = 0, int lineIndex = -1) =>
            new CampaignActionMessage(1, 2, 3, CampaignActionMessage.ContinueDialog, "d", "l", 1,
                sessionId, conversation, lineIndex);

        [Fact]
        public void ExactWireAppendsIdentityAfterAmount()
        {
            var message = Action(0x0807060504030201, 0xFFEEDDCCBBAA9988UL, 0x0A09);
            byte[] expected = {
                25, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 2,
                1, 0, 0, 0, (byte)'d', 1, 0, 0, 0, (byte)'l', 1, 0, 0, 0,
                1, 2, 3, 4, 5, 6, 7, 8,
                0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
                9, 10, 0, 0
            };
            Assert.Equal(48, MessageCodec.GetPayloadSize(message));
            Assert.Equal(expected, MessageCodec.Encode(message));
            var copy = Assert.IsType<CampaignActionMessage>(MessageCodec.Decode(expected));
            Assert.Equal(message.SessionId, copy.SessionId);
            Assert.Equal(message.Conversation, copy.Conversation);
            Assert.Equal(message.LineIndex, copy.LineIndex);
        }

        [Fact]
        public void IdentityExtremesAndUnsignedHighBitRoundTripWithoutLoss()
        {
            foreach (long session in new[] { 0L, 1L, long.MaxValue })
                foreach (ulong conversation in new[] { 0UL, 1UL, (ulong)long.MaxValue, 1UL << 63, ulong.MaxValue })
                    foreach (int lineIndex in new[] { -1, 0, 4095 })
                    {
                        var original = Action(session, conversation, lineIndex);
                        byte[] bytes = MessageCodec.Encode(original);
                        Assert.Equal(bytes.Length, MessageCodec.GetPayloadSize(original));
                        var copy = Assert.IsType<CampaignActionMessage>(MessageCodec.Decode(bytes));
                        Assert.Equal(session, copy.SessionId); Assert.Equal(conversation, copy.Conversation);
                        Assert.Equal(lineIndex, copy.LineIndex);
                        Assert.Equal(original.RequestId, copy.RequestId); Assert.Equal(original.Revision, copy.Revision);
                        Assert.Equal(original.Scene, copy.Scene); Assert.Equal(original.Kind, copy.Kind);
                        Assert.Equal(original.Target, copy.Target); Assert.Equal(original.Argument, copy.Argument);
                        Assert.Equal(original.Amount, copy.Amount);
                    }
        }

        [Fact]
        public void OldConstructorCallsUseDefaultsButWireAlwaysIncludesIdentity()
        {
            var message = new CampaignActionMessage(1, 2, 3, 2, "d", "l", 1);
            Assert.Equal(0L, message.SessionId); Assert.Equal(0UL, message.Conversation); Assert.Equal(-1, message.LineIndex);
            byte[] bytes = MessageCodec.Encode(message);
            Assert.Equal(48, bytes.Length);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255 },
                bytes.AsSpan(28).ToArray());
            var copy = Assert.IsType<CampaignActionMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(0L, copy.SessionId); Assert.Equal(0UL, copy.Conversation); Assert.Equal(-1, copy.LineIndex);
        }

        [Fact]
        public void EveryTruncationIncludingOldProtocol12PayloadIsRejected()
        {
            byte[] bytes = MessageCodec.Encode(Action(long.MaxValue, ulong.MaxValue, 4095));
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, length), out var message)); Assert.Null(message);
                Assert.Equal(ProtocolErrorKind.TruncatedPayload,
                    Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes.AsSpan(0, length))).Kind);
            }
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.Equal(ProtocolErrorKind.TrailingBytes, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var trailing)); Assert.Null(trailing);
        }

        [Theory]
        [InlineData(-1)] [InlineData(long.MinValue)]
        public void NegativeSessionRejectedInConstructorAndWire(long sessionId)
        {
            Assert.Equal(ProtocolErrorKind.InvalidCampaignState, Assert.Throws<ProtocolException>(() => Action(sessionId)).Kind);
            byte[] bytes = MessageCodec.Encode(Action());
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(bytes.Length - 20), sessionId);
            Assert.Equal(ProtocolErrorKind.InvalidCampaignState, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var message)); Assert.Null(message);
        }

        [Theory]
        [InlineData(int.MinValue)] [InlineData(-2)] [InlineData(4096)] [InlineData(int.MaxValue)]
        public void OutOfRangeLineIndexRejectedInConstructorAndWire(int lineIndex)
        {
            Assert.Equal(ProtocolErrorKind.InvalidCampaignState, Assert.Throws<ProtocolException>(() => Action(lineIndex: lineIndex)).Kind);
            byte[] bytes = MessageCodec.Encode(Action());
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), lineIndex);
            Assert.Equal(ProtocolErrorKind.InvalidCampaignState, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var message)); Assert.Null(message);
        }
    }
}
