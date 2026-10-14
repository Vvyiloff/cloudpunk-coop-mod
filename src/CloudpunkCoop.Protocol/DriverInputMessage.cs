namespace CloudpunkCoop.Protocol
{
    public readonly struct DriverInputMessage : IMessage
    {
        public DriverInputMessage(uint revision, uint sequence, float drive, float steer, float altitude, float strafe)
        {
            foreach (float v in new[] { drive, steer, altitude, strafe })
                if (!float.IsFinite(v) || v < -1 || v > 1)
                    throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Driver axes must be in [-1,1].");
            Revision = revision; Sequence = sequence; Drive = drive; Steer = steer; Altitude = altitude; Strafe = strafe;
        }
        public uint Revision { get; }
        public uint Sequence { get; }
        public float Drive { get; }
        public float Steer { get; }
        public float Altitude { get; }
        public float Strafe { get; }
        public Opcode Opcode => Opcode.DriverInput;
    }
}
