using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class RegionPresenceTests
    {
        [Fact]
        public void PresenceHasExactLayoutAndRejectsTruncatedOrTrailingFrames()
        {
            var message = new RegionPresenceMessage(0x04030201, 6, true);
            byte[] bytes = MessageCodec.Encode(message);
            Assert.Equal(new byte[] { 28, 1, 2, 3, 4, 6, 0, 0, 0, 1 }, bytes);
            Assert.Equal(10, MessageCodec.GetPayloadSize(message));
            var copy = Assert.IsType<RegionPresenceMessage>(MessageCodec.Decode(bytes));
            Assert.Equal(message.Sequence, copy.Sequence); Assert.Equal(6, copy.Scene); Assert.True(copy.Ready);
            for (int i = 0; i < bytes.Length; i++) Assert.Throws<ProtocolException>(() => MessageCodec.Decode(bytes.AsSpan(0, i)));
            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(new byte[] { 28, 0, 0, 0, 0, 6, 0, 0, 0, 1, 0 }));
        }
        [Theory]
        [InlineData(-2, false)] [InlineData(-1, true)] [InlineData(4096, true)]
        public void InvalidPresenceRejected(int scene, bool ready) =>
            Assert.Throws<ProtocolException>(() => new RegionPresenceMessage(0, scene, ready));
        [Fact]
        public void SeparateRegionsLoadingAndStalePeersCannotShareAmbientSimulation()
        {
            var tracker = new RegionPresenceTracker();
            Assert.False(tracker.SameRegion(6, true, 0));
            Assert.True(tracker.Receive(new RegionPresenceMessage(1, 6, true), 10));
            Assert.True(tracker.SameRegion(6, true, 12.99));
            Assert.False(tracker.SameRegion(5, true, 11));
            Assert.False(tracker.SameRegion(6, false, 11));
            Assert.False(tracker.SameRegion(6, true, 13));
            Assert.False(tracker.SameRegion(6, true, 9));
            Assert.False(tracker.Receive(new RegionPresenceMessage(0, 5, true), 11));
            Assert.Equal(6, tracker.PeerScene);
            Assert.True(tracker.Receive(new RegionPresenceMessage(2, 6, false), 11));
            Assert.False(tracker.SameRegion(6, true, 11));
            tracker.Reset(); Assert.Equal(-1, tracker.PeerScene);
            Assert.True(tracker.Receive(new RegionPresenceMessage(0, 5, true), 12));
            Assert.True(tracker.SameRegion(5, true, 12));
        }
        [Fact]
        public void SequenceWrapNeverRestoresAnOlderRegion()
        {
            var tracker = new RegionPresenceTracker();
            Assert.True(tracker.Receive(new RegionPresenceMessage(uint.MaxValue, 6, true), 1));
            Assert.True(tracker.Receive(new RegionPresenceMessage(0, 5, true), 2));
            Assert.False(tracker.Receive(new RegionPresenceMessage(uint.MaxValue, 6, true), 3));
            Assert.Equal(5, tracker.PeerScene);
            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Receive(new RegionPresenceMessage(1, 6, true), double.NaN));
        }
    }
}
