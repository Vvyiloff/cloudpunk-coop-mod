using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    /// <summary>Atomic full world frames; incomplete chunks never change rendered entities.</summary>
    public sealed class NpcFrameAssembler
    {
        private NpcMessage?[]? _chunks;
        private uint _epoch, _sequence;
        private int _scene, _received;
        private bool _haveFrame, _committed;
        public int PendingChunks => _received;

        public bool Add(NpcMessage message, out NpcVisualState[]? frame)
        {
            frame = null;
            if (_haveFrame)
            {
                int epochOrder = unchecked((int)(message.Epoch - _epoch));
                int sequenceOrder = unchecked((int)(message.Sequence - _sequence));
                if (epochOrder < 0 || epochOrder == 0 && sequenceOrder < 0 ||
                    epochOrder == 0 && sequenceOrder == 0 && _committed) return false;
                if (epochOrder > 0 || sequenceOrder > 0) _haveFrame = false;
            }
            if (!_haveFrame)
            {
                _haveFrame = true; _committed = false;
                _epoch = message.Epoch; _sequence = message.Sequence; _scene = message.Scene;
                _chunks = new NpcMessage?[message.Chunks]; _received = 0;
            }
            if (_chunks!.Length != message.Chunks || _scene != message.Scene) { RejectCurrent(); return false; }
            if (_chunks[message.Chunk] != null) return false;
            _chunks[message.Chunk] = message; _received++;
            if (_received != _chunks.Length) return false;
            var records = new List<NpcVisualState>(_chunks.Length * NpcMessage.MaxPerChunk);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcMessage? chunk in _chunks)
                foreach (var npc in chunk!.NpcSpan)
                {
                    if (!ids.Add(npc.Id)) { RejectCurrent(); return false; }
                    records.Add(npc);
                }
            _committed = true; frame = records.ToArray(); return true;
        }

        // Invalid frames consume their sequence too: a malformed newer frame must never
        // reopen the door to an older valid world snapshot.
        private void RejectCurrent() { _committed = true; _received = 0; }

        public void Clear() { _chunks = null; _received = 0; _haveFrame = _committed = false; }
    }
}
