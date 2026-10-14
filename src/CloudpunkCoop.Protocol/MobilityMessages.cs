using System;
namespace CloudpunkCoop.Protocol
{
    public readonly struct MobilityRequestMessage : IMessage
    {
        public MobilityRequestMessage(uint revision, bool onFoot) { Revision = revision; OnFoot = onFoot; }
        public uint Revision { get; }
        public bool OnFoot { get; }
        public Opcode Opcode => Opcode.MobilityRequest;
    }

    public readonly struct MobilityStateMessage : IMessage
    {
        public MobilityStateMessage(uint revision, bool onFoot, bool accepted, string reason,
            int scene, float x, float y, float z, float yaw)
        {
            foreach (float f in new[] { x, y, z, yaw })
                if (!float.IsFinite(f)) throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Invalid mobility pose.");
            Revision = revision; OnFoot = onFoot; Accepted = accepted; Reason = reason ?? "";
            Scene = scene; X = x; Y = y; Z = z; Yaw = yaw;
        }
        public uint Revision { get; }
        public bool OnFoot { get; }
        public bool Accepted { get; }
        public string Reason { get; }
        public int Scene { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }
        public Opcode Opcode => Opcode.MobilityState;
    }

    public readonly struct FootPoseMessage : IMessage
    {
        public FootPoseMessage(uint sequence, int scene, bool onFoot, float x, float y, float z,
            float yaw, byte animation, byte direction, string clip = "", int frame = 0)
        {
            foreach (float f in new[] { x, y, z, yaw })
                if (!float.IsFinite(f)) throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Invalid actor pose.");
            if (animation > 5 || direction > 7)
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Invalid actor animation.");
            if (frame < 0 || frame > 100000 || (clip?.Length ?? 0) > 256)
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Invalid actor animation frame or clip.");
            Sequence = sequence; Scene = scene; OnFoot = onFoot;
            X = x; Y = y; Z = z; Yaw = yaw; Animation = animation; Direction = direction;
            Clip = clip ?? ""; Frame = frame;
        }
        public uint Sequence { get; }
        public int Scene { get; }
        public bool OnFoot { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }
        public byte Animation { get; }
        public byte Direction { get; }
        public string Clip { get; }
        public int Frame { get; }
        public Opcode Opcode => Opcode.FootPose;
    }
}
