using System;
using System.Text;

namespace CloudpunkCoop.Protocol
{
    /// <summary>Immutable particle state; paths are relative to the car visual root.</summary>
    public readonly struct ParticleVisualState
    {
        public const int MaxPathByteLength = 256;

        public ParticleVisualState(string path, bool active, bool playing,
            float rate, float speed, float size, float lifetime,
            float scaleX, float scaleY, float scaleZ,
            float colorR, float colorG, float colorB, float colorA,
            float positionX = 0f, float positionY = 0f, float positionZ = 0f,
            float rotationX = 0f, float rotationY = 0f, float rotationZ = 0f, float rotationW = 1f)
        {
            Path = path ?? string.Empty;
            Active = active;
            Playing = playing;
            Rate = rate;
            Speed = speed;
            Size = size;
            Lifetime = lifetime;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
            ColorR = colorR;
            ColorG = colorG;
            ColorB = colorB;
            ColorA = colorA;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
        }

        public string Path { get; }
        public bool Active { get; }
        public bool Playing { get; }
        public float Rate { get; }
        public float Speed { get; }
        public float Size { get; }
        public float Lifetime { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }
        public float ColorR { get; }
        public float ColorG { get; }
        public float ColorB { get; }
        public float ColorA { get; }

        // Pose relative to the car visual root; rotation need not be normalized.
        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }

        internal void Validate()
        {
            if (Encoding.UTF8.GetByteCount(Path ?? string.Empty) > MaxPathByteLength)
                throw new ProtocolException(ProtocolErrorKind.InvalidStringLength,
                    "Particle path exceeds 256 UTF-8 bytes.");
            ValidateParameter(Rate);
            ValidateFinite(Speed);
            ValidateParameter(Size);
            ValidateParameter(Lifetime);
            ValidateFinite(ScaleX);
            ValidateFinite(ScaleY);
            ValidateFinite(ScaleZ);
            ValidateParameter(ColorR);
            ValidateParameter(ColorG);
            ValidateParameter(ColorB);
            ValidateParameter(ColorA);
            ValidatePosition(PositionX);
            ValidatePosition(PositionY);
            ValidatePosition(PositionZ);
            ValidateFinite(RotationX);
            ValidateFinite(RotationY);
            ValidateFinite(RotationZ);
            ValidateFinite(RotationW);
            float normSquared = RotationX * RotationX + RotationY * RotationY +
                RotationZ * RotationZ + RotationW * RotationW;
            if (!float.IsFinite(normSquared) || normSquared <= 0f)
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue,
                    "Particle rotation must have a finite nonzero squared norm.");
        }

        private static void ValidatePosition(float value)
        {
            if (!float.IsFinite(value) || Math.Abs(value) > 8192f)
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue,
                    "Particle relative position must be finite and within +/-8192.");
        }

        private static void ValidateParameter(float value)
        {
            if (!float.IsFinite(value) || value < 0)
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue,
                    "Particle parameters must be finite and nonnegative.");
        }

        private static void ValidateFinite(float value)
        {
            // Native particle templates can use a reversed velocity or mirrored scale.
            if (!float.IsFinite(value))
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleValue,
                    "Particle parameters must be finite.");
        }
    }

    /// <summary>
    /// CARVISUAL: opcode, uint32 sequence, byte count, then each particle's int32
    /// UTF-8 path length and path bytes, two boolean bytes and eighteen float32 fields: rate, speed, size, lifetime,
    /// scale xyz, color rgba, car-relative position xyz and rotation xyzw.
    /// All multibyte primitives use the existing little-endian protocol encoding.
    /// </summary>
    public sealed class CarVisualMessage : IMessage
    {
        public const int MaxParticleCount = 64;
        private readonly ParticleVisualState[] _particles;

        public CarVisualMessage(uint sequenceNumber, ParticleVisualState[] particles)
        {
            if (particles == null)
                throw new ArgumentNullException(nameof(particles));
            if (particles.Length > MaxParticleCount)
                throw new ProtocolException(ProtocolErrorKind.InvalidParticleCount,
                    "CarVisual allows at most 64 particles.");
            SequenceNumber = sequenceNumber;
            _particles = (ParticleVisualState[])particles.Clone();
        }

        public uint SequenceNumber { get; }
        public ParticleVisualState[] Particles => (ParticleVisualState[])_particles.Clone();
        public Opcode Opcode => Opcode.CarVisual;
        internal ReadOnlySpan<ParticleVisualState> ParticleSpan => _particles;

        public override string ToString() =>
            FormattableString.Invariant($"CarVisual(seq={SequenceNumber}, particles={_particles.Length})");
    }
}
