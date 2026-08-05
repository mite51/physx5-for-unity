using System;
using System.Collections.Generic;
using UnityEngine;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;

namespace UNDPWR.Core
{
    /// <summary>
    /// One deterministic physics world: a PhysX scene, a stable-ID registry, and the
    /// snapshot operations a rollback needs.
    /// </summary>
    /// <remarks>
    /// A peer runs exactly one of these. The framework used to consider running a second
    /// world to hold a confirmed timeline alongside a predicted one, and that is not what
    /// happens: measurements showed bit-exactness does not require a separate world, it
    /// requires that every peer perform an identical sequence of operations, which
    /// <see cref="UNDPWR.Rollback.RollbackEngine"/> arranges with a fixed prediction
    /// horizon.
    ///
    /// <para><b>Why registration is deferred.</b> PhysX only guarantees reproducible
    /// results when actors are inserted into the scene in the same order. Gameplay code
    /// spawns things in whatever order it likes, and two peers will not agree on that
    /// order, so <see cref="Register"/> only records the intent. The actors reach the
    /// scene when <see cref="CommitPending"/> runs, sorted by stable ID, so every peer
    /// issues an identical sequence of insertions no matter what its gameplay code did.
    /// This is the single most important thing the layer does; getting it wrong produces
    /// a desync that looks like a physics bug.</para>
    ///
    /// <para><b>What a snapshot does and does not contain.</b> Capture records pose,
    /// velocity, sleep state and articulation joint state. It cannot record the solver
    /// warm-start impulses PhysX keeps in its persistent contact manifolds, because no
    /// public API exposes them.
    ///
    /// That turns out not to matter, for a reason worth stating plainly because the
    /// opposite was believed here for a long time. Restoring does not preserve the
    /// warm-start data, but it does discard it <i>completely and identically</i>
    /// whatever the world did beforehand, so a step taken straight after a restore is a
    /// pure function of the snapshot. Two worlds with entirely different histories,
    /// handed the same snapshot, stay bit-identical for as long as they are stepped
    /// together. The asymmetry was never that restoring is lossy; it is that a step
    /// after a restore runs cold and a step after another step runs warm, and the two do
    /// not agree. <see cref="UNDPWR.Rollback.RollbackEngine"/> removes the difference by
    /// restoring before every step, including the ones nobody rolled back. Under PGS that
    /// is enough to make a replayed run bitwise identical to one that was never rolled
    /// back; under TGS it is not, because substep state survives the restore.</para>
    ///
    /// <para><b>Sleeping.</b> PhysX's own sleep timing does not replay, so the framework
    /// drives sleeping itself from a per-body rest counter that is in the snapshot, and
    /// pins the wake counter high while a body is awake so PhysX's path never runs. It is
    /// off by default; set <see cref="SimConfig.SleepTicks"/> to turn it on. Use
    /// <see cref="SetEntityEnabled"/> to stop simulating something entirely, in a way the
    /// snapshot can carry.</para>
    /// </remarks>
    public sealed class DeterministicWorld : IDisposable
    {
        private IntPtr _world;
        private readonly SimConfig _config;
        private readonly Dictionary<uint, SimEntity> _entities = new Dictionary<uint, SimEntity>();
        private readonly List<uint> _pendingCommit = new List<uint>();

        private byte[] _scratch = new byte[0];
        private SimPoseEntry[] _poseScratch = new SimPoseEntry[0];
        private SimEntryHash[] _hashScratch = new SimEntryHash[0];
        private SimInternalIdEntry[] _internalIdScratch = new SimInternalIdEntry[0];

        /// <summary>The configuration this world was created with.</summary>
        public SimConfig Config { get { return _config; } }

        /// <summary>True until <see cref="Dispose"/> runs.</summary>
        public bool IsValid { get { return _world != IntPtr.Zero; } }

        /// <summary>
        /// The native world handle, for the query and contact wrappers in this assembly.
        /// </summary>
        /// <remarks>
        /// Internal on purpose. Gameplay reaches scene queries and contact events through
        /// <see cref="UNDPWR.Gameplay.SimQuery"/> and
        /// <see cref="UNDPWR.Gameplay.SimContacts"/>, which keep the deterministic
        /// stable-ID resolution and sort order that calling the native layer by hand would
        /// bypass.
        /// </remarks>
        internal IntPtr Handle { get { return _world; } }

