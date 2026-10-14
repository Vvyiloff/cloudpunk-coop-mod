using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>Host-owned consent state. Network identity comes from the connection, not payload.</summary>
    public sealed class SeatExchange
    {
        public const double RequestLifetimeSeconds = 10d;
        public uint Revision { get; private set; }
        public byte Driver { get; private set; } = 1;
        public byte Requester { get; private set; }
        public double ExpiresAt { get; private set; }
        public bool BothSeated { get; private set; }

        public bool SetBothSeated(bool seated)
        {
            if (BothSeated == seated) return false;
            BothSeated = seated;
            Requester = 0;
            ExpiresAt = 0;
            if (!seated) Driver = 1;
            Revision++;
            return true;
        }

        /// <summary>Same player cancels; the other confirms only the matching current revision.</summary>
        public bool Press(byte player, uint expectedRevision, double now)
        {
            if (!double.IsFinite(now) || player < 1 || player > 2 || !BothSeated ||
                expectedRevision != Revision) return false;
            if (Expire(now)) return false; // Old request cannot be acknowledged after timeout.
            if (Requester == player)
            {
                Requester = 0;
                ExpiresAt = 0;
            }
            else if (Requester != 0)
            {
                Driver = Driver == 1 ? (byte)2 : (byte)1;
                Requester = 0;
                ExpiresAt = 0;
            }
            else
            {
                Requester = player;
                ExpiresAt = now + RequestLifetimeSeconds;
            }
            Revision++;
            return true;
        }

        public bool Expire(double now)
        {
            if (Requester == 0 || now < ExpiresAt) return false;
            Requester = 0;
            ExpiresAt = 0;
            Revision++;
            return true;
        }
    }
}
