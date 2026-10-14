namespace CloudpunkCoop.Protocol
{
    /// <summary>Why a peer is going away, carried by <see cref="ByeMessage"/>.</summary>
    public enum ByeReason : byte
    {
        None = 0,

        /// <summary>Orderly local shutdown, for example the game is quitting.</summary>
        LocalShutdown = 1,

        /// <summary>Orderly remote shutdown reported by the other peer.</summary>
        PeerShutdown = 2,

        /// <summary>The connection was dropped because of malformed or oversized frames.</summary>
        ProtocolError = 3,

        /// <summary>The host removed the guest from the session.</summary>
        Kicked = 4,

        /// <summary>No traffic within the configured timeout.</summary>
        Timeout = 5,
    }
}
