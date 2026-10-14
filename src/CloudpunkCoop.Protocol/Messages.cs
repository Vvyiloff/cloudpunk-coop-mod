using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Common shape of every wire message. Implementations are immutable value types
    /// containing only primitives (plus length-bounded strings), so nothing here can
    /// reference UnityEngine and nothing depends on memory layout or machine endianness.
    /// </summary>
    public interface IMessage
    {
        /// <summary>Wire discriminator, written as the first byte of a payload.</summary>
        Opcode Opcode { get; }
    }

    /// <summary>
    /// HELLO. First message a joining peer sends. Carries the protocol version it speaks
    /// and a display name for the UI.
    /// </summary>
    public readonly struct HelloMessage : IMessage
    {
        public HelloMessage(string protocolVersion, string displayName)
        {
            ProtocolVersion = protocolVersion ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string ProtocolVersion { get; }
        public string DisplayName { get; }

        public Opcode Opcode => Opcode.Hello;

        public override string ToString() =>
            FormattableString.Invariant($"Hello(ver={ProtocolVersion}, name={DisplayName})");
    }

    /// <summary>
    /// WELCOME. Host's answer to a valid HELLO, assigning the role the guest will play.
    /// </summary>
    public readonly struct WelcomeMessage : IMessage
    {
        public WelcomeMessage(PeerRole role, string protocolVersion)
        {
            Role = role;
            ProtocolVersion = protocolVersion ?? string.Empty;
        }

        public PeerRole Role { get; }

        /// <summary>Protocol version the host accepted.</summary>
        public string ProtocolVersion { get; }

        public Opcode Opcode => Opcode.Welcome;

        public override string ToString() =>
            FormattableString.Invariant($"Welcome(role={Role}, ver={ProtocolVersion})");
    }

    /// <summary>
    /// CARSTATE. Host streams the authoritative car pose to the guest every snapshot tick.
    /// All angles are radians, all units are Unity metres / metres-per-second.
    /// </summary>
    public readonly struct CarStateMessage : IMessage
    {
        public CarStateMessage(
            float positionX, float positionY, float positionZ,
            float rotationX, float rotationY, float rotationZ, float rotationW,
            float linearVelocityX, float linearVelocityY, float linearVelocityZ,
            float angularVelocityX, float angularVelocityY, float angularVelocityZ,
            float steerAngle, float damageLevel, uint sequenceNumber)
        {
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
            LinearVelocityX = linearVelocityX;
            LinearVelocityY = linearVelocityY;
            LinearVelocityZ = linearVelocityZ;
            AngularVelocityX = angularVelocityX;
            AngularVelocityY = angularVelocityY;
            AngularVelocityZ = angularVelocityZ;
            SteerAngle = steerAngle;
            DamageLevel = damageLevel;
            SequenceNumber = sequenceNumber;
        }

        public float PositionX { get; }
        public float PositionY { get; }
        public float PositionZ { get; }

        /// <summary>Rotation quaternion x component.</summary>
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float RotationW { get; }

        public float LinearVelocityX { get; }
        public float LinearVelocityY { get; }
        public float LinearVelocityZ { get; }

        public float AngularVelocityX { get; }
        public float AngularVelocityY { get; }
        public float AngularVelocityZ { get; }

        /// <summary>Front wheel steering angle in radians.</summary>
        public float SteerAngle { get; }

        /// <summary>Normalised vehicle damage, 0 is pristine and 1 is destroyed.</summary>
        public float DamageLevel { get; }

        /// <summary>Monotonically increasing snapshot counter, used to discard stale poses.</summary>
        public uint SequenceNumber { get; }

        public Opcode Opcode => Opcode.CarState;

        public override string ToString() =>
            FormattableString.Invariant(
                $"CarState(seq={SequenceNumber}, pos=({PositionX}, {PositionY}, {PositionZ}), steer={SteerAngle}, dmg={DamageLevel})");
    }

    /// <summary>
    /// INPUT. Guest streams driving intent to the host every input tick.
    /// Axes are normalised to -1..1; <see cref="Ascend"/> is negative to descend.
    /// </summary>
    public readonly struct InputMessage : IMessage
    {
        public InputMessage(
            float throttle, float brake, float steer, float ascend, float boost,
            uint sequenceNumber)
        {
            Throttle = throttle;
            Brake = brake;
            Steer = steer;
            Ascend = ascend;
            Boost = boost;
            SequenceNumber = sequenceNumber;
        }

        /// <summary>0..1 engine throttle.</summary>
        public float Throttle { get; }

        /// <summary>0..1 brake.</summary>
        public float Brake { get; }

        /// <summary>-1..1 steering axis.</summary>
        public float Steer { get; }

        /// <summary>-1..1 vertical thrust; negative descends.</summary>
        public float Ascend { get; }

        /// <summary>0..1 boost.</summary>
        public float Boost { get; }

        /// <summary>Monotonically increasing input counter, used to discard stale input.</summary>
        public uint SequenceNumber { get; }

        public Opcode Opcode => Opcode.Input;

        public override string ToString() =>
            FormattableString.Invariant(
                $"Input(seq={SequenceNumber}, throttle={Throttle}, brake={Brake}, steer={Steer}, ascend={Ascend}, boost={Boost})");
    }

    /// <summary>
    /// SEAT. Announces which peer currently holds the driving seat.
    /// </summary>
    public readonly struct SeatMessage : IMessage
    {
        public SeatMessage(byte seatId, bool isDriving)
        {
            SeatId = seatId;
            IsDriving = isDriving;
        }

        /// <summary>Seat slot the message refers to. 0 is the driver's seat.</summary>
        public byte SeatId { get; }

        /// <summary>True when the peer that sent this message currently drives.</summary>
        public bool IsDriving { get; }

        public Opcode Opcode => Opcode.Seat;

        public override string ToString() =>
            FormattableString.Invariant($"Seat(id={SeatId}, driving={IsDriving})");
    }

    /// <summary>
    /// PING. Round-trip probe. <see cref="SentAtMilliseconds"/> is echoed back verbatim in
    /// <see cref="PongMessage"/> so the originator can measure RTT without keeping state.
    /// </summary>
    public readonly struct PingMessage : IMessage
    {
        public PingMessage(uint sequenceNumber, long sentAtMilliseconds)
        {
            SequenceNumber = sequenceNumber;
            SentAtMilliseconds = sentAtMilliseconds;
        }

        public uint SequenceNumber { get; }

        /// <summary>Originator's monotonic clock reading, in milliseconds.</summary>
        public long SentAtMilliseconds { get; }

        public Opcode Opcode => Opcode.Ping;

        public override string ToString() =>
            FormattableString.Invariant($"Ping(seq={SequenceNumber}, sentAt={SentAtMilliseconds})");
    }

    /// <summary>
    /// PONG. Echo of a <see cref="PingMessage"/>. Both fields are copied unchanged from the ping.
    /// </summary>
    public readonly struct PongMessage : IMessage
    {
        public PongMessage(uint sequenceNumber, long sentAtMilliseconds)
        {
            SequenceNumber = sequenceNumber;
            SentAtMilliseconds = sentAtMilliseconds;
        }

        public uint SequenceNumber { get; }

        /// <summary>The <see cref="PingMessage.SentAtMilliseconds"/> value being echoed.</summary>
        public long SentAtMilliseconds { get; }

        public Opcode Opcode => Opcode.Pong;

        public override string ToString() =>
            FormattableString.Invariant($"Pong(seq={SequenceNumber}, sentAt={SentAtMilliseconds})");
    }

    /// <summary>
    /// BYE. Clean disconnect announcement, sent before the socket is torn down.
    /// </summary>
    public readonly struct ByeMessage : IMessage
    {
        public ByeMessage(ByeReason reason)
        {
            Reason = reason;
        }

        public ByeReason Reason { get; }

        public Opcode Opcode => Opcode.Bye;

        public override string ToString() =>
            FormattableString.Invariant($"Bye(reason={Reason})");
    }
}
