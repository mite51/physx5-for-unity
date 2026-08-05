using System;
using UnityEngine;
using UNDPWR.Interop;

namespace UNDPWR.Core
{
    /// <summary>
    /// Which processor runs the simulation.
    /// </summary>
    public enum SimBackendMode
    {
        /// <summary>
        /// CPU simulation. The only mode with a determinism guarantee, and the default.
        /// </summary>
        Cpu = 0,

        /// <summary>
        /// GPU simulation. Experimental.
        /// </summary>
        /// <remarks>
        /// PhysX makes no cross-machine determinism guarantee for GPU simulation: results
        /// depend on the driver, the card and the scheduling of thousands of concurrent
        /// blocks, none of which are reproducible across peers. Use it for single-player
        /// or presentation-only worlds. A networked <see cref="DeterministicWorld"/>
        /// refuses to start in this mode unless
        /// <see cref="SimConfig.AllowExperimentalGpuNetworking"/> is set, and logs an
        /// error explaining why when it does.
        /// </remarks>
        GpuExperimental = 1
    }

    /// <summary>
    /// Which constraint solver PhysX runs. Mirrors <c>PxSolverType</c>.
    /// </summary>
    /// <remarks>
    /// This choice reaches further than solver quality, because it decides whether replay
    /// is transparent, and transparency is what a variable rollback depth rests on. See
    /// <see cref="SimConfig.Solver"/>.
    /// </remarks>
    public enum SimSolverType
    {
        /// <summary>
        /// Projected Gauss-Seidel. The only solver measured to make replay bitwise
        /// transparent under the cold-step discipline.
        /// </summary>
        ProjectedGaussSeidel = 0,

        /// <summary>
        /// Temporal Gauss-Seidel. Carries per-substep state that a restore does not reach,
        /// so replay is never transparent. The current default.
        /// </summary>
        TemporalGaussSeidel = 1
    }

    /// <summary>
    /// Everything that must be identical on every peer for the simulation to agree.
    /// </summary>
    /// <remarks>
    /// This is the single place where simulation-affecting configuration lives, so that
    /// it can be hashed as a unit and checked at session join. A field that changes the
    /// simulation and is not in here is a determinism bug waiting to happen.
    /// <para>
    /// Everything is set explicitly rather than left to a PhysX default, because a
    /// default that shifts between SDK versions is a silent break. Start from
    /// <see cref="Deterministic"/> and change only what a design actually requires.
    /// </para>
    /// </remarks>
    [Serializable]
    public class SimConfig
    {
        // --------------------------------------------------------------- timing ----

        /// <summary>
        /// Simulation ticks per second. Fixed for the lifetime of a session.
        /// </summary>
        /// <remarks>
        /// The tick rate is part of the simulation: the same inputs at 50 Hz and 60 Hz
        /// produce different results, so it is hashed into the configuration and checked
        /// at join.
        /// </remarks>
        [Tooltip("Simulation ticks per second. Must match on every peer.")]
        public int TickRate = 60;

        /// <summary>
        /// How many ticks ahead of the last confirmed tick a peer simulates.
        /// </summary>
        /// <remarks>
        /// Fixed rather than derived from measured latency, and hashed, because the engine
        /// replays this whole window every tick: the horizon is the length of every peer's
        /// per-frame operation sequence, and those sequences have to match.
        /// <para>
        /// The reason they have to match is narrower than it looks, and it is not that a
        /// rewind is lossy. <c>restore(S); step()</c> was measured to be a pure function of
        /// <c>S</c>, and under PGS the cold-step discipline makes replay bitwise
        /// transparent outright, with peers rewinding by four and by sixteen landing on
        /// identical state. The framework runs TGS, which carries per-substep state that a
        /// restore does not reach and nothing exposed clears. The residual is far below the
        /// resolution of captured state, so one divergent rollback shows nothing at all,
        /// and then it accumulates for a few hundred frames and flips a bit long after the
        /// frame that caused it. Invisible once and fatal later is the worst shape a bug
        /// can have, so peers rewind the same amount every tick regardless of what their
        /// network delivered. Documentation/DeterminismInvestigation.md section 8 records
        /// what would have to change for this to become adaptive.
        /// </para>
        /// <para>
        /// Together with <see cref="LocalInputDelay"/> this sets the latency budget: an
        /// input has <c>PredictionHorizon + LocalInputDelay - 1</c> ticks of flight time
        /// before the peers waiting on it stall, which is 116 ms at the defaults. That is
        /// one-way delivery, not a round trip; nothing in the loop waits for a reply.
        /// Widening the horizon buys tolerance at the cost of a longer replay every frame
        /// and more of the window being guessed. Widening the delay buys the same tolerance
        /// at the cost of local input latency, and removes mispredictions rather than
        /// absorbing them.
        /// </para>
        /// </remarks>
        [Tooltip("Fixed number of ticks predicted ahead of the confirmed tick. Must match on every peer.")]
        public int PredictionHorizon = 6;

