using System;

namespace CloudpunkCoop.Protocol
{
    public readonly struct SeatRequestMessage : IMessage
    {
        public SeatRequestMessage(uint revision) { Revision = revision; }
        public uint Revision { get; }
        public Opcode Opcode => Opcode.SeatRequest;
    }

    public readonly struct SeatStateMessage : IMessage
    {
        public SeatStateMessage(uint revision, byte driver, byte requester, bool bothSeated, float secondsLeft)
        {
            if (driver < 1 || driver > 2 || requester > 2 || !float.IsFinite(secondsLeft) ||
                secondsLeft < 0 || secondsLeft > SeatExchange.RequestLifetimeSeconds ||
                (!bothSeated && (driver != 1 || requester != 0)) ||
                (requester == 0 && secondsLeft != 0))
                throw new ProtocolException(ProtocolErrorKind.InvalidSeatState, "Invalid seat authority state.");
            Revision = revision; Driver = driver; Requester = requester;
            BothSeated = bothSeated; SecondsLeft = secondsLeft;
        }
        public uint Revision { get; }
        public byte Driver { get; }
        public byte Requester { get; }
        public bool BothSeated { get; }
        public float SecondsLeft { get; }
        public Opcode Opcode => Opcode.SeatState;
    }
}
