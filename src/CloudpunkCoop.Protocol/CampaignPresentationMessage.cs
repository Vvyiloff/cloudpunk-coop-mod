using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// A small opaque conversation presentation DTO, independent of campaign snapshots.
    /// Sequence/revision/scene filtering and JSON interpretation belong to the plugin.
    /// </summary>
    public sealed class CampaignPresentationMessage : IMessage
    {
        public const int MaxDataLength = 16384;
        private readonly byte[] _data;

        public CampaignPresentationMessage(uint sequence, uint revision, int scene, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length > MaxDataLength)
                throw new ProtocolException(ProtocolErrorKind.InvalidBlobLength,
                    "Campaign presentation exceeds 16384 bytes.");
            Sequence = sequence; Revision = revision; Scene = scene;
            _data = (byte[])data.Clone();
        }

        public uint Sequence { get; }
        public uint Revision { get; }
        public int Scene { get; }
        public byte[] Data => (byte[])_data.Clone();
        public ReadOnlySpan<byte> DataSpan => _data;
        public Opcode Opcode => Opcode.CampaignPresentation;
    }
}