        /// <summary>
        /// The native <c>PxScene</c>, for the parts of the existing PhysX 5 package that
        /// take one. Do not add or remove actors through it; that bypasses the stable-ID
        /// ordering.
        /// </summary>
        public IntPtr ScenePtr
        {
            get { return _world == IntPtr.Zero ? IntPtr.Zero : NativeMethods.PxwWorldGetScene(_world); }
        }

        /// <summary>How many entries the registry holds, committed and pending.</summary>
        public int EntityCount { get { return _entities.Count; } }

        /// <summary>Every registered entity, keyed by stable ID.</summary>
        public IEnumerable<KeyValuePair<uint, SimEntity>> Entities { get { return _entities; } }

        /// <summary>
        /// Creates a world from a validated configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException">The configuration was null.</exception>
        /// <exception cref="ArgumentException">The configuration failed validation.</exception>
        /// <exception cref="SimNativeException">The native scene could not be created.</exception>
        public DeterministicWorld(SimConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            string reason;
            if (!config.Validate(out reason))
            {
                throw new ArgumentException("Invalid SimConfig: " + reason, "config");
            }

            if (config.Backend == SimBackendMode.GpuExperimental)
            {
                SimLog.Warning(
                    "Creating a world on the experimental GPU backend. PhysX gives no cross-machine " +
                    "determinism guarantee for GPU simulation, so peers will drift apart.");
            }

            _config = config.Clone();

            SimSceneDesc desc = _config.ToSceneDesc();
            _world = NativeMethods.PxwWorldCreate(ref desc);
            if (_world == IntPtr.Zero)
            {
                throw new SimNativeException(NativeResult.NullArgument, "PxwWorldCreate");
            }

            // Before anything is registered, so that every actor is pinned as it enters
            // the scene rather than being pinned retroactively.
            NativeResult sleepResult = (NativeResult)NativeMethods.PxwWorldSetSleepParams(
                _world, _config.SleepLinearThreshold, _config.SleepAngularThreshold, _config.SleepTicks);
            sleepResult.ThrowIfFailed("PxwWorldSetSleepParams");

            SimLog.Info(string.Format(
                "World created: {0} Hz, horizon {1} ticks, local input delay {2} ticks, {3} backend, " +
                "sleep after {4} ticks",
                _config.TickRate, _config.PredictionHorizon, _config.LocalInputDelay, _config.Backend,
                _config.SleepTicks == 0 ? "never" : _config.SleepTicks.ToString()));
        }

        // ---------------------------------------------------------- registration ----

        /// <summary>
        /// Records an actor under a stable ID. The actor does not enter the scene until
        /// <see cref="CommitPending"/> runs.
        /// </summary>
        /// <param name="stableId">
        /// The identity every peer knows this object by. Must be produced by
        /// <see cref="StableIdAllocator"/> or by content authoring, never by spawn order.
        /// </param>
        /// <param name="nativeHandle">The <c>PxActor</c> or articulation pointer.</param>
        /// <param name="kind">What sort of object the handle refers to.</param>
        /// <returns>The registered entity.</returns>
        /// <exception cref="SimNativeException">
        /// The ID was already in use, or the handle was null.
        /// </exception>
        public SimEntity Register(uint stableId, IntPtr nativeHandle, SimHandleKind kind)
        {
            ThrowIfDisposed();

            if (nativeHandle == IntPtr.Zero)
            {
                throw new SimNativeException(NativeResult.NullArgument,
                    string.Format("Register(id {0}) with a null handle", stableId));
            }
            if (_entities.ContainsKey(stableId))
            {
                throw new SimNativeException(NativeResult.DuplicateId,
                    string.Format("Register(id {0}); that ID already belongs to another actor", stableId));
            }

            NativeResult result = (NativeResult)NativeMethods.PxwWorldRegister(_world, stableId, nativeHandle, (uint)kind);
            result.ThrowIfFailed(string.Format("PxwWorldRegister(id {0})", stableId));

            // The configured iteration counts have to be pushed onto the body; PhysX does
            // not read them from anywhere. Only a non-kinematic dynamic has a solver to
            // configure. Every peer applies the same counts, which is why they are hashed.
            if (kind == SimHandleKind.RigidDynamic)
            {
                NativeMethods.PxwSetRigidDynamicSolverIterations(
                    nativeHandle, _config.SolverPositionIterations, _config.SolverVelocityIterations);
            }

            SimEntity entity = new SimEntity(stableId, nativeHandle, kind);
            _entities.Add(stableId, entity);
            _pendingCommit.Add(stableId);
            return entity;
        }

