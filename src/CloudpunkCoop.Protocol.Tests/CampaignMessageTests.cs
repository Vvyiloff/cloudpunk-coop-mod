using System;
using System.Buffers.Binary;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CampaignMessageTests
    {
        private static CampaignChunkMessage Chunk(uint revision = 7, ushort index = 0,
            ushort count = 2, int scene = 6, params byte[] data) =>
            new CampaignChunkMessage(revision, scene, index, count, data);

        [Fact]
        public void ChunkHasExactLittleEndianLayoutAndOwnsItsBytes()
        {
            byte[] source = { 8, 9 };
            var message = new CampaignChunkMessage(0x04030201, -2, 1, 2, source);
            source[0] = 99;
            byte[] exposed = message.Data; exposed[1] = 99;
            byte[] expected = { 24, 1, 2, 3, 4, 254, 255, 255, 255, 1, 0, 2, 0, 2, 0, 0, 0, 8, 9 };
            Assert.Equal(expected, MessageCodec.Encode(message));
            Assert.Equal(19, MessageCodec.GetPayloadSize(message));
            var decoded = Assert.IsType<CampaignChunkMessage>(MessageCodec.Decode(expected));
            Assert.Equal(message.Revision, decoded.Revision); Assert.Equal(-2, decoded.Scene);
            Assert.Equal((ushort)1, decoded.Index); Assert.Equal((ushort)2, decoded.Count);
            Assert.Equal(new byte[] { 8, 9 }, decoded.DataSpan.ToArray());
            Assert.Equal("18", ProtocolConstants.ProtocolVersion);
        }

        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)]
        [InlineData(4)] [InlineData(5)] [InlineData(6)]
        public void EveryActionKindRoundTripsAllFields(int kind)
        {
            var original = new CampaignActionMessage(uint.MaxValue, 17, -9, (byte)kind, "character/dialog/item", "строка", 100);
            byte[] bytes = MessageCodec.Encode(original);
            Assert.Equal(25, bytes[0]); Assert.Equal(bytes.Length, MessageCodec.GetPayloadSize(original));
            var copy = Assert.IsType<CampaignActionMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(original.RequestId, copy.RequestId); Assert.Equal(original.Revision, copy.Revision);
            Assert.Equal(original.Scene, copy.Scene); Assert.Equal(original.Kind, copy.Kind);
            Assert.Equal(original.Target, copy.Target); Assert.Equal(original.Argument, copy.Argument);
            Assert.Equal(original.Amount, copy.Amount);
            Assert.Equal(0L, copy.SessionId); Assert.Equal(0UL, copy.Conversation); Assert.Equal(-1, copy.LineIndex);
        }

        [Theory]
        [InlineData(true, "")] [InlineData(false, "Недоступно")]
        public void ReplyRoundTripsAllFields(bool accepted, string reason)
        {
            var original = new CampaignReplyMessage(42, uint.MaxValue, accepted, reason);
            byte[] bytes = MessageCodec.Encode(original);
            Assert.Equal(26, bytes[0]); Assert.Equal(bytes.Length, MessageCodec.GetPayloadSize(original));
            var copy = Assert.IsType<CampaignReplyMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(original.RequestId, copy.RequestId); Assert.Equal(original.Revision, copy.Revision);
            Assert.Equal(accepted, copy.Accepted); Assert.Equal(reason, copy.Reason);
        }

        [Fact]
        public void EveryTruncatedMessageAndTrailingByteIsRejected()
        {
            IMessage[] messages = {
                Chunk(data: new byte[] { 1, 2, 3 }),
                new CampaignActionMessage(1, 2, 3, 2, "dialog", "line", 1),
                new CampaignReplyMessage(1, 2, false, "reason")
            };
            foreach (var message in messages)
            {
                byte[] bytes = MessageCodec.Encode(message);
                for (int i = 0; i < bytes.Length; i++)
                {
                    Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, i), out var copy)); Assert.Null(copy);
                    Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes.AsSpan(0, i)));
                }
                Array.Resize(ref bytes, bytes.Length + 1);
                Assert.False(MessageCodec.TryDecode(bytes, out var trailing)); Assert.Null(trailing);
                Assert.Equal(ProtocolErrorKind.TrailingBytes, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            }
        }

        [Theory]
        [InlineData(-1)] [InlineData(int.MinValue)] [InlineData(16385)] [InlineData(int.MaxValue)]
        public void HostileBlobLengthRejectedBeforeAllocation(int length)
        {
            byte[] bytes = MessageCodec.Encode(Chunk());
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), length);
            Assert.Equal(ProtocolErrorKind.InvalidBlobLength,
                Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var message)); Assert.Null(message);
        }

        [Fact]
        public void LegalBlobLengthStillRequiresActualBytes()
        {
            byte[] bytes = MessageCodec.Encode(Chunk());
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), 16384);
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
        }

        [Fact]
        public void ChunkBoundsAndWireHeadersAreValidated()
        {
            Assert.Throws<ProtocolException>(() => Chunk(count: 0));
            Assert.Throws<ProtocolException>(() => Chunk(count: 65));
            Assert.Throws<ProtocolException>(() => Chunk(index: 2));
            Assert.Throws<ProtocolException>(() => Chunk(data: new byte[16385]));
            Assert.Throws<ArgumentNullException>(() => new CampaignChunkMessage(0, 0, 0, 1, null!));
            var maximum = new CampaignChunkMessage(0, 0, 63, 64, new byte[16384]);
            Assert.Equal(16401, MessageCodec.Encode(maximum).Length);
            Assert.Equal(maximum.Data, Assert.IsType<CampaignChunkMessage>(MessageCodec.Decode(MessageCodec.Encode(maximum))).Data);
            foreach (ushort count in new ushort[] { 0, 65, ushort.MaxValue })
            {
                byte[] bytes = MessageCodec.Encode(Chunk());
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), count);
                Assert.False(MessageCodec.TryDecode(bytes, out _));
            }
            byte[] invalidIndex = MessageCodec.Encode(Chunk());
            BinaryPrimitives.WriteUInt16LittleEndian(invalidIndex.AsSpan(9), 2);
            Assert.False(MessageCodec.TryDecode(invalidIndex, out _));
        }

        [Fact]
        public void ActionAndReplyRejectInvalidFieldsIncludingMalformedWire()
        {
            foreach (byte kind in new byte[] { 0, 7, 255 })
            {
                Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, kind, "x", "", 1));
                byte[] bytes = MessageCodec.Encode(new CampaignActionMessage(1, 0, 0, 1, "x", "", 1));
                bytes[13] = kind; Assert.False(MessageCodec.TryDecode(bytes, out _));
            }
            foreach (int amount in new[] { int.MinValue, -1, 0, 101, int.MaxValue })
            {
                Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 1, "x", "", amount));
                byte[] bytes = MessageCodec.Encode(new CampaignActionMessage(1, 0, 0, 1, "x", "", 1));
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 24), amount);
                Assert.False(MessageCodec.TryDecode(bytes, out _));
            }
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(0, 0, 0, 1, "x", "", 1));
            Assert.Throws<ProtocolException>(() => new CampaignReplyMessage(0, 0, true, ""));
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 1, "", "", 1));
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 2, "dialog", "", 1));
            foreach (IMessage valid in new IMessage[] {
                new CampaignActionMessage(1, 0, 0, 1, "x", "", 1), new CampaignReplyMessage(1, 0, true, "") })
            {
                byte[] bytes = MessageCodec.Encode(valid);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), 0);
                Assert.False(MessageCodec.TryDecode(bytes, out _));
            }
            byte[] reply = MessageCodec.Encode(new CampaignReplyMessage(1, 0, true, ""));
            reply[9] = 2; Assert.False(MessageCodec.TryDecode(reply, out _));
        }

        [Fact]
        public void StringBoundsCountUtf8BytesAndRejectInvalidEncoding()
        {
            string boundary = new string('я', 128), oversized = boundary + "я";
            var valid = new CampaignActionMessage(1, 0, 0, 2, boundary, boundary, 1);
            Assert.IsType<CampaignActionMessage>(MessageCodec.Decode(MessageCodec.Encode(valid)));
            Assert.IsType<CampaignReplyMessage>(MessageCodec.Decode(MessageCodec.Encode(new CampaignReplyMessage(1, 0, false, boundary))));
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 1, oversized, "", 1));
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 1, "x", oversized, 1));
            Assert.Throws<ProtocolException>(() => new CampaignReplyMessage(1, 0, false, oversized));
            Assert.Throws<ProtocolException>(() => new CampaignReplyMessage(1, 0, false, "\ud800"));
            Assert.Throws<ProtocolException>(() => new CampaignActionMessage(1, 0, 0, 1, null!, "", 1));
            foreach (IMessage message in new IMessage[] { valid, new CampaignReplyMessage(1, 0, false, "x") })
            {
                int offset = message is CampaignActionMessage ? 14 : 10;
                foreach (int length in new[] { -1, 257, int.MaxValue })
                {
                    byte[] bytes = MessageCodec.Encode(message);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), length);
                    Assert.False(MessageCodec.TryDecode(bytes, out _));
                }
                byte[] invalidUtf8 = MessageCodec.Encode(message);
                invalidUtf8[offset + 4] = 0xff;
                Assert.False(MessageCodec.TryDecode(invalidUtf8, out _));
            }
        }
    }
}
