using System;
using System.Collections.Generic;

namespace CloudpunkCoop.Protocol
{
    /// <summary>
    /// Bounded receive-time timeline. Unity objects never enter this class; sampling returns
    /// two wire poses and a blend factor for the main-thread renderer. No prediction.
    /// </summary>
    public sealed class CarSnapshotBuffer
    {
        private readonly struct Sample
        {
            public Sample(CarStateMessage state, double time) { State = state; Time = time; }
            public CarStateMessage State { get; }
            public double Time { get; }
        }

        private readonly List<Sample> _samples = new List<Sample>(64);
        private bool _haveSequence;
        private uint _lastSequence;
        public int Count => _samples.Count;
        public long Rejected { get; private set; }
        public long Teleports { get; private set; }

        public bool Add(CarStateMessage state, double receiveTime)
        {
            if (!double.IsFinite(receiveTime) || !ValidPose(state) ||
                (_haveSequence && unchecked((int)(state.SequenceNumber - _lastSequence)) <= 0) ||
                (_samples.Count > 0 && receiveTime < _samples[_samples.Count - 1].Time))
            {
                Rejected++;
                return false;
            }

            if (_samples.Count > 0)
            {
                CarStateMessage previous = _samples[_samples.Count - 1].State;
                double dx = state.PositionX - previous.PositionX;
                double dy = state.PositionY - previous.PositionY;
                double dz = state.PositionZ - previous.PositionZ;
                if (dx * dx + dy * dy + dz * dz > 100d * 100d)
                {
                    // Scene entry teleports must not fly the proxy through the entire city.
                    _samples.Clear();
                    Teleports++;
                }
                else if (receiveTime == _samples[_samples.Count - 1].Time)
                {
                    // TCP can deliver several 20 Hz snapshots in one main-thread drain.
                    // Spread that coalesced batch backwards at its known send cadence. A
                    // zero-length span would otherwise jump over the middle poses abruptly.
                    for (int i = _samples.Count - 1; i >= 0; i--)
                    {
                        Sample sample = _samples[i];
                        double latestTime = receiveTime -
                            unchecked(state.SequenceNumber - sample.State.SequenceNumber) * 0.050d;
                        if (sample.Time <= latestTime) break;
                        _samples[i] = new Sample(sample.State, latestTime);
                    }
                }
            }

            _lastSequence = state.SequenceNumber;
            _haveSequence = true;
            if (_samples.Count == 64) _samples.RemoveAt(0);
            _samples.Add(new Sample(state, receiveTime));
            return true;
        }

        public bool TrySample(double now, out CarStateMessage from, out CarStateMessage to,
            out float blend, out bool underrun)
        {
            from = default;
            to = default;
            blend = 0f;
            underrun = false;
            if (_samples.Count == 0 || !double.IsFinite(now)) return false;

            double target = now - 0.100d;
            while (_samples.Count > 2 && _samples[1].Time <= target) _samples.RemoveAt(0);
            Sample first = _samples[0];
            Sample last = _samples[_samples.Count - 1];
            if (target >= last.Time)
            {
                from = to = last.State;
                underrun = true;
                return true;
            }
            if (target <= first.Time)
            {
                from = to = first.State;
                return true;
            }

            for (int i = 1; i < _samples.Count; i++)
            {
                Sample next = _samples[i];
                if (next.Time <= target) continue;
                Sample previous = _samples[i - 1];
                from = previous.State;
                to = next.State;
                blend = (float)((target - previous.Time) / (next.Time - previous.Time));
                return true;
            }
            return false;
        }

        public void Clear()
        {
            _samples.Clear();
            _haveSequence = false;
            Rejected = 0;
            Teleports = 0;
        }

        private static bool ValidPose(CarStateMessage state)
        {
            if (!float.IsFinite(state.PositionX) || !float.IsFinite(state.PositionY) ||
                !float.IsFinite(state.PositionZ) || !float.IsFinite(state.RotationX) ||
                !float.IsFinite(state.RotationY) || !float.IsFinite(state.RotationZ) ||
                !float.IsFinite(state.RotationW)) return false;
            double norm = (double)state.RotationX * state.RotationX +
                (double)state.RotationY * state.RotationY + (double)state.RotationZ * state.RotationZ +
                (double)state.RotationW * state.RotationW;
            return norm > 0.001d && norm < 4d;
        }
    }
}
