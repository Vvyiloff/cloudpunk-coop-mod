using System;
using System.Collections.Generic;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    /// <summary>
    /// Framing tests. TCP delivers arbitrary chunk boundaries, so the accumulator has to
    /// reconstruct frames from any split, and it has to refuse an oversized length prefix
    /// before that prefix can turn into an allocation.
    /// </summary>
    public class FrameReaderTests
    {
        [Fact]
        public void SingleFrameInOneChunk_IsReturned()
        {
            byte[] payload = { 1, 2, 3, 4, 5 };
            byte[] frame = FrameCodec.EncodeFrame(payload);

            var reader = new FrameReader();
            reader.Append(frame);

            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(payload, decoded.ToArray());
            Assert.False(reader.TryReadFrame(out _));
        }

        [Fact]
        public void EmptyPayload_IsAValidFrame()
        {
            byte[] frame = FrameCodec.EncodeFrame(ReadOnlySpan<byte>.Empty);

            var reader = new FrameReader();
            reader.Append(frame);

            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(0, decoded.Length);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(17)]
        public void FrameSplitIntoChunks_IsReassembled(int splitAt)
        {
            byte[] payload = new byte[64];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)i;
            }

            byte[] frame = FrameCodec.EncodeFrame(payload);
            var reader = new FrameReader();

            reader.Append(frame.AsSpan(0, splitAt));

            // The frame is not complete until the remainder arrives.
            Assert.False(reader.TryReadFrame(out _));

            if (splitAt < FrameCodec.LengthPrefixSize)
            {
                // Not even a length prefix has arrived, so no frame is pending yet.
                Assert.False(reader.HasPendingFrame);
                Assert.Equal(0, reader.PendingPayloadBytes);
            }
            else
            {
                Assert.True(reader.HasPendingFrame);
                Assert.Equal(payload.Length - (splitAt - FrameCodec.LengthPrefixSize), reader.PendingPayloadBytes);
            }

            reader.Append(frame.AsSpan(splitAt));

            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(payload, decoded.ToArray());
        }

        [Fact]
        public void FrameSplitByteByByte_IsReassembled()
        {
            byte[] payload = { 10, 20, 30, 40, 50, 60, 70 };
            byte[] frame = FrameCodec.EncodeFrame(payload);

            var reader = new FrameReader();
            for (int i = 0; i < frame.Length - 1; i++)
            {
                reader.Append(frame.AsSpan(i, 1));
                Assert.False(reader.TryReadFrame(out _));
            }

            reader.Append(frame.AsSpan(frame.Length - 1, 1));
            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(payload, decoded.ToArray());
        }

        [Fact]
        public void MultipleFramesInOneChunk_AreAllReturned()
        {
            byte[] a = FrameCodec.EncodeFrame(new byte[] { 1 });
            byte[] b = FrameCodec.EncodeFrame(new byte[] { 2, 2 });
            byte[] c = FrameCodec.EncodeFrame(new byte[] { 3, 3, 3 });

            var combined = new List<byte>();
            combined.AddRange(a);
            combined.AddRange(b);
            combined.AddRange(c);

            var reader = new FrameReader();
            reader.Append(combined.ToArray());

            var results = new List<byte[]>();
            while (reader.TryReadFrame(out ReadOnlySpan<byte> decoded))
            {
                results.Add(decoded.ToArray());
            }

            Assert.Equal(3, results.Count);
            Assert.Equal(new byte[] { 1 }, results[0]);
            Assert.Equal(new byte[] { 2, 2 }, results[1]);
            Assert.Equal(new byte[] { 3, 3, 3 }, results[2]);
            Assert.Equal(0, reader.BufferedBytes);
        }

        [Fact]
        public void PartialFrameFollowedByAnother_OnlyCompleteFrameIsReturned()
        {
            byte[] first = FrameCodec.EncodeFrame(new byte[] { 9, 9 });
            byte[] second = FrameCodec.EncodeFrame(new byte[] { 8, 8, 8, 8 });

            var reader = new FrameReader();
            reader.Append(first);

            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> a));
            Assert.Equal(new byte[] { 9, 9 }, a.ToArray());

            // Only the length prefix of the second frame.
            reader.Append(second.AsSpan(0, FrameCodec.LengthPrefixSize));
            Assert.False(reader.TryReadFrame(out _));
            Assert.Equal(4, reader.PendingPayloadBytes);

            reader.Append(second.AsSpan(FrameCodec.LengthPrefixSize));
            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> b));
            Assert.Equal(new byte[] { 8, 8, 8, 8 }, b.ToArray());
        }

        [Fact]
        public void OversizedFrame_IsRejectedWhileOnlyThePrefixIsBuffered()
        {
            var writer = new ProtocolWriter(8);
            writer.WriteUInt32((uint)(ProtocolConstants.DefaultMaxFramePayloadSize + 1));

            var reader = new FrameReader();
            reader.Append(writer.WrittenSpan);

            ProtocolException ex = Assert.Throws<ProtocolException>(() => reader.TryReadFrame(out _));

            Assert.Equal(ProtocolErrorKind.OversizedFrame, ex.Kind);

            // Only the four prefix bytes are held: nothing was reserved for the claimed payload.
            Assert.Equal(FrameCodec.LengthPrefixSize, reader.BufferedBytes);
        }

        [Fact]
        public void OversizedFrame_IsRejectedOnAppendAlone()
        {
            var writer = new ProtocolWriter(8);
            writer.WriteUInt32(0x7FFFFFFF);

            var reader = new FrameReader();

            ProtocolException ex = Assert.Throws<ProtocolException>(() =>
            {
                reader.Append(writer.WrittenSpan);
                reader.TryReadFrame(out _);
            });

            Assert.Equal(ProtocolErrorKind.OversizedFrame, ex.Kind);
        }

        [Fact]
        public void NegativeLengthPrefix_IsRejected()
        {
            var writer = new ProtocolWriter(8);
            writer.WriteUInt32(0xFFFFFFFFu);

            var reader = new FrameReader();

            ProtocolException ex = Assert.Throws<ProtocolException>(() =>
            {
                reader.Append(writer.WrittenSpan);
                reader.TryReadFrame(out _);
            });

            Assert.Equal(ProtocolErrorKind.InvalidFrameLength, ex.Kind);
        }

        [Fact]
        public void CustomMaximum_IsHonoured()
        {
            byte[] payload = new byte[100];
            byte[] frame = FrameCodec.EncodeFrame(payload);

            var reader = new FrameReader(maxPayloadSize: 64);

            ProtocolException ex = Assert.Throws<ProtocolException>(() =>
            {
                reader.Append(frame);
                reader.TryReadFrame(out _);
            });

            Assert.Equal(ProtocolErrorKind.OversizedFrame, ex.Kind);
        }

        [Fact]
        public void FrameExactlyAtTheLimit_IsAccepted()
        {
            byte[] payload = new byte[64];
            byte[] frame = FrameCodec.EncodeFrame(payload);

            var reader = new FrameReader(maxPayloadSize: 64);
            reader.Append(frame);

            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(64, decoded.Length);
        }

        [Fact]
        public void GarbageAfterAValidFrame_DoesNotThrowUntilTheLengthIsRead()
        {
            byte[] good = FrameCodec.EncodeFrame(new byte[] { 7, 7 });

            var reader = new FrameReader();
            reader.Append(good);
            Assert.True(reader.TryReadFrame(out ReadOnlySpan<byte> decoded));
            Assert.Equal(new byte[] { 7, 7 }, decoded.ToArray());

            // One stray byte is not a length prefix yet; the reader must simply wait.
            reader.Append(new byte[] { 0xFF });
            Assert.False(reader.TryReadFrame(out _));
            Assert.Equal(1, reader.BufferedBytes);
        }

        [Fact]
        public void EveryOpcode_RoundTripsThroughFraming()
        {
            var messages = new IMessage[]
            {
                new HelloMessage("1.0", "host"),
                new WelcomeMessage(PeerRole.Guest, "1.0"),
                new CarStateMessage(1f, 2f, 3f, 0f, 0f, 0f, 1f, 4f, 5f, 6f, 0f, 0f, 0f, 0.1f, 0f, 1u),
                new InputMessage(1f, 0f, -1f, 0f, 0f, 2u),
                new SeatMessage(0, true),
                new PingMessage(3u, 99L),
                new PongMessage(3u, 99L),
                new ByeMessage(ByeReason.PeerShutdown),
            };

            var stream = new List<byte>();
            foreach (IMessage message in messages)
            {
                stream.AddRange(FrameCodec.EncodeFrame(message));
            }

            var reader = new FrameReader();
            reader.Append(stream.ToArray());

            var received = new List<IMessage>();
            while (reader.TryReadFrame(out ReadOnlySpan<byte> payload))
            {
                Assert.True(MessageCodec.TryDecode(payload, out IMessage? decoded));
                received.Add(decoded!);
            }

            Assert.Equal(messages.Length, received.Count);
            for (int i = 0; i < messages.Length; i++)
            {
                Assert.Equal(messages[i].Opcode, received[i].Opcode);
                Assert.Equal(messages[i].ToString(), received[i].ToString());
            }
        }

        [Fact]
        public void FrameReader_RejectsNonPositiveMaximum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameReader(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameReader(-1));
        }

        [Fact]
        public void AppendEmpty_IsANoOp()
        {
            var reader = new FrameReader();
            reader.Append(ReadOnlySpan<byte>.Empty);

            Assert.Equal(0, reader.BufferedBytes);
            Assert.False(reader.HasPendingFrame);
        }

        [Fact]
        public void LengthPrefixHelpers_AgreeOnLittleEndian()
        {
            var buffer = new byte[4];
            FrameCodec.WriteLengthPrefix(buffer, 0x01020304);

            Assert.Equal(new byte[] { 0x04, 0x03, 0x02, 0x01 }, buffer);
            Assert.Equal(0x01020304, FrameCodec.ReadLengthPrefix(buffer));
        }

        [Fact]
        public void NegativePayloadLength_IsRejected()
        {
            var buffer = new byte[4];

            ProtocolException ex = Assert.Throws<ProtocolException>(() =>
                FrameCodec.WriteLengthPrefix(buffer, -1));

            Assert.Equal(ProtocolErrorKind.InvalidFrameLength, ex.Kind);
        }

        [Fact]
        public void GetFrameSize_RejectsNegativePayload()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FrameCodec.GetFrameSize(-1));
            Assert.Equal(FrameCodec.LengthPrefixSize + 10, FrameCodec.GetFrameSize(10));
        }
    }
}