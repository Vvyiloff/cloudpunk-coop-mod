using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Cumulative scaled gameplay seconds within a caller-bound session. The caller normalizes
    /// and validates the gameplay clock; a peer cannot select its own inventory or effects.
    /// </summary>
    public sealed class PersonalClockMessage : IMessage
    {
        public const double MaxElapsedScaledSeconds = 1e9;

        public PersonalClockMessage(uint sequence, long sessionId, double elapsedScaledSeconds,
            long eligibleTicks=0, double lastTickStart=0)
        {
            if (sessionId <= 0 || !double.IsFinite(elapsedScaledSeconds) ||
                elapsedScaledSeconds < 0 || elapsedScaledSeconds > MaxElapsedScaledSeconds ||
                eligibleTicks<0 || !double.IsFinite(lastTickStart) || lastTickStart<0 || lastTickStart>elapsedScaledSeconds)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Invalid personal clock.");
            Sequence = sequence;
            SessionId = sessionId;
            ElapsedScaledSeconds = elapsedScaledSeconds;
            EligibleTicks=eligibleTicks;LastTickStart=lastTickStart;
        }

        public uint Sequence { get; }
        public long SessionId { get; }
        public double ElapsedScaledSeconds { get; }
        public long EligibleTicks {get;}
        public double LastTickStart {get;}
        public Opcode Opcode => Opcode.PersonalClock;
    }

    /// <summary>
    /// Pure receipt-time budget validation, without game or inventory access. The first sample
    /// establishes a baseline only, preventing offline decay on reconnect. Callers supply a
    /// finite monotonic wall clock and normalize/validate their gameplay clock separately.
    /// </summary>
    public sealed class PersonalClockTracker
    {
        // Application defenses, NOT native gameplay constants.
        public const double MaxScaledSecondsPerWallSecond = 4;
        public const double MaxBurstScaledSeconds = 0.5;
        private long _expectedSessionId;
        private PersonalClockMessage? _accepted;
        private double _received;
        private double _credit;

        public PersonalClockTracker(long expectedSessionId) => Reset(expectedSessionId);

        public double AcceptedElapsed => _accepted?.ElapsedScaledSeconds ?? 0;
        public long AcceptedTicks=>_accepted?.EligibleTicks ?? 0;
        public bool HasSample=>_accepted!=null;

        /// <summary>
        /// Rejections preserve all state and return zero advance. Unused credit is capped only
        /// after subtraction so long legitimate receipt gaps retain their full rate budget.
        /// </summary>
        public bool Receive(PersonalClockMessage message, double now, out double advance)
        {
            advance = 0;
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (message.SessionId != _expectedSessionId) return false;
            if (_accepted != null)
            {
                if (now < _received || unchecked((int)(message.Sequence - _accepted.Sequence)) <= 0)
                    return false;
                double delta = message.ElapsedScaledSeconds - _accepted.ElapsedScaledSeconds;
                if (delta < 0 || message.EligibleTicks<_accepted.EligibleTicks ||
                    delta>0 && message.EligibleTicks==_accepted.EligibleTicks ||
                    message.EligibleTicks>_accepted.EligibleTicks && message.LastTickStart<_accepted.ElapsedScaledSeconds)
                    return false;
                double budget = _credit + MaxScaledSecondsPerWallSecond * (now - _received);
                if (delta > budget) return false;
                _credit = Math.Min(MaxBurstScaledSeconds, budget - delta);
                advance = delta;
            }
            _accepted = message;
            _received = now;
            return true;
        }

        public bool Fresh(double now) => _accepted != null && double.IsFinite(now) &&
            now >= _received && now - _received < 3;

        public void Reset(long expectedSessionId)
        {
            if (expectedSessionId <= 0) throw new ArgumentOutOfRangeException(nameof(expectedSessionId));
            _expectedSessionId = expectedSessionId;
            _accepted = null;
            _received = 0;
            _credit = 0;
        }
    }
}
