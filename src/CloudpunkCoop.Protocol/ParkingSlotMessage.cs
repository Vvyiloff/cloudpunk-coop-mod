using System;
namespace CloudpunkCoop.Protocol
{
    public readonly struct ParkingSlotMessage : IMessage
    {
        public const byte SpawnRequest=4,SpawnGranted=5,SpawnDenied=6;
        public ParkingSlotMessage(uint request,uint revision,byte vehicle,byte kind,int scene,float x,float y,float z,string reason="")
        {
            if(vehicle<1 || vehicle>2 || kind>SpawnDenied || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState,"Invalid parking slot request.");
            Request=request; Revision=revision; Vehicle=vehicle; Kind=kind; Scene=scene; X=x; Y=y; Z=z; Reason=reason??"";
        }
        public uint Request {get;}
        public uint Revision {get;}
        public byte Vehicle {get;}
        public byte Kind {get;}
        public int Scene {get;}
        public float X {get;}
        public float Y {get;}
        public float Z {get;}
        public string Reason {get;}
        public Opcode Opcode=>Opcode.ParkingSlot;
    }
}
