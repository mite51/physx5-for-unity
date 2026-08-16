using System;

namespace UNDPWR.Net
{
    /// <summary>Immutable authoritative-session latency and rollback counters.</summary>
    public struct SimNetStats
    {
        public double SmoothedRttMilliseconds;
        public double JitterMilliseconds;
        public int InputLeadTicks;
        public long AcceptedInputs;
        public long RetimedInputs;
        public long RejectedInputs;
        public long Mispredictions;
        public long Rebuilds;
    }

    /// <summary>Adaptive input lead with fast increase and deliberately slow decrease.</summary>
    public sealed class SimAdaptiveInputLead
    {
        private readonly SimNetConfig _config;
        private readonly int _tickRate;
        private int _lateSamples;
        private long _lastLateAt;
        private bool _hasRtt;
        private double _rttMicroseconds;
        private double _jitterMicroseconds;

        public int CurrentLead { get; private set; }
        public double SmoothedRttMilliseconds { get { return _rttMicroseconds / 1000.0; } }
        public double JitterMilliseconds { get { return _jitterMicroseconds / 1000.0; } }

        public SimAdaptiveInputLead(SimNetConfig config, int tickRate)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (tickRate <= 0) throw new ArgumentOutOfRangeException("tickRate");
            _config = config;
            _tickRate = tickRate;
            CurrentLead = config.InitialInputLead;
        }

        public void RecordRtt(long roundTripMicroseconds)
        {
            if (roundTripMicroseconds < 0)
            {
                return;
            }
            if (!_hasRtt)
            {
                _rttMicroseconds = roundTripMicroseconds;
                _jitterMicroseconds = 0;
                _hasRtt = true;
                return;
            }
            double difference = Math.Abs(roundTripMicroseconds - _rttMicroseconds);
            _jitterMicroseconds += (difference - _jitterMicroseconds) * 0.25;
            _rttMicroseconds += (roundTripMicroseconds - _rttMicroseconds) * 0.125;
        }

        public void RecordDecision(SimAdmissionDisposition disposition, long nowMicroseconds)
        {
            if (disposition == SimAdmissionDisposition.Retimed)
            {
                _lateSamples += 1;
                _lastLateAt = nowMicroseconds;
                if (_lateSamples >= _config.LateSamplesBeforeLeadIncrease)
                {
                    CurrentLead = Math.Min(_config.MaximumInputLead, CurrentLead + 1);
                    _lateSamples = 0;
                }
            }
            else if (disposition == SimAdmissionDisposition.Accepted)
            {
                _lateSamples = 0;
            }
        }

        /// <summary>Applies measured relay latency and slow downward hysteresis.</summary>
        public void Update(long nowMicroseconds)
        {
            if (_hasRtt)
            {
                double tickMicroseconds = 1000000.0 / _tickRate;
                int measured = (int)Math.Ceiling(
                    (_rttMicroseconds + 2.0 * _jitterMicroseconds) / tickMicroseconds)
                    + _config.InputLeadSafetyMargin;
                measured = Clamp(measured);
                if (measured > CurrentLead)
                {
                    CurrentLead = measured;
                    _lastLateAt = nowMicroseconds;
                }
            }

            long stableMicroseconds = (long)(_config.StableSecondsBeforeLeadDecrease * 1000000.0f);
            if (CurrentLead > _config.MinimumInputLead
                && nowMicroseconds - _lastLateAt >= stableMicroseconds)
            {
                int measuredFloor = _hasRtt
                    ? Clamp((int)Math.Ceiling(
                        (_rttMicroseconds + 2.0 * _jitterMicroseconds)
                        / (1000000.0 / _tickRate)) + _config.InputLeadSafetyMargin)
                    : _config.MinimumInputLead;
                if (CurrentLead > measuredFloor)
                {
                    CurrentLead -= 1;
                }
                _lastLateAt = nowMicroseconds;
            }
        }

        private int Clamp(int value)
        {
            if (value < _config.MinimumInputLead) return _config.MinimumInputLead;
            if (value > _config.MaximumInputLead) return _config.MaximumInputLead;
            return value;
        }
    }
}
