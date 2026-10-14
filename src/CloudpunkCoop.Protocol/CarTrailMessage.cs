using System;
using System.Text;
namespace CloudpunkCoop.Protocol
{
    public readonly struct TrailVisualState
    {
        public TrailVisualState(string path, bool active, bool emitting, float time, float startWidth, float endWidth)
        { Path = path ?? ""; Active = active; Emitting = emitting; Time = time; StartWidth = startWidth; EndWidth = endWidth; }
        public string Path { get; }
        public bool Active { get; }
        public bool Emitting { get; }
        public float Time { get; }
        public float StartWidth { get; }
        public float EndWidth { get; }
        internal void Validate()
        {
            if (string.IsNullOrEmpty(Path) || Encoding.UTF8.GetByteCount(Path) > 256)
                throw new ProtocolException(ProtocolErrorKind.InvalidStringLength, "Invalid trail path.");
            if (!float.IsFinite(Time) || !float.IsFinite(StartWidth) || !float.IsFinite(EndWidth) || Time < 0 || StartWidth < 0 || EndWidth < 0)
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue, "Invalid trail parameters.");
        }
    }
    public sealed class CarTrailMessage : IMessage
    {
        private readonly TrailVisualState[] _trails;
        public CarTrailMessage(uint sequence, TrailVisualState[] trails)
        {
            if (trails == null) throw new ArgumentNullException(nameof(trails));
            if (trails.Length > 64) throw new ProtocolException(ProtocolErrorKind.InvalidParticleCount, "Too many trails.");
            foreach (var trail in trails) trail.Validate();
            SequenceNumber = sequence; _trails = (TrailVisualState[])trails.Clone();
        }
        public uint SequenceNumber { get; }
        public TrailVisualState[] Trails => (TrailVisualState[])_trails.Clone();
        internal ReadOnlySpan<TrailVisualState> TrailSpan => _trails;
        public Opcode Opcode => Opcode.CarTrail;
    }
}
