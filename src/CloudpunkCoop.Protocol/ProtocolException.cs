using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// The only exception type this library raises for malformed wire data.
    /// Everything that can be triggered by hostile or corrupted input surfaces as this type,
    /// so a consumer can catch exactly one thing and close the connection.
    /// </summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(ProtocolErrorKind kind, string message)
            : base(message)
        {
            Kind = kind;
        }

        public ProtocolException(ProtocolErrorKind kind, string message, Exception innerException)
            : base(message, innerException)
        {
            Kind = kind;
        }

        /// <summary>Machine-readable cause of the failure.</summary>
        public ProtocolErrorKind Kind { get; }
    }
}
