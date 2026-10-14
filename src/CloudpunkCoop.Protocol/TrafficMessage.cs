using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    public readonly struct TrafficVehicleState
    {
        public TrafficVehicleState(ushort id, bool active, float x, float y, float z,
            float qx, float qy, float qz, float qw)
        {
            Id = id; Active = active; X = x; Y = y; Z = z;
            Qx = qx; Qy = qy; Qz = qz; Qw = qw;
            foreach (float v in new[] { x, y, z, qx, qy, qz, qw })
                if (!float.IsFinite(v)) throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Nonfinite traffic pose.");
            float norm = qx*qx + qy*qy + qz*qz + qw*qw;
            if (norm < 0.001f || norm > 4f)
                throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Invalid traffic rotation.");
        }
        public ushort Id { get; }
        public bool Active { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Qx { get; }
        public float Qy { get; }
        public float Qz { get; }
        public float Qw { get; }
        internal void Validate()
        {
            foreach (float v in new[] { X,Y,Z,Qx,Qy,Qz,Qw })
                if (!float.IsFinite(v)) throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Nonfinite traffic pose.");
            float norm=Qx*Qx+Qy*Qy+Qz*Qz+Qw*Qw;
            if(norm < 0.001f || norm > 4f)
                throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Invalid traffic rotation.");
        }
    }

    /// <summary>Full bounded ambient pool snapshot. IDs index a fingerprint-validated pool.</summary>
    public sealed class TrafficMessage : IMessage
    {
        public const int MaxVehicles = 512;
        private readonly TrafficVehicleState[] _vehicles;
        public TrafficMessage(uint sequence, int scene, string poolFingerprint, TrafficVehicleState[] vehicles)
        {
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            if (vehicles.Length > MaxVehicles || string.IsNullOrEmpty(poolFingerprint) || poolFingerprint.Length != 64)
                throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Invalid traffic manifest.");
            foreach (char c in poolFingerprint)
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                    throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Invalid traffic fingerprint.");
            var ids = new HashSet<ushort>();
            foreach (var v in vehicles)
            {
                v.Validate();
                if (v.Id >= MaxVehicles || !ids.Add(v.Id))
                    throw new ProtocolException(ProtocolErrorKind.InvalidTrafficState, "Invalid or duplicate traffic ID.");
            }
            SequenceNumber = sequence; Scene = scene; PoolFingerprint = poolFingerprint;
            _vehicles = (TrafficVehicleState[])vehicles.Clone();
        }
        public uint SequenceNumber { get; }
        public int Scene { get; }
        public string PoolFingerprint { get; }
        public TrafficVehicleState[] Vehicles => (TrafficVehicleState[])_vehicles.Clone();
        internal ReadOnlySpan<TrafficVehicleState> VehicleSpan => _vehicles;
        public Opcode Opcode => Opcode.Traffic;
    }
}
