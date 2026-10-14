using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    public enum RegionalLeaseStatus { Dormant, Active, Paused, AwaitingSeed, Exhausted }

    /// <summary>Detached immutable authority state. Player 1 is the host; 0 means no owner.</summary>
    public sealed record RegionalWorldLease(int Scene, byte OwnerPlayer, uint Epoch,
        byte PendingCandidate, RegionalLeaseStatus Status, bool HasSimulated);

    /// <summary>
    /// Host-owned, deterministic authority reducer, with no clocks or native dependencies.
    /// Supply both authoritative presence snapshots atomically, not client frame assertions.
    /// Unknown scene (-1) or same-scene loading pauses an incumbent. Only a connected player's
    /// confirmed different scene, or disconnect, proves departure. Thus same-scene native
    /// reload ready=false -> true resumes without reseeding unless explicit departure was
    /// observed; integrations must report departure if native state was actually destroyed.
    /// A grant conservatively marks a region simulated, even before its first frame.
    /// ConfirmHandover is a trusted host assertion of successful FULL native AI state seed,
    /// including archive restore on dormant reentry. It does not perform or verify that seed.
    /// Reset retains archive obligations and the instance-wide epoch watermark. A new instance
    /// needs an external session identity to distinguish packets from a previous instance.
    /// </summary>
    public sealed class RegionalWorldAuthority
    {
        public const int MaximumScenes = 4096;
        private readonly object _gate = new();
        private readonly RegionalWorldLease?[] _regions = new RegionalWorldLease?[MaximumScenes];
        private int _hostScene = -1, _peerScene = -1;
        private bool _hostReady, _peerReady, _hostConnected, _peerConnected;
        private uint _lastEpoch;
        private bool _exhausted;

        /// <summary>Optional reserved epoch watermark; epochs at or below it are never issued.</summary>
        public RegionalWorldAuthority(uint lastIssuedEpoch = 0) { _lastEpoch = lastIssuedEpoch; }

        public bool EpochExhausted { get { lock (_gate) return _exhausted; } }

        public IReadOnlyList<RegionalWorldLease> Leases
        {
            get
            {
                lock (_gate)
                {
                    var snapshot = new List<RegionalWorldLease>();
                    foreach (var lease in _regions) if (lease != null) snapshot.Add(lease);
                    return snapshot.AsReadOnly();
                }
            }
        }

        public RegionalWorldLease? GetLease(int scene)
        {
            ValidateScene(scene);
            lock (_gate) return _regions[scene];
        }

        /// <summary>
        /// Scene must be -1 or 0..4095. Ready requires connected and a known scene.
        /// Both snapshots are validated before any state changes. Repeated snapshots are inert.
        /// </summary>
        public void UpdatePresence(int hostScene, bool hostReady, bool hostConnected,
            int peerScene, bool peerReady, bool peerConnected)
        {
            ValidatePresence(hostScene, hostReady, hostConnected);
            ValidatePresence(peerScene, peerReady, peerConnected);
            lock (_gate)
            {
                _hostScene = hostScene; _hostReady = hostReady; _hostConnected = hostConnected;
                _peerScene = peerScene; _peerReady = peerReady; _peerConnected = peerConnected;
                Track(hostScene, hostConnected);
                Track(peerScene, peerConnected);
                if (_exhausted) { FailClosed(); return; }
                for (int scene = 0; scene < MaximumScenes; scene++)
                {
                    var lease = _regions[scene];
                    if (lease == null) continue;
                    if (lease.OwnerPlayer != 0 && !Departed(lease.OwnerPlayer, scene))
                    {
                        _regions[scene] = lease with { Status = ReadyIn(lease.OwnerPlayer, scene)
                            ? RegionalLeaseStatus.Active : RegionalLeaseStatus.Paused };
                        continue;
                    }

                    // Retain a pending candidate while ready, even if the host subsequently joins.
                    byte candidate = lease.PendingCandidate != 0 && ReadyIn(lease.PendingCandidate, scene)
                        ? lease.PendingCandidate : ReadyIn(1, scene) ? (byte)1 : ReadyIn(2, scene) ? (byte)2 : (byte)0;
                    if (candidate == 0)
                    {
                        _regions[scene] = lease with { OwnerPlayer = 0, PendingCandidate = 0,
                            Status = RegionalLeaseStatus.Dormant };
                        continue;
                    }
                    if (lease.OwnerPlayer == 0 && lease.PendingCandidate == candidate &&
                        lease.Status == RegionalLeaseStatus.AwaitingSeed) continue;
                    if (!TryNextEpoch(out uint epoch)) return;
                    _regions[scene] = lease.HasSimulated
                        ? lease with { OwnerPlayer = 0, PendingCandidate = candidate, Epoch = epoch,
                            Status = RegionalLeaseStatus.AwaitingSeed }
                        : lease with { OwnerPlayer = candidate, PendingCandidate = 0, Epoch = epoch,
                            Status = RegionalLeaseStatus.Active, HasSimulated = true };
                }
            }
        }

        public bool ConfirmHandover(int scene, uint epoch, byte candidate)
        {
            lock (_gate)
            {
                if (_exhausted || !ValidScene(scene) || !ValidPlayer(candidate)) return false;
                var lease = _regions[scene];
                if (lease == null || lease.Status != RegionalLeaseStatus.AwaitingSeed ||
                    lease.OwnerPlayer != 0 || lease.Epoch != epoch || lease.PendingCandidate != candidate ||
                    !ReadyIn(candidate, scene)) return false;
                _regions[scene] = lease with { OwnerPlayer = candidate, PendingCandidate = 0,
                    Status = RegionalLeaseStatus.Active, HasSimulated = true };
                return true;
            }
        }

        /// <summary>Gate simulation as well as incoming snapshots with this exact lease identity.</summary>
        public bool CanAcceptSnapshot(int scene, uint epoch, byte sourcePlayer)
        {
            lock (_gate)
            {
                if (_exhausted || !ValidScene(scene) || !ValidPlayer(sourcePlayer)) return false;
                var lease = _regions[scene];
                return lease != null && lease.Status == RegionalLeaseStatus.Active &&
                    lease.OwnerPlayer == sourcePlayer && lease.Epoch == epoch && ReadyIn(sourcePlayer, scene);
            }
        }

        /// <summary>Invalidates pending ACKs and frames; archive history and epochs survive reset.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _hostScene = _peerScene = -1;
                _hostReady = _peerReady = _hostConnected = _peerConnected = false;
                for (int scene = 0; scene < MaximumScenes; scene++)
                    if (_regions[scene] is RegionalWorldLease lease)
                        _regions[scene] = lease with { OwnerPlayer = 0, PendingCandidate = 0,
                            Status = _exhausted ? RegionalLeaseStatus.Exhausted : RegionalLeaseStatus.Dormant };
            }
        }

        private void Track(int scene, bool connected)
        {
            if (connected && ValidScene(scene) && _regions[scene] == null)
                _regions[scene] = new RegionalWorldLease(scene, 0, 0, 0, RegionalLeaseStatus.Dormant, false);
        }

        private bool ReadyIn(byte player, int scene) => player == 1
            ? _hostConnected && _hostReady && _hostScene == scene
            : _peerConnected && _peerReady && _peerScene == scene;

        private bool Departed(byte player, int scene) => player == 1
            ? !_hostConnected || (_hostScene >= 0 && _hostScene != scene)
            : !_peerConnected || (_peerScene >= 0 && _peerScene != scene);

        private bool TryNextEpoch(out uint epoch)
        {
            epoch = 0;
            if (_lastEpoch == uint.MaxValue) { _exhausted = true; FailClosed(); return false; }
            epoch = ++_lastEpoch;
            return true;
        }

        private void FailClosed()
        {
            for (int scene = 0; scene < MaximumScenes; scene++)
                if (_regions[scene] is RegionalWorldLease lease)
                    _regions[scene] = lease with { OwnerPlayer = 0, PendingCandidate = 0,
                        Status = RegionalLeaseStatus.Exhausted };
        }

        private static bool ValidPlayer(byte player) => player == 1 || player == 2;
        private static bool ValidScene(int scene) => scene >= 0 && scene < MaximumScenes;
        private static void ValidateScene(int scene)
        {
            if (!ValidScene(scene)) throw new ArgumentOutOfRangeException(nameof(scene));
        }
        private static void ValidatePresence(int scene, bool ready, bool connected)
        {
            if (scene < -1 || scene >= MaximumScenes) throw new ArgumentOutOfRangeException(nameof(scene));
            if (ready && (!connected || scene == -1))
                throw new ArgumentException("Ready presence requires a connected player in a known scene.");
        }
    }
}
