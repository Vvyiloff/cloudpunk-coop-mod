using System;
using Xunit;

namespace CloudpunkCoop.Protocol.Tests
{
    /// <summary>
    /// Round-trip estimation tests. The clock is injected so measurements are deterministic
    /// instead of depending on how fast the test machine's loopback happens to be.
    /// </summary>
    public class RttEstimatorTests
    {
        /// <summary>Clock the test advances by hand, so RTT values are exact.</summary>
        private sealed class FakeClock
        {
            public long Now { get; set; }

            public void Advance(long milliseconds) => Now += milliseconds;

            public long Read() => Now;
        }

        [Fact]
        public void NoSamples_ReportsZero()
        {
            var estimator = new RttEstimator();

            Assert.False(estimator.HasSamples);
            Assert.Equal(0d, estimator.AverageMs);
            Assert.Equal(0d, estimator.MinMs);
            Assert.Equal(0d, estimator.MaxMs);
        }

        [Fact]
        public void SinglePong_ProducesExactRoundTrip()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            PingMessage ping = estimator.CreatePing();
            clock.Advance(50);

            Assert.True(estimator.RecordPong(RttEstimator.CreatePong(ping)));

            Assert.True(estimator.HasSamples);
            Assert.Equal(50d, estimator.AverageMs);
            Assert.Equal(50d, estimator.MinMs);
            Assert.Equal(50d, estimator.MaxMs);
            Assert.Equal(1L, estimator.CompletedCount);
        }

        [Fact]
        public void CreatePong_EchoesBothFieldsUnchanged()
        {
            var ping = new PingMessage(1234u, 987654321L);

            PongMessage pong = RttEstimator.CreatePong(ping);

            Assert.Equal(1234u, pong.SequenceNumber);
            Assert.Equal(987654321L, pong.SentAtMilliseconds);
        }

        [Fact]
        public void Average_IsMeanOverTheWindow()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            foreach (long latency in new long[] { 10, 20, 30, 40 })
            {
                PingMessage ping = estimator.CreatePing();
                clock.Advance(latency);
                estimator.RecordPong(RttEstimator.CreatePong(ping));
            }

            Assert.Equal(25d, estimator.AverageMs);
            Assert.Equal(10d, estimator.MinMs);
            Assert.Equal(40d, estimator.MaxMs);
        }

        [Fact]
        public void Window_EvictsOldSamples()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(windowSize: 2, clock.Read);

            foreach (long latency in new long[] { 10, 20, 300 })
            {
                PingMessage ping = estimator.CreatePing();
                clock.Advance(latency);
                estimator.RecordPong(RttEstimator.CreatePong(ping));
            }

            // The 10 ms sample has aged out, leaving 20 and 300.
            Assert.Equal(160d, estimator.AverageMs);
            Assert.Equal(20d, estimator.MinMs);
            Assert.Equal(300d, estimator.MaxMs);
        }

        [Fact]
        public void PongForUnknownSequence_IsCountedStale()
        {
            var estimator = new RttEstimator(16, () => 0L);

            Assert.False(estimator.RecordPong(new PongMessage(9999u, 0L)));

            Assert.Equal(1L, estimator.StaleCount);
            Assert.False(estimator.HasSamples);
        }

        [Fact]
        public void DuplicatePong_IsCountedStaleAndDoesNotDoubleCount()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            PingMessage ping = estimator.CreatePing();
            clock.Advance(25);

            Assert.True(estimator.RecordPong(RttEstimator.CreatePong(ping)));
            Assert.False(estimator.RecordPong(RttEstimator.CreatePong(ping)));

            Assert.Equal(1L, estimator.CompletedCount);
            Assert.Equal(1L, estimator.StaleCount);
            Assert.Equal(25d, estimator.AverageMs);
        }

        [Fact]
        public void OutOfOrderOlderPong_IsRejected()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            PingMessage first = estimator.CreatePing();
            PingMessage second = estimator.CreatePing();

            clock.Advance(10);
            Assert.True(estimator.RecordPong(RttEstimator.CreatePong(second)));

            clock.Advance(10);
            Assert.False(estimator.RecordPong(RttEstimator.CreatePong(first)));

            Assert.Equal(1L, estimator.CompletedCount);
            Assert.Equal(1L, estimator.StaleCount);
        }

        [Fact]
        public void OutstandingCount_TracksUnansweredPings()
        {
            var estimator = new RttEstimator(16, () => 0L);

            PingMessage a = estimator.CreatePing();
            estimator.CreatePing();
            Assert.Equal(2, estimator.OutstandingCount);

            estimator.RecordPong(RttEstimator.CreatePong(a));
            Assert.Equal(1, estimator.OutstandingCount);
        }

        [Fact]
        public void PruneOutstanding_DropsOnlyExpiredPings()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            estimator.CreatePing();
            clock.Advance(6000);
            estimator.CreatePing();
            clock.Advance(100);

            // The first ping is 6100 ms old and expires; the second is only 100 ms old.
            Assert.Equal(1, estimator.PruneOutstanding(5000));
            Assert.Equal(1, estimator.OutstandingCount);
        }

        [Fact]
        public void NegativeElapsedTime_IsDiscarded()
        {
            long now = 1000;
            var estimator = new RttEstimator(16, () => now);

            PingMessage ping = estimator.CreatePing();
            now = 500; // clock moved backwards

            Assert.False(estimator.RecordPong(RttEstimator.CreatePong(ping)));
            Assert.False(estimator.HasSamples);
        }

        [Fact]
        public void AbsurdElapsedTime_IsDiscarded()
        {
            long now = 0;
            var estimator = new RttEstimator(16, () => now);

            PingMessage ping = estimator.CreatePing();
            now = 120000; // two minutes; not a real round trip

            Assert.False(estimator.RecordPong(RttEstimator.CreatePong(ping)));
            Assert.False(estimator.HasSamples);
        }

        [Fact]
        public void Reset_ClearsEverything()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            PingMessage ping = estimator.CreatePing();
            clock.Advance(30);
            estimator.RecordPong(RttEstimator.CreatePong(ping));

            estimator.Reset();

            Assert.False(estimator.HasSamples);
            Assert.Equal(0, estimator.OutstandingCount);
            Assert.Equal(0L, estimator.CompletedCount);
            Assert.Equal(0d, estimator.AverageMs);
        }

        [Fact]
        public void SequenceNumbers_AreUniqueAndAdvance()
        {
            var estimator = new RttEstimator(16, () => 0L);

            uint first = estimator.CreatePing().SequenceNumber;
            uint second = estimator.CreatePing().SequenceNumber;

            Assert.NotEqual(first, second);
            Assert.True(second > first);
        }

        [Fact]
        public void Constructor_RejectsNonPositiveWindow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RttEstimator(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RttEstimator(-4));
        }

        [Fact]
        public void ToString_IsHumanReadable()
        {
            var clock = new FakeClock();
            var estimator = new RttEstimator(16, clock.Read);

            PingMessage ping = estimator.CreatePing();
            clock.Advance(12);
            estimator.RecordPong(RttEstimator.CreatePong(ping));

            Assert.Contains("RTT", estimator.ToString(), StringComparison.Ordinal);
            Assert.Contains("12.0ms", estimator.ToString(), StringComparison.Ordinal);
        }
    }
}