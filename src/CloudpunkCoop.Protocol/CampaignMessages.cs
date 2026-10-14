using System;
using System.Text;

namespace CloudpunkCoop.Protocol
{
    /// <summary>A bounded piece of an opaque campaign snapshot, ordered by Index.</summary>
    public sealed class CampaignChunkMessage : IMessage
    {
        public const int MaxChunkDataLength = 16384;
        public const int MaxChunks = 64;
        public const int MaxAssembledDataLength = 1024 * 1024;
        private readonly byte[] _data;

        public CampaignChunkMessage(uint revision, int scene, ushort index, ushort count, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ValidateHeader(index, count);
            if (data.Length > MaxChunkDataLength)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Campaign chunk exceeds 16384 bytes.");
            Revision = revision; Scene = scene; Index = index; Count = count;
            _data = (byte[])data.Clone();
        }

        internal static void ValidateHeader(ushort index, ushort count)
        {
            if (count == 0 || count > MaxChunks || index >= count)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Invalid campaign chunk index/count.");
        }

        public uint Revision { get; }
        public int Scene { get; }
        public ushort Index { get; }
        public ushort Count { get; }
        public byte[] Data => (byte[])_data.Clone();
        public ReadOnlySpan<byte> DataSpan => _data;
        public Opcode Opcode => Opcode.CampaignChunk;
    }

    /// <summary>
    /// Requests host-authoritative campaign operations. Kind 1 uses a character GUID,
    /// kind 2 a dialog ID and Argument=lineID, kinds 3..6 use Item.name as Target.
    /// The host resolves these opaque game identifiers and authorizes the operation,
    /// rejecting session zero and stale session/conversation/line identities as appropriate.
    /// </summary>
    public sealed class CampaignActionMessage : IMessage
    {
        public const byte Interact = 1, ContinueDialog = 2, UseInventory = 3, Equip = 4, Buy = 5, Sell = 6;

        public CampaignActionMessage(uint requestId, uint revision, int scene, byte kind,
            string target, string argument, int amount, long sessionId = 0,
            ulong conversation = 0, int lineIndex = -1)
        {
            CampaignValidation.RequestId(requestId);
            if (kind < Interact || kind > Sell || amount < 1 || amount > 100)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Invalid campaign action kind/amount.");
            if (sessionId < 0 || lineIndex < -1 || lineIndex > 4095)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Invalid campaign session/line index.");
            CampaignValidation.String(target, allowEmpty: false);
            CampaignValidation.String(argument, allowEmpty: kind != ContinueDialog);
            RequestId = requestId; Revision = revision; Scene = scene; Kind = kind;
            Target = target; Argument = argument; Amount = amount;
            SessionId = sessionId; Conversation = conversation; LineIndex = lineIndex;
        }

        public uint RequestId { get; }
        public uint Revision { get; }
        public int Scene { get; }
        public byte Kind { get; }
        public string Target { get; }
        public string Argument { get; }
        public int Amount { get; }
        public long SessionId { get; }
        public ulong Conversation { get; }
        public int LineIndex { get; }
        public Opcode Opcode => Opcode.CampaignAction;
    }

    public sealed class CampaignReplyMessage : IMessage
    {
        public CampaignReplyMessage(uint requestId, uint revision, bool accepted, string reason)
        {
            CampaignValidation.RequestId(requestId);
            CampaignValidation.String(reason, allowEmpty: true);
            RequestId = requestId; Revision = revision; Accepted = accepted; Reason = reason;
        }
        public uint RequestId { get; }
        public uint Revision { get; }
        public bool Accepted { get; }
        public string Reason { get; }
        public Opcode Opcode => Opcode.CampaignReply;
    }

    internal static class CampaignValidation
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static void RequestId(uint requestId)
        {
            if (requestId == 0)
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Campaign request ID must be positive.");
        }
        internal static void String(string value, bool allowEmpty)
        {
            if (value == null || (!allowEmpty && value.Length == 0))
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Missing campaign string.");
            try
            {
                if (Utf8.GetByteCount(value) > ProtocolConstants.MaxStringByteLength)
                    throw new ProtocolException(ProtocolErrorKind.InvalidStringLength, "Campaign string exceeds 256 UTF-8 bytes.");
            }
            catch (EncoderFallbackException)
            {
                throw new ProtocolException(ProtocolErrorKind.InvalidCampaignState, "Campaign string contains invalid Unicode.");
            }
        }
    }
}
