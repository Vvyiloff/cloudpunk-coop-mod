using System;
using System.Text;

namespace CloudpunkCoop.Protocol
{
    public readonly struct NpcVisualState
    {
        public NpcVisualState(string id, string model, bool crowd, bool active, float x, float y, float z,
            float yaw, float sx, float sy, float sz, byte animation, byte direction, string clip, int frame)
        {
            ValidateString(id); ValidateString(model); ValidateString(clip, true);
            foreach (float f in new[] { x,y,z,yaw,sx,sy,sz })
                if (!float.IsFinite(f)) throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Nonfinite NPC pose.");
            if (animation > 5 || direction > 7 || frame < 0 || frame > 100000)
                throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Invalid NPC animation.");
            Id = id; Model = model; Crowd = crowd; Active = active;
            X = x; Y = y; Z = z; Yaw = yaw; ScaleX = sx; ScaleY = sy; ScaleZ = sz;
            Animation = animation; Direction = direction; Clip = clip; Frame = frame;
        }
        public string Id { get; }
        public string Model { get; }
        public bool Crowd { get; }
        public bool Active { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }
        public byte Animation { get; }
        public byte Direction { get; }
        public string Clip { get; }
        public int Frame { get; }
        internal void Validate()
        {
            ValidateString(Id); ValidateString(Model); ValidateString(Clip, true);
            foreach (float f in new[] { X,Y,Z,Yaw,ScaleX,ScaleY,ScaleZ })
                if (!float.IsFinite(f)) throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Nonfinite NPC pose.");
            if (Animation > 5 || Direction > 7 || Frame < 0 || Frame > 100000)
                throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Invalid NPC animation.");
        }
        private static void ValidateString(string value, bool empty = false)
        {
            if (value == null || (!empty && value.Length == 0) || Encoding.UTF8.GetByteCount(value) > 256)
                throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Invalid NPC identity string.");
        }
    }

    public sealed class NpcMessage : IMessage
    {
        public const int MaxPerChunk = 64, MaxChunks = 64;
        private readonly NpcVisualState[] _npcs;
        public NpcMessage(uint epoch, uint sequence, int scene, byte chunk, byte chunks, NpcVisualState[] npcs)
        {
            if (npcs == null) throw new ArgumentNullException(nameof(npcs));
            if (chunks == 0 || chunks > MaxChunks || chunk >= chunks || npcs.Length > MaxPerChunk)
                throw new ProtocolException(ProtocolErrorKind.InvalidNpcState, "Invalid NPC frame chunk.");
            foreach (var npc in npcs) npc.Validate();
            Epoch = epoch; Sequence = sequence; Scene = scene; Chunk = chunk; Chunks = chunks;
            _npcs = (NpcVisualState[])npcs.Clone();
        }
        public uint Epoch { get; }
        public uint Sequence { get; }
        public int Scene { get; }
        public byte Chunk { get; }
        public byte Chunks { get; }
        public NpcVisualState[] Npcs => (NpcVisualState[])_npcs.Clone();
        internal ReadOnlySpan<NpcVisualState> NpcSpan => _npcs;
        public Opcode Opcode => Opcode.Npc;
    }
}
