namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Classification of a <see cref="ProtocolException"/>, so a consumer can log or react
    /// without parsing the message text.
    /// </summary>
    public enum ProtocolErrorKind
    {
        InvalidSeatState,
        InvalidTrafficState,
        InvalidNpcState,
        /// <summary>The frame length prefix was negative.</summary>
        InvalidFrameLength = 0,

        /// <summary>The frame length prefix exceeded the configured maximum payload size.</summary>
        OversizedFrame = 1,

        /// <summary>The opcode byte is not one of the known <see cref="Opcode"/> values.</summary>
        UnknownOpcode = 2,

        /// <summary>The payload ended in the middle of a field.</summary>
        TruncatedPayload = 3,

        /// <summary>A string field declared an illegal byte count.</summary>
        InvalidStringLength = 4,

        /// <summary>Bytes were left over after a complete message was decoded.</summary>
        TrailingBytes = 5,

        /// <summary>The read buffer would have had to grow beyond its hard bound.</summary>
        BufferOverflow = 6,

        /// <summary>The connection ended in the middle of a message.</summary>
        ConnectionClosed = 7,

        /// <summary>An outbound or inbound connect attempt did not complete in time.</summary>
        ConnectTimeout = 8,

        /// <summary>A visual message declared more than 64 particles.</summary>
        InvalidParticleCount = 9,

        /// <summary>A particle parameter or UTF-8 path is malformed.</summary>
        InvalidParticleValue = 10,
        InvalidCampaignState = 11,
        InvalidBlobLength = 12,
    }
}
