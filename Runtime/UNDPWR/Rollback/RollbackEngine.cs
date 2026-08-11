using System;
using System.Collections.Generic;
using UNDPWR.Core;
using UNDPWR.Diagnostics;
using UNDPWR.Interop;

namespace UNDPWR.Rollback
{
    /// <summary>
    /// Applies a tick's inputs to the world before it is stepped.
    /// </summary>
    /// <remarks>
    /// Everything a game does to the simulation goes through here: forces, impulses,
    /// steering, spawning. The reason is that a rollback replays ticks, so any effect
    /// applied outside this callback happens once on the original pass and not at all on
    /// the replay, which desyncs the peer against itself.
    /// <para>
    /// Implementations must be pure with respect to the tick: given the same world state
    /// and the same inputs they must do the same thing. In particular they must not read
    /// wall-clock time, <c>UnityEngine.Random</c>, or frame-rate-dependent values.
    /// </para>
    /// </remarks>
    public interface ISimStepHandler
    {
        /// <summary>
        /// Called immediately before the world is stepped.
        /// </summary>
        /// <param name="world">The world about to be stepped.</param>
        /// <param name="tick">The tick being simulated.</param>
        /// <param name="inputs">Every player's input for this tick, in slot order.</param>
        /// <param name="isReplay">
        /// True when this tick is being resimulated after a rollback. Use it to suppress
        /// one-shot presentation effects such as sounds, never to change the simulation.
        /// </param>
        void OnBeforeStep(DeterministicWorld world, int tick, SimInputFrame inputs, bool isReplay);

        /// <summary>
        /// Called immediately after the world is stepped, before the snapshot is taken.
        /// </summary>
        void OnAfterStep(DeterministicWorld world, int tick, bool isReplay);
    }

    /// <summary>
    /// Drives the simulation forward, rolling back and resimulating when a prediction
    /// turns out to be wrong.
    /// </summary>
    /// <remarks>
    /// <b>What the engine rests on.</b> Only the confirmed timeline is compared between
    /// peers, and it is advanced by a cold restore-and-step per tick. Under PGS that is
    /// bitwise transparent -- <c>restore(S); step()</c> is a pure function of <c>S</c> -- so
    /// however far each peer rewound or led, its confirmed hash for a tick is the same as
    /// everyone else's. That is why the clock can run free and the rewind depth can follow
    /// the network: what peers agree on does not depend on either.
    ///
    /// <para>What the measurements actually say, from the native suite, because the obvious
    /// explanation is the wrong one. A rewind is not lossy: two worlds driven along
    /// deliberately different histories agree bit-for-bit from the moment they are handed
    /// the same snapshot. Under PGS the cold-step discipline below makes replay bitwise
    /// transparent outright, and peers rewinding by four and by sixteen land on identical
    /// state. The framework's confirmed timeline uses PGS for exactly this reason;
    /// <see cref="SimConfig.Validate"/> refuses any other solver, because TGS carries
    /// per-substep state that a restore does not reach and a data-dependent rewind under it
    /// diverges by a residual that is invisible once and fatal several hundred frames later.
    /// Documentation/DeterminismInvestigation.md section 8 records the measurements.</para>
    ///
    /// <para><b>What a tick looks like.</b> Every <see cref="Advance"/>:</para>
    /// <list type="number">
    /// <item><description>drain whatever the confirmed frontier reached into the confirmed
    /// timeline, one cold restore-and-step per tick, capturing each;</description></item>
    /// <item><description>advance the free-running clock one tick of wall time, unless it
    /// would outrun what <see cref="SnapshotRing"/> can retain;</description></item>
    /// <item><description>resimulate the prediction window, but only from the earliest tick a
    /// misprediction or a new confirmation disturbed.</description></item>
    /// </list>
    /// <para>The lead over the confirmed tick is emergent -- it grows when confirmations lag
    /// and shrinks as they arrive -- so a peer never predicts a shared, fixed number of ticks
    /// ahead. A peer that would lead further than the snapshot history allows stalls instead,
    /// a visible pause rather than a silent loss of the window a late input still needs.</para>
    ///
    /// <para><b>Mid-match join</b> is not handled by prediction at all. A joiner cannot
    /// reproduce a history it was not present for, so instead every peer rebuilds from
    /// one agreed snapshot at an agreed tick, which puts them all back on an identical
    /// history. See <see cref="PrepareForRebuild"/>.</para>
    /// </remarks>
    public sealed class RollbackEngine
    {
        private readonly DeterministicWorld _world;
        private readonly SimConfig _config;
        private InputBuffer _inputs;
        private readonly SnapshotRing _snapshots;
        private readonly List<ISimStepHandler> _handlers = new List<ISimStepHandler>();
        private ISimStateProvider _stateProvider;

