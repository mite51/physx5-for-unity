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
        /// or presentation-only worlds. Authoritative sessions reject this backend through
        /// their network policy; the toggle remains available for non-networked worlds.
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
        /// How many past ticks of state are retained, bounding rollback distance and
        /// deciding both how far back a late input can still be applied and how far ahead of
        /// the confirmed tick the clock may run.
        /// </summary>
        /// <remarks>
        /// Peer-local and not hashed: retaining more history changes recovery capacity, not
        /// deterministic simulation. The authoritative network policy validates that this
        /// ring covers its input lead and hard-resync thresholds.
        /// </remarks>
        [Tooltip("Snapshots retained for rollback and authoritative recovery.")]
        public int SnapshotHistory = 64;

        // ------------------------------------------------------------ simulation ----

        /// <summary>Gravity, in metres per second squared.</summary>
        public Vector3 Gravity = new Vector3(0.0f, -9.81f, 0.0f);

        /// <summary>
        /// Solver position iterations applied to every dynamic body.
        /// </summary>
        /// <remarks>
        /// UNDPWR always uses Projected Gauss-Seidel. Solver selection is deliberately not a
        /// public option because TGS carries state snapshots cannot restore.
        /// </remarks>
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
        /// See <see cref="SimMass"/> for why this exists. The default of 1% is deliberately
        /// narrow: it collapses only bodies whose principal moments are almost exactly equal,
        /// without disturbing genuinely elongated bodies whose principal axes are well defined.
        /// When the frame collapses, a near-origin centre of mass is snapped to the origin as
        /// well, so the whole mass frame becomes peer-identical rather than only its orientation.
        /// A near-spherical compound that sits just above this (a spiked ball is ~1.3%) will not
        /// collapse at the default; pass a wider tolerance to <see cref="SimMass.Setup"/> for that
        /// one body rather than widening this global default. Set to zero to keep exact principal
        /// axes and accept the sensitivity.
        /// </remarks>
        public float MassIsotropyTolerance = 0.01f;

        // -------------------------------------------------------------- backend ----

        /// <summary>Which processor runs the simulation.</summary>
        public SimBackendMode Backend = SimBackendMode.Cpu;

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

        /// <summary>
        /// Records a per-entity hash alongside every confirmed snapshot, so a desync can name
        /// the body rather than only the tick.
        /// </summary>
        /// <remarks>
        /// Diagnostic, and excluded from <see cref="ComputeHash"/> for the same reason
        /// <see cref="DisablePvd"/> is: it changes what is observed, never what is simulated,
        /// and one peer investigating a desync must not be rejected by the others.
        /// <para>
        /// Off by default because it costs a native walk over every entry on each confirmed
        /// tick. Turn it on when a physics-channel desync needs attributing: every peer that
        /// has it on logs its own table for the disagreeing tick, and the entry whose hash
        /// differs between two such logs is the body that diverged.
        /// </para>
        /// </remarks>
        [Tooltip("Record per-entity hashes each confirmed tick so a desync can name the body. Diagnostic; not hashed.")]
        public bool PerEntityHashDiagnostics = false;

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
            if (SnapshotHistory < 2)
            {
                reason = "SnapshotHistory must retain at least two ticks.";
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
        /// <see cref="SnapshotHistory"/> is excluded because it changes recovery capacity,
        /// never the simulation result.
        /// </para>
        /// </remarks>
        public ulong ComputeHash()
        {
            ulong hash = 0xcbf29ce484222325UL;
            hash = SimHash.Combine(hash, TickRate);
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

            // PxSolverType::ePGS. Fixed because TGS cannot replay transparently.
            desc.SolverType = 0;

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