        /// <summary>
        /// Queues removal of a stable ID. Applied by <see cref="CommitPending"/>.
        /// </summary>
        /// <returns>False when the ID was not registered.</returns>
        public bool Unregister(uint stableId)
        {
            ThrowIfDisposed();

            if (!_entities.Remove(stableId))
            {
                SimLog.Warning(string.Format("Unregister(id {0}) ignored; that ID is not registered", stableId));
                return false;
            }

            NativeResult result = (NativeResult)NativeMethods.PxwWorldUnregister(_world, stableId);
            if (!result.Succeeded())
            {
                SimLog.Error(string.Format("PxwWorldUnregister(id {0}) returned {1}", stableId, result));
                return false;
            }

            _pendingCommit.Add(stableId);
            return true;
        }

        /// <summary>
        /// Flushes queued additions and removals into the scene, in stable-ID order.
        /// </summary>
        /// <remarks>
        /// Must be called at the same point in the tick on every peer, and never in the
        /// middle of a rollback replay: inserting an actor part-way through a replay
        /// would give it a different history than the peers that inserted it at the tick
        /// boundary. The rollback engine calls it, so gameplay code generally should not.
        /// </remarks>
        public void CommitPending()
        {
            ThrowIfDisposed();

            if (_pendingCommit.Count == 0)
            {
                return;
            }

            NativeResult result = (NativeResult)NativeMethods.PxwWorldCommitPending(_world);
            result.ThrowIfFailed("PxwWorldCommitPending");

            SimLog.Info(string.Format("Committed {0} registry change(s); {1} entities now in the scene",
                _pendingCommit.Count, _entities.Count));
            _pendingCommit.Clear();

            InvalidateScratch();
        }

        /// <summary>True when registry changes are waiting for <see cref="CommitPending"/>.</summary>
        public bool HasPendingChanges { get { return _pendingCommit.Count > 0; } }

        /// <summary>Looks up a registered entity. Returns false when the ID is unknown.</summary>
        public bool TryGetEntity(uint stableId, out SimEntity entity)
        {
            return _entities.TryGetValue(stableId, out entity);
        }

        /// <summary>
        /// Takes an entity in or out of the simulation without unregistering it, which
        /// keeps its stable ID and its slot in the snapshot layout.
        /// </summary>
        /// <remarks>
        /// Preferred over unregistering for anything that comes and goes, such as a
        /// pooled projectile. Unregistering changes the snapshot layout, and a layout
        /// change part-way through a session has to be agreed by every peer.
        /// </remarks>
        public bool SetEntityEnabled(uint stableId, bool enabled)
        {
            ThrowIfDisposed();

            SimEntity entity;
            if (!_entities.TryGetValue(stableId, out entity))
            {
                SimLog.Warning(string.Format("SetEntityEnabled(id {0}) ignored; that ID is not registered", stableId));
                return false;
            }

            NativeResult result = (NativeResult)NativeMethods.PxwWorldSetEntryEnabled(_world, stableId, enabled);
            if (!result.Succeeded())
            {
                SimLog.Error(string.Format("PxwWorldSetEntryEnabled(id {0}) returned {1}", stableId, result));
                return false;
            }

            entity.Enabled = enabled;
            return true;
        }

        // ------------------------------------------------------------- stepping ----

        /// <summary>
        /// Advances the simulation by exactly one tick.
        /// </summary>
        /// <remarks>
        /// Always one fixed timestep, never a variable frame time. A variable step makes
        /// the result depend on frame rate, which differs on every machine.
        /// </remarks>
        public void Step()
        {
            ThrowIfDisposed();
            NativeMethods.PxwWorldStep(_world, _config.FixedDeltaTime);
        }

        /// <summary>
        /// Begins a tick without waiting for it, for overlapping simulation with other
        /// work. Must be paired with <see cref="FetchResults"/> before any state is read.
        /// </summary>
        public void Simulate()
        {
            ThrowIfDisposed();
            NativeMethods.PxwWorldSimulate(_world, _config.FixedDeltaTime);
        }

