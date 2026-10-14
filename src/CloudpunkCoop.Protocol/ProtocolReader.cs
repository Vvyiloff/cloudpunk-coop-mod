using System;
using System.Text;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Forward-only little-endian reader over a payload span. Every read is bounds checked and
    /// raises <see cref="ProtocolException"/> when the payload ends early, so truncated or
    /// garbage input can never turn into an index-out-of-range or an allocation.
    /// </summary>
    public ref struct ProtocolReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public ProtocolReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        /// <summary>Offset of the next byte to be read.</summary>
        public int Position => _position;

        /// <summary>Bytes not yet consumed.</summary>
        public int Remaining => _data.Length - _position;

        /// <summary>True when the whole payload has been consumed.</summary>
        public bool IsAtEnd => _position >= _data.Length;

        public byte ReadByte()
        {
            Require(1);
            return _data[_position++];
        }

        public bool ReadBoolean()
        {
            byte raw = ReadByte();
            if (raw > 1)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.TruncatedPayload,
                    $"Boolean field must be 0 or 1 but was {raw}.");
            }

            return raw == 1;
        }

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(_data[_position] | (_data[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(_data[_position]
                | (_data[_position + 1] << 8)
                | (_data[_position + 2] << 16)
                | (_data[_position + 3] << 24));
            _position += 4;
            return value;
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public long ReadInt64()
        {
            Require(8);
            ulong raw = (ulong)_data[_position]
                | ((ulong)_data[_position + 1] << 8)
                | ((ulong)_data[_position + 2] << 16)
                | ((ulong)_data[_position + 3] << 24)
                | ((ulong)_data[_position + 4] << 32)
                | ((ulong)_data[_position + 5] << 40)
                | ((ulong)_data[_position + 6] << 48)
                | ((ulong)_data[_position + 7] << 56);
            _position += 8;
            return unchecked((long)raw);
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

        /// <summary>Validate a length-prefixed blob before any caller allocates a copy.</summary>
        public ReadOnlySpan<byte> ReadBlob(int maxByteLength)
        {
            if (maxByteLength < 0) throw new ArgumentOutOfRangeException(nameof(maxByteLength));
            int length = ReadInt32();
            if (length < 0 || length > maxByteLength)
                throw new ProtocolException(ProtocolErrorKind.InvalidBlobLength, "Blob length exceeds its bound.");
            Require(length);
            var result = _data.Slice(_position, length);
            _position += length;
            return result;
        }

        /// <summary>
        /// Reads a 32-bit little-endian byte count followed by that many UTF-8 bytes.
        /// The declared length is validated against both the protocol limit and the bytes that
        /// are actually left, so a bogus length can never cause an allocation.
        /// </summary>
        public string ReadString() => ReadString(ProtocolConstants.MaxStringByteLength);

        internal string ReadString(int maxByteLength, bool strictUtf8 = false)
        {
            int byteCount = ReadInt32();
            if (byteCount < 0 || byteCount > maxByteLength)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.InvalidStringLength,
                    $"String length {byteCount} is outside the allowed range 0..{maxByteLength}.");
            }

            if (byteCount > Remaining)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.TruncatedPayload,
                    $"String claims {byteCount} bytes but only {Remaining} remain.");
            }

            string value;
            try
            {
                value = byteCount == 0 ? string.Empty
                    : (strictUtf8 ? StrictUtf8 : Encoding.UTF8).GetString(_data.Slice(_position, byteCount));
            }
            catch (DecoderFallbackException)
            {
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue,
                    "Particle path contains invalid UTF-8.");
            }
            _position += byteCount;
            return value;
        }

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private void Require(int byteCount)
        {
            if (Remaining < byteCount)
            {
                throw new ProtocolException(
                    ProtocolErrorKind.TruncatedPayload,
                    $"Payload ended after {_position} of {_data.Length} bytes, {byteCount} more were needed.");
            }
        }
    }
}