        private int _confirmedTick = -1;
        private int _currentTick = -1;
        private bool _stalled;

        // Per-entity hashes for recent confirmed ticks, parallel to the snapshot ring and
        // allocated only when the diagnostic is asked for. Null when it is off.
        private readonly SimEntryHash[][] _entityHashes;
        private readonly int[] _entityHashTicks;
        private readonly int[] _entityHashCounts;

        // The earliest tick a misprediction has dirtied since the last Advance, or
        // int.MaxValue when nothing needs correcting. RunPredictionConditional rewinds no
        // further back than this.
        private int _pendingReplayFrom = int.MaxValue;

        /// <summary>The newest tick whose inputs are final and which will not be replayed.</summary>
        public int ConfirmedTick { get { return _confirmedTick; } }

        /// <summary>The newest tick simulated, confirmed or predicted.</summary>
        public int CurrentTick { get { return _currentTick; } }

        /// <summary>
        /// The tick a local input sampled right now should be stamped for.
        /// </summary>
        /// <remarks>
        /// Tracks <see cref="CurrentTick"/> plus <see cref="SimConfig.LocalInputDelay"/>. Build
        /// local input against this rather than against <see cref="CurrentTick"/>: a tick
        /// stamped further ahead reaches the other peers before they have to guess at it,
        /// which is the difference between a remote player who moves and one who moves and
        /// then snaps somewhere else.
        /// <para>
        /// Submit a <em>run</em> of ticks against this, never just this one. Every tick from
        /// the last one submitted through this value has to be covered, because
        /// <see cref="InputBuffer.ConfirmedThrough"/> needs an unbroken run: one missing tick
        /// is fatal rather than untidy, since the frontier stops at it permanently and the peer
        /// stalls for good once the clock reaches its bound, with input appearing to do nothing
        /// at all. Two things skip ticks here and neither is avoidable by stamping more
        /// carefully. Nothing covers the <see cref="SimConfig.LocalInputDelay"/> ticks between
        /// the tick a session starts at and the first tick it stamps for, which is why the
        /// first submitted run has to start at <see cref="CurrentTick"/> and cover the whole
        /// span up to this value rather than stamping a single tick.
        /// </para>
        /// <para>
        /// <see cref="UNDPWR.Net.SimSession.SubmitLocalInput"/> fills the run for you. A caller
        /// driving this engine directly has to loop it itself, from the tick after the last one
        /// it submitted — starting at <see cref="CurrentTick"/> for a fresh session, and at the
        /// resume tick after every <see cref="PrepareForRebuild"/>.
        /// </para>
        /// </remarks>
        public int LocalInputTick { get { return _currentTick + _config.LocalInputDelay; } }

        /// <summary>
        /// True when the peer is waiting for inputs rather than advancing.
        /// </summary>
        /// <remarks>
        /// Reached when the lead over the confirmed tick would outrun what
        /// <see cref="SnapshotRing"/> can retain. Stalling is the intended behaviour: leading
        /// further would overwrite a tick a late input still needs, so the peer pauses
        /// visibly instead of losing the window silently.
        /// </remarks>
        public bool IsStalled { get { return _stalled; } }

        /// <summary>
        /// How far ahead of the confirmed tick the peer is currently simulating.
        /// </summary>
        /// <remarks>
        /// Emergent rather than a shared constant: it grows when confirmations lag and shrinks
        /// as they arrive, bounded by <see cref="SimConfig.SnapshotHistory"/>. A policy that
        /// wants Overwatch-style adaptation tunes the peer-local
        /// <see cref="SimConfig.LocalInputDelay"/> from watching it.
        /// </remarks>
        public int CurrentLead { get { return _currentTick - _confirmedTick; } }

        /// <summary>How many ticks were resimulated on the most recent advance.</summary>
        public int LastReplayLength { get; private set; }

        /// <summary>Total ticks resimulated since the session started, for profiling.</summary>
        public long TotalReplayedTicks { get; private set; }

        /// <summary>The snapshot history, for the netcode layer to read hashes from.</summary>
        public SnapshotRing Snapshots { get { return _snapshots; } }

        /// <summary>The input buffer this engine predicts from.</summary>
        public InputBuffer Inputs { get { return _inputs; } }

        /// <summary>
        /// Creates an engine over a world.
        /// </summary>
        /// <param name="world">The world to drive. Must already have its actors committed.</param>
        /// <param name="playerIds">Every player in the session.</param>
        public RollbackEngine(DeterministicWorld world, IList<uint> playerIds)
        {
            if (world == null)
            {
                throw new ArgumentNullException("world");
            }

            _world = world;
            _config = world.Config;
            _inputs = new InputBuffer(playerIds, _config.SnapshotHistory);
            _snapshots = new SnapshotRing(_config.SnapshotHistory, world.StateSize);

            if (_config.PerEntityHashDiagnostics)
            {
                _entityHashes = new SimEntryHash[_config.SnapshotHistory][];
                _entityHashTicks = new int[_config.SnapshotHistory];
                _entityHashCounts = new int[_config.SnapshotHistory];
                for (int i = 0; i < _entityHashTicks.Length; ++i)
                {
                    _entityHashTicks[i] = -1;
                }
            }
        }

