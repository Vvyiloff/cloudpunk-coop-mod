using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Pure host authority. Begin/SetLine/End/Reset accept authoritative observations only;
    /// obtain request player identity from the connection, never its payload. Both players
    /// may continue, but exactly one grant is issued per observed runtime line index.
    /// Counters survive End/Reset for this instance; persistence/session identity is external.
    /// </summary>
    public sealed class DialogueAuthority
    {
        private readonly object _gate = new();
        public ulong Generation { get; private set; }
        public ulong EventId { get; private set; }
        public bool Active { get; private set; }
        public byte OwnerPlayer { get; private set; }
        public string DialogId { get; private set; } = "";
        public int LineIndex { get; private set; } = -1;
        public int LineId { get; private set; } = -1;
        public bool Ready { get; private set; }
        public bool AwaitingNextLine { get; private set; }
        public DialogueContinueEvent? LastContinue { get; private set; }

        /// <summary>Starts a fresh conversation; end the existing one before replacing it.</summary>
        public ulong Begin(byte ownerPlayer, string dialogId, int lineIndex, int lineId, bool ready)
        {
            if (!ValidPlayer(ownerPlayer)) throw new ArgumentOutOfRangeException(nameof(ownerPlayer));
            if (string.IsNullOrWhiteSpace(dialogId)) throw new ArgumentException("Dialog ID required.", nameof(dialogId));
            if (lineIndex < 0) throw new ArgumentOutOfRangeException(nameof(lineIndex));
            lock (_gate)
            {
                if (Active) throw new InvalidOperationException("End the active conversation before Begin.");
                ulong generation = checked(Generation + 1);
                ulong eventId = checked(EventId + 1);
                Generation = generation; EventId = eventId;
                Active = true; OwnerPlayer = ownerPlayer; DialogId = dialogId;
                LineIndex = lineIndex; LineId = lineId; Ready = ready;
                AwaitingNextLine = false; LastContinue = null;
                return Generation;
            }
        }

        /// <summary>
        /// Same-line observations update readiness before a grant, never rearm one afterwards.
        /// A strictly newer index releases the latch; repeated authored line IDs are allowed.
        /// </summary>
        public bool SetLine(ulong generation, int lineIndex, int lineId, bool ready)
        {
            lock (_gate)
            {
                if (!Active || generation != Generation || lineIndex < 0 || lineIndex < LineIndex)
                    return false;
                if (lineIndex == LineIndex)
                {
                    if (lineId != LineId || AwaitingNextLine) return false;
                    if (Ready == ready) return true;
                }
                ulong eventId = checked(EventId + 1);
                LineIndex = lineIndex; LineId = lineId; Ready = ready;
                AwaitingNextLine = false; EventId = eventId;
                return true;
            }
        }

        public bool OwnsConversation(byte player, ulong generation)
        {
            lock (_gate) return Active && generation == Generation && ValidPlayer(player) && player == OwnerPlayer;
        }

        /// <summary>
        /// ready is the host's fresh native readiness check, not a client assertion.
        /// A grant reserves advancement BEFORE calling native code. Even native failure
        /// cannot rearm this line; recovery requires an authoritative new line or End.
        /// </summary>
        public bool TryContinue(byte player, ulong generation, int lineIndex, int lineId, bool ready)
            => TryContinue(player, generation, lineIndex, lineId, ready, out _);

        public bool TryContinue(byte player, ulong generation, int lineIndex, int lineId, bool ready,
            out DialogueContinueEvent? grant)
        {
            lock (_gate)
            {
                grant = null;
                if (!Active || !ValidPlayer(player) || generation != Generation || lineIndex != LineIndex ||
                    lineId != LineId || !Ready || !ready || AwaitingNextLine) return false;
                ulong eventId = checked(EventId + 1);
                grant = new DialogueContinueEvent(eventId, Generation, player, DialogId, LineIndex, LineId);
                EventId = eventId; LastContinue = grant; Ready = false; AwaitingNextLine = true;
                return true;
            }
        }

        /// <summary>A stale end cannot terminate a newer conversation.</summary>
        public bool End(ulong generation)
        {
            lock (_gate)
            {
                if (!Active || generation != Generation) return false;
                Invalidate();
                return true;
            }
        }

        /// <summary>Invalidates even idle state without ever zeroing or recycling counters.</summary>
        public void Reset()
        {
            lock (_gate) Invalidate();
        }

        private void Invalidate()
        {
            ulong generation = checked(Generation + 1);
            ulong eventId = checked(EventId + 1);
            Generation = generation; EventId = eventId;
            Active = false; OwnerPlayer = 0; DialogId = ""; LineIndex = LineId = -1;
            Ready = false; AwaitingNextLine = false; LastContinue = null;
        }

        private static bool ValidPlayer(byte player) => player == 1 || player == 2;
    }

    /// <summary>Immutable grant identity for native execution and subsequent presentation.</summary>
    public sealed record DialogueContinueEvent(ulong EventId, ulong Generation, byte Player,
        string DialogId, int LineIndex, int LineId);
}