        /// <summary>
        /// How many ticks ahead of the tick it is simulating a peer stamps its own input.
        /// </summary>
        /// <remarks>
        /// Peer-local, and deliberately not hashed. An input carries the tick it applies to
        /// and is applied at that tick whenever it arrives, so a peer delaying by two and a
        /// peer delaying by five still simulate the identical input timeline. This is the
        /// one timing field a session does not have to agree on, though a competitive game
        /// will want to agree on it anyway for fairness.
        /// <para>
        /// What it buys is mispredictions that never happen. A remote input is first
        /// guessed <c>LocalInputDelay</c> ticks after its sender produced it, so anything
        /// that crosses the network faster than that is already in hand before the guess is
        /// made. Below the delay there is nothing to correct; above it, prediction and the
        /// horizon take over as before.
        /// </para>
        /// <para>
        /// The cost is exactly what it sounds like: the local player's own action happens
        /// this many ticks after they asked for it. The default spends 33 ms of that to
        /// stop remote players snapping on every input change, which is the trade most
        /// games want. Zero restores the older behaviour of stamping input for the current
        /// tick, the most responsive setting and the one that mispredicts most.
        /// </para>
        /// </remarks>
        [Tooltip("Ticks of delay applied to local input before it is stamped. Peer-local; need not match.")]
        public int LocalInputDelay = 2;

        /// <summary>
        /// How many past ticks of state are retained, bounding rollback distance and
        /// deciding how far back a late input can still be applied.
        /// </summary>
        /// <remarks>
        /// Peer-local and not hashed: a peer that retains more history than another
        /// simulates no differently, it just tolerates a later input. It has to cover the
        /// whole live window, which runs from the confirmed tick to the furthest tick any
        /// input has been stamped for, so <see cref="Validate"/> requires it to exceed
        /// <see cref="PredictionHorizon"/> plus <see cref="LocalInputDelay"/>.
        /// </remarks>
        [Tooltip("Snapshots retained. Must exceed PredictionHorizon + LocalInputDelay.")]
        public int SnapshotHistory = 32;

        // ------------------------------------------------------------ simulation ----

        /// <summary>Gravity, in metres per second squared.</summary>
        public Vector3 Gravity = new Vector3(0.0f, -9.81f, 0.0f);

        /// <summary>
        /// Which constraint solver runs. Hashed, since a session cannot mix the two.
        /// </summary>
        /// <remarks>
        /// PGS is the default. The Phase 1 measurement (Documentation/AdaptiveRollbackPlan.md
        /// §4) is complete and chose it: PGS is bitwise transparent under the cold-step
        /// discipline across every workload the framework is measured on — box grids, stacks
        /// up to eight deep, a settled character capsule, a 40x mass ratio, and articulations
        /// on both a free-swinging and a grounded chain — so two peers can roll back by
        /// different depths and still agree. TGS is not: it carries per-substep state a
        /// restore does not reach, and it diverges under variable depth on a grid at impact,
        /// on deep stacks and on every cold-step transparency test, holding only on quiet,
        /// shallow scenes. Adaptive rollback (Phases 2 and 3) requires transparency to
        /// variable depth, so it requires PGS.
        /// <para>
        /// The cost is small and bounded. On a 16-high stack PGS-cold actually settles
        /// quieter than TGS-cold (residual 0.000146 m/s against 0.003556 m/s) though it sags
        /// about 68 µm more; the one real limit is that a contact chain deeper than eight
        /// bodies defeats variable rollback depth on <em>either</em> solver, which is a
        /// content constraint the native chain-depth diagnostic measures rather than a solver
        /// choice. Set this to TGS only for a strictly fixed-horizon session that wants TGS's
        /// marginally tighter stacks and will never rewind by varying depths; it is hashed,
        /// so every peer must agree.
        /// </para>
        /// </remarks>
        [Tooltip("Constraint solver. Must match on every peer. PGS is required for adaptive rollback. See AdaptiveRollbackPlan.md.")]
        public SimSolverType Solver = SimSolverType.ProjectedGaussSeidel;