        /// <summary>Registers a step handler. Handlers run in registration order.</summary>
        /// <remarks>
        /// Registration order is part of the simulation, since two handlers applying
        /// forces in a different order produce different floating point results. Register
        /// them from one place at session start, not from each object's initialisation.
        /// </remarks>
        public void AddHandler(ISimStepHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }
            _handlers.Add(handler);
        }

        /// <summary>Removes a previously registered handler.</summary>
        public bool RemoveHandler(ISimStepHandler handler)
        {
            return _handlers.Remove(handler);
        }

        /// <summary>
        /// Registers the managed state channels that are captured and restored with the
        /// physics blob, or clears them by passing null.
        /// </summary>
        /// <remarks>
        /// Set once at session start, before <see cref="Initialise"/>, so tick zero already
        /// carries the managed state. A physics-only world leaves it unset; the engine then
        /// behaves exactly as it did before the gameplay layer existed.
        /// </remarks>
        public void SetStateProvider(ISimStateProvider provider)
        {
            _stateProvider = provider;
        }

        /// <summary>
        /// Captures the initial state as tick zero, before any stepping.
        /// </summary>
        /// <remarks>
        /// Must run after every actor has been registered and committed, and must produce
        /// an identical snapshot on every peer. That is the common starting point the
        /// whole session is built on.
        /// </remarks>
        public void Initialise()
        {
            _world.CommitPending();

            Snapshot slot = _snapshots.BeginWrite(0);
            ulong hash;
            byte[] buffer = slot.Data;
            int size = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, size, hash);
            CaptureManagedInto(slot);
            _snapshots.MarkConfirmedThrough(0);
            CaptureEntityHashes(0);

            _confirmedTick = 0;
            _currentTick = 0;

