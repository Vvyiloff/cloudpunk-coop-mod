using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    /// <summary>A detached, JSON-serializable save revision alias. Import validates both fields.</summary>
    public sealed record CampaignSaveAlias(string RevisionHash, string CampaignId);

    /// <summary>
    /// Maps save byte revisions to campaign identities without inspecting files or session state.
    /// Hashes are exactly 64 ASCII hex digits; IDs are nonempty GUIDs in 32-digit N form.
    /// Valid uppercase hex is normalized to lowercase. Whitespace and other GUID formats are rejected.
    /// </summary>
    public sealed class CampaignSaveIdentities
    {
        public const int MaxAliases = 8192;
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _aliases;
        private readonly HashSet<string> _campaignIds;

        public CampaignSaveIdentities() : this(Array.Empty<CampaignSaveAlias>()) { }

        /// <summary>
        /// Restores a detached snapshot. Any invalid entry, duplicate revision (even with the same
        /// ID), conflict, or excess entry rejects the entire construction. Multiple revisions may
        /// intentionally share one campaign ID. Caller-owned collections are never retained.
        /// </summary>
        public CampaignSaveIdentities(IEnumerable<CampaignSaveAlias> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var campaignIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null) throw new ArgumentException("Null alias entry.", nameof(entries));
                string hash = NormalizeHash(entry.RevisionHash);
                string id = NormalizeId(entry.CampaignId);
                if (aliases.ContainsKey(hash))
                    throw new ArgumentException("Duplicate or conflicting revision alias.", nameof(entries));
                if (aliases.Count >= MaxAliases)
                    throw new ArgumentException("Alias limit exceeded.", nameof(entries));
                aliases.Add(hash, id);
                campaignIds.Add(id);
            }
            _aliases = aliases;
            _campaignIds = campaignIds;
        }

        public int Count { get { lock (_gate) return _aliases.Count; } }

        /// <summary>Returns the known ID, or creates a fresh campaign for an unknown revision.</summary>
        public string Resolve(string hash)
        {
            string normalizedHash = NormalizeHash(hash);
            lock (_gate)
            {
                if (_aliases.TryGetValue(normalizedHash, out var existing)) return existing;
                EnsureCapacity();
                string id;
                do { id = Guid.NewGuid().ToString("N"); } while (_campaignIds.Contains(id));
                _aliases.Add(normalizedHash, id);
                _campaignIds.Add(id);
                return id;
            }
        }

        /// <summary>
        /// Associates a proven successfully saved revision with an already registered campaign.
        /// The caller supplies proof of save success; this class cannot verify it. Repeating the
        /// same association is harmless, but rebinding an existing revision always throws.
        /// </summary>
        public void Associate(string hash, string campaignId)
        {
            string normalizedHash = NormalizeHash(hash);
            string normalizedId = NormalizeId(campaignId);
            lock (_gate)
            {
                if (_aliases.TryGetValue(normalizedHash, out var existing))
                {
                    if (existing != normalizedId)
                        throw new InvalidOperationException("Revision already belongs to another campaign.");
                    return;
                }
                if (!_campaignIds.Contains(normalizedId))
                    throw new ArgumentException("Campaign ID is not registered.", nameof(campaignId));
                EnsureCapacity();
                _aliases.Add(normalizedHash, normalizedId);
            }
        }

        /// <summary>
        /// Returns a fresh detached array of immutable alias records, sorted by revision hash.
        /// Replacing array elements cannot change this registry or other exported snapshots.
        /// </summary>
        public CampaignSaveAlias[] Export()
        {
            lock (_gate)
            {
                var hashes = new List<string>(_aliases.Keys);
                hashes.Sort(StringComparer.Ordinal);
                var entries = new CampaignSaveAlias[hashes.Count];
                for (int i = 0; i < hashes.Count; i++)
                    entries[i] = new CampaignSaveAlias(hashes[i], _aliases[hashes[i]]);
                return entries;
            }
        }

        private void EnsureCapacity()
        {
            if (_aliases.Count >= MaxAliases)
                throw new InvalidOperationException("Alias limit exceeded.");
        }

        private static string NormalizeHash(string hash) => NormalizeHex(hash, 64, nameof(hash));

        private static string NormalizeId(string campaignId)
        {
            string normalized = NormalizeHex(campaignId, 32, nameof(campaignId));
            if (!Guid.TryParseExact(normalized, "N", out var id) || id == Guid.Empty)
                throw new ArgumentException("Expected a nonempty campaign GUID in N form.", nameof(campaignId));
            return normalized;
        }

        private static string NormalizeHex(string value, int length, string parameter)
        {
            if (value == null) throw new ArgumentNullException(parameter);
            if (value.Length != length)
                throw new ArgumentException("Unexpected hex length.", parameter);
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    throw new ArgumentException("Expected ASCII hex digits.", parameter);
            return value.ToLowerInvariant();
        }
    }
}