        /// <summary>Completes a tick started by <see cref="Simulate"/>.</summary>
        public void FetchResults()
        {
            ThrowIfDisposed();
            NativeMethods.PxwWorldFetchResults(_world);
        }

        /// <summary>
        /// Discards the simulation state PhysX carries between steps.
        /// </summary>
        /// <remarks>
        /// Not part of a normal rollback. Measurements show that wiping the contact
        /// caches makes a replay four orders of magnitude less accurate, because it
        /// throws away warm-start data the original tick actually had. Reserve it for a
        /// hard resynchronisation, where discarding history is the point.
        /// </remarks>
        public void ResetContactState(SimContactResetMode mode)
        {
            ThrowIfDisposed();
            NativeMethods.PxwWorldResetContactStateEx(_world, (uint)mode);
        }

        // ---------------------------------------------------------------- state ----

        /// <summary>Bytes required for a full snapshot of the current registry.</summary>
        public int StateSize
        {
            get
            {
                ThrowIfDisposed();
                return (int)NativeMethods.PxwWorldStateSize(_world);
            }
        }

        /// <summary>
        /// Captures the world into a buffer, growing it if required.
        /// </summary>
        /// <param name="buffer">
        /// Reused between calls. Passed by reference so it can be grown once and then
        /// left alone, since capture runs several times per frame during a replay.
        /// </param>
        /// <param name="hash">The captured state's hash, computed natively during capture.</param>
        /// <returns>The number of bytes written.</returns>
        public unsafe int CaptureState(ref byte[] buffer, out ulong hash)
        {
            ThrowIfDisposed();

            int required = (int)NativeMethods.PxwWorldStateSize(_world);
            if (buffer == null || buffer.Length < required)
            {
                buffer = new byte[required];
            }

            ulong localHash = 0;
            uint written;
            fixed (byte* dst = buffer)
            {
                written = NativeMethods.PxwWorldCaptureState(_world, dst, (uint)buffer.Length, &localHash);
            }

            hash = localHash;
            if (written == 0)
            {
                SimLog.Error(string.Format(
                    "PxwWorldCaptureState wrote nothing; needed {0} bytes into a {1} byte buffer",
                    required, buffer.Length));
            }
            return (int)written;
        }

        /// <summary>
        /// Restores the world from a snapshot.
        /// </summary>
        /// <remarks>
        /// The restore is a pure function of the bytes handed in, which is what lets two
        /// peers restoring the same snapshot arrive at bit-identical worlds. It follows
        /// that a snapshot must never be edited after capture.
        /// </remarks>
        /// <param name="buffer">The snapshot, as produced by <see cref="CaptureState"/>.</param>
        /// <param name="size">How many bytes of the buffer are meaningful.</param>
        /// <exception cref="SimNativeException">
        /// The blob was malformed, from an incompatible version, or described a different
        /// set of entries than this world holds.
        /// </exception>
        public unsafe void RestoreState(byte[] buffer, int size)
        {
            ThrowIfDisposed();

            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (size <= 0 || size > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("size",
                    string.Format("{0} is not a valid length for a {1} byte buffer", size, buffer.Length));
            }

            NativeResult result;
            fixed (byte* src = buffer)
            {
                result = (NativeResult)NativeMethods.PxwWorldRestoreState(_world, src, (uint)size);
            }

            if (result == NativeResult.EntryMismatch)
            {
                throw new SimNativeException(result,
                    "PxwWorldRestoreState; the snapshot describes a different set of entities than this world " +
                    "holds, which means the peers built their worlds differently");
            }
            result.ThrowIfFailed("PxwWorldRestoreState");
        }

        /// <summary>
        /// Hashes the live world.
        /// </summary>
        /// <remarks>
        /// Comparable bit-for-bit against another peer only when both have run an
        /// identical sequence of operations. That is what the fixed prediction horizon
        /// guarantees, and why the horizon is not adaptive.
        /// </remarks>
        public ulong HashState()
        {
            ThrowIfDisposed();
            return NativeMethods.PxwWorldHashState(_world);
        }

