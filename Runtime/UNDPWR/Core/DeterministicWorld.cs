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
    /// happens: measurements showed bit-exactness does not require a separate world, only
    /// that the confirmed timeline advance by a cold restore-and-step, which under PGS is a
    /// pure function of the snapshot before it. <see cref="UNDPWR.Rollback.RollbackEngine"/>
    /// drives that single world for both the confirmed timeline and prediction.
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
        private SimEntryHash[] _constructionScratch = new SimEntryHash[0];

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

        /// <summary>
        /// Whether the scene is actually running GPU rigid-body dynamics, as opposed to
        /// having merely been asked to.
        /// </summary>
        /// <remarks>
        /// A world built with <see cref="SimBackendMode.GpuExperimental"/> silently falls
        /// back to CPU when the native plugin has no CUDA context (a CPU-only plugin build,
        /// or no usable GPU), so <see cref="SimConfig.Backend"/> records the request while
        /// this reads what the scene became. False on an older native DLL that predates the
        /// query, which is treated as "not GPU" rather than allowed to throw.
        /// </remarks>
        public bool IsGpuDynamicsActive
        {
            get
            {
                if (_world == IntPtr.Zero)
                {
                    return false;
                }
                try
                {
                    return NativeMethods.PxwWorldIsGpuDynamicsEnabled(_world) != 0u;
                }
                catch (EntryPointNotFoundException)
                {
                    // A plugin built before this query existed. Report CPU rather than fail.
                    return false;
                }
            }
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
            CreateNativeWorld();
        }

        /// <summary>
        /// Creates the native scene and applies the sleep parameters, from
        /// <see cref="_config"/>. Shared by the constructor and <see cref="RecreateNativeWorld"/>.
        /// </summary>
        private void CreateNativeWorld()
        {
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
                "World created: {0} Hz, local input delay {1} ticks, {2} snapshot history, {3} backend, " +
                "sleep after {4} ticks",
                _config.TickRate, _config.LocalInputDelay, _config.SnapshotHistory, _config.Backend,
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

            PushRegistration(stableId, nativeHandle, kind);

            SimEntity entity = new SimEntity(stableId, nativeHandle, kind);
            _entities.Add(stableId, entity);
            _pendingCommit.Add(stableId);
            return entity;
        }

        /// <summary>
        /// Records a handle in the native registry and pushes the deterministic body
        /// defaults onto it. Shared by <see cref="Register"/> and the re-registration loop
        /// in <see cref="RecreateNativeWorld"/>.
        /// </summary>
        private void PushRegistration(uint stableId, IntPtr nativeHandle, SimHandleKind kind)
        {
            NativeResult result = (NativeResult)NativeMethods.PxwWorldRegister(_world, stableId, nativeHandle, (uint)kind);
            result.ThrowIfFailed(string.Format("PxwWorldRegister(id {0})", stableId));

            // The deterministic defaults have to be pushed onto the body; PhysX does not
            // read them from anywhere. Only a non-kinematic dynamic has a solver to
            // configure. This applies the hashed iteration counts and, just as importantly,
            // the two settings the native determinism suite depends on but PhysX does not
            // default to: speculative CCD off (its contact generation keys off velocity
            // history, so a restored state would behave differently from the one it was
            // captured from) and a bounded max depenetration velocity. Every peer applies
            // the same values, which is why the counts are hashed.
            if (kind == SimHandleKind.RigidDynamic)
            {
                NativeMethods.PxwApplyDeterministicRigidDefaults(
                    nativeHandle, _config.SolverPositionIterations, _config.SolverVelocityIterations);
            }
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

        /// <summary>
        /// Destroys the native scene and rebuilds it from scratch, re-registering every
        /// entity in stable-ID order, for a mid-match join or a synchronised rebuild.
        /// </summary>
        /// <remarks>
        /// This is the difference between a rebuild that agrees and one that only looks like
        /// it does. PhysX assigns each actor an internal index and island node when it enters
        /// a scene, in insertion order, and the solver visits bodies in that order — so two
        /// peers whose internal arrangements differ sum contact impulses differently and drift
        /// apart, however identical the state they restored. A world that has run a match has
        /// an arrangement shaped by every add, remove, enable and disable it saw along the way,
        /// which a joining peer cannot reproduce. Restoring an agreed snapshot into it is the
        /// known-incorrect path (DeterminismInvestigation.md §mid-match join).
        /// <para>
        /// Rebuilding from nothing removes the history. Releasing the scene leaves the actors
        /// themselves alive — they are owned by the Unity PhysX layer, not the scene — so they
        /// can be re-added to a fresh scene, and re-adding them in stable-ID order gives every
        /// peer, joiner included, the identical internal arrangement. The caller restores the
        /// agreed snapshot afterwards; <see cref="UNDPWR.Rollback.RollbackEngine.PrepareForRebuild"/>
        /// does both in the right order.
        /// </para>
        /// <para>
        /// The managed registry is preserved across the rebuild, so stable IDs, kinds and
        /// enabled state all survive. Pending, uncommitted changes are folded into the rebuild.
        /// </para>
        /// </remarks>
        public void RecreateNativeWorld()
        {
            ThrowIfDisposed();

            // Rebuild in stable-ID order, the same order CommitPending would insert in, so
            // the fresh internal arrangement is the one every peer reaches independently.
            List<SimEntity> ordered = new List<SimEntity>(_entities.Values);
            ordered.Sort(CompareByStableId);

            NativeMethods.PxwWorldDestroy(_world);
            _world = IntPtr.Zero;

            CreateNativeWorld();

            _pendingCommit.Clear();
            for (int i = 0; i < ordered.Count; ++i)
            {
                SimEntity entity = ordered[i];
                PushRegistration(entity.StableId, entity.NativeHandle, entity.Kind);
                _pendingCommit.Add(entity.StableId);
            }

            NativeResult commit = (NativeResult)NativeMethods.PxwWorldCommitPending(_world);
            commit.ThrowIfFailed("PxwWorldCommitPending (rebuild)");
            _pendingCommit.Clear();

            // A fresh world starts everything enabled, so the disabled entries have to be put
            // back the way they were before the rebuild.
            for (int i = 0; i < ordered.Count; ++i)
            {
                SimEntity entity = ordered[i];
                if (!entity.Enabled)
                {
                    NativeResult disable = (NativeResult)NativeMethods.PxwWorldSetEntryEnabled(
                        _world, entity.StableId, false);
                    if (!disable.Succeeded())
                    {
                        SimLog.Error(string.Format(
                            "Rebuild: PxwWorldSetEntryEnabled(id {0}, false) returned {1}", entity.StableId, disable));
                    }
                }
            }

            InvalidateScratch();
            SimLog.Info(string.Format("Native world rebuilt with {0} entities re-registered in stable-ID order",
                ordered.Count));
        }

        private static int CompareByStableId(SimEntity a, SimEntity b)
        {
            return a.StableId.CompareTo(b.StableId);
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
        /// Discards the simulation state PhysX carries between steps. A manual
        /// hard-resynchronisation tool only.
        /// </summary>
        /// <remarks>
        /// The rollback path never calls this: the cold-step discipline re-poses every body
        /// each step, which invalidates PhysX's contact cache, so restore + step is already
        /// a pure function of the restored state and there is no residue to remove. Calling
        /// this on every restore is actively harmful under variable-depth rollback, because
        /// peers rewind by different amounts and would reset a different number of times.
        /// Reserve it for a deliberate, one-shot hard resynchronisation, where discarding
        /// history is the explicit intent.
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
        /// Comparable bit-for-bit against another peer at a confirmed tick, because the
        /// confirmed timeline is advanced by a cold restore-and-step that is a pure function
        /// of the snapshot before it under PGS, independent of how far each peer predicted.
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
        /// Hashes how every registered body was built, as opposed to what state it is in.
        /// </summary>
        /// <remarks>
        /// Covers shape count and attachment order, each shape's geometry, local pose,
        /// contact and rest offsets, flags, filter data and material coefficients, and each
        /// body's mass properties, damping, velocity and depenetration clamps, solver
        /// iteration counts and thresholds.
        /// <para>
        /// None of that appears in a snapshot, in <see cref="CaptureState"/>'s hash or in
        /// <see cref="HashPerEntry"/>, because none of it changes as the simulation runs --
        /// and all of it is read by every solve. Two peers that construct the same entity
        /// differently therefore agree on every number the session exchanges and still
        /// diverge, with an unbounded delay between the cause and the symptom: the
        /// difference does nothing at all until the body is loaded hard enough for it to
        /// matter, and then the desync looks like anything but a construction bug.
        /// </para>
        /// <para>
        /// Peers exchange this once after the world is built and again after a rebuild. It
        /// matters most for compounds of offset shapes: a ball built from a core sphere and
        /// two dozen offset spikes has twenty-five geometries, local poses and material
        /// bindings that all have to match, and because a near-isotropic compound's mass is
        /// deliberately canonicalised (see <see cref="SimMass.Setup"/>) its mass hash is
        /// specifically designed not to reflect small shape differences. A single ULP in one
        /// spike's local pose leaves the mass hash and the state hash identical, and desyncs
        /// the ball within a couple of seconds of it being squeezed between two other
        /// bodies. Use <see cref="CompareConstruction"/> to find out which body differs.
        /// </para>
        /// </remarks>
        public ulong HashConstruction()
        {
            ThrowIfDisposed();
            return NativeMethods.PxwWorldHashConstruction(_world);
        }

        /// <summary>
        /// Reads the per-body construction hashes, in stable-ID order.
        /// </summary>
        /// <param name="count">How many of the returned entries are meaningful.</param>
        /// <returns>
        /// A buffer reused between calls; copy anything that must outlive the next call.
        /// </returns>
        public unsafe SimEntryHash[] ReadConstructionHashes(out int count)
        {
            ThrowIfDisposed();

            int capacity = (int)NativeMethods.PxwWorldGetEntryCount(_world);
            if (_constructionScratch.Length < capacity)
            {
                _constructionScratch = new SimEntryHash[capacity];
            }

            fixed (SimEntryHash* dst = _constructionScratch)
            {
                count = (int)NativeMethods.PxwWorldHashConstructionPerEntry(
                    _world, dst, (uint)_constructionScratch.Length);
            }
            return _constructionScratch;
        }

        /// <summary>
        /// Compares how this peer built its bodies against how another peer built theirs,
        /// and describes the first disagreement.
        /// </summary>
        /// <param name="peer">
        /// Records from another peer, as produced by <see cref="ReadConstructionHashes"/>.
        /// </param>
        /// <param name="peerCount">How many of <paramref name="peer"/> are meaningful.</param>
        /// <param name="problem">
        /// A description of the disagreement, or <c>null</c> when the two peers built the
        /// same bodies.
        /// </param>
        /// <returns><c>true</c> when the two peers agree.</returns>
        public unsafe bool CompareConstruction(SimEntryHash[] peer, int peerCount, out string problem)
        {
            ThrowIfDisposed();

            if (peer == null)
            {
                throw new ArgumentNullException("peer");
            }

            int localCount;
            SimEntryHash[] local = ReadConstructionHashes(out localCount);

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

                if (local[i].Hash != peer[i].Hash)
                {
                    problem = string.Format(
                        "Stable ID {0} was built differently on the two peers (construction hash 0x{1:X16} " +
                        "vs 0x{2:X16}). The shapes, their local poses or offsets, the materials, the mass, " +
                        "the solver iteration counts or the depenetration clamp differ. Every peer must " +
                        "build this entity from identical values; note that a compound of offset shapes has " +
                        "one geometry, local pose and material per shape to get right, and that a one-ULP " +
                        "difference in any of them is enough to desync the body once it is squeezed between " +
                        "two others while leaving it in perfect agreement until then.",
                        local[i].StableId, local[i].Hash, peer[i].Hash);
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

        private IntPtr _bodyHandle;

        /// <summary>
        /// The <c>PxRigidActor</c> that body I/O (forces, velocities, pose) acts on. Equal to
        /// <see cref="NativeHandle"/> for every kind except <see cref="SimHandleKind.Vehicle"/>,
        /// which registers by its <c>PxwVehicle</c> handle but is pushed and read through its
        /// chassis. Resolved once and cached, since the chassis is fixed for the vehicle's life.
        /// </summary>
        public IntPtr BodyHandle
        {
            get
            {
                if (_bodyHandle == IntPtr.Zero)
                {
                    _bodyHandle = Kind == SimHandleKind.Vehicle
                        ? NativeMethods.GetVehicleActor(NativeHandle)
                        : NativeHandle;
                }
                return _bodyHandle;
            }
        }

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
