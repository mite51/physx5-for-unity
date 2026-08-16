using System;
using UNDPWR.Core;

namespace UNDPWR.Net
{
    /// <summary>Authoritative scheduling, replay-budget, and recovery policy.</summary>
    [Serializable]
    public sealed class SimNetConfig
    {
        public int InitialInputLead = 3;
        public int MinimumInputLead = 1;
        public int MaximumInputLead = 8;
        public int InputLeadSafetyMargin = 1;
        public int ServerMaxFutureTicks = 12;
        public int InputRedundancy = 4;
        public int CanonicalFrameRedundancy = 4;
        public int MaxSimulationStepsPerFrame = 8;
        public int CatchUpWarningTicks = 12;
        public int HardResyncTicks = 30;
        public int LateSamplesBeforeLeadIncrease = 2;
        public float StableSecondsBeforeLeadDecrease = 5.0f;

        /// <summary>Validates this policy against the simulation it will drive.</summary>
        public bool Validate(SimConfig simulation, out string reason)
        {
            return Validate(simulation, true, out reason);
        }

        internal bool ValidateForEngine(SimConfig simulation, out string reason)
        {
            return Validate(simulation, false, out reason);
        }

        private bool Validate(SimConfig simulation, bool requireCpu, out string reason)
        {
            if (simulation == null)
            {
                reason = "Simulation config is required.";
                return false;
            }
            if (requireCpu && simulation.Backend != SimBackendMode.Cpu)
            {
                reason = "Authoritative network sessions require the CPU backend.";
                return false;
            }
            if (MinimumInputLead < 1 || InitialInputLead < MinimumInputLead
                || MaximumInputLead < InitialInputLead)
            {
                reason = "Input lead must satisfy 1 <= minimum <= initial <= maximum.";
                return false;
            }
            if (InputLeadSafetyMargin < 0)
            {
                reason = "InputLeadSafetyMargin cannot be negative.";
                return false;
            }
            if (ServerMaxFutureTicks < MaximumInputLead)
            {
                reason = "ServerMaxFutureTicks must cover MaximumInputLead.";
                return false;
            }
            if (InputRedundancy < 1 || CanonicalFrameRedundancy < 1)
            {
                reason = "Input and canonical-frame redundancy must be positive.";
                return false;
            }
            if (MaxSimulationStepsPerFrame < 1)
            {
                reason = "MaxSimulationStepsPerFrame must be positive.";
                return false;
            }
            if (CatchUpWarningTicks < 1 || HardResyncTicks <= CatchUpWarningTicks)
            {
                reason = "HardResyncTicks must exceed the positive catch-up warning threshold.";
                return false;
            }
            if (simulation.SnapshotHistory <= HardResyncTicks + MaximumInputLead + 1)
            {
                reason = string.Format(
                    "SnapshotHistory ({0}) must exceed HardResyncTicks ({1}) plus MaximumInputLead ({2}) and one safety tick.",
                    simulation.SnapshotHistory, HardResyncTicks, MaximumInputLead);
                return false;
            }
            if (LateSamplesBeforeLeadIncrease < 1 || StableSecondsBeforeLeadDecrease <= 0.0f)
            {
                reason = "Adaptive lead hysteresis values must be positive.";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>Hash of server-enforced fairness and recovery policy.</summary>
        public ulong ComputeHash()
        {
            ulong hash = SimHash.OffsetBasis;
            hash = SimHash.Combine(hash, InitialInputLead);
            hash = SimHash.Combine(hash, MinimumInputLead);
            hash = SimHash.Combine(hash, MaximumInputLead);
            hash = SimHash.Combine(hash, InputLeadSafetyMargin);
            hash = SimHash.Combine(hash, ServerMaxFutureTicks);
            hash = SimHash.Combine(hash, InputRedundancy);
            hash = SimHash.Combine(hash, CanonicalFrameRedundancy);
            hash = SimHash.Combine(hash, MaxSimulationStepsPerFrame);
            hash = SimHash.Combine(hash, CatchUpWarningTicks);
            hash = SimHash.Combine(hash, HardResyncTicks);
            hash = SimHash.Combine(hash, LateSamplesBeforeLeadIncrease);
            hash = SimHash.Combine(hash, StableSecondsBeforeLeadDecrease);
            return hash;
        }

        public static SimNetConfig Authoritative
        {
            get { return new SimNetConfig(); }
        }
    }
}
