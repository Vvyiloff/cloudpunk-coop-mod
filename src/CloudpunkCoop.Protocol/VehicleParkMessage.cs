namespace CloudpunkCoop.Protocol
{
    /// <summary>0 request, 1 accepted, 2 denied; only the current driver may request.</summary>
    public readonly struct VehicleParkMessage : IMessage
    {
        public VehicleParkMessage(uint revision,byte vehicle,byte kind)
        {
            if(vehicle<1 || vehicle>2 || kind>2)
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Invalid vehicle parking command.");
            Revision=revision; Vehicle=vehicle; Kind=kind;
        }
        public uint Revision { get; }
        public byte Vehicle { get; }
        public byte Kind { get; }
        public Opcode Opcode=>Opcode.VehiclePark;
    }
}