        /// <summary>Solver position iterations applied to every dynamic body.</summary>
        public uint SolverPositionIterations = 8;

        /// <summary>Solver velocity iterations applied to every dynamic body.</summary>
        public uint SolverVelocityIterations = 2;

        /// <summary>Relative velocity below which contacts stop bouncing.</summary>
        public float BounceThresholdVelocity = 0.2f;

        /// <summary>Distance within which friction anchors are merged.</summary>
        public float FrictionOffsetThreshold = 0.04f;

        /// <summary>Maximum continuous collision detection passes per step.</summary>
        public uint CcdMaxPasses = 1;

        /// <summary>Whether continuous collision detection runs at all.</summary>
        public bool EnableCcd = false;

        /// <summary>Whether the stabilization pass for resting stacks runs.</summary>
        public bool EnableStabilization = false;

        /// <summary>Whether persistent contact manifolds are used.</summary>
        public bool EnablePcm = true;

        /// <summary>
        /// Speed, in metres per second, below which a body counts as at rest for the
        /// purpose of framework sleeping. See <see cref="SleepTicks"/>.
        /// </summary>
        public float SleepLinearThreshold = 0.05f;

        /// <summary>
        /// Angular speed, in radians per second, below which a body counts as at rest.
        /// See <see cref="SleepTicks"/>.
        /// </summary>
        public float SleepAngularThreshold = 0.05f;

        /// <summary>
        /// How many consecutive ticks a body must stay below both sleep thresholds
        /// before the framework puts it to sleep. Zero disables sleeping, so bodies stay
        /// awake and are pinned against PhysX's own sleep path.
        /// </summary>
        /// <remarks>
        /// The framework decides sleeping, not PhysX, because PhysX's sleep timing
        /// depends on internal contact bookkeeping a snapshot cannot carry and so does
        /// not replay under rollback. The rest counter that drives this decision is in
        /// the snapshot, so it does; the wake counter is pinned high while a body is
        /// awake so PhysX's path never runs. Every peer must agree on all three sleep
        /// fields, which is why they are hashed.
        /// </remarks>
        [Tooltip("Ticks at rest before a body sleeps. 0 keeps everything awake. Must match on every peer.")]
        public uint SleepTicks = 0;

        /// <summary>
        /// Density used when mass properties are computed rather than authored.
        /// </summary>
        public float DefaultDensity = 1000.0f;

        /// <summary>
        /// Relative spread of principal moments below which a body's mass frame is
        /// collapsed to the identity.
        /// </summary>
        /// <remarks>
        /// See <see cref="SimMass"/> for why this exists. The default of 1% covers the
        /// bodies that are actually dangerous, which are the ones that are roughly as
        /// wide as they are tall and deep, without disturbing genuinely elongated bodies
        /// whose principal axes are well defined. Set to zero to keep exact principal
        /// axes and accept the sensitivity.
        /// </remarks>
        public float MassIsotropyTolerance = 0.01f;

        // -------------------------------------------------------------- backend ----

        /// <summary>Which processor runs the simulation.</summary>
        public SimBackendMode Backend = SimBackendMode.Cpu;

        /// <summary>
        /// Permits a networked session to run on the GPU backend despite the lack of any
        /// cross-machine determinism guarantee. For experiments only.
        /// </summary>
        public bool AllowExperimentalGpuNetworking = false;

