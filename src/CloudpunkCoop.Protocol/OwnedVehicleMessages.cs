using System;

namespace CloudpunkCoop.Protocol
{
    public sealed class OwnedCarMessage : IMessage
    {
        public OwnedCarMessage(byte vehicle, int scene, bool parked, CarStateMessage pose)
        {
            if(vehicle<1 || vehicle>2) throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Invalid owned vehicle.");
            Vehicle=vehicle; Scene=scene; Parked=parked; Pose=pose;
        }
        public byte Vehicle { get; }
        public int Scene { get; }
        public bool Parked { get; }
        public CarStateMessage Pose { get; }
        public Opcode Opcode=>Opcode.OwnedCar;
    }

    public readonly struct OccupancyRequestMessage : IMessage
    {
        public OccupancyRequestMessage(uint revision, byte vehicle)
        {
            if(vehicle>2) throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Invalid boarding target.");
            Revision=revision; Vehicle=vehicle;
        }
        public uint Revision { get; }
        public byte Vehicle { get; }
        public Opcode Opcode=>Opcode.OccupancyRequest;
    }

    public readonly struct OccupancyStateMessage : IMessage
    {
        public OccupancyStateMessage(uint revision,byte p1,byte p2,byte d1,byte d2,byte requester,float seconds)
        {
            bool In(byte player,byte car)=>player==1 ? p1==car : player==2 && p2==car;
            if(p1>2 || p2>2 || d1>2 || d2>2 || requester>2 ||
                ((p1==1 || p2==1) ? !In(d1,1) : d1!=0) ||
                ((p1==2 || p2==2) ? !In(d2,2) : d2!=0) ||
                !float.IsFinite(seconds) || seconds<0 || seconds>10 ||
                (requester==0 ? seconds!=0 : p1==0 || p1!=p2))
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Invalid vehicle occupancy.");
            Revision=revision; Player1Vehicle=p1; Player2Vehicle=p2; Driver1=d1; Driver2=d2;
            Requester=requester; SecondsLeft=seconds;
        }
        public uint Revision { get; }
        public byte Player1Vehicle { get; }
        public byte Player2Vehicle { get; }
        public byte Driver1 { get; }
        public byte Driver2 { get; }
        public byte Requester { get; }
        public float SecondsLeft { get; }
        public byte VehicleOf(byte player)=>player==1 ? Player1Vehicle : player==2 ? Player2Vehicle : (byte)0;
        public byte DriverOf(byte vehicle)=>vehicle==1 ? Driver1 : vehicle==2 ? Driver2 : (byte)0;
        public Opcode Opcode=>Opcode.OccupancyState;
    }
}
