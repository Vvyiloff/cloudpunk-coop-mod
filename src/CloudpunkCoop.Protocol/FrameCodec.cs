using System;
using System.IO;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Wire framing: a 4-byte little-endian unsigned payload length followed by that many
    /// payload bytes. There is no magic number and no version byte inside a frame; the
    /// protocol version travels in the HELLO message instead.
    ///
    /// A frame carries exactly one message payload.
    /// </summary>
    public static class FrameCodec
    {
        /// <summary>Size of the length prefix in bytes.</summary>
        public const int LengthPrefixSize = ProtocolConstants.LengthPrefixSize;

        /// <summary>Default maximum accepted payload size, in bytes.</summary>
        public const int DefaultMaxPayloadSize = ProtocolConstants.DefaultMaxFramePayloadSize;

        /// <summary>Total wire size of a frame carrying a payload of the given length.</summary>
        public static int GetFrameSize(int payloadSize)
        {
            if (payloadSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadSize), payloadSize, "Payload size cannot be negative.");
            }

            return LengthPrefixSize + payloadSize;
        }

        /// <summary>Writes a length prefix plus the payload into a new array. Intended for tests and simple senders.</summary>
        public static byte[] EncodeFrame(ReadOnlySpan<byte> payload)
        {
            var frame = new byte[GetFrameSize(payload.Length)];
            WriteLengthPrefix(frame, payload.Length);
            payload.CopyTo(new Span<byte>(frame, LengthPrefixSize, payload.Length));
            return frame;
        }

        /// <summary>Encodes a message straight into a framed byte array.</summary>
        public static byte[] EncodeFrame(IMessage message) => EncodeFrame(MessageCodec.Encode(message));

        /// <summary>Writes one frame to a stream, flushing it before returning.</summary>
        public static void WriteFrame(Stream stream, ReadOnlySpan<byte> payload)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            byte[] frame = EncodeFrame(payload);
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }

        /// <summary>Reads a single frame from a stream, blocking until it is complete.</summary>
        public static byte[] ReadFrame(Stream stream, int maxPayloadSize = DefaultMaxPayloadSize)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var reader = new FrameReader(maxPayloadSize);
            var chunk = new byte[Math.Min(8192, GetFrameSize(maxPayloadSize))];
            var prefix = new byte[LengthPrefixSize];

            while (true)
            {
                if (!reader.TryReadFrame(out var payload))
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read <= 0)
                    {
                        throw new ProtocolException(
                            ProtocolErrorKind.ConnectionClosed, "Stream ended before a complete frame arrived.");
                    }

                    reader.Append(chunk.AsSpan(0, read));
                    continue;
                }

                return payload.ToArray();
            }
        }

        /// <summary>
        /// Writes the little-endian length prefix into the first four bytes of
        /// <paramref name="destination"/>.
        /// </summary>
        public static void WriteLengthPrefix(Span<byte> destination, int payloadLength)
        {
            if (payloadLength < 0)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.InvalidFrameLength, $"Payload length {payloadLength} is negative.");
            }

            if (destination.Length < LengthPrefixSize)
            {
                throw new ArgumentException(
                    "Destination must be at least four bytes long.", nameof(destination));
            }

            uint raw = unchecked((uint)payloadLength);
            destination[0] = (byte)raw;
            destination[1] = (byte)(raw >> 8);
            destination[2] = (byte)(raw >> 16);
            destination[3] = (byte)(raw >> 24);
        }

        /// <summary>Reads a little-endian length prefix from the first four bytes of the source.</summary>
        public static int ReadLengthPrefix(ReadOnlySpan<byte> source)
        {
            if (source.Length < LengthPrefixSize)
            {
                throw new ArgumentException(
                    "Source must be at least four bytes long.", nameof(source));
            }

            uint raw = (uint)(source[0]
                | (source[1] << 8)
                | (source[2] << 16)
                | (source[3] << 24));
            return unchecked((int)raw);
        }
    }
}
