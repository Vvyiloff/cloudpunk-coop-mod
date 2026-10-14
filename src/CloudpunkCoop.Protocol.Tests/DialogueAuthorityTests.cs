using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public sealed class DialogueAuthorityTests
    {
        [Fact]
        public void InitialAndResetStateRejectRequestsAndNeverReuseGeneration()
        {
            var a = new DialogueAuthority();
            Assert.False(a.Active);
            Assert.False(a.TryContinue(1, 0, 0, 10, true));
            a.Reset();
            ulong resetGeneration = a.Generation;
            ulong first = a.Begin(1, "dialog", 0, 10, true);
            Assert.True(first > resetGeneration);
            a.Reset();
            Assert.True(a.Generation > first);
            Assert.False(a.OwnsConversation(1, first));
            Assert.False(a.SetLine(first, 1, 11, true));
            Assert.False(a.TryContinue(1, first, 0, 10, true));
            ulong invalidated = a.Generation;
            Assert.True(a.Begin(2, "dialog", 0, 10, true) > invalidated);
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        public void BothPlayersMayContinueButOnlyInitiatorOwnsConversation(byte owner, byte other)
        {
            var a = new DialogueAuthority();
            ulong g = a.Begin(owner, "dialog", 0, 10, true);
            Assert.True(a.OwnsConversation(owner, g));
            Assert.False(a.OwnsConversation(other, g));
            Assert.True(a.TryContinue(other, g, 0, 10, true, out var grant));
            Assert.Equal(other, grant!.Player);
            Assert.Equal(owner, a.OwnerPlayer);
            Assert.Equal(g, grant.Generation);
            Assert.Equal("dialog", grant.DialogId);
            Assert.Equal(0, grant.LineIndex);
            Assert.Equal(10, grant.LineId);
            Assert.False(a.TryContinue(owner, g, 0, 10, true));
        }

        [Fact]
        public void RequiresStoredAndFreshReadinessAndExactLineIdentity()
        {
            var a = new DialogueAuthority();
            ulong g = a.Begin(1, "dialog", 3, 42, false);
            ulong before = a.EventId;
            Assert.False(a.TryContinue(2, g, 3, 42, true));
            Assert.Equal(before, a.EventId);
            Assert.True(a.SetLine(g, 3, 42, true));
            before = a.EventId;
            Assert.False(a.TryContinue(2, g, 3, 42, false));
            Assert.False(a.TryContinue(2, g, 2, 42, true));
            Assert.False(a.TryContinue(2, g, 3, 43, true));
            Assert.False(a.TryContinue(2, g + 1, 3, 42, true));
            Assert.False(a.TryContinue(0, g, 3, 42, true));
            Assert.False(a.TryContinue(3, g, 3, 42, true));
            Assert.Equal(before, a.EventId);
            Assert.True(a.TryContinue(2, g, 3, 42, true));
        }

        [Fact]
        public void GrantFreezesUntilNewerIndexEvenWhenAuthoredIdRepeats()
        {
            var a = new DialogueAuthority();
            ulong g = a.Begin(1, "dialog", 0, 42, true);
            Assert.True(a.TryContinue(1, g, 0, 42, true, out var first));
            Assert.True(a.AwaitingNextLine);
            Assert.False(a.Ready);
            Assert.False(a.SetLine(g, 0, 42, true));
            Assert.False(a.SetLine(g, 0, 43, true));
            Assert.False(a.TryContinue(2, g, 0, 42, true));
            Assert.True(a.SetLine(g, 1, 42, false));
            Assert.False(a.AwaitingNextLine);
            Assert.False(a.SetLine(g, 0, 42, true));
            Assert.False(a.SetLine(g, 1, 43, true));
            Assert.False(a.TryContinue(1, g, 0, 42, true));
            Assert.False(a.TryContinue(1, g, 1, 42, true));
            Assert.True(a.SetLine(g, 1, 42, true));
            Assert.True(a.TryContinue(2, g, 1, 42, true, out var second));
            Assert.True(second!.EventId > first!.EventId);
            Assert.Same(second, a.LastContinue);
        }

        [Fact]
        public void EndAndRepeatedDialogInvalidateAllPreviousRequests()
        {
            var a = new DialogueAuthority();
            ulong old = a.Begin(1, "same", 0, 7, true);
            Assert.True(a.TryContinue(1, old, 0, 7, true, out var first));
            Assert.True(a.End(old));
            Assert.True(a.Generation > old);
            Assert.False(a.Active);
            Assert.Equal(0, a.OwnerPlayer);
            Assert.Null(a.LastContinue);
            Assert.False(a.TryContinue(2, old, 0, 7, true));
            Assert.False(a.End(old));
            ulong current = a.Begin(2, "same", 0, 7, true);
            Assert.True(current > old);
            Assert.False(a.OwnsConversation(2, old));
            Assert.False(a.SetLine(old, 1, 8, true));
            Assert.False(a.End(old));
            Assert.False(a.TryContinue(1, old, 0, 7, true));
            Assert.True(a.TryContinue(1, current, 0, 7, true, out var second));
            Assert.True(second!.EventId > first!.EventId);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task SimultaneousPlayersAndDuplicateRequestsHaveOneWinner(byte owner)
        {
            var a = new DialogueAuthority();
            ulong g = a.Begin(owner, "dialog", 0, 10, true);
            using var start = new ManualResetEventSlim(false);
            var requests = new Task<bool>[16];
            for (int i = 0; i < requests.Length; i++)
            {
                byte player = (byte)(i % 2 + 1);
                requests[i] = Task.Run(() => { start.Wait(); return a.TryContinue(player, g, 0, 10, true); });
            }
            start.Set();
            bool[] results = await Task.WhenAll(requests);
            Assert.Single(Array.FindAll(results, result => result));
            Assert.Equal(2UL, a.EventId);
            Assert.True(a.AwaitingNextLine);
        }

        [Fact]
        public void InvalidBeginAndActiveReplacementLeaveStateUnchanged()
        {
            var a = new DialogueAuthority();
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Begin(0, "dialog", 0, 1, true));
            Assert.Throws<ArgumentException>(() => a.Begin(1, "", 0, 1, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Begin(1, "dialog", -1, 1, true));
            Assert.Equal(0UL, a.Generation);
            ulong g = a.Begin(1, "dialog", 0, 1, true);
            Assert.Throws<InvalidOperationException>(() => a.Begin(2, "other", 0, 1, true));
            Assert.Equal(g, a.Generation);
            Assert.True(a.OwnsConversation(1, g));
            ulong id = a.EventId;
            Assert.True(a.SetLine(g, 0, 1, true));
            Assert.Equal(id, a.EventId);
        }
    }
}
