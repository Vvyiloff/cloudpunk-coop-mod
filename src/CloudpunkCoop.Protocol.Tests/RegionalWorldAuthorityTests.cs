using System;
using System.Collections.Generic;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public sealed class RegionalWorldAuthorityTests
    {
        [Fact]
        public void SimultaneousInitialEntryPrefersHostAndRepeatedPresenceDoesNotReissueEpoch()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(7, true, true, 7, true, true);
            var first = Lease(a, 7);
            Assert.Equal((byte)1, first.OwnerPlayer);
            Assert.True(first.Epoch > 0);
            Assert.Equal(RegionalLeaseStatus.Active, first.Status);
            Assert.True(a.CanAcceptSnapshot(7, first.Epoch, 1));
            Assert.False(a.CanAcceptSnapshot(7, first.Epoch, 2));
            a.UpdatePresence(7, true, true, 7, true, true);
            Assert.Equal(first, Lease(a, 7));
            Assert.Single(a.Leases);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void FirstReadyPlayerRemainsIncumbentWhenOtherPlayerJoins(byte incumbent)
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(4, incumbent == 1, true, 4, incumbent == 2, true);
            var first = Lease(a, 4);
            Assert.Equal(incumbent, first.OwnerPlayer);
            a.UpdatePresence(4, true, true, 4, true, true);
            Assert.Equal(first, Lease(a, 4));
            Assert.True(a.CanAcceptSnapshot(4, first.Epoch, incumbent));
            Assert.False(a.CanAcceptSnapshot(4, first.Epoch, (byte)(3 - incumbent)));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void IncumbentLoadingOrUnknownScenePausesBothSourcesAndResumesWithoutReseed(byte owner, bool unknown)
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(6, owner == 1, true, 6, owner == 2, true);
            var first = Lease(a, 6);
            int loadingScene = unknown ? -1 : 6;
            a.UpdatePresence(owner == 1 ? loadingScene : 6, owner != 1, true,
                owner == 2 ? loadingScene : 6, owner != 2, true);
            var paused = Lease(a, 6);
            Assert.Equal(owner, paused.OwnerPlayer);
            Assert.Equal(first.Epoch, paused.Epoch);
            Assert.Equal((byte)0, paused.PendingCandidate);
            Assert.Equal(RegionalLeaseStatus.Paused, paused.Status);
            Assert.False(a.CanAcceptSnapshot(6, paused.Epoch, 1));
            Assert.False(a.CanAcceptSnapshot(6, paused.Epoch, 2));
            Assert.False(a.ConfirmHandover(6, paused.Epoch, (byte)(3 - owner)));
            a.UpdatePresence(6, true, true, 6, true, true);
            Assert.Equal(first, Lease(a, 6));
        }

        [Fact]
        public void InitialLoadingNeverGrantsExecutionAndFirstReadyPlayerNeedsNoSeed()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(-1, false, true, -1, false, true);
            Assert.Empty(a.Leases);
            a.UpdatePresence(9, false, true, 9, false, true);
            Assert.Equal(RegionalLeaseStatus.Dormant, Lease(a, 9).Status);
            Assert.False(Lease(a, 9).HasSimulated);
            Assert.False(a.CanAcceptSnapshot(9, 0, 1));
            Assert.False(a.ConfirmHandover(9, 0, 2));
            a.UpdatePresence(9, false, true, 9, true, true);
            var granted = Lease(a, 9);
            Assert.Equal((byte)2, granted.OwnerPlayer);
            Assert.True(a.CanAcceptSnapshot(9, granted.Epoch, 2));
            a.UpdatePresence(9, true, true, 9, true, true);
            Assert.Equal(granted, Lease(a, 9));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void ConfirmedDepartureRequiresExactSuccessfulSeedBeforePeerCanExecute(byte owner, bool disconnect)
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(10, owner == 1, true, 10, owner == 2, true);
            var old = Lease(a, 10);
            a.UpdatePresence(owner == 1 ? 11 : 10, owner != 1, owner != 1 || !disconnect,
                owner == 2 ? 11 : 10, owner != 2, owner != 2 || !disconnect);
            var pending = Lease(a, 10);
            byte candidate = (byte)(3 - owner);
            AssertPending(pending, candidate, old.Epoch);
            Assert.False(a.CanAcceptSnapshot(10, old.Epoch, owner));
            Assert.False(a.CanAcceptSnapshot(10, pending.Epoch, candidate));
            Assert.False(a.ConfirmHandover(10, old.Epoch, candidate));
            Assert.False(a.ConfirmHandover(10, pending.Epoch, owner));
            Assert.False(a.ConfirmHandover(11, pending.Epoch, candidate));
            Assert.True(a.ConfirmHandover(10, pending.Epoch, candidate));
            Assert.False(a.ConfirmHandover(10, pending.Epoch, candidate));
            Assert.True(a.CanAcceptSnapshot(10, pending.Epoch, candidate));
            Assert.False(a.CanAcceptSnapshot(10, pending.Epoch, owner));
        }

        [Fact]
        public void DormantReentryIncludingSamePlayerReconnectRequiresArchiveSeed()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(0, true, true, -1, false, false);
            var first = Lease(a, 0);
            a.UpdatePresence(0, false, false, -1, false, false);
            var dormant = Lease(a, 0);
            Assert.Equal(RegionalLeaseStatus.Dormant, dormant.Status);
            Assert.Equal((byte)0, dormant.OwnerPlayer);
            Assert.True(dormant.HasSimulated);
            a.UpdatePresence(0, true, true, -1, false, false);
            var pending = Lease(a, 0);
            AssertPending(pending, 1, first.Epoch);
            Assert.False(a.CanAcceptSnapshot(0, first.Epoch, 1));
            Assert.False(a.CanAcceptSnapshot(0, pending.Epoch, 1));
            Assert.True(a.ConfirmHandover(0, pending.Epoch, 1));
            Assert.True(a.CanAcceptSnapshot(0, pending.Epoch, 1));
        }

        [Fact]
        public void SamePlayerExplicitMoveAwayAndBackRequiresReseedUnlikeSameSceneReadinessToggle()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(20, true, true, -1, false, false);
            var first = Lease(a, 20);
            a.UpdatePresence(21, false, true, -1, false, false);
            a.UpdatePresence(20, false, true, -1, false, false);
            Assert.Equal(RegionalLeaseStatus.Dormant, Lease(a, 20).Status);
            a.UpdatePresence(20, true, true, -1, false, false);
            AssertPending(Lease(a, 20), 1, first.Epoch);
        }

        [Theory]
        [InlineData(-1, true)]
        [InlineData(30, true)]
        [InlineData(31, true)]
        [InlineData(30, false)]
        public void CandidateLosingReadinessPresenceOrConnectionInvalidatesLateAcknowledgement(int scene, bool connected)
        {
            var a = PendingPeer();
            var pending = Lease(a, 30);
            a.UpdatePresence(-1, false, false, scene, false, connected);
            Assert.False(a.ConfirmHandover(30, pending.Epoch, 2));
            a.UpdatePresence(-1, false, false, 30, true, true);
            var replacement = Lease(a, 30);
            AssertPending(replacement, 2, pending.Epoch);
            Assert.False(a.ConfirmHandover(30, pending.Epoch, 2));
            Assert.True(a.ConfirmHandover(30, replacement.Epoch, 2));
        }

        [Fact]
        public void PendingCandidateIsStickyButReplacementGetsNewEpoch()
        {
            var a = PendingPeer();
            var peerPending = Lease(a, 30);
            a.UpdatePresence(30, true, true, 30, true, true);
            Assert.Equal(peerPending, Lease(a, 30));
            a.UpdatePresence(30, true, true, 31, true, true);
            var hostPending = Lease(a, 30);
            AssertPending(hostPending, 1, peerPending.Epoch);
            Assert.False(a.ConfirmHandover(30, peerPending.Epoch, 2));
            Assert.True(a.ConfirmHandover(30, hostPending.Epoch, 1));
        }

        [Fact]
        public void SimultaneousSwapAndDepartureKeepRegionsIndependent()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(40, true, true, 41, true, true);
            var host = Lease(a, 40);
            var peer = Lease(a, 41);
            Assert.True(a.CanAcceptSnapshot(40, host.Epoch, 1));
            Assert.True(a.CanAcceptSnapshot(41, peer.Epoch, 2));
            a.UpdatePresence(41, true, true, 40, true, true);
            var toPeer = Lease(a, 40);
            var toHost = Lease(a, 41);
            AssertPending(toPeer, 2, host.Epoch);
            AssertPending(toHost, 1, peer.Epoch);
            Assert.True(a.ConfirmHandover(40, toPeer.Epoch, 2));
            Assert.Equal(toHost, Lease(a, 41));
            Assert.False(a.CanAcceptSnapshot(41, toHost.Epoch, 1));
            Assert.True(a.ConfirmHandover(41, toHost.Epoch, 1));
            a.UpdatePresence(41, false, false, 40, false, false);
            Assert.All(a.Leases, lease => Assert.Equal(RegionalLeaseStatus.Dormant, lease.Status));
            a.UpdatePresence(41, true, true, 40, true, true);
            AssertPending(Lease(a, 40), 2, toPeer.Epoch);
            AssertPending(Lease(a, 41), 1, toHost.Epoch);
        }

        [Fact]
        public void MovementInOneRegionDoesNotChangeOtherActiveLease()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(1, true, true, 4095, true, true);
            var unaffected = Lease(a, 4095);
            a.UpdatePresence(2, true, true, 4095, true, true);
            Assert.Equal(unaffected, Lease(a, 4095));
            Assert.True(a.CanAcceptSnapshot(4095, unaffected.Epoch, 2));
            Assert.Equal(RegionalLeaseStatus.Dormant, Lease(a, 1).Status);
            Assert.Equal((byte)1, Lease(a, 2).OwnerPlayer);
        }

        [Fact]
        public void ResetInvalidatesPendingAndActiveIdentitiesAndRetainsArchiveObligation()
        {
            var a = PendingPeer();
            var oldPending = Lease(a, 30);
            a.UpdatePresence(32, true, true, 30, true, true);
            var oldActive = Lease(a, 32);
            a.Reset();
            a.Reset();
            Assert.False(a.ConfirmHandover(30, oldPending.Epoch, 2));
            Assert.False(a.CanAcceptSnapshot(32, oldActive.Epoch, 1));
            a.UpdatePresence(32, true, true, 30, true, true);
            var newHost = Lease(a, 32);
            var newPeer = Lease(a, 30);
            AssertPending(newHost, 1, oldActive.Epoch);
            AssertPending(newPeer, 2, oldPending.Epoch);
            Assert.True(newPeer.Epoch > oldActive.Epoch);
            Assert.False(a.ConfirmHandover(30, oldPending.Epoch, 2));
            Assert.True(a.ConfirmHandover(32, newHost.Epoch, 1));
            Assert.True(a.ConfirmHandover(30, newPeer.Epoch, 2));
        }

        [Fact]
        public void WrongSceneEpochSourceAndInvalidIdentitiesNeverAcceptFramesOrAcknowledgements()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(50, true, true, 51, true, true);
            var lease = Lease(a, 50);
            Assert.False(a.CanAcceptSnapshot(50, 0, 1));
            Assert.False(a.CanAcceptSnapshot(50, lease.Epoch + 1, 1));
            Assert.False(a.CanAcceptSnapshot(50, lease.Epoch, 2));
            Assert.False(a.CanAcceptSnapshot(51, lease.Epoch, 1));
            foreach (int scene in new[] { -2, -1, 4096, int.MaxValue })
            {
                Assert.False(a.CanAcceptSnapshot(scene, lease.Epoch, 1));
                Assert.False(a.ConfirmHandover(scene, lease.Epoch, 1));
            }
            foreach (byte player in new byte[] { 0, 3, 255 })
            {
                Assert.False(a.CanAcceptSnapshot(50, lease.Epoch, player));
                Assert.False(a.ConfirmHandover(50, lease.Epoch, player));
            }
            Assert.Equal(lease, Lease(a, 50));
        }

        [Theory]
        [InlineData(-2, false, true)]
        [InlineData(4096, false, true)]
        [InlineData(-1, true, true)]
        [InlineData(1, true, false)]
        public void InvalidEitherPresenceIsRejectedBeforeAnyMutation(int scene, bool ready, bool connected)
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(1, true, true, 2, true, true);
            var before = a.Leases;
            Assert.ThrowsAny<ArgumentException>(() => a.UpdatePresence(scene, ready, connected, 3, true, true));
            Assert.ThrowsAny<ArgumentException>(() => a.UpdatePresence(3, true, true, scene, ready, connected));
            Assert.Equal(before, a.Leases);
            Assert.True(a.CanAcceptSnapshot(1, Lease(a, 1).Epoch, 1));
            Assert.True(a.CanAcceptSnapshot(2, Lease(a, 2).Epoch, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.GetLease(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.GetLease(4096));
        }

        [Fact]
        public void AllValidScenesAreBoundedAndHistoryDoesNotEvictArchiveObligations()
        {
            var a = new RegionalWorldAuthority();
            for (int scene = 0; scene < RegionalWorldAuthority.MaximumScenes; scene++)
                a.UpdatePresence(scene, true, true, -1, false, false);
            Assert.Equal(4096, a.Leases.Count);
            Assert.True(Lease(a, 0).HasSimulated);
            a.UpdatePresence(0, true, true, -1, false, false);
            AssertPending(Lease(a, 0), 1, 1);
            Assert.Equal(4096, a.Leases.Count);
        }

        [Fact]
        public void LeaseSnapshotsAreDetachedAndReadOnly()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(60, true, true, -1, false, false);
            var snapshot = a.Leases;
            var original = snapshot[0];
            var forged = original with { OwnerPlayer = 2 };
            Assert.Throws<NotSupportedException>(() => ((IList<RegionalWorldLease>)snapshot)[0] = forged);
            a.UpdatePresence(60, false, true, -1, false, false);
            Assert.Equal(RegionalLeaseStatus.Active, original.Status);
            Assert.Equal(RegionalLeaseStatus.Paused, Lease(a, 60).Status);
            Assert.Equal((byte)1, Lease(a, 60).OwnerPlayer);
        }

        [Fact]
        public void EpochExhaustionRevokesEveryRegionAndCannotBeClearedByReset()
        {
            var a = new RegionalWorldAuthority(uint.MaxValue - 2);
            a.UpdatePresence(70, true, true, 71, true, true);
            var host = Lease(a, 70);
            var peer = Lease(a, 71);
            Assert.Equal(uint.MaxValue - 1, host.Epoch);
            Assert.Equal(uint.MaxValue, peer.Epoch);
            Assert.True(a.CanAcceptSnapshot(70, host.Epoch, 1));
            a.UpdatePresence(71, true, true, 70, true, true);
            Assert.True(a.EpochExhausted);
            Assert.All(a.Leases, lease =>
            {
                Assert.Equal(RegionalLeaseStatus.Exhausted, lease.Status);
                Assert.Equal((byte)0, lease.OwnerPlayer);
                Assert.Equal((byte)0, lease.PendingCandidate);
            });
            Assert.False(a.CanAcceptSnapshot(70, host.Epoch, 1));
            Assert.False(a.CanAcceptSnapshot(71, peer.Epoch, 2));
            Assert.False(a.ConfirmHandover(70, host.Epoch, 2));
            a.Reset();
            a.UpdatePresence(72, true, true, -1, false, false);
            Assert.True(a.EpochExhausted);
            Assert.Equal(RegionalLeaseStatus.Exhausted, Lease(a, 72).Status);
            Assert.False(a.CanAcceptSnapshot(72, 0, 1));
        }

        private static RegionalWorldAuthority PendingPeer()
        {
            var a = new RegionalWorldAuthority();
            a.UpdatePresence(30, true, true, 30, true, true);
            a.UpdatePresence(-1, false, false, 30, true, true);
            return a;
        }

        private static RegionalWorldLease Lease(RegionalWorldAuthority authority, int scene)
            => Assert.IsType<RegionalWorldLease>(authority.GetLease(scene));

        private static void AssertPending(RegionalWorldLease lease, byte candidate, uint olderEpoch)
        {
            Assert.Equal((byte)0, lease.OwnerPlayer);
            Assert.Equal(candidate, lease.PendingCandidate);
            Assert.Equal(RegionalLeaseStatus.AwaitingSeed, lease.Status);
            Assert.True(lease.HasSimulated);
            Assert.True(lease.Epoch > olderEpoch);
        }
    }
}
