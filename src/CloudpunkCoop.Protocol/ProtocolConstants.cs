namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Values that both peers must agree on. Nothing here depends on Unity or on the game.
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>Version string sent in <see cref="HelloMessage"/>.</summary>
        public const string ProtocolVersion = "18";

        /// <summary>
        /// Default upper bound for a single frame payload, in bytes.
        /// A length prefix larger than this is rejected before any buffer is allocated.
        /// </summary>
        public const int DefaultMaxFramePayloadSize = 65536;

        /// <summary>
        /// Upper bound for the UTF-8 byte count of any string field. Both the writer and the
        /// reader enforce it, so a hostile peer cannot make us allocate on a bogus length.
        /// </summary>
        public const int MaxStringByteLength = 256;

        /// <summary>Default bound for the inbound message queue of a connection.</summary>
        public const int DefaultMaxQueuedMessages = 1024;

        /// <summary>Size of the frame length prefix: a little-endian unsigned 32-bit integer.</summary>
        public const int LengthPrefixSize = 4;
    }
}