            SimLog.Info(string.Format(
                "Initialised at tick 0; physics {0} bytes hash 0x{1:X16}, combined hash 0x{2:X16}",
                size, hash, slot.CombinedHash));
        }

        /// <summary>
        /// Records a received input, which may make more ticks confirmable.
        /// </summary>
        /// <remarks>
        /// Takes the local peer's own input as well as everything that arrived over the
        /// network; the two are the same thing once stamped. Stamp local input with
        /// <see cref="LocalInputTick"/> rather than <see cref="CurrentTick"/> so it reaches
        /// the other peers before they guess at it.
        /// <para>
        /// Note what this deliberately does not do: it does not itself rewind. It records how
        /// far back the next <see cref="Advance"/> has to replay from -- the earliest tick a
        /// misprediction disturbed -- and the rewind happens there.
        /// </para>
        /// </remarks>
        public void SubmitInput(SimInput input)
        {
            int mispredictedFrom = _inputs.Submit(input);

            // The return says how far back the next Advance has to replay from: the earliest
            // tick whose input turned out to differ from the guess. Only the earliest since the
            // last Advance matters, so keep the minimum.
            if (mispredictedFrom >= 0 && mispredictedFrom < _pendingReplayFrom)
            {
                _pendingReplayFrom = mispredictedFrom;
            }
        }

        /// <summary>
        /// Advances the simulation by one tick of wall time.
        /// </summary>
        /// <remarks>
        /// Called once per fixed update. The clock advances one tick per call regardless of
        /// how far confirmation has reached, and the lead over the confirmed tick is whatever
        /// the network happens to allow. The only hard stop is running further ahead than
        /// <see cref="SnapshotRing"/> can retain: a lead of
        /// <c>SnapshotHistory - LocalInputDelay - 1</c> is the most that leaves room for both
        /// the retained window and local input stamped ahead, and the peer stalls rather than
        /// outrunning it.
        /// <para>
        /// Peers run different-length prediction windows every frame, which only lands on
        /// agreeing confirmed state because replay is bitwise transparent under PGS and the
        /// confirmed timeline is advanced by the same cold restore-and-step regardless of
        /// window width. <see cref="SimConfig.Validate"/> refuses any other solver.
        /// </para>
        /// </remarks>
        /// <returns>False when the peer is stalled waiting for inputs.</returns>
        public bool Advance()
        {
            int nextTick = _currentTick + 1;

            // Confirmation may not run the clock past wall time. AdvanceConfirmed drags the
            // clock up to whatever it confirms, and local input is stamped LocalInputDelay
            // ticks ahead, so a peer whose own input is the last one a tick is waiting for --
            // a solo host, or anyone during a lull -- finds the frontier permanently that far
            // in the future. Without this it would be pulled to the frontier every frame and
            // the simulation would run LocalInputDelay times faster than real time. Confirming
            // is settling the past; it is not licence to simulate the future early.
            //
            // This never delays anything: a tick held back here is confirmed on the very next
            // call, by which time the clock has reached it.
            int newConfirmed = _inputs.ConfirmedThrough;
            if (newConfirmed > nextTick)
            {
                newConfirmed = nextTick;
            }

            // The most the clock may lead confirmation by. The ring must retain the whole live
            // window, from the confirmed tick out to the furthest tick local input is stamped.
            int maxLead = _config.SnapshotHistory - _config.LocalInputDelay - 1;
            if (maxLead < 1)
            {
                maxLead = 1;
            }
            bool canAdvanceClock = (nextTick - newConfirmed) <= maxLead;

            // Nothing to do only when the clock is pinned by the history limit and no fresh
            // confirmation has arrived to relieve it. New confirmation is always worth
            // processing -- it shrinks the lead and lets the clock move again next frame.
            if (!canAdvanceClock && newConfirmed <= _confirmedTick)
            {
                if (!_stalled)
                {
                    _stalled = true;
                    SimLog.Warning(string.Format(
                        "Stalled at tick {0}: confirmed only through {1}, and the lead cannot exceed {2} ticks " +
                        "without outrunning the {3}-tick snapshot history. Widen SnapshotHistory to lead further, " +
                        "or the peer that is behind needs to catch up.",
                        _currentTick, _confirmedTick, maxLead, _config.SnapshotHistory));
                }
                return false;
            }

            if (_stalled)
            {
                _stalled = false;
                SimLog.Info(string.Format("Resumed at tick {0}", _currentTick));
            }

            AdvanceConfirmed(newConfirmed);

            int windowEnd = canAdvanceClock ? nextTick : _currentTick;
            RunPredictionConditional(windowEnd);
            return true;
        }

        /// <summary>
        /// Steps the confirmed timeline forward to <paramref name="newConfirmed"/>.
        /// </summary>
        /// <remarks>
        /// Each confirmed tick is computed by restoring the previous confirmed snapshot
        /// and taking exactly one step, which is the same operation on every peer, from
        /// the same bytes, with the same inputs. That is what makes the resulting hash
        /// comparable bit-for-bit. The whole confirmed backlog is drained in one call: under
        /// PGS a confirmed tick is a pure function of the snapshot before it, so how many
        /// arrive in a frame -- a property of the network, not the simulation -- cannot change
        /// the state two peers agree on.
        /// </remarks>
        private void AdvanceConfirmed(int newConfirmed)
        {
            while (_confirmedTick < newConfirmed)
            {
                int tick = _confirmedTick + 1;

                RestoreTo(_confirmedTick);
                StepOnce(tick, false);
                CaptureInto(tick, true);
                CaptureEntityHashes(tick);

                _confirmedTick = tick;
                if (_currentTick < tick)
                {
                    _currentTick = tick;
                }
            }
        }

        /// <summary>
        /// Records a hash per entity for a freshly confirmed tick, while the world still holds
        /// exactly that state.
        /// </summary>
        /// <remarks>
        /// This is the only moment the table can be taken cheaply: <see cref="AdvanceConfirmed"/>
        /// has just restored, stepped and captured, so the live world *is* the confirmed tick.
        /// Asking for the same table later would mean restoring the world away from wherever
        /// prediction had left it and putting it back again.
        /// </remarks>
        private void CaptureEntityHashes(int tick)
        {
            if (_entityHashes == null)
            {
                return;
            }

            int count;
            SimEntryHash[] scratch = _world.HashPerEntity(out count);

            int slot = Slot(tick, _entityHashes.Length);
            SimEntryHash[] destination = _entityHashes[slot];
            if (destination == null || destination.Length < count)
            {
                destination = new SimEntryHash[count];
                _entityHashes[slot] = destination;
            }
            Array.Copy(scratch, destination, count);
            _entityHashTicks[slot] = tick;
            _entityHashCounts[slot] = count;
        }

        /// <summary>
        /// The per-entity hashes recorded for a confirmed tick, when
        /// <see cref="SimConfig.PerEntityHashDiagnostics"/> is on and the tick is still retained.
        /// </summary>
        /// <param name="tick">The confirmed tick to look up.</param>
        /// <param name="entries">The recorded table, valid until the slot is reused.</param>
        /// <param name="count">How many entries of <paramref name="entries"/> are meaningful.</param>
        public bool TryGetConfirmedEntityHashes(int tick, out SimEntryHash[] entries, out int count)
        {
            entries = null;
            count = 0;
            if (_entityHashes == null || tick < 0)
            {
                return false;
            }

            int slot = Slot(tick, _entityHashes.Length);
            if (_entityHashTicks[slot] != tick || _entityHashes[slot] == null)
            {
                return false;
            }

            entries = _entityHashes[slot];
            count = _entityHashCounts[slot];
            return true;
        }

        private static int Slot(int tick, int capacity)
        {
            int slot = tick % capacity;
            return slot < 0 ? slot + capacity : slot;
        }

        /// <summary>
        /// Reads the PhysX identity assigned to every registered body, for verifying that two
        /// peers built the world in the same order.
        /// </summary>
        /// <remarks>
        /// A passthrough to <see cref="DeterministicWorld.ReadInternalIds"/> so a session can run
        /// the registration-order check (<see cref="UNDPWR.Net.SimRegistrationCheck"/>) without
        /// reaching past the engine for the world. The returned buffer is reused between calls;
        /// copy anything that must outlive the next call. Valid only after the first step.
        /// </remarks>
        public SimInternalIdEntry[] ReadInternalIds(out int count)
        {
            return _world.ReadInternalIds(out count);
        }

        /// <summary>
        /// Resimulates the prediction window up to <paramref name="windowEnd"/>, but only
        /// from the earliest tick a misprediction or a new confirmation actually disturbed.
        /// </summary>
        /// <remarks>
        /// PGS has transparent replay -- restore-and-step is a pure function of the restored
        /// snapshot (§4) -- so a shorter, data-dependent rewind lands on exactly the state a
        /// full re-simulation would, and any redundant work would be pure cost. This replays
        /// only the ticks whose input changed (<see cref="InputBuffer.Submit"/>'s return) or
        /// that were newly exposed above the last simulated tick, and reuses every valid
        /// snapshot below that.
        /// <para>
        /// The window end is a parameter because the free-running clock hands it the tick it
        /// just advanced to (<c>CurrentTick + 1</c>), or the current tick when it is pinned by
        /// the history bound. On entry <see cref="_currentTick"/> is the last tick that
        /// already has a snapshot; on exit it is <paramref name="windowEnd"/>.
        /// </para>
        /// <para>
        /// The confirmed timeline is advanced first, and its hashes are what peers compare,
        /// so a bug here can only smear this peer's own prediction between confirmations; it
        /// cannot desync the session. Even so this keeps the cold-step discipline: one restore
        /// before every step, the first being the rewind to the tick before the replay start,
        /// and never two restores in a row.
        /// </para>
        /// </remarks>
        private void RunPredictionConditional(int windowEnd)
        {
            if (windowEnd < _confirmedTick)
            {
                windowEnd = _confirmedTick;
            }

            // Everything above the newest tick already simulated is new and unconditionally
            // needs a step. A misprediction pulls the start earlier, but never below the
            // first predicted tick: anything at or under the confirmed frontier was folded
            // into the confirmed drain above, which leaves the freshly confirmed snapshot
            // at _confirmedTick as the predecessor the prediction window replays from.
            int replayFrom = _currentTick + 1;
            if (_pendingReplayFrom != int.MaxValue)
            {
                int dirty = _pendingReplayFrom;
                if (dirty < _confirmedTick + 1)
                {
                    dirty = _confirmedTick + 1;
                }
                if (dirty < replayFrom)
                {
                    replayFrom = dirty;
                }
            }
            _pendingReplayFrom = int.MaxValue;

            int replayed = 0;
            if (replayFrom <= windowEnd)
            {
                // The predecessor snapshot is valid by construction: replayFrom is the
                // earliest dirty tick, so replayFrom - 1 was not disturbed and its snapshot
                // -- confirmed when it equals _confirmedTick, otherwise a prediction from a
                // prior frame -- still holds.
                RestoreTo(replayFrom - 1);
                for (int tick = replayFrom; tick <= windowEnd; ++tick)
                {
                    // One restore before every step, including steps that were not rolled
                    // back to. PhysX warm-starts its solver from cached contact data, and
                    // moving an actor throws that cache away, so a step that follows a restore
                    // runs cold while a step that follows another step runs warm, and the two
                    // do not agree. Restoring before every step makes every step cold and
                    // removes the asymmetry; under PGS that makes a replayed run bitwise
                    // identical to a run that was never rolled back. The first restore is the
                    // rewind above, so only subsequent steps re-restore. Restoring twice would
                    // be as wrong as not at all: quaternion normalisation is not idempotent and
                    // a second restore shifts the rotation by one ULP.
                    if (tick > replayFrom)
                    {
                        RestoreTo(tick - 1);
                    }

                    StepOnce(tick, true);
                    CaptureInto(tick, false);
                    ++replayed;
                }

                _currentTick = windowEnd;
            }

            LastReplayLength = replayed;
            TotalReplayedTicks += replayed;

            SimLog.Verbose(string.Format(
                "Conditionally replayed {0} tick(s) from tick {1} to {2}", replayed, replayFrom, windowEnd));
        }

        private void RestoreTo(int tick)
        {
            Snapshot snapshot;
            if (!_snapshots.TryGet(tick, out snapshot))
            {
                // The tick fell out of history, which means the peer fell further behind
                // than SnapshotHistory allows. Nothing local can fix that; the session
                // has to resynchronise.
                throw new InvalidOperationException(string.Format(
                    "Cannot rewind to tick {0}; the oldest retained tick is {1}. This peer has fallen further " +
                    "behind than SimConfig.SnapshotHistory ({2} ticks) allows and needs a synchronised rebuild.",
                    tick, _snapshots.OldestTick, _config.SnapshotHistory));
            }

            _world.RestoreState(snapshot.Data, snapshot.Size);
            RestoreManagedFrom(snapshot);
        }

        private void StepOnce(int tick, bool isReplay)
        {
            SimLog.CurrentTick = tick;

            SimInputFrame frame = _inputs.GetOrPredict(tick);

            for (int i = 0; i < _handlers.Count; ++i)
            {
                _handlers[i].OnBeforeStep(_world, tick, frame, isReplay);
            }

            _world.Step();

            for (int i = 0; i < _handlers.Count; ++i)
            {
                _handlers[i].OnAfterStep(_world, tick, isReplay);
            }

            SimLog.CurrentTick = -1;
        }

        private void CaptureInto(int tick, bool confirmed)
        {
            Snapshot slot = _snapshots.BeginWrite(tick);
            ulong hash;
            byte[] buffer = slot.Data;
            int size = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, size, hash);
            CaptureManagedInto(slot);
            slot.IsConfirmed = confirmed;
        }

        /// <summary>
        /// Captures the two managed channels into a slot the physics channel has already
        /// been written to. A no-op that leaves the channels empty when no provider is set.
        /// </summary>
        private void CaptureManagedInto(Snapshot slot)
        {
            if (_stateProvider == null)
            {
                slot.EntitySize = 0;
                slot.EntityHash = 0;
                slot.GameSize = 0;
                slot.GameHash = 0;
                return;
            }

            SimStateWriter entityWriter = new SimStateWriter(slot.EntityData);
            _stateProvider.CaptureEntityState(ref entityWriter);
            slot.EntityData = entityWriter.Buffer;
            slot.EntitySize = entityWriter.Position;
            slot.EntityHash = entityWriter.Hash;

            SimStateWriter gameWriter = new SimStateWriter(slot.GameData);
            _stateProvider.CaptureGameState(ref gameWriter);
            slot.GameData = gameWriter.Buffer;
            slot.GameSize = gameWriter.Position;
            slot.GameHash = gameWriter.Hash;
        }

        /// <summary>
        /// Restores the two managed channels from a slot. A no-op when no provider is set.
        /// </summary>
        private void RestoreManagedFrom(Snapshot slot)
        {
            if (_stateProvider == null)
            {
                return;
            }

            SimStateReader entityReader = new SimStateReader(slot.EntityData, slot.EntitySize);
            _stateProvider.RestoreEntityState(ref entityReader);

            SimStateReader gameReader = new SimStateReader(slot.GameData, slot.GameSize);
            _stateProvider.RestoreGameState(ref gameReader);
        }

        /// <summary>
        /// Returns the confirmed snapshot for a tick, for hashing against other peers or
        /// for handing to a joining peer.
        /// </summary>
        /// <returns>False when the tick is not retained or is not yet confirmed.</returns>
        public bool TryGetConfirmedSnapshot(int tick, out Snapshot snapshot)
        {
            if (!_snapshots.TryGet(tick, out snapshot))
            {
                return false;
            }
            if (!snapshot.IsConfirmed)
            {
                snapshot = null;
                return false;
            }
            return true;
        }

        /// <summary>The sorted player-ID set this engine currently runs with.</summary>
        public uint[] CopyPlayerIds()
        {
            return _inputs.CopyPlayerIds();
        }

        /// <summary>
        /// Packages a confirmed tick's full state — every channel plus the current roster —
        /// into a <see cref="SimRebuildState"/> for a synchronised rebuild.
        /// </summary>
        /// <remarks>
        /// Called on the peer that owns the timeline (the host) to produce the agreed state
        /// every other peer, including a mid-match joiner, restores through
        /// <see cref="PrepareForRebuild(ref SimRebuildState, bool)"/>. The returned state owns
        /// its buffers (they are copied out of the ring), so it is safe to serialise and send
        /// after later ticks have recycled the slot.
        /// </remarks>
        /// <param name="tick">The confirmed tick to capture. Usually <see cref="ConfirmedTick"/>.</param>
        /// <param name="state">The captured state, valid only when this returns true.</param>
        /// <returns>False when the tick is not retained or not yet confirmed.</returns>
        public bool CaptureRebuildState(int tick, out SimRebuildState state)
        {
            Snapshot snapshot;
            if (!TryGetConfirmedSnapshot(tick, out snapshot))
            {
                state = new SimRebuildState();
                return false;
            }

            SimRebuildState raw = new SimRebuildState();
            raw.ResumeTick = tick;
            raw.PlayerIds = _inputs.CopyPlayerIds();
            raw.PhysicsData = snapshot.Data;
            raw.PhysicsSize = snapshot.Size;
            raw.PhysicsHash = snapshot.Hash;
            raw.EntityData = snapshot.EntityData;
            raw.EntitySize = snapshot.EntitySize;
            raw.GameData = snapshot.GameData;
            raw.GameSize = snapshot.GameSize;

            // The ring reuses these buffers, so hand back copies that own their bytes.
            state = raw.Compact();
            return true;
        }

        /// <summary>
        /// Restores an agreed <see cref="SimRebuildState"/> on this peer: rebuilds the native
        /// world, restores every channel from the supplied bytes rather than from local
        /// history, replaces the roster when it changed, and resumes from the agreed tick.
        /// </summary>
        /// <remarks>
        /// This is the joiner-safe form of <see cref="PrepareForRebuild(int, byte[], int, bool)"/>.
        /// The other overload captures the managed channels from whatever the provider currently
        /// holds, which assumes the caller has already driven its game objects to the agreed
        /// state — impossible for a peer that was not present for the ticks that produced it.
        /// Here the managed channels are restored straight from the payload, so a fresh joiner
        /// that has built the identical static world (same pool, same stable IDs) reaches the
        /// exact entity and game state every existing peer holds, without simulating a tick.
        /// <para>
        /// A changed roster is applied by rebuilding the input buffer for the new player set.
        /// Because the pool is fixed and spawning only toggles an active flag that lives in the
        /// entity channel (see <see cref="Gameplay.SimEntityPool"/>), the physics layout does
        /// not depend on the roster: the new player's avatar is spawned by a deterministic
        /// action after resume, not carried in this snapshot.
        /// </para>
        /// </remarks>
        /// <param name="state">The agreed state, by reference to avoid copying its buffers.</param>
        /// <param name="reconcile">
        /// An optional callback run after the world and roster have been restored but before the
        /// resume snapshot is captured, so the game can bring its entities into line with the new
        /// roster — activate a joiner's avatar, retire a leaver's — in a way that is baked into
        /// the captured tick rather than replayed afterwards. It must be a pure function of the
        /// restored state and the roster (both identical on every peer), so the snapshot it
        /// produces agrees bit-for-bit. Running it again on a peer that received an
        /// already-reconciled snapshot is harmless because it is idempotent: the desired set
        /// already matches.
        /// </param>
        /// <param name="recreateWorld">
        /// When true (the default) the native world is destroyed and rebuilt in stable-ID order
        /// before the snapshot is restored, which is mandatory for a joiner — see the remarks on
        /// <see cref="PrepareForRebuild(int, byte[], int, bool)"/>.
        /// </param>
        public void PrepareForRebuild(ref SimRebuildState state, Action reconcile = null, bool recreateWorld = true)
        {
            if (state.PhysicsData == null)
            {
                throw new ArgumentException("Rebuild state has no physics payload.", "state");
            }
            if (state.ResumeTick < 0)
            {
                throw new ArgumentOutOfRangeException("state", "Tick numbers start at zero.");
            }

            int resumeTick = state.ResumeTick;
            SimLog.Info(string.Format(
                "Synchronised rebuild: resuming at tick {0} from a {1} byte snapshot, {2} player(s){3}",
                resumeTick, state.PhysicsSize, state.PlayerIds == null ? 0 : state.PlayerIds.Length,
                recreateWorld ? ", rebuilding the native world" : ""));

            if (recreateWorld)
            {
                _world.RecreateNativeWorld();
            }
            else
            {
                _world.CommitPending();
            }

            _world.RestoreState(state.PhysicsData, state.PhysicsSize);

            // Restore the managed channels from the payload, not from local history, so a peer
            // that never simulated these ticks still lands on the agreed managed state.
            if (_stateProvider != null)
            {
                SimStateReader entityReader = new SimStateReader(
                    state.EntityData ?? EmptyBytes, state.EntitySize);
                _stateProvider.RestoreEntityState(ref entityReader);

                SimStateReader gameReader = new SimStateReader(
                    state.GameData ?? EmptyBytes, state.GameSize);
                _stateProvider.RestoreGameState(ref gameReader);
            }

            // Replace the roster when it changed. A rebuild is the only safe moment to do this,
            // because it discards the timeline the old input buffer described anyway.
            if (state.PlayerIds != null && !SamePlayerSet(state.PlayerIds))
            {
                _inputs = new InputBuffer(state.PlayerIds, _config.SnapshotHistory);
            }

            // Bring entities into line with the new roster before the tick is captured, so the
            // change rides in the snapshot every peer restores rather than being replayed after.
            if (reconcile != null)
            {
                reconcile();
                _world.CommitPending();
            }

            // Everything before the rebuild describes a timeline that no longer exists.
            _snapshots.Clear();
            _inputs.Reset(resumeTick);

            Snapshot slot = _snapshots.BeginWrite(resumeTick);
            ulong hash;
            byte[] buffer = slot.Data;
            int captured = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, captured, hash);
            CaptureManagedInto(slot);
            slot.IsConfirmed = true;

            _confirmedTick = resumeTick;
            _currentTick = resumeTick;
            _stalled = false;
            LastReplayLength = 0;
            _pendingReplayFrom = int.MaxValue;

            SimLog.Info(string.Format("Rebuild complete; state hash is 0x{0:X16}", hash));
        }

        private static readonly byte[] EmptyBytes = new byte[0];

        private bool SamePlayerSet(uint[] candidate)
        {
            uint[] current = _inputs.CopyPlayerIds();
            if (candidate.Length != current.Length)
            {
                return false;
            }
            uint[] sorted = new uint[candidate.Length];
            Array.Copy(candidate, sorted, candidate.Length);
            Array.Sort(sorted);
            for (int i = 0; i < current.Length; ++i)
            {
                if (sorted[i] != current[i])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Discards the timeline in preparation for a synchronised rebuild.
        /// </summary>
        /// <remarks>
        /// A peer that joins mid-match has no history, and the native suite shows a world
        /// with no history cannot reproduce a running world's trace under any contact
        /// reset mode. What it can do is agree with another world that also started from
        /// the same snapshot: two worlds rebuilt that way were measured bit-identical for
        /// thirty-two ticks with their actors registered in opposite orders.
        /// <para>
        /// So the joiner does not chase the others. Every peer restores the same snapshot
        /// at the same tick, discards its history, and continues from there, which puts
        /// them all back on an identical footing. The same procedure is the recovery path
        /// when a desync is detected.
        /// </para>
        /// </remarks>
        /// <param name="resumeTick">The tick the session resumes from.</param>
        /// <param name="state">The agreed snapshot every peer restores.</param>
        /// <param name="size">How many bytes of <paramref name="state"/> are meaningful.</param>
        /// <param name="recreateWorld">
        /// When true (the default), the native world is destroyed and rebuilt from scratch
        /// before the snapshot is restored, so every peer — a mid-match joiner included —
        /// reaches the identical PhysX internal arrangement. Restoring into a world that has
        /// run a match instead is the known-incorrect path: its internal indices carry the
        /// shape of a history the joiner never saw. Pass false only for a peer that provably
        /// built its world in the same order the snapshot was captured under and has not
        /// churned its registry since, which in practice means never for a real rebuild.
        /// </param>
        public void PrepareForRebuild(int resumeTick, byte[] state, int size, bool recreateWorld = true)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }
            if (resumeTick < 0)
            {
                throw new ArgumentOutOfRangeException("resumeTick", "Tick numbers start at zero.");
            }

            SimLog.Info(string.Format("Synchronised rebuild: resuming at tick {0} from a {1} byte snapshot{2}",
                resumeTick, size, recreateWorld ? ", rebuilding the native world" : ""));

            if (recreateWorld)
            {
                // Rebuild from nothing so the internal arrangement is the one every peer
                // reaches independently, then capture from the fresh world below.
                _world.RecreateNativeWorld();
            }
            else
            {
                _world.CommitPending();
            }
            _world.RestoreState(state, size);

            // Everything before the rebuild describes a timeline that no longer exists.
            _snapshots.Clear();
            _inputs.Reset(resumeTick);

            Snapshot slot = _snapshots.BeginWrite(resumeTick);
            ulong hash;
            byte[] buffer = slot.Data;
            int captured = _world.CaptureState(ref buffer, out hash);
            slot.Data = buffer;
            _snapshots.CompleteWrite(slot, captured, hash);
            // The managed channels are captured from whatever the provider currently holds,
            // so the game layer must have applied the agreed managed state to its objects
            // before calling this, exactly as it applies the agreed physics snapshot.
            CaptureManagedInto(slot);
            slot.IsConfirmed = true;

            _confirmedTick = resumeTick;
            _currentTick = resumeTick;
            _stalled = false;
            LastReplayLength = 0;
            _pendingReplayFrom = int.MaxValue;

            SimLog.Info(string.Format("Rebuild complete; state hash is 0x{0:X16}", hash));
        }
    }
}
