using System;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CampaignPresentationTests
    {
        [Fact]
        public void ExactWireLayoutAndDefensiveCopies()
        {
            byte[] source = { 123, 125 };
            var original = new CampaignPresentationMessage(0x04030201, 0x08070605, -2, source);
            source[0] = 0;
            byte[] exposed = original.Data; exposed[1] = 0;
            byte[] expected = { 27, 1, 2, 3, 4, 5, 6, 7, 8, 254, 255, 255, 255, 2, 0, 0, 0, 123, 125 };
            Assert.Equal(expected, MessageCodec.Encode(original));
            Assert.Equal(19, MessageCodec.GetPayloadSize(original));
            var copy = Assert.IsType<CampaignPresentationMessage>(MessageCodec.Decode(expected));
            Assert.Equal(original.Sequence, copy.Sequence); Assert.Equal(original.Revision, copy.Revision);
            Assert.Equal(-2, copy.Scene); Assert.Equal(new byte[] { 123, 125 }, copy.DataSpan.ToArray());
            Assert.Equal("18", ProtocolConstants.ProtocolVersion);
        }

        [Theory]
        [InlineData(0)] [InlineData(16384)]
        public void EmptyAndMaximumOpaqueDataRoundTrip(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++) data[i] = (byte)i;
            var original = new CampaignPresentationMessage(uint.MaxValue, uint.MaxValue, int.MinValue, data);
            byte[] bytes = MessageCodec.Encode(original);
            Assert.Equal(17 + length, bytes.Length);
            Assert.Equal(bytes.Length, MessageCodec.GetPayloadSize(original));
            Assert.Equal(data, Assert.IsType<CampaignPresentationMessage>(MessageCodec.Decode(bytes)).Data);
        }

        [Fact]
        public void JsonBytesRemainOpaqueAndAllTruncationsAreRejected()
        {
            byte[] data = Encoding.UTF8.GetBytes("{\"lineID\":\"строка\",\"visible\":3}");
            byte[] bytes = MessageCodec.Encode(new CampaignPresentationMessage(8, 7, 6, data));
            Assert.Equal(data, Assert.IsType<CampaignPresentationMessage>(MessageCodec.Decode(bytes)).Data);
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.False(MessageCodec.TryDecode(bytes.AsSpan(0, length), out var message)); Assert.Null(message);
                Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes.AsSpan(0, length)));
            }
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.False(MessageCodec.TryDecode(bytes, out var trailing)); Assert.Null(trailing);
            Assert.Equal(ProtocolErrorKind.TrailingBytes, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
        }

        [Theory]
        [InlineData(-1)] [InlineData(int.MinValue)] [InlineData(16385)] [InlineData(int.MaxValue)]
        public void HostileLengthIsRejectedBeforeAllocation(int length)
        {
            byte[] bytes = MessageCodec.Encode(new CampaignPresentationMessage(1, 2, 3, Array.Empty<byte>()));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), length);
            Assert.Equal(ProtocolErrorKind.InvalidBlobLength, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out var message)); Assert.Null(message);
        }

        [Fact]
        public void ValidLengthWithoutDataAndInvalidConstructorDataAreRejected()
        {
            byte[] bytes = MessageCodec.Encode(new CampaignPresentationMessage(1, 2, 3, Array.Empty<byte>()));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), 16384);
            Assert.Equal(ProtocolErrorKind.TruncatedPayload, Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes)).Kind);
            Assert.False(MessageCodec.TryDecode(bytes, out _));
            Assert.Throws<ProtocolException>(() => new CampaignPresentationMessage(1, 2, 3, new byte[16385]));
            Assert.Throws<ArgumentNullException>(() => new CampaignPresentationMessage(1, 2, 3, null!));
        }

        [Fact]
        public void SequenceAndRevisionAreTransportMetadataNotCodecFreshnessPolicy()
        {
            foreach (uint sequence in new uint[] { 10, 9, 0, uint.MaxValue })
            {
                var original = new CampaignPresentationMessage(sequence, 0, 6, Array.Empty<byte>());
                Assert.Equal(sequence, Assert.IsType<CampaignPresentationMessage>(MessageCodec.Decode(MessageCodec.Encode(original))).Sequence);
            }
        }

        [Fact]
        public void ParentAssemblerCallHasNonNullBytesOnSuccessAndSupportsReset()
        {
            var assembler = new CampaignFrameAssembler();
            var message = new CampaignChunkMessage(1, 6, 0, 1, new byte[] { 42 });
            Assert.True(assembler.Add(message, out byte[]? bytes));
            Assert.Equal(new byte[] { 42 }, bytes);
            assembler.Reset();
            if (assembler.Add(message, out byte[]? assembled))
                Assert.Equal(42, assembled[0]);
            else
                Assert.Fail("Reset must allow the same revision in a new session.");
        }
    }
}
