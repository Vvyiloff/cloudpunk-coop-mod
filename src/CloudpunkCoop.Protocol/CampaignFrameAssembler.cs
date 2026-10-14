using System;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Keeps one bounded revision. Add returns bytes only once all indices are present,
    /// concatenated in index order. Newer revisions supersede incomplete frames; poisoned
    /// revisions cannot restart. Revisions are monotonic uint values (no implicit wrap).
    /// Reset starts a new session and deliberately clears the revision watermark.
    /// </summary>
    public sealed class CampaignFrameAssembler
    {
        private CampaignChunkMessage?[]? _chunks;
        private uint _revision;
        private int _scene, _received, _bytes;
        private bool _haveRevision, _closed;

        public int PendingChunks => _received;
        public int PendingBytes => _bytes;

        public bool Add(CampaignChunkMessage message,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? frame)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            frame = null;
            if (_haveRevision && (message.Revision < _revision || message.Revision == _revision && _closed))
                return false;
            if (!_haveRevision || message.Revision > _revision)
            {
                _revision = message.Revision; _scene = message.Scene;
                _haveRevision = true; _closed = false;
                _received = _bytes = 0;
                _chunks = new CampaignChunkMessage?[message.Count];
            }
            if (message.Count != _chunks!.Length || message.Scene != _scene)
            {
                CloseCurrent(); return false;
            }
            var previous = _chunks[message.Index];
            if (previous != null)
            {
                if (!previous.DataSpan.SequenceEqual(message.DataSpan)) CloseCurrent();
                return false;
            }
            if (message.DataSpan.Length > CampaignChunkMessage.MaxAssembledDataLength - _bytes)
            {
                CloseCurrent(); return false;
            }
            _chunks[message.Index] = message;
            _received++; _bytes += message.DataSpan.Length;
            if (_received != _chunks.Length) return false;

            frame = new byte[_bytes];
            int offset = 0;
            foreach (var chunk in _chunks)
            {
                chunk!.DataSpan.CopyTo(frame.AsSpan(offset));
                offset += chunk.DataSpan.Length;
            }
            CloseCurrent();
            return true;
        }

        private void CloseCurrent()
        {
            _chunks = null; _received = _bytes = 0; _closed = true;
        }

        /// <summary>Discard pending bytes and history when starting a new session.</summary>
        public void Reset()
        {
            CloseCurrent(); _haveRevision = false; _revision = 0; _scene = 0;
        }

        public void Clear() => Reset();
    }
}
