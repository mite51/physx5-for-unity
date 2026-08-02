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
        /// This is fixed rather than derived from measured latency, and that is the whole
        /// basis of bit-exact hashing. Every peer performing the identical sequence of
        /// operations every tick is what keeps them bit-identical; a peer that rewound
        /// three ticks and one that rewound five have different histories and will drift
        /// apart, slowly but measurably.
        /// <para>
        /// The cost is that a peer whose inputs arrive later than this horizon stalls
        /// rather than predicting further ahead. Size it for the worst round trip the
        /// session should tolerate: at 60 Hz, 6 ticks is 100 ms.
        /// </para>
        /// </remarks>
        [Tooltip("Fixed number of ticks predicted ahead of the confirmed tick. Must match on every peer.")]
        public int PredictionHorizon = 6;

        /// <summary>
        /// How many past ticks of state are retained, bounding rollback distance and
        /// deciding how far back a late input can still be applied.
        /// </summary>
        [Tooltip("Snapshots retained. Must be larger than PredictionHorizon.")]
        public int SnapshotHistory = 32;

        // ------------------------------------------------------------ simulation ----

        /// <summary>Gravity, in metres per second squared.</summary>
        public Vector3 Gravity = new Vector3(0.0f, -9.81f, 0.0f);

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
            if (SnapshotHistory <= PredictionHorizon)
            {
                reason = string.Format(
                    "SnapshotHistory ({0}) must exceed PredictionHorizon ({1}), otherwise the tick a rollback " +
                    "needs to rewind to has already been overwritten.",
                    SnapshotHistory, PredictionHorizon);
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
        /// </remarks>
        public ulong ComputeHash()
        {
            ulong hash = 0xcbf29ce484222325UL;
            hash = SimHash.Combine(hash, TickRate);
            hash = SimHash.Combine(hash, PredictionHorizon);
            hash = SimHash.Combine(hash, Gravity.x);
            hash = SimHash.Combine(hash, Gravity.y);
            hash = SimHash.Combine(hash, Gravity.z);
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

            // PxSolverType::eTGS. More stable for stacks and articulations than PGS, and
            // fixed for the same reason as the pruner.
            desc.SolverType = 1;

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
