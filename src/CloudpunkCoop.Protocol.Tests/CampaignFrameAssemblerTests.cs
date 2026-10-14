using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CampaignFrameAssemblerTests
    {
        private static CampaignChunkMessage C(uint revision, ushort index, ushort count = 3,
            int scene = 6, params byte[] data) => new CampaignChunkMessage(revision, scene, index, count, data);

        [Fact]
        public void ReorderedChunksOnlyProduceCompleteOrderedFrameOnce()
        {
            var a = new CampaignFrameAssembler();
            Assert.False(a.Add(C(1, 2, data: new byte[] { 4, 5 }), out var frame)); Assert.Null(frame);
            Assert.False(a.Add(C(1, 0, data: new byte[] { 1 }), out frame)); Assert.Null(frame);
            Assert.Equal(2, a.PendingChunks); Assert.Equal(3, a.PendingBytes);
            Assert.False(a.Add(C(1, 2, data: new byte[] { 4, 5 }), out frame)); Assert.Null(frame);
            Assert.Equal(2, a.PendingChunks);
            Assert.True(a.Add(C(1, 1, data: new byte[] { 2, 3 }), out frame));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, frame);
            Assert.Equal(0, a.PendingBytes); Assert.Equal(0, a.PendingChunks);
            Assert.False(a.Add(C(1, 1), out frame)); Assert.Null(frame);
        }

        [Fact]
        public void NewerIncompleteRevisionBlocksOlderCompleteAndIncompleteFrames()
        {
            var a = new CampaignFrameAssembler();
            Assert.False(a.Add(C(10, 0, 2, data: new byte[] { 1 }), out _));
            Assert.False(a.Add(C(11, 1, 2, data: new byte[] { 4 }), out _));
            Assert.False(a.Add(C(10, 1, 2, data: new byte[] { 2 }), out _));
            Assert.False(a.Add(C(9, 0, 1), out _));
            Assert.True(a.Add(C(11, 0, 2, data: new byte[] { 3 }), out var frame));
            Assert.Equal(new byte[] { 3, 4 }, frame);
            Assert.False(a.Add(C(10, 0, 1), out _));
            Assert.True(a.Add(C(12, 0, 1, scene: 7), out frame)); Assert.Empty(frame!);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void ConflictsPoisonCurrentRevisionAndKeepWatermark(int conflict)
        {
            var a = new CampaignFrameAssembler();
            a.Add(C(5, 0, 2, data: new byte[] { 1 }), out _);
            var invalid = conflict == 0 ? C(5, 0, 2, data: new byte[] { 2 })
                : conflict == 1 ? C(5, 1, 2, scene: 7) : C(5, 1, 3);
            Assert.False(a.Add(invalid, out var frame)); Assert.Null(frame);
            Assert.Equal(0, a.PendingBytes); Assert.Equal(0, a.PendingChunks);
            Assert.False(a.Add(C(5, 1, 2), out _));
            Assert.False(a.Add(C(5, 0, 1), out _));
            Assert.False(a.Add(C(4, 0, 1), out _));
            Assert.True(a.Add(C(6, 0, 1), out _));
        }

        [Fact]
        public void MaximumFrameIsBoundedAndConcatenatedByIndex()
        {
            var a = new CampaignFrameAssembler();
            for (int i = 63; i >= 0; i--)
            {
                byte[] data = new byte[16384]; Array.Fill(data, (byte)i);
                bool complete = a.Add(C(1, (ushort)i, 64, data: data), out var frame);
                Assert.Equal(i == 0, complete);
                if (i != 0)
                {
                    Assert.Null(frame); Assert.InRange(a.PendingBytes, 0, 1024 * 1024);
                }
                else
                {
                    Assert.Equal(1024 * 1024, frame!.Length);
                    for (int index = 0; index < 64; index++)
                        for (int offset = 0; offset < 16384; offset++)
                            Assert.Equal((byte)index, frame[index * 16384 + offset]);
                }
            }
            Assert.Equal(0, a.PendingBytes);
        }

        [Fact]
        public void ResetDropsPendingDataAndAllowsNewSessionRevisionZero()
        {
            var a = new CampaignFrameAssembler();
            a.Add(C(uint.MaxValue, 0, 2, data: new byte[16384]), out _);
            Assert.False(a.Add(C(0, 0, 1), out _));
            a.Reset(); Assert.Equal(0, a.PendingBytes); Assert.Equal(0, a.PendingChunks);
            Assert.False(a.Add(C(0, 1, 2, data: new byte[] { 2 }), out _));
            Assert.True(a.Add(C(0, 0, 2, data: new byte[] { 1 }), out var frame));
            Assert.Equal(new byte[] { 1, 2 }, frame);
            a.Reset(); Assert.True(a.Add(C(0, 0, 1), out _));
            a.Add(C(1, 0, 2), out _); a.Add(C(1, 0, 2, data: new byte[] { 9 }), out _);
            a.Clear(); Assert.True(a.Add(C(0, 0, 1), out _));
            Assert.Throws<ArgumentNullException>(() => a.Add(null!, out _));
        }
    }
}