        /// <summary>
        /// PhysX worker threads. Zero keeps the simulation on the calling thread.
        /// </summary>
        /// <remarks>
        /// Non-zero is safe only alongside enhanced determinism, which is always enabled,
        /// but zero removes the question entirely and is the default for that reason.
        /// </remarks>
        public uint CpuWorkerThreads = 0;

        /// <summary>Suppresses the PhysX Visual Debugger connection.</summary>
        public bool DisablePvd = true;

        // ------------------------------------------------------------ derived ----

        /// <summary>Seconds per tick, the fixed timestep handed to PhysX.</summary>
        public float FixedDeltaTime
        {
            get { return 1.0f / TickRate; }
        }

        /// <summary>
        /// Validates the configuration, returning false and a description when a field
        /// would make the simulation non-deterministic or the rollback engine unusable.
        /// </summary>
        /// <param name="reason">Set to an explanation when validation fails.</param>
        public bool Validate(out string reason)
        {
            if (TickRate <= 0)
            {
                reason = "TickRate must be positive.";
                return false;
            }
            if (PredictionHorizon < 0)
            {
                reason = "PredictionHorizon cannot be negative.";
                return false;
            }
            if (LocalInputDelay < 0)
            {
                reason = "LocalInputDelay cannot be negative.";
                return false;
            }
            if (SnapshotHistory <= PredictionHorizon + LocalInputDelay)
            {
                reason = string.Format(
                    "SnapshotHistory ({0}) must exceed PredictionHorizon ({1}) plus LocalInputDelay ({2}), which " +
                    "together span the live window from the confirmed tick to the furthest tick input has been " +
                    "stamped for. Below that, a tick a rollback still needs has already been overwritten.",
                    SnapshotHistory, PredictionHorizon, LocalInputDelay);
                return false;
            }
            if (DefaultDensity <= 0.0f)
            {
                reason = "DefaultDensity must be positive.";
                return false;
            }
            if (MassIsotropyTolerance < 0.0f || MassIsotropyTolerance >= 1.0f)
            {
                reason = "MassIsotropyTolerance must be in [0, 1).";
                return false;
            }
            if (Backend == SimBackendMode.GpuExperimental && !AllowExperimentalGpuNetworking)
            {
                reason =
                    "The GPU backend has no cross-machine determinism guarantee, so it cannot be used for a " +
                    "networked session. Set AllowExperimentalGpuNetworking to override this for local " +
                    "experiments.";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// A hash of every field that affects the simulation, for comparing configuration
        /// across peers before a session starts.
        /// </summary>
        /// <remarks>
        /// Catching a mismatched tick rate or gravity at join is worth a great deal more
        /// than diagnosing the resulting desync twenty seconds in. Fields that only
        /// affect diagnostics, such as <see cref="DisablePvd"/>, are excluded so that a
        /// peer running with the debugger attached is not rejected.
        /// <para>
        /// <see cref="LocalInputDelay"/> and <see cref="SnapshotHistory"/> are excluded for
        /// a different reason: they are peer-local latency choices that change when a peer
        /// produces an input and how long it keeps a snapshot, never what the simulation
        /// does with either. Hashing them would reject a session over a difference that
        /// cannot desync it.
        /// </para>
        /// </remarks>
        public ulong ComputeHash()
        {
            ulong hash = 0xcbf29ce484222325UL;
            hash = SimHash.Combine(hash, TickRate);
            hash = SimHash.Combine(hash, PredictionHorizon);
            hash = SimHash.Combine(hash, Gravity.x);
            hash = SimHash.Combine(hash, Gravity.y);
            hash = SimHash.Combine(hash, Gravity.z);
            hash = SimHash.Combine(hash, (int)Solver);
            hash = SimHash.Combine(hash, (int)SolverPositionIterations);
            hash = SimHash.Combine(hash, (int)SolverVelocityIterations);
            hash = SimHash.Combine(hash, BounceThresholdVelocity);
            hash = SimHash.Combine(hash, FrictionOffsetThreshold);
            hash = SimHash.Combine(hash, (int)CcdMaxPasses);
            hash = SimHash.Combine(hash, EnableCcd ? 1 : 0);
            hash = SimHash.Combine(hash, EnableStabilization ? 1 : 0);
            hash = SimHash.Combine(hash, EnablePcm ? 1 : 0);
            hash = SimHash.Combine(hash, SleepLinearThreshold);
            hash = SimHash.Combine(hash, SleepAngularThreshold);
            hash = SimHash.Combine(hash, (int)SleepTicks);
            hash = SimHash.Combine(hash, DefaultDensity);
            hash = SimHash.Combine(hash, MassIsotropyTolerance);
            hash = SimHash.Combine(hash, (int)Backend);
            hash = SimHash.Combine(hash, (int)CpuWorkerThreads);
            return hash;
        }

        /// <summary>
        /// Builds the native scene descriptor this configuration describes.
        /// </summary>
        public SimSceneDesc ToSceneDesc()
        {
            SimSceneFlags flags = SimSceneFlags.EnhancedDeterminism;
            if (EnablePcm) flags |= SimSceneFlags.EnablePcm;
            if (EnableCcd) flags |= SimSceneFlags.EnableCcd;
            if (EnableStabilization) flags |= SimSceneFlags.EnableStabilization;
            if (DisablePvd) flags |= SimSceneFlags.DisablePvd;

            SimSceneDesc desc = new SimSceneDesc();
            desc.Gravity = Gravity;
            desc.Flags = (uint)flags;

            // eDYNAMIC_AABB_TREE. Fixed rather than configurable: the pruner affects
            // query results, and a peer that answered a query differently would take a
            // different gameplay decision.
            desc.PruningStructureType = 1;

            desc.SolverType = (int)Solver;

            desc.BroadPhaseType = -1;
            desc.CpuWorkerThreads = CpuWorkerThreads;
            desc.UseGpu = Backend == SimBackendMode.GpuExperimental ? 1u : 0u;
            desc.BounceThresholdVelocity = BounceThresholdVelocity;
            desc.FrictionOffsetThreshold = FrictionOffsetThreshold;
            desc.CcdMaxPasses = CcdMaxPasses;
            return desc;
        }

        /// <summary>
        /// The recommended configuration for a networked deterministic session.
        /// </summary>
        public static SimConfig Deterministic
        {
            get { return new SimConfig(); }
        }

        /// <summary>Returns a field-by-field copy.</summary>
        public SimConfig Clone()
        {
            return (SimConfig)MemberwiseClone();
        }
    }

    /// <summary>
    /// FNV-1a helpers, matching the native layer so that hashes computed on either side
    /// of the interop boundary are comparable.
    /// </summary>
    public static class SimHash
    {
        /// <summary>The FNV-1a 64-bit offset basis.</summary>
        public const ulong OffsetBasis = 0xcbf29ce484222325UL;

        private const ulong Prime = 0x100000001b3UL;

        /// <summary>Folds a byte into a running hash.</summary>
        public static ulong Combine(ulong hash, byte value)
        {
            return (hash ^ value) * Prime;
        }

        /// <summary>Folds an integer into a running hash, little-endian.</summary>
        public static ulong Combine(ulong hash, int value)
        {
            for (int i = 0; i < 4; ++i)
            {
                hash = Combine(hash, (byte)((value >> (i * 8)) & 0xFF));
            }
            return hash;
        }

        /// <summary>Folds an unsigned integer into a running hash, little-endian.</summary>
        public static ulong Combine(ulong hash, uint value)
        {
            return Combine(hash, unchecked((int)value));
        }

        /// <summary>
        /// Folds a float into a running hash by its exact bits, so that two values that
        /// differ in the last bit hash differently.
        /// </summary>
        public static ulong Combine(ulong hash, float value)
        {
            return Combine(hash, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        /// <summary>Folds a 64-bit value into a running hash, little-endian.</summary>
        public static ulong Combine(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; ++i)
            {
                hash = Combine(hash, (byte)((value >> (i * 8)) & 0xFF));
            }
            return hash;
        }

        /// <summary>Folds a span of bytes into a running hash.</summary>
        public static ulong Combine(ulong hash, byte[] data, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                hash = Combine(hash, data[i]);
            }
            return hash;
        }
    }
}
