using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    public class CarSnapshotBufferTests
    {
        private static CarStateMessage Pose(uint seq, float x = 0f, float w = 1f) =>
            new CarStateMessage(x, 0f, 0f, 0f, 0f, 0f, w, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, seq);

        [Fact]
        public void EmptyBufferHasNoPose()
        {
            Assert.False(new CarSnapshotBuffer().TrySample(1d, out _, out _, out _, out _));
        }

        [Fact]
        public void SamplesAtOneHundredMillisecondDelay()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1, 0), 1d);
            b.Add(Pose(2, 10), 1.05d);
            b.Add(Pose(3, 20), 1.10d);
            Assert.True(b.TrySample(1.125d, out var a, out var z, out var blend, out var underrun));
            Assert.Equal(1u, a.SequenceNumber);
            Assert.Equal(2u, z.SequenceNumber);
            Assert.InRange(blend, 0.499f, 0.501f);
            Assert.False(underrun);
        }

        [Fact]
        public void UnderrunReturnsLatestWithoutExtrapolation()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1), 1d);
            b.Add(Pose(2, 10), 1.05d);
            b.TrySample(2d, out var a, out var z, out var blend, out var underrun);
            Assert.Equal(2u, a.SequenceNumber);
            Assert.Equal(2u, z.SequenceNumber);
            Assert.Equal(0f, blend);
            Assert.True(underrun);
        }

        [Fact]
        public void StartupUsesFirstPoseUntilDelayElapses()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1), 1d);
            b.Add(Pose(2, 10), 1.05d);
            b.TrySample(1.06d, out var a, out var z, out _, out var underrun);
            Assert.Equal(1u, a.SequenceNumber);
            Assert.Equal(1u, z.SequenceNumber);
            Assert.False(underrun);
        }

        [Fact]
        public void DuplicatesAndStaleSequencesAreRejectedIncludingAcrossWrap()
        {
            var b = new CarSnapshotBuffer();
            Assert.True(b.Add(Pose(uint.MaxValue), 1d));
            Assert.True(b.Add(Pose(0), 1.05d));
            Assert.False(b.Add(Pose(uint.MaxValue), 1.06d));
            Assert.False(b.Add(Pose(0), 1.07d));
            Assert.Equal(2, b.Count);
            Assert.Equal(2, b.Rejected);
        }

        [Theory]
        [InlineData(float.NaN, 1f)]
        [InlineData(float.PositiveInfinity, 1f)]
        [InlineData(0f, 0f)]
        [InlineData(0f, float.NaN)]
        public void InvalidPoseDoesNotConsumeSequence(float x, float w)
        {
            var b = new CarSnapshotBuffer();
            Assert.False(b.Add(Pose(1, x, w), 1d));
            Assert.True(b.Add(Pose(1), 1.05d));
        }

        [Fact]
        public void TeleportSnapsInsteadOfInterpolatingAcrossScene()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1), 1d);
            b.Add(Pose(2, 1000), 1.05d);
            b.TrySample(1.125d, out var a, out var z, out _, out _);
            Assert.Equal(1000f, a.PositionX);
            Assert.Equal(1000f, z.PositionX);
            Assert.Equal(1, b.Teleports);
        }

        [Fact]
        public void CoalescedTcpBatchWithSameReceiveTimeIsFinite()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1), 1d);
            b.Add(Pose(2, 10), 1d);
            b.Add(Pose(3, 20), 1.05d);
            b.TrySample(1.125d, out var a, out var z, out var blend, out _);
            Assert.Equal(2u, a.SequenceNumber);
            Assert.Equal(3u, z.SequenceNumber);
            Assert.InRange(blend, 0.499f, 0.501f);
        }

        [Fact]
        public void CoalescedBatchDoesNotJumpOverIntermediatePoses()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1, 0), 1d);
            b.Add(Pose(2, 10), 1.2d);
            b.Add(Pose(3, 20), 1.2d);
            b.Add(Pose(4, 30), 1.2d);
            b.TrySample(1.299d, out var a, out var z, out var blend, out _);
            float before = a.PositionX + (z.PositionX - a.PositionX) * blend;
            b.TrySample(1.301d, out a, out z, out blend, out _);
            float after = a.PositionX + (z.PositionX - a.PositionX) * blend;
            Assert.InRange(after - before, 0f, 0.21f);
        }

        [Fact]
        public void BacklogIsBoundedAndClearAllowsNewSessionSequence()
        {
            var b = new CarSnapshotBuffer();
            for (uint i = 0; i < 1000; i++) b.Add(Pose(i), i / 20d);
            Assert.Equal(64, b.Count);
            b.TrySample(50d, out _, out _, out _, out _);
            Assert.InRange(b.Count, 1, 3);
            b.Clear();
            Assert.Equal(0, b.Count);
            Assert.True(b.Add(Pose(0), 51d));
        }

        [Fact]
        public void ClockRegressionAndNonFiniteTimeAreRejected()
        {
            var b = new CarSnapshotBuffer();
            b.Add(Pose(1), 1d);
            Assert.False(b.Add(Pose(2), 0.9d));
            Assert.False(b.Add(Pose(2), double.NaN));
            Assert.True(b.Add(Pose(2), 1.05d));
        }
    }
}