        /// <summary>
        /// Hashes each entity separately, to identify which one diverged.
        /// </summary>
        /// <remarks>
        /// A whole-world hash says only that something is wrong. Comparing per-entity
        /// hashes against another peer names the body, which is usually enough to
        /// recognise the cause.
        /// </remarks>
        /// <returns>
        /// A buffer reused between calls, valid until the next call. Copy anything that
        /// needs to outlive it.
        /// </returns>
        public unsafe SimEntryHash[] HashPerEntity(out int count)
        {
            ThrowIfDisposed();

            int capacity = (int)NativeMethods.PxwWorldGetEntryCount(_world);
            if (_hashScratch.Length < capacity)
            {
                _hashScratch = new SimEntryHash[capacity];
            }

            fixed (SimEntryHash* dst = _hashScratch)
            {
                count = (int)NativeMethods.PxwWorldHashPerEntry(_world, dst, (uint)_hashScratch.Length);
            }
            return _hashScratch;
        }

        /// <summary>
        /// Reads every entity's pose, for driving presentation transforms.
        /// </summary>
        /// <returns>
        /// A buffer reused between calls, valid until the next call.
        /// </returns>
        public unsafe SimPoseEntry[] ReadPoses(out int count)
        {
            ThrowIfDisposed();

            int capacity = (int)NativeMethods.PxwWorldGetEntryCount(_world);
            if (_poseScratch.Length < capacity)
            {
                _poseScratch = new SimPoseEntry[capacity];
            }

            fixed (SimPoseEntry* dst = _poseScratch)
            {
                count = (int)NativeMethods.PxwWorldReadPoses(_world, dst, (uint)_poseScratch.Length);
            }
            return _poseScratch;
        }

        /// <summary>
        /// Reads the PhysX-assigned identity of every registered body, in stable-ID order.
        /// </summary>
        /// <remarks>
        /// Indices are assigned when an actor enters a scene, so call this after the
        /// first <see cref="CommitPending"/> and step.
        /// </remarks>
        /// <param name="count">How many records were written.</param>
        /// <returns>
        /// A buffer reused between calls, valid until the next call. Copy anything that
        /// needs to outlive it.
        /// </returns>
        public unsafe SimInternalIdEntry[] ReadInternalIds(out int count)
        {
            ThrowIfDisposed();

            int capacity = (int)NativeMethods.PxwWorldGetEntryCount(_world);
            if (_internalIdScratch.Length < capacity)
            {
                _internalIdScratch = new SimInternalIdEntry[capacity];
            }

            fixed (SimInternalIdEntry* dst = _internalIdScratch)
            {
                count = (int)NativeMethods.PxwWorldReadInternalIds(_world, dst, (uint)_internalIdScratch.Length);
            }
            return _internalIdScratch;
        }

        /// <summary>
        /// Hashes the mapping from stable ID to PhysX's internal indices.
        /// </summary>
        /// <remarks>
        /// Peers exchange this once, after the world is built and stepped. Equal hashes
        /// mean every peer put the same body in the same place in PhysX's internal
        /// ordering, which is a precondition for their simulations agreeing at all.
        /// <para>
        /// Unequal hashes mean the registration order differs, and no amount of state
        /// synchronisation will fix it: the solver visits bodies in index order, so the
        /// peers sum contact impulses differently and round differently. The resulting
        /// desync is gradual and gives no hint of its cause, which is exactly why it is
        /// worth catching up front. Use <see cref="CompareInternalIds"/> to find out
        /// which body is misplaced.
        /// </para>
        /// </remarks>
        public ulong HashInternalIds()
        {
            ThrowIfDisposed();
            return NativeMethods.PxwWorldHashInternalIds(_world);
        }

        /// <summary>
        /// Compares this world's stable-ID to PhysX-index mapping against a peer's and
        /// describes the first disagreement.
        /// </summary>
        /// <param name="peer">
        /// Records from another peer, as produced by <see cref="ReadInternalIds"/>.
        /// </param>
        /// <param name="peerCount">How many of <paramref name="peer"/> are meaningful.</param>
        /// <param name="problem">
        /// A description of the disagreement, or <c>null</c> when the mappings match.
        /// </param>
        /// <returns><c>true</c> when the two peers agree.</returns>
        public unsafe bool CompareInternalIds(SimInternalIdEntry[] peer, int peerCount, out string problem)
        {
            ThrowIfDisposed();

            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }

