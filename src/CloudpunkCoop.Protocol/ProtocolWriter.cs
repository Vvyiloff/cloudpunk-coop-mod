using System;
using System.Text;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Append-only little-endian writer over a growable byte buffer.
    /// Every primitive is written byte-by-byte in a fixed order, so the produced bytes are
    /// identical on every platform and every .NET runtime. Floats are written as their
    /// IEEE-754 bit pattern, never as text and never via endianness-dependent conversions.
    /// </summary>
    public struct ProtocolWriter
    {
        private byte[] _buffer;
        private int _length;

        public ProtocolWriter(int initialCapacity)
        {
            // The capacity is a hint, not a contract: MessageCodec sizes the buffer to the
            // exact payload, and several messages are smaller than 16 bytes. Anything
            // positive is accepted and rounded up to a sane starting size.
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity), initialCapacity, "Capacity must be positive.");
            }

            _buffer = new byte[Math.Max(initialCapacity, MinimumCapacity)];
            _length = 0;
        }

        /// <summary>Smallest buffer the writer will start with, regardless of the requested hint.</summary>
        private const int MinimumCapacity = 16;

        /// <summary>Number of bytes written so far.</summary>
        public int Length => _length;

        /// <summary>True when nothing has been written.</summary>
        public bool IsEmpty => _length == 0;

        /// <summary>
        /// The bytes written so far. Valid until the next write or <see cref="Reset"/>.
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan => _buffer == null
            ? ReadOnlySpan<byte>.Empty
            : new ReadOnlySpan<byte>(_buffer, 0, _length);

        /// <summary>Rewinds the writer so the underlying buffer can be reused.</summary>
        public void Reset() => _length = 0;

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_length] = value;
            _length++;
        }

        /// <summary>Booleans travel as a single byte, 0 or 1.</summary>
        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(2);
            _buffer[_length] = (byte)value;
            _buffer[_length + 1] = (byte)(value >> 8);
            _length += 2;
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(4);
            _buffer[_length] = (byte)value;
            _buffer[_length + 1] = (byte)(value >> 8);
            _buffer[_length + 2] = (byte)(value >> 16);
            _buffer[_length + 3] = (byte)(value >> 24);
            _length += 4;
        }

        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

        public void WriteBlob(ReadOnlySpan<byte> value, int maxByteLength)
        {
            if (maxByteLength < 0) throw new ArgumentOutOfRangeException(nameof(maxByteLength));
            if (value.Length > maxByteLength)
                throw new ProtocolException(ProtocolErrorKind.InvalidBlobLength, "Blob length exceeds its bound.");
            WriteInt32(value.Length);
            EnsureCapacity(value.Length);
            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        public void WriteInt64(long value)
        {
            ulong raw = unchecked((ulong)value);
            EnsureCapacity(8);
            _buffer[_length] = (byte)raw;
            _buffer[_length + 1] = (byte)(raw >> 8);
            _buffer[_length + 2] = (byte)(raw >> 16);
            _buffer[_length + 3] = (byte)(raw >> 24);
            _buffer[_length + 4] = (byte)(raw >> 32);
            _buffer[_length + 5] = (byte)(raw >> 40);
            _buffer[_length + 6] = (byte)(raw >> 48);
            _buffer[_length + 7] = (byte)(raw >> 56);
            _length += 8;
        }

        /// <summary>
        /// Writes the IEEE-754 bit pattern of <paramref name="value"/>. Bit-level, so NaN and
        /// negative zero survive a round trip and the encoding is endian-independent.
        /// </summary>
        public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

        /// <summary>Writes the little-endian IEEE-754 binary64 bit pattern.</summary>
        public void WriteDouble(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        /// <summary>
        /// Writes a 32-bit little-endian byte count followed by the UTF-8 bytes.
        /// Throws when the encoded form would exceed <see cref="ProtocolConstants.MaxStringByteLength"/>.
        /// </summary>
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteInt32(0);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > ProtocolConstants.MaxStringByteLength)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.InvalidStringLength,
                    $"String is {byteCount} UTF-8 bytes, the limit is {ProtocolConstants.MaxStringByteLength}.");
            }

            WriteInt32(byteCount);
            EnsureCapacity(byteCount);
            Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _length);
            _length += byteCount;
        }

        /// <summary>Copies the written bytes into a new array.</summary>
        public byte[] ToArray()
        {
            var result = new byte[_length];
            if (_length > 0 && _buffer != null)
            {
                Array.Copy(_buffer, result, _length);
            }

            return result;
        }

        private void EnsureCapacity(int extra)
        {
            if (_buffer == null)
            {
                _buffer = new byte[Math.Max(MinimumCapacity, extra)];
                _length = 0;
                return;
            }

            int required = _length + extra;
            if (required <= _buffer.Length)
            {
                return;
            }

            int capacity = _buffer.Length * 2;
            if (capacity < required)
            {
                capacity = required;
            }

            Array.Resize(ref _buffer, capacity);
        }
    }
}
