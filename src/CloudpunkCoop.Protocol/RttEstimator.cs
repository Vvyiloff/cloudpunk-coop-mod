using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Round-trip time estimate built from PING/PONG pairs.
    ///
    /// The ping carries the sender's own clock reading, which the pong echoes unchanged. That
    /// means the sender needs no map of outstanding requests: one uint sequence number is
    /// enough to reject a stale reply, and the timestamp does all the measuring. No clock
    /// synchronisation between the two machines is required, because both readings come from
    /// the same clock.
    ///
    /// Not thread-safe; drive it from the thread that owns the connection.
    /// </summary>
    public sealed class RttEstimator
    {
        /// <summary>How many recent samples the smoothed estimate averages over.</summary>
        public const int DefaultWindowSize = 16;

        private readonly int _windowSize;
        private readonly Queue<double> _samples = new Queue<double>();
        private readonly Dictionary<uint, long> _outstanding = new Dictionary<uint, long>();

        private uint _nextSequence = 1;
        private uint _lastCompletedSequence;
        private double _sum;

        public RttEstimator(int windowSize = DefaultWindowSize, Func<long>? clock = null)
        {
            if (windowSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(windowSize), windowSize, "Window size must be positive.");
            }

            _windowSize = windowSize;
            Clock = clock ?? (() => Environment.TickCount64);
        }

        /// <summary>Monotonic millisecond clock used for measurements.</summary>
        public Func<long> Clock { get; }

        /// <summary>Pings sent whose pong has not arrived.</summary>
        public int OutstandingCount => _outstanding.Count;

        /// <summary>Pongs matched to a ping since construction.</summary>
        public long CompletedCount { get; private set; }

        /// <summary>Pongs that arrived for a sequence number we never sent or already completed.</summary>
        public long StaleCount { get; private set; }

        /// <summary>True once at least one round trip has been measured.</summary>
        public bool HasSamples => _samples.Count > 0;

        /// <summary>Mean round trip over the window, in milliseconds; 0 when unknown.</summary>
        public double AverageMs => _samples.Count == 0 ? 0d : _sum / _samples.Count;

        /// <summary>Lowest round trip currently in the window, in milliseconds.</summary>
        public double MinMs
        {
            get
            {
                double min = double.MaxValue;
                foreach (double sample in _samples)
                {
                    if (sample < min)
                    {
                        min = sample;
                    }
                }

                return _samples.Count == 0 ? 0d : min;
            }
        }

        /// <summary>Highest round trip currently in the window, in milliseconds.</summary>
        public double MaxMs
        {
            get
            {
                double max = 0d;
                foreach (double sample in _samples)
                {
                    if (sample > max)
                    {
                        max = sample;
                    }
                }

                return max;
            }
        }

        /// <summary>
        /// Builds the next PING. The caller must send it; the estimator records the send time
        /// itself so the measurement starts from the moment the ping was prepared.
        /// </summary>
        public PingMessage CreatePing()
        {
            long now = Clock();
            uint sequence = _nextSequence++;

            _outstanding[sequence] = now;
            return new PingMessage(sequence, now);
        }

        /// <summary>
        /// Builds the matching PONG for a received ping, echoing both fields unchanged.
        /// </summary>
        public static PongMessage CreatePong(PingMessage ping) =>
            new PongMessage(ping.SequenceNumber, ping.SentAtMilliseconds);

        /// <summary>
        /// Records an incoming pong. Returns false when it matches no outstanding ping, which
        /// happens on a duplicate or a pong for a sequence number that was never sent.
        /// </summary>
        public bool RecordPong(PongMessage pong)
        {
            if (!_outstanding.TryGetValue(pong.SequenceNumber, out long sentAt))
            {
                StaleCount++;
                return false;
            }

            // Guard against a replayed pong overwriting a newer measurement.
            if (pong.SequenceNumber <= _lastCompletedSequence)
            {
                StaleCount++;
                _outstanding.Remove(pong.SequenceNumber);
                return false;
            }

            _outstanding.Remove(pong.SequenceNumber);
            _lastCompletedSequence = pong.SequenceNumber;
            CompletedCount++;

            double elapsed = Clock() - sentAt;

            // A negative or absurd value means the clock was misused rather than the network
            // being slow; drop it instead of poisoning the average.
            if (elapsed < 0d || elapsed > 60000d)
            {
                return false;
            }

            _samples.Enqueue(elapsed);
            _sum += elapsed;
            while (_samples.Count > _windowSize)
            {
                _sum -= _samples.Dequeue();
            }

            return true;
        }

        /// <summary>Drops outstanding pings older than <paramref name="timeoutMs"/>; returns how many were dropped.</summary>
        public int PruneOutstanding(long timeoutMs = 5000)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "Timeout must be positive.");
            }

            long now = Clock();
            var expired = new List<uint>();

            foreach (KeyValuePair<uint, long> entry in _outstanding)
            {
                if (now - entry.Value > timeoutMs)
                {
                    expired.Add(entry.Key);
                }
            }

            foreach (uint sequence in expired)
            {
                _outstanding.Remove(sequence);
            }

            return expired.Count;
        }

        /// <summary>Forgets all measurements and outstanding pings.</summary>
        public void Reset()
        {
            _samples.Clear();
            _outstanding.Clear();
            _sum = 0d;
            CompletedCount = 0;
            StaleCount = 0;
            _lastCompletedSequence = 0;
        }

        public override string ToString() =>
            FormattableString.Invariant(
                $"RTT(avg={AverageMs:0.0}ms, min={MinMs:0.0}ms, max={MaxMs:0.0}ms, samples={_samples.Count})");
    }
}