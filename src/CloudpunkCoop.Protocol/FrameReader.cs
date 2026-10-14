using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Incremental frame accumulator.
    ///
    /// TCP is a byte stream, not a message stream: a single <c>Read</c> can return half a
    /// frame, three frames, or one byte. This class holds the leftover bytes between reads
    /// and hands out complete payloads, so the caller never has to reason about where a
    /// frame happened to land in the stream.
    ///
    /// The length prefix is validated <em>before</em> the payload is buffered. A peer that
    /// claims a 2 GB frame is rejected on the spot rather than making us allocate for it.
    ///
    /// Consumed frames are dropped by advancing an offset rather than by shifting the buffer,
    /// so the span returned by <see cref="TryReadFrame"/> stays valid while the caller decodes
    /// it. The buffer is compacted on the next <see cref="Append"/>, which is exactly the
    /// lifetime the returned span is documented to have.
    /// </summary>
    public sealed class FrameReader
    {
        private readonly int _maxPayloadSize;
        private byte[] _buffer;
        private int _length;

        /// <summary>
        /// Offset of the first byte that has not been handed out yet. Bytes before it are
        /// consumed but still physically present until the next <see cref="Append"/> compacts.
        /// </summary>
        private int _start;

        /// <summary>Payload length declared by the most recent complete prefix, or -1.</summary>
        private int _expectedPayloadLength = -1;

        public FrameReader(int maxPayloadSize = ProtocolConstants.DefaultMaxFramePayloadSize)
        {
            if (maxPayloadSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPayloadSize), maxPayloadSize, "Maximum payload size must be positive.");
            }

            _maxPayloadSize = maxPayloadSize;
            _buffer = new byte[Math.Max(1024, ProtocolConstants.LengthPrefixSize + 64)];
        }

        /// <summary>Maximum payload length this reader will accept, in bytes.</summary>
        public int MaxPayloadSize => _maxPayloadSize;

        /// <summary>Bytes received but not yet returned as a frame.</summary>
        public int BufferedBytes => _length - _start;

        /// <summary>True when a valid length prefix is buffered and its payload is still incomplete.</summary>
        public bool HasPendingFrame => _expectedPayloadLength >= 0;

        /// <summary>
        /// Payload bytes still required to complete the frame whose length prefix is already
        /// buffered. Zero when no prefix is buffered yet.
        /// </summary>
        public int PendingPayloadBytes
        {
            get
            {
                if (_expectedPayloadLength < 0)
                {
                    return 0;
                }

                int available = BufferedBytes - ProtocolConstants.LengthPrefixSize;
                int missing = _expectedPayloadLength - available;
                return missing > 0 ? missing : 0;
            }
        }

        /// <summary>
        /// Appends freshly read bytes. This invalidates any span handed out by an earlier
        /// <see cref="TryReadFrame"/>, because the buffer may be compacted to make room.
        /// </summary>
        public void Append(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return;
            }

            Compact();
            EnsureCapacity(bytes.Length);
            bytes.CopyTo(new Span<byte>(_buffer, _length, bytes.Length));
            _length += bytes.Length;
        }

        /// <summary>
        /// Attempts to take one complete payload out of the accumulator.
        /// </summary>
        /// <param name="payload">
        /// The payload when the method returns true; otherwise empty. The span aliases the
        /// internal buffer and stays valid until the next <see cref="Append"/> call, so it can
        /// be decoded immediately. Copy it if it must outlive the next read.
        /// </param>
        /// <returns>True when a whole frame was available.</returns>
        /// <exception cref="ProtocolException">
        /// Thrown when the buffered length prefix is negative or exceeds
        /// <see cref="MaxPayloadSize"/>.
        /// </exception>
        public bool TryReadFrame(out ReadOnlySpan<byte> payload)
        {
            payload = ReadOnlySpan<byte>.Empty;

            if (_expectedPayloadLength < 0)
            {
                if (BufferedBytes < ProtocolConstants.LengthPrefixSize)
                {
                    return false;
                }

                _expectedPayloadLength = ReadLengthPrefixChecked();
            }

            int frameSize = ProtocolConstants.LengthPrefixSize + _expectedPayloadLength;
            if (BufferedBytes < frameSize)
            {
                return false;
            }

            // Advance the offset instead of moving bytes: the span handed to the caller points
            // into the buffer right here, and shifting now would overwrite it with the next frame.
            payload = new ReadOnlySpan<byte>(_buffer, _start + ProtocolConstants.LengthPrefixSize, _expectedPayloadLength);
            _start += frameSize;
            _expectedPayloadLength = -1;
            return true;
        }

        /// <summary>
        /// Reads and validates the buffered length prefix. Validation happens here, before any
        /// payload-sized buffer could be requested.
        /// </summary>
        private int ReadLengthPrefixChecked()
        {
            uint raw = (uint)(_buffer[_start]
                | (_buffer[_start + 1] << 8)
                | (_buffer[_start + 2] << 16)
                | (_buffer[_start + 3] << 24));

            // A prefix with the high bit set would be a negative int; treat it as hostile
            // rather than letting it wrap into a comparison that accepts it.
            if (raw > int.MaxValue)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.InvalidFrameLength,
                    $"Frame length prefix 0x{raw:X8} exceeds the signed 32-bit range.");
            }

            int length = (int)raw;
            if (length > _maxPayloadSize)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.OversizedFrame,
                    $"Frame payload of {length} bytes exceeds the {_maxPayloadSize} byte limit.");
            }

            return length;
        }

        /// <summary>Moves unconsumed bytes to the front of the buffer and resets the offset.</summary>
        private void Compact()
        {
            if (_start == 0)
            {
                return;
            }

            int remaining = _length - _start;
            if (remaining > 0)
            {
                Array.Copy(_buffer, _start, _buffer, 0, remaining);
            }

            _length = remaining;
            _start = 0;
        }

        private void EnsureCapacity(int extra)
        {
            int required = _length + extra;
            if (required <= _buffer.Length)
            {
                return;
            }

            int capacity = _buffer.Length;
            while (capacity < required)
            {
                capacity *= 2;
            }

            Array.Resize(ref _buffer, capacity);
        }
    }
}