            int localCount;
            SimInternalIdEntry[] local = ReadInternalIds(out localCount);

            if (localCount != peerCount)
            {
                problem = string.Format(
                    "Peers registered different numbers of bodies: {0} locally, {1} remotely. " +
                    "Every peer must register the same set of entities before stepping.",
                    localCount, peerCount);
                return false;
            }

            for (int i = 0; i < localCount; i++)
            {
                if (local[i].StableId != peer[i].StableId)
                {
                    problem = string.Format(
                        "Registration order differs at position {0}: this peer has stable ID {1}, " +
                        "the other has {2}. Entities must be committed in ascending stable-ID order.",
                        i, local[i].StableId, peer[i].StableId);
                    return false;
                }

                if (local[i].InternalActorIndex != peer[i].InternalActorIndex ||
                    local[i].IslandNodeIndex != peer[i].IslandNodeIndex)
                {
                    problem = string.Format(
                        "Stable ID {0} was given different PhysX identities: actor index {1} vs {2}, " +
                        "island node {3} vs {4}. The peers will diverge once this body touches another.",
                        local[i].StableId,
                        local[i].InternalActorIndex, peer[i].InternalActorIndex,
                        local[i].IslandNodeIndex, peer[i].IslandNodeIndex);
                    return false;
                }
            }

            problem = null;
            return true;
        }

        /// <summary>
        /// Reads the world poses of an articulation's links, indexed by link index.
        /// </summary>
        /// <returns>The number of links written.</returns>
        public unsafe int ReadArticulationLinkPoses(uint stableId, SimTransform[] destination)
        {
            ThrowIfDisposed();

            if (destination == null)
            {
                throw new ArgumentNullException("destination");
            }

            fixed (SimTransform* dst = destination)
            {
                return (int)NativeMethods.PxwWorldReadArticulationLinkPoses(
                    _world, stableId, dst, (uint)destination.Length);
            }
        }

        /// <summary>
        /// Captures into the world's own scratch buffer and returns its hash, without the
        /// caller having to own a buffer. For diagnostics rather than the rollback path,
        /// which keeps its own snapshot ring.
        /// </summary>
        public ulong CaptureToScratch(out byte[] buffer, out int size)
        {
            ulong hash;
            size = CaptureState(ref _scratch, out hash);
            buffer = _scratch;
            return hash;
        }

        private void InvalidateScratch()
        {
            // The snapshot layout changed with the registry, so anything sized against
            // the old layout is stale. Reallocated lazily on next use.
            _scratch = new byte[0];
        }

        private void ThrowIfDisposed()
        {
            if (_world == IntPtr.Zero)
            {
                throw new ObjectDisposedException("DeterministicWorld");
            }
        }

        /// <summary>Destroys the native world, its registry and its scene.</summary>
        public void Dispose()
        {
            if (_world == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.PxwWorldDestroy(_world);
            _world = IntPtr.Zero;
            _entities.Clear();
            _pendingCommit.Clear();
            SimLog.Info("World destroyed");
        }
    }

    /// <summary>
    /// One registered object: its stable ID, its native handle and what kind it is.
    /// </summary>
    public sealed class SimEntity
    {
        /// <summary>The identity every peer knows this object by.</summary>
        public uint StableId { get; private set; }

        /// <summary>The native <c>PxActor</c> or articulation pointer.</summary>
        public IntPtr NativeHandle { get; private set; }

        /// <summary>What kind of object the handle refers to.</summary>
        public SimHandleKind Kind { get; private set; }

        /// <summary>
        /// Whether the entity is currently simulated. Set through
        /// <see cref="DeterministicWorld.SetEntityEnabled"/>, never directly.
        /// </summary>
        public bool Enabled { get; internal set; }

        /// <summary>
        /// Anything the game wants to hang off the entity, typically the presentation
        /// GameObject. Never read by the framework, so it cannot affect the simulation.
        /// </summary>
        public object UserData { get; set; }

        internal SimEntity(uint stableId, IntPtr nativeHandle, SimHandleKind kind)
        {
            StableId = stableId;
            NativeHandle = nativeHandle;
            Kind = kind;
            Enabled = true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("SimEntity(id {0}, {1}{2})", StableId, Kind, Enabled ? "" : ", disabled");
        }
    }
}
