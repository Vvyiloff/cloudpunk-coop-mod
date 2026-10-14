using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>Independent region presence; never a command to move the other player.</summary>
    public sealed class RegionPresenceMessage : IMessage
    {
        public RegionPresenceMessage(uint sequence, int scene, bool ready)
        {
            if (scene < -1 || scene > 4095 || ready && scene < 0)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Invalid region presence.");
            Sequence = sequence; Scene = scene; Ready = ready;
        }
        public uint Sequence { get; }
        public int Scene { get; }
        public bool Ready { get; }
        public Opcode Opcode => Opcode.RegionPresence;
    }

    /// <summary>Rejects old presence updates, including sequence wrap, and expires loading peers.</summary>
    public sealed class RegionPresenceTracker
    {
        private RegionPresenceMessage? _peer;
        private double _received;
        public int PeerScene => _peer?.Scene ?? -1;
        public bool Receive(RegionPresenceMessage message, double now)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (_peer != null && unchecked((int)(message.Sequence - _peer.Sequence)) <= 0) return false;
            _peer = message; _received = now; return true;
        }
        public bool Fresh(double now) => _peer != null && double.IsFinite(now) && now >= _received && now - _received < 3;
        public bool Ready(double now) => Fresh(now) && _peer != null && _peer.Ready;
        public bool SameRegion(int localScene, bool localReady, double now) =>
            localReady && localScene >= 0 && Fresh(now) && _peer != null && _peer.Ready && _peer.Scene == localScene;
        public void Reset() { _peer = null; _received = 0; }
    }
}
