using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Host-owned, main-thread-serialized occupancy and seat-swap authority.
    /// The caller obtains player identity from the connection, never the payload.
    /// Vehicle IDs identify cars, not the player entitled to drive them.
    /// </summary>
    public sealed class VehicleOccupancy
    {
        public const double RequestLifetimeSeconds = 10d;

        public uint Revision { get; private set; }
        public byte Player1Vehicle { get; private set; }
        public byte Player2Vehicle { get; private set; }
        public byte Driver1 { get; private set; }
        public byte Driver2 { get; private set; }
        public byte Requester { get; private set; }
        public double ExpiresAt { get; private set; }
        public byte RequestedVehicle { get; private set; }

        /// <summary>Zero is on foot; one and two are the host and guest cars.</summary>
        public bool SetPlayerVehicle(byte player, byte vehicle, uint expectedRevision)
        {
            if (!ValidPlayer(player) || vehicle > 2 || expectedRevision != Revision)
                return false;

            byte previous = VehicleOf(player);
            if (previous == vehicle) return false;

            if (player == 1) Player1Vehicle = vehicle;
            else Player2Vehicle = vehicle;

            // A departing passenger leaves the driver untouched. A departing
            // driver promotes the only possible remaining occupant.
            if (previous != 0 && DriverOf(previous) == player)
                SetDriver(previous, VehicleOf(Other(player)) == previous ? Other(player) : (byte)0);
            if (vehicle != 0 && DriverOf(vehicle) == 0)
                SetDriver(vehicle, player);

            ClearConsent();
            AdvanceRevision();
            return true;
        }

        /// <summary>Remove the guest without resetting the host's car or seat.</summary>
        public void DisconnectGuest()
        {
            if (Player2Vehicle != 0)
            {
                SetPlayerVehicle(2, 0, Revision);
            }
            else if (Requester != 0)
            {
                ClearConsent();
                AdvanceRevision();
            }
        }

        /// <summary>
        /// Both players must occupy the same car. First press requests, the same
        /// player cancels, and the other confirms using the current revision.
        /// An expired press clears consent and returns false; refresh before retrying.
        /// </summary>
        public bool PressSwap(byte player, uint expectedRevision, double now)
        {
            if (!ValidPlayer(player) || !double.IsFinite(now) || expectedRevision != Revision ||
                Player1Vehicle == 0 || Player1Vehicle != Player2Vehicle)
                return false;
            if (Expire(now)) return false;

            if (Requester == player)
            {
                ClearConsent();
            }
            else if (Requester != 0)
            {
                SetDriver(RequestedVehicle, Other(DriverOf(RequestedVehicle)));
                ClearConsent();
            }
            else
            {
                Requester = player;
                RequestedVehicle = Player1Vehicle;
                ExpiresAt = now + RequestLifetimeSeconds;
            }

            AdvanceRevision();
            return true;
        }

        public bool Expire(double now)
        {
            if (!double.IsFinite(now) || Requester == 0 || now < ExpiresAt) return false;
            ClearConsent();
            AdvanceRevision();
            return true;
        }

        private static bool ValidPlayer(byte player) => player == 1 || player == 2;
        private static byte Other(byte player) => player == 1 ? (byte)2 : (byte)1;
        private byte VehicleOf(byte player) => player == 1 ? Player1Vehicle : Player2Vehicle;
        private byte DriverOf(byte vehicle) => vehicle == 1 ? Driver1 : Driver2;

        private void SetDriver(byte vehicle, byte player)
        {
            if (vehicle == 1) Driver1 = player;
            else Driver2 = player;
        }

        private void ClearConsent()
        {
            Requester = 0;
            RequestedVehicle = 0;
            ExpiresAt = 0;
        }

        private void AdvanceRevision() => Revision = unchecked(Revision + 1);
    }
}
